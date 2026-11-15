using System.Globalization;
using System.Text.Json;

using IacAgent.Host.Configuration;
using IacAgent.Host.Recording;

namespace IacAgent.Host.Benchmark;

/// <summary>Runs every suite combination, one after another, and writes results.csv.</summary>
public static class BenchmarkCommand
{
    /// <summary>Runs the suite. Returns the folder that contains results.csv.</summary>
    public static async Task<string> RunAsync(
        string suiteJson,
        string? resumeDirectory,
        string? endpoint,
        int? maxRepairIterations,
        int? maxTotalTokens,
        bool verbose,
        string toolVersion,
        Func<GenerateRequest, decimal?, CancellationToken, Task> run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suiteJson);
        ArgumentNullException.ThrowIfNull(run);
        var suite = BenchmarkSuite.Load(suiteJson);
        var root = ResolveRoot(suite.Name, resumeDirectory);
        Directory.CreateDirectory(root);
        var rows = new List<IReadOnlyList<string>>();
        var csvPath = Path.Combine(root, "results.csv");
        foreach (var combination in Combinations(suite, root, endpoint, maxRepairIterations, maxTotalTokens, verbose, toolVersion))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(await RunCombinationAsync(combination, run, cancellationToken).ConfigureAwait(false));
            await File.WriteAllTextAsync(csvPath, BenchmarkCsv.Format(rows), cancellationToken).ConfigureAwait(false);
        }

        return root;
    }

    private static string ResolveRoot(string suiteName, string? resumeDirectory)
    {
        if (string.IsNullOrWhiteSpace(resumeDirectory))
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            return Path.GetFullPath(Path.Combine("bench", suiteName, stamp));
        }

        if (!Directory.Exists(resumeDirectory))
        {
            throw new InvalidOperationException("The resume folder does not exist.");
        }

        return Path.GetFullPath(resumeDirectory);
    }

    private static IEnumerable<Combination> Combinations(
        BenchmarkSuite suite,
        string root,
        string? endpoint,
        int? maxRepairIterations,
        int? maxTotalTokens,
        bool verbose,
        string toolVersion)
    {
        foreach (var task in suite.Tasks)
        {
            foreach (var profile in suite.Profiles)
            {
                foreach (var model in suite.Models)
                {
                    foreach (var temperature in suite.Temperatures)
                    {
                        foreach (var security in suite.Security)
                        {
                            for (var repeat = 1; repeat <= suite.Repeat; repeat++)
                            {
                                yield return new Combination(task, profile, model, temperature, security, repeat, root, endpoint, maxRepairIterations, maxTotalTokens, verbose, toolVersion);
                            }
                        }
                    }
                }
            }
        }
    }

    private static async Task<IReadOnlyList<string>> RunCombinationAsync(
        Combination combination,
        Func<GenerateRequest, decimal?, CancellationToken, Task> run,
        CancellationToken cancellationToken)
    {
        var runJson = Path.Combine(combination.Directory, "run.json");
        if (!File.Exists(runJson))
        {
            Directory.CreateDirectory(combination.Directory);
            try
            {
                await run(combination.Request(), combination.Task.Budget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The pipeline writes run.json and rethrows. The suite keeps going.
            }
        }

        return File.Exists(runJson) ? ReadRow(combination, runJson) : combination.Row("Error", 0, 0, 0, string.Empty, 0, 0, 0, 0);
    }

    private static IReadOnlyList<string> ReadRow(Combination combination, string runJson)
    {
        try
        {
            var document = JsonSerializer.Deserialize(File.ReadAllText(runJson), RunJsonContext.Default.RunDocument);
            if (document is null)
            {
                return combination.Row("Error", 0, 0, 0, string.Empty, 0, 0, 0, 0);
            }

            var last = document.Iterations.Count == 0 ? null : document.Iterations[^1];
            return combination.Row(
                document.Outcome,
                document.Iterations.Count,
                document.Tokens.Input,
                document.Tokens.Output,
                document.CostUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                document.DurationMs,
                document.Resources.Count,
                last?.BlockingIssues ?? 0,
                last?.AdvisoryIssues ?? 0);
        }
        catch (JsonException)
        {
            return combination.Row("Error", 0, 0, 0, string.Empty, 0, 0, 0, 0);
        }
    }

    private sealed record Combination(
        BenchmarkTask Task,
        string Profile,
        BenchmarkModel Model,
        string Temperature,
        string Security,
        int Repeat,
        string Root,
        string? Endpoint,
        int? MaxRepairIterations,
        int? MaxTotalTokens,
        bool Verbose,
        string ToolVersion)
    {
        public string Directory => Path.Combine(Root, Task.Id, Profile, ModelFolder(), Temperature, Security, Repeat.ToString(CultureInfo.InvariantCulture));

        public GenerateRequest Request()
        {
            return new GenerateRequest(
                Task.Prompt,
                Profile,
                Directory,
                Model.Strong,
                Model.Cheap,
                Endpoint,
                MaxRepairIterations,
                MaxTotalTokens,
                true,
                Verbose,
                Security,
                ToolVersion,
                Temperature);
        }

        public IReadOnlyList<string> Row(
            string outcome,
            int iterations,
            int inputTokens,
            int outputTokens,
            string cost,
            long duration,
            int resources,
            int blocking,
            int advisory)
        {
            var cheap = string.IsNullOrWhiteSpace(Model.Cheap) ? Model.Strong : Model.Cheap;
            return
            [
                Task.Id,
                Profile,
                Model.Strong,
                cheap,
                Temperature,
                Security,
                Repeat.ToString(CultureInfo.InvariantCulture),
                outcome,
                iterations.ToString(CultureInfo.InvariantCulture),
                inputTokens.ToString(CultureInfo.InvariantCulture),
                outputTokens.ToString(CultureInfo.InvariantCulture),
                cost,
                duration.ToString(CultureInfo.InvariantCulture),
                resources.ToString(CultureInfo.InvariantCulture),
                blocking.ToString(CultureInfo.InvariantCulture),
                advisory.ToString(CultureInfo.InvariantCulture),
            ];
        }

        private string ModelFolder()
        {
            var strong = Sanitize(Model.Strong);
            if (string.IsNullOrWhiteSpace(Model.Cheap) || Model.Cheap == Model.Strong)
            {
                return strong;
            }

            return strong + "--" + Sanitize(Model.Cheap);
        }

        private static string Sanitize(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '-' : character);
            }

            var text = builder.ToString().Trim();
            return string.IsNullOrEmpty(text) ? "model" : text;
        }
    }
}
