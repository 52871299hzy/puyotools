using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PuyoTools.App.Cli.Commands.Compression;
using PuyoTools.App.Tools;

namespace PuyoTools.App.Cli.Commands.Textures
{
    class TextureDecodeCommand : Command
    {
        public TextureDecodeCommand()
            : base("decode", "Decode textures")
        {
            Option<string[]> inputOption = new("--input", "-i")
            {
                Description = "Files to decode (pattern matching supported).",
                Required = true,
            };
            Options.Add(inputOption);

            Option<string[]> excludeOption = new("--exclude")
            {
                Description = "Files to exclude from being decoded (pattern matching supported)."
            };
            Options.Add(excludeOption);

            Option<string> outputOption = new("--output", "-o")
            {
                Description = "Output directory for decoded textures. If not specified, creates 'Decoded Textures' subdirectory."
            };
            Options.Add(outputOption);

            Option<bool> compressedOption = new("--compressed")
            {
                Description = "Also decode compressed textures."
            };
            Options.Add(compressedOption);

            Option<bool> overwriteOption = new("--overwrite")
            {
                Description = "Overwrite source texture file with its decoded texture file."
            };
            Options.Add(overwriteOption);

            Option<bool> deleteOption = new("--delete")
            {
                Description = "Delete source texture file on successful decode."
            };
            Options.Add(deleteOption);

            SetAction(parseResult =>
            {
                TextureDecodeOptions options = new()
                {
                    Input = parseResult.GetValue(inputOption),
                    Exclude = parseResult.GetValue(excludeOption),
                    Output = parseResult.GetValue(outputOption),
                    Compressed = parseResult.GetValue(compressedOption),
                    Overwrite = parseResult.GetValue(overwriteOption),
                    Delete = parseResult.GetValue(deleteOption),
                };

                Execute(options, parseResult.Configuration.Output);
            });
        }

        private void Execute(TextureDecodeOptions options, TextWriter writer)
        {
            // Get the files to process by the tool
            // Handle absolute paths by using individual patterns instead of bulk processing
            var allFiles = new List<string>();

            foreach (var inputPattern in options.Input)
            {
                var matcher = new Matcher();
                matcher.AddInclude(inputPattern);
                if (options.Exclude?.Any() == true)
                {
                    matcher.AddExcludePatterns(options.Exclude);
                }

                // Determine the base directory for this pattern
                string baseDir;
                if (Path.IsPathRooted(inputPattern))
                {
                    // For absolute paths, use the directory part as base or root if it's just a file
                    var dirPart = Path.GetDirectoryName(inputPattern);
                    baseDir = string.IsNullOrEmpty(dirPart) ? Path.GetPathRoot(inputPattern) : dirPart;
                    
                    // Adjust the pattern to be relative to baseDir
                    var fileName = Path.GetFileName(inputPattern);
                    matcher = new Matcher();
                    matcher.AddInclude(fileName);
                    if (options.Exclude?.Any() == true)
                    {
                        matcher.AddExcludePatterns(options.Exclude);
                    }
                }
                else
                {
                    baseDir = Environment.CurrentDirectory;
                }

                var files = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDir)))
                    .Files
                    .Select(x => Path.IsPathRooted(x.Path) ? x.Path : Path.Combine(baseDir, x.Path))
                    .ToArray();

                allFiles.AddRange(files);
            }

            // Create options in the format the tool uses
            var toolOptions = new TextureDecoderOptions
            {
                DecodeCompressedTextures = options.Compressed,
                OutputToSourceDirectory = options.Overwrite,
                DeleteSource = options.Delete,
                OutputDirectory = options.Output,
                OutputWriter = writer,
            };

            // Create the progress handler (only if the quiet option is not set)
            var progress = new SynchronousProgress<ToolProgress>(x =>
            {
                writer.WriteLine($"Processing {x.File} ... ({x.Progress:P0})");
            });

            // Execute the tool
            var tool = new TextureDecoder(toolOptions);
            tool.Execute(allFiles, progress);
        }
    }
}
