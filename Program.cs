
namespace DFind;

/// <summary>
/// Checks for command line arguments and configures the DuplicateFinder instance accordingly
/// supported flags:
/// <list>
/// <item><c>-r</c> recursively search subfolders to find duplicates</item>
/// <item><c>-f</c> nothing...</item>
/// </list>
/// </summary>
public static class Flags
{
    public static void Parse(string cmd, DuplicateFinder df)
    {
        switch (cmd.ToLower())
        {
            case "-r":
                df.Recursive = System.IO.SearchOption.AllDirectories;
                return;
            case "-jpg":
                df.Pattern.Add(".jpg");
                return;
            case "-png":
                df.Pattern.Add(".png");
                return;
            case "-gif":
                df.Pattern.Add(".gif");
                return;
            case "-v":
                df.Verbal = true;
                return;
        }
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        if (!(args.Length > 0))
        {
            Console.Error.WriteLine("error - need a directory path to check");
            Environment.Exit(1);
        }

        string path = args.Last();
        if (!Path.Exists(path))
        {
            Console.Error.WriteLine("error - location doesn't exist");
            Environment.Exit(1);
        }

        DuplicateFinder df = new(path);

        var cmds = args.Where(x => x.StartsWith('-'));
        foreach (string? cmd in cmds)
        {
            Flags.Parse(cmd, df);
        }

        df.Process();
    }
}
