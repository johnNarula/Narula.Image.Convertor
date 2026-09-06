using Narula.Image.Convertor;

// The window is asked for either explicitly with -ui, or implicitly by running this with nothing
// at all, which is what a double-click looks like. Both hand the remaining arguments over so the
// window opens already filled in; it takes what it can use and quietly drops the rest.
//
// Explicit -h still prints help, leaving scripts that ask for usage unaffected. The decision
// lives here rather than in Application so that running the library never spawns a process.
if (UiLauncher.Requested(args, out string[] forWindow))
{
    if (UiLauncher.TryLaunch(Console.Error, forWindow))
    {
        return 0;
    }

    Console.Error.WriteLine("img2img: -ui needs img2imgUI beside this program, and it is not there.");
    return 2;
}

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
