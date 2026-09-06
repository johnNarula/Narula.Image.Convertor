namespace Narula.Image.Convertor;

internal static class Directories
{
    private const int Attempts = 3;
    private const int FirstDelayMs = 25;

    /// <summary>
    /// Creates a directory, retrying briefly on I/O errors.
    /// </summary>
    /// <remarks>
    /// The retry covers genuine short-lived races — a sibling worker creating the same parent, or
    /// a directory entry still being torn down after a delete. It is deliberately small: a
    /// blocked write does not become unblocked by waiting, and stalling for seconds before
    /// reporting that would only make the failure harder to read.
    ///
    /// Note for whoever hits this next: on Windows, a create refused by Defender's Controlled
    /// Folder Access surfaces as a <see cref="FileNotFoundException"/> naming the folder you asked
    /// it to create, which reads like a bug in the caller. <see cref="Application"/> turns that
    /// into an actionable message rather than passing the raw text through.
    /// </remarks>
    public static void EnsureExists(string path)
    {
        int delay = FirstDelayMs;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(path);
                return;
            }
            catch (Exception exception) when
                (exception is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                Thread.Sleep(delay);
                delay *= 2;
            }
        }
    }
}
