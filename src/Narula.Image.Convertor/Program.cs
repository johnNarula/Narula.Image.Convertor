using Narula.Image.Convertor;

using CancellationTokenSource cancellation = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await Application.RunAsync(args, cancellation.Token);
