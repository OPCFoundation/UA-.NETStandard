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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        private const string kRootNotifierNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RootNotifier";

        private const uint kRootNotifierNodeId = 8800;

        /// <summary>
        /// Keeps candidate root-notifier references private until publication and excludes them on abort.
        /// </summary>
        /// <param name="publish">Whether to publish the prepared candidate.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedRootNotifierReferencesRemainPrivateUntilPublicationOrAbortAsync(bool publish)
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            var factory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            NodeManagerBatchResult? result = null;

            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], null).ConfigureAwait(false))
            {
                LifecycleRootNotifierNodeManager candidate = RequireLifecycleValue(factory.Manager);
                Assert.That(candidate.ServerReferenceExistsAfterCreation, Is.False);
                Assert.That(HasServerNotifierReference(candidate.RootNotifierId), Is.False);
                if (publish)
                {
                    result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }

            Assert.That(
                HasServerNotifierReference(RequireLifecycleValue(factory.Manager).RootNotifierId),
                Is.EqualTo(publish));
            if (!publish)
            {
                Assert.That(result, Is.Null);
            }
        }

        /// <summary>
        /// Preserves the serving root-notifier reference when a later candidate factory fails.
        /// </summary>
        [Test]
        public async Task FailedRootNotifierReplacementKeepsServingReferenceAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            var originalFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            NodeManagerBatchResult original = await CommitRootNotifierAdditionAsync(
                lifecycle,
                originalFactory).ConfigureAwait(false);
            NodeManagerRegistration registration = original.Registrations[0];
            NodeId notifierId = RequireLifecycleValue(originalFactory.Manager).RootNotifierId;
            Assert.That(HasServerNotifierReference(notifierId), Is.True);

            var replacementFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            var failingFactory = new LifecycleFailingFactory();
            await Assert.ThatAsync(() => lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(registration, replacementFactory),
                    NodeManagerBatchChange.Add(failingFactory)
                ],
                null).AsTask(), Throws.TypeOf<IOException>()).ConfigureAwait(false);

            LifecycleRootNotifierNodeManager candidate = RequireLifecycleValue(replacementFactory.Manager);
            Assert.That(candidate.ServerReferenceExistsAfterCreation, Is.True);
            Assert.That(HasServerNotifierReference(notifierId), Is.True);
        }

        /// <summary>
        /// Prevents retired same-NodeId cleanup from removing the replacement's notifier reference.
        /// </summary>
        [Test]
        public async Task RetiredSameIdRootNotifierCannotRemoveReplacementReferenceAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            var originalFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            NodeManagerBatchResult original = await CommitRootNotifierAdditionAsync(
                lifecycle,
                originalFactory).ConfigureAwait(false);
            NodeManagerRegistration registration = original.Registrations[0];
            NodeId notifierId = RequireLifecycleValue(originalFactory.Manager).RootNotifierId;

            var replacementFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            NodeManagerBatchResult replacement;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(registration, replacementFactory)],
                null).ConfigureAwait(false))
            {
                Assert.That(HasServerNotifierReference(notifierId), Is.True);
                replacement = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            }

            Assert.That(replacement.CleanupFailure, Is.Null);
            await replacement.Retirement.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(HasServerNotifierReference(notifierId), Is.True);
        }

        /// <summary>
        /// Prepares and commits a candidate root notifier.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider.</param>
        /// <param name="factory">The root-notifier factory.</param>
        /// <returns>The committed batch result.</returns>
        private async Task<NodeManagerBatchResult> CommitRootNotifierAdditionAsync(
            INodeManagerBatchLifecycle lifecycle,
            LifecycleRootNotifierFactory factory)
        {
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)],
                null).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            return result;
        }

        /// <summary>
        /// Checks whether the serving Server object has a forward notifier reference to the node.
        /// </summary>
        /// <param name="notifierId">The notifier node identifier.</param>
        /// <returns><see langword="true"/> when the reference exists.</returns>
        private bool HasServerNotifierReference(NodeId notifierId)
        {
            return m_server.CurrentInstance.ServerObject!.ReferenceExists(
                ReferenceTypeIds.HasNotifier,
                false,
                notifierId);
        }

        /// <summary>
        /// Creates a manager that contributes one root notifier.
        /// </summary>
        private sealed class LifecycleRootNotifierFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Initializes the factory with its manager namespace and notifier identifier.
            /// </summary>
            /// <param name="namespaceUri">The manager namespace URI.</param>
            /// <param name="identifier">The numeric notifier identifier.</param>
            public LifecycleRootNotifierFactory(string namespaceUri, uint identifier)
            {
                m_namespaceUri = namespaceUri;
                m_identifier = identifier;
            }

            /// <summary>
            /// Gets the most recently created manager.
            /// </summary>
            public LifecycleRootNotifierNodeManager? Manager { get; private set; }

            /// <summary>
            /// Gets the namespace used by the manager.
            /// </summary>
            public ArrayOf<string> NamespacesUris => [m_namespaceUri];

            /// <summary>
            /// Creates the candidate manager.
            /// </summary>
            /// <param name="server">The staging server.</param>
            /// <param name="configuration">The server configuration.</param>
            /// <param name="cancellationToken">Cancels manager creation.</param>
            /// <returns>The created manager.</returns>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Manager = new LifecycleRootNotifierNodeManager(
                    server,
                    configuration,
                    m_namespaceUri,
                    m_identifier);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }

            /// <summary>The manager namespace URI.</summary>
            private readonly string m_namespaceUri;

            /// <summary>The numeric notifier identifier.</summary>
            private readonly uint m_identifier;
        }

        /// <summary>
        /// Builds a single root notifier during address-space creation.
        /// </summary>
        private sealed class LifecycleRootNotifierNodeManager : AsyncCustomNodeManager, INodeManagerReloadParticipant
        {
            /// <summary>
            /// Initializes the manager with its staged namespace and notifier identifier.
            /// </summary>
            /// <param name="server">The staging server.</param>
            /// <param name="configuration">The server configuration.</param>
            /// <param name="namespaceUri">The manager namespace URI.</param>
            /// <param name="identifier">The numeric notifier identifier.</param>
            public LifecycleRootNotifierNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                uint identifier)
                : base(server, configuration, NullLogger.Instance, namespaceUri)
            {
                RootNotifierId = new NodeId(identifier, NamespaceIndexes[0]);
            }

            /// <summary>
            /// Gets the root notifier identifier.
            /// </summary>
            public NodeId RootNotifierId { get; }

            /// <summary>
            /// Gets whether the serving Server object already had the reference at the end of creation.
            /// </summary>
            public bool ServerReferenceExistsAfterCreation { get; private set; }

            /// <summary>
            /// Creates and registers the test root notifier.
            /// </summary>
            /// <param name="externalReferences">References contributed by this manager.</param>
            /// <param name="cancellationToken">Cancels address-space creation.</param>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var root = new BaseObjectState(null)
                {
                    NodeId = RootNotifierId,
                    BrowseName = new QualifiedName("RootNotifier", NamespaceIndexes[0]),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken).ConfigureAwait(false);
                await AddRootNotifierAsync(root, cancellationToken).ConfigureAwait(false);
                ServerReferenceExistsAfterCreation = Server.ServerObject!.ReferenceExists(
                    ReferenceTypeIds.HasNotifier,
                    false,
                    RootNotifierId);
            }

            /// <summary>
            /// Retains the unchanged root-notifier identity in the replacement.
            /// </summary>
            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }
        }

        /// <summary>
        /// Fails candidate creation after an earlier replacement has been prepared.
        /// </summary>
        private sealed class LifecycleFailingFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Gets the namespace requested by the failing candidate.
            /// </summary>
            public ArrayOf<string> NamespacesUris => [];

            /// <summary>
            /// Throws the deterministic factory failure.
            /// </summary>
            /// <param name="server">The staging server.</param>
            /// <param name="configuration">The server configuration.</param>
            /// <param name="cancellationToken">Cancels manager creation.</param>
            /// <returns>Does not return.</returns>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                throw new IOException("Later candidate factory failed.");
            }
        }
    }
}
