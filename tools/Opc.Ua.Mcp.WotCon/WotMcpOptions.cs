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
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Wot;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-owned policy for WoT document processing. Tool requests cannot raise these ceilings.
    /// </summary>
    public sealed class WotMcpOptions
    {
        /// <summary>
        /// Gets or sets exact HTTPS dependency origins. An empty set disables all remote retrieval.
        /// </summary>
        public ArrayOf<string> AllowedDependencyOrigins { get; set; } = [];

        /// <summary>
        /// Gets or sets host permission for private-address dependencies on allowed origins; loopback stays blocked.
        /// </summary>
        public bool AllowPrivateDependencyAddresses { get; set; }

        /// <summary>
        /// Gets or sets the maximum UTF-8 JSON document size.
        /// </summary>
        public int MaxDocumentBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum combined source/dependency content size.
        /// </summary>
        public long MaxTotalBytes { get; set; } = 64L * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum supplied, fetched, or generated document count.
        /// </summary>
        public int MaxDocuments { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum dependency-resolution or generated linked-document depth.
        /// </summary>
        public int MaxResolverDepth { get; set; } = 16;

        /// <summary>
        /// Gets or sets the whole document-operation deadline, including every dependency request.
        /// </summary>
        public int OperationTimeoutMilliseconds { get; set; } = 30_000;

        /// <summary>
        /// Gets or sets the maximum document operations admitted concurrently by this host's tool instance.
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = 2;

        /// <summary>
        /// Validates host policy before processing untrusted documents.
        /// </summary>
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxDocumentBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDocumentBytes, 16 * 1024 * 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxTotalBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTotalBytes, 128L * 1024 * 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxDocuments, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDocuments, 256);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxResolverDepth, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxResolverDepth, 16);
            ArgumentOutOfRangeException.ThrowIfLessThan(OperationTimeoutMilliseconds, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(OperationTimeoutMilliseconds, 120_000);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentOperations, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxConcurrentOperations, 32);
            foreach (string origin in AllowedDependencyOrigins)
            {
                _ = WotDependencyHttp.ParseOrigin(origin);
            }
        }

        /// <summary>
        /// Freezes the host policy for one operation or HTTP handler.
        /// </summary>
        internal WotMcpOptions Snapshot()
        {
            Validate();
            return new WotMcpOptions
            {
                AllowedDependencyOrigins = [.. AllowedDependencyOrigins],
                AllowPrivateDependencyAddresses = AllowPrivateDependencyAddresses,
                MaxDocumentBytes = MaxDocumentBytes,
                MaxTotalBytes = MaxTotalBytes,
                MaxDocuments = MaxDocuments,
                MaxResolverDepth = MaxResolverDepth,
                OperationTimeoutMilliseconds = OperationTimeoutMilliseconds,
                MaxConcurrentOperations = MaxConcurrentOperations
            };
        }
    }

    /// <summary>
    /// One explicitly supplied document, either small inline text or a root-confined file.
    /// </summary>
    public sealed class WotDocumentSource
    {
        /// <summary>
        /// Gets or sets a reference identifying this document in a local dependency set.
        /// </summary>
        public string Reference { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets UTF-8 text, limited to 64 KiB; mutually exclusive with FilePath.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Gets or sets a source file beneath the configured MCP TransferRoot.
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// Gets or sets the supplied document's media type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";
    }

    /// <summary>
    /// Bounded document input and explicit dependency/conformance choices.
    /// </summary>
    public sealed class WotDocumentInput
    {
        /// <summary>
        /// Gets or sets the primary document or document-set archive.
        /// </summary>
        public required WotDocumentSource Source { get; set; }

        /// <summary>
        /// Gets or sets supplied dependencies; every entry needs a Reference and exactly one content source.
        /// </summary>
        [JsonConverter(typeof(WotInputArrayJsonConverter<WotDocumentSource>))]
        public ArrayOf<WotDocumentSource> Dependencies { get; set; } = [];

        /// <summary>
        /// Gets or sets the document address used for relative references, not an endpoint to fetch.
        /// </summary>
        public string BaseUri { get; set; } = "https://mcp.invalid/root.json";

        /// <summary>
        /// Gets or sets explicit permission to consult the host's remote dependency allowlist.
        /// </summary>
        public bool ResolveRemoteDependencies { get; set; }

        /// <summary>
        /// Gets or sets whether Source names the document-set ZIP produced by wot_from_nodeset.
        /// </summary>
        public bool DocumentSetArchive { get; set; }

        /// <summary>
        /// Gets or sets strict authoring checks instead of the default lossless consumer rules.
        /// </summary>
        public bool StrictAuthoring { get; set; }

        /// <summary>
        /// Gets or sets explicit conformance claims required during strict authoring checks.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> RequiredConformance { get; set; } = [];

        /// <summary>
        /// Gets or sets the existing converter's preservation-envelope policy.
        /// </summary>
        public WotNodeSetPreservationMode PreservationMode { get; set; } = WotNodeSetPreservationMode.WhenRequired;
    }
}
