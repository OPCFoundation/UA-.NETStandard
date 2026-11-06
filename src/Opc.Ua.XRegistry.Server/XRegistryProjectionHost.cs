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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Selects how the previous projection generation is retired after a
    /// successful replacement.
    /// </summary>
    public enum XRegistryProjectionRetirementPolicy
    {
        /// <summary>
        /// Keep the previous generation alive until its monitored items and
        /// requests drain.
        /// </summary>
        Graceful,

        /// <summary>
        /// Invalidate its monitored items with <see cref="StatusCodes.BadNodeIdUnknown"/>
        /// and dispose the previous generation without waiting for the drain.
        /// </summary>
        Immediate
    }

    /// <summary>
    /// One NodeSet2 document loaded as a runtime NodeManager source.
    /// </summary>
    /// <remarks>
    /// A dependency closure produces one or more of these, ordered so that a
    /// source is preceded by every source whose model it depends on.
    /// </remarks>
    public sealed class XRegistryProjectionSource
    {
        /// <summary>
        /// Initializes a projection source.
        /// </summary>
        /// <param name="name">The diagnostic source name.</param>
        /// <param name="modelNamespaceUris">The model namespaces the source owns.</param>
        /// <param name="nodeSetXml">The serialized NodeSet2 document.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
        public XRegistryProjectionSource(
            string name,
            ArrayOf<string> modelNamespaceUris,
            ByteString nodeSetXml)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ModelNamespaceUris = modelNamespaceUris;
            NodeSetXml = nodeSetXml;
            m_bytes = nodeSetXml.IsNull ? [] : nodeSetXml.Memory.ToArray();
        }

        /// <summary>
        /// Gets the diagnostic source name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the model namespace URIs this source owns.
        /// </summary>
        public ArrayOf<string> ModelNamespaceUris { get; }

        /// <summary>
        /// Gets the serialized NodeSet2 document.
        /// </summary>
        public ByteString NodeSetXml { get; }

        /// <summary>
        /// Opens a fresh read-only stream over the serialized document.
        /// </summary>
        /// <remarks>
        /// The bytes are materialized once, so repeated opens - which the
        /// lifecycle performs when it re-imports a generation - share them.
        /// </remarks>
        public Stream OpenRead()
        {
            return new MemoryStream(m_bytes, writable: false);
        }

        private readonly byte[] m_bytes;
    }

    /// <summary>
    /// The full set of NodeSet2 sources for one dependency closure, added or
    /// reloaded as a single runtime NodeManager.
    /// </summary>
    public sealed class XRegistryProjectionDocument
    {
        /// <summary>
        /// Initializes a projection document.
        /// </summary>
        /// <param name="key">The stable closure key this document projects.</param>
        /// <param name="sources">The ordered NodeSet2 sources.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
        public XRegistryProjectionDocument(string key, ArrayOf<XRegistryProjectionSource> sources)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Sources = sources;
        }

        /// <summary>
        /// Gets the stable closure key this document projects.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the ordered NodeSet2 sources.
        /// </summary>
        public ArrayOf<XRegistryProjectionSource> Sources { get; }

        /// <summary>
        /// Gets or sets the model namespace used as the generation's default.
        /// </summary>
        public string? DefaultNamespaceUri { get; init; }

        /// <summary>
        /// Gets or sets the callback that wires domain behavior onto the
        /// generation after its NodeSet is imported.
        /// </summary>
        /// <remarks>
        /// This is the single seam a companion specification uses to attach the
        /// live behavior its projected nodes need - a protocol binding runtime, a
        /// value provider, an operation handler. The returned disposable is owned
        /// by the generation and disposed when the generation is retired, which is
        /// what keeps that behavior from outliving the nodes it serves.
        /// </remarks>
        public Func<INodeManagerBuilder, CancellationToken, ValueTask<IAsyncDisposable?>>?
            ConfigureAsync
        { get; init; }
    }

    /// <summary>
    /// Marks the host-specific registration carried by an
    /// <see cref="XRegistryProjectionHandle"/>.
    /// </summary>
    /// <remarks>
    /// Implementations are opaque to the refresh engine; only the owning
    /// <see cref="IXRegistryProjectionHost"/> interprets them.
    /// </remarks>
    public interface IXRegistryProjectionRegistration
    {
        /// <summary>
        /// Gets the stable identifier the host assigned to this registration.
        /// </summary>
        Guid Id { get; }
    }

    /// <summary>
    /// An opaque handle to a live projection generation held by the host.
    /// </summary>
    public sealed class XRegistryProjectionHandle
    {
        /// <summary>
        /// Initializes a projection handle.
        /// </summary>
        /// <param name="key">The closure key.</param>
        /// <param name="generation">The host generation.</param>
        /// <param name="registration">The host-specific registration.</param>
        /// <param name="rootNodeIds">The materialized root nodes.</param>
        /// <param name="materializedNodeCount">The materialized node count.</param>
        /// <param name="warning">A non-fatal warning raised after the commit.</param>
        public XRegistryProjectionHandle(
            string key,
            long generation,
            IXRegistryProjectionRegistration? registration,
            ArrayOf<NodeId> rootNodeIds,
            int materializedNodeCount,
            string warning = "")
        {
            Key = key ?? string.Empty;
            Generation = generation;
            Registration = registration;
            RootNodeIds = rootNodeIds;
            MaterializedNodeCount = materializedNodeCount;
            Warning = warning ?? string.Empty;
        }

        /// <summary>
        /// Gets the closure key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the host generation.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the host-specific registration.
        /// </summary>
        public IXRegistryProjectionRegistration? Registration { get; }

        /// <summary>
        /// Gets the materialized root node ids.
        /// </summary>
        public ArrayOf<NodeId> RootNodeIds { get; }

        /// <summary>
        /// Gets the materialized node count.
        /// </summary>
        public int MaterializedNodeCount { get; }

        /// <summary>
        /// Gets a non-fatal host warning produced after the replacement
        /// generation was committed, for example a deferred cleanup of the
        /// previous generation.
        /// </summary>
        public string Warning { get; }
    }

    /// <summary>
    /// The seam between the refresh engine and the live server's NodeManager
    /// lifecycle.
    /// </summary>
    /// <remarks>
    /// The production implementation adds a runtime NodeSet on first activation
    /// and reloads it on update, keeping the stable registry NodeManager
    /// separate. A test double records the sequence of operations without a
    /// running server.
    /// </remarks>
    public interface IXRegistryProjectionHost
    {
        /// <summary>
        /// Adds a projection for its first activation.
        /// </summary>
        /// <param name="document">The projection document.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A handle to the new live generation.</returns>
        ValueTask<XRegistryProjectionHandle> AddAsync(
            XRegistryProjectionDocument document,
            CancellationToken ct = default);

        /// <summary>
        /// Reloads a live projection so new requests reach the replacement while
        /// the previous generation keeps serving its monitored items until they
        /// drain.
        /// </summary>
        /// <param name="current">The handle being replaced.</param>
        /// <param name="document">The replacement projection document.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A handle to the replacement generation.</returns>
        ValueTask<XRegistryProjectionHandle> ShadowReloadAsync(
            XRegistryProjectionHandle current,
            XRegistryProjectionDocument document,
            CancellationToken ct = default);

        /// <summary>
        /// Reloads a live projection and immediately retires the previous
        /// generation.
        /// </summary>
        /// <param name="current">The handle being replaced.</param>
        /// <param name="document">The replacement projection document.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A handle to the replacement generation.</returns>
        ValueTask<XRegistryProjectionHandle> ImmediateReloadAsync(
            XRegistryProjectionHandle current,
            XRegistryProjectionDocument document,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a live projection after its monitored items drain, without
        /// disconnecting clients.
        /// </summary>
        /// <param name="handle">The handle to remove.</param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask RemoveAsync(
            XRegistryProjectionHandle handle,
            CancellationToken ct = default);
    }

}
