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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingTicketTrustProviderTests
    {
        [Test]
        public async Task IsTrustedAsyncDeviceRequiresExactSignerCertificateInTicketAuthorities()
        {
            using Certificate signer = CreateRsaCertificate("CN=Ticket signer");
            using Certificate root = CreateRsaCertificate("CN=Ticket CA");
            using CertificateCollection chain = Certificates(signer, root);
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.SetupProperty(
                m => m.AcceptError,
                static (Certificate _, ServiceResult _) => true);
            manager.Setup(m => m.ReadTrustListAsync(
                    OnboardingTicketTrustProvider.TicketAuthorities,
                    TrustListMasks.TrustedCertificates,
                    It.IsAny<CancellationToken>()))
                .Returns((TrustListIdentifier _, TrustListMasks _, CancellationToken _) =>
                    Task.FromResult(TrustList(root)));
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            bool trusted = await provider.IsTrustedAsync(
                chain, OnboardingTicketSource.Device).ConfigureAwait(false);

            Assert.That(trusted, Is.False, "A trusted root does not mean the presented signer is explicitly trusted.");
            manager.Verify(m => m.ReadTrustListAsync(
                OnboardingTicketTrustProvider.TicketAuthorities,
                TrustListMasks.TrustedCertificates,
                It.IsAny<CancellationToken>()), Times.Once);
            manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                It.IsAny<TrustListIdentifier>(),
                It.IsAny<ValidationOptions>(),
                It.IsAny<CancellationToken>()), Times.Never);
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public async Task IsTrustedAsyncDeviceValidatesWhenSignerRawDerIsExplicitlyTrusted()
        {
            using Certificate signer = CreateRsaCertificate("CN=Explicit ticket signer");
            using CertificateCollection chain = Certificates(signer);
            var manager = ManagerWithTrustList(() => TrustList(signer), valid: true);
            var provider = new OnboardingTicketTrustProvider(manager.Object);
            using var cancellation = new CancellationTokenSource();

            bool trusted = await provider.IsTrustedAsync(
                chain, OnboardingTicketSource.Device, cancellation.Token).ConfigureAwait(false);

            Assert.That(trusted, Is.True);
            VerifyTicketValidationOptions(manager, cancellation.Token);
            manager.Verify(m => m.ReadTrustListAsync(
                OnboardingTicketTrustProvider.TicketAuthorities,
                TrustListMasks.TrustedCertificates,
                cancellation.Token), Times.Once);
        }

        [Test]
        public async Task IsTrustedAsyncOutOfBandAllowsCaTrustWithoutExplicitLeafRead()
        {
            using Certificate signer = CreateRsaCertificate("CN=Out-of-band signer");
            using CertificateCollection chain = Certificates(signer);
            var manager = ManagerWithTrustList(static () => throw new AssertionException(
                "Out-of-band validation must not read an explicit-leaf trust list."), valid: true);
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            bool trusted = await provider.IsTrustedAsync(
                chain, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(trusted, Is.True);
            manager.Verify(m => m.ReadTrustListAsync(
                It.IsAny<TrustListIdentifier>(),
                It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
            manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                OnboardingTicketTrustProvider.TicketAuthorities,
                It.IsAny<ValidationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(manager.Invocations
                .Single(call => call.Method.Name == nameof(ICertificateManager.ValidateAsync)).Arguments[0],
                Is.SameAs(chain));
        }

        [Test]
        public async Task IsTrustedAsyncReturnsManagerValidationResult()
        {
            using Certificate signer = CreateRsaCertificate("CN=Invalid signer");
            using CertificateCollection chain = Certificates(signer);
            var manager = ManagerWithTrustList(static () => new TrustListData(), valid: false);
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            bool trusted = await provider.IsTrustedAsync(
                chain, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                OnboardingTicketTrustProvider.TicketAuthorities,
                It.IsAny<ValidationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(manager.Invocations
                .Single(call => call.Method.Name == nameof(ICertificateManager.ValidateAsync)).Arguments[0],
                Is.SameAs(chain));
        }

        [Test]
        public async Task IsTrustedAsyncRejectsEmptyChainWithoutCallingManager()
        {
            using var certificates = new CertificateCollection();
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            bool trusted = await provider.IsTrustedAsync(
                certificates, OnboardingTicketSource.OutOfBand).ConfigureAwait(false);

            Assert.That(trusted, Is.False);
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ConstructorAndIsTrustedAsyncRejectNullAndInvalidSource()
        {
            Assert.That(
                () => new OnboardingTicketTrustProvider(null!),
                Throws.ArgumentNullException);
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            await Assert.ThatAsync(
                async () => await provider.IsTrustedAsync(null!, OnboardingTicketSource.Device).ConfigureAwait(false),
                Throws.ArgumentNullException);
            using var certificates = new CertificateCollection();
            await Assert.ThatAsync(
                async () => await provider.IsTrustedAsync(
                    certificates, (OnboardingTicketSource)42).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task IsTrustedAsyncHonorsInitialCancellationWithoutManagerCalls()
        {
            using Certificate signer = CreateRsaCertificate("CN=Cancelled signer");
            using CertificateCollection chain = Certificates(signer);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var provider = new OnboardingTicketTrustProvider(manager.Object);

            await Assert.ThatAsync(
                async () => await provider.IsTrustedAsync(
                    chain, OnboardingTicketSource.Device, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            manager.VerifyNoOtherCalls();
        }

        [Test]
        public void TrustListIdentifierIsDedicatedToTicketAuthorities()
        {
            Assert.That(
                OnboardingTicketTrustProvider.TicketAuthorities.Name,
                Is.EqualTo("TicketAuthorities"));
            Assert.That(
                OnboardingTicketTrustProvider.TicketAuthorities,
                Is.Not.EqualTo(TrustListIdentifier.Peers));
        }

        private static Mock<ICertificateManager> ManagerWithTrustList(
            Func<TrustListData> trustListFactory,
            bool valid)
        {
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.SetupProperty(
                m => m.AcceptError,
                static (Certificate _, ServiceResult _) => true);
            manager.Setup(m => m.ReadTrustListAsync(
                    It.IsAny<TrustListIdentifier>(),
                    It.IsAny<TrustListMasks>(),
                    It.IsAny<CancellationToken>()))
                .Returns((TrustListIdentifier _, TrustListMasks _, CancellationToken _) =>
                    Task.FromResult(trustListFactory()));
            manager.Setup(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<TrustListIdentifier>(),
                    It.IsAny<ValidationOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new CertificateValidationResult(
                    valid, valid ? StatusCodes.Good : StatusCodes.BadCertificateUntrusted,
                    Array.Empty<ServiceResult>(), false)));
            return manager;
        }

        private static void VerifyTicketValidationOptions(
            Mock<ICertificateManager> manager,
            CancellationToken expectedToken)
        {
            manager.Verify(m => m.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                OnboardingTicketTrustProvider.TicketAuthorities,
                It.Is<ValidationOptions>(options =>
                    options.ValidateForSigning &&
                    options.AutoAcceptUntrustedCertificates == false &&
                    options.RejectSHA1SignedCertificates == true &&
                    options.RejectUnknownRevocationStatus == true &&
                    options.MinimumCertificateKeySize == 2048 &&
                    options.AcceptError != null &&
                    !options.AcceptError(null!, null!)),
                expectedToken), Times.Once);
        }

        private static TrustListData TrustList(params Certificate[] certificates)
        {
            return new TrustListData { TrustedCertificates = Certificates(certificates) };
        }

        private static CertificateCollection Certificates(params Certificate[] certificates)
        {
            var collection = new CertificateCollection();
            foreach (Certificate certificate in certificates)
            {
                using Certificate copy = Certificate.FromRawData(certificate.RawData);
                collection.Add(copy);
            }
            return collection;
        }

        private static Certificate CreateRsaCertificate(string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }
    }
}
