using System.Globalization;
using System.Text.Json;

namespace IacAgent.Host.Benchmark;

internal sealed record BenchmarkModel(string Strong, string? Cheap);

internal sealed record BenchmarkTask(string Id, string Prompt, decimal? Budget, IReadOnlyList<IReadOnlyList<string>>? Expect);

internal sealed record BenchmarkSuite(
    string Name,
    int Repeat,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<BenchmarkModel> Models,
    IReadOnlyList<string> Temperatures,
    IReadOnlyList<string> Security,
    IReadOnlyList<int?> MaxRetries,
    int RunTimeoutMinutes,
    IReadOnlyList<BenchmarkTask> Tasks)
{
    public static BenchmarkSuite Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The suite file is not JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The suite file must be a JSON object.");
            }

            return new BenchmarkSuite(
                ReadName(document.RootElement),
                ReadRepeat(document.RootElement),
                ReadStrings(document.RootElement, "profiles", "A suite needs at least one profile."),
                ReadModels(document.RootElement),
                ReadTemperatures(document.RootElement),
                ReadSecurity(document.RootElement),
                ReadMaxRetries(document.RootElement),
                ReadTimeout(document.RootElement),
                ReadTasks(document.RootElement));
        }
    }

    private static string ReadName(JsonElement root)
    {
        var name = ReadString(root, "name");
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            throw new InvalidOperationException("The suite needs a name that can be a folder.");
        }

        return name;
    }

    private static int ReadRepeat(JsonElement root)
    {
        if (!root.TryGetProperty("repeat", out var property) || !property.TryGetInt32(out var repeat) || repeat < 1)
        {
            throw new InvalidOperationException("repeat must be at least 1.");
        }

        return repeat;
    }

    private static IReadOnlyList<BenchmarkModel> ReadModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var property) || property.ValueKind != JsonValueKind.Array || property.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("A suite needs at least one model.");
        }

        var models = new List<BenchmarkModel>();
        foreach (var item in property.EnumerateArray())
        {
            var strong = ReadString(item, "strong");
            if (string.IsNullOrWhiteSpace(strong))
            {
                throw new InvalidOperationException("Each model needs a strong id.");
            }

            var cheap = ReadString(item, "cheap");
            models.Add(new BenchmarkModel(strong, string.IsNullOrWhiteSpace(cheap) ? null : cheap));
        }

        return models;
    }

    private static IReadOnlyList<string> ReadTemperatures(JsonElement root)
    {
        if (!root.TryGetProperty("temperatures", out var property) || property.ValueKind != JsonValueKind.Array || property.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("A suite needs at least one temperature.");
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDecimal(out var temperature) || temperature is < 0 or > 2)
            {
                throw new InvalidOperationException("temperature must be a number from 0 to 2.");
            }

            values.Add(temperature.ToString("0.################", CultureInfo.InvariantCulture));
        }

        return values;
    }

    private static IReadOnlyList<string> ReadSecurity(JsonElement root)
    {
        var values = ReadStrings(root, "security", "A suite needs at least one security level.");
        foreach (var value in values)
        {
            if (!value.Equals("baseline", StringComparison.OrdinalIgnoreCase) && !value.Equals("strict", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("security must be baseline or strict.");
            }
        }

        return values.Select(value => value.ToLowerInvariant()).ToList();
    }

    private static IReadOnlyList<int?> ReadMaxRetries(JsonElement root)
    {
        if (!root.TryGetProperty("maxRetries", out var property))
        {
            return [null];
        }

        if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("maxRetries must be a list of zero or greater.");
        }

        var values = new List<int?>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var retries) || retries < 0)
            {
                throw new InvalidOperationException("maxRetries must be a list of zero or greater.");
            }

            values.Add(retries);
        }

        return values;
    }

    private static int ReadTimeout(JsonElement root)
    {
        if (!root.TryGetProperty("runTimeoutMinutes", out var property))
        {
            return 15;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var minutes) || minutes < 0)
        {
            throw new InvalidOperationException("runTimeoutMinutes must be zero or greater.");
        }

        return minutes;
    }

    private static IReadOnlyList<BenchmarkTask> ReadTasks(JsonElement root)
    {
        if (!root.TryGetProperty("tasks", out var property) || property.ValueKind != JsonValueKind.Array || property.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("A suite needs at least one task.");
        }

        var tasks = new List<BenchmarkTask>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var prompt = ReadString(item, "prompt");
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['/', '\\', ':']) >= 0 || string.IsNullOrWhiteSpace(prompt))
            {
                throw new InvalidOperationException("Each task needs an id that can be a folder, and a prompt.");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException($"Task id '{id}' is used more than once.");
            }

            decimal? budget = null;
            if (item.TryGetProperty("budget", out var budgetProperty))
            {
                if (budgetProperty.ValueKind != JsonValueKind.Number || !budgetProperty.TryGetDecimal(out var parsed) || parsed < 0)
                {
                    throw new InvalidOperationException($"Task '{id}' has a budget that is not zero or greater.");
                }

                budget = parsed;
            }

            tasks.Add(new BenchmarkTask(id, prompt, budget, ReadExpect(item, id)));
        }

        return tasks;
    }

    private static IReadOnlyList<IReadOnlyList<string>>? ReadExpect(JsonElement task, string id)
    {
        if (!task.TryGetProperty("expect", out var expect))
        {
            return null;
        }

        if (expect.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Task '{id}' expect must be a list of groups.");
        }

        if (expect.GetArrayLength() == 0)
        {
            return null;
        }

        var groups = new List<IReadOnlyList<string>>();
        foreach (var group in expect.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array || group.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"Task '{id}' has an empty expect group.");
            }

            var types = new List<string>();
            foreach (var type in group.EnumerateArray())
            {
                var text = type.ValueKind == JsonValueKind.String ? type.GetString() : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException($"Task '{id}' has an empty resource type in expect.");
                }

                types.Add(text);
            }

            groups.Add(types);
        }

        return groups;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name, string error)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array || property.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(error);
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(error);
            }

            values.Add(text);
        }

        return values;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
