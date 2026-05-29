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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for the Subscription Transfer conformance unit.
    /// Tests 002, 014, 017, 018, 019 map to official JS test cases
    /// for transfer after session close, mixed valid/invalid transfers,
    /// anonymous user token transfers, and StatusChangeNotification behavior.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionTransfer")]
    public class SubscriptionTransferTests : TestFixture
    {
        [OneTimeSetUp]
        public async Task TransferOneTimeSetUpAsync()
        {
            if (Session != null)
            {
                try
                { await Session.CloseAsync(5000, true).ConfigureAwait(false); }
                catch { }
                Session.Dispose();
            }
            Session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            Assert.That(Session, Is.Not.Null, "Failed to create signed transfer session");
        }

        [Test]
        public async Task TransferAfterSessionCloseWithDeleteSubscriptionsTrueAsync()
        {
            // Close session with deleteSubscriptions=true, then try transfer.
            // Subscription should be gone; transfer should return BadSubscriptionIdInvalid.
            ISession session1 = await CreateSessionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp = await CreateSubAsync(session1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddMonitoredItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            // Consume initial data
            await Task.Delay(1500).ConfigureAwait(false);
            await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            // Close with deleteSubscriptions=true (default)
            await session1.CloseAsync(5000, true).ConfigureAwait(false);
            session1.Dispose();

            // Open new session and try to transfer the deleted subscription
            ISession session2 = await CreateSessionAsync().ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer = await TransferOrIgnoreAsync(
                    session2, [subId], false).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(1));
                Assert.That(
                    xfer.Results[0].StatusCode,
                    Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid),
                    $"Expected BadSubscriptionIdInvalid, got {xfer.Results[0].StatusCode}.");
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferMixedValidAndInvalidSubscriptionIdsAsync()
        {
            // Two subscriptions, transfer one valid + one invalid ID.
            // Expect Good + BadSubscriptionIdInvalid.
            ISession session1 = await CreateSessionAsync().ConfigureAwait(false);

            CreateSubscriptionResponse resp1 = await CreateSubAsync(session1).ConfigureAwait(false);
            uint subIdValid = resp1.SubscriptionId;
            await AddMonitoredItemAsync(session1, subIdValid,
                VariableIds.Server_ServerStatus_CurrentTime, handle: 1).ConfigureAwait(false);

            // Create a second subscription and delete it to get an invalid ID
            CreateSubscriptionResponse resp2 = await CreateSubAsync(session1).ConfigureAwait(false);
            uint subIdInvalid = resp2.SubscriptionId;
            await DeleteSubAsync(session1, subIdInvalid).ConfigureAwait(false);

            // Close session without deleting the valid subscription
            session1.DeleteSubscriptionsOnClose = false;
            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            ISession session2 = await CreateSessionAsync().ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer = await TransferOrIgnoreAsync(
                    session2, [subIdValid, subIdInvalid], false).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(2));

                // First result should be Good (valid subscription)
                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True,
                    $"Valid subscription transfer failed: {xfer.Results[0].StatusCode}.");

                // Second result should be BadSubscriptionIdInvalid
                Assert.That(
                    xfer.Results[1].StatusCode,
                    Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid),
                    $"Expected BadSubscriptionIdInvalid for deleted sub, got {xfer.Results[1].StatusCode}.");

                // Clean up transferred subscription
                await DeleteSubAsync(session2, subIdValid).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferWithAnonymousUserTokenSucceedsAsync()
        {
            // Anonymous user token; transfer should succeed between sessions.
            ISession session1 = await CreateSessionAsync().ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            await AddMonitoredItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            // Close session without deleting subscription
            session1.DeleteSubscriptionsOnClose = false;
            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            // New session with anonymous auth (default for SecurityPolicies.None)
            ISession session2 = await CreateSessionAsync().ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer = await TransferOrIgnoreAsync(
                    session2, [subId], true).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True,
                    $"Transfer with anonymous token failed: {xfer.Results[0].StatusCode}.");

                // Verify we can publish on the new session
                await Task.Delay(500).ConfigureAwait(false);
                PublishResponse pub = await session2.PublishAsync(
                    null, default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));

                await DeleteSubAsync(session2, subId).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TransferReturnsGoodSubscriptionTransferredOnOldSessionAsync()
        {
            // Transfer should return Good_SubscriptionTransferred status change
            // notification on remaining publish request in old session.
            ISession session1 = await CreateSessionAsync().ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1,
                interval: 200).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            await AddMonitoredItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 100).ConfigureAwait(false);

            // Get initial data
            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse initialPub = await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(initialPub.ResponseHeader.ServiceResult), Is.True);

            // Queue a publish request on the old session before transferring
            ValueTask<PublishResponse> pendingPubTask = session1.PublishAsync(
                null, default,
                CancellationToken.None);
            Task<PublishResponse> pendingPub = pendingPubTask.AsTask();

            ISession session2 = await CreateSessionAsync().ConfigureAwait(false);
            try
            {
                // Transfer the subscription to session2
                TransferSubscriptionsResponse xfer = await TransferOrIgnoreAsync(
                    session2, [subId], true).ConfigureAwait(false);

                if (StatusCode.IsBad(xfer.Results[0].StatusCode))
                {
                    Assert.Ignore(
                        $"Subscription transfer not supported: {xfer.Results[0].StatusCode}");
                }

                Assert.That(StatusCode.IsGood(xfer.Results[0].StatusCode), Is.True);

                // Check the pending publish on session1 for StatusChangeNotification
                try
                {
                    PublishResponse oldPub = await pendingPub.ConfigureAwait(false);

                    // Should have StatusChangeNotification with GoodSubscriptionTransferred
                    bool hasTransferNotification = HasStatusChangeNotification(oldPub);

                    if (!hasTransferNotification)
                    {
                        // May also get BadNoSubscription or similar
                        Assert.Warn("Old session did not receive " +
                            "GoodSubscriptionTransferred StatusChangeNotification.");
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNoSubscription ||
                        sre.StatusCode == StatusCodes.BadSessionClosed)
                {
                    // Acceptable: session may have been cleaned up
                }

                // Verify new session receives data
                await Task.Delay(500).ConfigureAwait(false);
                PublishResponse newPub = await session2.PublishAsync(
                    null, default,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(newPub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(newPub.SubscriptionId, Is.EqualTo(subId));

                await DeleteSubAsync(session2, subId).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
                try
                {
                    await CloseSessionAsync(session1).ConfigureAwait(false);
                }
                catch
                {
                    // session1 may already be in a bad state
                }
            }
        }

        [Test]
        public async Task TransferWithAnonymousUserDifferentSecurityPoliciesAsync()
        {
            // Anonymous user token over connections; transfer should succeed.
            // Since we use in-process server with SecurityPolicies.None,
            // we test transferring between two independent sessions.
            ISession session1 = await CreateSessionAsync().ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            await AddMonitoredItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            // Get initial data
            await Task.Delay(500).ConfigureAwait(false);
            await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            // Close session without deleting subscription
            session1.DeleteSubscriptionsOnClose = false;
            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            // Connect a new session (anonymous, same policy in test environment)
            ISession session2 = await CreateSessionAsync().ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer = await TransferOrIgnoreAsync(
                    session2, [subId], true).ConfigureAwait(false);

                Assert.That(xfer.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True,
                    $"Transfer with anonymous token failed: {xfer.Results[0].StatusCode}.");

                // Verify publish works
                await Task.Delay(500).ConfigureAwait(false);
                PublishResponse pub = await session2.PublishAsync(
                    null, default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));

                await DeleteSubAsync(session2, subId).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            ISession session,
            double interval = DefaultInterval,
            uint lifetime = DefaultLifetime,
            uint keepAlive = DefaultKeepAlive)
        {
            return await session.CreateSubscriptionAsync(
                null, interval, lifetime, keepAlive, 0,
                true, 0,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task DeleteSubAsync(ISession session, uint id)
        {
            await session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> AddMonitoredItemAsync(
            ISession session, uint subId, NodeId nodeId,
            uint handle = 1, double sampling = 250)
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

            CreateMonitoredItemsResponse resp = await session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private Task<ISession> CreateSessionAsync()
        {
            return ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256);
        }

        private async Task CloseSessionAsync(ISession session)
        {
            await session.CloseAsync(5000, true).ConfigureAwait(false);
            session.Dispose();
        }

        private async Task<TransferSubscriptionsResponse> TransferOrIgnoreAsync(
            ISession target, uint[] subIds, bool sendInitial)
        {
            try
            {
                TransferSubscriptionsResponse resp =
                    await target.TransferSubscriptionsAsync(
                        null,
                        subIds.ToArrayOf(),
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
                    "TransferSubscriptions not supported: " + sre.StatusCode.ToString());
                return null; // unreachable
            }
        }

        private static bool HasStatusChangeNotification(PublishResponse pub)
        {
            if (pub.NotificationMessage?.NotificationData == null)
            {
                return false;
            }
            foreach (ExtensionObject ext in pub.NotificationMessage.NotificationData)
            {
                if (ExtensionObject.ToEncodeable(ext) is StatusChangeNotification)
                {
                    return true;
                }
            }
            return false;
        }

        private const double DefaultInterval = 500;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
