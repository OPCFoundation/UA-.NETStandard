/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NamespaceMetadataRegistry"/> against a
    /// hand-built <c>Server/Namespaces</c> subtree and a fake host, without a
    /// running server. The end-to-end behaviour through
    /// <see cref="IConfigurationNodeManager"/> is covered by
    /// <see cref="ConfigurationNodeManagerTests"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class NamespaceMetadataRegistryTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            var host = new FakeHost(withNamespacesNode: false);
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => new NamespaceMetadataRegistry(null!, s_telemetry.CreateLogger<NamespaceMetadataRegistry>()));
                Assert.Throws<ArgumentNullException>(() => new NamespaceMetadataRegistry(host, null!));
            });
        }

        [Test]
        public async Task GetWithNullUriReturnsNullAsync()
        {
            NamespaceMetadataRegistry registry = CreateRegistry(new FakeHost(withNamespacesNode: true));
            Assert.That(await registry.GetAsync((string)null!).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task WithoutNamespacesNodeNothingIsFoundOrCreatedAsync()
        {
            var host = new FakeHost(withNamespacesNode: false);
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState? found = await registry.GetAsync("urn:missing").ConfigureAwait(false);
            NamespaceMetadataState created = await registry.CreateAsync("urn:missing").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.Null);
                Assert.That(created, Is.Null);
                Assert.That(host.RegisteredNodes, Is.Empty);
                Assert.DoesNotThrow(() => registry.Attach(host.SystemContext));
                Assert.DoesNotThrow(registry.Detach);
            });
        }

        [Test]
        public async Task GetFindsExistingChildByUriAndByIndexAndCachesItAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState metadata = host.AddMetadataNode(DeterministicServerMock.TestNamespaceUri);
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState? byUri = await registry.GetAsync(DeterministicServerMock.TestNamespaceUri).ConfigureAwait(false);
            NamespaceMetadataState? byUriAgain = await registry.GetAsync(DeterministicServerMock.TestNamespaceUri).ConfigureAwait(false);
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);
            NamespaceMetadataState? byIndex = await registry.GetAsync(index).ConfigureAwait(false);
            NamespaceMetadataState? unknown = await registry.GetAsync("urn:unknown").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byUri, Is.SameAs(metadata));
                Assert.That(byUriAgain, Is.SameAs(metadata));
                Assert.That(byIndex, Is.SameAs(metadata));
                Assert.That(unknown, Is.Null);
                Assert.That(host.RegisteredNodes, Is.Empty, "lookups never register nodes");
            });
        }

        [Test]
        public async Task CreateReturnsExistingNodeOrRegistersANewOneAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState existing = host.AddMetadataNode("urn:existing");
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState found = await registry.CreateAsync("urn:existing").ConfigureAwait(false);
            NamespaceMetadataState created = await registry.CreateAsync("urn:created").ConfigureAwait(false);
            NamespaceMetadataState createdAgain = await registry.CreateAsync("urn:created").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.SameAs(existing));
                Assert.That(created, Is.Not.Null);
                Assert.That(created.NamespaceUri!.Value, Is.EqualTo("urn:created"));
                Assert.That(created.BrowseName.NamespaceIndex, Is.EqualTo(host.NamespaceIndex));
                Assert.That(created.DefaultRolePermissions, Is.Not.Null);
                Assert.That(created.DefaultUserRolePermissions, Is.Not.Null);
                Assert.That(createdAgain, Is.SameAs(created), "a second create finds the registered node");
                Assert.That(host.RegisteredNodes, Is.EqualTo(new[] { created }));
            });
        }

        [Test]
        public async Task AttachTracksDefaultPermissionChangesAndDetachStopsAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState metadata = host.AddMetadataNode("urn:tracked");
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            int raised = 0;
            object? sender = null;
            registry.DefaultPermissionsChanged += (s, _) =>
            {
                raised++;
                sender = s;
            };

            // Not attached: changes are not observed.
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            Assert.That(raised, Is.Zero);

            registry.Attach(host.SystemContext);
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            Assert.Multiple(() =>
            {
                Assert.That(raised, Is.EqualTo(1));
                Assert.That(sender, Is.SameAs(host), "the host node manager is reported as sender");
            });

            // Attaching twice must not double-subscribe.
            registry.Attach(host.SystemContext);
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            Assert.That(raised, Is.EqualTo(2));

            // A node created through the registry is tracked as well, on both permission properties.
            NamespaceMetadataState created = await registry.CreateAsync("urn:created").ConfigureAwait(false);
            ChangeDefaultRolePermissions(created, host.SystemContext);
            ChangeDefaultUserRolePermissions(created, host.SystemContext);
            Assert.That(raised, Is.EqualTo(4));

            registry.Detach();
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            ChangeDefaultRolePermissions(created, host.SystemContext);
            ChangeDefaultUserRolePermissions(created, host.SystemContext);
            Assert.That(raised, Is.EqualTo(4), "detached nodes no longer raise the event");
        }

        [Test]
        public async Task DetachReleasesNodesOwnedByOtherManagersAsync()
        {
            // A metadata node reached only through a forward reference from
            // Server/Namespaces (as companion nodesets owned by other managers
            // provide it) is subscribed by CreateAsync and must be released by
            // Detach even though the host never lists it among its own nodes.
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState foreign = host.AddForeignMetadataNode("urn:foreign");
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;

            NamespaceMetadataState found = await registry.CreateAsync("urn:foreign").ConfigureAwait(false);
            Assert.That(found, Is.SameAs(foreign));

            ChangeDefaultRolePermissions(foreign, host.SystemContext);
            Assert.That(raised, Is.EqualTo(1));

            registry.Detach();
            ChangeDefaultRolePermissions(foreign, host.SystemContext);
            Assert.That(raised, Is.EqualTo(1), "Detach must release nodes it subscribed to but does not own");
        }

        [Test]
        public async Task NamespacesChangeInvalidatesCacheAndTracksNewChildrenAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;

            // Cache the negative result first.
            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.Null);

            // Add a child the way another manager would and signal the change.
            NamespaceMetadataState late = host.AddMetadataNode("urn:late");
            host.NamespacesNode!.ClearChangeMasks(host.SystemContext, true);

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.SameAs(late),
                "the cached negative result is dropped when Server/Namespaces changes");

            ChangeDefaultRolePermissions(late, host.SystemContext);
            Assert.That(raised, Is.EqualTo(1), "children discovered through the change are tracked");
        }

        [Test]
        public async Task MissLinkedLaterThroughReferenceWithoutChangeEventIsFoundAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);

            // Looked up before the namespace's node manager linked its metadata.
            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null);

            // Linked the way AddReferencesAsync does it: a reference only, no
            // StateChanged on Server/Namespaces.
            NamespaceMetadataState late = host.AddForeignMetadataNode(DeterministicServerMock.TestNamespaceUri);

            NamespaceMetadataState? byIndex = await registry.GetAsync(index).ConfigureAwait(false);
            NamespaceMetadataState? byUri = await registry.GetAsync(DeterministicServerMock.TestNamespaceUri)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byIndex, Is.SameAs(late),
                    "a cached miss must not hide metadata linked later (its default permissions would be skipped)");
                Assert.That(byUri, Is.SameAs(late));
            });
        }

        [Test]
        public async Task CachedMissIsAnsweredWithoutEnumeratingNamespacesAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            host.AddMetadataNode("urn:other");
            host.NamespacesNode!.ClearChangeMasks(host.SystemContext, true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);

            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null);
            int enumerations = host.NamespacesEnumerations;

            // The permission hot path asks for the same namespace again and again.
            for (int i = 0; i < 10; i++)
            {
                Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null);
                Assert.That(await registry.GetAsync(DeterministicServerMock.TestNamespaceUri).ConfigureAwait(false), Is.Null);
            }

            Assert.That(host.NamespacesEnumerations, Is.EqualTo(enumerations));
        }

        [Test]
        public async Task MissLinkedLaterWithUnchangedReferenceCountIsFoundAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState other = host.AddForeignMetadataNode("urn:other");
            host.NamespacesNode!.ClearChangeMasks(host.SystemContext, true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.Null);

            // One reference removed and another added: the counts are unchanged.
            host.NamespacesNode.RemoveReference(ReferenceTypeIds.HasComponent, false, other.NodeId);
            NamespaceMetadataState late = host.AddForeignMetadataNode("urn:late");

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.SameAs(late));
        }

        [Test]
        public async Task InvalidateDropsCachedMissAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            // Server/Namespaces already has an unannounced reference change,
            // so a further one leaves its change masks as they are.
            host.AddForeignMetadataNode("urn:other");
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.Null);

            // Linked the way ConfigurationNodeManager.AddReferencesAsync does
            // it, which reports the change through Invalidate.
            NamespaceMetadataState late = host.AddForeignMetadataNode("urn:late");
            registry.Invalidate();

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.SameAs(late));
        }

        [Test]
        public async Task InvalidateDropsCachedMissByIndexAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            host.AddForeignMetadataNode("urn:other");
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);

            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null);
            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null, "the miss is cached by index");

            NamespaceMetadataState late = host.AddForeignMetadataNode(DeterministicServerMock.TestNamespaceUri);
            registry.Invalidate();

            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.SameAs(late));
            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.SameAs(late), "the hit is cached by index");
        }

        [Test]
        public async Task ConcurrentLookupsSeeMetadataLinkedBeforeInvalidateAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            host.AddForeignMetadataNode("urn:other");
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);
            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.Null);

            using var stop = new CancellationTokenSource();
            NamespaceMetadataState? linked = null;
            int staleAfterInvalidate = 0;
            Task[] readers = new Task[4];
            for (int ii = 0; ii < readers.Length; ii++)
            {
                readers[ii] = Task.Run(async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        // Read the published node first: once it is visible here the
                        // invalidation has completed, so the lookup must find it.
                        NamespaceMetadataState? expected = Volatile.Read(ref linked);
                        NamespaceMetadataState? found = await registry.GetAsync(index).ConfigureAwait(false);
                        if (expected != null && !ReferenceEquals(found, expected))
                        {
                            Interlocked.Increment(ref staleAfterInvalidate);
                        }
                    }
                });
            }

            await Task.Delay(20).ConfigureAwait(false);
            NamespaceMetadataState late = host.AddForeignMetadataNode(DeterministicServerMock.TestNamespaceUri);
            registry.Invalidate();
            Volatile.Write(ref linked, late);
            await Task.Delay(50).ConfigureAwait(false);
            stop.Cancel();
            await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(staleAfterInvalidate, Is.Zero, "a miss cached before the invalidation was returned after it");
            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.SameAs(late));
        }

        [Test]
        public async Task DefaultPermissionChangeIsSeenThroughCachedLookupAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState metadata = host.AddMetadataNode(DeterministicServerMock.TestNamespaceUri);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;
            ushort index = (ushort)host.SystemContext.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);

            Assert.That(await registry.GetAsync(index).ConfigureAwait(false), Is.SameAs(metadata));
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            ArrayOf<RolePermissionType> changed = metadata.DefaultRolePermissions!.Value;

            // The cached lookup returns the node itself, so the next request reads the
            // changed permissions, and the change event invalidates sampling verdicts.
            NamespaceMetadataState? again = await registry.GetAsync(index).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(again, Is.SameAs(metadata));
                Assert.That(again!.DefaultRolePermissions!.Value, Is.EqualTo(changed));
                Assert.That(raised, Is.EqualTo(1));
            });
        }

        private static NamespaceMetadataRegistry CreateRegistry(FakeHost host)
        {
            return new NamespaceMetadataRegistry(host, s_telemetry.CreateLogger<NamespaceMetadataRegistry>());
        }

        /// <summary>
        /// A namespace-only policy change invalidates a warm subscriber before the next background value.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        [Category("SecurityRegression")]
        public async Task SecurityR28TightenedNamespaceEncryptionBlocksNextNotificationAsync(MessageSecurityMode mode)
        {
            var host = new FakeHost(withNamespacesNode: true);
            IServerInternal server = host.SystemContext.Server;
            using var queues = (MonitoredItemQueueFactory)server.MonitoredItemQueueFactory;
            NamespaceMetadataState metadata = host.AddMetadataNode(DeterministicServerMock.TestNamespaceUri);
            metadata.AddDefaultAccessRestrictions(host.SystemContext);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            var configuration = new Mock<IConfigurationNodeManager>();
            configuration.Setup(value => value.GetNamespaceMetadataStateAsync(
                    It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                .Returns((ushort index, CancellationToken cancellationToken) =>
                    registry.GetAsync(index, cancellationToken));
            Mock.Get(server).SetupGet(value => value.ConfigurationNodeManager).Returns(configuration.Object);
            Mock.Get(server.NodeManager).SetupGet(value => value.ConfigurationNodeManager)
                .Returns(configuration.Object);
            registry.DefaultPermissionsChanged += (_, _) =>
                configuration.Raise(value => value.DefaultPermissionsChanged += null, EventArgs.Empty);
            await using var owner = new TestableAsyncCustomNodeManager(
                server, new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("NamespaceNotification", owner.NamespaceIndex),
                BrowseName = new QualifiedName("NamespaceNotification", owner.NamespaceIndex),
                DataType = DataTypeIds.Int32,
                Value = 42,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            await owner.AddNodeAsync(owner.SystemContext, NodeId.Null, node).ConfigureAwait(false);
            Mock.Get(server.NodeManager)
                .Setup(value => value.GetManagerHandleAsync(node.NodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                    (new NodeHandle(node.NodeId, node), owner)));
            var weakValues = new ConcurrentQueue<DataValue>();
            var encryptedValues = new ConcurrentQueue<DataValue>();
            await using var monitored = new MonitoredNode2(owner, server, node);
            monitored.Add(CreateNamespaceReceiver(1, mode, weakValues).Object);
            monitored.Add(CreateNamespaceReceiver(2, MessageSecurityMode.SignAndEncrypt, encryptedValues).Object);
            try
            {
                await ReportAsync().ConfigureAwait(false);
                Assert.That(weakValues, Has.Count.EqualTo(1));
                Assert.That(encryptedValues, Has.Count.EqualTo(1));
                Assert.That(weakValues.ToArray()[0].WrappedValue, Is.EqualTo(new Variant(42)));
                Assert.That(encryptedValues.ToArray()[0].WrappedValue, Is.EqualTo(new Variant(42)));
                metadata.DefaultAccessRestrictions!.Value = (ushort)AccessRestrictionType.EncryptionRequired;
                metadata.ClearChangeMasks(host.SystemContext, true);
                node.Value = 99;
                await ReportAsync().ConfigureAwait(false);
                Assert.That(weakValues, Has.Count.EqualTo(2));
                Assert.That(encryptedValues, Has.Count.EqualTo(2));
                DataValue denied = weakValues.ToArray()[1];
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(denied.WrappedValue.IsNull, Is.True);
                Assert.That(encryptedValues.ToArray()[1].WrappedValue, Is.EqualTo(new Variant(99)));

                metadata.DefaultAccessRestrictions.Value = 0;
                metadata.ClearChangeMasks(host.SystemContext, true);
                node.Value = 100;
                await ReportAsync().ConfigureAwait(false);
                Assert.That(weakValues, Has.Count.EqualTo(3));
                Assert.That(encryptedValues, Has.Count.EqualTo(3));
                Assert.That(weakValues.ToArray()[2].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(weakValues.ToArray()[2].WrappedValue, Is.EqualTo(new Variant(100)));
                Assert.That(encryptedValues.ToArray()[2].WrappedValue, Is.EqualTo(new Variant(100)));
            }
            finally
            {
                registry.Detach();
            }

            async Task ReportAsync()
            {
                await monitored.OnMonitoredNodeChangedAsync(
                    host.SystemContext, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await monitored.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Restriction observers follow replacement, removal and registry lifetime rather than the original property.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityR28RestrictionReplacementRemovalAndDetachInvalidateCorrectly()
        {
            var host = new FakeHost(withNamespacesNode: true);
            using var queues = (MonitoredItemQueueFactory)host.SystemContext.Server.MonitoredItemQueueFactory;
            NamespaceMetadataState metadata = host.AddMetadataNode(DeterministicServerMock.TestNamespaceUri);
            metadata.AddDefaultAccessRestrictions(host.SystemContext);
            metadata.ClearChangeMasks(host.SystemContext, true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;
            registry.Attach(host.SystemContext);
            registry.Attach(host.SystemContext);
            metadata.DefaultAccessRestrictions!.Value = (ushort)AccessRestrictionType.EncryptionRequired;
            metadata.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(1));
            PropertyState<ushort> retired = metadata.DefaultAccessRestrictions;
            metadata.DefaultAccessRestrictions = new PropertyState<ushort>.Implementation<VariantBuilder>(metadata)
            {
                BrowseName = new QualifiedName(BrowseNames.DefaultAccessRestrictions),
                Value = (ushort)AccessRestrictionType.SigningRequired
            };
            metadata.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(2));
            int beforeRetiredChange = raised;
            retired.Value = 0;
            retired.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(beforeRetiredChange));
            metadata.DefaultAccessRestrictions = null;
            metadata.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(3));
            registry.Detach();
            metadata.AddDefaultAccessRestrictions(host.SystemContext);
            metadata.DefaultAccessRestrictions!.Value = (ushort)AccessRestrictionType.EncryptionRequired;
            metadata.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(3));
            registry.Attach(host.SystemContext);
            metadata.DefaultAccessRestrictions.Value = 0;
            metadata.ClearChangeMasks(host.SystemContext, true);
            Assert.That(raised, Is.EqualTo(4));
            registry.Detach();
        }

        /// <summary>
        /// Creates an independent subscriber retaining its own endpoint and received values.
        /// </summary>
        private static Mock<IDataChangeMonitoredItem2> CreateNamespaceReceiver(
            uint id, MessageSecurityMode mode, ConcurrentQueue<DataValue> values)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(id, 1));
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.EndpointDescription).Returns(new EndpointDescription
            {
                SecurityMode = mode,
                TransportProfileUri = Profiles.UaTcpTransport
            });
            var item = new Mock<IDataChangeMonitoredItem2>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.Session).Returns(session.Object);
            item.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
            item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
            item.Setup(value => value.QueueValue(It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>()))
                .Callback(new NamespaceQueueValueCallback((in value, _) => values.Enqueue(value)));
            item.Setup(value => value.QueueValue(
                    It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>(), It.IsAny<bool>()))
                .Callback(new NamespaceFilteredQueueValueCallback((in value, _, _) => values.Enqueue(value)));
            return item;
        }

        /// <summary>
        /// Captures the actual ref-readonly notification without boxing it.
        /// </summary>
        private delegate void NamespaceQueueValueCallback(in DataValue value, ServiceResult? error);

        /// <summary>
        /// Captures the production enqueue overload that explicitly selects monitored-item filter handling.
        /// </summary>
        private delegate void NamespaceFilteredQueueValueCallback(
            in DataValue value, ServiceResult? error, bool ignoreFilters);

        /// <summary>
        /// Writes a new, distinct permission set so a <c>Value</c> change mask
        /// is raised on every call, then flushes the masks to fire
        /// <c>StateChanged</c>.
        /// </summary>
        private static void ChangeDefaultRolePermissions(NamespaceMetadataState metadata, ISystemContext context)
        {
            metadata.DefaultRolePermissions!.Value = NextPermissions();
            metadata.ClearChangeMasks(context, true);
        }

        private static void ChangeDefaultUserRolePermissions(NamespaceMetadataState metadata, ISystemContext context)
        {
            metadata.DefaultUserRolePermissions!.Value = NextPermissions();
            metadata.ClearChangeMasks(context, true);
        }

        private static ArrayOf<RolePermissionType> NextPermissions()
        {
            uint permissions = (uint)Interlocked.Increment(ref s_permissionCounter);
            return
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = permissions
                }
            ];
        }

        /// <summary>
        /// Minimal host: a mocked server, its system context with a node-id
        /// factory, and an optional <c>Server/Namespaces</c> node. Registered
        /// nodes are collected so tests can assert on them; metadata nodes
        /// are built with the same generated factories production uses.
        /// </summary>
        private sealed class FakeHost : INamespaceMetadataHost, INodeIdFactory
        {
            public FakeHost(bool withNamespacesNode)
            {
                IServerInternal server = DeterministicServerMock.Create(out _).Object;
                SystemContext = new ServerSystemContext(server) { NodeIdFactory = this };

                if (withNamespacesNode)
                {
                    NamespacesNode = new CountingNamespacesState
                    {
                        NodeId = ObjectIds.Server_Namespaces,
                        BrowseName = new QualifiedName(BrowseNames.Namespaces, 0)
                    };
                }
            }

            public ServerSystemContext SystemContext { get; }

            public ushort NamespaceIndex => 1;

            public NamespacesState? NamespacesNode { get; }

            /// <summary>
            /// How often the children or references of <c>Server/Namespaces</c>
            /// were enumerated.
            /// </summary>
            public int NamespacesEnumerations => ((CountingNamespacesState?)NamespacesNode)?.Enumerations ?? 0;

            public List<NodeState> RegisteredNodes { get; } = [];

            public NamespacesState? FindServerNamespacesNode()
            {
                return NamespacesNode;
            }

            public ValueTask AddPredefinedNodeAsync(NodeState node, CancellationToken cancellationToken)
            {
                RegisteredNodes.Add(node);
                return default;
            }

            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId(Guid.NewGuid(), NamespaceIndex);
            }

            /// <summary>
            /// Adds a fully populated metadata node as a child of
            /// <c>Server/Namespaces</c>, the way a NodeSet or another manager would.
            /// </summary>
            public NamespaceMetadataState AddMetadataNode(string namespaceUri)
            {
                NamespaceMetadataState metadata = CreateMetadataNode(namespaceUri);
                NamespacesNode!.AddChild(metadata);
                return metadata;
            }

            /// <summary>
            /// Adds a metadata node owned by "another manager": it is not a
            /// child of <c>Server/Namespaces</c> but only reachable through a
            /// forward reference, which the registry resolves through the
            /// server's master node manager.
            /// </summary>
            public NamespaceMetadataState AddForeignMetadataNode(string namespaceUri)
            {
                NamespaceMetadataState metadata = CreateMetadataNode(namespaceUri);
                NamespacesNode!.AddReference(ReferenceTypeIds.HasComponent, false, metadata.NodeId);
                Mock.Get(SystemContext.Server.NodeManager)
                    .Setup(m => m.FindNodeInAddressSpaceAsync(metadata.NodeId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(metadata);
                return metadata;
            }

            private NamespaceMetadataState CreateMetadataNode(string namespaceUri)
            {
                NamespaceMetadataState metadata = SystemContext.CreateInstanceOfNamespaceMetadataType(
                    NamespacesNode!,
                    new QualifiedName(namespaceUri, NamespaceIndex));
                metadata.NodeId = New(SystemContext, metadata);
                metadata.NamespaceUri!.Value = namespaceUri;
                metadata.AddDefaultRolePermissions(SystemContext)
                    .AddDefaultUserRolePermissions(SystemContext);

                // Consume the creation change masks so the first flush in a
                // test reports only the change that test made.
                metadata.ClearChangeMasks(SystemContext, true);
                return metadata;
            }
        }

        /// <summary>
        /// A <c>Server/Namespaces</c> node that counts enumerations of its
        /// children and references.
        /// </summary>
        private sealed class CountingNamespacesState : NamespacesState
        {
            public CountingNamespacesState()
                : base(null)
            {
            }

            public int Enumerations { get; private set; }

            public override void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
            {
                Enumerations++;
                base.GetChildren(context, children);
            }

            public override void GetReferences(ISystemContext context, IList<IReference> references)
            {
                Enumerations++;
                base.GetReferences(context, references);
            }
        }

        private static int s_permissionCounter;
    }
}
