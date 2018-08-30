using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CommandLineParser.Arguments;

namespace ArcFileOpener
{
    internal static class Program
    {
        private static readonly CommandLineParser.CommandLineParser Parser = new CommandLineParser.CommandLineParser();

        private static readonly EnumeratedValueArgument<string> Action = new EnumeratedValueArgument<string>('a', "action",
            "REQUIRED: The action to perform. Can be one of: 'list', 'extract', or 'create'", new[] {"list", "extract", "create"});

        private static readonly ValueArgument<string> OutputPath = new ValueArgument<string>('o', "output-location", "Specify an output location. In extract mode, this sets the root directory (the default is the current directory) to extract to, and in create mode this sets the name of the created ARC file (the default is 'Pack.arc')");
        private static readonly ValueArgument<string> FilenameFilters = new ValueArgument<string>('n', "only-named", "Ignore any files (inside the archives) with names that are not in the given (comma-seperated) list");
        private static readonly SwitchArgument Flatfile = new SwitchArgument('f', "flatfile", "Do not create subdirectories for individual arc files", false);
        public static readonly SwitchArgument Verbose = new SwitchArgument('v', "verbose", "Print some extra debugging info", false);
        private static readonly SwitchArgument IncludeAll = new SwitchArgument('i', "include-all", "Include every ARC file in the current directory (or EVERY file (including other ARC files, and files in subdirectories), in create mode). Overrides any provided files", false);

