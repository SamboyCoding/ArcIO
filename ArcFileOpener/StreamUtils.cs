using System;
using System.IO;

namespace ArcFileOpener
{
    public static class StreamUtils
    {
        public static uint ReadUInt32(FileStream stream)
        {
            byte[] bytes = new byte[4];
            stream.Read(bytes, 0, 4);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static void WriteUInt32(Stream stream, uint toWrite)
        {
            byte[] bytes = BitConverter.GetBytes(toWrite);
            stream.Write(bytes, 0, 4);
        }
    }
}