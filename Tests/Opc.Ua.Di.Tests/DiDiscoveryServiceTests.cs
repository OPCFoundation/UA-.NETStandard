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
using Opc.Ua.Client;
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
            Func<CancellationToken, Task<ManagedSession>> accessor =
                _ => Task.FromResult<ManagedSession>(null!);

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new DiDiscoveryService(accessor, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void EnumerateDevicesAsyncInvokesSessionAccessor()
        {
            // Verify EnumerateDevicesAsync awaits the accessor by
            // having the accessor throw a sentinel exception — the
            // exception must surface to the caller.
            int accessorCalls = 0;
            var sentinel = new InvalidOperationException("accessor-called");
            Func<CancellationToken, Task<ManagedSession>> accessor = _ =>
            {
                accessorCalls++;
                throw sentinel;
            };

            var service = new DiDiscoveryService(
                accessor, new Mock<ITelemetryContext>().Object);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.EnumerateDevicesAsync())!;
            Assert.That(ex, Is.SameAs(sentinel));
            Assert.That(accessorCalls, Is.EqualTo(1));
        }

        [Test]
        public void EnumerateDevicesAsyncPropagatesCancellation()
        {
            CancellationToken capturedToken = default;
            Func<CancellationToken, Task<ManagedSession>> accessor = ct =>
            {
                capturedToken = ct;
                throw new OperationCanceledException(ct);
            };

            var service = new DiDiscoveryService(
                accessor, new Mock<ITelemetryContext>().Object);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await service.EnumerateDevicesAsync(cts.Token));
            Assert.That(capturedToken, Is.EqualTo(cts.Token));
        }
    }
}
