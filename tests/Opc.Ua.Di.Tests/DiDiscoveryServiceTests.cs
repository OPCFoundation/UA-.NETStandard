// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Client.Hosting;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Construction-validation tests for the internal
    /// <see cref="DiDiscoveryService"/>. Full
    /// <c>EnumerateDevicesAsync</c> coverage requires a real
    /// <c>ManagedSession</c> (sealed, private ctor) so we limit
    /// behavioural verification to the accessor-invocation path.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class DiDiscoveryServiceTests
    {
        [Test]
        public void ConstructorThrowsOnNullSessionAccessor()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiDiscoveryService(
                    null!, new Mock<ITelemetryContext>().Object))!;
            Assert.That(ex.ParamName, Is.EqualTo("sessionAccessor"));
        }

        [Test]
        public void ConstructorThrowsOnNullTelemetry()
        {
            static Task<ManagedSession> accessor(CancellationToken _) => Task.FromResult<ManagedSession>(null!);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiDiscoveryService(accessor, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public async Task EnumerateDevicesAsyncInvokesSessionAccessor()
        {
            // Verify EnumerateDevicesAsync awaits the accessor by
            // having the accessor throw a sentinel exception — the
            // exception must surface when iteration starts.
            int accessorCalls = 0;
            var sentinel = new InvalidOperationException("accessor-called");
            Task<ManagedSession> accessor(CancellationToken _)
            {
                accessorCalls++;
                throw sentinel;
            }

            var service = new DiDiscoveryService(
accessor, new Mock<ITelemetryContext>().Object);

            InvalidOperationException ex = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await foreach (DeviceEntry _ in service.EnumerateDevicesAsync())
                    {
                    }
                }))!;
            Assert.That(ex, Is.SameAs(sentinel));
            Assert.That(accessorCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task EnumerateDevicesAsyncPropagatesCancellation()
        {
            CancellationToken capturedToken = default;
            Task<ManagedSession> accessor(CancellationToken ct)
            {
                capturedToken = ct;
                throw new OperationCanceledException(ct);
            }

            var service = new DiDiscoveryService(
accessor, new Mock<ITelemetryContext>().Object);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () =>
                {
                    await foreach (DeviceEntry _ in service.EnumerateDevicesAsync(cts.Token))
                    {
                    }
                });
            Assert.That(capturedToken, Is.EqualTo(cts.Token));
        }
    }
}
