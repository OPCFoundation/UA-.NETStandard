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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Performs real ordinary Read and monitored Publish operations on the borrowed shared Session.
    /// </summary>
    internal sealed class SharedServiceProbe
    {
        /// <summary>
        /// Gets validated Read responses.
        /// </summary>
        internal int Reads { get; private set; }

        /// <summary>
        /// Gets actual Publish responses, including any keep-alives.
        /// </summary>
        internal int Publishes { get; private set; }

        /// <summary>
        /// Gets decoded monitored-item notifications.
        /// </summary>
        internal int Notifications { get; private set; }

        /// <summary>
        /// Validates changing server-time values while the PubSub flow remains established.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal async Task RunAsync(ISession session, CancellationToken cancellationToken)
        {
            ReadResponse read = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            if (read.Results.Count != 1 ||
                StatusCode.IsBad(read.Results[0].StatusCode) ||
                !read.Results[0].WrappedValue.TryGetValue(out DateTimeUtc _))
            {
                throw new InvalidOperationException("The ordinary Read did not return a valid server timestamp.");
            }
            Reads++;
            CreateSubscriptionResponse created = await session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0, cancellationToken).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(
                    null, created.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 100,
                                QueueSize = 10,
                                DiscardOldest = true
                            }
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                if (monitored.Results.Count != 1 || StatusCode.IsBad(monitored.Results[0].StatusCode))
                {
                    throw new InvalidOperationException("The ordinary monitored item was not created.");
                }
                DateTimeUtc first = default;
                bool changed = false;
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
                while (true)
                {
                    PublishResponse published = await session.PublishAsync(null, acknowledgements, cancellationToken)
                        .ConfigureAwait(false);
                    Publishes++;
                    if (published.SubscriptionId != created.SubscriptionId)
                    {
                        throw new InvalidOperationException("Publish returned another subscription.");
                    }
                    foreach (ExtensionObject notification in published.NotificationMessage.NotificationData)
                    {
                        if (!notification.TryGetValue(out DataChangeNotification? change) || change == null)
                        {
                            continue;
                        }
                        foreach (MonitoredItemNotification item in change.MonitoredItems)
                        {
                            if (item.ClientHandle != 1 ||
                                StatusCode.IsBad(item.Value.StatusCode) ||
                                !item.Value.WrappedValue.TryGetValue(out DateTimeUtc timestamp))
                            {
                                throw new InvalidOperationException("The ordinary Publish notification is invalid.");
                            }
                            Notifications++;
                            if (!first.IsNull && timestamp != first)
                            {
                                changed = true;
                            }
                            if (first.IsNull)
                            {
                                first = timestamp;
                            }
                        }
                    }
                    if (changed && Publishes >= 2)
                    {
                        return;
                    }
                    acknowledgements = published.NotificationMessage.NotificationData.Count == 0
                        ? []
                        :
                        [
                            new SubscriptionAcknowledgement
                            {
                                SubscriptionId = created.SubscriptionId,
                                SequenceNumber = published.NotificationMessage.SequenceNumber
                            }
                        ];
                }
            }
            finally
            {
                await DeleteSubscriptionAsync(session, created.SubscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks teardown independently of the cancelled operation deadline.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task DeleteSubscriptionAsync(ISession session, uint subscriptionId)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                null, [subscriptionId], cleanup.Token).ConfigureAwait(false);
            if (deleted.Results.Count != 1 || StatusCode.IsBad(deleted.Results[0]))
            {
                throw new InvalidOperationException("The ordinary subscription did not close cleanly.");
            }
        }
    }
}
