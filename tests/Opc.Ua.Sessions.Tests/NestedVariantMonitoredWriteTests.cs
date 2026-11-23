// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// A deeply nested Variant written to a variable with a monitored item is
    /// copied by the server for the data change snapshot. The copy recursed
    /// through frames of several KB per nesting level, so a value of about
    /// 1 KB that the decoder accepts overflowed the stack of the thread pool
    /// thread and terminated the server process.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [NonParallelizable]
    public class NestedVariantMonitoredWriteTests : TestFixture
    {
        /// <summary>
        /// Below the decoder's nesting limit for a WriteRequest and the
        /// encoder's for the PublishResponse that carries the value back.
        /// </summary>
        private const int kDepth = 190;

        [Test]
        public async Task NestedVariantWrittenToAMonitoredVariableIsPublishedAsync()
        {
            ISession session = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                var nodeId = ExpandedNodeId.ToNodeId(Constants.ScalarStaticArrayVariant, session.NamespaceUris);
                Assert.That(
                    await WriteAsync(session, nodeId, Nested(1)).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));

                CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                    null, 100, 600, 5, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
                CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                    null,
                    subscription.SubscriptionId,
                    TimestampsToReturn.Neither,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 0,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    (await PublishUntilDataAsync(session).ConfigureAwait(false)).IsNull,
                    Is.False,
                    "the initial value is published");

                Variant nested = Nested(kDepth);
                Assert.That(
                    await WriteAsync(session, nodeId, nested).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));

                Variant published = await PublishUntilDataAsync(session).ConfigureAwait(false);
                Assert.That(published, Is.EqualTo(nested));

                ReadResponse read = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.Results[0].WrappedValue, Is.EqualTo(nested));

                Assert.That(
                    await WriteAsync(session, nodeId, Nested(1)).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
                await session.DeleteSubscriptionsAsync(
                    null, [subscription.SubscriptionId], CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// A Variant array whose single element is a Variant array, and so on,
        /// <paramref name="depth"/> times around an Int32.
        /// </summary>
        private static Variant Nested(int depth)
        {
            var value = new Variant(1);
            for (int ii = 0; ii < depth; ii++)
            {
                value = new Variant(new Variant[] { value }.ToArrayOf());
            }
            return value;
        }

        private static async Task<StatusCode> WriteAsync(ISession session, NodeId nodeId, Variant value)
        {
            WriteResponse response = await session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<Variant> PublishUntilDataAsync(ISession session)
        {
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            for (int ii = 0; ii < 20; ii++)
            {
                PublishResponse response = await session.PublishAsync(
                    null, acknowledgements, CancellationToken.None).ConfigureAwait(false);
                NotificationMessage message = response.NotificationMessage;
                acknowledgements = message.NotificationData.Count > 0
                    ?
                    [
                        new SubscriptionAcknowledgement
                        {
                            SubscriptionId = response.SubscriptionId,
                            SequenceNumber = message.SequenceNumber
                        }
                    ]
                    : default;
                foreach (ExtensionObject data in message.NotificationData)
                {
                    if (data.TryGetValue(out DataChangeNotification? change) &&
                        change!.MonitoredItems.Count > 0)
                    {
                        return change.MonitoredItems[change.MonitoredItems.Count - 1].Value.WrappedValue;
                    }
                }
            }
            return Variant.Null;
        }
    }
}
