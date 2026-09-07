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
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the simulation registry after its release moved onto the behavior
    /// mechanism: loops run on the server clock, and teardown awaits their drain
    /// instead of blocking inside Dispose.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    // A behavior that fails to start or release parks a thread rather than failing an
    // assertion, so every case here is time-boxed.
    [CancelAfter(30000)]
    public sealed class SimulationLifetimeTests
    {
        [Test]
        public async Task LoopTicksOnTheServerClockAsync()
        {
            var clock = new FakeTimeProvider();
            using var manager = new ClockedManager(clock);
            NodeManagerBuilder builder = manager.NewBuilder();

            int ticks = 0;
            builder.Simulation(TimeSpan.FromSeconds(1))
                .OnTick((_, _) => Interlocked.Increment(ref ticks));

            await manager.ActivateAsync().ConfigureAwait(false);
            builder.Seal();

            Assert.That(ticks, Is.Zero, "no tick before the clock advances");

            await AdvanceAndSettleAsync(clock, TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);
            Assert.That(ticks, Is.EqualTo(1));

            await AdvanceAndSettleAsync(clock, TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);
            Assert.That(ticks, Is.EqualTo(2));

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task TeardownAwaitsTheRunningLoopAsync()
        {
            var clock = new FakeTimeProvider();
            using var manager = new ClockedManager(clock);
            NodeManagerBuilder builder = manager.NewBuilder();

            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool handlerFinished = false;

            builder.Simulation(TimeSpan.FromSeconds(1))
                .OnTick(async (_, _, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    handlerFinished = true;
                });

            await manager.ActivateAsync().ConfigureAwait(false);
            builder.Seal();

            clock.Advance(TimeSpan.FromSeconds(1));
            await entered.Task.ConfigureAwait(false);

            // Teardown must not complete while the handler is still inside its tick.
            ValueTask teardown = manager.DeleteAddressSpaceAsync();
            Assert.That(handlerFinished, Is.False);

            release.TrySetResult(true);
            await teardown.ConfigureAwait(false);

            Assert.That(
                handlerFinished,
                Is.True,
                "teardown must await the in-flight tick, not abandon it");
        }

        [Test]
        public void SynchronousDisposeDoesNotBlockOnARunningLoop()
        {
            var clock = new FakeTimeProvider();
            var manager = new ClockedManager(clock);
            NodeManagerBuilder builder = manager.NewBuilder();

            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            builder.Simulation(TimeSpan.FromSeconds(1))
                .OnTick(async (_, _, _) => await release.Task.ConfigureAwait(false));

            builder.Seal();
            clock.Advance(TimeSpan.FromSeconds(1));

            // Dispose is signal-only: it must return promptly even though a handler is
            // parked, because it can be reached from a synchronous shutdown path.
            var disposed = Task.Run(manager.Dispose);
            Assert.That(
                disposed.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "Dispose must not block on an in-flight tick");

            release.TrySetResult(true);
        }

        [Test]
        public void LateHandlerAfterStartIsRejected()
        {
            var clock = new FakeTimeProvider();
            using var manager = new ClockedManager(clock);
            NodeManagerBuilder builder = manager.NewBuilder();

            ISimulationBuilder simulation = builder
                .Simulation(TimeSpan.FromSeconds(1))
                .OnTick((_, _) => { });

            builder.Seal();

            // The running loop enumerates a snapshot, so a late handler would never be
            // invoked. Rejecting it beats silently dropping it.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => simulation.OnTick((_, _) => { }))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        private static async Task AdvanceAndSettleAsync(
            FakeTimeProvider clock,
            TimeSpan delta)
        {
            clock.Advance(delta);

            // The loop body runs on the thread pool, so give it a bounded window to
            // observe the tick rather than asserting on a race.
            for (int i = 0; i < 100; i++)
            {
                await Task.Yield();
                await Task.Delay(1).ConfigureAwait(false);
            }
        }

        private sealed class ClockedManager : FluentNodeManagerBase
        {
            public ClockedManager(TimeProvider clock)
                : base(CreateMockServer(clock), TestNamespaceUri)
            {
            }

            public NodeManagerBuilder NewBuilder()
            {
                return CreateFluentBuilder(1);
            }

            public ValueTask ActivateAsync()
            {
                return ActivateNodeBehaviorsAsync(CancellationToken.None);
            }

            private const string TestNamespaceUri = "urn:test:simulation-lifetime";

            private static IServerInternal CreateMockServer(TimeProvider clock)
            {
                var ns = new NamespaceTable();
                ns.Append(Ua.Namespaces.OpcUa);
                ns.Append(TestNamespaceUri);

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mock = new Mock<IServerInternal>();
                mock.SetupGet(m => m.NamespaceUris).Returns(ns);
                mock.SetupGet(m => m.TypeTree).Returns(new TypeTable(ns));
                mock.SetupGet(m => m.Telemetry).Returns(mockTelemetry.Object);
                IServiceMessageContext msgCtx = ServiceMessageContext.Create(
                    mockTelemetry.Object);
                mock.SetupGet(m => m.MessageContext).Returns(msgCtx);

                // The manager reads its clock through this optional interface. Moq
                // requires every As<T>() before the first Object access.
                mock.As<ITimeProviderProvider>()
                    .SetupGet(m => m.TimeProvider)
                    .Returns(clock);

                mock.SetupGet(m => m.DefaultSystemContext).Returns(
                    new ServerSystemContext(mock.Object));
                return mock.Object;
            }
        }
    }
}
