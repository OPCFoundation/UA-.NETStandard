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
