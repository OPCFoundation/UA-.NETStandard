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

#if NET6_0_OR_GREATER

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="SharedKestrelHostRegistry"/>. The
    /// registry is the gating piece for the
    /// <c>fu-shared-kestrel-host</c> feature so we verify its
    /// ref-counting, path-routing, and TLS-thumbprint mismatch behaviours
    /// without spinning a real HTTPS listener.
    /// </summary>
    [TestFixture]
    [Category("SharedKestrelHost")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SharedKestrelHostTests
    {
        private const string kThumbprint = "0000000000000000000000000000000000000000";
        private const string kOtherThumbprint = "1111111111111111111111111111111111111111";

        [Test]
        public void AcquireWithFirstListenerInvokesHostFactory()
        {
            var key = NewKey();
            var listener = (HttpsTransportListener?)null;
            int factoryInvocations = 0;
            try
            {
                using SharedHostLease lease = SharedKestrelHostRegistry.Instance.Acquire(
                    key,
                    listener!,
                    "/test",
                    acc => { factoryInvocations++; return MakeStubHost(); },
                    kThumbprint);
                Assert.That(factoryInvocations, Is.EqualTo(1));
                Assert.That(SharedKestrelHostRegistry.Instance.Count, Is.GreaterThan(0));
                Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));
            }
            finally
            {
                Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.Zero);
            }
        }

        [Test]
        public void AcquireWithSecondListenerReusesExistingHost()
        {
            var key = NewKey();
            int factoryInvocations = 0;
            using SharedHostLease lease1 = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/listenerA",
                acc => { factoryInvocations++; return MakeStubHost(); },
                kThumbprint);
            using SharedHostLease lease2 = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/listenerB",
                acc => { factoryInvocations++; return MakeStubHost(); },
                kThumbprint);
            Assert.That(factoryInvocations, Is.EqualTo(1), "Second Acquire must reuse host, not call factory.");
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2));
        }

        [Test]
        public void AcquireWithMismatchedThumbprintThrows()
        {
            var key = NewKey();
            using SharedHostLease lease = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SharedKestrelHostRegistry.Instance.Acquire(
                    key,
                    (HttpsTransportListener)null!,
                    "/test",
                    acc => MakeStubHost(),
                    kOtherThumbprint))!;
            Assert.That(ex.Message, Does.Contain(kThumbprint));
            Assert.That(ex.Message, Does.Contain(kOtherThumbprint));
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));
        }

        [Test]
        public void ReleasingLastLeaseStopsTheHost()
        {
            var key = NewKey();
            SharedHostLease lease = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));
            lease.Dispose();
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.Zero);
        }

        [Test]
        public void DoubleDisposeOfLeaseIsIdempotent()
        {
            var key = NewKey();
            SharedHostLease lease = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint);
            lease.Dispose();
            Assert.DoesNotThrow(() => lease.Dispose());
        }

        [Test]
        public void AcquireValidatesArguments()
        {
            var key = NewKey();
            Assert.Throws<ArgumentNullException>(
                () => SharedKestrelHostRegistry.Instance.Acquire(
                    key, (HttpsTransportListener)null!, "/test", null!, kThumbprint));
            Assert.Throws<ArgumentException>(
                () => SharedKestrelHostRegistry.Instance.Acquire(
                    key, (HttpsTransportListener)null!, "/test", acc => MakeStubHost(), ""));
        }

        [Test]
        public void AccessorIsAlreadyWiredWhenFactoryRuns()
        {
            // The registry wires SharedHostAccessor.Instance BEFORE calling
            // the hostFactory, so the factory (and the Kestrel Startup that
            // it builds) can resolve the SharedKestrelHost via DI during
            // the synchronous IHost.StartAsync that follows.
            var key = NewKey();
            SharedHostAccessor? captured = null;
            using SharedHostLease lease = SharedKestrelHostRegistry.Instance.Acquire(
                key,
                (HttpsTransportListener)null!,
                "/test",
                acc =>
                {
                    captured = acc;
                    Assert.That(acc.Instance, Is.Not.Null,
                        "Registry must wire SharedKestrelHost into the accessor before calling factory.");
                    return MakeStubHost();
                },
                kThumbprint);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Instance, Is.Not.Null);
            Assert.That(captured.Instance!.Key, Is.EqualTo(key));
            Assert.That(captured.Instance.ServerCertificateThumbprint, Is.EqualTo(kThumbprint));
        }

        private static SharedHostKey NewKey()
        {
            // unique port per test to avoid cross-test pollution in the
            // process-wide registry (tests are Parallelizable).
            return new SharedHostKey(
                $"shared-test-{Guid.NewGuid():N}",
                UnsecureRandom.Shared.Next(40000, 60000));
        }

        private static IHost MakeStubHost()
        {
            // A genuine HostBuilder with no Kestrel — Start/Stop are no-ops
            // for the purposes of the registry's lifecycle assertions.
            return new HostBuilder().Build();
        }

        private static class UnsecureRandom
        {
            internal static Random Shared { get; } = new(Environment.TickCount);
        }
    }
}

#endif // NET6_0_OR_GREATER
