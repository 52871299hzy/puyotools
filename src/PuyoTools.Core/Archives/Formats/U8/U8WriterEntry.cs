using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PuyoTools.Archives.Formats.U8
{
    public class U8WriterEntry : ArchiveWriterEntry
    {
        public U8WriterEntry(Stream source, string? name = null, bool leaveOpen = false) : base(source, name, leaveOpen)
        {
        }
    }
}