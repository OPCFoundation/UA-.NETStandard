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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace AggregationServer
{
    /// <summary>
    /// Converts linked native documents from a captured registry image and delegates synthesized documents
    /// to the standard NodeSet converter without exposing native readable summaries as new type declarations.
    /// </summary>
    /// <param name="options">The WoT node set converter options.</param>
    /// <param name="telemetry">The telemetry context used to create the logger.</param>
    internal sealed class LinkedNativeDocumentConverter(WotNodeSetConverterOptions options, ITelemetryContext telemetry)
        : IWotDocumentConverter, IServerStartupTask, IDisposable
    {
        /// <summary>
        /// Connects the fallback converter to the running server's address space.
        /// </summary>
        /// <param name="server">The server context.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (server is not IServerInternal internalServer)
            {
                throw new NotSupportedException(
                    "The linked sample converter requires an IServerInternal AddressSpace context.");
            }
            lock (m_lifetimeLock)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                m_fallback.AddressSpace = new AddressSpaceWotNodeResolver(internalServer);
            }
            return default;
        }

        /// <summary>
        /// Converts a resource using captured native documents or the fallback synthesized conversion path.
        /// </summary>
        /// <param name="resource">The WoT resource.</param>
        /// <param name="content">The byte string.</param>
        /// <param name="snapshot">The WoT registry snapshot.</param>
        /// <param name="contents">The captured document contents and any available type-definition context.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<WotConversionOutput> ConvertAsync(
            WotResource resource,
            ByteString content,
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            CancellationToken cancellationToken)
        {
            lock (m_lifetimeLock)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                m_users++;
            }
            try
            {
                using WotDocument document = WotDocument.Parse(content.Memory, options);
                if (!document.TryGetNativeProjection(out JsonElement native))
                {
                    IReadOnlyDictionary<string, ByteString> declarations =
                        contents is IWotDocumentConversionContext context
                            ? new ReadableDefinitions(contents, context) : contents;
                    return await m_fallback.ConvertAsync(resource, content, snapshot, declarations, cancellationToken)
                        .ConfigureAwait(false);
                }
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    CaptureDocuments(snapshot, contents, cancellationToken);
                    if (!m_documents.Any(candidate => candidate.Utf8Json.Span.SequenceEqual(document.Utf8Json.Span)))
                    {
                        return WotConversionOutput.Failure(
                            "The exact target document content is missing or mismatched in the captured image.");
                    }
                    return await ConvertNativeAsync(document, native, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    m_gate.Release();
                }
            }
            catch (Exception ex) when (
                ex is JsonException or FormatException or ArgumentException or InvalidOperationException or IOException)
            {
                m_logger.ConversionFailed();
                return WotConversionOutput.Failure(ex.Message);
            }
            finally
            {
                lock (m_lifetimeLock)
                {
                    m_users--;
                    if (m_disposed && m_users == 0)
                    {
                        ClearDocuments();
                        m_gate.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Marks the converter disposed and clears captured documents immediately when no conversion is active.
        /// </summary>
        public void Dispose()
        {
            lock (m_lifetimeLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                if (m_users == 0)
                {
                    ClearDocuments();
                    m_gate.Dispose();
                }
            }
        }

        private async ValueTask<WotConversionOutput> ConvertNativeAsync(
            WotDocument document,
            JsonElement native,
            CancellationToken cancellationToken)
        {
            ExpandedNodeId root = ReadRoot(document);
            string namespaceUri = root.NamespaceUri ??
                throw new FormatException("A linked native document must have a portable root namespace.");
            if (!m_validated.TryGetValue(namespaceUri, out UANodeSet? verifiedContext))
            {
                WotDocument[] siblings = m_documents
                    .Where(candidate => TryReadRoot(candidate, out ExpandedNodeId candidateRoot) &&
                        candidateRoot.NamespaceUri == namespaceUri).ToArray();
                var entries = new List<WotDocumentSetEntry>(siblings.Length);
                try
                {
                    foreach (WotDocument sibling in siblings)
                    {
                        string href = sibling.RootElement.GetProperty("id").GetString() ??
                            throw new FormatException("A published linked document must have an identity.");
                        entries.Add(new WotDocumentSetEntry(
                            href, WotDocument.Parse(sibling.Utf8Json, options)));
                    }
                    // RootHref identifies one parent-free document, not a filter over Entries.
                    // Keep every partition, including independent roots, and choose stably.
                    string? rootHref = entries.Where(entry => !HasDocumentParent(entry.Document))
                        .Select(entry => entry.Href).OrderBy(href => href, StringComparer.Ordinal).FirstOrDefault();
                    if (rootHref is null)
                    {
                        return WotConversionOutput.Failure(
                            "The linked native document set has no parent-free root.");
                    }
                    using var set = new WotDocumentSet(rootHref, entries.ToArrayOf());
                    entries.Clear();
                    IWotNodeResolver? resolver = siblings.All(WotNodeSetConverter.TakesRestorePath)
                        ? null : new WotDocumentNodeResolver(m_documents.Where(candidate =>
                            TryReadRoot(candidate, out ExpandedNodeId candidateRoot) &&
                            candidateRoot.NamespaceUri != namespaceUri));
                    WotConversionResult<UANodeSet> verified = await WotNodeSetConverter.ToNodeSetAsync(
                        set, options, resolver, cancellationToken).ConfigureAwait(false);
                    if (!verified.Success)
                    {
                        return WotConversionOutput.Failure(verified.Diagnostics
                            .Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                            .Select(diagnostic => diagnostic.ToString()).ToArray());
                    }
                    verifiedContext = verified.Value!;
                    m_validated.Add(namespaceUri, verifiedContext);
                }
                finally
                {
                    foreach (WotDocumentSetEntry entry in entries)
                    {
                        entry.Dispose();
                    }
                }
            }

            // Full-set validation above checks every readable claim against its owning partition.
            // Extract only this partition; importing the merged set here would duplicate sibling owners.
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("uav:nodes");
                native.WriteTo(writer);
                writer.WriteEndObject();
            }
            using WotDocument partition = WotDocument.Parse(output.ToArray(), options);
            WotConversionResult<UANodeSet> extracted = WotNodeSetConverter.ToNodeSetResult(partition, options);
            if (!extracted.Success || extracted.Value is null)
            {
                return WotConversionOutput.Failure(extracted.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()).ToArray());
            }
            ArrayOf<WotProjectedAffordance> affordances = WotNodeSetConverter
                .ResolveAffordanceNodes(document, document.Events.Count == 0 ? extracted.Value : verifiedContext, root)
                .ConvertAll(WotProjectedAffordance.FromConverted);
            return new WotConversionOutput(extracted.Value, [], root).WithProjectedAffordances(affordances);
        }

        private void CaptureDocuments(
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            CancellationToken cancellationToken)
        {
            KeyValuePair<string, ByteString>[] captured = contents.ToArray();
            var image = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ByteString> entry in captured)
            {
                cancellationToken.ThrowIfCancellationRequested();
                image.Add(entry.Key, WotContentDigest.Compute(entry.Value));
            }
            if (ReferenceEquals(m_snapshot, snapshot) && m_image is not null &&
                image.Count == m_image.Count && image.All(entry =>
                    m_image.TryGetValue(entry.Key, out ByteString digest) && digest == entry.Value))
            {
                return;
            }

            var documents = new List<WotDocument>(captured.Length);
            try
            {
                foreach (KeyValuePair<string, ByteString> entry in captured)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WotDocument? parsed = null;
                    try
                    {
                        parsed = WotDocument.Parse(entry.Value.Memory, options);
                        if (!WotProjection.IsProjection(parsed))
                        {
                            documents.Add(parsed);
                            parsed = null;
                        }
                    }
                    catch (Exception ex) when (
                        ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
                    {
                        // A malformed sibling is not evidence. Any dependent claim must still
                        // pass whole-set validation; unrelated resources can continue.
                        m_logger.SiblingRejected();
                    }
                    finally
                    {
                        parsed?.Dispose();
                    }
                }
                ClearDocuments();
                m_documents.AddRange(documents);
                documents.Clear();
                m_snapshot = snapshot;
                m_image = image;
            }
            finally
            {
                foreach (WotDocument document in documents)
                {
                    document.Dispose();
                }
            }
        }

        private static ExpandedNodeId ReadRoot(WotDocument document)
        {
            if (!TryReadRoot(document, out ExpandedNodeId root))
            {
                throw new FormatException("A linked document must declare its portable root NodeId.");
            }
            return root;
        }

        private static bool TryReadRoot(WotDocument document, out ExpandedNodeId root)
        {
            root = default;
            return document.TryGetUav("id", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() is { } identity &&
                ExpandedNodeId.TryParse(identity, out root) && !root.IsNull;
        }

        private static bool HasDocumentParent(WotDocument document)
        {
            return document.RootElement.TryGetProperty("links", out JsonElement links) &&
                links.ValueKind == JsonValueKind.Array && links.EnumerateArray().Any(link =>
                    link.TryGetProperty("rel", out JsonElement relation) &&
                    relation.GetString() == "uav:componentOf");
        }

        private void ClearDocuments()
        {
            foreach (WotDocument document in m_documents)
            {
                document.Dispose();
            }
            m_documents.Clear();
            m_validated.Clear();
            m_snapshot = null;
            m_image = null;
        }

        private sealed class ReadableDefinitions(
            IReadOnlyDictionary<string, ByteString> contents,
            IWotDocumentConversionContext context)
            : IReadOnlyDictionary<string, ByteString>, IWotDocumentConversionContext
        {
            /// <summary>
            /// Gets the captured content associated with a resource key.
            /// </summary>
            /// <param name="key">The resource key to look up in the captured contents.</param>
            public ByteString this[string key] => contents[key];
            /// <summary>
            /// Gets the keys for <c>ReadableDefinitions</c>.
            /// </summary>
            public IEnumerable<string> Keys => contents.Keys;
            /// <summary>
            /// Gets the captured document content values.
            /// </summary>
            public IEnumerable<ByteString> Values => contents.Values;
            /// <summary>
            /// Gets the count for <c>ReadableDefinitions</c>.
            /// </summary>
            public int Count => contents.Count;

            /// <summary>
            /// Gets synthesized DataType definition sources while excluding documents on the native restore path.
            /// </summary>
            /// <param name="options">The WoT node set converter options.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ArrayOf<WotDataTypeDefinitionSource> GetDataTypeDefinitions(
                WotNodeSetConverterOptions options, CancellationToken cancellationToken = default)
            {
                // Native types remain owned and validated by their complete preserved set.
                // Their readable summaries are not declarations for this synthesized owner.
                return context.GetDataTypeDefinitions(options, cancellationToken).ToList()
                    .Where(source => !WotNodeSetConverter.TakesRestorePath(source.Document)).ToArrayOf();
            }

            /// <summary>
            /// Determines whether the captured contents contain the resource key.
            /// </summary>
            /// <param name="key">The resource key to look up.</param>
            public bool ContainsKey(string key)
            {
                return contents.ContainsKey(key);
            }

            /// <summary>
            /// Looks up the captured document content for a resource key.
            /// </summary>
            /// <param name="key">The resource key to look up.</param>
            /// <param name="value">The captured content returned when the lookup succeeds.</param>
            public bool TryGetValue(string key, out ByteString value)
            {
                return contents.TryGetValue(key, out value);
            }

            /// <summary>
            /// Enumerates the captured resource keys and their document contents.
            /// </summary>
            public IEnumerator<KeyValuePair<string, ByteString>> GetEnumerator()
            {
                return contents.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private readonly WotNodeSetDocumentConverter m_fallback = new(options);
        private readonly ILogger m_logger = telemetry.CreateLogger<LinkedNativeDocumentConverter>();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly List<WotDocument> m_documents = [];
        private readonly Dictionary<string, UANodeSet> m_validated = new(StringComparer.Ordinal);
        private readonly System.Threading.Lock m_lifetimeLock = new();
        private WotRegistrySnapshot? m_snapshot;
        private Dictionary<string, ByteString>? m_image;
        private int m_users;
        private bool m_disposed;
    }

    /// <summary>
    /// Contains the source-generated log-event declarations for <c>LinkedNativeDocumentConverterLog</c>.
    /// </summary>
    internal static partial class LinkedNativeDocumentConverterLog
    {
        /// <summary>
        /// Writes the log event: <c>A malformed captured sibling was excluded from linked conversion evidence.</c>.
        /// </summary>
        /// <param name="logger">The logger receiving this event.</param>
        [LoggerMessage(EventId = AggregationServerEventIds.LinkedNativeDocumentConverter,
            Level = LogLevel.Warning, Message = "A malformed captured sibling was excluded from linked conversion evidence.")]
        public static partial void SiblingRejected(this ILogger logger);

        /// <summary>
        /// Writes the log event: <c>Linked document conversion rejected an invalid captured document.</c>.
        /// </summary>
        /// <param name="logger">The logger receiving this event.</param>
        [LoggerMessage(EventId = AggregationServerEventIds.LinkedNativeDocumentConverter + 1,
            Level = LogLevel.Warning, Message = "Linked document conversion rejected an invalid captured document.")]
        public static partial void ConversionFailed(this ILogger logger);
    }
}
