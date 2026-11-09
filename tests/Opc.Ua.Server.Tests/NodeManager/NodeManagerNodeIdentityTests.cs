/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies deletion of a captured node does not remove a replacement with the same identifier.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerNodeIdentityTests
    {
        /// <summary>
        /// Runtime deletion leaves a replacement indexed even when its value equals the captured node.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task AsyncRuntimeDeletionPreservesReplacement(bool replace, bool compareByNodeId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new AsyncIdentityManager(server.Object);
                NodeId id = new("runtime", 1);
                var original = new IdentityProbeNode(id, compareByNodeId);
                var replacement = new IdentityProbeNode(id, compareByNodeId);
                manager.Index(original);
                var entered = new TaskCompletionSource<NodeState>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                manager.BeforeRemoval = async (node, cancellationToken) =>
                {
                    entered.TrySetResult(node);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                };
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                Task<bool> deletion = manager.DeleteNodeAsync(manager.SystemContext, id, deadline.Token).AsTask();
                try
                {
                    Assert.That(
                        await entered.Task.WaitAsync(deadline.Token).ConfigureAwait(false), Is.SameAs(original));
                    if (replace)
                    {
                        Assert.That(original.Equals(replacement), Is.EqualTo(compareByNodeId));
                        manager.IndexViaBase(replacement);
                    }
                    release.TrySetResult(true);
                    await deletion.WaitAsync(deadline.Token).ConfigureAwait(false);
                    Assert.That(manager.Find(id), replace ? Is.SameAs(replacement) : Is.Null);
                }
                finally
                {
                    release.TrySetResult(true);
                    await deletion.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Synchronous removal checks the captured node against an indexed replacement.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SynchronousRuntimeRemovalPreservesReplacement(bool replace, bool compareByNodeId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new SynchronousIdentityManager(server.Object);
                NodeId id = new("runtime", 1);
                var original = new IdentityProbeNode(id, compareByNodeId);
                var replacement = new IdentityProbeNode(id, compareByNodeId);
                manager.Index(original);
                if (replace)
                {
                    Assert.That(original.Equals(replacement), Is.EqualTo(compareByNodeId));
                    manager.IndexViaBase(replacement);
                }
                manager.RemoveCaptured(original);
                Assert.That(manager.Find(id), replace ? Is.SameAs(replacement) : Is.Null);
            }
        }

        /// <summary>
        /// Asynchronous address-space deletion preserves a replacement indexed after the node snapshot.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task AsyncAddressSpaceDeletionPreservesReplacement(bool replace, bool compareByNodeId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new AsyncIdentityManager(server.Object);
                NodeId id = new("teardown", 1);
                var original = new IdentityProbeNode(id, compareByNodeId);
                var replacement = new IdentityProbeNode(id, compareByNodeId);
                manager.Index(original);
                if (replace)
                {
                    Assert.That(original.Equals(replacement), Is.EqualTo(compareByNodeId));
                    original.AfterDelete = () => manager.IndexViaInterface(replacement);
                }
                await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
                Assert.That(manager.Find(id), replace ? Is.SameAs(replacement) : Is.Null);
            }
        }

        /// <summary>
        /// Synchronous address-space deletion preserves a replacement indexed after the node snapshot.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SynchronousAddressSpaceDeletionPreservesReplacement(bool replace, bool compareByNodeId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new SynchronousIdentityManager(server.Object);
                NodeId id = new("teardown", 1);
                var original = new IdentityProbeNode(id, compareByNodeId);
                var replacement = new IdentityProbeNode(id, compareByNodeId);
                manager.Index(original);
                if (replace)
                {
                    Assert.That(original.Equals(replacement), Is.EqualTo(compareByNodeId));
                    original.AfterDelete = () => manager.IndexViaInterface(replacement);
                }
                manager.DeleteAddressSpace();
                Assert.That(manager.Find(id), replace ? Is.SameAs(replacement) : Is.Null);
            }
        }

        /// <summary>
        /// Exposes the existing protected node index and a barrier before asynchronous removal.
        /// </summary>
        private sealed class AsyncIdentityManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates an isolated asynchronous manager.
            /// </summary>
            public AsyncIdentityManager(IServerInternal server)
                : base(server, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <summary>
            /// Gets or sets the callback invoked before removing the captured node.
            /// </summary>
            public Func<NodeState, CancellationToken, ValueTask>? BeforeRemoval { get; set; }

            /// <summary>
            /// Indexes a node using the protected dictionary.
            /// </summary>
            public void Index(NodeState node)
            {
                PredefinedNodes[node.NodeId] = node;
            }

            /// <summary>
            /// Replaces a node through the dictionary's concurrent base type.
            /// </summary>
            public void IndexViaBase(NodeState node)
            {
                ConcurrentDictionary<NodeId, NodeState> nodes = PredefinedNodes;
                nodes[node.NodeId] = node;
            }

            /// <summary>
            /// Replaces a node through the dictionary interface.
            /// </summary>
            public void IndexViaInterface(NodeState node)
            {
                IDictionary<NodeId, NodeState> nodes = PredefinedNodes;
                nodes[node.NodeId] = node;
            }

            /// <inheritdoc/>
            protected override async ValueTask RemovePredefinedNodeAsync(
                ISystemContext context,
                NodeState node,
                List<LocalReference> referencesToRemove,
                CancellationToken cancellationToken = default)
            {
                if (BeforeRemoval is { } beforeRemoval)
                {
                    await beforeRemoval(node, cancellationToken).ConfigureAwait(false);
                }
                await base.RemovePredefinedNodeAsync(context, node, referencesToRemove, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Exposes the existing protected node index and synchronous captured-node removal.
        /// </summary>
        private sealed class SynchronousIdentityManager : CustomNodeManager2
        {
            /// <summary>
            /// Creates an isolated synchronous manager.
            /// </summary>
            public SynchronousIdentityManager(IServerInternal server)
                : base(server, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <summary>
            /// Indexes a node using the protected dictionary.
            /// </summary>
            public void Index(NodeState node)
            {
                PredefinedNodes[node.NodeId] = node;
            }

            /// <summary>
            /// Replaces a node through the dictionary's concurrent base type.
            /// </summary>
            public void IndexViaBase(NodeState node)
            {
                ConcurrentDictionary<NodeId, NodeState> nodes = PredefinedNodes;
                nodes[node.NodeId] = node;
            }

            /// <summary>
            /// Replaces a node through the dictionary interface.
            /// </summary>
            public void IndexViaInterface(NodeState node)
            {
                IDictionary<NodeId, NodeState> nodes = PredefinedNodes;
                nodes[node.NodeId] = node;
            }

            /// <summary>
            /// Removes the instance captured by a deletion.
            /// </summary>
            public void RemoveCaptured(NodeState node)
            {
                RemovePredefinedNode(SystemContext, node, []);
            }
        }

        /// <summary>
        /// A node whose equality can deliberately identify different same-identifier instances as equal.
        /// </summary>
        private sealed class IdentityProbeNode : BaseObjectState
        {
            /// <summary>
            /// Creates a node with the selected equality behavior.
            /// </summary>
            public IdentityProbeNode(NodeId id, bool compareByNodeId)
                : base(null)
            {
                NodeId = id;
                m_compareByNodeId = compareByNodeId;
            }

            /// <summary>
            /// Gets or sets the callback invoked after address-space deletion visits this node.
            /// </summary>
            public Action? AfterDelete { get; set; }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return ReferenceEquals(this, obj) ||
                    (m_compareByNodeId && obj is IdentityProbeNode other &&
                        other.m_compareByNodeId && NodeId == other.NodeId);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return m_compareByNodeId ? NodeId.GetHashCode() : RuntimeHelpers.GetHashCode(this);
            }

            /// <inheritdoc/>
            protected override void OnAfterDelete(ISystemContext context)
            {
                base.OnAfterDelete(context);
                AfterDelete?.Invoke();
            }

            /// <summary>Whether equal identifiers compare as equal node values.</summary>
            private readonly bool m_compareByNodeId;
        }
    }
}
