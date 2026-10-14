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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Owns one bounded conversion, its documents and its local-first resolver.
    /// </summary>
    internal sealed class WotDocumentScope : IDisposable, IWotThingResolver, IWotSchemaResolver, IWotContextResolver,
        IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver
    {
        /// <summary>
        /// Captures one operation's immutable limits and deadline.
        /// </summary>
        internal WotDocumentScope(
            WotDocumentInput input, WotMcpOptions policy, OpcUaMcpOptions filesPolicy,
            McpFileTransfers files, IHttpClientFactory clients, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Source);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(filesPolicy);
            ArgumentNullException.ThrowIfNull(files);
            ArgumentNullException.ThrowIfNull(clients);
            policy = policy.Snapshot();
            if (input.Dependencies.IsNull || input.Dependencies.Count >= policy.MaxDocuments)
            {
                throw new ArgumentException("The supplied dependency set exceeds the host document limit.");
            }
            if (string.IsNullOrWhiteSpace(input.BaseUri) ||
                input.BaseUri.Length > 4096 ||
                !Uri.TryCreate(input.BaseUri, UriKind.Absolute, out Uri? baseUri) ||
                !string.IsNullOrEmpty(baseUri.UserInfo) ||
                !string.IsNullOrEmpty(baseUri.Fragment))
            {
                throw new ArgumentException("BaseUri must be a credential-free absolute document address.");
            }
            if (filesPolicy.MaxTransferBytes <= 0)
            {
                throw new ArgumentException("The host transfer limit must be positive.");
            }
            m_input = input;
            m_policy = policy;
            m_files = files;
            m_clients = clients;
            m_baseUri = baseUri;
            MaxTotalBytes = Math.Min(policy.MaxTotalBytes, filesPolicy.MaxTransferBytes);
            if (input.RequiredConformance.Count > 64 ||
                (!input.StrictAuthoring && !input.RequiredConformance.IsEmpty))
            {
                throw new ArgumentException(
                    "RequiredConformance is limited to 64 claims and requires StrictAuthoring.");
            }
            Options = new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = (int)Math.Min(policy.MaxDocumentBytes, MaxTotalBytes),
                MaxNodeSetSize = (int)Math.Min(64L * 1024 * 1024, MaxTotalBytes),
                MaxResolverDepth = policy.MaxResolverDepth,
                MaxResolverDocuments = policy.MaxDocuments,
                MaxResolverDocumentBytes = (int)Math.Min(policy.MaxDocumentBytes, MaxTotalBytes),
                MaxResolverTotalBytes = MaxTotalBytes,
                MaxDocumentSetDocuments = policy.MaxDocuments,
                MaxDocumentSetDepth = policy.MaxResolverDepth,
                MaxDocumentSetBytes = MaxTotalBytes,
                ConformanceMode = input.StrictAuthoring ? WotConformanceMode.Strict : WotConformanceMode.Permissive,
                AuthoringValidation = input.StrictAuthoring,
                RequiredConformance = input.RequiredConformance,
                PreservationMode = input.PreservationMode
            };
            Options.Validate();
            m_timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            m_timeout.CancelAfter(policy.OperationTimeoutMilliseconds);
        }

        /// <summary>
        /// Gets the token covering input I/O, every dependency request and conversion.
        /// </summary>
        internal CancellationToken Token => m_timeout.Token;

        /// <summary>
        /// Gets the bounded converter options.
        /// </summary>
        internal WotNodeSetConverterOptions Options { get; }

        /// <summary>
        /// Gets the stricter host/transfer ceiling applied to complete generated document sets.
        /// </summary>
        internal long MaxTotalBytes { get; }

        /// <summary>
        /// Gets the original primary input bytes.
        /// </summary>
        internal ByteString SourceBytes { get; private set; }

        /// <summary>
        /// Gets the complete primary document set; dependencies are resolver context, not silently added roots.
        /// </summary>
        internal WotDocumentSet Documents { get; private set; } = null!;

        /// <summary>
        /// Loads only explicit input files. Linked URLs are consulted later only through the approved resolver.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal async ValueTask InitializeAsync(bool nodeSetInput)
        {
            Token.ThrowIfCancellationRequested();
            if (nodeSetInput && m_input.DocumentSetArchive)
            {
                throw new ArgumentException("A NodeSet input cannot be a WoT document-set archive.");
            }
            long sourceLimit = nodeSetInput || m_input.DocumentSetArchive
                ? Options.MaxNodeSetSize : Options.MaxJsonDocumentSize;
            string rootReference = string.IsNullOrWhiteSpace(m_input.Source.Reference)
                ? m_baseUri.AbsoluteUri : Key(m_input.Source.Reference);
            m_sourceReference = rootReference;
            m_sourceContentType = nodeSetInput ? "application/xml" :
                m_input.DocumentSetArchive ? "application/zip" : m_input.Source.ContentType;
            m_nonJsonDocuments = nodeSetInput ? 1 : 0;
            SourceBytes = await ReadSourceAsync(m_input.Source, sourceLimit).ConfigureAwait(false);
            if (m_input.DocumentSetArchive)
            {
                Documents = await WotDocumentArchive.ReadAsync(SourceBytes, this).ConfigureAwait(false);
            }
            else if (!nodeSetInput)
            {
                WotDocument document = AddDocument(rootReference, SourceBytes, m_input.Source.ContentType, "supplied");
                RequireObjectRoot(document);
                m_primaryEntry = new WotDocumentSetEntry(rootReference, document);
                Documents = new WotDocumentSet(rootReference, [m_primaryEntry]);
            }
            for (int index = 0; index < m_input.Dependencies.Count; index++)
            {
                WotDocumentSource dependency = m_input.Dependencies[index] ??
                    throw new ArgumentException("A dependency entry cannot be null.");
                ArgumentException.ThrowIfNullOrWhiteSpace(dependency.Reference);
                ByteString bytes = await ReadSourceAsync(dependency, Options.MaxJsonDocumentSize).ConfigureAwait(false);
                _ = AddDocument(Key(dependency.Reference), bytes, dependency.ContentType, "supplied");
            }
        }

        /// <summary>
        /// Imports one archive member within the same decompressed-content budget.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        /// <exception cref="IOException"></exception>
        internal async ValueTask<WotDocument> ImportArchiveDocumentAsync(
            string reference, Stream content, long declaredLength)
        {
            string key = Key(reference);
            if (m_cache.ContainsKey(key))
            {
                throw new FormatException("Archive documents must have distinct normalized references.");
            }
            if (declaredLength > Options.MaxJsonDocumentSize || declaredLength > RemainingBytes)
            {
                throw new IOException("An archive member exceeds the remaining document budget.");
            }
            ByteString bytes = await ReadArchiveContentAsync(content, Options.MaxJsonDocumentSize)
                .ConfigureAwait(false);
            if (bytes.Length != declaredLength)
            {
                throw new FormatException("An archive member does not match its declared decompressed length.");
            }
            WotDocument document = AddDocument(key, bytes, "application/json", "archive");
            RequireObjectRoot(document);
            return document;
        }

        /// <summary>
        /// Counts every decompressed archive byte, including the manifest, against the operation budget.
        /// </summary>
        internal async ValueTask<ByteString> ReadArchiveContentAsync(Stream content, long limit)
        {
            ByteString bytes = await ReadBoundedAsync(content, Math.Min(limit, RemainingBytes), Token)
                .ConfigureAwait(false);
            m_totalBytes += bytes.Length;
            return bytes;
        }

        /// <summary>
        /// Gets bounded, credential-free provenance without exposing document bodies.
        /// </summary>
        internal JsonObject Provenance()
        {
            var documents = new JsonArray();
            foreach ((string reference, ResolvedDocument document) in m_cache)
            {
                JsonNode entry = new JsonObject
                {
                    ["reference"] = WotDocumentJson.Reference(reference),
                    ["source"] = document.Source,
                    ["bytes"] = document.Content.Length,
                    ["contentType"] = document.ContentType,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(document.Content.Span))
                };
                documents.Add(entry);
            }
            return new JsonObject
            {
                ["primarySource"] = new JsonObject
                {
                    ["reference"] = WotDocumentJson.Reference(m_sourceReference),
                    ["bytes"] = SourceBytes.Length,
                    ["contentType"] = m_sourceContentType,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(SourceBytes.Span))
                },
                ["documents"] = documents,
                ["totalInputBytes"] = m_totalBytes,
                ["dependencyRequests"] = m_requested.Count,
                ["missingDependencies"] = m_missing.Count,
                ["remoteResolutionRequested"] = m_input.ResolveRemoteDependencies
            };
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveThingAsync(
            string reference, WotResolutionContext context, CancellationToken cancellationToken)
        {
            return ResolveAsync(reference, context, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveSchemaAsync(
            string reference, WotResolutionContext context, CancellationToken cancellationToken)
        {
            return ResolveAsync(reference, context, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolverResult> ResolveContextAsync(
            string reference, WotResolutionContext context, CancellationToken cancellationToken)
        {
            return ResolveAsync(reference, context, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            return Nodes.HoldsNamespaceAsync(namespaceUri, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri, string browseName, WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            return Nodes.ResolveByBrowseNameAsync(namespaceUri, browseName, expected, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId, CancellationToken cancellationToken = default)
        {
            return Nodes.ResolveByNodeIdAsync(expandedNodeId, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri, string name, CancellationToken cancellationToken = default)
        {
            return Nodes.ResolveReferenceTypesAsync(namespaceUri, name, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId, WotDeclarationScope scope, CancellationToken cancellationToken = default)
        {
            return Nodes.ResolveDeclarationsAsync(typeNodeId, scope, cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_primaryEntry?.Dispose();
            foreach (WotDocument document in m_documents)
            {
                document.Dispose();
            }
            m_client?.Dispose();
            m_timeout.Dispose();
        }

        /// <summary>
        /// Copies bounded decompressed bytes before allowing an allocation to grow beyond the requested limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="IOException"></exception>
        internal static async ValueTask<ByteString> ReadBoundedAsync(Stream source, long limit, CancellationToken ct)
        {
            if (limit is < 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            ct.ThrowIfCancellationRequested();
            using var buffer = new McpBoundedMemoryStream(limit, ct);
            byte[] chunk = new byte[8192];
            while (true)
            {
                int requested = (int)Math.Min(chunk.Length, limit - buffer.Length + 1);
                int read = await source.ReadAsync(chunk.AsMemory(0, requested), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return ByteString.From(buffer.ToArray());
                }
                if (read > limit - buffer.Length)
                {
                    throw new IOException("The document exceeds its configured byte budget.");
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rebuilds the existing node/declaration resolver only after another dependency becomes available.
        /// </summary>
        private WotDocumentNodeResolver Nodes
        {
            get
            {
                if (m_nodes is null || m_indexedCount != m_documents.Count)
                {
                    var objects = new List<WotDocument>();
                    foreach (WotDocument document in m_documents)
                    {
                        if (document.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            objects.Add(document);
                        }
                    }
                    m_nodes = new WotDocumentNodeResolver(objects);
                    m_indexedCount = m_documents.Count;
                }
                return m_nodes;
            }
        }

        /// <summary>
        /// Gets the unconsumed content budget.
        /// </summary>
        private long RemainingBytes => MaxTotalBytes - m_totalBytes;

        /// <summary>
        /// Reads exactly one supplied source without allowing arbitrary paths.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="FormatException"></exception>
        private async ValueTask<ByteString> ReadSourceAsync(WotDocumentSource source, long limit)
        {
            Token.ThrowIfCancellationRequested();
            if ((source.Text is null) == (source.FilePath is null))
            {
                throw new ArgumentException("Provide exactly one of text or filePath.");
            }
            limit = Math.Min(limit, RemainingBytes);
            ByteString bytes;
            if (source.Text is not null)
            {
                if (source.Text.Length > 65_536 || source.Text.Length > limit)
                {
                    throw new ArgumentException("Inline text exceeds 64 KiB or the remaining document budget.");
                }
                var utf8 = new UTF8Encoding(false, true);
                int length = utf8.GetByteCount(source.Text);
                if (length > 65_536 || length > limit)
                {
                    throw new ArgumentException("Inline text exceeds 64 KiB or the remaining document budget.");
                }
                bytes = ByteString.From(utf8.GetBytes(source.Text));
            }
            else
            {
                ByteString loaded = default;
                await m_files.UploadAsync(source.FilePath!, async (stream, ct) =>
                {
                    if (stream.Length > limit)
                    {
                        throw new IOException("The source exceeds the remaining document budget.");
                    }
                    loaded = await ReadBoundedAsync(stream, limit, ct).ConfigureAwait(false);
                    return loaded.Length;
                }, Token).ConfigureAwait(false);
                bytes = loaded;
            }
            if (bytes.IsEmpty)
            {
                throw new FormatException("A document source cannot be empty.");
            }
            m_totalBytes += bytes.Length;
            return bytes;
        }

        /// <summary>
        /// Resolves only explicit local documents or approved HTTPS dependencies, sharing one operation budget.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="FormatException"></exception>
        private async ValueTask<WotResolverResult> ResolveAsync(
            string reference, WotResolutionContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Token.ThrowIfCancellationRequested();
            string key = ResolveKey(reference);
            if (context.Depth > m_policy.MaxResolverDepth)
            {
                throw new InvalidOperationException("The dependency depth exceeds host policy.");
            }
            if (m_cache.TryGetValue(key, out ResolvedDocument? cached))
            {
                return WotResolverResult.FromBytes(cached.Content.Memory, cached.ContentType);
            }
            if (m_missing.Contains(key))
            {
                return WotResolverResult.NotFound;
            }
            if (m_requested.Count >= m_policy.MaxDocuments ||
                m_cache.Count + m_missing.Count + m_nonJsonDocuments >= m_policy.MaxDocuments)
            {
                throw new InvalidOperationException("The dependency request or document count exceeds host policy.");
            }
            m_requested.Add(key);
            if (!m_input.ResolveRemoteDependencies)
            {
                m_missing.Add(key);
                return WotResolverResult.NotFound;
            }
            Uri uri = WotDependencyHttp.RequireAllowed(new Uri(key, UriKind.Absolute), m_policy);
            m_client ??= m_clients.CreateClient(WotDependencyHttp.ClientName);
            m_client.DefaultRequestHeaders.Clear();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.Accept.ParseAdd(
                "application/td+json, application/tm+json, application/ld+json, " +
                "application/schema+json, application/json");
            request.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Token);
            using HttpResponseMessage response = await m_client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw new HttpRequestException("Dependency redirects are not permitted.", null, response.StatusCode);
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                m_missing.Add(key);
                return WotResolverResult.NotFound;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("The dependency server refused the request.", null, response.StatusCode);
            }
            string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            RequireJsonContentType(contentType);
            long limit = Math.Min(Options.MaxJsonDocumentSize, RemainingBytes);
            if (response.Content.Headers.ContentLength is long length && length > limit)
            {
                throw new IOException("The dependency exceeds the remaining document budget.");
            }
            Stream stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable streamScope = stream.ConfigureAwait(false);
            ByteString bytes = await ReadBoundedAsync(stream, limit, linked.Token).ConfigureAwait(false);
            m_totalBytes += bytes.Length;
            if (response.Content.Headers.ContentEncoding.Count != 0)
            {
                if (response.Content.Headers.ContentEncoding.Count != 1)
                {
                    throw new FormatException("Only one supported dependency content encoding is permitted.");
                }
                string encoding = string.Concat(response.Content.Headers.ContentEncoding);
                using var compressed = new MemoryStream(bytes.ToArray(), writable: false);
                using Stream decompressed = encoding.ToLowerInvariant() switch
                {
                    "gzip" => new GZipStream(compressed, CompressionMode.Decompress),
                    "deflate" => new DeflateStream(compressed, CompressionMode.Decompress),
                    "br" => new BrotliStream(compressed, CompressionMode.Decompress),
                    _ => throw new FormatException("The dependency content encoding is not supported.")
                };
                bytes = await ReadBoundedAsync(decompressed,
                    Math.Min(Options.MaxJsonDocumentSize, RemainingBytes), linked.Token).ConfigureAwait(false);
                m_totalBytes += bytes.Length;
            }
            _ = AddDocument(key, bytes, contentType, "remote");
            return WotResolverResult.FromBytes(bytes.Memory, contentType);
        }

        /// <summary>
        /// Stores immutable document bytes and rejects conflicting content for the same declared reference.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private WotDocument AddDocument(string reference, ByteString bytes, string contentType, string source)
        {
            RequireJsonContentType(contentType);
            if (m_cache.TryGetValue(reference, out ResolvedDocument? existing))
            {
                if (!existing.Content.Span.SequenceEqual(bytes.Span))
                {
                    throw new FormatException("The same document reference was supplied with different content.");
                }
                return existing.Document;
            }
            if (m_cache.Count + m_nonJsonDocuments >= m_policy.MaxDocuments)
            {
                throw new InvalidOperationException("The document count exceeds host policy.");
            }
            var document = WotDocument.Parse(bytes.Memory, Options);
            m_documents.Add(document);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                (long)document.Properties.Count + document.Actions.Count + document.Events.Count >
                    Options.MaxAffordanceCount)
            {
                throw new FormatException("The document exceeds the affordance count limit.");
            }
            m_cache.Add(reference, new ResolvedDocument(bytes, contentType, source, document));
            IndexReferences(document.RootElement, new Uri(reference), links: false);
            return document;
        }

        /// <summary>
        /// Accepts only media types the shared external-schema resolver understands.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static void RequireJsonContentType(string contentType)
        {
            foreach (string supported in WotExternalSchemaResolver.ReadableContentTypes)
            {
                if (string.Equals(contentType, supported, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            throw new FormatException("The document did not declare a supported JSON media type.");
        }

        /// <summary>
        /// A TD/TM root is an object; dependency schemas and contexts may also use other JSON value kinds.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private static void RequireObjectRoot(WotDocument document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A WoT document must have a JSON object root.");
            }
        }

        /// <summary>
        /// Remembers the owning document of relative links without rewriting the preserved JSON.
        /// Ambiguous aliases are never resolved by selecting an arbitrary document.
        /// </summary>
        private void IndexReferences(JsonElement element, Uri owner, bool links)
        {
            Token.ThrowIfCancellationRequested();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    IndexReferences(item, owner, links);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        (property.Name is "tm:ref" or "uav:externalSchema" or "$ref" or "@context" ||
                            (links && property.Name == "href")))
                    {
                        string reference = property.Value.GetString()!;
                        if (!string.IsNullOrWhiteSpace(reference) &&
                            reference[0] != '#' &&
                            reference.Length <= 4096 &&
                            !Uri.TryCreate(reference, UriKind.Absolute, out _) &&
                            Uri.TryCreate(owner, reference, out Uri? targetUri) &&
                            string.IsNullOrEmpty(targetUri.UserInfo))
                        {
                            string alias = WithoutFragment(reference);
                            string target = new UriBuilder(targetUri) { Fragment = string.Empty }.Uri.AbsoluteUri;
                            if (m_relativeReferences.TryGetValue(alias, out string? previous) && previous != target)
                            {
                                m_relativeReferences[alias] = null;
                            }
                            else
                            {
                                m_relativeReferences[alias] = target;
                            }
                        }
                    }
                    if (!property.Name.Contains(':', StringComparison.Ordinal))
                    {
                        IndexReferences(property.Value, owner, property.Name == "links");
                    }
                }
            }
        }

        /// <summary>
        /// Uses the owner of an authored relative dependency rather than rebasing every link onto the primary input.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        private string ResolveKey(string reference)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            if (!Uri.TryCreate(reference, UriKind.Absolute, out _) &&
                m_relativeReferences.TryGetValue(WithoutFragment(reference), out string? target))
            {
                return target ??
                    throw new FormatException(
                        "A relative dependency reference is ambiguous across documents; supply absolute references.");
            }
            return Key(reference);
        }

        /// <summary>
        /// Resolves a document reference against its explicit base and strips fragments for body lookup.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="UnauthorizedAccessException"></exception>
        private string Key(string reference, Uri? owner = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            if (reference.Length > 4096 || !Uri.TryCreate(owner ?? m_baseUri, reference, out Uri? uri))
            {
                throw new ArgumentException("A dependency reference is invalid or too long.");
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new UnauthorizedAccessException("Credential-bearing document references are not permitted.");
            }
            return new UriBuilder(uri) { Fragment = string.Empty }.Uri.AbsoluteUri;
        }

        /// <summary>
        /// Removes only a JSON Pointer fragment; query strings remain part of a document's identity.
        /// </summary>
        private static string WithoutFragment(string reference)
        {
            int fragment = reference.IndexOf('#', StringComparison.Ordinal);
            return fragment < 0 ? reference : reference[..fragment];
        }

        /// <summary>
        /// A document retained for one operation, never a cross-request or cross-identity cache.
        /// </summary>
        private sealed record ResolvedDocument(
            ByteString Content, string ContentType, string Source, WotDocument Document);

        private readonly WotDocumentInput m_input;
        private readonly WotMcpOptions m_policy;
        private readonly McpFileTransfers m_files;
        private readonly IHttpClientFactory m_clients;
        private readonly Uri m_baseUri;
        private readonly CancellationTokenSource m_timeout;
        private readonly Dictionary<string, ResolvedDocument> m_cache = new(StringComparer.Ordinal);
        private readonly List<WotDocument> m_documents = [];
        private readonly HashSet<string> m_requested = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_missing = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> m_relativeReferences = new(StringComparer.Ordinal);
        private long m_totalBytes;
        private int m_nonJsonDocuments;
        private string m_sourceReference = string.Empty;
        private string m_sourceContentType = string.Empty;
        private WotDocumentNodeResolver? m_nodes;
        private int m_indexedCount;
        private HttpClient? m_client;
        private WotDocumentSetEntry? m_primaryEntry;
    }
}
