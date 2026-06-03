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

#nullable enable

#pragma warning disable CA2016

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2 failover-style transfer tests. Force a transport channel
    /// break and verify the subscription survives — either via
    /// <see cref="Opc.Ua.Client.Subscriptions.ISubscriptionManager.TransferSubscriptionsOnRecreate"/>
    /// (server kept the subscription) or via the V2 manager's
    /// internal recreate fallback (server discarded). Both outcomes
    /// must result in the subscription continuing to deliver data.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("Failover")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionFailoverTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        [CancelAfter(120_000)]
        public async Task FailoverChannelBreakWithTransferOnRecreateV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectFailoverAsync(
                nameof(FailoverChannelBreakWithTransferOnRecreateV2Async),
                transferOnRecreate: true, ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);
                Assert.That(sub.TryAddMonitoredItem(
                    "Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(250) },
                    out _), Is.True);

                bool firstData = await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(firstData, Is.True);

                // Force channel break. The V2 manager + ManagedSession
                // reconnect logic must restore the channel and then
                // run TransferSubscriptions (with recreate fallback if
                // the server has discarded the subscription).
                int preCount = handler.DataChangeCount;
                ITransportChannel? channel = session.InnerSession?.TransportChannel;
                if (channel == null)
                {
                    Assert.Inconclusive(
                        "InnerSession.TransportChannel is null; cannot force channel break.");
                    return;
                }
                TestContext.Out.WriteLine(
                    "Closing transport channel to force V2 transfer/recreate…");
                channel.Dispose();

                bool reconnected = await WaitForAsync(
                    () => session.Connected,
                    TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                Assert.That(reconnected, Is.True,
                    "Session must auto-reconnect after channel loss.");

                bool subStillCreated = await WaitForAsync(
                    () => sub.Created, TimeSpan.FromSeconds(30), ct)
                    .ConfigureAwait(false);
                Assert.That(subStillCreated, Is.True,
                    "Subscription must remain Created after the V2 " +
                    "TransferSubscriptions / recreate fallback path.");

                // Either transfer succeeded (same ServerId) or the V2
                // manager re-created the subscription (possibly with a
                // new ServerId). Either way: new data must flow.
                bool postData = await WaitForAsync(
                    () => handler.DataChangeCount > preCount,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(postData, Is.True,
                    "Subscription must continue to deliver data after the " +
                    "failover (TransferSubscriptions succeeded or recreate fallback ran).");

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { await session.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
        public async Task FailoverChannelBreakWithoutTransferOnRecreateV2Async(
            CancellationToken ct)
        {
            // With TransferSubscriptionsOnRecreate=false the V2 manager
            // does NOT call TransferSubscriptions after a reconnect.
            // The server-side subscription, however, survives the
            // transport-level reconnect because the inner Session keeps
            // its SessionId. So this configuration must also continue
            // to deliver data without the explicit transfer call.
            ManagedSession session = await ConnectFailoverAsync(
                nameof(FailoverChannelBreakWithoutTransferOnRecreateV2Async),
                transferOnRecreate: false, ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);
                Assert.That(sub.TryAddMonitoredItem(
                    "Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(250) },
                    out _), Is.True);
                Assert.That(await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);

                uint preServerId = ((Opc.Ua.Client.Subscriptions.Subscription)sub).ServerId;
                int preCount = handler.DataChangeCount;
                ITransportChannel? channel = session.InnerSession?.TransportChannel;
                if (channel == null)
                {
                    Assert.Inconclusive(
                        "InnerSession.TransportChannel is null; cannot force channel break.");
                    return;
                }
                TestContext.Out.WriteLine(
                    "Closing transport channel — no TransferSubscriptions on recreate…");
                channel.Dispose();

                Assert.That(await WaitForAsync(() => session.Connected,
                    TimeSpan.FromSeconds(60), ct).ConfigureAwait(false), Is.True);
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false), Is.True);
                Assert.That(await WaitForAsync(
                    () => handler.DataChangeCount > preCount,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false), Is.True,
                    "Subscription must continue to deliver after channel reconnect");
                Assert.That(((Opc.Ua.Client.Subscriptions.Subscription)sub).ServerId, Is.EqualTo(preServerId),
                    "Without TransferSubscriptions on recreate, the server-side " +
                    "ServerId should be preserved across a transport-level reconnect.");

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { await session.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        private async Task<ManagedSession> ConnectFailoverAsync(
            string sessionName, bool transferOnRecreate, CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            var builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(120));
            if (transferOnRecreate)
            {
                builder = builder.WithTransferSubscriptionsOnRecreate();
            }
            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return predicate();
        }
    }
}
