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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises root registration through initial facades without a lifecycle staging server.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class AsyncRootNotifierFacadeTests
    {
        /// <summary>
        /// Publishes a root once and attaches the existing Server subscriber through its registered facade.
        /// </summary>
        /// <param name="adapted">Whether to register the standard asynchronous adapter.</param>
        /// <param name="forwarded">Whether to register a custom forwarding facade.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task InitiallyRegisteredFacadePublishesRootAndAttachesExistingSubscriberAsync(
            bool adapted,
            bool forwarded)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var serverObject = new ServerObjectState(null)
                {
                    NodeId = ObjectIds.Server,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                server.SetupGet(value => value.ServerObject).Returns(serverObject);
                Mock<ICoreNodeManager> core = Mock.Get(server.Object.CoreNodeManager);
                core.Setup(value => value.GetManagerHandleAsync(
                        ObjectIds.Server, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<object>(serverObject));
                core.Setup(value => value.AddReferencesAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                    .Callback<IDictionary<NodeId, IList<IReference>>, CancellationToken>((references, _) =>
                    {
                        foreach (IReference reference in references[ObjectIds.Server])
                        {
                            serverObject.AddReferenceIfMissing(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        }
                    })
                    .Returns(default(ValueTask));
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                IAsyncNodeManager registered = adapted
                    ? new AsyncNodeManagerAdapter(manager.SyncNodeManager)
                    : manager;
                if (forwarded)
                {
                    registered = CreateForwardingFacade(manager);
                }
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [registered]);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                IEventMonitoredItem item = events.CreateMonitoredItem(
                    context, core.Object, serverObject, 1, new MonitoredItemIdFactory(), TimestampsToReturn.Both, 1000,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                    },
                    new EventFilter(), false);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("initial-facade-root", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("InitialFacadeRoot", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddPredefinedNodeAsync(root).ConfigureAwait(false);
                Assert.That(ChangePreparer.GetServer(manager), Is.Null);
                Assert.That(item.MonitoringAllEvents, Is.True);
                Assert.That(root.AreEventsMonitored, Is.False);
                Assert.That(serverObject.ReferenceExists(
                    ReferenceTypeIds.HasNotifier, false, root.NodeId), Is.False);

                await manager.RegisterRootNotifierAsync(root).ConfigureAwait(false);
                await manager.RegisterRootNotifierAsync(root).ConfigureAwait(false);

                Assert.That(serverObject.ReferenceExists(
                    ReferenceTypeIds.HasNotifier, false, root.NodeId), Is.True);
                Assert.That(root.AreEventsMonitored, Is.True);
                Assert.That(manager.MonitoredNodes[root.NodeId].EventMonitoredItems[item.Id], Is.SameAs(item));
                core.Verify(value => value.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Once);
                await manager.SubscribeToAllEventsAsync(context, 1, item, true).ConfigureAwait(false);
                events.DeleteMonitoredItem(item.Id);
            }
        }

        /// <summary>
        /// Keeps a bound candidate private even when another facade over its resource is already published.
        /// </summary>
        [Test]
        public async Task PreparedFacadeDoesNotBorrowAnotherPublishedFacadeOfTheResourceAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [manager]);
                using ChangePreparer.StagingServer staging = new ChangePreparer(server.Object).CreateServer();
                var candidate = new AsyncNodeManagerAdapter(manager.SyncNodeManager);
                staging.Attach(manager);
                staging.RegisteredFacade = candidate;
                staging.Bind();

                Assert.That(staging.IsStaging, Is.False);
                Assert.That(master.IsPublishedNodeManager(manager), Is.True);
                Assert.That(master.IsPublishedNodeManager(candidate), Is.False);
                Assert.That(master.GetPublishedRootNotifierOwner(manager), Is.Null);

                staging.RegisteredFacade = manager;
                Assert.That(master.GetPublishedRootNotifierOwner(manager), Is.SameAs(manager));

                staging.Detach();
                Assert.That(master.GetPublishedRootNotifierOwner(manager), Is.Null);
            }
        }

        /// <summary>
        /// Rejects ambiguous initial registrations instead of choosing one facade's generation ownership.
        /// </summary>
        [Test]
        public async Task AmbiguousInitialFacadesDoNotChooseARootOwnerAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                IAsyncNodeManager first = CreateForwardingFacade(manager);
                IAsyncNodeManager second = CreateForwardingFacade(manager);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [first, second]);

                Assert.That(master.IsPublishedNodeManager(first), Is.True);
                Assert.That(master.IsPublishedNodeManager(second), Is.True);
                Assert.Throws<InvalidOperationException>(() => master.GetPublishedRootNotifierOwner(manager));
            }
        }

        /// <summary>
        /// Creates the custom facade shape used by initial asynchronous registrations.
        /// </summary>
        private static IAsyncNodeManager CreateForwardingFacade(TestableAsyncCustomNodeManager manager)
        {
            var facade = new Mock<IAsyncNodeManager>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
            facade.SetupGet(value => value.NamespaceUris).Returns(manager.NamespaceUris);
            return facade.Object;
        }
    }
}
