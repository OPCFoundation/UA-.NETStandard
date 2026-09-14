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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Qualified registry model, storage limits and authorization for an opt-in endpoint.
    /// </summary>
    public sealed record XRegistryTransactionalOptions
    {
        /// <summary>
        /// Stable registry identity. Persisted identities must match this value at startup.
        /// </summary>
        public string RegistryId { get; init; } = "xregistry";

        /// <summary>
        /// Model source; retained as an owned snapshot rather than a borrowed JSON element.
        /// </summary>
        public JsonElement Model
        {
            get => m_model;
            init => m_model = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        /// <summary>
        /// Initial root attributes for pristine storage, including required
        /// model-defined values that have no default. Persisted roots remain authoritative.
        /// </summary>
        public JsonElement InitialMetadata
        {
            get => m_initialMetadata;
            init => m_initialMetadata = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        /// <summary>
        /// Public registry URL used for self and collection links.
        /// </summary>
        public Uri PublicRoot { get; init; } = new("https://localhost/");

        /// <summary>
        /// Publishes persistent shortself addresses after explicit InitializeShortLinksAsync maintenance.
        /// Disabling only suppresses serialization; existing aliases and their routing remain retained.
        /// </summary>
        public bool ShortLinksEnabled { get; init; }

        /// <summary>
        /// Reserved registry-relative alias mount.
        /// It cannot collide with a model collection or change after initialization.
        /// </summary>
        public string ShortLinkPrefix { get; init; } = "/_s";

        /// <summary>
        /// Maximum live aliases, including logical Resource, Meta and referenced Version addresses.
        /// </summary>
        public int MaxShortLinks { get; init; } = 8192;

        /// <summary>
        /// Explicit discovery catalog. Entries are published but never contacted by the endpoint.
        /// </summary>
        public ArrayOf<Uri> DiscoveryRegistries { get; init; }

        /// <summary>
        /// Maximum entity count, including root, groups, resource meta and versions.
        /// </summary>
        public int MaxEntities { get; init; } = 4096;

        /// <summary>
        /// Maximum bytes in an individual domain document.
        /// </summary>
        public int MaxDocumentBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Maximum serialized generation bytes, including retained operation outcomes.
        /// </summary>
        public int MaxStateBytes { get; init; } = 128 * 1024 * 1024;

        /// <summary>
        /// Maximum outstanding prepared operations before commit or abort.
        /// </summary>
        public int MaxPreparedOperations { get; init; } = 64;

        /// <summary>
        /// Aggregate encoded candidate bytes retained by outstanding preparations.
        /// </summary>
        public int MaxPreparedBytes { get; init; } = 256 * 1024 * 1024;

        /// <summary>
        /// Maximum records in a top-level collection response. Clients can request a
        /// smaller positive limit. Inline collections are never partially paginated.
        /// </summary>
        public int PageSize { get; init; } = 1000;

        /// <summary>
        /// Lifetime of an authenticated generation-bound continuation. No server
        /// snapshot is retained; a registry mutation or restart invalidates the cursor.
        /// </summary>
        public TimeSpan CursorLifetime { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Optional approved include-document source; no network resolution is enabled by default.
        /// </summary>
        public IXRegistryModelDocumentResolver? ModelResolver { get; init; }

        /// <summary>
        /// Base URI for relative includes, defaulting to PublicRoot/modelsource.
        /// </summary>
        public Uri? ModelSourceUri { get; init; }

        /// <summary>
        /// Maximum encoded expanded model size and individual included-document size.
        /// </summary>
        public int MaxModelBytes { get; init; } = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum combined object and include recursion depth.
        /// </summary>
        public int MaxModelDepth { get; init; } = 64;

        /// <summary>
        /// Maximum include references processed during a single model update.
        /// </summary>
        public int MaxModelDocuments { get; init; } = 64;

        /// <summary>
        /// Qualified domain validators. The default checks JSON/1.0 and XML/1.0 syntax only.
        /// Hosts can replace or extend this list; duplicate format ownership is rejected.
        /// </summary>
        public ArrayOf<IXRegistryDocumentValidator> DocumentValidators { get; init; } =
            [new XRegistrySyntaxDocumentValidator()];

        /// <summary>
        /// Optional immutable document storage. When configured, new generations retain blob references
        /// rather than copying every domain document into the metadata snapshot.
        /// </summary>
        public IXRegistryDocumentStore? DocumentStore { get; init; }

        /// <summary>
        /// Default mutation authorization requires this role and authenticated identity.
        /// </summary>
        public string WriteRole { get; init; } = "xregistry.write";

        /// <summary>
        /// Optional policy evaluated before any data or outcomes are accessed.
        /// Hosts supply trusted caller contexts, never credentials from request JSON.
        /// </summary>
        public Func<XRegistryCallContext, bool, CancellationToken, ValueTask<bool>>? AuthorizeAsync { get; init; }

        private readonly JsonElement m_model;
        private readonly JsonElement m_initialMetadata;
    }
}
