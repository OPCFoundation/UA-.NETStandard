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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using V2 = Opc.Ua.Client.Subscriptions;
using V2Items = Opc.Ua.Client.Subscriptions.MonitoredItems;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2-shaped transfer tests. The classic engine exposes an explicit
    /// <c>Subscription.TransferAsync</c> across 5 transfer types
    /// (KeepOpen / CloseSession / DisconnectedAck / DisconnectedRepublish /
    /// DisconnectedRepublishDelayedAck) × <c>sendInitialValues</c> ×
    /// <c>sequentialPublishing</c> = 20 combinations. The V2 engine
    /// exposes transfer only as
    /// (a) <see cref="ManagedSessionBuilder.WithTransferSubscriptionsOnRecreate"/>
    /// driven from inside <c>RecreateInPlaceAsync</c> (failover), or
    /// (b) the new
    /// <see cref="ISubscriptionManager.LoadAsync(System.IO.Stream, IServiceMessageContext, System.Func{string, ISubscriptionNotificationHandler}, bool, System.Threading.CancellationToken)"/>
    /// with <c>transferSubscriptions: true</c>.
    /// </summary>
    /// <remarks>
    /// This file covers the V2-public surfaces of transfer. Tests for
    /// the classic-specific shape (explicit
    /// <c>Subscription.TransferAsync</c> with manual delayed-ack hooks)
    /// remain in the Classic project. See
    /// <c>plans/v2-subscription-parity.md</c> for the parity matrix.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("TransferSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class TransferSubscriptionV2Tests : ClientTestFramework
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

        // ===== 1. Save → Load with transferSubscriptions = true =====

        [Test]
        [Order(100)]
        [CancelAfter(60_000)]
        public async Task TransferViaSaveLoadV2Async(CancellationToken ct)
        {
            string saveFile = Path.Combine(Path.GetTempPath(),
                $"V2Transfer-{Guid.NewGuid():N}.bin");

            ManagedSession originSession = await ConnectV2Async(
                nameof(TransferViaSaveLoadV2Async) + "_origin", ct,
                deleteSubscriptionsOnClose: false).ConfigureAwait(false);
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription originSub = originSession.AddSubscription(
                    originHandler, new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => originSub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;
                Assert.That(originSub.TryAddMonitoredItem(
                    "CurrentTime", timeNode,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(100) },
                    out V2Items.IMonitoredItem? originItem), Is.True);

                bool firstData = await originHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(firstData, Is.True);

                uint originSubscriptionServerId = ((V2.Subscription)originSub).Id;
                uint originItemServerId = originItem!.ServerId;
                uint originItemClientHandle = originItem.ClientHandle;

                using (var saveStream = File.Create(saveFile))
                {
                    await originSession.SaveSubscriptionsAsync(saveStream, ct: ct)
                        .ConfigureAwait(false);
                }
                Assert.That(new FileInfo(saveFile).Length, Is.GreaterThan(0));

                StatusCode close = await originSession.CloseAsync()
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(close), Is.True);

                targetSession = await ConnectV2Async(
                    nameof(TransferViaSaveLoadV2Async) + "_target", ct)
                    .ConfigureAwait(false);

                var targetHandler = new RecordingSubscriptionHandler();
                IReadOnlyList<ISubscription> loaded;
                using (var input = File.OpenRead(saveFile))
                {
                    loaded = await targetSession.LoadSubscriptionsAsync(
                        input, _ => targetHandler,
                        transferSubscriptions: true, ct)
                        .ConfigureAwait(false);
                }
                Assert.That(loaded, Has.Count.EqualTo(1));
                ISubscription transferred = loaded[0];

                // Two valid outcomes after LoadAsync(transferSubscriptions:true):
                // 1. Transfer succeeded: server id preserved, item server id preserved.
                // 2. Transfer rejected by server (e.g. BadUserAccessDenied between
                //    two anonymous sessions per Part 4 §5.13.7): the V2 manager
                //    falls back to recreate which mints fresh ids.
                //
                // Both must produce a working subscription that publishes against
                // the target session. Distinguish via the preserved id; assert the
                // outcome is internally consistent.
                bool transferActuallyTookOver =
                    ((V2.Subscription)transferred).Id == originSubscriptionServerId;
                TestContext.Out.WriteLine(transferActuallyTookOver
                    ? $"Transfer preserved server id {originSubscriptionServerId}"
                    : $"Transfer denied → fallback recreate (origin Id={originSubscriptionServerId}, new Id={((V2.Subscription)transferred).Id})");

                Assert.That(transferred.MonitoredItems.TryGetMonitoredItemByName(
                    "CurrentTime", out V2Items.IMonitoredItem? transferredItem),
                    Is.True);
                Assert.That(transferredItem, Is.Not.Null);
                if (transferActuallyTookOver)
                {
                    Assert.That(transferredItem!.ServerId,
                        Is.EqualTo(originItemServerId),
                        "Transfer should preserve the server item id");
                    Assert.That(transferredItem.ClientHandle,
                        Is.EqualTo(originItemClientHandle),
                        "Transfer should preserve the client handle so " +
                        "notifications route to the correct item");
                }

                // Either way, publish must resume against the target session.
                bool dataAfterTransfer = await targetHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(dataAfterTransfer, Is.True,
                    "Restored subscription should publish on the target session");

                await transferred.DisposeAsync().ConfigureAwait(false);
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
                try { File.Delete(saveFile); } catch { /* best effort */ }
            }
        }

        // ===== 2. Save → Load with transferSubscriptions = false (recreate fallback) =====

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task LoadWithoutTransferRecreatesSubscriptionV2Async(
            CancellationToken ct)
        {
            string saveFile = Path.Combine(Path.GetTempPath(),
                $"V2LoadNoTransfer-{Guid.NewGuid():N}.bin");

            ManagedSession originSession = await ConnectV2Async(
                nameof(LoadWithoutTransferRecreatesSubscriptionV2Async) + "_origin", ct)
                .ConfigureAwait(false);
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription originSub = originSession.AddSubscription(
                    originHandler, new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => originSub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                Assert.That(originSub.TryAddMonitoredItem(
                    "State", VariableIds.Server_ServerStatus_State,
                    o => o with { SamplingInterval = TimeSpan.Zero },
                    out _), Is.True);

                bool gotData = await originHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True);

                uint originSubServerId = ((V2.Subscription)originSub).Id;

                using (var output = File.Create(saveFile))
                {
                    await originSession.SubscriptionManager.SaveAsync(
                        output, originSession.MessageContext, null, ct)
                        .ConfigureAwait(false);
                }

                targetSession = await ConnectV2Async(
                    nameof(LoadWithoutTransferRecreatesSubscriptionV2Async) + "_target", ct)
                    .ConfigureAwait(false);

                var targetHandler = new RecordingSubscriptionHandler();
                IReadOnlyList<ISubscription> loaded;
                using (var input = File.OpenRead(saveFile))
                {
                    loaded = await targetSession.SubscriptionManager
                        .LoadAsync(input, targetSession.MessageContext,
                            _ => targetHandler,
                            transferSubscriptions: false, ct)
                        .ConfigureAwait(false);
                }
                Assert.That(loaded, Has.Count.EqualTo(1));
                ISubscription recreated = loaded[0];

                bool reCreated = await WaitForAsync(() => recreated.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(reCreated, Is.True,
                    "Loaded subscription should be re-created on the server when transferSubscriptions=false");

                // With transferSubscriptions=false, the V2 manager
                // creates a fresh server subscription with a new id
                // rather than taking over the saved id.
                uint newSubServerId = ((V2.Subscription)recreated).Id;
                Assert.That(newSubServerId, Is.Not.Zero);
                Assert.That(newSubServerId, Is.Not.EqualTo(originSubServerId),
                    "Recreated subscription should have a fresh server id");

                bool targetData = await targetHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(targetData, Is.True,
                    "Recreated subscription should publish on the target session");

                await recreated.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { await originSession.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try { await originSession.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                if (targetSession != null)
                {
                    try { await targetSession.CloseAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                    try { await targetSession.DisposeAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
                try { File.Delete(saveFile); } catch { /* best effort */ }
            }
        }

        // ===== 3. WithTransferSubscriptionsOnRecreate propagates the option =====

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task BuilderTransferOnRecreateOptionV2Async(CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSession session = await new ManagedSessionBuilder(
                ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(BuilderTransferOnRecreateOptionV2Async))
                .WithTransferSubscriptionsOnRecreate(true)
                .ConnectAsync(ct).ConfigureAwait(false);
            try
            {
                // The option lives on the V2 SubscriptionManager. We can
                // verify the manager has the property set so the recreate
                // path will request transfer when it runs.
                ISubscriptionManager manager = session.SubscriptionManager;
                Assert.That(manager, Is.Not.Null);
                // The interface itself doesn't surface the flag (it's on
                // the concrete SubscriptionManager). We exercise the
                // through-flow by creating a subscription and confirming
                // the manager continues to work — the flag is verified
                // via the failover path in the test for
                // ManagedSessionReconnectIntegrationTests in Sessions.Tests
                // when that runs end-to-end.
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True,
                    "Subscription should be created on the server");
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ===== 4. Save → Load round-trips multiple items + triggering =====

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task SaveLoadRoundTripWithMultipleItemsV2Async(
            CancellationToken ct)
        {
            string saveFile = Path.Combine(Path.GetTempPath(),
                $"V2SaveLoadRound-{Guid.NewGuid():N}.bin");

            ManagedSession session = await ConnectV2Async(
                nameof(SaveLoadRoundTripWithMultipleItemsV2Async), ct)
                .ConfigureAwait(false);
            ManagedSession? target = null;
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 50,
                        MaxNotificationsPerPublish = 42
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add 3 items with different configs
                Assert.That(sub.TryAddMonitoredItem("Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(250) },
                    out V2Items.IMonitoredItem? timeItem), Is.True);
                Assert.That(sub.TryAddMonitoredItem("State",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(500) },
                    out V2Items.IMonitoredItem? stateItem), Is.True);
                Assert.That(sub.TryAddMonitoredItem("Build",
                    VariableIds.Server_ServerStatus_BuildInfo,
                    o => o with { SamplingInterval = TimeSpan.Zero },
                    out V2Items.IMonitoredItem? buildItem), Is.True);

                bool allCreated = await WaitForAsync(
                    () => timeItem!.Created && stateItem!.Created && buildItem!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(allCreated, Is.True);

                using (var output = File.Create(saveFile))
                {
                    await session.SubscriptionManager.SaveAsync(
                        output, session.MessageContext, null, ct)
                        .ConfigureAwait(false);
                }

                target = await ConnectV2Async(
                    nameof(SaveLoadRoundTripWithMultipleItemsV2Async) + "_target", ct)
                    .ConfigureAwait(false);

                var targetHandler = new RecordingSubscriptionHandler();
                IReadOnlyList<ISubscription> loaded;
                using (var input = File.OpenRead(saveFile))
                {
                    loaded = await target.SubscriptionManager.LoadAsync(input,
                        target.MessageContext, _ => targetHandler,
                        transferSubscriptions: false, ct).ConfigureAwait(false);
                }

                Assert.That(loaded, Has.Count.EqualTo(1));
                ISubscription loadedSub = loaded[0];
                Assert.That(loadedSub.MonitoredItems.Count, Is.EqualTo(3u));
                Assert.That(loadedSub.MonitoredItems.TryGetMonitoredItemByName(
                    "Time", out _), Is.True);
                Assert.That(loadedSub.MonitoredItems.TryGetMonitoredItemByName(
                    "State", out _), Is.True);
                Assert.That(loadedSub.MonitoredItems.TryGetMonitoredItemByName(
                    "Build", out _), Is.True);

                bool reCreated = await WaitForAsync(() => loadedSub.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(reCreated, Is.True);

                await loadedSub.DisposeAsync().ConfigureAwait(false);
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { await session.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                if (target != null)
                {
                    try { await target.CloseAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                    try { await target.DisposeAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
                try { File.Delete(saveFile); } catch { /* best effort */ }
            }
        }

        // ===== helpers =====

        private async Task<ManagedSession> ConnectV2Async(
            string sessionName, CancellationToken ct,
            bool deleteSubscriptionsOnClose = true)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSession session = await new ManagedSessionBuilder(
                ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .ConnectAsync(ct).ConfigureAwait(false);
            session.DeleteSubscriptionsOnClose = deleteSubscriptionsOnClose;
            return session;
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
