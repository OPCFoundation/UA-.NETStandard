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
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Documents the bridge wiring gap: classic
    /// <see cref="Opc.Ua.Client.Subscription"/> instances added to a
    /// session whose engine is <see cref="DefaultSubscriptionEngine"/>
    /// (V2) currently do not receive publish notifications, because
    /// the V2 publish loop does not route messages for "external"
    /// (classic-owned) subscription ids through
    /// <see cref="Opc.Ua.Client.Subscriptions.Engine.SubscriptionBridge"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests are <see cref="ExplicitAttribute">[Explicit]</see>
    /// so the broken/skipped behaviour does not fail CI but a developer
    /// can run them locally to reproduce the gap. Once the wiring is
    /// implemented (V2 <c>SubscriptionManager</c> exposes an external-
    /// subscription registration hook + <c>DefaultSubscriptionEngine</c>
    /// registers each classic <c>Subscription</c> on create / unregisters
    /// on delete + extends the handler with <c>availableSequenceNumbers</c>),
    /// flip <c>[Explicit]</c> off and rewrite each <c>Assert.Inconclusive</c>
    /// into a positive assertion.
    /// </para>
    /// <para>
    /// See <c>plans/26-v2-subscription-parity.md</c> §6 "Bridge wiring
    /// TODO" and the inline doc on
    /// <see cref="Opc.Ua.Client.Subscriptions.Engine.SubscriptionBridge"/>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("BridgeGap")]
    [Explicit("Documents the classic-API-on-V2-engine bridge wiring gap.")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ClassicOnEngineBridgeGapTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            ClientFixtureSubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance;
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

        /// <summary>
        /// Demonstrates that <see cref="Subscription.SaveMessageInCache"/>
        /// signature already matches
        /// <see cref="Opc.Ua.Client.Subscriptions.Engine.ISubscriptionMessageSink"/>
        /// so the classic subscription is sink-shaped already; only the
        /// V2 manager-side routing remains.
        /// </summary>
        [Test]
        [Order(100)]
        [CancelAfter(30_000)]
        public async Task ClassicSubscriptionImplementsMessageSinkV2Async(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ISession session = await ClientFixture
                .ConnectAsync(endpoint, new UserIdentity())
                .ConfigureAwait(false);
            try
            {
                using var sub = new Subscription(session.DefaultSubscription)
                {
                    DisplayName = "BridgeGapProbe"
                };
                Assert.That(sub, Is.InstanceOf<Opc.Ua.Client.Subscriptions.Engine.ISubscriptionMessageSink>(),
                    "Classic Subscription must implement ISubscriptionMessageSink so " +
                    "the V2 SubscriptionBridge can deliver translated notifications.");
            }
            finally
            {
                try
                { await session.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                session.Dispose();
            }
        }

        /// <summary>
        /// Documents the runtime behaviour: a classic
        /// <see cref="Subscription"/> created on a V2-engine session
        /// does not receive publish notifications today. Once the V2
        /// manager exposes an external-subscription registration API
        /// and the <see cref="DefaultSubscriptionEngine"/> wires the
        /// bridge in, this test should assert that notifications DO
        /// arrive instead of Assert.Inconclusive.
        /// </summary>
        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task ClassicSubscriptionOnV2EngineReceivesNoNotificationsAsync(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ISession session = await ClientFixture
                .ConnectAsync(endpoint, new UserIdentity())
                .ConfigureAwait(false);
            try
            {
                Assert.That(((Session)session).SubscriptionEngine,
                    Is.InstanceOf<DefaultSubscriptionEngine>(),
                    "Test precondition: session must be on the V2 engine.");

                int notificationCount = 0;
                using var subscription = new Subscription(session.DefaultSubscription)
                {
                    DisplayName = "ClassicOnV2Probe",
                    PublishingInterval = 500,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    PublishingEnabled = true
                };
                bool added = session.AddSubscription(subscription);
                Assert.That(added, Is.True);
                await subscription.CreateAsync(ct).ConfigureAwait(false);
                Assert.That(subscription.Created, Is.True);

                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "CurrentTime",
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = 250,
                    QueueSize = 10
                };
                item.Notification += (_, _) => Interlocked.Increment(ref notificationCount);
                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

                // Give the server a generous window to publish.
                await Task.Delay(5_000, ct).ConfigureAwait(false);

                if (Volatile.Read(ref notificationCount) > 0)
                {
                    // The bridge wiring has been implemented — flip this
                    // test from documenting-the-gap to asserting it.
                    Assert.Pass(
                        "Bridge wiring is now functional: classic subscription " +
                        $"received {notificationCount} notification(s) on the V2 engine. " +
                        "Remove [Explicit] from this fixture and convert " +
                        "Assert.Inconclusive into a positive assertion.");
                }
                else
                {
                    Assert.Inconclusive(
                        "DOCUMENTED GAP: classic Subscription on V2 engine received " +
                        "zero notifications. The V2 SubscriptionManager publish loop " +
                        "deletes 'unknown' (classic-owned) subscriptions on the server " +
                        "(SubscriptionManager.cs:937). Wire the SubscriptionBridge into " +
                        "DefaultSubscriptionEngine to fix.");
                }
            }
            finally
            {
                try
                { await session.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                session.Dispose();
            }
        }
    }
}
