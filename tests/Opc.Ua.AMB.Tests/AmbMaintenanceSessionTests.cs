// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Follows a maintenance activity over a real client session: every
    /// transition arrives as an event that carries the state of the
    /// <c>MaintenanceEventStateMachineType</c> and the planned date
    /// (OPC 10000-110 §12).
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbMaintenanceSessionTests
    {
        [Test]
        public async Task AClientFollowsAMaintenanceActivityAsync()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "amb-maintenance-" + Guid.NewGuid().ToString("N"));
            var planned = new DateTimeUtc(new DateTime(2027, 3, 1, 8, 0, 0, DateTimeKind.Utc));
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AmbMaintenanceSessionTests),
                    builder => builder.WithMaintenance(
                        "AnnualInspection",
                        AmbConditionClass.Inspection,
                        details =>
                        {
                            details.Description = new LocalizedText("Annual inspection.");
                            details.PlannedDate = planned;
                        }))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity inspection = asset.Maintenance!.Activities[0];
            var typeId = new NodeId("AssetMaintenanceActivityConditionType", server.Manager.TypeNamespaceIndex);

            using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await clientFixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki")).ConfigureAwait(false);
            using ISession session = await clientFixture.ConnectAsync(
                new Uri(server.EndpointUrl),
                SecurityPolicies.None).ConfigureAwait(false);
            NodeId diMaintenance = ExpandedNodeId.ToNodeId(
                Opc.Ua.Di.ObjectTypeIds.MaintenanceRequiredAlarmType,
                session.NamespaceUris);
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
                                    Field(
                                        diMaintenance,
                                        new QualifiedName("MaintenanceState", amb),
                                        new QualifiedName("CurrentState"),
                                        new QualifiedName("Id")),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("Message")),
                                    Field(diMaintenance, new QualifiedName("PlannedDate", amb)),
                                    Field(Ua.ObjectTypeIds.BaseEventType, new QualifiedName("ConditionClassId"))
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

            await inspection.StartAsync().ConfigureAwait(false);
            await inspection.FinishAsync(new LocalizedText("Inspection done, no findings.")).ConfigureAwait(false);
            List<EventFieldList> events = await CollectEventsAsync(session, subscription.SubscriptionId, typeId, 2)
                .ConfigureAwait(false);

            Assert.That(events, Has.Count.EqualTo(2), "one event per transition");
            Assert.Multiple(() =>
            {
                Assert.That(
                    State(events[0]),
                    Is.EqualTo(new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Executing, amb)));
                Assert.That(events[0].EventFields[2].TryGetValue(out LocalizedText started), Is.True);
                Assert.That(started.Text, Is.EqualTo("Annual inspection."));
                Assert.That(events[0].EventFields[3].TryGetValue(out DateTimeUtc date), Is.True);
                Assert.That(date, Is.EqualTo(planned));
                Assert.That(
                    events[0].EventFields[4].TryGetValue(out NodeId conditionClass) ? conditionClass : NodeId.Null,
                    Is.EqualTo(AmbConditionClass.Inspection.GetTypeId(session.NamespaceUris)));
                Assert.That(
                    State(events[1]),
                    Is.EqualTo(new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Finished, amb)));
                Assert.That(events[1].EventFields[2].TryGetValue(out LocalizedText finished), Is.True);
                Assert.That(finished.Text, Is.EqualTo("Inspection done, no findings."));
            });
        }

        private static NodeId State(EventFieldList fields)
        {
            return fields.EventFields[1].TryGetValue(out NodeId state) ? state : NodeId.Null;
        }

        private static SimpleAttributeOperand Field(NodeId typeDefinitionId, params QualifiedName[] browsePath)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = browsePath.ToArrayOf(),
                AttributeId = Attributes.Value
            };
        }

        private static async Task<List<EventFieldList>> CollectEventsAsync(
            ISession session,
            uint subscriptionId,
            NodeId eventType,
            int expected)
        {
            var collected = new List<EventFieldList>();
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
            while (collected.Count < expected && DateTime.UtcNow < deadline)
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
                            collected.Add(fields);
                        }
                    }
                }
            }
            return collected;
        }
    }
}
