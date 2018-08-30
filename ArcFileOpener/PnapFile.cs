using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ArcFileOpener
{
    public class PnapFile
    {
        public struct PackedFileMetadata
        {
            //This is preceded by a uint of unknown significance
            public uint index;
            public uint offset_x;
            public uint offset_y;
            public uint width;

            public uint height;

            //There are 3 more unknown uints here
            public uint length;
        }

        public bool IsPNAPFile = true;

        private readonly string _filePath;
        private readonly FileStream _stream;

        private readonly uint width;
        private readonly uint height;
        private readonly uint entryCount;

        private readonly uint contentStart;

        private readonly List<PackedFileMetadata> Files = new List<PackedFileMetadata>();

        public PnapFile(string path)
        {
            _filePath = path;
            _stream = File.OpenRead(path);

            byte[] firstBytes = new byte[4];
            _stream.Read(firstBytes, 0, 4);
            string firstFourChars = new string(Encoding.ASCII.GetChars(firstBytes));

            if (Program.Verbose.Value)
                Console.WriteLine("[Verb] Attempting to parse " + path + " as a PNAP File... first 4 characters are \"" + firstFourChars + "\"");

            if (firstFourChars != "PNAP")
            {
                IsPNAPFile = false;
                return;
            }

            //Format of a PNAP header: 4 bytes are the string "PNAP" (already read), then a uint with an unknown significance, then another signifying
            //the width of the images, then one for the height, then one for the number of contained PNG files.

            //Read the uint we don't know the meaning of, and discard
            StreamUtils.ReadUInt32(_stream);

            //Read the width
            width = StreamUtils.ReadUInt32(_stream);

            //Read the height
            height = StreamUtils.ReadUInt32(_stream);

            //Read the entry count
            entryCount = StreamUtils.ReadUInt32(_stream);

            for (int i = 0; i < entryCount; i++)
            {
                StreamUtils.ReadUInt32(_stream); //discard the meaningless one
                uint index = StreamUtils.ReadUInt32(_stream);
                uint offsetX = StreamUtils.ReadUInt32(_stream);
                uint offsetY = StreamUtils.ReadUInt32(_stream);
                uint fileWidth = StreamUtils.ReadUInt32(_stream);
                uint fileHeight = StreamUtils.ReadUInt32(_stream);
                StreamUtils.ReadUInt32(_stream); // More
                StreamUtils.ReadUInt32(_stream); // Unknown
                StreamUtils.ReadUInt32(_stream); // Values
                uint length = StreamUtils.ReadUInt32(_stream);

                Files.Add(new PackedFileMetadata
                {
                    index = index,
                    offset_x = offsetX,
                    offset_y = offsetY,
                    width = fileWidth,
                    height = fileHeight,
                    length = length
                });
            }

            contentStart = (uint) _stream.Position; //Once we've read the header, the next byte is now the first byte of the first file
        }

        public void WriteLayerInfo(string path)
        {
            if (Program.Verbose.Value) Console.WriteLine("[Verb] Writing layers.txt info to " + path);

            string ourFileName = Path.GetFileNameWithoutExtension(_filePath);
            string content = "PNAP File: ";
            content += Path.GetFullPath(_filePath) + "\r\n";
            content += "\tOverall PNAP Image Width  : " + width + "\r\n";
            content += "\tOverall PNAP Image Height : " + height + "\r\n";
            content += "\tNumber of subimages       : " + entryCount + "\r\n";
            content += "\r\n\r\n";

            string fullPath = Path.GetFullPath(_filePath);

            JObject machineReadable = new JObject
            {
                ["PNAPath"] = fullPath,
                ["Width"] = width,
                ["Height"] = height,
                ["SubImageCount"] = entryCount
            };

            JArray subImages = new JArray();

            foreach (PackedFileMetadata meta in Files)
            {
                content += "\tContained File: " + ourFileName + "_" + meta.index + ".png\r\n";
                content += "\t\tX Offset : " + meta.offset_x + "\r\n";
                content += "\t\tY Offset : " + meta.offset_y + "\r\n";
                content += "\t\tWidth    : " + meta.width + "\r\n";
                content += "\t\tHeight   : " + meta.height + "\r\n";
                content += "\t\tFile Size: " + meta.length + " bytes\r\n";

                JObject subImage = new JObject
                {
                    ["Path"] = Path.Combine(Path.GetDirectoryName(fullPath),
                        Path.GetFileNameWithoutExtension(fullPath) + "_" + meta.index + ".png"),
                    ["FileName"] = Path.GetFileNameWithoutExtension(fullPath) + "_" + meta.index + ".png",
                    ["XOffset"] = meta.offset_x,
                    ["YOffset"] = meta.offset_y,
                    ["Width"] = meta.width,
                    ["Height"] = meta.height,
                    ["Size"] = meta.length
                };

                subImages.Add(subImage);
            }

            machineReadable["SubImages"] = subImages;

            File.WriteAllText(path, content);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".json"),
                machineReadable.ToString());
        }

        private long GetFileContentStart(int fileIndex)
        {
            if (fileIndex < 0 || fileIndex >= Files.Count) return -1;

            long pos = contentStart;
            for (int i = 0; i < fileIndex; i++)
                pos += Files[i].length;

            return pos;
        }

        public void UnpackFile(PackedFileMetadata file)
        {
            if (!Files.Contains(file)) return;

            string outputFilePath = Path.Combine(Path.GetDirectoryName(_filePath),
                Path.GetFileNameWithoutExtension(_filePath) + "_" + file.index + ".png");

            _stream.Seek(GetFileContentStart(Files.IndexOf(file)), SeekOrigin.Begin);
            byte[] content = new byte[file.length];
            _stream.Read(content, 0, (int) file.length);

            File.WriteAllBytes(outputFilePath, content);
        }

        public void UnpackFiles()
        {
            foreach (PackedFileMetadata meta in Files)
            {
                UnpackFile(meta);
            }
        }
    }
}