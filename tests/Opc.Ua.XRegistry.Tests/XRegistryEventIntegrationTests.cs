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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Category("XRegistry")]
    [NonParallelizable]
    public sealed class XRegistryEventIntegrationTests
    {
        [Test]
        public async Task GeneratedFilterReceivesNativeGroupEventThroughServerNotifier()
        {
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(XRegistryEventIntegrationTests),
                Guid.NewGuid().ToString("N"));
            var options = new XRegistryServerOptions
            {
                EventsEnabled = true,
                EventSourceUrl = "https://registry.example.test",
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            var factory = new TestNodeManagerFactory(options);
            var fixture = new ServerFixture<TestServer>(
                telemetry => new TestServer(telemetry, factory))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            TestServer server = await fixture.StartAsync(pkiRoot).ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext channel) = await server
                    .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                    .ConfigureAwait(false);
                try
                {
                    var services = new ServerTestServices(server, channel);
                    ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                        XRegistryWellKnown.XRegistryNamespaceUri);
                    var registryNodeId = new NodeId(XRegistryWellKnown.RegistryObject, ns);
                    EventFilter filter = GroupCreatedEventTypeRecord.EventFilters.Build(
                        server.CurrentInstance.NamespaceUris,
                        new EventRecordDecoderRegistry().RegisterxRegistryDecoders(
                            server.CurrentInstance.NamespaceUris));
                    uint subscriptionId = await CreateSubscriptionAsync(
                        services,
                        requestHeader,
                        registryNodeId,
                        filter).ConfigureAwait(false);

                    XRegistryRegistrationNodeManager manager = factory.Manager!;
                    CreateGroupMethodStateResult created = await manager.OnCreateGroupAsync(
                        manager.SystemContext,
                        null!,
                        registryNodeId,
                        "schemas",
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(created.ServiceResult, Is.EqualTo(ServiceResult.Good));

                    EventFieldList? evt = await CollectAsync(
                        services,
                        requestHeader,
                        subscriptionId,
                        fields => IsGroupCreated(fields, filter, server.CurrentInstance.NamespaceUris))
                        .ConfigureAwait(false);
                    Assert.That(evt, Is.Not.Null);
                    Assert.Multiple(() =>
                    {
                        Assert.That(
                            Field(evt!, filter, BrowseNames.SourceUrl).TryGetValue(out string sourceUrl)
                                ? sourceUrl
                                : string.Empty,
                            Is.EqualTo(options.EventSourceUrl));
                        Assert.That(
                            Field(evt!, filter, BrowseNames.Subject).TryGetValue(out string subject)
                                ? subject
                                : string.Empty,
                            Is.EqualTo("/groups/schemas"));
                        Assert.That(
                            Field(evt!, filter, BrowseNames.Epoch).TryGetValue(out uint epoch)
                                ? epoch
                                : 0u,
                            Is.EqualTo(1u));
                    });
                    await services.DeleteSubscriptionsAsync(
                        Stamp(requestHeader),
                        new ArrayOf<uint>(new[] { subscriptionId })).ConfigureAwait(false);
                }
                finally
                {
                    await server.CloseSessionAsync(
                        channel,
                        Stamp(requestHeader),
                        true,
                        RequestLifetime.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server.Dispose();
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        private static async Task<uint> CreateSubscriptionAsync(
            ServerTestServices services,
            RequestHeader requestHeader,
            NodeId sourceNodeId,
            EventFilter filter)
        {
            CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                Stamp(requestHeader),
                100,
                1200,
                20,
                0,
                true,
                0).ConfigureAwait(false);
            ArrayOf<MonitoredItemCreateRequest> items =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = sourceNodeId,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 77,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter)
                    }
                }
            ];
            CreateMonitoredItemsResponse created = await services.CreateMonitoredItemsAsync(
                Stamp(requestHeader),
                subscription.SubscriptionId,
                TimestampsToReturn.Neither,
                items).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscription.SubscriptionId;
        }

        private static async Task<EventFieldList?> CollectAsync(
            ServerTestServices services,
            RequestHeader requestHeader,
            uint subscriptionId,
            Func<EventFieldList, bool> predicate)
        {
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await services.PublishAsync(
                    Stamp(requestHeader),
                    acknowledgements,
                    timeout.Token).ConfigureAwait(false);
                acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(sequenceNumber =>
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = sequenceNumber
                    });
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (!notification.TryGetValue(out EventNotificationList? events))
                    {
                        continue;
                    }
                    foreach (EventFieldList evt in events.Events)
                    {
                        if (evt.ClientHandle == 77 && predicate(evt))
                        {
                            return evt;
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsGroupCreated(
            EventFieldList evt,
            EventFilter filter,
            NamespaceTable namespaceUris)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.GroupCreatedEventType,
                namespaceUris);
            return Field(evt, filter, global::Opc.Ua.BrowseNames.EventType)
                .TryGetValue(out NodeId actual) && actual == expected;
        }

        private static Variant Field(EventFieldList evt, EventFilter filter, string browseName)
        {
            for (int index = 0; index < filter.SelectClauses.Count; index++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[index];
                if (clause.BrowsePath.Count > 0 &&
                    string.Equals(clause.BrowsePath[^1].Name, browseName, StringComparison.Ordinal))
                {
                    return evt.EventFields[index];
                }
            }
            return Variant.Null;
        }

        private static RequestHeader Stamp(RequestHeader requestHeader)
        {
            requestHeader.Timestamp = DateTimeUtc.Now;
            return requestHeader;
        }

        private sealed class TestServer : ReferenceServer
        {
            public TestServer(ITelemetryContext telemetry, INodeManagerFactory factory)
                : base(telemetry)
            {
                AddNodeManager(factory);
            }
        }

        private sealed class TestNodeManagerFactory : INodeManagerFactory
        {
            public TestNodeManagerFactory(XRegistryServerOptions options)
            {
                m_options = options;
            }

            public ArrayOf<string> NamespacesUris => [XRegistryWellKnown.XRegistryNamespaceUri];

            public XRegistryRegistrationNodeManager? Manager { get; private set; }

            public INodeManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return Manager = new XRegistryRegistrationNodeManager(
                    server,
                    configuration,
                    m_options);
            }

            private readonly XRegistryServerOptions m_options;
        }
    }
}