        public static void Main(string[] args)
        {
            Action.Optional = false;
            Action.ValueOptional = false;

            Parser.Arguments.Add(Action);
            Parser.Arguments.Add(OutputPath);
            Parser.Arguments.Add(Verbose);
            Parser.Arguments.Add(Flatfile);
            Parser.Arguments.Add(FilenameFilters);
            Parser.Arguments.Add(IncludeAll);

            Console.WriteLine("ArcIO Version 1.1\r\nCopyright (c) Sam Byass 2018\r\n");

            try
            {
                Parser.ParseCommandLine(args);
            }
            catch (Exception)
            {
                Parser.PrintUsage(Console.Error);
                return;
            }

            string outputRoot = OutputPath.Parsed ? OutputPath.Value : ".";

            if(Flatfile.Value && OutputPath.Parsed) Directory.Delete(outputRoot, true);

            string[] filenameFilters = FilenameFilters.Parsed ? FilenameFilters.Value.Split(',') : new string[0];

            if (Parser.AdditionalArgumentsSettings.AdditionalArguments.Length == 0 && !IncludeAll.Value)
            {
                Console.WriteLine("[Fatal] No input files specified!");
            }

            if (IncludeAll.Value)
            {
                Parser.AdditionalArgumentsSettings.AdditionalArguments = Action.Value != "create"
                    ? new List<string>(Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.arc", SearchOption.TopDirectoryOnly)).ToArray()
                    : new List<string>(Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*", SearchOption.AllDirectories)).ToArray();
            }

            foreach (string inputFilename in Parser.AdditionalArgumentsSettings.AdditionalArguments)
            {
                ArcFile archive = null;
                if (Action.Value != "create")
                {
                    if (Verbose.Value) Console.WriteLine("[Verb] Opening File: " + inputFilename);
                    archive = new ArcFile(inputFilename);

                    if(!archive.IsValidArcFile) continue;
                }

                string outputDir = Flatfile.Value ? outputRoot : Path.Combine(outputRoot, inputFilename.Replace(".arc", ""));

                switch (Action.Value)
                {
                    case "extract":
                        Console.WriteLine("[Info] Extracting " + inputFilename + " => " + outputDir);
                        if (Directory.Exists(outputDir) && !Flatfile.Value)
                        {
                            Console.WriteLine("[Info] Deleting Existing output folder...");
                            Directory.Delete(outputDir, true);
                            Thread.Sleep(500);
                        }

                        Directory.CreateDirectory(outputDir);

                        if (filenameFilters.Length > 0)
                        {
                            List<ArcFile.ContainedFileMetadata> filtered = archive.Entries.FindAll(entry => filenameFilters.Contains(entry.Filename));
                            archive.Entries.Clear();
                            archive.Entries.AddRange(filtered);
                        }

                        Console.WriteLine();
                        float pos = 0;
                        Console.Write($"Unpacking ARChive... File {pos}/{archive.Entries.Count} ({pos / archive.Entries.Count}%): <Initialising...>");

                        foreach (ArcFile.ContainedFileMetadata fileMetadata in archive.Entries)
                        {
                            Console.CursorLeft = 0;
                            pos++;
                            Console.Write($"Unpacking ARChive... File {pos}/{archive.Entries.Count} ({Math.Round(pos / archive.Entries.Count * 100f, 2)}%): {archive.Entries[(int) (pos-1)].Filename}\t  ");

                            if (Verbose.Value) Console.WriteLine("[Verb] Extracting " + fileMetadata.Filename + "...");

                            string outputFilePath = archive.ExtractFile((int) pos - 1, outputDir);

                            //Check if we need to unpnap this file
                            PnapFile file = new PnapFile(outputFilePath);
                            if (file.IsPNAPFile)
                            {
                                file.WriteLayerInfo(Path.Combine(Path.GetDirectoryName(outputFilePath), Path.GetFileNameWithoutExtension(outputFilePath) + "_layers.txt"));
                                file.UnpackFiles();
                            }
                        }

                        Console.WriteLine("\n");

                        break;
                    case "list":
                        uint totalSize = 0;
                        Console.WriteLine(archive.FileLocation + ":");
                        foreach (ArcFile.ContainedFileMetadata fileMetadata in archive.Entries)
                        {
                            if (filenameFilters.Length > 0 && !filenameFilters.Contains(fileMetadata.Filename))
                            {
                                if (Verbose.Value) Console.WriteLine("[Verb] Ignoring file " + fileMetadata.Filename + " as it's not in the filename list.");
                                continue;
                            }

                            Console.WriteLine($"\t-{fileMetadata.Filename} ({fileMetadata.Length} bytes)");
                            totalSize += fileMetadata.Length;
                        }

                        Console.WriteLine($"{archive.Entries.Count} files ({totalSize} bytes)\n");
                        break;
                    case "create":
                        List<string> files = new List<string>();
                        foreach (string arg in Parser.AdditionalArgumentsSettings.AdditionalArguments)
                        {
                            if (Directory.Exists(arg))
                                files.AddRange(Directory.EnumerateFiles(arg, "*", SearchOption.AllDirectories));
                            else if (File.Exists(arg))
                                files.Add(arg);
                        }

                        uint numEntries = (uint) files.Count;
                        string outputFile = "Pack.arc";
                        if (OutputPath.Parsed)
                            outputFile = OutputPath.Value + (OutputPath.Value.EndsWith(".arc") ? "" : ".arc");

                        Console.WriteLine("[Info] Creating file " + outputFile + " from " + numEntries + " files...");

                        uint currentOffset = 0;

                        using(MemoryStream body = new MemoryStream())
                        using (MemoryStream filetable = new MemoryStream())
                        {
                            Console.WriteLine("[Info] Writing filetable and body streams...");
                            foreach (string file in files)
                            {
                                if (Verbose.Value) Console.WriteLine("[Verb] Reading file: " + file);
                                byte[] buffer = File.ReadAllBytes(file);
                                if (Verbose.Value) Console.WriteLine("[Verb] Read " + buffer.Length + " bytes. Writing file size to filetable...");
                                StreamUtils.WriteUInt32(filetable, (uint) buffer.Length);
                                if (Verbose.Value) Console.WriteLine("[Verb] Writing current offset (" + currentOffset + ") to filetable...");
                                StreamUtils.WriteUInt32(filetable, currentOffset);

                                if (Verbose.Value) Console.WriteLine("[Verb] Getting bytes for filename...");
                                List<byte> filenameBytes = new List<byte>();
                                foreach (char c in Path.GetFileName(file))
                                {
                                    byte[] twoBytes = BitConverter.GetBytes(c);
                                    filenameBytes.AddRange(twoBytes);
                                }

                                filenameBytes.Add(0); //Write the null byte as the filename terminator
                                filenameBytes.Add(0);

                                if (Verbose.Value) Console.WriteLine("[Verb] Filename is " + filenameBytes.Count + " bytes. Writing to filetable...");
                                filetable.Write(filenameBytes.ToArray(), 0, filenameBytes.Count);

                                if (Verbose.Value) Console.WriteLine("[Verb] Writing " + buffer.Length + " bytes to body...");
                                body.Write(buffer, 0, buffer.Length);
                                currentOffset += (uint) buffer.Length;
                                if (Verbose.Value) Console.WriteLine("[Verb] File written.\n");
                            }

                            var filetableSize = (uint) filetable.Length;

                            Console.WriteLine("[Info] Writing final file...");
                            using (FileStream outStream = File.Create(outputFile))
                            {
                                if (Verbose.Value) Console.WriteLine("[Verb] Writing entry number (" + numEntries + ") to header...");
                                StreamUtils.WriteUInt32(outStream, numEntries);
                                if (Verbose.Value) Console.WriteLine("[Verb] Writing filetable size (" + filetableSize + ") to header...");
                                StreamUtils.WriteUInt32(outStream, filetableSize);

                                filetable.Seek(0, SeekOrigin.Begin);
                                body.Seek(0, SeekOrigin.Begin);
                                if (Verbose.Value) Console.WriteLine("[Verb] Writing filetable...");
                                filetable.CopyTo(outStream);
                                if (Verbose.Value) Console.WriteLine("[Verb] Writing body...");
                                body.CopyTo(outStream);
                            }

                            Console.WriteLine("Done!");
                        }
                        break;
                    default:
                        Console.WriteLine("Unknown action " + Action.Value + ". While you're at it, how did you even get to this point?");
                        break;
                }
            }
        }
    }
}