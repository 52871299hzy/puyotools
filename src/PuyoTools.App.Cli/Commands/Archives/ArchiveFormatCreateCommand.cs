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
        private readonly Option<string[]> _fromEntriesOption;

        public ArchiveFormatCreateCommand(IArchiveFormat format)
            : base(format.CommandName, $"Create {format.Name} archive")
        {
            _format = format;

            _inputOption = new("--input", "-i")
            {
                Description = "Files to add to the archive (pattern matching supported).",
                Required = false,
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

            _fromEntriesOption = new("--from-entries")
            {
                Description = "Add files from entries.txt files (pattern matching supported)."
            };
            Add(_fromEntriesOption);

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
            options.Input = parseResult.GetValue(_inputOption);
            options.Exclude = parseResult.GetValue(_excludeOption);
            options.Output = parseResult.GetRequiredValue(_outputOption);
            options.Compress = parseResult.GetValue(_compressOption);
            options.FromEntries = parseResult.GetValue(_fromEntriesOption);

            // Validate that either --input or --from-entries is provided
            if ((options.Input == null || options.Input.Length == 0) && 
                (options.FromEntries == null || options.FromEntries.Length == 0))
            {
                throw new ArgumentException("Either --input or --from-entries must be specified.");
            }
        }

        protected void Execute(ArchiveCreateOptions options, TextWriter writer)
        {
            var files = new List<ArchiveCreatorFileEntry>();
            
            // Process regular input files if provided
            if (options.Input?.Any() == true)
            {
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
            }

            // Process entries.txt files if specified
            if (options.FromEntries?.Any() == true)
            {
                foreach (var entriesPattern in options.FromEntries)
                {
                    // Get the entries.txt files to process
                    var matcher = new Matcher();
                    matcher.AddInclude(entriesPattern);
                    if (options.Exclude?.Any() == true)
                    {
                        matcher.AddExcludePatterns(options.Exclude);
                    }

                    // Determine the base directory for this pattern  
                    string baseDir;
                    if (Path.IsPathRooted(entriesPattern))
                    {
                        // For absolute paths, use the directory part as base or root if it's just a file
                        var dirPart = Path.GetDirectoryName(entriesPattern);
                        baseDir = string.IsNullOrEmpty(dirPart) ? Path.GetPathRoot(entriesPattern) : dirPart;
                        
                        // Adjust the pattern to be relative to baseDir
                        var fileName = Path.GetFileName(entriesPattern);
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

                    var entriesFiles = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDir)))
                        .Files
                        .Select(x => Path.IsPathRooted(x.Path) ? x.Path : Path.Combine(baseDir, x.Path))
                        .ToArray();

                    // Process each entries.txt file
                    foreach (var entriesFile in entriesFiles)
                    {
                        try
                        {
                            var entriesDir = Path.GetDirectoryName(entriesFile);
                            var entryPaths = File.ReadLines(entriesFile)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Select(line => Path.Combine(entriesDir, line.Trim()));

                            var entriesFileEntries = entryPaths.Select(x => new ArchiveCreatorFileEntry
                            {
                                SourceFile = x,
                                Filename = x,
                                FilenameInArchive = Path.GetFileName(x),
                            }).ToArray();

                            files.AddRange(entriesFileEntries);
                        }
                        catch (Exception ex)
                        {
                            writer.WriteLine($"Error reading entries file {entriesFile}: {ex.Message}");
                        }
                    }
                }
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
