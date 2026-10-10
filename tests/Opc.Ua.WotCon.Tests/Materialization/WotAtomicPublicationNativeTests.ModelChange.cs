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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeMetadataOnlyPublicationDoesNotRepeatServerModelChange(bool recoveryRetry)
        {
            bool failAcknowledgment = false;
            var projection = new Mock<IWotRegistryRecoveryProjection>(MockBehavior.Strict);
            projection.Setup(value => value.SynchronizeAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                .Returns(() => failAcknowledgment
                    ? throw new IOException("The first recovery acknowledgment failed.")
                    : default(ValueTask));
            using IDisposable registration = m_registry.RegisterRecoveryProjection(projection.Object);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("event-image-initial")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 1000, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse monitored = await m_session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Neither,
                    [NativeModelChangeItem()], CancellationToken.None).ConfigureAwait(false);
                Assert.That(monitored.Results.Count, Is.EqualTo(1));
                Assert.That(monitored.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                if (recoveryRetry)
                {
                    m_indeterminateDecision = true;
                    await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                        HandoffRequest("event-image-unknown", 1)).ConfigureAwait(false),
                        Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
                    string directory = Path.Combine(m_root, "registry");
                    File.Move(Directory.GetFiles(directory, "manifest.json.tmp-*").Single(),
                        Path.Combine(directory, "manifest.json"));
                    m_indeterminateDecision = false;
                    failAcknowledgment = true;
                    await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync().ConfigureAwait(false),
                        Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>()).ConfigureAwait(false);
                }
                else
                {
                    await m_coordinator.RefreshAsync(HandoffRequest("event-image-changed", 1)).ConfigureAwait(false);
                }

                PublishResponse changed = await PublishNativeModelChangeAsync(subscription.SubscriptionId)
                    .ConfigureAwait(false);
                Assert.That(changed.NotificationMessage.NotificationData.Count, Is.EqualTo(1));
                if (!changed.NotificationMessage.NotificationData[0]
                    .TryGetValue(out EventNotificationList? events) || events is null)
                {
                    throw new InvalidOperationException("The structural notification is not an event list.");
                }
                Assert.That(events.Events.Count, Is.EqualTo(1));
                ArrayOf<Variant> fields = events.Events[0].EventFields;
                Assert.That(fields.Count, Is.EqualTo(3));
                Assert.That(fields[0], Is.EqualTo(Variant.From(Ua.ObjectTypeIds.BaseModelChangeEventType)));
                Assert.That(fields[1], Is.EqualTo(Variant.From(Ua.ObjectIds.Server)));
                Assert.That(fields[2].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("A live NodeManager was batch."));
                var owners = m_server.NodeManagerLifecycle.Registrations;
                uint generation = m_coordinator.Generation;

                if (recoveryRetry)
                {
                    failAcknowledgment = false;
                    Assert.That(await m_coordinator.RecoverAsync().ConfigureAwait(false), Is.True);
                }
                else
                {
                    await m_coordinator.RefreshAsync(HandoffRequest("event-image-unchanged", generation))
                        .ConfigureAwait(false);
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                PublishResponse unchanged = await m_session.PublishAsync(null,
                    [new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscription.SubscriptionId,
                        SequenceNumber = changed.NotificationMessage.SequenceNumber
                    }], timeout.Token).ConfigureAwait(false);
                Assert.That(unchanged.SubscriptionId, Is.EqualTo(subscription.SubscriptionId));
                Assert.That(unchanged.Results.Count, Is.EqualTo(1));
                Assert.That(unchanged.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(unchanged.NotificationMessage.NotificationData.IsEmpty, Is.True,
                    "Metadata-only publication must not release another model-change intent.");
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
                Assert.That(m_coordinator.Generation, Is.EqualTo(generation));
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async Task<PublishResponse> PublishNativeModelChangeAsync(uint subscriptionId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                PublishResponse response = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                if (!response.NotificationMessage.NotificationData.IsEmpty)
                {
                    return response;
                }
            }
            Assert.Fail("The committed structural change did not deliver its Server model-change event.");
            throw new InvalidOperationException("No structural event was delivered.");
        }

        private static MonitoredItemCreateRequest NativeModelChangeItem()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(Ua.ObjectTypeIds.BaseEventType, QualifiedName.From(Ua.BrowseNames.EventType));
            filter.AddSelectClause(Ua.ObjectTypeIds.BaseEventType, QualifiedName.From(Ua.BrowseNames.SourceNode));
            filter.AddSelectClause(Ua.ObjectTypeIds.BaseEventType, QualifiedName.From(Ua.BrowseNames.Message));
            filter.WhereClause.Push(FilterOperator.Equals,
                Variant.FromStructure(new SimpleAttributeOperand
                {
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value,
                    BrowsePath = [QualifiedName.From(Ua.BrowseNames.EventType)]
                }),
                Variant.FromStructure(new LiteralOperand
                {
                    Value = Variant.From(Ua.ObjectTypeIds.BaseModelChangeEventType)
                }));
            return new MonitoredItemCreateRequest
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
                    QueueSize = 10,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(filter)
                }
            };
        }
    }
}
