using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PuyoTools.App.Tools
{
    class TextureDecoderOptions
    {
        public bool DecodeCompressedTextures { get; set; }

        public bool OutputToSourceDirectory { get; set; }

        public bool DeleteSource { get; set; }

        public string OutputDirectory { get; set; }

        public TextWriter OutputWriter { get; set; }
    }
}
