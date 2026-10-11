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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// The ApplicationRegistrationChangedAuditEvents of RegisterApplication,
    /// UpdateApplication and UnregisterApplication (OPC 10000-12 6.5.12)
    /// reach a subscriber on Server.EventNotifier.
    /// </summary>
    /// <remarks>
    /// A Client only receives an Event if it has ReceiveEvents on the
    /// EventType Node (OPC 10000-3 8.55), and the GDS NodeSet grants it on
    /// ApplicationRegistrationChangedAuditEventType (AuditEventDefaultPermissions)
    /// only to SecurityAdmin. A DiscoveryAdmin may call the Methods but
    /// does not receive their audit events; the CTT GDS Application
    /// Directory test cases 011, 018 and 028 subscribe as the DiscoveryAdmin
    /// user and therefore have to subscribe with a SecurityAdmin Session.
    /// </remarks>
    [TestFixture]
    [Category("GDS")]
    [Category("GDSApplicationDirectory")]
    [Category("Audit")]
    public sealed class GdsApplicationRegistrationAuditEventTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task AuditEventSetUpAsync()
        {
            m_directoryNodeId = ToNodeId(ObjectIds.Directory);
            m_eventTypeId = ToNodeId(ObjectTypeIds.ApplicationRegistrationChangedAuditEventType);

            m_discoveryAdminSession = await ClientFixture
                .ConnectAsync(
                    ServerUrl,
                    SecurityPolicies.Basic256Sha256,
                    default,
                    new UserIdentity("DiscoveryAdmin", "demo"u8))
                .ConfigureAwait(false);
            if (!m_discoveryAdminSession.MessageContext.Factory.ContainsEncodeableType(
                DataTypeIds.ApplicationRecordDataType))
            {
                m_discoveryAdminSession.MessageContext.Factory.Builder.AddOpcUaGds().Commit();
            }
        }

        [OneTimeTearDown]
        public async Task AuditEventTearDownAsync()
        {
            if (m_discoveryAdminSession != null)
            {
                await m_discoveryAdminSession.CloseAsync(5000, true).ConfigureAwait(false);
                m_discoveryAdminSession.Dispose();
                m_discoveryAdminSession = null!;
            }
        }

        [Test]
        public async Task ApplicationRegistrationAuditEventsReachSecurityAdminSubscriberAsync()
        {
            // The fixture Session is a SecurityAdmin; the Methods are called by
            // a DiscoveryAdmin, like the CTT does.
            uint securityAdminSubscription = await SubscribeToServerEventsAsync(Session)
                .ConfigureAwait(false);
            uint discoveryAdminSubscription = await SubscribeToServerEventsAsync(m_discoveryAdminSession)
                .ConfigureAwait(false);
            try
            {
                ApplicationRecordDataType record = CreateTestApplicationRecord("AuditEvents");
                NodeId applicationId = await RegisterApplicationAsync(record).ConfigureAwait(false);
                record.ApplicationId = applicationId;
                record.ProductUri = "urn:opcfoundation.org:tests:test:product:AuditEventsUpdated";
                await CallDirectoryMethodAsync(
                    MethodIds.Directory_UpdateApplication,
                    new Variant(new ExtensionObject(record))).ConfigureAwait(false);
                await CallDirectoryMethodAsync(
                    MethodIds.Directory_UnregisterApplication,
                    new Variant(applicationId)).ConfigureAwait(false);

                NodeId[] expectedMethods =
                [
                    ToNodeId(MethodIds.Directory_RegisterApplication),
                    ToNodeId(MethodIds.Directory_UpdateApplication),
                    ToNodeId(MethodIds.Directory_UnregisterApplication)
                ];

                List<RegistrationAuditEvent> received = await CollectRegistrationEventsAsync(
                    Session,
                    events => expectedMethods.All(m => events.Any(e => e.MethodId == m)))
                    .ConfigureAwait(false);

                foreach (NodeId methodId in expectedMethods)
                {
                    RegistrationAuditEvent auditEvent = received.FirstOrDefault(e => e.MethodId == methodId)!;
                    Assert.That(auditEvent, Is.Not.Null,
                        $"No ApplicationRegistrationChangedAuditEvent for Method {methodId}; received " +
                        $"{received.Count} event(s) of that type.");
                    Assert.That(auditEvent.SourceNode, Is.EqualTo(m_directoryNodeId),
                        "The SourceNode of the audit event shall be the Directory Object.");
                }

                // The DiscoveryAdmin has no ReceiveEvents on the EventType.
                List<RegistrationAuditEvent> discoveryAdminEvents = await CollectRegistrationEventsAsync(
                    m_discoveryAdminSession,
                    _ => false,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                Assert.That(discoveryAdminEvents, Is.Empty,
                    "Only SecurityAdmin has ReceiveEvents on ApplicationRegistrationChangedAuditEventType.");
            }
            finally
            {
                await DeleteSubscriptionAsync(Session, securityAdminSubscription).ConfigureAwait(false);
                await DeleteSubscriptionAsync(m_discoveryAdminSession, discoveryAdminSubscription)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<uint> SubscribeToServerEventsAsync(ISession session)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 1000, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);

            var filter = new EventFilter
            {
                SelectClauses =
                [
                    Field(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.EventType),
                    Field(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.SourceNode),
                    Field(Ua.ObjectTypeIds.AuditUpdateMethodEventType, Ua.BrowseNames.MethodId)
                ],
                WhereClause = new ContentFilter()
            };
            CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                null,
                subscription.SubscriptionId,
                TimestampsToReturn.Neither,
                new MonitoredItemCreateRequest[]
                {
                    new()
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = Ua.ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            Filter = new ExtensionObject(filter),
                            QueueSize = 100,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(items.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(items.Results[0].StatusCode), Is.True,
                $"Subscribing to the events of the Server failed: {items.Results[0].StatusCode}");
            return subscription.SubscriptionId;
        }

        private static SimpleAttributeOperand Field(NodeId typeDefinitionId, string browseName)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = [new QualifiedName(browseName)],
                AttributeId = Attributes.Value
            };
        }

        /// <summary>
        /// Publishes until <paramref name="isComplete"/> returns true or the
        /// timeout expires and returns the ApplicationRegistrationChangedAuditEvents.
        /// </summary>
        private async Task<List<RegistrationAuditEvent>> CollectRegistrationEventsAsync(
            ISession session,
            Func<List<RegistrationAuditEvent>, bool> isComplete,
            TimeSpan? timeout = null)
        {
            var received = new List<RegistrationAuditEvent>();
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!isComplete(received) && DateTime.UtcNow < deadline)
            {
                PublishResponse response = await session.PublishAsync(
                    null,
                    Array.Empty<SubscriptionAcknowledgement>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (!notification.TryGetValue(out EventNotificationList? eventList))
                    {
                        continue;
                    }
                    foreach (EventFieldList fields in eventList.Events)
                    {
                        if (fields.EventFields.Count == 3 &&
                            fields.EventFields[0].TryGetValue(out NodeId eventType) &&
                            eventType == m_eventTypeId)
                        {
                            fields.EventFields[1].TryGetValue(out NodeId sourceNode);
                            fields.EventFields[2].TryGetValue(out NodeId methodId);
                            received.Add(new RegistrationAuditEvent(sourceNode, methodId));
                        }
                    }
                }
            }
            return received;
        }

        private static async Task DeleteSubscriptionAsync(ISession session, uint subscriptionId)
        {
            try
            {
                await session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { subscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // best effort
            }
        }

        private async Task<NodeId> RegisterApplicationAsync(ApplicationRecordDataType record)
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                MethodIds.Directory_RegisterApplication,
                new Variant(new ExtensionObject(record))).ConfigureAwait(false);
            Assert.That(result.OutputArguments.Count, Is.EqualTo(1));
            return (NodeId)result.OutputArguments[0];
        }

        private async Task<CallMethodResult> CallDirectoryMethodAsync(
            ExpandedNodeId methodId,
            Variant argument)
        {
            CallResponse response = await m_discoveryAdminSession.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new()
                    {
                        ObjectId = m_directoryNodeId,
                        MethodId = ToNodeId(methodId),
                        InputArguments = new Variant[] { argument }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"Calling {methodId} as DiscoveryAdmin failed: {response.Results[0].StatusCode}");
            return response.Results[0];
        }

        private sealed record RegistrationAuditEvent(NodeId SourceNode, NodeId MethodId);

        private NodeId m_directoryNodeId;
        private NodeId m_eventTypeId;
        private ISession m_discoveryAdminSession;
    }
}
