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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Direct tests for <see cref="SimulationRegistry"/> — the
    /// manager-owned periodic-loop registry. Exercises lifecycle
    /// (NewSimulation/Start/Dispose) and a real-clock smoke test
    /// to validate the <see cref="System.Threading.PeriodicTimer"/>
    /// integration.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class SimulationRegistryTests
    {
        [Test]
        public void ConstructorThrowsOnNullOwner()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SimulationRegistry(null!, NullLogger()))!;
            Assert.That(ex.ParamName, Is.EqualTo("owner"));
        }

        [Test]
        public void ConstructorAcceptsNullLogger()
        {
            using var registry = new SimulationRegistry(
                CreateOwner(), logger: null);
            Assert.That(registry, Is.Not.Null);
        }

        [Test]
        public void NewSimulationReturnsBuilder()
        {
            using var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());
            ISimulationBuilder builder = registry.NewSimulation(
                TimeSpan.FromMilliseconds(25));
            Assert.That(builder, Is.Not.Null);
        }

        [Test]
        public void NewSimulationAfterStartThrowsBadInvalidState()
        {
            using var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());
            registry.Start();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => registry.NewSimulation(TimeSpan.FromMilliseconds(25)))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void StartIsIdempotent()
        {
            using var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());
            registry.Start();
            // Second call must be a no-op (does not throw).
            Assert.DoesNotThrow(() => registry.Start());
        }

        [Test]
        public void DisposeWithoutStartIsSafe()
        {
            var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());
            Assert.DoesNotThrow(() => registry.Dispose());
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());
            registry.Start();
            registry.Dispose();
            // Second dispose must be a no-op (cts already cleared).
            Assert.DoesNotThrow(() => registry.Dispose());
        }

        [Test]
        [Category("Timing")]
        public async Task RegisteredLoopFiresAfterStart()
        {
            using var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());

            int ticks = 0;
            registry.NewSimulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) => Interlocked.Increment(ref ticks));

            registry.Start();
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref ticks), Is.GreaterThanOrEqualTo(1),
                "Tick handler must fire at least once within 200ms.");
        }

        [Test]
        [Category("Timing")]
        public async Task DisposeStopsFurtherTicks()
        {
            var registry = new SimulationRegistry(
                CreateOwner(), NullLogger());

            int ticks = 0;
            registry.NewSimulation(TimeSpan.FromMilliseconds(25))
                .OnTick((ctx, dt) => Interlocked.Increment(ref ticks));

            registry.Start();
            await Task.Delay(100).ConfigureAwait(false);
            registry.Dispose();
            int snapshot = Volatile.Read(ref ticks);
            await Task.Delay(150).ConfigureAwait(false);
            int afterDispose = Volatile.Read(ref ticks);

            Assert.That(afterDispose, Is.LessThanOrEqualTo(snapshot + 1),
                "No (or at most one in-flight) tick may fire after Dispose.");
        }

        private static ILogger? NullLogger()
        {
            return null;
        }

        private static TestFluentManager CreateOwner()
        {
            return new TestFluentManager();
        }

        private sealed class TestFluentManager : FluentNodeManagerBase
        {
            public TestFluentManager()
                : base(CreateMockServer(), TestNamespaceUri)
            {
            }

            private const string TestNamespaceUri = "urn:test:simulation-registry";

            private static IServerInternal CreateMockServer()
            {
                var ns = new NamespaceTable();
                ns.Append(global::Opc.Ua.Namespaces.OpcUa);

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mock = new Mock<IServerInternal>();
                mock.SetupGet(m => m.NamespaceUris).Returns(ns);
                mock.SetupGet(m => m.Telemetry).Returns(mockTelemetry.Object);
                IServiceMessageContext msgCtx = ServiceMessageContext.Create(
                    mockTelemetry.Object);
                mock.SetupGet(m => m.MessageContext).Returns(msgCtx);
                mock.SetupGet(m => m.DefaultSystemContext).Returns(
                    new ServerSystemContext(mock.Object));
                return mock.Object;
            }
        }
    }
}
