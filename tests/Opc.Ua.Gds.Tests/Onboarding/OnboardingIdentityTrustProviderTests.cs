/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingIdentityTrustProviderTests
    {
        [Test]
        public async Task IsTrustedAsyncScopesTicketAnchorsAndMergesIssuerAndCrlMaterialAsync()
        {
            using Certificate ticketAuthority = CreateAuthority("CN=Ticket identity authority");
            using Certificate ticketIssuer = CreateAuthority("CN=Ticket authority issuer");
            using Certificate suppliedIssuer = CreateAuthority("CN=Supplied issuer");
            using Certificate managedTrusted = CreateAuthority("CN=Managed trust certificate");
            using Certificate managedIssuer = CreateAuthority("CN=Managed issuer certificate");
            using Certificate identity = CreateIdentityCertificate(
                "CN=Identity with CDP", "2.5.29.31");
            byte[] trustedCrl = CreateCrl(managedTrusted, identity).RawData;
            byte[] issuerCrl = CreateCrl(managedIssuer, identity).RawData;

            using TrustListData snapshot = TrustList(
                [managedTrusted], [managedIssuer], [trustedCrl], [issuerCrl]);
            var manager = CreateManager(
                () => snapshot,
                valid: true,
                out ValidationCapture capture);
            var logger = new RecordingLogger<OnboardingIdentityTrustProvider>();
            var provider = new OnboardingIdentityTrustProvider(manager.Object, logger);
            using var suppliedIssuers = new CertificateCollection { suppliedIssuer };
            DeviceIdentityTicketType ticket = DeviceTicket(ticketAuthority, ticketIssuer);
            using var cancellation = new CancellationTokenSource();

            bool trusted = await provider.IsTrustedAsync(
                identity, suppliedIssuers, ticket, cancellation.Token).ConfigureAwait(false);

            Assert.That(trusted, Is.True);
            Assert.That(capture.ReadMasks, Is.EqualTo(TrustListMasks.All));
            Assert.That(capture.ReadTrustList, Is.EqualTo(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities));
            Assert.That(capture.ReadToken, Is.EqualTo(cancellation.Token));
            Assert.That(capture.ValidatedIdentityRawData, Is.EqualTo(identity.RawData));
            Assert.That(capture.ValidatedToken, Is.EqualTo(cancellation.Token));
            Assert.That(capture.Options, Is.Not.Null);
            CertificateValidationScope scope = capture.Options!.TrustScope!;
            Assert.That(scope.TrustedCertificates, Has.Count.EqualTo(1));
            Assert.That(scope.TrustedCertificates[0].ToArray(), Is.EqualTo(ticketAuthority.RawData));
            Assert.That(scope.IssuerCertificates, Has.Count.EqualTo(4));
            Assert.That(ContainsDer(scope.IssuerCertificates, ticketIssuer.RawData), Is.True);
            Assert.That(ContainsDer(scope.IssuerCertificates, suppliedIssuer.RawData), Is.True);
            Assert.That(ContainsDer(scope.IssuerCertificates, managedTrusted.RawData), Is.True);
            Assert.That(ContainsDer(scope.IssuerCertificates, managedIssuer.RawData), Is.True);
            Assert.That(scope.CertificateRevocationLists, Has.Count.EqualTo(2));
            Assert.That(
                scope.CertificateRevocationLists[0].ToArray(),
                Is.EqualTo(trustedCrl));
            Assert.That(
                scope.CertificateRevocationLists[1].ToArray(),
                Is.EqualTo(issuerCrl));
            Assert.That(capture.Options.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(capture.Options.RejectSHA1SignedCertificates, Is.True);
            Assert.That(capture.Options.RejectUnknownRevocationStatus, Is.True);
            Assert.That(capture.Options.MinimumCertificateKeySize, Is.EqualTo((ushort)2048));
            Assert.That(capture.Options.ValidateForSigning, Is.False,
                "Device identities are application certificates, not ticket-signing certificates.");
            Assert.That(capture.Options.AcceptError, Is.Not.Null);
            Assert.That(capture.Options.AcceptError!(identity, new ServiceResult(StatusCodes.BadCertificateUntrusted)),
                Is.False);
            Assert.That(logger.Messages, Is.Empty,
                "A certificate advertising revocation information must not take the logged skip path.");
            Assert.That(() => snapshot.TrustedCertificates[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => snapshot.IssuerCertificates[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(suppliedIssuers[0].RawData, Is.EqualTo(suppliedIssuer.RawData),
                "The provider must not dispose issuer handles borrowed from its caller.");

            manager.Verify(m => m.ReadTrustListAsync(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                TrustListMasks.All,
                cancellation.Token), Times.Once);
            manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                It.IsAny<ValidationOptions>(),
                cancellation.Token), Times.Once);
            manager.VerifyNoOtherCalls();
        }

        [TestCase("2.5.29.31")]
        [TestCase("1.3.6.1.5.5.7.1.1")]
        public async Task IsTrustedAsyncRequiresUnknownRevocationRejectionWhenEitherLocationExtensionExistsAsync(
            string extensionOid)
        {
            using Certificate authority = CreateAuthority("CN=Identity authority with revocation");
            using Certificate identity = CreateIdentityCertificate("CN=Identity", extensionOid);
            var manager = CreateManager(static () => new TrustListData(), valid: true,
                out ValidationCapture capture);
            var logger = new RecordingLogger<OnboardingIdentityTrustProvider>();
            var provider = new OnboardingIdentityTrustProvider(manager.Object, logger);
            using var issuers = new CertificateCollection();

            bool trusted = await provider.IsTrustedAsync(
                identity, issuers, DeviceTicket(authority)).ConfigureAwait(false);

            Assert.That(trusted, Is.True);
            Assert.That(capture.Options!.RejectUnknownRevocationStatus, Is.True);
            Assert.That(logger.Messages, Is.Empty);
        }

        [Test]
        public async Task IsTrustedAsyncLogsAndAllowsUnknownStatusOnlyWhenNoRevocationLocationExistsAsync()
        {
            using Certificate authority = CreateAuthority("CN=Identity authority without revocation");
            using Certificate identity = CreateIdentityCertificate("CN=Identity without revocation");
            var manager = CreateManager(static () => new TrustListData(), valid: true,
                out ValidationCapture capture);
            var logger = new RecordingLogger<OnboardingIdentityTrustProvider>();
            var provider = new OnboardingIdentityTrustProvider(manager.Object, logger);
            using var issuers = new CertificateCollection();

            bool trusted = await provider.IsTrustedAsync(
                identity, issuers, DeviceTicket(authority)).ConfigureAwait(false);

            Assert.That(trusted, Is.True);
            Assert.That(capture.Options!.RejectUnknownRevocationStatus, Is.False);
            Assert.That(logger.Messages, Has.Count.EqualTo(1));
            Assert.That(logger.Messages[0], Does.Contain("no CRL distribution point or authority information access"));
            Assert.That(logger.Messages[0], Does.Not.Contain(identity.Subject));
            Assert.That(logger.Messages[0], Does.Not.Contain(authority.Subject));
        }

        [Test]
        public async Task IsTrustedAsyncReturnsFalseWhenScopedManagerValidationFailsAsync()
        {
            using Certificate authority = CreateAuthority("CN=Rejected identity authority");
            using Certificate identity = CreateIdentityCertificate("CN=Rejected identity");
            var manager = CreateManager(static () => new TrustListData(), valid: false,
                out ValidationCapture capture);
            var provider = new OnboardingIdentityTrustProvider(
                manager.Object, new RecordingLogger<OnboardingIdentityTrustProvider>());
            using var issuers = new CertificateCollection();

            bool trusted = await provider.IsTrustedAsync(
                identity, issuers, DeviceTicket(authority)).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            Assert.That(capture.Options!.TrustScope!.TrustedCertificates[0].ToArray(),
                Is.EqualTo(authority.RawData));
        }

        [Test]
        public async Task IsTrustedAsyncDisposesOwnedSnapshotAndPropagatesValidationFailure()
        {
            using Certificate authority = CreateAuthority("CN=Snapshot failure authority");
            using Certificate identity = CreateIdentityCertificate("CN=Snapshot failure identity");
            using TrustListData snapshot = TrustList([authority], [authority], [], []);
            var manager = CreateManager(() => snapshot, valid: true, out _);
            manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                    It.IsAny<ValidationOptions>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Certificate engine unavailable."));
            var provider = new OnboardingIdentityTrustProvider(
                manager.Object, new RecordingLogger<OnboardingIdentityTrustProvider>());
            using var issuers = new CertificateCollection { authority };

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.IsTrustedAsync(identity, issuers, DeviceTicket(authority)).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("Certificate engine unavailable."));
            Assert.That(() => snapshot.TrustedCertificates[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => snapshot.IssuerCertificates[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(issuers[0].RawData, Is.EqualTo(authority.RawData));
        }

        [Test]
        public async Task IsTrustedAsyncRejectsTicketWithNoAuthorityWithoutReadingManagedTrustListAsync()
        {
            using Certificate identity = CreateIdentityCertificate("CN=Identity without ticket authority");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingIdentityTrustProvider(
                manager.Object, new RecordingLogger<OnboardingIdentityTrustProvider>());
            using var issuers = new CertificateCollection();
            var ticket = new DeviceIdentityTicketType { Authorities = [] };

            bool trusted = await provider.IsTrustedAsync(identity, issuers, ticket).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public async Task IsTrustedAsyncRejectsMalformedTicketAuthorityBeforeManagerCallsAsync()
        {
            using Certificate identity = CreateIdentityCertificate("CN=Identity with malformed authority");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingIdentityTrustProvider(
                manager.Object, new RecordingLogger<OnboardingIdentityTrustProvider>());
            using var issuers = new CertificateCollection();
            var ticket = new DeviceIdentityTicketType
            {
                Authorities =
                [
                    new CertificateAuthorityType
                    {
                        AuthorityCertificate = ByteString.From(new byte[] { 0x30, 0x00 }.AsSpan())
                    }
                ]
            };

            bool trusted = await provider.IsTrustedAsync(identity, issuers, ticket).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ConstructorAndIsTrustedAsyncRejectNullArguments()
        {
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var logger = new RecordingLogger<OnboardingIdentityTrustProvider>();
            Assert.That(
                () => new OnboardingIdentityTrustProvider(null!, logger),
                Throws.ArgumentNullException);
            Assert.That(
                () => new OnboardingIdentityTrustProvider(manager.Object, null!),
                Throws.ArgumentNullException);

            var provider = new OnboardingIdentityTrustProvider(manager.Object, logger);
            using Certificate identity = CreateIdentityCertificate("CN=Null argument identity");
            using var issuers = new CertificateCollection();
            var ticket = new DeviceIdentityTicketType();
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.IsTrustedAsync(null!, issuers, ticket).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.IsTrustedAsync(identity, null!, ticket).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.IsTrustedAsync(identity, issuers, null!).ConfigureAwait(false));
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public async Task IsTrustedAsyncForwardsCancellationWithoutTrustListOrValidationCalls()
        {
            using Certificate authority = CreateAuthority("CN=Cancelled identity authority");
            using Certificate identity = CreateIdentityCertificate("CN=Cancelled identity");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingIdentityTrustProvider(
                manager.Object, new RecordingLogger<OnboardingIdentityTrustProvider>());
            using var issuers = new CertificateCollection();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.IsTrustedAsync(
                    identity, issuers, DeviceTicket(authority), cancellation.Token).ConfigureAwait(false));
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public void DeviceIdentityAuthoritiesUsesDedicatedManagedTrustListName()
        {
            Assert.That(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities.Name,
                Is.EqualTo("DeviceIdentityAuthorities"));
            Assert.That(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                Is.Not.EqualTo(TrustListIdentifier.Peers));
        }

        private static Mock<ICertificateManager> CreateManager(
            Func<TrustListData> trustListFactory,
            bool valid,
            out ValidationCapture capture)
        {
            var state = new ValidationCapture();
            capture = state;
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(m => m.ReadTrustListAsync(
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                    TrustListMasks.All,
                    It.IsAny<CancellationToken>()))
                .Callback<TrustListIdentifier, TrustListMasks, CancellationToken>(
                    (trustList, masks, token) =>
                    {
                        state.ReadTrustList = trustList;
                        state.ReadMasks = masks;
                        state.ReadToken = token;
                    })
                .Returns((TrustListIdentifier _, TrustListMasks _, CancellationToken _) =>
                    Task.FromResult(trustListFactory()));
            manager.Setup(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(),
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                    It.IsAny<ValidationOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier, ValidationOptions,
                    CancellationToken>((chain, _, options, token) =>
                    {
                        state.ValidatedIdentityRawData = chain[0].RawData.AsSpan().ToArray();
                        state.Options = options;
                        state.ValidatedToken = token;
                    })
                .Returns(Task.FromResult(new CertificateValidationResult(
                    valid,
                    valid ? StatusCodes.Good : StatusCodes.BadCertificateUntrusted,
                    Array.Empty<ServiceResult>(),
                    false)));
            return manager;
        }

        private static TrustListData TrustList(
            Certificate[] trusted,
            Certificate[] issuers,
            byte[][] trustedCrls,
            byte[][] issuerCrls)
        {
            var data = new TrustListData
            {
                TrustedCertificates = Copies(trusted),
                IssuerCertificates = Copies(issuers)
            };
            foreach (byte[] crl in trustedCrls)
            {
                data.TrustedCrls.Add(new X509CRL(crl));
            }
            foreach (byte[] crl in issuerCrls)
            {
                data.IssuerCrls.Add(new X509CRL(crl));
            }
            return data;
        }

        private static CertificateCollection Copies(Certificate[] certificates)
        {
            var collection = new CertificateCollection();
            foreach (Certificate certificate in certificates)
            {
                using Certificate copy = Certificate.FromRawData(certificate.RawData);
                collection.Add(copy);
            }
            return collection;
        }

        private static DeviceIdentityTicketType DeviceTicket(
            Certificate authority,
            Certificate? issuer = null)
        {
            var ticket = new DeviceIdentityTicketType
            {
                Authorities =
                [
                    new CertificateAuthorityType
                    {
                        AuthorityCertificate = new ByteString((ReadOnlyMemory<byte>)authority.RawData),
                        IssuerCertificates = issuer == null
                            ? []
                            : [new ByteString((ReadOnlyMemory<byte>)issuer.RawData)]
                    }
                ]
            };
            return ticket;
        }

        private static bool ContainsDer(ArrayOf<ByteString> certificates, byte[] rawData)
        {
            foreach (ByteString certificate in certificates)
            {
                if (certificate.Span.SequenceEqual(rawData))
                {
                    return true;
                }
            }
            return false;
        }

        private static Certificate CreateAuthority(string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter)
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateIdentityCertificate(string subject, string? extensionOid = null)
        {
            var builder = CertificateBuilder.Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(s_notAfter);
            if (extensionOid != null)
            {
                builder.AddExtension(new X509Extension(new Oid(extensionOid), [0x30, 0x00], false));
            }
            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        private static IX509CRL CreateCrl(Certificate issuer, Certificate revoked)
        {
            return CrlBuilder.Create(issuer.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddMinutes(-1))
                .SetNextUpdate(DateTime.UtcNow.AddDays(1))
                .AddRevokedCertificate(revoked)
                .CreateForRSA(issuer);
        }

        private static readonly DateTime s_notBefore =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_notAfter =
            new(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class ValidationCapture
        {
            public TrustListIdentifier? ReadTrustList { get; set; }
            public TrustListMasks ReadMasks { get; set; }
            public CancellationToken ReadToken { get; set; }
            public byte[]? ValidatedIdentityRawData { get; set; }
            public CancellationToken ValidatedToken { get; set; }
            public ValidationOptions? Options { get; set; }
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
