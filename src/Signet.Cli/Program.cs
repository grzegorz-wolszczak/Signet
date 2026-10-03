using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

// Exit codes: 0 = OK, 1 = general error, 2 = the EPUB cannot be loaded/saved,
// >2 = argument parsing error (from System.CommandLine).
return BuildRootCommand().Parse(args).Invoke();

static RootCommand BuildRootCommand()
{
    RootCommand root = new(ApplicationInfo.NameWithVersion + " — headless CLI (import/round-trip EPUB).");

    Argument<FileInfo> openInput = new("input")
    {
        Description = "The .epub file to load.",
    };
    Command open = new("open", "Loads an EPUB into the in-memory model and prints a summary (version, resources, warnings).")
    {
        openInput,
    };
    open.SetAction(parseResult => RunOpen(parseResult.GetValue(openInput)!));
    root.Subcommands.Add(open);

    Argument<FileInfo> rtInput = new("input")
    {
        Description = "Source .epub file.",
    };
    Argument<FileInfo> rtOutput = new("output")
    {
        Description = "Target .epub file (will be overwritten).",
    };
    Command roundtrip = new("roundtrip", "Loads an EPUB and writes it back (import -> Book -> export).")
    {
        rtInput,
        rtOutput,
    };
    roundtrip.SetAction(parseResult => RunRoundtrip(parseResult.GetValue(rtInput)!, parseResult.GetValue(rtOutput)!));
    root.Subcommands.Add(roundtrip);

    return root;
}

static int RunOpen(FileInfo input)
{
    try
    {
        using Book book = new ImportEpub(input.FullName).GetBook();
        PrintBookSummary(book);
        return 0;
    }
    catch (EpubLoadException ex)
    {
        Console.Error.WriteLine($"Cannot load EPUB: {ex.Message}");
        return 2;
    }
}

static int RunRoundtrip(FileInfo input, FileInfo output)
{
    try
    {
        using Book book = new ImportEpub(input.FullName).GetBook();
        PrintBookSummary(book);

        new ExportEpub(book).WriteBook(output.FullName);
        long size = new FileInfo(output.FullName).Length;
        Console.WriteLine($"Saved: {output.FullName} ({size} B)");
        return 0;
    }
    catch (EpubLoadException ex)
    {
        Console.Error.WriteLine($"Cannot load EPUB: {ex.Message}");
        return 2;
    }
    catch (EpubExportException ex)
    {
        Console.Error.WriteLine($"Cannot save EPUB: {ex.Message}");
        return 2;
    }
}

static void PrintBookSummary(Book book)
{
    Console.WriteLine($"EPUB version: {book.EpubVersion}");
    Console.WriteLine($"Resources: {book.GetAllResources().Count} (HTML in spine: {book.GetHtmlResources().Count})");
    Console.WriteLine($"NCX: {(book.GetNcx() is null ? "missing" : "present")}");

    int fonts = book.GetAllResources().OfType<FontResource>().Count(f => !string.IsNullOrEmpty(f.ObfuscationAlgorithm));
    if (fonts > 0)
    {
        Console.WriteLine($"Obfuscated fonts: {fonts}");
    }

    if (book.LoadWarnings.Count > 0)
    {
        Console.WriteLine($"Warnings ({book.LoadWarnings.Count}):");
        foreach (string warning in book.LoadWarnings)
        {
            Console.WriteLine($"  - {warning}");
        }
    }
}
