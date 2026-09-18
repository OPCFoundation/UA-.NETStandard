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
 *
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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    public class JwksIssuerKeyResolverTests
    {
        [Test]
        public async Task GetKeysAsyncFetchesAndCachesInitialKeys()
        {
            using var rsa = RSA.Create(2048);
            using var handler = new QueueMessageHandler(CreateJwks(CreateRsaJwk(rsa, "kid-rsa", "sig")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                new FakeTimeProvider(),
                TimeSpan.FromMinutes(5));

            IReadOnlyList<IIssuerVerificationKey> keys = await resolver.GetKeysAsync("kid-rsa").ConfigureAwait(false);
            IReadOnlyList<IIssuerVerificationKey> cached = await resolver.GetKeysAsync("kid-rsa").ConfigureAwait(false);

            Assert.That(keys, Has.Count.EqualTo(1));
            Assert.That(cached, Has.Count.EqualTo(1));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetKeysAsyncRefreshesMissAfterMinimumInterval()
        {
            using var first = RSA.Create(2048);
            using var second = RSA.Create(2048);
            var timeProvider = new FakeTimeProvider();
            using var handler = new QueueMessageHandler(
                CreateJwks(CreateRsaJwk(first, "kid-1", "sig")),
                CreateJwks(CreateRsaJwk(second, "kid-2", "sig")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                timeProvider,
                TimeSpan.FromMinutes(5));

            Assert.That(await resolver.GetKeysAsync("kid-1").ConfigureAwait(false), Has.Count.EqualTo(1));
            Assert.That(await resolver.GetKeysAsync("kid-2").ConfigureAwait(false), Is.Empty);
            Assert.That(handler.RequestCount, Is.EqualTo(1));

            timeProvider.Advance(TimeSpan.FromMinutes(5));
            IReadOnlyList<IIssuerVerificationKey> refreshed = await resolver
                .GetKeysAsync("kid-2")
                .ConfigureAwait(false);

            Assert.That(refreshed, Has.Count.EqualTo(1));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        }

        [Test]
        public async Task GetKeysAsyncParsesRsaAndEcKeys()
        {
            using var rsa = RSA.Create(2048);
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var handler = new QueueMessageHandler(CreateJwks(
                CreateRsaJwk(rsa, "kid-rsa", "sig"),
                CreateEcJwk(ecdsa, "kid-ec")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                new FakeTimeProvider(),
                TimeSpan.FromMinutes(5));

            IReadOnlyList<IIssuerVerificationKey> keys = await resolver.GetKeysAsync(null).ConfigureAwait(false);
            byte[] data = Encoding.ASCII.GetBytes("header.payload");
            byte[] rsaSignature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            byte[] ecSignature = ecdsa.SignData(data, HashAlgorithmName.SHA256);

            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(FindKey(keys, "kid-rsa").VerifySignature(data, rsaSignature), Is.True);
            Assert.That(FindKey(keys, "kid-ec").VerifySignature(data, ecSignature), Is.True);
        }

        [Test]
        public async Task GetKeysAsyncFiltersEncryptionOnlyKeys()
        {
            using var rsa = RSA.Create(2048);
            using var handler = new QueueMessageHandler(CreateJwks(CreateRsaJwk(rsa, "kid-enc", "enc")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                new FakeTimeProvider(),
                TimeSpan.FromMinutes(5));

            IReadOnlyList<IIssuerVerificationKey> keys = await resolver.GetKeysAsync(null).ConfigureAwait(false);

            Assert.That(keys, Is.Empty);
        }

        [Test]
        public async Task GetKeysAsyncKeepsPreviousSnapshotUsableDuringRefresh()
        {
            using var first = RSA.Create(2048);
            using var second = RSA.Create(2048);
            var timeProvider = new FakeTimeProvider();
            using var handler = new QueueMessageHandler(
                CreateJwks(CreateRsaJwk(first, "kid-1", "sig")),
                CreateJwks(CreateRsaJwk(second, "kid-2", "sig")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                timeProvider,
                TimeSpan.FromMinutes(5));

            IReadOnlyList<IIssuerVerificationKey> previous = await resolver
                .GetKeysAsync("kid-1")
                .ConfigureAwait(false);
            timeProvider.Advance(TimeSpan.FromMinutes(5));

            Assert.That(
                await resolver.GetKeysAsync("kid-2").ConfigureAwait(false),
                Has.Count.EqualTo(1));

            byte[] data = Encoding.ASCII.GetBytes("header.payload");
            byte[] signature = first.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(previous[0].VerifySignature(data, signature), Is.True);
        }

        [Test]
        public async Task RetiredKeysAreCollectibleWhileResolverRemainsAliveAsync()
        {
            using var rsa = RSA.Create(2048);
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string jwks = CreateJwks(CreateRsaJwk(rsa, "rsa", "sig"), CreateEcJwk(ec, "ec"));
            using var handler = new QueueMessageHandler(jwks, jwks);
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test", "https://issuer.example.test/keys",
                httpClient, new FakeTimeProvider(), TimeSpan.Zero);
            WeakReference<IIssuerVerificationKey>[] retired = await ReadWeakKeysAsync(resolver)
                .ConfigureAwait(false);

            await resolver.GetKeysAsync("missing").ConfigureAwait(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(handler.RequestCount, Is.EqualTo(2));
            Assert.That(retired, Has.Length.EqualTo(2));
            foreach (WeakReference<IIssuerVerificationKey> key in retired)
            {
                Assert.That(key.TryGetTarget(out _), Is.False, "The resolver must not retain retired keys.");
            }
            GC.KeepAlive(resolver);
        }

        [Test]
        public async Task ReturnedKeysSurviveRepeatedRefreshAndResolverDisposalAsync()
        {
            using var rsa = RSA.Create(2048);
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string jwks = CreateJwks(CreateRsaJwk(rsa, "rsa", "sig"), CreateEcJwk(ec, "ec"));
            using var handler = new QueueMessageHandler(Enumerable.Repeat(jwks, 33).ToArray());
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test", "https://issuer.example.test/keys",
                httpClient, new FakeTimeProvider(), TimeSpan.Zero);
            IReadOnlyList<IIssuerVerificationKey> retained = await resolver.GetKeysAsync(null).ConfigureAwait(false);
            byte[] data = Encoding.ASCII.GetBytes("retained-reader");
            byte[] rsaSignature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            byte[] ecSignature = ec.SignData(data, HashAlgorithmName.SHA256);

            for (int i = 0; i < 32; i++)
            {
                await resolver.GetKeysAsync("missing").ConfigureAwait(false);
                Assert.That(FindKey(retained, "rsa").VerifySignature(data, rsaSignature), Is.True);
                Assert.That(FindKey(retained, "ec").VerifySignature(data, ecSignature), Is.True);
            }
            IReadOnlyList<IIssuerVerificationKey> current = await resolver.GetKeysAsync(null).ConfigureAwait(false);
            resolver.Dispose();

            Assert.That(handler.RequestCount, Is.EqualTo(33));
            Assert.That(FindKey(retained, "rsa").VerifySignature(data, rsaSignature), Is.True);
            Assert.That(FindKey(retained, "ec").VerifySignature(data, ecSignature), Is.True);
            Assert.That(FindKey(current, "rsa").VerifySignature(data, rsaSignature), Is.True);
            Assert.That(FindKey(current, "ec").VerifySignature(data, ecSignature), Is.True);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await resolver.GetKeysAsync(null).ConfigureAwait(false));
        }

        [Test]
        public async Task DisposeDuringFetchDoesNotDisposeAnActiveRefreshGateAsync()
        {
            using var rsa = RSA.Create(2048);
            using var handler = new GatedMessageHandler(CreateJwks(CreateRsaJwk(rsa, "rsa", "sig")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test", "https://issuer.example.test/keys",
                httpClient, new FakeTimeProvider(), TimeSpan.Zero);
            Task<IReadOnlyList<IIssuerVerificationKey>> pending = resolver.GetKeysAsync("rsa").AsTask();
            try
            {
                await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                resolver.Dispose();
            }
            finally
            {
                handler.Release.TrySetResult(true);
            }

            ObjectDisposedException error = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await pending.ConfigureAwait(false));
            Assert.That(error.ObjectName, Is.EqualTo(nameof(JwksIssuerKeyResolver)));
        }

        [Test]
        public async Task GetKeysAsyncSkipsMalformedKeyAndRetainsUsableKeys()
        {
            using var rsa = RSA.Create(2048);
            string malformedEc =
                "{\"kty\":\"EC\",\"kid\":\"bad-ec\",\"use\":\"sig\",\"key_ops\":[\"verify\"]," +
                "\"alg\":\"ES256\",\"crv\":\"secp256k1\",\"x\":\"AA\",\"y\":\"AA\"}";
            using var handler = new QueueMessageHandler(
                CreateJwks(malformedEc, CreateRsaJwk(rsa, "kid-rsa", "sig")));
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                new FakeTimeProvider(),
                TimeSpan.FromMinutes(5));

            IReadOnlyList<IIssuerVerificationKey> keys = await resolver
                .GetKeysAsync(null)
                .ConfigureAwait(false);

            Assert.That(keys, Has.Count.EqualTo(1));
            Assert.That(keys[0].KeyId, Is.EqualTo("kid-rsa"));
        }

        [Test]
        public async Task GetKeysAsyncThrottlesFailedRefreshes()
        {
            using var rsa = RSA.Create(2048);
            var timeProvider = new FakeTimeProvider();
            using var handler = new QueueMessageHandler(
                CreateJwks(CreateRsaJwk(rsa, "kid-rsa", "sig")),
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.ServiceUnavailable);
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            using var resolver = new JwksIssuerKeyResolver(
                "https://issuer.example.test",
                "https://issuer.example.test/keys",
                httpClient,
                timeProvider,
                TimeSpan.FromMinutes(5));

            await resolver.GetKeysAsync("kid-rsa").ConfigureAwait(false);
            timeProvider.Advance(TimeSpan.FromMinutes(5));

            Assert.That(
                await resolver.GetKeysAsync("missing").ConfigureAwait(false),
                Is.Empty);
            Assert.That(
                await resolver.GetKeysAsync("missing").ConfigureAwait(false),
                Is.Empty);
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<WeakReference<IIssuerVerificationKey>[]> ReadWeakKeysAsync(
            JwksIssuerKeyResolver resolver)
        {
            IReadOnlyList<IIssuerVerificationKey> keys = await resolver.GetKeysAsync(null).ConfigureAwait(false);
            return keys.Select(key => new WeakReference<IIssuerVerificationKey>(key)).ToArray();
        }

        private static IIssuerVerificationKey FindKey(IReadOnlyList<IIssuerVerificationKey> keys, string kid)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i].KeyId, kid, StringComparison.Ordinal))
                {
                    return keys[i];
                }
            }
            throw new AssertionException("Expected key was not returned.");
        }

        private static string CreateJwks(params string[] keys)
        {
            return "{\"keys\":[" + string.Join(",", keys) + "]}";
        }

        private static string CreateRsaJwk(RSA rsa, string kid, string use)
        {
            RSAParameters parameters = rsa.ExportParameters(false);
            return "{\"kty\":\"RSA\",\"kid\":\"" +
                kid +
                "\",\"use\":\"" +
                use +
                "\",\"key_ops\":[\"verify\"],\"alg\":\"RS256\",\"n\":\"" +
                Base64UrlEncode(parameters.Modulus) +
                "\",\"e\":\"" +
                Base64UrlEncode(parameters.Exponent) +
                "\"}";
        }

        private static string CreateEcJwk(ECDsa ecdsa, string kid)
        {
            ECParameters parameters = ecdsa.ExportParameters(false);
            return "{\"kty\":\"EC\",\"kid\":\"" +
                kid +
                "\",\"use\":\"sig\",\"key_ops\":[\"verify\"],\"alg\":\"ES256\",\"crv\":\"P-256\",\"x\":\"" +
                Base64UrlEncode(parameters.Q.X) +
                "\",\"y\":\"" +
                Base64UrlEncode(parameters.Q.Y) +
                "\"}";
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class GatedMessageHandler(string jwks) : HttpMessageHandler
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jwks, Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed class QueueMessageHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> m_responses = new();

            public QueueMessageHandler(
                params string[] responses)
            {
                foreach (string response in responses)
                {
                    m_responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(response, Encoding.UTF8, "application/json")
                    });
                }
            }

            public QueueMessageHandler(params object[] responses)
            {
                foreach (object response in responses)
                {
                    if (response is HttpStatusCode statusCode)
                    {
                        m_responses.Enqueue(new HttpResponseMessage(statusCode));
                    }
                    else
                    {
                        m_responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                (string)response,
                                Encoding.UTF8,
                                "application/json")
                        });
                    }
                }
            }

            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                return Task.FromResult(m_responses.Dequeue());
            }
        }
    }
}
