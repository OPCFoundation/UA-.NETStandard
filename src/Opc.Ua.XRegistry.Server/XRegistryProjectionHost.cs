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
using Opc.Ua.Server.RuntimeNodeSet;

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

    /// <summary>
    /// The production <see cref="IXRegistryProjectionHost"/> that projects
    /// closures onto the live server through the public NodeManager lifecycle.
    /// </summary>
    /// <remarks>
    /// First activation uses
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync"/>;
    /// updates use
    /// <see cref="RuntimeNodeSetLifecycleExtensions.ShadowReloadRuntimeNodeSetAsync"/>,
    /// so the previous generation keeps serving its existing monitored items
    /// until they drain. The stable registry NodeManager is never touched.
    /// </remarks>
    public sealed class LifecycleXRegistryProjectionHost : IXRegistryProjectionHost
    {
        /// <summary>
        /// Initializes a host over the supplied lifecycle.
        /// </summary>
        /// <param name="lifecycle">The node manager lifecycle to project onto.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is <c>null</c>.</exception>
        public LifecycleXRegistryProjectionHost(INodeManagerLifecycle lifecycle)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryProjectionHandle> AddAsync(
            XRegistryProjectionDocument document,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(document);

            RuntimeNodeSetOptions options = BuildOptions(document);
            NodeManagerRegistration registration = await m_lifecycle
                .AddRuntimeNodeSetAsync(options, callerContext: null, ct)
                .ConfigureAwait(false);
            return new XRegistryProjectionHandle(
                document.Key,
                registration.Generation,
                new NodeManagerProjectionRegistration(registration),
                [],
                0);
        }

        /// <inheritdoc/>
        public ValueTask<XRegistryProjectionHandle> ShadowReloadAsync(
            XRegistryProjectionHandle current,
            XRegistryProjectionDocument document,
            CancellationToken ct = default)
        {
            return ReloadAsync(current, document, immediate: false, ct);
        }

        /// <inheritdoc/>
        public ValueTask<XRegistryProjectionHandle> ImmediateReloadAsync(
            XRegistryProjectionHandle current,
            XRegistryProjectionDocument document,
            CancellationToken ct = default)
        {
            return ReloadAsync(current, document, immediate: true, ct);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            XRegistryProjectionHandle handle,
            CancellationToken ct = default)
        {
            if (m_lifecycle.IsShuttingDown)
            {
                return;
            }

            if (handle?.Registration is NodeManagerProjectionRegistration wrapper)
            {
                await m_lifecycle
                    .RemoveAsync(wrapper.Registration, callerContext: null, ct)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<XRegistryProjectionHandle> ReloadAsync(
            XRegistryProjectionHandle current,
            XRegistryProjectionDocument document,
            bool immediate,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(document);

            if (current?.Registration is not NodeManagerProjectionRegistration wrapper)
            {
                // No live registration to reload; fall back to a fresh add.
                return await AddAsync(document, ct).ConfigureAwait(false);
            }

            RuntimeNodeSetOptions options = BuildOptions(document);
            NodeManagerRegistration next;
            string warning = string.Empty;
            try
            {
                next = immediate
                    ? await m_lifecycle
                        .ImmediateReloadRuntimeNodeSetAsync(wrapper.Registration, options, ct)
                        .ConfigureAwait(false)
                    : await m_lifecycle
                        .ShadowReloadRuntimeNodeSetAsync(wrapper.Registration, options, ct)
                        .ConfigureAwait(false);
            }
            catch (NodeManagerReloadCommittedException ex)
            {
                next = ex.Registration;
                warning = "The replacement is active, but prior-generation cleanup is pending: " +
                    ex.Message;
            }

            return new XRegistryProjectionHandle(
                document.Key,
                next.Generation,
                new NodeManagerProjectionRegistration(next),
                [],
                0,
                warning);
        }

        private static RuntimeNodeSetOptions BuildOptions(XRegistryProjectionDocument document)
        {
            var sources = new RuntimeNodeSetSource[document.Sources.Count];
            for (int ii = 0; ii < document.Sources.Count; ii++)
            {
                XRegistryProjectionSource source = document.Sources[ii];
                sources[ii] = RuntimeNodeSetSource.FromStream(
                    source.Name,
                    _ => new ValueTask<Stream>(source.OpenRead()),
                    source.ModelNamespaceUris);
            }

            return new RuntimeNodeSetOptions
            {
                Sources = new ArrayOf<RuntimeNodeSetSource>(sources),
                DefaultNamespaceUri = document.DefaultNamespaceUri,
                AllowLifecycleFromRequestCallback = true,
                ConfigureAsync = document.ConfigureAsync
            };
        }

        private readonly INodeManagerLifecycle m_lifecycle;

        /// <summary>
        /// Carries the lifecycle registration owned by this host through the
        /// host-agnostic <see cref="XRegistryProjectionHandle"/>.
        /// </summary>
        private sealed class NodeManagerProjectionRegistration : IXRegistryProjectionRegistration
        {
            public NodeManagerProjectionRegistration(NodeManagerRegistration registration)
            {
                Registration = registration;
            }

            public Guid Id => Registration.Id;

            public NodeManagerRegistration Registration { get; }
        }
    }
}
