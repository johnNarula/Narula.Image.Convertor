using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class DirectoriesTests
{
    [Fact]
    public void Creates_a_nested_path_in_one_call()
    {
        using TestWorkspace workspace = new();
        string nested = Path.Combine(workspace.Root, "a", "b", "Converted to jpg");

        Directories.EnsureExists(nested);

        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void Is_a_no_op_when_the_folder_already_exists()
    {
        using TestWorkspace workspace = new();

        Directories.EnsureExists(workspace.Source);
        Directories.EnsureExists(workspace.Source);

        Assert.True(Directory.Exists(workspace.Source));
    }

    [Fact]
    public void Survives_many_workers_creating_the_same_folder_at_once()
    {
        using TestWorkspace workspace = new();
        string shared = Path.Combine(workspace.Root, "contended", "deep");

        Parallel.For(0, 64, _ => Directories.EnsureExists(shared));

        Assert.True(Directory.Exists(shared));
    }

    [Fact]
    public void Still_throws_when_a_file_occupies_the_name()
    {
        using TestWorkspace workspace = new();
        string blocked = Path.Combine(workspace.Source, "in-the-way");
        File.WriteAllText(blocked, "not a folder");

        Assert.Throws<IOException>(() => Directories.EnsureExists(blocked));
    }

    [Fact]
    public async Task A_blocked_destination_is_reported_clearly_and_exits_two()
    {
        using TestWorkspace workspace = new();
        workspace.WritePng("photo.png");

        // A file sitting where the destination folder needs to be.
        string blocked = Path.Combine(workspace.Root, "blocked");
        File.WriteAllText(blocked, "not a folder");

        StringWriter captured = new();
        TextWriter original = Console.Error;
        Console.SetError(captured);

        int exitCode;
        try
        {
            exitCode = await Application.RunAsync(["-s", workspace.Source, "-d", blocked, "-t", "jpg"]);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal(2, exitCode);
        Assert.Contains("could not create destination folder", captured.ToString());
    }
}
