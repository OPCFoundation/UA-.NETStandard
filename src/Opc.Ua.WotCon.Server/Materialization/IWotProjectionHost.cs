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
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One NodeSet2 document loaded as a runtime NodeManager source. A projection
    /// closure produces one or more of these (TM type NodeSets loaded before the
    /// dependent TD instance NodeSet).
    /// </summary>
    public sealed class WotProjectionSource
    {
        /// <summary>Initializes a new projection source.</summary>
        public WotProjectionSource(
            string name,
            ImmutableArray<string> modelNamespaceUris,
            byte[] nodeSetXml)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ModelNamespaceUris = modelNamespaceUris.IsDefault
                ? ImmutableArray<string>.Empty : modelNamespaceUris;
            NodeSetXml = nodeSetXml ?? throw new ArgumentNullException(nameof(nodeSetXml));
        }

        /// <summary>Gets the diagnostic source name.</summary>
        public string Name { get; }

        /// <summary>Gets the model namespace URIs this source owns.</summary>
        public ImmutableArray<string> ModelNamespaceUris { get; }

        /// <summary>Gets the serialized NodeSet2 XML bytes.</summary>
        public byte[] NodeSetXml { get; }
    }

    /// <summary>
    /// The full set of NodeSet2 sources for one projection closure to be added
    /// or shadow-reloaded as a single runtime NodeManager.
    /// </summary>
    public sealed class WotProjectionDocument
    {
        /// <summary>Initializes a new projection document.</summary>
        public WotProjectionDocument(
            string closureKey,
            ImmutableArray<WotProjectionSource> sources)
        {
            ClosureKey = closureKey ?? throw new ArgumentNullException(nameof(closureKey));
            Sources = sources.IsDefault ? ImmutableArray<WotProjectionSource>.Empty : sources;
        }

        /// <summary>Gets the stable closure key this document projects.</summary>
        public string ClosureKey { get; }

        /// <summary>Gets the ordered NodeSet2 sources.</summary>
        public ImmutableArray<WotProjectionSource> Sources { get; }
    }

    /// <summary>
    /// An opaque handle to a live projection generation held by the host. It
    /// wraps the underlying runtime NodeManager registration and records the
    /// materialized root NodeIds and node count.
    /// </summary>
    public sealed class WotProjectionHandle
    {
        /// <summary>Initializes a new projection handle.</summary>
        public WotProjectionHandle(
            string closureKey,
            long generation,
            object? registration,
            ImmutableArray<NodeId> rootNodeIds,
            int materializedNodeCount)
        {
            ClosureKey = closureKey ?? string.Empty;
            Generation = generation;
            Registration = registration;
            RootNodeIds = rootNodeIds.IsDefault ? ImmutableArray<NodeId>.Empty : rootNodeIds;
            MaterializedNodeCount = materializedNodeCount;
        }

        /// <summary>Gets the closure key.</summary>
        public string ClosureKey { get; }

        /// <summary>Gets the projection generation.</summary>
        public long Generation { get; }

        /// <summary>Gets the underlying runtime registration (host-specific).</summary>
        public object? Registration { get; }

        /// <summary>Gets the materialized root NodeIds.</summary>
        public ImmutableArray<NodeId> RootNodeIds { get; }

        /// <summary>Gets the materialized node count.</summary>
        public int MaterializedNodeCount { get; }
    }

    /// <summary>
    /// The seam between the materialization coordinator and the live server's
    /// NodeManager lifecycle. The production implementation adds a runtime
    /// NodeSet on first activation and shadow-reloads it on update, keeping the
    /// stable registry NodeManager separate. A test double records the sequence
    /// of add/shadow-reload/remove operations without a running server.
    /// </summary>
    public interface IWotProjectionHost
    {
        /// <summary>
        /// Adds a projection for its first activation and returns a handle to the
        /// new live generation.
        /// </summary>
        ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Shadow-reloads a live projection: new service requests are routed to
        /// the replacement generation while the previous generation keeps serving
        /// its existing monitored items until they drain.
        /// </summary>
        ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current,
            WotProjectionDocument document,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a live projection after its monitored items drain, without
        /// disconnecting clients.
        /// </summary>
        ValueTask RemoveAsync(
            WotProjectionHandle handle,
            CancellationToken cancellationToken = default);
    }
}
