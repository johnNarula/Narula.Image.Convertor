using Narula.Image.Convertor;

// No arguments means someone double-clicked this or typed the bare name, so show the window.
// Explicit -h still prints help, leaving scripts that ask for usage unaffected. The decision
// lives here rather than in Application so that running the library never spawns a process.
if (args.Length == 0 && UiLauncher.TryLaunch(Console.Error))
{
    return 0;
}

using CancellationTokenSource cancellation = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await Application.RunAsync(args, cancellation.Token);
