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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Discovers an asset over a real client session and watches it move in
    /// <c>AssetsByAssetId</c> when its <c>AssetId</c> is written: the
    /// category's NodeVersion changes and a <c>GeneralModelChangeEvent</c>
    /// names the category (OPC 10000-110 §8.2, "AMB Asset Discovery by
    /// AssetId").
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbDiscoverySessionTests
    {
        [Test]
        public async Task AClientFindsTheAssetAndSeesItMoveAsync()
        {
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbDiscoverySessionTests),
                Guid.NewGuid().ToString("N"));
            DeviceState? device = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AmbDiscoverySessionTests),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor:4711").ConfigureAwait(false);
                        device = sensor.Device;
                        await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset.WithConfigurableAssetId("S-1"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await clientFixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki")).ConfigureAwait(false);
            using ISession session = await clientFixture.ConnectAsync(
                new Uri(server.EndpointUrl),
                SecurityPolicies.None).ConfigureAwait(false);

            NodeId byAssetId = ExpandedNodeId.ToNodeId(ObjectIds.AssetsByAssetId, session.NamespaceUris);
            NodeId findAlias = ExpandedNodeId.ToNodeId(MethodIds.AssetsByAssetId_FindAlias, session.NamespaceUris);
            NodeId nodeVersion = server.Manager.FindPredefinedNode<AliasNameCategoryState>(byAssetId)!
                .GetNodeVersionProperty()!.NodeId;

            ArrayOf<AliasNameDataType> before = await FindAsync(session, byAssetId, findAlias, "S-1")
                .ConfigureAwait(false);
            DataValue versionBefore = await session.ReadValueAsync(nodeVersion, CancellationToken.None)
                .ConfigureAwait(false);

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
                                    Field(Ua.ObjectTypeIds.BaseEventType, "EventType"),
                                    Field(Ua.ObjectTypeIds.GeneralModelChangeEventType, "Changes")
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

            WriteResponse write = await session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = device!.AssetId!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From("S-2"))
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            bool announced = await WaitForModelChangeAsync(
                session,
                subscription.SubscriptionId,
                byAssetId,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            ArrayOf<AliasNameDataType> old = await FindAsync(session, byAssetId, findAlias, "S-1")
                .ConfigureAwait(false);
            ArrayOf<AliasNameDataType> moved = await FindAsync(session, byAssetId, findAlias, "S-2")
                .ConfigureAwait(false);
            DataValue versionAfter = await session.ReadValueAsync(nodeVersion, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(before.Count, Is.EqualTo(1));
                Assert.That(
                    ExpandedNodeId.ToNodeId(before[0].ReferencedNodes[0], session.NamespaceUris),
                    Is.EqualTo(device.NodeId));
                Assert.That(StatusCode.IsGood(write.Results[0]), Is.True, write.Results[0].ToString());
                Assert.That(announced, Is.True, "a GeneralModelChangeEvent names AssetsByAssetId");
                Assert.That(old.Count, Is.Zero);
                Assert.That(moved.Count, Is.EqualTo(1));
                Assert.That(versionAfter.WrappedValue, Is.Not.EqualTo(versionBefore.WrappedValue));
            });
        }

        private static async Task<ArrayOf<AliasNameDataType>> FindAsync(
            ISession session,
            NodeId category,
            NodeId findAlias,
            string pattern)
        {
            ArrayOf<Variant> output = await session.CallAsync(
                category,
                findAlias,
                CancellationToken.None,
                Variant.From(pattern),
                Variant.From(NodeId.Null)).ConfigureAwait(false);
            Assert.That(
                output[0].TryGetValue(out ArrayOf<AliasNameDataType> aliases, session.MessageContext),
                Is.True);
            return aliases;
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

        private static async Task<bool> WaitForModelChangeAsync(
            ISession session,
            uint subscriptionId,
            NodeId affected,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
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
                        if (fields.EventFields.Count < 2 ||
                            !fields.EventFields[0].TryGetValue(out NodeId eventType) ||
                            eventType != Ua.ObjectTypeIds.GeneralModelChangeEventType ||
                            !fields.EventFields[1].TryGetValue(
                                out ArrayOf<ModelChangeStructureDataType> changes,
                                session.MessageContext))
                        {
                            continue;
                        }
                        foreach (ModelChangeStructureDataType change in changes)
                        {
                            if (change.Affected == affected)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
