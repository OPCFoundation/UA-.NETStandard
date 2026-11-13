/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter
{
    /// <summary>
    /// Reads local NodeSet2 files and writes the linked WoT export.
    /// </summary>
    internal sealed class ExportCommand(
        TextWriter output, TextWriter error, IWotUnitMappingProvider? unitMappingProvider = null)
    {
        /// <summary>
        /// Runs the command. Returns 0 on success, 1 on an export error,
        /// 2 on an input error, or 130 on cancellation.
        /// </summary>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (args is ["--help"] or ["-h"])
            {
                await m_output.WriteLineAsync(
                    "Usage: opcua-wot-export <source-path> [output-path]\n" +
                    "Read *NodeSet2.xml in the source folder and its subfolders.\n" +
                    "Default output: <source-path>\\out. A source file is also accepted.\n" +
                    "An explicit output path is relative to the current folder.").ConfigureAwait(false);
                return 0;
            }
            if (args.Length is < 1 or > 2 || args.Any(a => a.StartsWith('-')))
            {
                await m_error.WriteLineAsync(
                    "ERROR: Supply a source path and, if needed, an output path. Use --help for help.")
                    .ConfigureAwait(false);
                return 2;
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string source = Path.GetFullPath(args[0]);
                bool singleFile = File.Exists(source);
                if (!singleFile && !Directory.Exists(source))
                {
                    await m_error.WriteLineAsync($"ERROR: Source path does not exist: {source}").ConfigureAwait(false);
                    return 2;
                }
                if (singleFile && !IsNodeSet(source))
                {
                    await m_error.WriteLineAsync("ERROR: The source file name must end with NodeSet2.xml.")
                        .ConfigureAwait(false);
                    return 2;
                }
                string sourceFolder = singleFile ? Path.GetDirectoryName(source)! : source;
                string destination = Path.GetFullPath(args.Length == 2 ? args[1] : Path.Combine(sourceFolder, "out"));
                if (IsWithin(sourceFolder, destination))
                {
                    await m_error.WriteLineAsync("ERROR: The output folder must not contain the source folder.")
                        .ConfigureAwait(false);
                    return 2;
                }
                string[] files = singleFile
                    ? [source]
                    : [.. FindNodeSets(sourceFolder, destination).Order(StringComparer.Ordinal)];
                if (files.Length == 0)
                {
                    await m_error.WriteLineAsync("ERROR: No *NodeSet2.xml files were found.").ConfigureAwait(false);
                    return 2;
                }
                int failed = 0;
                int written = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourceFolder, files[i]);
                    await m_output.WriteLineAsync($"[{i + 1}/{files.Length}] Read {relative}").ConfigureAwait(false);
                    try
                    {
                        string folder = Path.Combine(destination, relative);
                        written += await ExportFileAsync(files[i], folder, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsInputError(ex))
                    {
                        failed++;
                        await m_error.WriteLineAsync($"ERROR: {relative}: {ex.Message}").ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        failed++;
                        await m_error.WriteLineAsync(
                            $"ERROR: {relative}: Unexpected {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
                    }
                }
                await m_output.WriteLineAsync(
                    $"Done. Files: {files.Length}. Failed: {failed}. " +
                    $"Documents written: {written}. Output: {destination}")
                    .ConfigureAwait(false);
                return failed == 0 ? 0 : 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await m_error.WriteLineAsync("ERROR: Export stopped. The output can be incomplete.")
                    .ConfigureAwait(false);
                return 130;
            }
            catch (Exception ex) when (IsInputError(ex))
            {
                await m_error.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
                return 1;
            }
        }

        /// <summary>
        /// Exports one source as a linked document set without changing its hrefs.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private async Task<int> ExportFileAsync(string source, string destination, CancellationToken cancellationToken)
        {
            var options = new WotNodeSetConverterOptions
            {
                UnitMappingProvider = unitMappingProvider,
                MaxDocumentSetDocuments = 100_000,
                MaxDocumentSetDepth = 128,
                MaxDocumentSetBytes = 512L * 1024 * 1024,
                MaxResolverDocuments = 100_000,
                MaxResolverDepth = 128,
                MaxResolverTotalBytes = 512L * 1024 * 1024
            };
            using var stream = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (stream.Length > options.MaxNodeSetSize)
            {
                throw new FormatException($"The NodeSet exceeds the {options.MaxNodeSetSize} byte limit.");
            }
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            UANodeSet nodeSet = UANodeSet.Read(buffer) ?? throw new FormatException("The file is not a NodeSet2.");
            await m_output.WriteLineAsync("  Convert the NodeSet. Check source preservation.").ConfigureAwait(false);
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                nodeSet, "model.TM.json", options: options, cancellationToken: cancellationToken).ConfigureAwait(false);
            using WotDocumentSet? documents = result.Value;
            foreach (WotDiagnostic diagnostic in result.Diagnostics)
            {
                TextWriter writer = diagnostic.Severity == WotDiagnosticSeverity.Info ? m_output : m_error;
                await writer.WriteLineAsync($"  {diagnostic.Severity.ToString().ToUpperInvariant()}: {diagnostic}")
                    .ConfigureAwait(false);
            }
            if (!result.Success || documents is null)
            {
                throw new FormatException("The converter could not preserve this NodeSet. No documents were written.");
            }
            int models = 0;
            for (int i = 0; i < documents.Entries.Count; i++)
            {
                WotDocumentSetEntry entry = documents.Entries[i];
                if (entry.Href != Path.GetFileName(entry.Href) || entry.Href.IndexOfAny(['\\', '/', ':']) >= 0)
                {
                    throw new FormatException($"The converter returned an unsafe file name: {entry.Href}");
                }
                if (entry.Document.Kind == WotDocumentKind.ThingModel)
                {
                    models++;
                }
            }
            Directory.CreateDirectory(destination);
            for (int i = 0; i < documents.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotDocumentSetEntry entry = documents.Entries[i];
                string path = Path.Combine(destination, entry.Href);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, entry.Document.Utf8Json, cancellationToken)
                        .ConfigureAwait(false);
                    File.Move(temporary, path, overwrite: true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
            if (models != documents.Entries.Count)
            {
                await m_error.WriteLineAsync(
                    $"  WARNING: Keep all {documents.Entries.Count - models} support documents. " +
                    "They are not Thing Models. Their file names are link targets, not type claims.")
                    .ConfigureAwait(false);
            }
            if (models == 0)
            {
                await m_error.WriteLineAsync("  WARNING: This NodeSet has no Thing Model in the converter output.")
                    .ConfigureAwait(false);
            }
            await m_output.WriteLineAsync(
                $"  Wrote {models} Thing Models and {documents.Entries.Count - models} support files.")
                .ConfigureAwait(false);
            return documents.Entries.Count;
        }

        /// <summary>
        /// Finds input files without entering output folders or directory links.
        /// </summary>
        private static IEnumerable<string> FindNodeSets(string source, string destination)
        {
            foreach (string file in Directory.EnumerateFiles(source))
            {
                if (IsNodeSet(file))
                {
                    yield return file;
                }
            }
            foreach (string folder in Directory.EnumerateDirectories(source))
            {
                if (!IsWithin(folder, destination) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                {
                    foreach (string file in FindNodeSets(folder, destination))
                    {
                        yield return file;
                    }
                }
            }
        }

        /// <summary>
        /// Checks the NodeSet2 file suffix without a case restriction.
        /// </summary>
        private static bool IsNodeSet(string path)
        {
            return path.EndsWith("NodeSet2.xml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether a path is equal to, or is below, a folder.
        /// </summary>
        private static bool IsWithin(string path, string folder)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string normalizedFolder = Path.TrimEndingDirectorySeparator(folder);
            string prefix = Path.EndsInDirectorySeparator(normalizedFolder)
                ? normalizedFolder
                : normalizedFolder + Path.DirectorySeparatorChar;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(path), normalizedFolder, comparison) ||
                path.StartsWith(prefix, comparison);
        }

        /// <summary>
        /// Selects expected file and conversion errors for clear command-line reports.
        /// </summary>
        private static bool IsInputError(Exception exception)
        {
            return exception is IOException or UnauthorizedAccessException or ArgumentException
                or FormatException or XmlException or InvalidOperationException or NotSupportedException;
        }

        /// <summary>Receives progress and completion messages.</summary>
        private readonly TextWriter m_output = output ?? throw new ArgumentNullException(nameof(output));

        /// <summary>Receives warnings and errors.</summary>
        private readonly TextWriter m_error = error ?? throw new ArgumentNullException(nameof(error));
    }
}
