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
 *
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
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal interface IWotPreparedViewSourceConsumer
    {
        void BindSourceImage(WotPreparedSourceImage sources);
    }

    internal sealed class WotPreparedSourceImage
    {
        internal void Exclude(IAsyncNodeManager owner)
        {
            m_removed.Add(owner);
        }

        internal IAsyncNodeManagerFactory Capture(IAsyncNodeManagerFactory factory)
        {
            return new SourceFactory(this, factory);
        }

        internal void BindRegistrations(ArrayOf<NodeManagerRegistration> registrations)
        {
            if (registrations.Count != m_candidates.Count)
            {
                throw new ArgumentException(
                    "The prepared source registration image is incomplete.", nameof(registrations));
            }
            for (int i = 0; i < registrations.Count; i++)
            {
                if (!ReferenceEquals(registrations[i].NodeManager, m_candidates[i]))
                {
                    throw new ArgumentException(
                        "A prepared source registration has another owner.", nameof(registrations));
                }
            }
            m_bound = true;
        }

        internal void RequireBound()
        {
            if (!m_bound)
            {
                throw new InvalidOperationException("The candidate source owners have not been bound.");
            }
        }

        internal async ValueTask<(object? Handle, IAsyncNodeManager? Owner)> FindAsync(
            IServerInternal server, NodeId nodeId, CancellationToken cancellationToken)
        {
            if (m_server is not null && !ReferenceEquals(m_server, server))
            {
                throw new ArgumentException("The prepared source image belongs to another server.");
            }
            object? found = null;
            IAsyncNodeManager? owner = null;
            foreach (IAsyncNodeManager candidate in m_candidates)
            {
                object handle = await candidate.GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
                if (handle is null)
                {
                    continue;
                }
                if (owner is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdExists, "A selected Node has multiple prepared source owners.");
                }
                found = handle;
                owner = candidate;
            }
            if (owner is not null)
            {
                return (found, owner);
            }
            (object? liveHandle, IAsyncNodeManager? liveOwner) = await server.NodeManager
                .GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
            return liveOwner is null || m_removed.Contains(liveOwner) ? (null, null) : (liveHandle, liveOwner);
        }

        private sealed class SourceFactory(WotPreparedSourceImage image, IAsyncNodeManagerFactory inner)
            : IAsyncNodeManagerFactory, IRequestCallbackSafeNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => inner.NamespacesUris;
            public bool AllowLifecycleFromRequestCallback =>
                inner is IRequestCallbackSafeNodeManagerFactory { AllowLifecycleFromRequestCallback: true };

            public async ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                if (image.m_server is not null && !ReferenceEquals(image.m_server, server))
                {
                    throw new ArgumentException("The source factories do not share one server.");
                }
                image.m_server = server;
                IAsyncNodeManager manager = await inner.CreateAsync(server, configuration, cancellationToken)
                    .ConfigureAwait(false);
                image.m_candidates.Add(manager);
                return manager;
            }
        }

        private readonly List<IAsyncNodeManager> m_candidates = [];
        private readonly HashSet<IAsyncNodeManager> m_removed = [];
        private IServerInternal? m_server;
        private bool m_bound;
    }
}
