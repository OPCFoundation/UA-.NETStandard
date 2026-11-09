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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Verifies subscription-only recovery without waiting for the session keep-alive timer.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class PublishFailureRecoveryIntegrationTests : ChannelManagerIntegrationTestBase
    {
        /// <summary>
        /// A failed in-flight Publish must initiate recovery and restore changing notifications.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(120_000)]
        public async Task PublishFailureRecoveryRestoresSubscriptionWithoutApplicationReadAsync(
            bool useChannelManager,
            CancellationToken ct)
        {
            var bindings = new CapturingChannelBindings();
            await using var manager = new ClientChannelManager(ClientFixture.Config, Telemetry, bindings);
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None).ConfigureAwait(false);
            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(PublishFailureRecoveryRestoresSubscriptionWithoutApplicationReadAsync))
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                });
            if (useChannelManager)
            {
                builder.WithChannelManager(manager);
            }

            ManagedSessionType session = await builder.ConnectAsync(ct).ConfigureAwait(false);
            IServiceResponseMutator? originalMutator = ReferenceServer.ResponseMutator;
            var controller = new MockResponseController();
            var handler = new TimeNotificationHandler();
            int outerRecoveries = 0;
            int channelRecoveries = 0;
            int keepAliveErrors = 0;
            var faulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await using ISubscription subscription = session.AddSubscription(
                    handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingEnabled = true,
                        PublishingInterval = TimeSpan.FromMilliseconds(250),
                        KeepAliveCount = 10,
                        LifetimeCount = 100
                    });
                Assert.That(
                    subscription.TryAddMonitoredItem(
                        "ServerCurrentTime",
                        VariableIds.Server_ServerStatus_CurrentTime,
                        options => options with
                        {
                            SamplingInterval = TimeSpan.FromMilliseconds(250),
                            QueueSize = 10
                        },
                        out _),
                    Is.True);
                Assert.That(
                    await WaitForAsync(
                        () => subscription.Created && handler.ChangeCount >= 2,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Creation and live notification delivery must settle before extending keep-alive.");

                session.KeepAliveInterval = 30_000;
                session.ConnectionStateChanged += (_, e) =>
                {
                    if (e.NewState == ConnectionState.Reconnecting)
                    {
                        Interlocked.Increment(ref outerRecoveries);
                    }
                };
                session.ChannelStateChanged += (_, e) =>
                {
                    if (e.NewState == ChannelState.TransportReconnecting)
                    {
                        Interlocked.Increment(ref channelRecoveries);
                    }
                };
                session.KeepAlive += (_, e) =>
                {
                    if (ServiceResult.IsBad(e.Status))
                    {
                        Interlocked.Increment(ref keepAliveErrors);
                    }
                };

                ITransportChannel transport = useChannelManager
                    ? bindings.Channel!
                    : session.InnerSession.TransportChannel;
                Assert.That(transport, Is.Not.Null);
                ReferenceServer.ResponseMutator = controller;
                // The response hook runs before the Publish response reaches the client:
                // disposing here guarantees an outstanding Publish, not an idle transport.
                using IDisposable expectation = controller.ExpectNextResponse<PublishResponse>(_ =>
                {
                    transport.Dispose();
                    faulted.TrySetResult(true);
                });

                Assert.That(
                    await WaitForAsync(() => faulted.Task.IsCompleted, DefaultWait, ct).ConfigureAwait(false),
                    Is.True,
                    "The live server must reach the in-flight Publish fault barrier.");
                Assert.That(
                    await WaitForAsync(
                        () => Volatile.Read(ref outerRecoveries) + Volatile.Read(ref channelRecoveries) > 0 &&
                            Volatile.Read(ref keepAliveErrors) > 0,
                        TimeSpan.FromSeconds(10),
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Publish failure must initiate recovery well before the 30-second keep-alive.");
                Assert.That(
                    Volatile.Read(ref keepAliveErrors),
                    Is.GreaterThan(0),
                    "The failed Publish must signal the existing keep-alive error path.");

                Assert.That(
                    await WaitForAsync(
                        () => session.Connected && subscription.Created,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True);
                int changesAfterRecovery = handler.ChangeCount;
                Assert.That(
                    await WaitForAsync(
                        () => handler.ChangeCount >= changesAfterRecovery + 2,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "New server-time changes must arrive after recovery without an application Read.");

                int recoveredOuterCycles = Volatile.Read(ref outerRecoveries);
                int recoveredChannelCycles = Volatile.Read(ref channelRecoveries);
                int stableChanges = handler.ChangeCount;
                Assert.That(
                    await WaitForAsync(
                        () => handler.ChangeCount >= stableChanges + 3,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Recovery must remain healthy across subsequent changing notifications.");
                Assert.That(Volatile.Read(ref outerRecoveries), Is.EqualTo(recoveredOuterCycles));
                Assert.That(Volatile.Read(ref channelRecoveries), Is.EqualTo(recoveredChannelCycles));
                Assert.That(recoveredOuterCycles, Is.LessThanOrEqualTo(1),
                    "Concurrent failures from the old transport must not start repeated outer recovery cycles.");
                Assert.That(recoveredChannelCycles, Is.LessThanOrEqualTo(1),
                    "Concurrent failures from the old transport must not start repeated channel recovery cycles.");
                Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            }
            finally
            {
                ReferenceServer.ResponseMutator = originalMutator;
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Captures the native transport so the test faults it rather than releasing its managed lease.
        /// </summary>
        private sealed class CapturingChannelBindings : ITransportChannelBindings
        {
            /// <summary>
            /// Gets the most recently created native transport.
            /// </summary>
            public ITransportChannel? Channel => Volatile.Read(ref m_channel);

            /// <inheritdoc/>
            public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
            {
                ITransportChannel? channel = ((ITransportChannelBindings)ClientChannelManager.DefaultChannelBindings)
                    .Create(uriScheme, telemetry);
                Volatile.Write(ref m_channel, channel);
                return channel;
            }

            /// <summary>The most recently created native transport.</summary>
            private ITransportChannel? m_channel;
        }

        /// <summary>
        /// Counts distinct, good server-time values rather than replayed or buffered notifications.
        /// </summary>
        private sealed class TimeNotificationHandler : ISubscriptionNotificationHandler
        {
            /// <summary>
            /// Gets the number of distinct good timestamps delivered.
            /// </summary>
            public int ChangeCount => Volatile.Read(ref m_changeCount);

            /// <inheritdoc/>
            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                foreach (DataValueChange change in notification.Span)
                {
                    if (StatusCode.IsGood(change.Value.StatusCode) &&
                        change.Value.WrappedValue.TryGetValue(out DateTimeUtc time))
                    {
                        lock (m_gate)
                        {
                            if (time > m_lastTime)
                            {
                                m_lastTime = time;
                                Interlocked.Increment(ref m_changeCount);
                            }
                        }
                    }
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            /// <summary>Serializes timestamp comparisons across notification callbacks.</summary>
            private readonly Lock m_gate = new();

            /// <summary>The latest distinct timestamp delivered.</summary>
            private DateTimeUtc m_lastTime;

            /// <summary>The number of distinct good timestamps delivered.</summary>
            private int m_changeCount;
        }
    }
}
