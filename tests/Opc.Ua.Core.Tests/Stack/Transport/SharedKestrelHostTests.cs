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
        public async Task AcquireWithFirstListenerInvokesHostFactoryAsync()
        {
            SharedHostKey key = NewKey();
            var listener = (HttpsTransportListener?)null;
            int factoryInvocations = 0;
            try
            {
                await using SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                    key,
                    listener!,
                    "/test",
                    acc =>
                    {
                        factoryInvocations++;
                        return MakeStubHost();
                    },
                    kThumbprint).ConfigureAwait(false);
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
        public async Task AcquireWithSecondListenerReusesExistingHostAsync()
        {
            SharedHostKey key = NewKey();
            int factoryInvocations = 0;
            await using SharedHostLease lease1 = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/listenerA",
                acc =>
                {
                    factoryInvocations++;
                    return MakeStubHost();
                },
                kThumbprint).ConfigureAwait(false);
            await using SharedHostLease lease2 = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/listenerB",
                acc =>
                {
                    factoryInvocations++;
                    return MakeStubHost();
                },
                kThumbprint).ConfigureAwait(false);
            Assert.That(factoryInvocations, Is.EqualTo(1), "Second Acquire must reuse host, not call factory.");
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2));
        }

        [Test]
        public async Task AcquireWithMismatchedThumbprintThrowsAsync()
        {
            SharedHostKey key = NewKey();
            await using SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint).ConfigureAwait(false);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await SharedKestrelHostRegistry.Instance.AcquireAsync(
                    key,
                    null!,
                    "/test",
                    acc => MakeStubHost(),
                    kOtherThumbprint).ConfigureAwait(false))!;
            Assert.That(ex.Message, Does.Contain(kThumbprint));
            Assert.That(ex.Message, Does.Contain(kOtherThumbprint));
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));
        }

        /// <summary>
        /// A listener whose host-level settings (for example mutual TLS)
        /// differ from the host's must not join it: the host serves one set of
        /// settings for every listener and is rebuilt from any of them on a
        /// certificate rotation.
        /// </summary>
        [Test]
        public async Task AcquireWithDifferentHostSettingsThrowsAsync()
        {
            SharedHostKey key = NewKey();
            await using SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/first",
                acc => MakeStubHost(),
                kThumbprint,
                "mtls=1").ConfigureAwait(false);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await SharedKestrelHostRegistry.Instance.AcquireAsync(
                    key,
                    null!,
                    "/second",
                    acc => MakeStubHost(),
                    kThumbprint,
                    "mtls=0").ConfigureAwait(false))!;
            Assert.That(ex.Message, Does.Contain("mtls=0"));
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));

            await using SharedHostLease same = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/third",
                acc => MakeStubHost(),
                kThumbprint,
                "mtls=1").ConfigureAwait(false);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2));
        }

        [Test]
        public async Task ReleasingLastLeaseStopsTheHostAsync()
        {
            SharedHostKey key = NewKey();
            SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint).ConfigureAwait(false);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(1));
            await lease.DisposeAsync().ConfigureAwait(false);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.Zero);
        }

        [Test]
        public async Task DoubleDisposeOfLeaseIsIdempotentAsync()
        {
            SharedHostKey key = NewKey();
            SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/test",
                acc => MakeStubHost(),
                kThumbprint).ConfigureAwait(false);
            await lease.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await lease.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public async Task AcquireValidatesArgumentsAsync()
        {
            SharedHostKey key = NewKey();
            Assert.ThrowsAsync<ArgumentNullException>(async () => await SharedKestrelHostRegistry.Instance.AcquireAsync(
                    key, null!, "/test", null!, kThumbprint).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentException>(async () => await SharedKestrelHostRegistry.Instance.AcquireAsync(
                    key, null!, "/test", acc => MakeStubHost(), string.Empty).ConfigureAwait(false));
        }

        [Test]
        public async Task AccessorIsAlreadyWiredWhenFactoryRunsAsync()
        {
            // The registry wires SharedHostAccessor.Instance BEFORE calling
            // the hostFactory, so the factory (and the Kestrel Startup that
            // it builds) can resolve the SharedKestrelHost via DI during
            // the synchronous IHost.StartAsync that follows.
            SharedHostKey key = NewKey();
            SharedHostAccessor? captured = null;
            await using SharedHostLease lease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key,
                null!,
                "/test",
                acc =>
                {
                    captured = acc;
                    Assert.That(acc.Instance, Is.Not.Null,
                        "Registry must wire SharedKestrelHost into the accessor before calling factory.");
                    return MakeStubHost();
                },
                kThumbprint).ConfigureAwait(false);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Instance, Is.Not.Null);
            Assert.That(captured.Instance!.Key, Is.EqualTo(key));
            Assert.That(captured.Instance.ServerCertificateThumbprint, Is.EqualTo(kThumbprint));
        }

        /// <summary>
        /// The TLS certificate a shared host serves is owned by the host: the
        /// listener that built the host can go away while another listener
        /// keeps the host (and the certificate) in use.
        /// </summary>
        [Test]
        public async Task SharedHostCertificateLivesAsLongAsTheHostAsync()
        {
            SharedHostKey key = NewKey();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var listenerA = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            await using var listenerB = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            var certificate = new DisposeTracker();
            SharedHostLease leaseA = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerA, "/a",
                acc =>
                {
                    acc.Instance!.OwnCertificate(certificate);
                    return MakeStubHost();
                },
                kThumbprint).ConfigureAwait(false);
            SharedHostLease leaseB = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerB, "/b", _ => MakeStubHost(), kThumbprint).ConfigureAwait(false);

            await leaseA.DisposeAsync().ConfigureAwait(false);
            Assert.That(certificate.Disposed, Is.False, "The host still serves the certificate for B.");

            await leaseB.DisposeAsync().ConfigureAwait(false);
            Assert.That(certificate.Disposed, Is.True, "The last lease stops the host and its certificate.");
        }

        /// <summary>
        /// Rotating the certificate from one listener rebuilds the shared host
        /// with the new certificate and keeps every other listener on it, so a
        /// later restart of the other listener with the new certificate rejoins.
        /// </summary>
        [Test]
        public async Task RotateCertificateRebuildsHostAndKeepsOtherListenersAsync()
        {
            SharedHostKey key = NewKey();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var listenerA = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            await using var listenerB = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            var oldCertificate = new DisposeTracker();
            var newCertificate = new DisposeTracker();
            await using SharedHostLease leaseA = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerA, "/a",
                acc =>
                {
                    acc.Instance!.OwnCertificate(oldCertificate);
                    return MakeStubHost();
                },
                kThumbprint).ConfigureAwait(false);
            SharedHostLease leaseB = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerB, "/b", _ => MakeStubHost(), kThumbprint).ConfigureAwait(false);

            int rebuilt = 0;
            bool rotated = await SharedKestrelHostRegistry.Instance.RotateCertificateAsync(
                key, listenerA,
                acc =>
                {
                    rebuilt++;
                    acc.Instance!.OwnCertificate(newCertificate);
                    return MakeStubHost();
                },
                kOtherThumbprint).ConfigureAwait(false);

            Assert.That(rotated, Is.True);
            Assert.That(rebuilt, Is.EqualTo(1));
            Assert.That(oldCertificate.Disposed, Is.True, "The old host and certificate are released.");
            Assert.That(newCertificate.Disposed, Is.False);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2),
                "The other listener must stay on the rebuilt host.");

            // the other listener's own rotation is then a no-op on the host, and a
            // restart of it with the new certificate rejoins the same host.
            Assert.That(await SharedKestrelHostRegistry.Instance.RotateCertificateAsync(
                key, listenerB, _ => throw new InvalidOperationException("No second rebuild."),
                kOtherThumbprint).ConfigureAwait(false), Is.True);
            await leaseB.DisposeAsync().ConfigureAwait(false);
            await using SharedHostLease rejoined = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerB, "/b", _ => throw new InvalidOperationException("Host must be reused."),
                kOtherThumbprint).ConfigureAwait(false);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2));
        }

        /// <summary>
        /// A rotation whose new host fails to start restarts a host with the
        /// previous certificate for every listener and surfaces the error.
        /// </summary>
        [Test]
        public async Task RotateCertificateStartFailureRestoresPreviousHostAsync()
        {
            SharedHostKey key = NewKey();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var listenerA = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            await using var listenerB = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
            await using SharedHostLease leaseA = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerA, "/a", _ => MakeStubHost(), kThumbprint).ConfigureAwait(false);
            await using SharedHostLease leaseB = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, listenerB, "/b", _ => MakeStubHost(), kThumbprint).ConfigureAwait(false);

            var restoredCertificate = new DisposeTracker();
            int restored = 0;
            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await SharedKestrelHostRegistry.Instance.RotateCertificateAsync(
                    key, listenerA,
                    _ => throw new InvalidOperationException("new host failed"),
                    kOtherThumbprint,
                    acc =>
                    {
                        restored++;
                        acc.Instance!.OwnCertificate(restoredCertificate);
                        return MakeStubHost();
                    }).ConfigureAwait(false))!;

            Assert.That(ex.Message, Is.EqualTo("new host failed"));
            Assert.That(restored, Is.EqualTo(1));
            Assert.That(restoredCertificate.Disposed, Is.False);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(2),
                "Both listeners stay registered on the restored host.");
            await using SharedHostLease rejoined = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                key, null!, "/c", _ => throw new InvalidOperationException("Host must be reused."),
                kThumbprint).ConfigureAwait(false);
            Assert.That(SharedKestrelHostRegistry.Instance.ListenerCount(key), Is.EqualTo(3));
        }

        private sealed class DisposeTracker : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private static SharedHostKey NewKey()
        {
            // unique port per test to avoid cross-test pollution in the
            // process-wide registry (tests are Parallelizable).
#pragma warning disable CA5394 // Random is non-crypto-safe by design — only used to pick a unique test port
            int port = UnsecureRandom.Shared.Next(40000, 60000);
#pragma warning restore CA5394
            return new SharedHostKey($"shared-test-{Guid.NewGuid():N}", port);
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
