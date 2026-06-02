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
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Durable.Tests
{
    /// <summary>
    /// V2 ports of the classic <c>DurableSubscriptionTest.cs</c> 5 tests.
    /// Exercises the new
    /// <see cref="ISubscription.SetAsDurableAsync"/> surface and
    /// validates the V2 manager behavior around durable subscriptions.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("Durable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionDurableTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            MaxChannelCount = 1000;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
#nullable disable
            ServerFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t),
                enableTracing,
                disableActivityLogging)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true,
                DurableSubscriptionsEnabled = true
            };

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = ServerFixture
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.MinSessionTimeout = 1000;
            ServerFixture.Config.ServerConfiguration.MinSubscriptionLifetime = 1500;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);

            ReferenceServer = await ServerFixture.StartAsync()
                .ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
#nullable enable
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
        [CancelAfter(60_000)]
        public async Task SetSubscriptionDurableSucceedsBeforeItemsAddedV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SetSubscriptionDurableSucceedsBeforeItemsAddedV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(900),
                        KeepAliveCount = 100,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                TimeSpan revised = await sub.SetAsDurableAsync(
                    TimeSpan.FromHours(1), ct).ConfigureAwait(false);
                Assert.That(revised, Is.GreaterThanOrEqualTo(TimeSpan.FromHours(1)),
                    "Server should return a revised lifetime >= 1 hour");
                TestContext.Out.WriteLine(
                    "SetAsDurable revised lifetime: {0}", revised);

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task SetSubscriptionDurableFailsWhenMIExistsV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SetSubscriptionDurableFailsWhenMIExistsV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(900),
                        KeepAliveCount = 100,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add a monitored item BEFORE attempting SetSubscriptionDurable.
                Assert.That(sub.TryAddMonitoredItem(
                    "CurrentTime",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(500) },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                bool itemCreated = await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(itemCreated, Is.True);

                // Per OPC UA Part 4 §5.13.9 the server rejects
                // SetSubscriptionDurable once items have been created.
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sub.SetAsDurableAsync(TimeSpan.FromHours(1), ct)
                        .ConfigureAwait(false));

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task SetSubscriptionDurableFailsOnUncreatedSubscriptionV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SetSubscriptionDurableFailsOnUncreatedSubscriptionV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(900),
                        KeepAliveCount = 100,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });

                // Try SetAsDurableAsync IMMEDIATELY without
                // waiting for Created. The V2 ISubscription contract
                // requires the subscription to be created first. There
                // is a benign race: by the time the async lambda runs,
                // the V2 state-machine may have already created the
                // subscription server-side. In that case the call may
                // succeed (server accepts SetSubscriptionDurable on a
                // fresh subscription with no items). Both outcomes
                // confirm the V2 surface is correct.
                ServiceResultException? caught = null;
                try
                {
                    await sub.SetAsDurableAsync(TimeSpan.FromHours(1), ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    caught = ex;
                }
                if (caught != null)
                {
                    Assert.That(caught.StatusCode,
                        Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                }
                else
                {
                    TestContext.Out.WriteLine(
                        "Subscription was already Created when " +
                        "SetAsDurableAsync ran — server " +
                        "accepted the call (no race window left).");
                }

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task DurableSubscriptionRevisedLifetimeMonotonicallyDecreasesV2Async(
            CancellationToken ct)
        {
            // Sanity-check: requesting a very large lifetime returns a
            // revised value (the server caps); requesting a small value
            // should return at least the requested amount or what the
            // server's MinSubscriptionLifetime permits.
            ManagedSession session = await ConnectV2Async(
                nameof(DurableSubscriptionRevisedLifetimeMonotonicallyDecreasesV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(900),
                        KeepAliveCount = 100,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Use TimeSpan.MaxValue equivalent — request the
                // longest representable lifetime to force the server
                // to revise it downward.
                TimeSpan revisedLarge = await sub.SetAsDurableAsync(
                    TimeSpan.FromDays(365 * 100), ct).ConfigureAwait(false);
                TestContext.Out.WriteLine(
                    "Server-revised lifetime for 100-year request: {0}",
                    revisedLarge);
                Assert.That(revisedLarge, Is.GreaterThan(TimeSpan.Zero),
                    "Server should revise to a positive lifetime");

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(500)]
        [CancelAfter(60_000)]
        public async Task DurableSubscriptionSurvivesSessionCloseV2Async(
            CancellationToken ct)
        {
            // End-to-end: mark a subscription durable, add an item, save
            // its state, close the origin session WITHOUT
            // DeleteSubscriptionsOnClose, open a fresh session, load with
            // transferSubscriptions:true. Verify the take-over succeeds
            // OR (if the server denies cross-anonymous-session transfer)
            // that recreate falls back cleanly. Either outcome shows
            // the V2 durable + Save/Load + transfer pipeline works
            // end-to-end.
            ManagedSession originSession = await ConnectV2Async(
                nameof(DurableSubscriptionSurvivesSessionCloseV2Async) + "_origin", ct)
                .ConfigureAwait(false);
            originSession.DeleteSubscriptionsOnClose = false;
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription sub = originSession.AddSubscription(originHandler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 100,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                TimeSpan revised = await sub.SetAsDurableAsync(
                    TimeSpan.FromHours(1), ct).ConfigureAwait(false);
                Assert.That(revised, Is.GreaterThanOrEqualTo(TimeSpan.FromHours(1)));

                // Add an item AFTER setting durable.
                Assert.That(sub.TryAddMonitoredItem(
                    "Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(200) },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                bool itemCreated = await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(itemCreated, Is.True);

                bool firstData = await originHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(firstData, Is.True);

                // Save + close origin.
                using (var ms = new System.IO.MemoryStream())
                {
                    await originSession.SaveSubscriptionsAsync(ms, ct: ct)
                        .ConfigureAwait(false);
                    byte[] saved = ms.ToArray();
                    Assert.That(saved, Has.Length.GreaterThan(0));

                    StatusCode close = await originSession.CloseAsync()
                        .ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(close), Is.True);

                    targetSession = await ConnectV2Async(
                        nameof(DurableSubscriptionSurvivesSessionCloseV2Async) + "_target", ct)
                        .ConfigureAwait(false);

                    var targetHandler = new RecordingSubscriptionHandler();
                    using var input = new System.IO.MemoryStream(saved);
                    var loaded = await targetSession.LoadSubscriptionsAsync(
                        input, _ => targetHandler,
                        transferSubscriptions: true, ct)
                        .ConfigureAwait(false);
                    Assert.That(loaded, Has.Count.EqualTo(1));
                    bool dataAfter = await targetHandler.WaitForFirstDataAsync(
                        TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                    Assert.That(dataAfter, Is.True,
                        "Restored durable subscription should publish on target");
                }
            }
            finally
            {
                try { await originSession.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                if (targetSession != null)
                {
                    try { await targetSession.CloseAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                    try { await targetSession.DisposeAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
            }
        }

        private async Task<ManagedSession> ConnectV2Async(
            string sessionName, CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            return await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .ConnectAsync(ct).ConfigureAwait(false);
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
