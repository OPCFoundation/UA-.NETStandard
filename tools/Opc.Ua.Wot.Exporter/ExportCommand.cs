// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        TextWriter output,
        TextWriter error,
        IWotUnitMappingProvider? unitMappingProvider = null,
        IWotQuantityKindMappingProvider? quantityKindMappingProvider = null,
        Func<string, string?>? getEnvironmentVariable = null)
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
                    ("Usage: opcua-wot-export <source-path> [output-path] " +
                        "[--registry-base-url <url>] [--group-id <id>] " +
                        "[--metadata <file>] [--identifier-safe-keys]\n" +
                        "Read *NodeSet2.xml in the source folder and its subfolders.\n" +
                        "Default output: <source-path>\\out. A source file is also accepted.\n" +
                        "An explicit output path is relative to the current folder.\n" +
                        "Default registry: https://opcfoundation.org. Default group: opcua.\n" +
                        "OPCUA_WOT_REGISTRY_BASE_URL overrides the default registry; " +
                        "--registry-base-url takes precedence.")
                    .AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false);
                return 0;
            }
            if (!TryReadArguments(
                args, m_getEnvironmentVariable("OPCUA_WOT_REGISTRY_BASE_URL"),
                out string[] paths, out string registryBaseUrl, out string groupId,
                out string? metadataPath, out bool identifierSafeKeys))
            {
                await m_error.WriteLineAsync(
                    "ERROR: Supply a source path and, if needed, an output path. Use --help for help.".AsMemory(),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                return 2;
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExportMetadata metadata = metadataPath is null
                    ? new ExportMetadata()
                    : ExportMetadata.Parse(await File.ReadAllBytesAsync(metadataPath, cancellationToken)
                        .ConfigureAwait(false));
                string source = Path.GetFullPath(paths[0]);
                bool singleFile = File.Exists(source);
                if (!singleFile && !Directory.Exists(source))
                {
                    await m_error.WriteLineAsync(
                        $"ERROR: Source path does not exist: {source}".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    return 2;
                }
                if (singleFile && !IsNodeSet(source))
                {
                    await m_error.WriteLineAsync(
                        "ERROR: The source file name must end with NodeSet2.xml.".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    return 2;
                }
                string sourceFolder = singleFile ? Path.GetDirectoryName(source)! : source;
                string destination = Path.GetFullPath(paths.Length == 2 ? paths[1] : Path.Combine(sourceFolder, "out"));
                if (IsWithin(sourceFolder, destination))
                {
                    await m_error.WriteLineAsync(
                        "ERROR: The output folder must not contain the source folder.".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    return 2;
                }
                string[] files = singleFile
                    ? [source]
                    : [.. FindNodeSets(sourceFolder, destination, cancellationToken).Order(StringComparer.Ordinal)];
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Length == 0)
                {
                    await m_error.WriteLineAsync(
                        "ERROR: No *NodeSet2.xml files were found.".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    return 2;
                }
                int failed = 0;
                int written = 0;
                var loaded = new List<(string Path, UANodeSet NodeSet)>();
                for (int i = 0; i < files.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourceFolder, files[i]);
                    await m_output.WriteLineAsync(
                        $"[{i + 1}/{files.Length}] Read {relative}".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    try
                    {
                        loaded.Add((files[i],
                            await ReadNodeSetAsync(files[i], cancellationToken).ConfigureAwait(false)));
                    }
                    catch (Exception ex) when (IsInputError(ex))
                    {
                        failed++;
                        await m_error.WriteLineAsync(
                            $"ERROR: {relative}: {ex.Message}".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        failed++;
                        await m_error.WriteLineAsync(
                            $"ERROR: {relative}: Unexpected {ex.GetType().Name}: {ex.Message}".AsMemory(),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                var nodeResolver = WotDocumentNodeResolver.FromNodeSets(
                    loaded.Select(file => file.NodeSet).ToArrayOf(), cancellationToken);
                var converted = new List<(string Path, UANodeSet NodeSet)>();
                var mapping = new ExportModelMapping(loaded.Select(file => file.NodeSet), metadata, identifierSafeKeys);
                var references = new RegistryModelReferences(registryBaseUrl, groupId, mapping);
                foreach ((string path, UANodeSet nodeSet) in loaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourceFolder, path);
                    try
                    {
                        Dictionary<ExpandedNodeId, string> modelIds = CreateModelIds(nodeSet, cancellationToken);
                        using WotDocumentSet documents = await ConvertFileAsync(
                            nodeSet, nodeResolver, cancellationToken)
                            .ConfigureAwait(false);
                        references.Add(nodeSet, documents, modelIds);
                        mapping.AddDefinitions(documents);
                        converted.Add((path, nodeSet));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        failed++;
                        string detail = IsInputError(ex)
                            ? ex.Message : $"Unexpected {ex.GetType().Name}: {ex.Message}";
                        await m_error.WriteLineAsync(
                            $"ERROR: {relative}: {detail}".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                }
                foreach ((string path, UANodeSet nodeSet) in converted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourceFolder, path);
                    try
                    {
                        using WotDocumentSet documents = await ConvertFileAsync(
                            nodeSet, nodeResolver, cancellationToken, reportDiagnostics: false).ConfigureAwait(false);
                        written += await ExportFileAsync(
                            documents, Path.Combine(destination, relative), references, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        failed++;
                        string detail = IsInputError(ex)
                            ? ex.Message : $"Unexpected {ex.GetType().Name}: {ex.Message}";
                        await m_error.WriteLineAsync(
                            $"ERROR: {relative}: {detail}".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                }
                await m_output.WriteLineAsync(
                    ($"Done. Files: {files.Length}. Failed: {failed}. " +
                        $"Documents written: {written}. Output: {destination}").AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                return failed == 0 ? 0 : 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Final diagnostics must still be written after export cancellation.
                await m_error.WriteLineAsync(
                    "ERROR: Export stopped. The output can be incomplete.".AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false);
                return 130;
            }
            catch (Exception ex) when (IsInputError(ex))
            {
                await m_error.WriteLineAsync($"ERROR: {ex.Message}".AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false);
                return 1;
            }
        }

        /// <summary>
        /// Reads positional paths and registry options without accepting ambiguous URL components.
        /// </summary>
        private static bool TryReadArguments(
            string[] args,
            string? environmentRegistryBaseUrl,
            out string[] paths,
            out string registryBaseUrl,
            out string groupId,
            out string? metadataPath,
            out bool identifierSafeKeys)
        {
            var positional = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            registryBaseUrl = string.IsNullOrEmpty(environmentRegistryBaseUrl)
                ? "https://opcfoundation.org" : environmentRegistryBaseUrl;
            groupId = "opcua";
            metadataPath = null;
            identifierSafeKeys = false;
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (argument is not ['-', ..])
                {
                    positional.Add(argument);
                }
                else if (argument == "--identifier-safe-keys" && seen.Add(argument))
                {
                    identifierSafeKeys = true;
                }
                else if (argument is not ("--registry-base-url" or "--group-id" or "--metadata") ||
                    !seen.Add(argument) ||
                    ++i == args.Length ||
                    args[i] is ['-', ..])
                {
                    paths = [];
                    return false;
                }
                else if (argument == "--registry-base-url")
                {
                    registryBaseUrl = args[i];
                }
                else if (argument == "--group-id")
                {
                    groupId = args[i];
                }
                else
                {
                    metadataPath = args[i];
                }
            }
            paths = [.. positional];
            if (paths.Length is < 1 or > 2 ||
                !Uri.TryCreate(registryBaseUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("http" or "https") ||
                uri.Host.Length == 0 ||
                uri.Query.Length != 0 ||
                uri.Fragment.Length != 0 ||
                uri.UserInfo.Length != 0 ||
                string.IsNullOrWhiteSpace(groupId) ||
                groupId is "." or "..")
            {
                return false;
            }
            registryBaseUrl = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        /// <summary>
        /// Reads a bounded source model before any dependent model is exported.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static async Task<UANodeSet> ReadNodeSetAsync(string source, CancellationToken cancellationToken)
        {
            var options = new WotNodeSetConverterOptions();
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
            // Validate before combining contexts so a malformed model cannot stop unrelated exports.
            _ = WotDocumentNodeResolver.FromNodeSets([nodeSet], cancellationToken);
            return nodeSet;
        }

        /// <summary>
        /// Converts one source and checks preservation before registry addresses are assigned.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private async Task<WotDocumentSet> ConvertFileAsync(
            UANodeSet nodeSet,
            IWotNodeResolver nodeResolver,
            CancellationToken cancellationToken,
            bool reportDiagnostics = true)
        {
            var options = new WotNodeSetConverterOptions
            {
                UnitMappingProvider = unitMappingProvider,
                QuantityKindMappingProvider = quantityKindMappingProvider,
                MaxDocumentSetDocuments = 100_000,
                MaxDocumentSetDepth = 128,
                MaxDocumentSetBytes = 512L * 1024 * 1024,
                MaxResolverDocuments = 100_000,
                MaxResolverDepth = 128,
                MaxResolverTotalBytes = 512L * 1024 * 1024
            };
            if (reportDiagnostics)
            {
                await m_output.WriteLineAsync(
                    "  Convert the NodeSet. Check source preservation.".AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                nodeSet, "model.TM.json", options: options, nodeResolver: nodeResolver,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            WotDocumentSet? documents = result.Value;
            try
            {
                if (reportDiagnostics || !result.Success)
                {
                    foreach (WotDiagnostic diagnostic in result.Diagnostics)
                    {
                        TextWriter writer = diagnostic.Severity == WotDiagnosticSeverity.Info ? m_output : m_error;
                        await writer.WriteLineAsync(
                            $"  {diagnostic.Severity.ToString().ToUpperInvariant()}: {diagnostic}".AsMemory(),
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (!result.Success || documents is null)
                {
                    throw new FormatException(
                        "The converter could not preserve this NodeSet. No documents were written.");
                }
                return documents;
            }
            catch
            {
                documents?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Writes registry-linked models and support documents with their local file names.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private async Task<int> ExportFileAsync(
            WotDocumentSet documents,
            string destination,
            RegistryModelReferences references,
            CancellationToken cancellationToken)
        {
            int models = 0;
            for (int i = 0; i < documents.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            ArrayOf<ReadOnlyMemory<byte>> output = references.Write(documents, cancellationToken);
            foreach (string diagnostic in references.Diagnostics.Distinct(StringComparer.Ordinal))
            {
                await m_error.WriteLineAsync($"  WARNING: {diagnostic}".AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
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
                    await File.WriteAllBytesAsync(temporary, output[i], cancellationToken)
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
                    ($"  WARNING: Keep all {documents.Entries.Count - models} support documents. " +
                        "They are not Thing Models. Their file names are link targets, not type claims.").AsMemory(),
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            if (models == 0)
            {
                await m_error.WriteLineAsync(
                    "  WARNING: This NodeSet has no Thing Model in the converter output.".AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            await m_output.WriteLineAsync(
                $"  Wrote {models} Thing Models and {documents.Entries.Count - models} support files.".AsMemory(),
                cancellationToken)
                .ConfigureAwait(false);
            return documents.Entries.Count;
        }

        /// <summary>
        /// Allocates catalogue identifiers from native namespaces and BrowseNames.
        /// Duplicate names retain the source-order suffixes used by the baseline catalogue.
        /// </summary>
        private static Dictionary<ExpandedNodeId, string> CreateModelIds(
            UANodeSet nodeSet, CancellationToken cancellationToken)
        {
            var nodes = new List<(ExpandedNodeId Identity, string Id)>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(node.NodeId) || string.IsNullOrEmpty(node.BrowseName))
                {
                    continue;
                }
                var nativeId = NodeId.Parse(node.NodeId);
                string namespaceUri = nativeId.NamespaceIndex == 0
                    ? Types.Namespaces.OpcUa
                    : nodeSet.NamespaceUris?[nativeId.NamespaceIndex - 1] ??
                        throw new FormatException($"No namespace URI for native NodeId '{node.NodeId}'.");
                string name = QualifiedName.Parse(node.BrowseName).Name!;
                string prefix;
                if (Uri.TryCreate(namespaceUri, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0)
                {
                    prefix = "urn:" + string.Join('.', uri.Host.Split('.').Reverse());
                    string path = uri.AbsolutePath.Trim('/').Replace('/', '.');
                    if (path.Length > 0)
                    {
                        prefix += "." + path;
                    }
                }
                else
                {
                    prefix = namespaceUri.StartsWith("urn:", StringComparison.Ordinal)
                        ? namespaceUri.TrimEnd(':')
                        : "urn:" + Uri.EscapeDataString(namespaceUri);
                }
                string id = prefix + "." + Uri.EscapeDataString(name);
                nodes.Add((new ExpandedNodeId(nativeId.WithNamespaceIndex(0), namespaceUri), id));
                counts.TryGetValue(id, out int count);
                counts[id] = count + 1;
            }
            var used = new HashSet<string>(counts.Keys, StringComparer.Ordinal);
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new Dictionary<ExpandedNodeId, string>();
            foreach ((ExpandedNodeId identity, string baseId) in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = baseId;
                if (counts[baseId] > 1)
                {
                    ordinals.TryGetValue(baseId, out int ordinal);
                    do
                    {
                        ordinal++;
                        id = baseId + "_" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    while (!used.Add(id));
                    ordinals[baseId] = ordinal;
                }
                result.Add(identity, id);
            }
            return result;
        }

        /// <summary>
        /// Finds input files without entering output folders or directory links.
        /// </summary>
        internal static IEnumerable<string> FindNodeSets(
            string source, string destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string file in Directory.EnumerateFiles(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsNodeSet(file))
                {
                    yield return file;
                }
            }
            foreach (string folder in Directory.EnumerateDirectories(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsWithin(folder, destination) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                {
                    foreach (string file in FindNodeSets(folder, destination, cancellationToken))
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
                or FormatException or XmlException or JsonException or InvalidOperationException or NotSupportedException;
        }

        /// <summary>Receives progress and completion messages.</summary>
        private readonly TextWriter m_output = output ?? throw new ArgumentNullException(nameof(output));

        /// <summary>Receives warnings and errors.</summary>
        private readonly TextWriter m_error = error ?? throw new ArgumentNullException(nameof(error));

        /// <summary>Reads environment configuration without requiring process-wide state in tests.</summary>
        private readonly Func<string, string?> m_getEnvironmentVariable =
            getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }
}
