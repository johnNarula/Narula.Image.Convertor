namespace Narula.Image.Convertor;

internal static class Directories
{
    private const int Attempts = 4;

    /// <summary>
    /// Creates a directory, retrying briefly on I/O errors.
    /// </summary>
    /// <remarks>
    /// Cloud-backed folders — OneDrive, Dropbox — are reparse points serviced by a filter driver.
    /// The first write into one whose placeholder has not been hydrated can fail spuriously, and
    /// OneDrive reports it as ERROR_FILE_NOT_FOUND, which .NET surfaces as a FileNotFoundException
    /// naming the folder you were trying to create. Retrying costs nothing when there is no
    /// contention, and a genuine permission problem still throws on the last attempt.
    /// </remarks>
    public static void EnsureExists(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(path);
                return;
            }
            catch (IOException) when (attempt < Attempts)
            {
                // Another process (or the sync driver) may have won the race meanwhile.
                if (Directory.Exists(path))
                {
                    return;
                }

                Thread.Sleep(50 * attempt);
            }
        }
    }
}
