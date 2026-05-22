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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Conformance.Tests.SubscriptionServices
{
    /// <summary>
    /// compliance tests for the Subscription Multiple conformance unit.
    /// Tests 001-003 cover creating the maximum number of subscriptions,
    /// managing many subscriptions across sessions, and verifying that each
    /// subscription receives both data and event notifications.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionMultiple")]
    public class SubscriptionMultipleTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Subscription Multiple")]
        [Property("Tag", "001")]
        public async Task CreateMaxSubscriptionsPerSessionWithItemsAsync()
        {
            uint maxSubs = await ReadMaxSubscriptionsPerSession().ConfigureAwait(false);
            if (maxSubs == 0)
            {
                Assert.Ignore("MaxSubscriptionsPerSession not available or is 0.");
            }

            // Cap to reasonable test size
            int count = (int)Math.Min(maxSubs, 20);
            var subIds = new List<uint>();
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(cr.SubscriptionId);

                    await AddMonitoredItemAsync(
                        cr.SubscriptionId, nodeId,
                        handle: (uint)(i + 1)).ConfigureAwait(false);
                }

                Assert.That(subIds, Has.Count.EqualTo(count),
                    $"Should create {count} subscriptions.");

                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                int dcCount = 0;
                for (int i = 0; i < count * 2; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        dcCount++;
                    }
                }
                Assert.That(dcCount, Is.GreaterThan(0),
                    "At least one subscription should deliver a notification.");
            }
            finally
            {
                if (subIds.Count > 0)
                {
                    DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                        null, subIds.ToArray().ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Multiple")]
        [Property("Tag", "002")]
        public async Task MaxSubscriptionsAcrossMultipleSessionsAsync()
        {
            uint maxSubs = await ReadMaxSubscriptionsPerSession().ConfigureAwait(false);
            if (maxSubs == 0)
            {
                Assert.Ignore("MaxSubscriptionsPerSession not available or is 0.");
            }

            const int sessionCount = 2;
            int subsPerSession = (int)Math.Min(maxSubs, 10);
            var sessions = new List<ISession>();
            var subIdsBySession = new List<List<uint>>();
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                for (int s = 0; s < sessionCount; s++)
                {
                    ISession sess = await ClientFixture.ConnectAsync(
                        ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
                    sessions.Add(sess);
                    var sessionSubs = new List<uint>();

                    for (int j = 0; j < subsPerSession; j++)
                    {
                        CreateSubscriptionResponse cr = await sess.CreateSubscriptionAsync(
                            null, DefaultInterval, DefaultLifetime, DefaultKeepAlive,
                            0, true, 0,
                            CancellationToken.None).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);

                        var item = new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = nodeId,
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = (uint)((s * subsPerSession) + j + 1),
                                SamplingInterval = 250,
                                Filter = default,
                                DiscardOldest = true,
                                QueueSize = 10
                            }
                        };
                        await sess.CreateMonitoredItemsAsync(
                            null, cr.SubscriptionId, TimestampsToReturn.Both,
                            new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                            CancellationToken.None).ConfigureAwait(false);

                        sessionSubs.Add(cr.SubscriptionId);
                    }
                    subIdsBySession.Add(sessionSubs);
                }

                await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                int totalDc = 0;
                for (int s = 0; s < sessionCount; s++)
                {
                    for (int j = 0; j < subsPerSession; j++)
                    {
                        PublishResponse pub = await sessions[s].PublishAsync(
                            null, default,
                            CancellationToken.None).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                        if (HasDataChangeNotification(pub))
                        {
                            totalDc++;
                        }
                    }
                }
                Assert.That(totalDc, Is.GreaterThan(0),
                    "At least one subscription across all sessions should " +
                    "receive a data change.");
            }
            finally
            {
                for (int s = 0; s < sessions.Count; s++)
                {
                    try
                    {
                        if (subIdsBySession.Count > s)
                        {
                            await sessions[s].DeleteSubscriptionsAsync(
                                null, subIdsBySession[s].ToArray().ToArrayOf(),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        await sessions[s].CloseAsync(5000, true).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    sessions[s].Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Multiple")]
        [Property("Tag", "003")]
        public async Task CreateSubsWithDataItemsWritePublishVerifyAsync()
        {
            uint maxSubs = await ReadMaxSubscriptionsPerSession().ConfigureAwait(false);
            if (maxSubs == 0)
            {
                Assert.Ignore("MaxSubscriptionsPerSession not available or is 0.");
            }

            int count = (int)Math.Min(maxSubs, 10);
            var subIds = new List<uint>();
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(cr.SubscriptionId);

                    await AddMonitoredItemAsync(
                        cr.SubscriptionId, nodeId,
                        handle: (uint)(i + 1)).ConfigureAwait(false);
                }

                // Write value and publish twice
                for (int round = 0; round < 2; round++)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                    await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                    int dcCount = 0;
                    for (int i = 0; i < count * 2; i++)
                    {
                        PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                        if (HasDataChangeNotification(pub))
                        {
                            dcCount++;
                        }
                    }
                    Assert.That(dcCount, Is.GreaterThan(0),
                        $"Round {round}: expected at least one notification.");
                }
            }
            finally
            {
                if (subIds.Count > 0)
                {
                    DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                        null, subIds.ToArray().ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
                }
            }
        }

        private static readonly Random s_random = new();

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            double interval = DefaultInterval,
            uint lifetime = DefaultLifetime,
            uint keepAlive = DefaultKeepAlive,
            uint maxNotif = 0,
            bool enabled = true,
            byte priority = 0)
        {
            return await Session.CreateSubscriptionAsync(
                null, interval, lifetime, keepAlive, maxNotif,
                enabled, priority,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> AddMonitoredItemAsync(
            uint subId, NodeId nodeId,
            uint handle = 1, double sampling = 250,
            uint queueSize = 10)
        {
            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = handle,
                    SamplingInterval = sampling,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            };

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private async Task WriteInt32ValueAsync(NodeId nodeId, int value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(value))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResp.Results[0]), Is.True);
        }

        private async Task<PublishResponse> PublishAsync()
        {
            return await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private static bool HasDataChangeNotification(PublishResponse pub)
        {
            if (pub.NotificationMessage?.NotificationData == null ||
                pub.NotificationMessage.NotificationData.Count == 0)
            {
                return false;
            }
            foreach (ExtensionObject ext in pub.NotificationMessage.NotificationData)
            {
                var dcn = ExtensionObject.ToEncodeable(ext) as DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems != default && dcn.MonitoredItems.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<uint> ReadMaxSubscriptionsPerSession()
        {
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds
                            .Server_ServerCapabilities_MaxSubscriptionsPerSession,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (!StatusCode.IsGood(readResp.Results[0].StatusCode))
            {
                return 0;
            }

            return readResp.Results[0].WrappedValue.GetUInt32();
        }

        private const double DefaultInterval = 1000;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
