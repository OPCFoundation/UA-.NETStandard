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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Export;

namespace UaLens.NodeSets.Loading
{
    /// <summary>
    /// Discovers exact ModelUri metadata in OPCFoundation/UA-Nodeset's latest branch.
    /// The catalog is pinned to one tree revision. No namespace URI is used as a network address.
    /// </summary>
    internal sealed class UaNodeSetRepository : INodeSetRepository, IDisposable
    {
        /// <summary>
        /// Uses the supplied client without taking ownership. Injected clients should disable automatic redirects.
        /// The default client disables redirects and has a 60-second request timeout.
        /// </summary>
        public UaNodeSetRepository(HttpClient? httpClient = null)
        {
            m_ownsClient = httpClient is null;
            if (httpClient is null)
            {
                m_ownedHandler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CheckCertificateRevocationList = true
                };
                m_client = new HttpClient(m_ownedHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
            }
            else
            {
                m_client = httpClient;
            }
        }

        public async Task<NodeSetDocument?> FindAsync(
            NodeSetRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            ArgumentException.ThrowIfNullOrWhiteSpace(requirement.ModelUri);
            ObjectDisposedException.ThrowIf(m_disposed, this);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_catalog ??= await ReadCatalogAsync(cancellationToken).ConfigureAwait(false);
                foreach (Candidate candidate in m_catalog)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!candidate.HasMetadata)
                    {
                        using MemoryStream? header = await DownloadAsync(
                            candidate.Url, NodeSetDocumentReader.MaxHeaderBytes, true, cancellationToken)
                            .ConfigureAwait(false);
                        if (header is null)
                        {
                            candidate.Models = [];
                            candidate.HasMetadata = true;
                            continue;
                        }
                        m_headerBytes += header.Length;
                        if (m_headerBytes > MaxCatalogHeaderBytes)
                        {
                            throw new InvalidDataException(
                                $"Repository discovery exceeds the {MaxCatalogHeaderBytes}-byte metadata limit.");
                        }
                        try
                        {
                            candidate.Models = await Task.Run(
                                () => NodeSetMetadata.ReadHeader(header), cancellationToken).ConfigureAwait(false);
                            if (candidate.Models.Count == 0)
                            {
                                throw new InvalidDataException(
                                    $"Repository NodeSet2 '{candidate.Url}' contains no model metadata.");
                            }
                        }
                        catch (XmlException exception)
                        {
                            string reason = header.Length == NodeSetDocumentReader.MaxHeaderBytes ?
                                $"metadata exceeds the {NodeSetDocumentReader.MaxHeaderBytes}-byte header limit" :
                                "metadata is malformed";
                            throw new InvalidDataException(
                                $"Repository NodeSet2 '{candidate.Url}' {reason}.", exception);
                        }
                        candidate.HasMetadata = true;
                    }
                    if (!(candidate.Models.ToArray() ?? []).Any(model => NodeSetMetadata.Satisfies(model, requirement)))
                    {
                        continue;
                    }
                    if (!m_documents.TryGetValue(candidate.Url, out NodeSetDocument? document))
                    {
                        using MemoryStream? content = await DownloadAsync(
                            candidate.Url, NodeSetDocumentReader.MaxDocumentBytes, false, cancellationToken)
                            .ConfigureAwait(false) ??
                            throw new HttpRequestException(
                                $"The cataloged NodeSet2 '{candidate.Url}' is no longer available.",
                                null, HttpStatusCode.NotFound);
                        if (m_documentBytes + content.Length > MaxCachedDocumentBytes)
                        {
                            throw new InvalidDataException(
                                $"Repository downloads exceed the {MaxCachedDocumentBytes}-byte cache limit.");
                        }
                        document = await NodeSetDocumentReader.ReadAsync(
                            content, candidate.Url, cancellationToken).ConfigureAwait(false);
                        NodeSetMetadata.Verify(document, requirement);
                        m_documents.Add(candidate.Url, document);
                        m_documentBytes += content.Length;
                    }
                    NodeSetMetadata.Verify(document, requirement);
                    return document;
                }
                return null;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("The approved NodeSet2 repository download timed out.", exception);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (m_ownsClient)
            {
                m_client.Dispose();
            }
            m_ownedHandler?.Dispose();
            m_gate.Dispose();
        }

        private async Task<List<Candidate>> ReadCatalogAsync(CancellationToken cancellationToken)
        {
            using MemoryStream content = await DownloadAsync(TreeUrl, MaxTreeBytes, false, cancellationToken)
                .ConfigureAwait(false) ??
                throw new HttpRequestException("The approved UA-Nodeset repository tree was not found.", null,
                    HttpStatusCode.NotFound);
            using JsonDocument json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            JsonElement root = json.RootElement;
            if (root.GetProperty("truncated").GetBoolean())
            {
                throw new InvalidDataException("GitHub returned a truncated UA-Nodeset tree; discovery is incomplete.");
            }
            string revision = root.GetProperty("sha").GetString() ??
                throw new InvalidDataException("GitHub returned a tree without a revision.");
            if (revision.Length != 40 || revision.Any(static ch => !char.IsAsciiHexDigit(ch)))
            {
                throw new InvalidDataException("GitHub returned an invalid repository revision.");
            }
            var candidates = new List<Candidate>();
            foreach (JsonElement item in root.GetProperty("tree").EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? path = item.GetProperty("path").GetString();
                if (item.GetProperty("type").GetString() != "blob" ||
                    path is null ||
                    !path.EndsWith(".NodeSet2.xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string[] segments = path.Split('/');
                if (segments.Any(static segment => segment.Length == 0 ||
                    segment is "." or ".." ||
                    segment.Contains('\\', StringComparison.Ordinal)))
                {
                    throw new InvalidDataException($"GitHub returned an invalid NodeSet2 path '{path}'.");
                }
                if (candidates.Count >= MaxCatalogFiles)
                {
                    throw new InvalidDataException($"The repository exceeds the {MaxCatalogFiles}-file catalog limit.");
                }
                string encodedPath = string.Join('/', segments.Select(Uri.EscapeDataString));
                candidates.Add(new Candidate($"{RawRoot}/{revision}/{encodedPath}"));
            }
            candidates.Sort(static (left, right) => string.CompareOrdinal(left.Url, right.Url));
            return candidates;
        }

        private async Task<MemoryStream?> DownloadAsync(
            string url,
            int maximum,
            bool headerOnly,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            CancellationToken requestCancellation = timeout.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("UaLens-NodeSetLoader/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                url == TreeUrl ? "application/vnd.github+json" : "application/xml"));
            if (url == TreeUrl)
            {
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            }
            if (headerOnly)
            {
                request.Headers.Range = new RangeHeaderValue(0, maximum - 1);
            }
            using HttpResponseMessage response = await m_client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, requestCancellation).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } effective && effective != request.RequestUri)
            {
                throw new HttpRequestException(
                    "Redirects are not permitted when downloading approved NodeSet2 models.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            if (!headerOnly && response.StatusCode == HttpStatusCode.PartialContent)
            {
                throw new HttpRequestException($"GitHub returned an incomplete document for '{url}'.");
            }
            if (!headerOnly && response.Content.Headers.ContentLength > maximum)
            {
                throw new InvalidDataException($"'{url}' exceeds the {maximum}-byte input limit.");
            }
            Stream stream = await response.Content.ReadAsStreamAsync(requestCancellation).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await NodeSetDocumentReader.ReadBoundedAsync(
                    stream, maximum, url, headerOnly, requestCancellation).ConfigureAwait(false);
            }
        }

        private sealed class Candidate(string url)
        {
            public string Url { get; } = url;
            public ArrayOf<ModelTableEntry> Models { get; set; }
            public bool HasMetadata { get; set; }
        }

        internal const string TreeUrl =
            "https://api.github.com/repos/OPCFoundation/UA-Nodeset/git/trees/latest?recursive=1";

        internal const string RawRoot = "https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset";
        internal const int MaxTreeBytes = 8 * 1024 * 1024;
        internal const int MaxCatalogFiles = 512;
        internal const int MaxCatalogHeaderBytes = 64 * 1024 * 1024;
        internal const int MaxCachedDocumentBytes = 256 * 1024 * 1024;
        private readonly HttpClient m_client;
        private readonly HttpClientHandler? m_ownedHandler;
        private readonly bool m_ownsClient;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, NodeSetDocument> m_documents = new(StringComparer.Ordinal);
        private List<Candidate>? m_catalog;
        private long m_headerBytes;
        private long m_documentBytes;
        private bool m_disposed;
    }
}
