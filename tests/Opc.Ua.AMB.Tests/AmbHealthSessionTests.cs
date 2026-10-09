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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Receives an asset health alarm over a real client session: the event
    /// names the asset, the condition class and the potential root causes,
    /// and the client acknowledges it (OPC 10000-110 §9.3, §9.4).
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbHealthSessionTests
    {
        [Test]
        public async Task AClientReceivesAndAcknowledgesAHealthAlarmAsync()
        {
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbHealthSessionTests),
                Guid.NewGuid().ToString("N"));
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(AmbHealthSessionTests),
                    health => health
                        .WithDeviceHealth(deriveFromAlarms: true)
                        .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure))
                    .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IAssetHealth health = asset.Health!;
            IAssetHealthAlarm fieldbus = health.Alarms[0];
            AcknowledgeableConditionState alarm = await server
                .FindNodeAsync<AcknowledgeableConditionState>(fieldbus.NodeId)
                .ConfigureAwait(false);
            var typeId = new NodeId("AssetFailureAlarmType", server.Manager.TypeNamespaceIndex);
            var cable = new NodeId("Cable", server.Manager.InstanceNamespaceIndex);

            using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await clientFixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki")).ConfigureAwait(false);
            using ISession session = await clientFixture.ConnectAsync(
                new Uri(server.EndpointUrl),
                SecurityPolicies.None).ConfigureAwait(false);
            session.Factory.Builder.AddOpcUaAMB().Commit();
            NodeId diFailure = ExpandedNodeId.ToNodeId(Opc.Ua.Di.ObjectTypeIds.FailureAlarmType, session.NamespaceUris);
            ushort amb = (ushort)session.NamespaceUris.GetIndex(Namespaces.AMB);

            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 50, 1000, 100, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                null,
                subscription.SubscriptionId,
                TimestampsToReturn.Neither,
                [
                    new MonitoredItemCreateRequest
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
                            QueueSize = 100,
                            DiscardOldest = true,
                            Filter = new ExtensionObject(new EventFilter
                            {
                                SelectClauses =
                                [
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("EventType")),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("SourceNode")),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("Severity")),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("ConditionClassId")),
                                    Field(diFailure, new QualifiedName("PotentialRootCauses", amb)),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("EventId"))
                                ],
                                WhereClause = new ContentFilter()
                            })
                        }
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(items.Results[0].StatusCode),
                Is.True,
                items.Results[0].StatusCode.ToString());

            await fieldbus.RaiseAsync(
                700,
                new LocalizedText("The fieldbus is down."),
                [AssetRootCauses.Of(cable, new LocalizedText("Cable cut"))]).ConfigureAwait(false);
            EventFieldList? received = await WaitForEventAsync(session, subscription.SubscriptionId, typeId)
                .ConfigureAwait(false);
            Assert.That(received, Is.Not.Null, "the alarm event arrives");
            ArrayOf<Variant> fields = received!.EventFields;

            CallMethodResult acknowledged = (await session.CallAsync(
                null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = alarm.NodeId,
                        MethodId = alarm.Acknowledge!.NodeId,
                        InputArguments = [fields[5], Variant.From(new LocalizedText("seen"))]
                    }
                ],
                CancellationToken.None).ConfigureAwait(false)).Results[0];
            EventFieldList? acknowledgement = await WaitForEventAsync(session, subscription.SubscriptionId, typeId)
                .ConfigureAwait(false);
            await fieldbus.ClearAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(fields[1].TryGetValue(out NodeId source) ? source : NodeId.Null, Is.EqualTo(device.NodeId));
                Assert.That(fields[2].TryGetValue(out ushort severity) ? severity : (ushort)0, Is.EqualTo((ushort)700));
                Assert.That(
                    fields[3].TryGetValue(out NodeId conditionClass) ? conditionClass : NodeId.Null,
                    Is.EqualTo(AmbConditionClass.ConnectionFailure.GetTypeId(session.NamespaceUris)));
                Assert.That(
                    fields[4].TryGetValue(out ArrayOf<RootCauseDataType> rootCauses, session.MessageContext),
                    Is.True,
                    "PotentialRootCauses is selected through the Device Integration type");
                Assert.That(rootCauses.Count, Is.EqualTo(1));
                Assert.That(rootCauses[0].RootCauseId, Is.EqualTo(cable));
                Assert.That(rootCauses[0].RootCause.Text, Is.EqualTo("Cable cut"));
                Assert.That(
                    StatusCode.IsGood(acknowledged.StatusCode),
                    Is.True,
                    acknowledged.StatusCode.ToString());
                Assert.That(alarm.Retain!.Value, Is.False, "acknowledged and cleared");
                Assert.That(acknowledgement, Is.Not.Null, "the acknowledgement is reported as an event");
                Assert.That(
                    acknowledgement!.EventFields[5],
                    Is.Not.EqualTo(fields[5]),
                    "the acknowledgement event has an EventId of its own");
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
            });
        }

        private static SimpleAttributeOperand Field(NodeId typeDefinitionId, QualifiedName browseName)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = [browseName],
                AttributeId = Attributes.Value
            };
        }

        private static async Task<EventFieldList?> WaitForEventAsync(
            ISession session,
            uint subscriptionId,
            NodeId eventType)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
            while (DateTime.UtcNow < deadline)
            {
                PublishResponse publish;
                try
                {
                    publish = acknowledgements.Count == 0
                        ? await session.PublishWithTimeoutAsync(1000).ConfigureAwait(false)
                        : await session.PublishWithTimeoutAsync(acknowledgements, 1000).ConfigureAwait(false);
                    acknowledgements = [];
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    continue;
                }

                if (publish.SubscriptionId == subscriptionId &&
                    publish.NotificationMessage.SequenceNumber != 0)
                {
                    acknowledgements =
                    [
                        new SubscriptionAcknowledgement
                        {
                            SubscriptionId = publish.SubscriptionId,
                            SequenceNumber = publish.NotificationMessage.SequenceNumber
                        }
                    ];
                }

                foreach (ExtensionObject notification in publish.NotificationMessage.NotificationData)
                {
                    if (!notification.TryGetValue(out EventNotificationList? events) || events == null)
                    {
                        continue;
                    }
                    foreach (EventFieldList fields in events.Events)
                    {
                        if (fields.EventFields.Count > 0 &&
                            fields.EventFields[0].TryGetValue(out NodeId type) &&
                            type == eventType)
                        {
                            return fields;
                        }
                    }
                }
            }
            return null;
        }
    }
}
