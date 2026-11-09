using IacAgent.Spike;

var command = args.Length == 0 ? "all" : args[0].ToLowerInvariant();
if (command is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

if (command is not ("all" or "models" or "tools" or "samples"))
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var failures = 0;
if (command is "all" or "models")
{
    failures += await ChatProbe.RunAsync(cancellation.Token);
}

if (command is "all" or "tools")
{
    failures += await ToolProbe.RunAsync(cancellation.Token);
}

if (command is "all" or "samples")
{
    failures += SampleProbe.Run();
}

return failures == 0 ? 0 : 1;

static void PrintUsage()
{
    Console.WriteLine("""
        IacAgent Phase 0 spike

        Usage:
          dotnet run -- [all|models|tools|samples]

        Environment:
          OPENROUTER_API_KEY      OpenRouter API key
          IACAGENT_MODEL_STRONG   OpenRouter model id
          OLLAMA_HOST             Ollama base URL (default http://127.0.0.1:11434)
          OLLAMA_MODEL            Ollama model name
          IACAGENT_MODEL_CHEAP    used when OLLAMA_MODEL is unset
        """);
}
