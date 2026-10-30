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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("X509IdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class X509IdentityTokenHandlerTests
    {
        /// <summary>
        /// Verifies the <see cref="X509IdentityTokenHandler(CertificateIdentifier,
        /// ICertificatePasswordProvider, ICertificateProvider)"/> ctor:
        /// the handler is a POCO (no live cert reference) and
        /// <see cref="X509IdentityTokenHandler.SignAsync"/> resolves
        /// the private-key cert via <see cref="ICertificateProvider"/>
        /// on each call.
        /// </summary>
        [Test]
        public async Task CertificateIdentifierCtorResolvesViaProviderForSignAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-x509handler-id-" + System.Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(storePath);
            try
            {
                using Certificate cert = CertificateBuilder
                    .Create("CN=X509HandlerCertIdentifier, O=OPC Foundation")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                await cert.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    storePath,
                    password: null,
                    telemetry).ConfigureAwait(false);

                var id = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storePath,
                    Thumbprint = cert.Thumbprint
                };

                using var manager = new CertificateManager(telemetry);
                var passwordProvider = new CertificatePasswordProvider();
                var handler = new X509IdentityTokenHandler(
                    id,
                    passwordProvider,
                    manager.CertificateProvider);

                Assert.That(handler.Token, Is.Not.Null,
                    "Wire-format X509IdentityToken must be populated.");
                Assert.That(((X509IdentityToken)handler.Token).CertificateData.Length,
                    Is.GreaterThan(0));

                SignatureData signature = await handler.SignAsync(
                    [0x01, 0x02, 0x03, 0x04],
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

                Assert.That(signature, Is.Not.Null);
                Assert.That(signature.Signature.Length, Is.GreaterThan(0));
            }
            finally
            {
                if (Directory.Exists(storePath))
                {
                    Directory.Delete(storePath, true);
                }
            }
        }

        [Test]
        public async Task CreateAsyncAwaitsProviderUntilReleasedMaterializesTokenAndSigns()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerAsyncFactory, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider();
            var identifier = CreateIdentifier(cert);
            var passwordProvider = new CertificatePasswordProvider();
            byte[] data = [0x01, 0x02, 0x03, 0x04];

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                identifier,
                passwordProvider,
                provider);

            await provider.Started.ConfigureAwait(false);
            Assert.That(creation.IsCompleted, Is.False,
                "The factory must remain pending while certificate resolution is pending.");

            provider.Release(cert);
            X509IdentityTokenHandler handler = await creation.ConfigureAwait(false);

            Assert.That(((X509IdentityToken)handler.Token).CertificateData,
                Is.EqualTo(cert.RawData.ToByteString()),
                "The wire token must eagerly contain the resolved certificate bytes.");

            SignatureData signature = await handler.SignAsync(
                data,
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

            Assert.That(signature.Algorithm, Is.EqualTo(SecurityAlgorithms.RsaSha256));
            Assert.That(signature.Signature.Length, Is.GreaterThan(0));
            Assert.That(await handler.VerifyAsync(
                data,
                signature,
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false), Is.True,
                "The signature must verify with the public certificate materialized from the token.");
            Assert.That(provider.ResolutionCount, Is.EqualTo(1),
                "Signing should use the provider's synchronous cache hit after factory resolution.");
        }

        [Test]
        public async Task CreateAsyncCancellationBeforeProviderReleaseThrowsOperationCanceledException()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerAsyncCancel, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider();
            using var cancellation = new CancellationTokenSource();

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                CreateIdentifier(cert),
                new CertificatePasswordProvider(),
                provider,
                ct: cancellation.Token);

            await provider.Started.ConfigureAwait(false);
            Assert.That(creation.IsCompleted, Is.False,
                "The factory should still be awaiting the unresolved provider operation.");

            cancellation.Cancel();

            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await creation.ConfigureAwait(false));
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(provider.IsReleased, Is.False,
                "Cancellation must complete without releasing a certificate.");
            Assert.That(provider.ResolutionCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CreateAsyncCancellationAfterProviderReturnsStillThrowsOperationCanceledException()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerAsyncIgnoredCancel, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider(ignoreCancellation: true);
            using var cancellation = new CancellationTokenSource();

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                CreateIdentifier(cert),
                new CertificatePasswordProvider(),
                provider,
                ct: cancellation.Token);

            await provider.Started.ConfigureAwait(false);
            cancellation.Cancel();
            Assert.That(creation.IsCompleted, Is.False,
                "The provider deliberately ignores cancellation until the test releases it.");

            provider.Release(cert);

            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await creation.ConfigureAwait(false));

            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(provider.IsReleased, Is.True);
        }

        [Test]
        public async Task CreateAsyncMissingCertificateThrowsBadIdentityTokenInvalid()
        {
            using Certificate identity = CertificateBuilder
                .Create("CN=X509HandlerMissing, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider();

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                CreateIdentifier(identity),
                new CertificatePasswordProvider(),
                provider);

            await provider.Started.ConfigureAwait(false);
            provider.Release(null);

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await creation.ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(exception.Message, Does.Contain("Cannot resolve a private-key certificate"));
        }

        [Test]
        public async Task CreateAsyncCertificateWithoutPrivateKeyThrowsBadIdentityTokenInvalid()
        {
            using Certificate privateKeyCertificate = CertificateBuilder
                .Create("CN=X509HandlerPublicOnly, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate publicOnlyCertificate = Certificate.FromRawData(privateKeyCertificate.RawData);
            var provider = new DeferredCertificateProvider();

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                CreateIdentifier(privateKeyCertificate),
                new CertificatePasswordProvider(),
                provider);

            await provider.Started.ConfigureAwait(false);
            provider.Release(publicOnlyCertificate);

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await creation.ConfigureAwait(false));

            Assert.That(publicOnlyCertificate.HasPrivateKey, Is.False);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(exception.Message, Does.Contain("Cannot resolve a private-key certificate"));
        }

        [Test]
        public async Task CreateAsyncProviderExceptionPropagatesOriginalException()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerAsyncFailure, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider();
            var expected = new InvalidOperationException("certificate store failed");

            Task<X509IdentityTokenHandler> creation = X509IdentityTokenHandler.CreateAsync(
                CreateIdentifier(cert),
                new CertificatePasswordProvider(),
                provider);

            await provider.Started.ConfigureAwait(false);
            provider.Fail(expected);

            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await creation.ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(provider.ResolutionCount, Is.EqualTo(1));
        }

        [Test]
        public async Task UserIdentityCreateAsyncAwaitsProviderUntilReleasedAndCreatesSigningIdentity()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=UserIdentityAsyncCertificate, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var provider = new DeferredCertificateProvider();
            var identifier = CreateIdentifier(cert);
            byte[] data = [0x10, 0x20, 0x30, 0x40];

            Task<UserIdentity> creation = UserIdentity.CreateAsync(
                identifier,
                new CertificatePasswordProvider(),
                provider);

            await provider.Started.ConfigureAwait(false);
            Assert.That(creation.IsCompleted, Is.False,
                "UserIdentity.CreateAsync must await asynchronous certificate resolution.");

            provider.Release(cert);
            UserIdentity identity = await creation.ConfigureAwait(false);

            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
            Assert.That(((X509IdentityToken)identity.TokenHandler.Token).CertificateData,
                Is.EqualTo(cert.RawData.ToByteString()));

            SignatureData signature = await identity.TokenHandler.SignAsync(
                data,
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

            Assert.That(signature.Algorithm, Is.EqualTo(SecurityAlgorithms.RsaSha256));
            Assert.That(signature.Signature.Length, Is.GreaterThan(0));
            Assert.That(await identity.TokenHandler.VerifyAsync(
                data,
                signature,
                SecurityPolicies.Basic256Sha256).ConfigureAwait(false), Is.True);
            Assert.That(provider.ResolutionCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A user identity token signature that carries an unexpected /
        /// mismatched signature algorithm must be surfaced as
        /// <see cref="StatusCodes.BadUserSignatureInvalid"/>, not the
        /// channel-level <see cref="StatusCodes.BadSecurityChecksFailed"/> that
        /// the shared <see cref="SecurityPolicies.VerifySignatureData"/> raises
        /// internally (OPC UA Part 4 5.7.3.3). Regression for CTT Security User
        /// X509 018, which prefers BadUserSignatureInvalid on secure channels.
        /// </summary>
        [Test]
        public async Task VerifyWithMismatchedSignatureAlgorithmYieldsBadUserSignatureInvalid()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerWrongAlg, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var token = new X509IdentityToken
            {
                CertificateData = cert.RawData.ToByteString()
            };
            var handler = new X509IdentityTokenHandler(token);

            // Basic256Sha256 expects RsaSha256; RsaSha1 is a valid-but-mismatched
            // algorithm that makes VerifySignatureData fall through to its
            // BadSecurityChecksFailed guard. The signature bytes are irrelevant
            // because the algorithm mismatch short-circuits before the crypto verify.
            var signature = new SignatureData
            {
                Algorithm = SecurityAlgorithms.RsaSha1,
                Signature = new byte[] { 0x00 }.ToByteString()
            };

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                () => handler.VerifyAsync(
                    [0x01, 0x02, 0x03, 0x04],
                    signature,
                    SecurityPolicies.Basic256Sha256).AsTask());

            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCodes.BadUserSignatureInvalid),
                "A user-token signature with a mismatched algorithm is an invalid user signature.");
        }

        private static CertificateIdentifier CreateIdentifier(Certificate certificate)
        {
            return new CertificateIdentifier
            {
                Thumbprint = certificate.Thumbprint
            };
        }

        private sealed class DeferredCertificateProvider : ICertificateProvider
        {
            public DeferredCertificateProvider(bool ignoreCancellation = false)
            {
                m_ignoreCancellation = ignoreCancellation;
            }

            public Task Started => m_started.Task;

            public bool IsReleased => m_resolution.Task.IsCompleted;

            public int ResolutionCount => m_resolutionCount;

            public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
            {
                // The async factory's first lookup is intentionally cold; after
                // release, the test certificate is returned as a cache hit for signing.
                return m_releasedCertificate?.AddRef();
            }

            public ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
                CertificateIdentifier identifier,
                ICertificatePasswordProvider? passwordProvider = null,
                string? applicationUri = null,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_resolutionCount);
                m_started.TrySetResult(true);
                return new ValueTask<Certificate?>(ResolveAsync(ct));
            }

            public void Release(Certificate? certificate)
            {
                m_releasedCertificate = certificate;
                m_resolution.TrySetResult(certificate);
            }

            public void Fail(Exception exception)
            {
                m_resolution.TrySetException(exception);
            }

            private async Task<Certificate?> ResolveAsync(CancellationToken ct)
            {
                if (m_ignoreCancellation)
                {
                    Certificate? uncancelled = await m_resolution.Task.ConfigureAwait(false);
                    return uncancelled?.AddRef();
                }

                var cancelled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = ct.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                    cancelled);
                await Task.WhenAny(m_resolution.Task, cancelled.Task).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Certificate? certificate = await m_resolution.Task.ConfigureAwait(false);
                return certificate?.AddRef();
            }

            private readonly TaskCompletionSource<Certificate?> m_resolution =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly bool m_ignoreCancellation;
            private int m_resolutionCount;
            private Certificate? m_releasedCertificate;
        }
    }
}
