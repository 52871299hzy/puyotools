using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PuyoTools.App.Formats.Archives;
using PuyoTools.App.Formats.Compression;
using PuyoTools.App.Tools;

namespace PuyoTools.App.Cli.Commands.Archives
{
    class ArchiveFormatCreateCommand : Command
    {
        private readonly IArchiveFormat _format;

        private readonly Option<string[]> _inputOption;
        private readonly Option<string[]> _excludeOption;
        private readonly Option<string> _outputOption;
        private readonly Option<string> _compressOption;

        public ArchiveFormatCreateCommand(IArchiveFormat format)
            : base(format.CommandName, $"Create {format.Name} archive")
        {
            _format = format;

            _inputOption = new("--input", "-i")
            {
                Description = "Files to add to the archive (pattern matching supported).",
                Required = true,
                AllowMultipleArgumentsPerToken = true,
            };
            Add(_inputOption);

            _excludeOption = new("--exclude")
            {
                Description = "Files to exclude from being added to the archive (pattern matching supported)."
            };
            Add(_excludeOption);

            _outputOption = new("--output", "-o")
            {
                Description = "The name of the archive to create.",
                Required = true,
            };
            Add(_outputOption);

            _compressOption = new Option<string>("--compress")
            {
                Description = "Compress the archive"
            }
                .AcceptOnlyFromAmong(CompressionFactory.EncoderFormats.Select(x => x.CommandName).ToArray());
            Add(_compressOption);

            SetAction(parseResult => Execute(CreateOptions(parseResult), parseResult.Configuration.Output));
        }

        protected virtual ArchiveCreateOptions CreateOptions(ParseResult parseResult)
        {
            ArchiveCreateOptions options = new();
            SetBaseOptions(parseResult, options);
            return options;
        }

        protected void SetBaseOptions(ParseResult parseResult, ArchiveCreateOptions options)
        {
            options.Input = parseResult.GetRequiredValue(_inputOption);
            options.Exclude = parseResult.GetValue(_excludeOption);
            options.Output = parseResult.GetRequiredValue(_outputOption);
            options.Compress = parseResult.GetValue(_compressOption);
        }

        protected void Execute(ArchiveCreateOptions options, TextWriter writer)
        {
            var files = new List<ArchiveCreatorFileEntry>();
            
            foreach (var input in options.Input)
            {
                string filename = input;
                //string filenameInArchive = Path.GetFileName(filename);
                string? filenameInArchive = null;
                int seperatorIndex = input.IndexOf(',');
                if (seperatorIndex != -1)
                {
                    filename = input.Substring(0, seperatorIndex);
                    filenameInArchive = input.Substring(seperatorIndex + 1);
                }

                // Get the files to process by the tool
                // To ensure files are added in the order specified, they will be matched seperately.
                var matcher = new Matcher();
                matcher.AddInclude(filename);
                if (options.Exclude?.Any() == true)
                {
                    matcher.AddExcludePatterns(options.Exclude);
                }

                // Determine the base directory for this pattern
                string baseDir;
                if (Path.IsPathRooted(filename))
                {
                    // For absolute paths, use the directory part as base or root if it's just a file
                    var dirPart = Path.GetDirectoryName(filename);
                    baseDir = string.IsNullOrEmpty(dirPart) ? Path.GetPathRoot(filename) : dirPart;
                    
                    // Adjust the pattern to be relative to baseDir
                    var fileName = Path.GetFileName(filename);
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

                var matchedFiles = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDir)))
                    .Files
                    .Select(x => Path.IsPathRooted(x.Path) ? x.Path : Path.Combine(baseDir, x.Path))
                    .Select(x => new ArchiveCreatorFileEntry
                    {
                        SourceFile = x,
                        //Filename = filename,
                        //FilenameInArchive = filenameInArchive,
                        Filename = x,
                        FilenameInArchive = filenameInArchive ?? x,
                    })
                    .ToArray();

                files.AddRange(matchedFiles);
            }

            // Create options in the format the tool uses
            var toolOptions = new ArchiveCreatorOptions
            {
                CompressionFormat = options.Compress is not null
                    ? CompressionFactory.EncoderFormats.FirstOrDefault(x => x.CommandName == options.Compress)
                    : null,
            };

            // Create the progress handler (only if the quiet option is not set)
            var progress = new SynchronousProgress<ToolProgress>(x =>
            {
                writer.WriteLine($"Processing {x.File} ... ({x.Progress:P0})");
            });

            // Execute the tool
            var tool = new ArchiveCreator(_format, toolOptions, options as IArchiveFormatOptions);
            tool.Execute(files, options.Output, progress);
        }
    }
}
