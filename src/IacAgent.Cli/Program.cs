using IacAgent.Cli;

try
{
    return await Application.RunAsync(args).ConfigureAwait(false);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return ExitCodes.Unexpected;
}
