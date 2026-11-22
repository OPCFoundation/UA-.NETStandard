// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Legacy adapter coverage; TODO: remove with synchronous manager support.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

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
        /// Attaches a root registered after the route swap while preparation bookkeeping is still present.
        /// </summary>
        /// <param name="adapted">Whether the registered facade wraps the asynchronous manager.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RootRegisteredDuringPublicationAttachesExistingServerSubscriberAsync(bool adapted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            var factory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId, adapted);
            try
            {
                await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                LifecycleRootNotifierNodeManager candidate = RequireLifecycleValue(factory.Manager);
                IAsyncNodeManager registered = RequireLifecycleValue(factory.RegisteredManager);
                IAsyncNodeManager alternate = adapted
                    ? candidate
                    : new AsyncNodeManagerAdapter(candidate.SyncNodeManager);
                var lateRoot = new BaseObjectState(null)
                {
                    NodeId = new NodeId(kRootNotifierNodeId + 1, candidate.NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("PublishedLateRoot", candidate.NamespaceIndexes[0]),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await candidate.AddPredefinedNodeAsync(lateRoot, timeout.Token).ConfigureAwait(false);
                Assert.That(master.IsPublishedNodeManager(registered), Is.False);
                Assert.That(master.IsPublishedNodeManager(alternate), Is.False);

                ExecutionContext executionContext = ExecutionContext.Capture() ??
                    throw new InvalidOperationException("The test execution context is unavailable.");
                Task? registration = null;
                bool registeredVisible = false;
                bool alternateVisible = true;
                bool rootRegistered = false;
                bool referencePublished = true;
                bool monitored = true;
                bool registrationCompleted = true;
                m_server.HostBoundary.Publication = (plan, publish) =>
                {
                    var commit = new Mock<INodeManagerPublicationCommit>();
                    commit.SetupGet(value => value.Publishes).Returns(() => plan.Commit.Publishes);
                    commit.Setup(value => value.EnterPublicationScope())
                        .Returns(() => plan.Commit.EnterPublicationScope());
                    commit.Setup(value => value.DecideAsync(
                            It.IsAny<LifecycleWriter.Mutation.DecisionWindow?>(), It.IsAny<CancellationToken>()))
                        .Returns<LifecycleWriter.Mutation.DecisionWindow?, CancellationToken>(
                            plan.Commit.DecideAsync);
                    commit.Setup(value => value.CaptureCommittedReferences(It.IsAny<PublishedReferenceChanges>()))
                        .Callback<PublishedReferenceChanges>(plan.Commit.CaptureCommittedReferences);
                    commit.Setup(value => value.Publish(It.IsAny<bool>())).Callback<bool>(historianChanged =>
                    {
                        plan.Commit.Publish(historianChanged);
                        registeredVisible = master.IsPublishedNodeManager(registered);
                        alternateVisible = master.IsPublishedNodeManager(alternate);
                        ExecutionContext.Run(executionContext, _ =>
                        {
                            registration = candidate.RegisterRootNotifierAsync(lateRoot, timeout.Token).AsTask();
                        }, null);
                        rootRegistered = candidate.IsRootNotifier(lateRoot.NodeId);
                        referencePublished = HasServerNotifierReference(lateRoot.NodeId);
                        monitored = lateRoot.AreEventsMonitored;
                        registrationCompleted = registration!.IsCompleted;
                    });
                    return publish(plan with { Commit = commit.Object });
                };

                LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                m_server.HostBoundary.Publication = null;
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Issues.Count, Is.Zero);
                Assert.That(registeredVisible, Is.True);
                Assert.That(alternateVisible, Is.False);
                Assert.That(rootRegistered, Is.True);
                Assert.That(referencePublished, Is.False);
                Assert.That(monitored, Is.False);
                Assert.That(registrationCompleted, Is.False);
                Assert.That(registration, Is.Not.Null);
                await registration!.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(HasServerNotifierReference(lateRoot.NodeId), Is.True);
                Assert.That(lateRoot.AreEventsMonitored, Is.True);
            }
            finally
            {
                m_server.HostBoundary.Publication = null;
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await services.DeleteSubscriptionsAsync(m_requestHeader, [subscriptionId]).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes a root withdrawn after reference discovery instead of publishing a stale reference.
        /// </summary>
        /// <param name="adapted">Whether the registered facade wraps the asynchronous manager.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RootDeletedDuringDecisionDoesNotLeaveServerReferenceAsync(bool adapted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var factory = new LifecycleRootNotifierFactory(kRootNotifierNamespaceUri, kRootNotifierNodeId, adapted);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            LifecycleRootNotifierNodeManager candidate = RequireLifecycleValue(factory.Manager);
            NodeId rootId = candidate.RootNotifierId;

            LifecycleResult result = await prepared.CommitAsync(async cancellationToken =>
            {
                bool deleted = await candidate.DeleteNodeAsync(
                    candidate.SystemContext, rootId, emitModelChange: false, cancellationToken).ConfigureAwait(false);
                Assert.That(deleted, Is.True);
                Assert.That(HasServerNotifierReference(rootId), Is.False);
            }, timeout.Token).ConfigureAwait(false);

            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(result.Issues.Count, Is.Zero);
            Assert.That(candidate.Find(rootId), Is.Null);
            Assert.That(HasServerNotifierReference(rootId), Is.False);
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
            /// <param name="adapted">Whether to register an adapter over the manager.</param>
            public LifecycleRootNotifierFactory(string namespaceUri, uint identifier, bool adapted = false)
            {
                m_namespaceUri = namespaceUri;
                m_identifier = identifier;
                m_adapted = adapted;
            }

            /// <summary>
            /// Gets the most recently created manager.
            /// </summary>
            public LifecycleRootNotifierNodeManager? Manager { get; private set; }

            /// <summary>
            /// Gets the exact facade returned for registration.
            /// </summary>
            public IAsyncNodeManager? RegisteredManager { get; private set; }

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
                RegisteredManager = m_adapted ? new AsyncNodeManagerAdapter(Manager.SyncNodeManager) : Manager;
                return new ValueTask<IAsyncNodeManager>(RegisteredManager);
            }

            /// <summary>The manager namespace URI.</summary>
            private readonly string m_namespaceUri;

            /// <summary>The numeric notifier identifier.</summary>
            private readonly uint m_identifier;

            /// <summary>Whether to register an adapter over the manager.</summary>
            private readonly bool m_adapted;
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
