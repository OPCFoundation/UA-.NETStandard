// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var factory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            LifecycleResult? result = null;

            await using (IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory)]).ConfigureAwait(false))
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
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var originalFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            LifecycleResult original = await CommitRootNotifierAdditionAsync(
                lifecycle,
                originalFactory).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(original.Registrations[0]);
            NodeId notifierId = RequireLifecycleValue(originalFactory.Manager).RootNotifierId;
            Assert.That(HasServerNotifierReference(notifierId), Is.True);

            var replacementFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            var failingFactory = new LifecycleFailingFactory();
            await Assert.ThatAsync(async () =>
            {
                await using ILifecycleWriter writer = await LifecycleInspection.Capture(
                    lifecycle, registration).BeginAsync(CancellationToken.None).ConfigureAwait(false);
                await using IPreparedChanges prepared = await writer.PrepareAsync([
                    LifecycleChange.Replace(registration.Slot, replacementFactory),
                    LifecycleChange.Add(failingFactory)
                ]).ConfigureAwait(false);
            }, Throws.TypeOf<IOException>()).ConfigureAwait(false);

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
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var originalFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            LifecycleResult original = await CommitRootNotifierAdditionAsync(
                lifecycle,
                originalFactory).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(original.Registrations[0]);
            NodeId notifierId = RequireLifecycleValue(originalFactory.Manager).RootNotifierId;

            var replacementFactory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId);
            LifecycleResult replacement;
            await using ILifecycleWriter exactWriter1 = await LifecycleInspection.Capture(
                m_server.NodeManagerLifecycle, registration).BeginAsync(CancellationToken.None).ConfigureAwait(false);
            await using (IPreparedChanges prepared = await exactWriter1.PrepareAsync(
                [LifecycleChange.Replace(registration.Slot, replacementFactory)]).ConfigureAwait(false))
            {
                Assert.That(HasServerNotifierReference(notifierId), Is.True);
                replacement = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                await exactWriter1.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(replacement.CleanupFailure, Is.Null);
            await replacement.Retired.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(HasServerNotifierReference(notifierId), Is.True);
        }

        /// <summary>
        /// Prepares and commits a candidate root notifier.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider.</param>
        /// <param name="factory">The root-notifier factory.</param>
        /// <returns>The committed batch result.</returns>
        private static async Task<LifecycleResult> CommitRootNotifierAdditionAsync(
            INodeManagerLifecycle lifecycle,
            LifecycleRootNotifierFactory factory)
        {
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory)]).ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
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
