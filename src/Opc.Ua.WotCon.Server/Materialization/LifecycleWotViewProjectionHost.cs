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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The production <see cref="IWotViewProjectionHost"/>. It publishes projection
    /// Views into the live server address space by lazily creating a single shared
    /// <see cref="WotProjectionViewNodeManager"/> through the public NodeManager
    /// lifecycle, then delegating each apply and remove to it. One shared manager is
    /// used because the coordinator refreshes a document by applying the new View and
    /// only then removing the old one under the same deterministic NodeId; a single
    /// manager that replaces the View in place avoids two managers transiently
    /// claiming the same NodeId. Removal is identity-checked against the handle that
    /// was returned by the matching apply so a superseding refresh is never undone by
    /// the trailing remove of the generation it replaced.
    /// </summary>
    public sealed class LifecycleWotViewProjectionHost : IWotViewProjectionHost, IDisposable
    {
        /// <summary>
        /// Initializes a new live projection-view host.
        /// </summary>
        /// <param name="lifecycle">
        /// The lifecycle used to publish and retire the projection-view NodeManager.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="lifecycle"/> is <c>null</c>.
        /// </exception>
        public LifecycleWotViewProjectionHost(INodeManagerLifecycle lifecycle)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        /// <inheritdoc/>
        public async ValueTask<WotViewProjectionHandle> ApplyAsync(
            WotViewProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                WotProjectionViewNodeManager manager =
                    await EnsureManagerAsync(cancellationToken).ConfigureAwait(false);
                List<string> applyOmissions = [];
                int count = await manager
                    .ApplyViewAsync(request, applyOmissions, cancellationToken)
                    .ConfigureAwait(false);
                var all = new List<string>(request.Plan.Omissions.Count + applyOmissions.Count);
                for (int i = 0; i < request.Plan.Omissions.Count; i++)
                {
                    all.Add(request.Plan.Omissions[i]);
                }
                all.AddRange(applyOmissions);
                ArrayOf<string> omissions = all.ToArrayOf();
                var handle = new WotViewProjectionHandle(
                    request.ResourceXid,
                    request.ViewNodeId,
                    count,
                    JoinOmissions(omissions),
                    omissions);
                m_live[request.ViewNodeId] = handle;
                return handle;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            WotViewProjectionHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (handle is null || m_lifecycle.IsShuttingDown)
            {
                // While shutting down the lifecycle tears every registration down
                // itself, so the trailing remove can be skipped.
                return;
            }
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_manager is null)
                {
                    return;
                }
                if (m_live.TryGetValue(handle.ViewNodeId, out WotViewProjectionHandle? tracked) &&
                    ReferenceEquals(tracked, handle))
                {
                    m_live.Remove(handle.ViewNodeId);
                    await m_manager
                        .RemoveViewAsync(handle.ViewNodeId, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private async ValueTask<WotProjectionViewNodeManager> EnsureManagerAsync(
            CancellationToken cancellationToken)
        {
            if (m_manager is not null)
            {
                return m_manager;
            }
            var factory = new ViewNodeManagerFactory();
            m_registration = await m_lifecycle
                .AddAsync(factory, callerContext: null, cancellationToken)
                .ConfigureAwait(false);
            m_manager = factory.Created ?? throw new InvalidOperationException(
                "The projection-view NodeManager factory did not create a NodeManager.");
            return m_manager;
        }

        private static string JoinOmissions(ArrayOf<string> omissions)
        {
            if (omissions.Count == 0)
            {
                return string.Empty;
            }
            var parts = new string[omissions.Count];
            for (int i = 0; i < omissions.Count; i++)
            {
                parts[i] = omissions[i];
            }
            return string.Join(" ", parts);
        }

        private readonly INodeManagerLifecycle m_lifecycle;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<NodeId, WotViewProjectionHandle> m_live = new();
        private WotProjectionViewNodeManager? m_manager;
        private NodeManagerRegistration? m_registration;

        /// <summary>
        /// The single-use factory that creates the shared projection-view NodeManager
        /// and captures the created instance so the host can drive it directly.
        /// </summary>
        private sealed class ViewNodeManagerFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Gets the NodeManager created by <see cref="CreateAsync"/>, or <c>null</c>
            /// before it has run.
            /// </summary>
            public WotProjectionViewNodeManager? Created { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => new string[] { Namespaces.WotCon };

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                // CA2000 cannot model ownership transfer through ValueTask<IAsyncNodeManager>.
                // TODO: Remove this suppression when CA2000 recognizes factory ownership transfer.
#pragma warning disable CA2000
                var manager = new WotProjectionViewNodeManager(
                    server,
                    configuration,
                    server.Telemetry.CreateLogger<WotProjectionViewNodeManager>());
#pragma warning restore CA2000
                Created = manager;
                return new ValueTask<IAsyncNodeManager>(manager);
            }
        }
    }
}
