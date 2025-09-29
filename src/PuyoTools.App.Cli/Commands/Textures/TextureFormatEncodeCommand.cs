using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PuyoTools.App.Formats.Textures;
using PuyoTools.App.Tools;

namespace PuyoTools.App.Cli.Commands.Textures
{
    class TextureFormatEncodeCommand : Command
    {
        private readonly ITextureFormat _format;

        private readonly Option<string[]> _inputOption;
        private readonly Option<string[]> _excludeOption;
        private readonly Option<string> _outputOption;

        public TextureFormatEncodeCommand(ITextureFormat format)
            : base(format.CommandName, $"Create {format.Name} texture")
        {
            _format = format;

            _inputOption = new("--input", "-i")
            {
                Description = "Files to encode (pattern matching supported).",
                Required = true,
            };
            Options.Add(_inputOption);

            _excludeOption = new("--exclude")
            {
                Description = "Files to exclude from being encoded (pattern matching supported)."
            };
            Options.Add(_excludeOption);

            _outputOption = new("--output", "-o")
            {
                Description = "Output directory for encoded textures. If not specified, creates 'Encoded Textures' subdirectory."
            };
            Options.Add(_outputOption);

            SetAction(parseResult => Execute(CreateOptions(parseResult), parseResult.Configuration.Output));
        }

        protected virtual TextureFormatEncodeOptions CreateOptions(ParseResult parseResult)
        {
            TextureFormatEncodeOptions options = new();
            SetBaseOptions(parseResult, options);
            return options;
        }

        protected void SetBaseOptions(ParseResult parseResult, TextureFormatEncodeOptions options)
        {
            options.Input = parseResult.GetRequiredValue(_inputOption);
            options.Exclude = parseResult.GetValue(_excludeOption);
            options.Output = parseResult.GetValue(_outputOption);
        }

        protected void Execute(TextureFormatEncodeOptions options, TextWriter writer)
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
            var toolOptions = new TextureEncoderOptions
            {
                OutputDirectory = options.Output,
            };

            // Create the progress handler (only if the quiet option is not set)
            var progress = new SynchronousProgress<ToolProgress>(x =>
            {
                writer.WriteLine($"Processing {x.File} ... ({x.Progress:P0})");
            });

            // Execute the tool
            var tool = new TextureEncoder(_format, toolOptions, options as ITextureFormatOptions);
            tool.Execute(allFiles, progress);
        }
    }
}
