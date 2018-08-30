using System;
using System.Collections.Generic;
using System.IO;

namespace ArcFileOpener
{
    public class ArcFile
    {
        public struct ContainedFileMetadata
        {
            public uint Length;
            public uint Offset;
            public string Filename;
        }

        public bool IsValidArcFile = true;
        public readonly string FileLocation;
        private readonly FileStream _stream;
        private readonly uint DataStart;
        private readonly uint ExpectedEntries;
        public readonly List<ContainedFileMetadata> Entries = new List<ContainedFileMetadata>();

        public ArcFile(string fileLocation)
        {
            FileLocation = fileLocation;
            _stream = File.OpenRead(FileLocation);

            ExpectedEntries = StreamUtils.ReadUInt32(_stream);
            DataStart = StreamUtils.ReadUInt32(_stream) + 8;

            if (Program.Verbose.Value)
                Console.WriteLine("[Verb] Header successfully read. Expecting " + ExpectedEntries + " files, file content begins at offset " +
                                  DataStart);

            PopulateFileList();
        }

        private void PopulateFileList()
        {
            Entries.Clear();

            if (Program.Verbose.Value) Console.WriteLine("[Verb] Reading file table...");

            try
            {
                for (uint i = 0; i < ExpectedEntries; i++)
                {
                    if (Program.Verbose.Value) Console.WriteLine("[Verb] \tAttempting to parse info for file #" + (i + 1));
                    uint length = StreamUtils.ReadUInt32(_stream);
                    if (Program.Verbose.Value) Console.WriteLine("[Verb] \t\tFile Size is " + length + " bytes");
                    uint offset = StreamUtils.ReadUInt32(_stream);
                    if (Program.Verbose.Value) Console.WriteLine("[Verb] \t\tFile content starts at offset " + offset);


                    if (Program.Verbose.Value) Console.WriteLine("[Verb] \t\tReading filename...");
                    List<char> filenameChars = new List<char>();
                    byte[] twoBytes = new byte[2];
                    while (true)
                    {
                        _stream.Read(twoBytes, 0, 2);
                        //The second byte "should" be 0
                        if (twoBytes[1] != 0)
                        {
                            Console.WriteLine("[Warning] While reading filename, second byte not 0 at offset " + (_stream.Position - 1));
                        }

                        if (twoBytes[0] == 0)
                        {
                            break;
                        }

                        filenameChars.Add(BitConverter.ToChar(twoBytes, 0));
                    }

                    string filename = new string(filenameChars.ToArray());
                    if (Program.Verbose.Value) Console.WriteLine("[Verb] \t\tFilename: " + filename);

                    Entries.Add(new ContainedFileMetadata
                    {
                        Length = length,
                        Offset = offset,
                        Filename = filename
                    });
                }

                if (Program.Verbose.Value)
                    Console.WriteLine("[Verb] Filetable successfully read. Metadata for " + Entries.Count + " files has been loaded.");

                if (Entries.Count != ExpectedEntries)
                {
                    Console.WriteLine("[Warning!] Archive should contain " + ExpectedEntries + " files, but only contains metadata for " +
                                      Entries.Count +
                                      " in the header!");
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[Error] " + FileLocation + " does not appear to be an arc file. It will be skipped." + (Program.Verbose.Value ? "\r\n" : " Turn on verbose mode to see the error that occurred."));

                if(Program.Verbose.Value)
                    Console.Error.WriteLine(e);

                IsValidArcFile = false;
            }
        }

        public string ExtractFile(int index, string outputDir)
        {
            ContainedFileMetadata data = Entries[index];

            byte[] buffer = new byte[data.Length];
            if (Program.Verbose.Value)
                Console.WriteLine("[Verb] \tFile begins at offset " + data.Offset + ". Seeking to position " + (data.Offset + DataStart));
            _stream.Seek(data.Offset + DataStart, SeekOrigin.Begin);
            if (Program.Verbose.Value) Console.WriteLine("[Verb] \tAbout to read " + data.Length + " bytes.");
            _stream.Read(buffer, 0, Convert.ToInt32(data.Length));

            string file = Path.Combine(outputDir, data.Filename);
            if (Program.Verbose.Value) Console.WriteLine("[Verb] \tSuccessfully read file contents. Creating file " + file);

            using (var s = File.Create(file))
            {
                if (Program.Verbose.Value) Console.WriteLine("[Verb] \tAbout to write " + buffer.Length + " bytes to file");
                s.Write(buffer, 0, buffer.Length);
                if (Program.Verbose.Value) Console.WriteLine("[Verb] \tWrite Successful");
            }

            if (Program.Verbose.Value) Console.WriteLine("[Verb] \tFile Extracted.");

            return file;
        }
    }
}