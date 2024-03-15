using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DFind;

public class DuplicateFinder
{
    private SearchOption _recursive = SearchOption.TopDirectoryOnly;

    public List<string> Pattern { get; set; }
    public IEnumerable<IGrouping<long, FileInfo>> FileSizeGroups { get; set; }
    public bool Verbal { get; set; }
    public SearchOption Recursive
    {
        get { return _recursive; }
        set
        {
            _recursive = value;
            //TODO: logging
            //Console.WriteLine($"[setup] recursive mode is {(_recursive == System.IO.SearchOption.AllDirectories ? "ON" : "OFF")}");
        }
    }

    private readonly string _location;

    public DuplicateFinder(string location)
    {
        this._location = location;
        Pattern = [];
        FileSizeGroups = [];

        Debug.Assert(Path.Exists(_location));
    }

    public DuplicateFinder(string location, string[] pattern)
    {
        this._location = location;
        Pattern = [.. pattern];
        FileSizeGroups = [];

        Debug.Assert(Path.Exists(_location));
    }

    private void GetFiles()
    {
        DirectoryInfo dInfo = new(_location);
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = _recursive == SearchOption.AllDirectories,
            BufferSize = 16384,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary

        };
        if (Pattern.Count > 0)
        {
            FileSizeGroups = from x in dInfo.GetFiles("*.*", options)
                             where Pattern.Contains(x.Extension)
                             group x by x.Length into g
                             select g;
        }
        else
        {   
            FileSizeGroups = from x in dInfo.GetFiles("*.*", options)
                             group x by x.Length into g
                             select g;
        }
    }

    private void HandleGroup(IGrouping<long, FileInfo> group, ConcurrentBag<FileInfo> Duplicates)
    {
        //TODO: logging
        //Console.WriteLine($"checking group size: {group.Key}");
        List<FileInfo> Compared = [];
        //List<FileInfo> Duplicates = [];

        foreach (FileInfo A in group.Where(x => !Duplicates.Contains(x)))
        {
            foreach (FileInfo B in group.Where(x => x != A && !Compared.Contains(x)))
            {
                bool result = Compare(A, B);
                if (result == true)
                {
                    //Console.WriteLine($"{B.FullName}");
                    Duplicates.Add(B);
                }
            }
            Compared.Add(A);
        }

    }

    private static bool CompareHash(FileStream A, FileStream B)
    {
        using MD5 md5 = MD5.Create();
        var hashA = md5.ComputeHash(A);
        var hashB = md5.ComputeHash(B);

        if (hashA.Length != hashB.Length)
        {
            return false;
        }
        return hashA.SequenceEqual(hashB);
    }
    private static bool Compare(FileInfo A, FileInfo B)
    {

        using FileStream fsA = File.OpenRead(A.FullName);
        using FileStream fsB = File.OpenRead(B.FullName);

        // if start / end bytes are not the same, it's not a duplicate so return early
        if (!CompareStartEndBytes(fsA, fsB))
        {
            return false;
        }
        if (!CompareHash(fsA, fsB))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// compares the first and last bytes of files A,B
    /// </summary>
    /// <param name="A"></param>
    /// <param name="B"></param>
    /// <returns><see langword="true"/> if the start/end bytes of files A,B are duplicates, otherwise <see langword="false"/> </returns>
    private static bool CompareStartEndBytes(FileStream A, FileStream B)
    {
        Debug.Assert(A.Length == B.Length);

        const int bufsize_16KB = 16384;

        // defaults to 16KB buffer but if the file is smaller than that, 
        // just use the whole file as the buffer
        int bufsize = bufsize_16KB;
        bool skipEndChunks = false;
        if (A.Length < bufsize)
        {
            skipEndChunks = true;
            bufsize = (int)A.Length;
        }

        byte[] bufferA = ArrayPool<byte>.Shared.Rent(bufsize);
        byte[] bufferB = ArrayPool<byte>.Shared.Rent(bufsize);
        try
        {
            A.Read(bufferA, 0, bufsize);
            B.Read(bufferB, 0, bufsize);

            Span<byte> startSpanA = new(bufferA, 0, bufsize);
            Span<byte> startSpanB = new(bufferB, 0, bufsize);
            
            if (!startSpanA.SequenceEqual(startSpanB))
            {
                return false;
            }

            // if start chunks didn't return false and the file is the same size as the buffer,
            // we have read the whole file, so it's a duplicate: return true.
            // no need to check the whole file again
            if (skipEndChunks == true)
            {
                return true;
            }

            A.Position = A.Length - bufsize;
            B.Position = B.Length - bufsize;

            Array.Clear(bufferA);
            Array.Clear(bufferB);

            A.Read(bufferA, 0, bufsize);
            B.Read(bufferB, 0, bufsize);

            Debug.Assert(!startSpanA.IsEmpty);
            Debug.Assert(!startSpanA.IsEmpty);

            if (!startSpanA.SequenceEqual(startSpanB))
            {
                return false;
            }
            return true;
        }

        finally
        {
            ArrayPool<byte>.Shared.Return(bufferA);
            ArrayPool<byte>.Shared.Return(bufferB);
        }
    }

    public void Process()
    {

        GetFiles();
        ConcurrentBag<FileInfo> Duplicates = [];
        Parallel.ForEach(FileSizeGroups, new ParallelOptions { MaxDegreeOfParallelism = 10 }, group =>
        {
            if (group.Skip(1).Any())
            {
                HandleGroup(group, Duplicates);
            }   

        });
        
        foreach (FileInfo duplicate in Duplicates)
        {
            Console.WriteLine($"\"{duplicate.FullName}\"");
        }
    
        if (Verbal)
        {
            Console.WriteLine($"found {Duplicates.Count} duplicates");
        }        
    }
}
