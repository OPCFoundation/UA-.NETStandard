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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="SimulationBuilderExtensions"/> /
    /// <see cref="ISimulationBuilder"/> — fluent simulation tick API.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class SimulationBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        [Test]
        public void SimulationRejectsNegativeInterval()
        {
            using var h = SimulationHarness.Create();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => h.Builder.Simulation(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => h.Builder.Simulation(TimeSpan.FromMilliseconds(-1)));
        }

        [Test]
        public void SimulationOnPlainBuilderThrowsBadConfigurationError()
        {
            using var h = SimulationHarness.CreateWithoutFluentBase();
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => h.Builder.Simulation(TimeSpan.FromMilliseconds(10)))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task OnTickFiresPeriodically()
        {
            using var h = SimulationHarness.Create();
            int ticks = 0;
            h.Builder.Simulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) => Interlocked.Increment(ref ticks));
            h.Builder.Seal();

            await WaitForAsync(
                () => Volatile.Read(ref ticks) >= 2,
                "Tick should fire at least twice with a 25ms interval.")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AsyncOnTickIsAwaited()
        {
            using var h = SimulationHarness.Create();
            int started = 0;
            int completed = 0;
            h.Builder.Simulation(TimeSpan.FromMilliseconds(25))
                .OnTick(async (ctx, dt, ct) =>
                {
                    Interlocked.Increment(ref started);
                    await Task.Delay(5, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref completed);
                });
            h.Builder.Seal();

            await WaitForAsync(() => Volatile.Read(ref started) >= 2).ConfigureAwait(false);
            await WaitForAsync(() => Volatile.Read(ref completed) >= 2).ConfigureAwait(false);
        }

        [Test]
        public async Task ExceptionInHandlerDoesNotKillLoop()
        {
            using var h = SimulationHarness.Create();
            int ticks = 0;
            h.Builder.Simulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) =>
                {
                    int n = Interlocked.Increment(ref ticks);
                    if (n == 1)
                    {
                        throw new InvalidOperationException("boom");
                    }
                });
            h.Builder.Seal();

            await WaitForAsync(
                () => Volatile.Read(ref ticks) >= 3,
                "Handler-thrown exception must not stop subsequent ticks.")
                .ConfigureAwait(false);
        }

        [Test]
        public void OnTickAfterSealRejected()
        {
            using var h = SimulationHarness.Create();
            ISimulationBuilder sb = h.Builder.Simulation(TimeSpan.FromMilliseconds(100));
            h.Builder.Seal();

            // Adding a NEW simulation should be rejected after Seal/Start.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => h.Builder.Simulation(TimeSpan.FromMilliseconds(100)))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void DisposeStopsRunningLoops()
        {
            var h = SimulationHarness.Create();
            int ticks = 0;
            h.Builder.Simulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) => Interlocked.Increment(ref ticks));
            h.Builder.Seal();

            Thread.Sleep(75);
            h.Dispose();
            int snapshot = Volatile.Read(ref ticks);

            Thread.Sleep(75);
            int afterDispose = Volatile.Read(ref ticks);

            Assert.That(afterDispose, Is.EqualTo(snapshot).Within(1),
                "No ticks should fire after Dispose (modulo one already-in-flight).");
        }

        [Test]
        public async Task MultipleOnTickHandlersAllFire()
        {
            using var h = SimulationHarness.Create();
            int handlerA = 0, handlerB = 0;
            h.Builder.Simulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) => Interlocked.Increment(ref handlerA))
                .OnTick((ctx, dt) => Interlocked.Increment(ref handlerB));
            h.Builder.Seal();

            await WaitForAsync(
                () => Volatile.Read(ref handlerA) >= 2 && Volatile.Read(ref handlerB) >= 2)
                .ConfigureAwait(false);
            Assert.That(
                Math.Abs(Volatile.Read(ref handlerA) - Volatile.Read(ref handlerB)),
                Is.LessThanOrEqualTo(1),
                "Both handlers fire on the same tick — counts should be in lockstep.");
        }

        private static async Task WaitForAsync(
            Func<bool> condition,
            string? message = null,
            int timeoutMs = 5000)
        {
            DateTime end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    Assert.Fail(
                        message ?? $"Condition not satisfied within {timeoutMs}ms.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test harness — a FluentNodeManagerBase subclass + a NodeManagerBuilder
        /// attached via AttachToBuilder.
        /// </summary>
        private sealed class SimulationHarness : IDisposable
        {
            internal NodeManagerBuilder Builder { get; private set; } = null!;
            private TestFluentManager? m_manager;
            private NodeManagerBuilder? m_plainBuilder;

            internal static SimulationHarness Create()
            {
                var harness = new SimulationHarness
                {
                    m_manager = new TestFluentManager()
                };
                ServerSystemContext ctx = harness.m_manager.SystemContext;
                harness.Builder = new NodeManagerBuilder(
                    ctx,
                    harness.m_manager,
                    defaultNamespaceIndex: kNs,
                    rootResolver: _ => null!,
                    nodeIdResolver: _ => null!,
                    typeIdResolver: _ => []);
                harness.m_manager.AttachToBuilder(harness.Builder);
                return harness;
            }

            internal static SimulationHarness CreateWithoutFluentBase()
            {
                var harness = new SimulationHarness
                {
                    m_plainBuilder = new NodeManagerBuilder(
                        new SystemContext(telemetry: null!) { NamespaceUris = new NamespaceTable() },
                        Mock.Of<IAsyncNodeManager>(),
                        defaultNamespaceIndex: kNs,
                        rootResolver: _ => null!,
                        nodeIdResolver: _ => null!,
                        typeIdResolver: _ => [])
                };
                harness.Builder = harness.m_plainBuilder;
                return harness;
            }

            public void Dispose()
            {
                m_manager?.Dispose();
                m_manager = null;
            }
        }

        private sealed class TestFluentManager : FluentNodeManagerBase
        {
            public TestFluentManager()
                : base(CreateMockServer(), TestNamespaceUri)
            {
            }

            private const string TestNamespaceUri = "urn:test:simulation";

            private static IServerInternal CreateMockServer()
            {
                var ns = new NamespaceTable();
                ns.Append(Ua.Namespaces.OpcUa);

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mock = new Mock<IServerInternal>();
                mock.SetupGet(m => m.NamespaceUris).Returns(ns);
                mock.SetupGet(m => m.Telemetry).Returns(mockTelemetry.Object);
                IServiceMessageContext msgCtx = ServiceMessageContext.Create(mockTelemetry.Object);
                mock.SetupGet(m => m.MessageContext).Returns(msgCtx);
                mock.SetupGet(m => m.DefaultSystemContext).Returns(
                    new ServerSystemContext(mock.Object));
                return mock.Object;
            }
        }
    }
}
