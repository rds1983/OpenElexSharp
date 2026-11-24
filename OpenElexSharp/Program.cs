using OpenElexSharp;

LaunchOptions options;
try
{
    options = LaunchOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine("Usage: OpenElexSharp [--smoke-test]");
    return 1;
}

Console.WriteLine(options.SmokeTest
    ? "OpenElexSharp starting (smoke test)."
    : "OpenElexSharp starting.");

using (var game = new OpenElexGame(options))
{
    game.Run();
}

Console.WriteLine("OpenElexSharp exited.");
return 0;
