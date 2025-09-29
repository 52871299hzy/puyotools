using PuyoTools.Core;
using PuyoTools.Core.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PuyoTools.Archives.Formats.U8
{
    public class U8Writer : ArchiveWriter<U8WriterEntry>
    {
        public U8Writer(Stream destination) : base(destination)
        {
        }

        public override void Write(Stream destination)
        {
            long streamStart = destination.Position;

            using BinaryWriter writer = new(destination, Encoding.UTF8, true);

            // Puyo Tools is only capable of building U8 archives that do not contain directories.
            // It's just very difficult to do with the way Puyo Tools is structured.

            // First things first, let's get the header size
            int headerSize = ((_entries.Count + 1) * 12) + 1;
            for (int i = 0; i < _entries.Count; i++)
            {
                headerSize += _entries[i].Name.Length + 1;
            }

            // Get the name and data offset
            int nameOffset = 0;
            int dataOffset = MathHelper.RoundUp(0x20 + headerSize, 32);

            // Start writing out the header
            writer.Write(U8Constants.MagicCode);

            writer.WriteUInt32BigEndian(0x20); // Root node offset (always 0x20)
            writer.WriteInt32BigEndian(headerSize); // Header size
            writer.WriteInt32BigEndian(dataOffset); // Data offset

            // Pad to 32 bytes
            writer.Align(32, streamStart);

            // Write the root node
            writer.WriteByte(1);
            writer.WriteByte((byte)(nameOffset >> 16));
            writer.WriteUInt16BigEndian((ushort)(nameOffset & 0xFFFF));
            writer.WriteInt32BigEndian(0);
            writer.WriteInt32BigEndian(_entries.Count + 1);

            nameOffset++;

            // Write out the file nodes
            for (int i = 0; i < _entries.Count; i++)
            {
                writer.WriteByte(0);
                writer.WriteByte((byte)(nameOffset >> 16));
                writer.WriteUInt16BigEndian((ushort)(nameOffset & 0xFFFF));
                writer.WriteInt32BigEndian(dataOffset);
                writer.WriteInt32BigEndian((int)_entries[i].Length);

                nameOffset += _entries[i].Name.Length + 1;
                dataOffset += MathHelper.RoundUp((int)_entries[i].Length, 32);
            }

            // Write out the filename table
            writer.WriteNullTerminatedString(string.Empty);
            for (int i = 0; i < _entries.Count; i++)
            {
                writer.WriteNullTerminatedString(_entries[i].Name);
            }

            // Pad to 32 bytes
            writer.Align(32, streamStart);

            // Write the file data
            for (int i = 0; i < _entries.Count; i++)
            {
                WriteEntry(destination, _entries[i]);
                writer.Align(32, streamStart);
            }
        }

        protected override U8WriterEntry CreateEntry(Stream source, string? name = null, bool leaveOpen = false)
        {
            return new U8WriterEntry(source, name, leaveOpen);
        }
    }
}