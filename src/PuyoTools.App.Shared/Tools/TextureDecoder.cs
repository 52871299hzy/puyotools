using PuyoTools.App.Formats.Compression;
using PuyoTools.App.Formats.Textures;
using PuyoTools.Core.Textures;
using PuyoTools.Core.Textures.Gvr;
using PuyoTools.Core.Textures.Pvr;
using PuyoTools.Core.Textures.Gim;
using PuyoTools.Core.Textures.Svr;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PuyoTools.App.Tools
{
    class TextureDecoder
    {
        private readonly TextureDecoderOptions options;

        public TextureDecoder(TextureDecoderOptions options)
        {
            this.options = options;
        }

        public void Execute(
            IList<string> files,
            IProgress<ToolProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];

                // Report progress.
                progress?.Report(new ToolProgress((double)i / files.Count, file));

                // Let's open the file.
                // But, we're going to do this in a try catch in case any errors happen.
                try
                {
                    ITextureFormat format;
                    MemoryStream textureData = new MemoryStream();

                    using (FileStream inStream = File.OpenRead(file))
                    {
                        // Set source to inStream
                        // The reason we do it like this is because source does not always equal inStream.
                        // You'll see why very soon.
                        Stream source = inStream;

                        // Get the format of the texture
                        format = TextureFactory.GetFormat(source, Path.GetFileName(file));
                        if (format == null)
                        {
                            // Maybe it's compressed? Let's check.
                            // But first, we need to make sure we want to check
                            if (options.DecodeCompressedTextures)
                            {
                                // Get the compression format, if it is compressed that is.
                                ICompressionFormat compressionFormat = CompressionFactory.GetFormat(source, Path.GetFileName(file));
                                if (compressionFormat != null)
                                {
                                    // Ok, it appears to be compressed. Let's decompress it, and then check the format again
                                    source = new MemoryStream();
                                    compressionFormat.GetCodec().Decompress(inStream, source);

                                    source.Position = 0;
                                    format = TextureFactory.GetFormat(source, Path.GetFileName(file));
                                    
                                    // Report detailed decompression information
                                    if (format != null && options.OutputWriter != null)
                                    {
                                        string detailedInfo = GetDetailedFormatInfo(format, source, file);
                                        options.OutputWriter.WriteLine($"Decompressed {compressionFormat.Name} -> {detailedInfo}");
                                    }
                                }
                            }

                            // If we still don't know what the texture format is, just skip the file.
                            if (format == null)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // Report detailed format information for non-compressed textures
                            if (options.OutputWriter != null)
                            {
                                string detailedInfo = GetDetailedFormatInfo(format, source, file);
                                options.OutputWriter.WriteLine(detailedInfo);
                            }
                        }

                        // Alright, let's decode the texture now
                        TextureBase texture = format.GetCodec();

                        // Events
                        if (texture is ITextureHasExternalPalette textureWithExternalPalette)
                        {
                            textureWithExternalPalette.ExternalPaletteRequired += (sender, e) =>
                            {
                                // It appears that we need to load an external palette.
                                // Let's get the filename for this palette file, see if it exists, and load it in
                                string paletteName = Path.ChangeExtension(file, format.PaletteFileExtension);

                                if (!File.Exists(paletteName))
                                {
                                    // If the palette file doesn't exist, just skip over this texture.
                                    return;
                                }

                                // Copy the palette data to another stream so we can safely delete the data.
                                var paletteMemoryStream = new MemoryStream();
                                using (FileStream paletteFileStream = File.OpenRead(paletteName))
                                {
                                    paletteFileStream.CopyTo(paletteMemoryStream);
                                }
                                paletteMemoryStream.Position = 0;

                                e.Palette = paletteMemoryStream;

                                // Delete the palette file if the user chose to delete the source texture
                                if (options.DeleteSource)
                                {
                                    File.Delete(paletteName);
                                }
                            };
                        }

                        try
                        {
                            texture.Read(source, textureData, (int)source.Length);
                        }
                        catch (TextureNeedsPaletteException)
                        {
                            // Just skip over this texture.
                            continue;

                            /*// It appears that we need to load an external palette.
                            // Let's get the filename for this palette file, see if it exists, and load it in
                            string paletteName = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file)) + format.PaletteFileExtension;

                            if (!File.Exists(paletteName))
                            {
                                // If the palette file doesn't exist, just skip over this texture.
                                continue;
                            }

                            source.Position = 0; // We need to reset the position
                            textureData = new MemoryStream(); // Just incase some data was written
                            using (FileStream paletteData = File.OpenRead(paletteName))
                            {
                                texture.PaletteStream = paletteData;
                                texture.Read(source, textureData);
                            }

                            // Delete the palette file if the user chose to delete the source texture
                            if (settings.DeleteSource)
                            {
                                File.Delete(paletteName);
                            }*/
                        }
                    }

                    // Get the output path and create it if it does not exist.
                    string outPath;
                    if (options.OutputToSourceDirectory)
                    {
                        outPath = Path.GetDirectoryName(file);
                    }
                    else if (!string.IsNullOrEmpty(options.OutputDirectory))
                    {
                        outPath = options.OutputDirectory;
                    }
                    else
                    {
                        outPath = Path.Combine(Path.GetDirectoryName(file), "Decoded Textures");
                    }

                    if (!Directory.Exists(outPath))
                    {
                        Directory.CreateDirectory(outPath);
                    }

                    // Time to write out the file
                    using (FileStream destination = File.Create(Path.Combine(outPath, Path.GetFileNameWithoutExtension(file) + ".png")))
                    {
                        textureData.WriteTo(destination);
                    }

                    // Delete the source if the user chose to
                    if (options.DeleteSource)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Meh, just ignore the error.
                }
            }
        }

        /// <summary>
        /// Gets detailed format information for a texture by inspecting the codec.
        /// </summary>
        /// <param name="format">The detected texture format.</param>
        /// <param name="source">The stream containing the texture data.</param>
        /// <param name="file">The filename for display purposes.</param>
        /// <returns>A detailed format description string.</returns>
        private string GetDetailedFormatInfo(ITextureFormat format, Stream source, string file)
        {
            try
            {
                long originalPosition = source.Position;
                source.Position = 0;

                string formatDetails = $"Detected {format.Name} format: {file}";

                // Get detailed information based on format type
                if (format.Name == "GVR")
                {
                    var gvrDecoder = new GvrTextureDecoder(source);
                    formatDetails = $"Detected {format.Name} format: {file} " +
                                   $"[{gvrDecoder.Width}x{gvrDecoder.Height}, " +
                                   $"Pixel: {gvrDecoder.PixelFormat}, " +
                                   (gvrDecoder.PaletteFormat.HasValue ? $"Palette: {gvrDecoder.PaletteFormat.Value}, " : "") +
                                   (gvrDecoder.GlobalIndex.HasValue ? $"GBIX: {gvrDecoder.GlobalIndex.Value}" : "No GBIX") + "]";
                }
                else if (format.Name == "PVR")
                {
                    var pvrDecoder = new PvrTextureDecoder(source);
                    formatDetails = $"Detected {format.Name} format: {file} " +
                                   $"[{pvrDecoder.Width}x{pvrDecoder.Height}, " +
                                   $"Pixel: {pvrDecoder.PixelFormat}, " +
                                   $"Data: {pvrDecoder.DataFormat}, " +
                                   (pvrDecoder.CompressionFormat != PvrCompressionFormat.None ? $"Compression: {pvrDecoder.CompressionFormat}, " : "") +
                                   (pvrDecoder.GlobalIndex.HasValue ? $"GBIX: {pvrDecoder.GlobalIndex.Value}" : "No GBIX") + "]";
                }
                else if (format.Name == "GIM")
                {
                    var gimDecoder = new GimTextureDecoder(source);
                    formatDetails = $"Detected {format.Name} format: {file} " +
                                   $"[{gimDecoder.Width}x{gimDecoder.Height}, " +
                                   $"Pixel: {gimDecoder.PixelFormat}, " +
                                   (gimDecoder.PaletteFormat.HasValue ? $"Palette: {gimDecoder.PaletteFormat.Value}, " : "") +
                                   (gimDecoder.Metadata != null ? $"Metadata: {gimDecoder.Metadata.OriginalFilename}" : "") + "]";
                }
                else if (format.Name == "SVR")
                {
                    var svrDecoder = new SvrTextureDecoder(source);
                    formatDetails = $"Detected {format.Name} format: {file} " +
                                   $"[{svrDecoder.Width}x{svrDecoder.Height}, " +
                                   $"Pixel: {svrDecoder.PixelFormat}" +
                                   (svrDecoder.GlobalIndex.HasValue ? $", GBIX: {svrDecoder.GlobalIndex.Value}" : ", No GBIX") + "]";
                }

                source.Position = originalPosition;
                return formatDetails;
            }
            catch
            {
                // Fall back to basic format info if detailed parsing fails
                return $"Detected {format.Name} format: {file}";
            }
        }
    }
}
