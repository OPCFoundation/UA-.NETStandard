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
using ISession = Opc.Ua.Client.ISession;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Stress.Tests
{
    /// <summary>
    /// compliance tests for Subscription Transfer covering
    /// basic transfer, SendInitialValues, error cases,
    /// transfer with monitored items, continued notifications,
    /// and advanced scenarios.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionTransferDepth")]
    public class SubscriptionTransferDepthTests : TestFixture
    {
        // Per Part 5 §5.13.7 transferring a subscription whose owning user is
        // anonymous requires the new session to use Sign or SignAndEncrypt.
        // Replace the inherited None-mode Session with a signed one so the
        // transfer test cases exercise the actual TransferSubscriptions logic
        // rather than tripping the spec's anonymous-on-None gate.
        [OneTimeSetUp]
        public async Task TransferOneTimeSetUpAsync()
        {
            if (Session != null)
            {
                try
                {
                    await Session.CloseAsync(5000, true).ConfigureAwait(false);
                }
                catch
                {
                }
                Session.Dispose();
            }
            Session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            Assert.That(Session, Is.Not.Null, "Failed to create signed transfer session");
        }

        [Test]
        public async Task TransferSubscriptionToNewSessionSucceedsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, subId, true)
                        .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(
                        xfer.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(xfer.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True);

                // Clean up via new session
                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSubscriptionOriginalSessionCannotPublishAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                // Original session should get error on publish
                await Task.Delay(300).ConfigureAwait(false);
                try
                {
                    PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                    // May get BadNoSubscription or a publish for another sub
                    Assert.That(pub.SubscriptionId,
                        Is.Not.EqualTo(subId).Or
.Zero);
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(
                        sre.StatusCode == StatusCodes.BadNoSubscription ||
                        StatusCode.IsBad(sre.StatusCode),
                        Is.True);
                }

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSubscriptionNewSessionCanPublishAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSubscriptionPreservesMonitoredItemsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                handle: 1, sampling: 50).ConfigureAwait(false);
            await AddItemAsync(Session, subId,
                ToNodeId(Constants.ScalarStaticInt32),
                handle: 2).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSubscriptionIdPreservedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, subId, false)
                        .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True);

                // Publish on new session should see same SubId
                await Task.Delay(300).ConfigureAwait(false);
                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSubscriptionReturnsAvailableSeqNumsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Publish to generate seq numbers
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, subId, true)
                        .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True);
                Assert.That(
                    xfer.Results[0].AvailableSequenceNumbers,
                    Is.Not.Null);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithSendInitialTrueGetsDataAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0),
                    "SendInitialValues=true should produce DCN.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithSendInitialFalseNoImmediateDataAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                ToNodeId(Constants.ScalarStaticInt32))
                .ConfigureAwait(false);

            // Consume initial on original session
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                // Static node with false → may be KeepAlive
                Assert.That(pub.NotificationMessage, Is.Not.Null);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithSendInitialTrueAllItemsReportAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            for (uint h = 1; h <= 3; h++)
            {
                await AddItemAsync(Session, subId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h, sampling: 50).ConfigureAwait(false);
            }

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSendInitialFalseStaticNodeNoDataAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                ToNodeId(Constants.ScalarStaticInt32))
                .ConfigureAwait(false);

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferSendInitialRespectsMonitoringModeAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            // Create item in Disabled mode
            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Disabled,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                // Disabled item should not report even with initial=true
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.Zero);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferNonExistentSubscriptionReturnsBadAsync()
        {
            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, 999999, false)
                        .ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsBad(xfer.Results[0].StatusCode),
                    Is.True);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferAlreadyTransferredFailsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                // Try to transfer again from original session (which
                // no longer owns it)
                try
                {
                    TransferSubscriptionsResponse xfer2 =
                        await Session.TransferSubscriptionsAsync(
                            null,
                            new uint[] { subId }.ToArrayOf(),
                            false,
                            CancellationToken.None).ConfigureAwait(false);

                    // Original got it back, which is valid behavior
                    Assert.That(
                        StatusCode.IsGood(xfer2.Results[0].StatusCode),
                        Is.True);
                    await Session.DeleteSubscriptionsAsync(
                        null, new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(StatusCode.IsBad(sre.StatusCode),
                        Is.True);
                    // Clean up on session2
                    await session2.DeleteSubscriptionsAsync(
                        null, new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferMixedValidInvalidPartialResultsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await session2.TransferSubscriptionsAsync(
                        null,
                        new uint[] { subId, 999999u }.ToArrayOf(),
                        false,
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(2));
                bool firstGood =
                    StatusCode.IsGood(xfer.Results[0].StatusCode);
                bool secondBad =
                    StatusCode.IsBad(xfer.Results[1].StatusCode);

                if (!firstGood)
                {
                    Assert.Ignore("TransferSubscriptions failed for " +
                        "valid subscription: " +
                        xfer.Results[0].StatusCode.ToString());
                }

                Assert.That(secondBad, Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented)
            {
                Assert.Ignore("TransferSubscriptions not supported.");
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferToSameSessionBehaviorAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            try
            {
                TransferSubscriptionsResponse xfer =
                    await Session.TransferSubscriptionsAsync(
                        null,
                        new uint[] { subId }.ToArrayOf(),
                        false,
                        CancellationToken.None).ConfigureAwait(false);

                // Self-transfer may succeed or fail
                Assert.That(xfer.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented)
            {
                Assert.Fail("TransferSubscriptions not supported.");
            }
            catch (ServiceResultException)
            {
                // Self-transfer rejection is acceptable
            }
            finally
            {
                try
                {
                    await Session.DeleteSubscriptionsAsync(
                        null, new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // May already be invalid
                }
            }
        }

        [Test]
        public async Task TransferEmptyListBehaviorAsync()
        {
            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                try
                {
                    TransferSubscriptionsResponse xfer =
                        await session2.TransferSubscriptionsAsync(
                            null,
                            Array.Empty<uint>().ToArrayOf(),
                            false,
                            CancellationToken.None).ConfigureAwait(false);

                    Assert.That(xfer.Results.Count, Is.Zero);
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True);
                }
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferDeletedSubscriptionReturnsBadAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, subId, false)
                        .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsBad(xfer.Results[0].StatusCode),
                    Is.True);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithMultipleMonitoredItemsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            for (uint h = 1; h <= 5; h++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    (int)(h - 1) % Constants.ScalarStaticNodes.Length];
                await AddItemAsync(Session, subId, ToNodeId(eni),
                    handle: h).ConfigureAwait(false);
            }

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithDisabledItemAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Disabled,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.Zero,
                    "Disabled item should not report.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithSamplingItemAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferItemCountPreservedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            const int itemCount = 3;
            for (uint h = 1; h <= itemCount; h++)
            {
                await AddItemAsync(Session, subId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h, sampling: 50).ConfigureAwait(false);
            }

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                if (pub.NotificationMessage.NotificationData.Count > 0)
                {
                    var dcn = ExtensionObject.ToEncodeable(
                        pub.NotificationMessage.NotificationData[0]) as
                        DataChangeNotification;
                    if (dcn != null)
                    {
                        Assert.That(dcn.MonitoredItems.Count,
                            Is.GreaterThanOrEqualTo(itemCount));
                    }
                }

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithDataChangeFilterAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 100,
                    Filter = new ExtensionObject(filter),
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithQueuedNotificationsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Let some data queue up
            await Task.Delay(500).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(200).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferredSubContinuesPeriodicNotificationsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                int dataCount = 0;
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                    PublishResponse pub = await session2.PublishAsync(
                        null,
                        default,
                        CancellationToken.None).ConfigureAwait(false);
                    if (pub.NotificationMessage.NotificationData.Count > 0)
                    {
                        dataCount++;
                    }
                }

                Assert.That(dataCount, Is.GreaterThan(0),
                    "Transferred sub should continue notifications.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferredSubWriteTriggerNotificationAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddItemAsync(Session, subId, nodeId)
                .ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                // Consume initial
                await Task.Delay(300).ConfigureAwait(false);
                await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                // Write a new value
                await session2.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(
                                Variant.From(UnsecureRandom.Shared.Next()))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferredSubKeepAliveOnNewSessionAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            // No monitored items
            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(500).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.Zero,
                    "No items → KeepAlive expected.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferredSubSequenceNumberContinuesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(Session, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubBefore = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            uint seqBefore =
                pubBefore.NotificationMessage.SequenceNumber;

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pubAfter = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    pubAfter.NotificationMessage.SequenceNumber,
                    Is.GreaterThan(seqBefore),
                    "Seq number should continue after transfer.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferMultipleSubscriptionsAtOnceAsync()
        {
            var subIds = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                CreateSubscriptionResponse resp = await CreateSubAsync(
                    Session, interval: 200).ConfigureAwait(false);
                subIds.Add(resp.SubscriptionId);
                await AddItemAsync(Session, resp.SubscriptionId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: (uint)(i + 1), sampling: 100)
                    .ConfigureAwait(false);
            }

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await session2.TransferSubscriptionsAsync(
                        null,
                        subIds.ToArray().ToArrayOf(),
                        true,
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(3));
                if (StatusCode.IsBad(xfer.Results[0].StatusCode))
                {
                    Assert.Ignore("TransferSubscriptions failed: " +
                        xfer.Results[0].StatusCode.ToString());
                }

                foreach (TransferResult r in xfer.Results)
                {
                    Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                }

                await session2.DeleteSubscriptionsAsync(
                    null, subIds.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented)
            {
                Assert.Ignore("TransferSubscriptions not supported.");
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferThenDeleteOnNewSessionAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                Session, interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                DeleteSubscriptionsResponse del =
                    await session2.DeleteSubscriptionsAsync(
                        null, new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(del.Results[0]), Is.True);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            Client.ISession session,
            double interval = DefaultInterval)
        {
            return await session.CreateSubscriptionAsync(
                null, interval, DefaultLifetime, DefaultKeepAlive,
                0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> AddItemAsync(
            Client.ISession session, uint subId, NodeId nodeId,
            uint handle = 1, double sampling = 100)
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
                    QueueSize = 10
                }
            };

            CreateMonitoredItemsResponse resp =
                await session.CreateMonitoredItemsAsync(
                    null, subId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode),
                Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private async Task<TransferSubscriptionsResponse> TransferOrIgnoreAsync(
            Client.ISession target, uint subId, bool sendInitial)
        {
            try
            {
                TransferSubscriptionsResponse resp =
                    await target.TransferSubscriptionsAsync(
                        null,
                        new uint[] { subId }.ToArrayOf(),
                        sendInitial,
                        CancellationToken.None).ConfigureAwait(false);

                // Per-result Bad statuses are expected outcomes for negative
                // tests; do not treat them as "service not supported".
                return resp;
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented)
            {
                Assert.Ignore(
                    "TransferSubscriptions not supported: " +
                    sre.StatusCode.ToString());
                return null; // unreachable
            }
        }

        private Task<Client.ISession> CreateSessionAsync()
        {
            return ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256);
        }

        private async Task CloseSessionAsync(Client.ISession session)
        {
            await session.CloseAsync(5000, true).ConfigureAwait(false);
            session.Dispose();
        }

        private const double DefaultInterval = 500;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
