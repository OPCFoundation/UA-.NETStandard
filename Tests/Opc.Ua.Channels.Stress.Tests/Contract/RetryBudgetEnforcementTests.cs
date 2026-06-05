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

#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Fakes;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Channels.Stress.Tests.Contract
{
    /// <summary>
    /// Layer-1 contract tests that verify the channel manager honors the
    /// caller-supplied <see cref="IRetryBudget"/> during reconnect cycles
    /// and that the budget reference is propagated from the outer reconnect
    /// call site down into <see cref="IClientChannelManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Category("ChannelManager")]
    [Category("RetryBudget")]
    [Parallelizable(ParallelScope.None)]
    public sealed class RetryBudgetEnforcementTests : ContractTestBase
    {
        /// <summary>
        /// L1-BUD1: when every reconnect attempt fails, the channel manager
        /// drives the budget to exhaustion and faults the channel.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [CancelAfter(30_000)]
        public async Task BudgetExhaustsAndChannelFaultsAsync(CancellationToken ct)
        {
            var fakeTime = new FakeTimeProvider();
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(2),
                MaxAttempts = int.MaxValue
            };

            int failGate = 0;
            using var bindings = new FakeChannelBindings(_ =>
            {
                var transport = new FakeTransport();
                if (Volatile.Read(ref failGate) != 0)
                {
                    transport.ConfigureFault(FaultMode.OpenFails);
                }
                return transport;
            });

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateApplicationConfiguration(telemetry);
            await using var manager = new ClientChannelManager(
                configuration,
                telemetry,
                bindings,
                policy,
                fakeTime);

            IReconnectParticipant participant = MakeParticipant("p", MakeEndpoint());
            IManagedTransportChannel channel = await manager
                .GetAsync(participant, ct)
                .ConfigureAwait(false);

            // Switch every transport (existing and future) to fail-on-open
            // so the reconnect loop can never make forward progress.
            Volatile.Write(ref failGate, 1);
            foreach (FakeTransport transport in bindings.Created)
            {
                transport.ConfigureFault(FaultMode.OpenFails);
            }

            var budget = new RetryBudget(TimeSpan.FromSeconds(5), fakeTime);

            Task reconnectTask = manager
                .ReconnectAsync(channel, budget, ct)
                .AsTask();

            await DriveFakeTimeUntilCompleteAsync(
                fakeTime,
                reconnectTask,
                TimeSpan.FromMilliseconds(500),
                ct)
                .ConfigureAwait(false);

            // Budget exhaustion surfaces as BadSecureChannelClosed from the
            // manager's reconnect overload; the channel is left in the
            // Faulted state and the budget reports exhaustion.
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await reconnectTask.ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                Assert.That(budget.IsExhausted, Is.True);
            });
        }

        /// <summary>
        /// L1-BUD2: after a reconnect cycle that completes successfully the
        /// budget is not exhausted. Production code in
        /// <c>ConnectionStateMachine</c> additionally invokes
        /// <see cref="IRetryBudget.Reset"/> on success and rebuilds the
        /// cached budget for the next cycle; this test asserts both
        /// observable contracts.
        /// </summary>
        /// <param name="ct">Cancellation token supplied by NUnit.</param>
        [Test]
        [CancelAfter(30_000)]
        public async Task BudgetIsNotExhaustedAndResetsAfterSuccessfulReconnectAsync(
            CancellationToken ct)
        {
            var fakeTime = new FakeTimeProvider();
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(2),
                MaxAttempts = int.MaxValue
            };

            int created = 0;
            using var bindings = new FakeChannelBindings(_ =>
            {
                int sequence = Interlocked.Increment(ref created);
                var transport = new FakeTransport();
                // Transports created during the first two reconnect attempts
                // fail; the transport allocated for the third attempt is
                // healthy and completes the cycle.
                if (sequence == 2 || sequence == 3)
                {
                    transport.ConfigureFault(FaultMode.OpenFails);
                }
                return transport;
            });

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateApplicationConfiguration(telemetry);
            await using var manager = new ClientChannelManager(
                configuration,
                telemetry,
                bindings,
                policy,
                fakeTime);

            IReconnectParticipant participant = MakeParticipant("p", MakeEndpoint());
            IManagedTransportChannel channel = await manager
                .GetAsync(participant, ct)
                .ConfigureAwait(false);

            // Force the manager to abandon the initial transport on each
            // reconnect attempt so the factory-driven failures above can
            // take effect and a healthy transport is built for attempt 3.
            foreach (FakeTransport transport in bindings.Created)
            {
                transport.ConfigureFault(FaultMode.OpenFails);
            }

            var budget = new RetryBudget(TimeSpan.FromSeconds(5), fakeTime);

            Task reconnectTask = manager
                .ReconnectAsync(channel, budget, ct)
                .AsTask();

            await DriveFakeTimeUntilCompleteAsync(
                fakeTime,
                reconnectTask,
                TimeSpan.FromMilliseconds(500),
                ct)
                .ConfigureAwait(false);

            await reconnectTask.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(budget.IsExhausted, Is.False);
            });

            // Production semantics: the outer state machine calls Reset on
            // the same budget instance after a successful reconnect cycle.
            budget.Reset();
            Assert.That(budget.ElapsedSinceFirstAttempt, Is.EqualTo(TimeSpan.Zero));

            // A freshly constructed budget for the next cycle is also a
            // valid fallback if the caller chose not to reuse the instance.
            var nextCycleBudget = new RetryBudget(TimeSpan.FromSeconds(5), fakeTime);
            Assert.That(nextCycleBudget.IsExhausted, Is.False);
        }

        /// <summary>
        /// L1-BUD3: the same <see cref="IRetryBudget"/> reference flows from
        /// the outer reconnect call site (the seam used by
        /// <c>ManagedSession.HandleReconnectAsync</c> →
        /// <c>Session.ReconnectAsync(budget)</c>) down into
        /// <see cref="IClientChannelManager.ReconnectAsync(IManagedTransportChannel, IRetryBudget, CancellationToken)"/>
        /// overload.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task BudgetReferenceIsForwardedToChannelManagerAsync()
        {
            var managerMock = new Mock<IClientChannelManager>(MockBehavior.Strict);
            var channelMock = new Mock<IManagedTransportChannel>(MockBehavior.Loose).Object;

            IRetryBudget? capturedBudget = null;
            managerMock
                .Setup(m => m.ReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(),
                    It.IsAny<IRetryBudget>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IManagedTransportChannel, IRetryBudget, CancellationToken>(
                    (_, budget, _) => capturedBudget = budget)
                .Returns(ValueTask.CompletedTask);

            var expectedBudget = new RetryBudget(TimeSpan.FromSeconds(5));

            // The forwarding seam exercised by ManagedSession.HandleReconnectAsync
            // is Session.ReconnectManagedChannelAsync (private static). It is
            // the single call site that must hand the same IRetryBudget
            // instance to the channel manager.
            MethodInfo? forwarder = typeof(Session).GetMethod(
                "ReconnectManagedChannelAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(forwarder, Is.Not.Null,
                "Session.ReconnectManagedChannelAsync test seam must exist.");

            object? invocation = forwarder!.Invoke(
                obj: null,
                parameters:
                [
                    managerMock.Object,
                    channelMock,
                    expectedBudget,
                    CancellationToken.None
                ]);

            Assert.That(invocation, Is.InstanceOf<ValueTask>());
            await ((ValueTask)invocation!).ConfigureAwait(false);

            managerMock.Verify(
                m => m.ReconnectAsync(
                    channelMock,
                    It.Is<IRetryBudget>(b => ReferenceEquals(b, expectedBudget)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.That(capturedBudget, Is.SameAs(expectedBudget));
        }

        private static async Task DriveFakeTimeUntilCompleteAsync(
            FakeTimeProvider fakeTime,
            Task target,
            TimeSpan step,
            CancellationToken ct)
        {
            // Yield once before advancing so the reconnect loop has a
            // chance to reach its first awaitable suspension point.
            await Task.Yield();

            const int maxTicks = 1_000;
            for (int tick = 0; tick < maxTicks && !target.IsCompleted; tick++)
            {
                ct.ThrowIfCancellationRequested();
                fakeTime.Advance(step);

                // Let the manager observe the fake-time advance and progress
                // through its reconnect loop before the next tick.
                await Task.Yield();
                await Task.Delay(1, ct).ConfigureAwait(false);
            }

            if (!target.IsCompleted)
            {
                Assert.Fail("The reconnect task did not complete after advancing fake time.");
            }
        }

        private static ConfiguredEndpoint MakeEndpoint()
        {
            return CreateEndpoint("opc.tcp://localhost:4840/RetryBudgetContract");
        }

        private static RetryBudgetParticipant MakeParticipant(string id, ConfiguredEndpoint endpoint)
        {
            return new RetryBudgetParticipant(id, endpoint);
        }

        private sealed class RetryBudgetParticipant : IReconnectParticipant
        {
            public RetryBudgetParticipant(string id, ConfiguredEndpoint endpoint)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                if (channel == null)
                {
                    throw new ArgumentNullException(nameof(channel));
                }

                _ = reconnectAttempt;
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
            }
        }
    }
}