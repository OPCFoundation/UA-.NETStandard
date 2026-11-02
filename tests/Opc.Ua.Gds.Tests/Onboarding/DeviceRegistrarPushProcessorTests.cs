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
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises concrete push orchestration over real generation-bound sessions and scripted generated calls.
    /// Tests intentionally do not open sockets or run software installers or new certificate authorities.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarPushProcessorTests
    {
        /// <summary>
        /// Creates independent bootstrap and issued public credentials.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            m_bootstrap = CertificateBuilder.Create("CN=Bootstrap Push Device").SetRSAKeySize(2048).CreateForRSA();
            m_issued = CertificateBuilder.Create("CN=Issued Push Device").SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Releases fixture-owned certificate handles.
        /// </summary>
        [OneTimeTearDown]
        public void TearDown()
        {
            m_bootstrap.Dispose();
            m_issued.Dispose();
        }

        /// <summary>
        /// Every mandatory collaborator remains explicit for direct construction as well as fluent hosting.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void ConstructorRejectsEachMissingProvider(int missing)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            Assert.Throws<ArgumentNullException>(() => new DeviceRegistrarPushProcessor(
                missing == 0 ? null! : new Mock<IDeviceRegistrarPushConnectionProvider>().Object,
                missing == 1 ? null! : new Mock<IDeviceRegistrarPushEnrollmentProvider>().Object,
                missing == 2 ? null! : new OnboardingPushIdentityValidator(
                    new Mock<IOnboardingIdentitySelector>().Object, telemetry),
                missing == 3 ? null! : new Mock<IDeviceRegistrarOnboardingPolicy>().Object,
                missing == 4 ? null! : new Mock<IApplicationsDatabase>().Object,
                missing == 5 ? null! : lifecycle,
                missing == 6 ? null! : lifecycle,
                missing == 7 ? null! : new Mock<ICertificateRequestIssuanceStore>().Object,
                missing == 8 ? null! : telemetry));
        }

        /// <summary>
        /// A canceled registration is not accepted or dispatched to a provider.
        /// </summary>
        [Test]
        public async Task CancellationBeforeProcessingDoesNotOpenAnyConnection()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application, cancellation.Token).ConfigureAwait(false));

            Assert.That(harness.Initial.Requests, Is.Empty);
            Assert.That(harness.Proof.Requests, Is.Empty);
            Assert.That(harness.LeasesDisposed, Is.Zero);
            harness.Identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Generated ticket, CSR, trust, certificate and apply calls precede actual installed-channel promotion.
        /// </summary>
        [Test]
        public async Task PushUsesGeneratedOperationsAndPromotesOnlyTheFreshActualPeer()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeProof = async () =>
            {
                DeviceRegistrarDevice? pending = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                    .ConfigureAwait(false);
                Assert.That(pending!.DcaCertificate.IsEmpty, Is.True);
                Assert.That(pending.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            };

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaApplicationId, Is.EqualTo(Harness.ApplicationId));
            Assert.That(device.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.PendingDcaCertificate.IsEmpty, Is.True);
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.CallMethods, Does.Contain(new NodeId(Ua.MethodIds.FileType_Open)));
            Assert.That(harness.CallMethods, Does.Contain(new NodeId(Ua.MethodIds.FileType_Write)));
            Assert.That(harness.CallMethods, Does.Contain(new NodeId(Ua.MethodIds.TrustListType_CloseAndUpdate)));
            Assert.That(harness.CallMethods,
                Does.Contain(new NodeId(Ua.MethodIds.ServerConfigurationType_UpdateCertificate)));
            Assert.That(harness.CallMethods[harness.CallMethods.Count - 2],
                Is.EqualTo(new NodeId(Ua.MethodIds.ServerConfigurationType_ApplyChanges)));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(2));
            Assert.That(harness.Initial.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            Assert.That(harness.Proof.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            harness.Identities.Verify(selector => selector.SelectAsync(
                It.Is<ArrayOf<ByteString>>(values => values.Count == 1 && values[0] == harness.Bootstrap),
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A discovery certificate does not compensate for an incorrect authenticated peer after reconnect.
        /// </summary>
        [Test]
        public async Task WrongActualInstalledPeerLeavesCredentialPending()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.Proof.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_bootstrap.RawData);
            harness.Proof.Channel.Object.EndpointDescription.ServerCertificate = harness.Issued;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(device.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(2));
        }

        /// <summary>
        /// Software requirements are persisted and reject issuance rather than call an accepting update stub.
        /// </summary>
        [Test]
        public async Task RequiredSoftwareApprovalStopsBeforeCsrAndCredentialMutation()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { RequiresSoftware = true };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(1));
            harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<NodeId>(),
                It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            DeviceRegistrarIdentityCandidate? candidate = await harness.Lifecycle.FindCandidateByCertificateAsync(
                harness.Bootstrap).ConfigureAwait(false);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.False);
        }

        /// <summary>
        /// A real software-manager approval survives candidate refresh and allows the complete credential path.
        /// </summary>
        [Test]
        public async Task PersistedSoftwareApprovalAllowsPushWithoutInstallingSoftware()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { RequiresSoftware = true };
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.UpdateSoftwareStatusAsync(Harness.Product, true, "approved-revision")
                .ConfigureAwait(false);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.SoftwareStatus, Is.True);
            Assert.That(device.SoftwareRevision, Is.EqualTo("approved-revision"));
            Assert.That(device.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Existing managed relations cannot be retained as singleton DCA privileges.
        /// </summary>
        [Test]
        public async Task SingletonRejectsManagedApplicationPrivilegeBeforeIssuance()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { Singleton = true };
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.AssociateApplicationAsync(
                Harness.Product, new NodeId("managed-application", 1), string.Empty).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(1));
            Assert.That((await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false))!.IsSingletonRestricted, Is.True);
        }

        /// <summary>
        /// Singleton discovery remains restricted after installed-peer proof and later topology changes.
        /// </summary>
        [Test]
        public async Task SingletonPushPersistsRestrictionAfterInstalledPeerProofAndCompositeRediscovery()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { Singleton = true };
            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.IsSingletonRestricted, Is.True);

            harness.Singleton = false;
            harness.Initial.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);
            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Lifecycle.AssociateApplicationAsync(
                    Harness.Product, new NodeId("later-managed", 1), "").ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That((await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false))!.IsSingletonRestricted, Is.True);
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Older lifecycle providers cannot accept a singleton without retaining its credential restriction.
        /// </summary>
        [Test]
        public async Task SingletonPushRejectsLifecycleWithoutPersistedScopeCapability()
        {
            await using var harness = new Harness(m_bootstrap, m_issued, scopeCapability: false) { Singleton = true };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
            Assert.That((await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false))!.PendingDcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// A replaced generation cannot receive a CSR even when the enrollment provider resumes later.
        /// </summary>
        [Test]
        public async Task GenerationReplacementDuringEnrollmentCannotDispatchCsr()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeCsr = harness.Initial.Recreate;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
        }

        /// <summary>
        /// Mutating transport certificate metadata without replacing the session cannot transfer admission.
        /// </summary>
        [Test]
        public async Task ActualPeerReplacementDuringEnrollmentCannotDispatchCsr()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeCsr = () => harness.Initial.Channel
                .SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
        }

        /// <summary>
        /// An erroneous provider cannot generate a second key using a retained callback.
        /// </summary>
        [Test]
        public async Task EnrollmentCannotInvokeCsrFactoryTwice()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { RepeatCsr = true };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.CallMethods.Count, Is.EqualTo(2));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// Missing correlated issuance never creates credentials or records a fake pending installation.
        /// </summary>
        [Test]
        public async Task MissingIssuanceReceiptFailsWithoutInstallingOrSynthesizingCertificate()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { MissingReceipt = true };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.CallMethods.Count, Is.EqualTo(2));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// A retained receipt cannot be applied under substituted identity, application, profile, or request evidence.
        /// </summary>
        [TestCase("identity")]
        [TestCase("application")]
        [TestCase("request")]
        [TestCase("group")]
        [TestCase("type")]
        [TestCase("partial-trust")]
        [TestCase("ticket")]
        [TestCase("private-key")]
        public async Task InvalidEnrollmentEvidenceFailsBeforeCredentialMutation(string substitution)
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { EnrollmentMutation = substitution };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(harness.CallMethods.Count, Is.EqualTo(2));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate.IsEmpty, Is.True);
            Assert.That(device.DcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// Failed trust transfer and failed handle cleanup both propagate while issuance remains pending.
        /// </summary>
        [Test]
        public async Task TrustTransferAndRemoteHandleCleanupFailuresAreBothPreserved()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var transfer = new ServiceResultException(StatusCodes.BadTimeout);
            var cleanup = new ServiceResultException(StatusCodes.BadUnexpectedError);
            harness.TrustWriteError = transfer;
            harness.TrustCloseError = cleanup;

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Is.EqualTo(new Exception[] { transfer, cleanup }));
            Assert.That(harness.CallMethods[harness.CallMethods.Count - 1],
                Is.EqualTo(new NodeId(Ua.MethodIds.FileType_Close)));
            Assert.That(harness.CallMethods,
                Does.Not.Contain(new NodeId(Ua.MethodIds.ServerConfigurationType_UpdateCertificate)));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
        }

        /// <summary>
        /// An uncertain apply result retains pending issuance and never triggers blind reconnect or promotion.
        /// </summary>
        [Test]
        public async Task ApplyFailureRetainsPendingReceiptForExplicitRecovery()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            harness.ApplyError = failure;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.DcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// Installed pending credential recovery uses retained identity and receipt, not a second CSR or ticket guess.
        /// </summary>
        [Test]
        public async Task PendingInstalledCredentialResumesWithoutRepeatingCsrOrTrustMutation()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.RecordIssuedDcaCertificateAsync(
                Harness.Product, Harness.ApplicationId, harness.Issued).ConfigureAwait(false);
            harness.Initial.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.PendingDcaCertificate.IsEmpty, Is.True);
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods.Count, Is.EqualTo(2));
            harness.Identities.VerifyNoOtherCalls();
            harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<NodeId>(),
                It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Persisted pending credentials do not allow reconstructing or guessing lost request ownership.
        /// </summary>
        [Test]
        public async Task InstalledCredentialWithoutRetainedRequestEvidenceFailsClosed()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { MissingRetained = true };
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.RecordIssuedDcaCertificateAsync(
                Harness.Product, Harness.ApplicationId, harness.Issued).ConfigureAwait(false);
            harness.Initial.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.CsrCalls, Is.Zero);
            harness.Identities.VerifyNoOtherCalls();
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(device.PendingDcaCertificate, Is.EqualTo(harness.Issued));
        }

        /// <summary>
        /// Software approval is rechecked after certificate application and before installed-peer promotion.
        /// </summary>
        [Test]
        public async Task SoftwareRevocationBeforeProofPreventsPromotion()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeProof = async () =>
            {
                await harness.Lifecycle.UpdateSoftwareStatusAsync(Harness.Product, false, "revoked")
                    .ConfigureAwait(false);
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(device.PendingDcaCertificate, Is.EqualTo(harness.Issued));
        }

        /// <summary>
        /// Primary processing and provider lease cleanup failures are both observable.
        /// </summary>
        [Test]
        public async Task ProcessingAndLeaseDisposalFailuresAreBothPreserved()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            var cleanup = new InvalidOperationException("lease cleanup failed");
            harness.ApplyError = failure;
            harness.CleanupError = cleanup;

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Is.EqualTo(new Exception[] { failure, cleanup }));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid offered endpoint security or identity is rejected before reading tickets.
        /// </summary>
        [TestCase("application")]
        [TestCase("security")]
        [TestCase("url")]
        public async Task InvalidOfferedEndpointCannotAuthorizeDeviceOperations(string mutation)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            EndpointDescription offered = harness.InitialLease.Object.OfferedEndpoint;
            switch (mutation)
            {
                case "application":
                    offered.Server.ApplicationUri = "urn:other-application";
                    break;
                case "security":
                    offered.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "url":
                    offered.EndpointUrl = "not an endpoint URL";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(harness.Initial.Requests, Is.Empty);
            harness.Identities.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Fluent registration requires explicit missing boundaries instead of providing no-op implementations.
        /// </summary>
        [Test]
        public void FluentRegistrationRequiresExplicitProvidersAndSharesTheHostedQueue()
        {
            var services = new ServiceCollection();
            Assert.Throws<InvalidOperationException>(() => services.AddDeviceRegistrarPush());
            services.AddSingleton(new Mock<IDeviceRegistrarPushConnectionProvider>().Object);
            services.AddSingleton(new Mock<IDeviceRegistrarPushEnrollmentProvider>().Object);
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            services.AddSingleton<IDeviceRegistrarCertificateLifecycle>(lifecycle);
            services.AddSingleton(new Mock<ICertificateRequestIssuanceStore>().Object);
            services.AddSingleton(new Mock<IDeviceRegistrarIdentityLifecycle>().Object);
            services.AddSingleton(new Mock<IDeviceRegistrarOnboardingPolicy>().Object);
            services.AddSingleton(new Mock<IApplicationsDatabase>().Object);
            services.AddSingleton(new Mock<IOnboardingIdentitySelector>().Object);
            services.AddSingleton(NUnitTelemetryContext.Create());

            Assert.That(services.AddDeviceRegistrarPush(3), Is.SameAs(services));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarPushProcessor>(),
                Is.TypeOf<DeviceRegistrarPushProcessor>());
            DeviceRegistrarPushScheduler scheduler = provider.GetRequiredService<DeviceRegistrarPushScheduler>();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarPushScheduler>(), Is.SameAs(scheduler));
            Assert.That(provider.GetRequiredService<IHostedService>(), Is.SameAs(scheduler));
            Assert.Throws<InvalidOperationException>(() => services.AddDeviceRegistrarPush());
        }

        /// <summary>
        /// Owns deterministic sessions and real lifecycle state while mocking only host and issuance boundaries.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates one bootstrap generation, one installed-proof generation, and a correlated issued receipt.
            /// </summary>
            public Harness(Certificate bootstrap, Certificate issued, bool scopeCapability = true)
            {
                Bootstrap = ByteString.From(bootstrap.RawData.AsSpan());
                Issued = ByteString.From(issued.RawData.AsSpan());
                Identity = new OnboardingIdentitySelection(
                    Bootstrap, new DeviceIdentityTicketType(), Product, "signed-ticket", string.Empty);
                Application = new ApplicationDescription
                {
                    ApplicationUri = ApplicationUri,
                    ApplicationName = new LocalizedText("Push DCA"),
                    ApplicationType = ApplicationType.Server,
                    ProductUri = "urn:push-product",
                    DiscoveryUrls = ["opc.tcp://localhost:4840"]
                };
                Initial = CreateSession(bootstrap);
                Proof = CreateSession(issued);
                InitialLease = Lease(Initial);
                Mock<IDeviceRegistrarPushConnection> proofLease = Lease(Proof);
                var connections = new Mock<IDeviceRegistrarPushConnectionProvider>(MockBehavior.Strict);
                connections.Setup(provider => provider.ConnectAsync(
                    It.IsAny<ApplicationDescription>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns<ApplicationDescription, ByteString, CancellationToken>(async (_, expected, _) =>
                    {
                        if (expected.IsEmpty)
                        {
                            return InitialLease.Object;
                        }
                        Assert.That(expected, Is.EqualTo(Issued));
                        if (BeforeProof != null)
                        {
                            await BeforeProof().ConfigureAwait(false);
                        }
                        return proofLease.Object;
                    });
                var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
                database.Setup(value => value.FindApplications(ApplicationUri))
                    .Returns(() => m_record == null ? [] : [m_record]);
                database.Setup(value => value.RegisterApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Callback<ApplicationRecordDataType>(record =>
                    {
                        m_record = CoreUtils.Clone(record)!;
                        m_record.ApplicationId = ApplicationId;
                    })
                    .Returns(ApplicationId);
                var policy = new Mock<IDeviceRegistrarOnboardingPolicy>(MockBehavior.Strict);
                policy.Setup(value => value.EvaluateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<DeviceRegistrarOnboardingDecision>(
                        new DeviceRegistrarOnboardingDecision(Record(), RequiresSoftware,
                            RequiresSoftware ? new ManagerDescription
                            {
                                PurposeUri = "urn:software-update",
                                EndpointUrls = ["opc.tcp://software-manager:4840"]
                            } : null)));
                Identities.Setup(selector => selector.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Identity);
                Enrollments.Setup(provider => provider.GetOrCreateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), ApplicationId,
                    It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()))
                    .Returns<OnboardingIdentitySelection, NodeId,
                        Func<CancellationToken, ValueTask<ByteString>>, CancellationToken>(
                        async (_, _, create, ct) =>
                        {
                            BeforeCsr?.Invoke();
                            ByteString csr = await create(ct).ConfigureAwait(false);
                            Assert.That(csr, Is.EqualTo(s_csr));
                            if (RepeatCsr)
                            {
                                await create(ct).ConfigureAwait(false);
                            }
                            return Enrollment();
                        });
                Enrollments.Setup(provider => provider.FindAsync(Product, ApplicationId,
                    It.IsAny<CancellationToken>())).Returns(() =>
                        new ValueTask<DeviceRegistrarPushEnrollment?>(MissingRetained ? null : Enrollment()));
                var issuances = new Mock<ICertificateRequestIssuanceStore>(MockBehavior.Strict);
                issuances.Setup(store => store.GetOrCreateAsync(
                    ApplicationId, RequestId,
                    It.IsAny<Func<CancellationToken, ValueTask<CertificateRequestIssuance>>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<NodeId, NodeId, Func<CancellationToken, ValueTask<CertificateRequestIssuance>>,
                        CancellationToken>((_, _, create, ct) =>
                        MissingReceipt ? create(ct) : new ValueTask<CertificateRequestIssuance>(
                            new CertificateRequestIssuance("DefaultApplicationGroup", "RsaSha256",
                                Issued, EnrollmentMutation == "private-key" ? s_csr : default, [Issued])));
                Processor = new DeviceRegistrarPushProcessor(connections.Object, Enrollments.Object,
                    new OnboardingPushIdentityValidator(Identities.Object, Initial.Telemetry),
                    policy.Object, database.Object, Lifecycle,
                    scopeCapability ? Lifecycle :
                        new Mock<IDeviceRegistrarCertificateLifecycle>(MockBehavior.Strict).Object,
                    issuances.Object, Initial.Telemetry);
            }

            /// <summary>
            /// Gets the concrete processor under test.
            /// </summary>
            public DeviceRegistrarPushProcessor Processor { get; }

            /// <summary>
            /// Gets the offered DCA application description.
            /// </summary>
            public ApplicationDescription Application { get; }

            /// <summary>
            /// Gets the original public identity.
            /// </summary>
            public ByteString Bootstrap { get; }

            /// <summary>
            /// Gets the request-correlated issued public credential.
            /// </summary>
            public ByteString Issued { get; }

            /// <summary>
            /// Gets the validated ticket identity retained by enrollment.
            /// </summary>
            public OnboardingIdentitySelection Identity { get; }

            /// <summary>
            /// Gets the actual in-memory lifecycle shared by candidate and certificate APIs.
            /// </summary>
            public MemoryDeviceRegistrarLifecycle Lifecycle { get; } = new();

            /// <summary>
            /// Gets the bootstrap transport fixture.
            /// </summary>
            public SessionRequestContextTestContext Initial { get; }

            /// <summary>
            /// Gets the independent installed-proof transport fixture.
            /// </summary>
            public SessionRequestContextTestContext Proof { get; }

            /// <summary>
            /// Gets the selected endpoint and model targets.
            /// </summary>
            public Mock<IDeviceRegistrarPushConnection> InitialLease { get; }

            /// <summary>
            /// Gets the strict ticket selector.
            /// </summary>
            public Mock<IOnboardingIdentitySelector> Identities { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the host's strict durable request boundary.
            /// </summary>
            public Mock<IDeviceRegistrarPushEnrollmentProvider> Enrollments { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets actual generated method dispatches in order.
            /// </summary>
            public List<NodeId> CallMethods { get; } = [];

            /// <summary>
            /// Gets or sets whether the real software approval gate is initially closed.
            /// </summary>
            public bool RequiresSoftware { get; set; }

            /// <summary>
            /// Gets or sets the device's actual IsSingleton value.
            /// </summary>
            public bool Singleton { get; set; }

            /// <summary>
            /// Gets or sets whether correlated issuance is absent.
            /// </summary>
            public bool MissingReceipt { get; set; }

            /// <summary>
            /// Gets or sets whether installed credential recovery has lost its request evidence.
            /// </summary>
            public bool MissingRetained { get; set; }

            /// <summary>
            /// Gets or sets whether the provider incorrectly invokes its CSR factory twice.
            /// </summary>
            public bool RepeatCsr { get; set; }

            /// <summary>
            /// Gets or sets one concrete enrollment-evidence substitution.
            /// </summary>
            public string EnrollmentMutation { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets an explicit ApplyChanges transport failure.
            /// </summary>
            public Exception? ApplyError { get; set; }

            /// <summary>
            /// Gets or sets a provider lease cleanup failure.
            /// </summary>
            public Exception? CleanupError { get; set; }

            /// <summary>
            /// Gets or sets an explicit generated trust-file Write failure.
            /// </summary>
            public Exception? TrustWriteError { get; set; }

            /// <summary>
            /// Gets or sets an explicit generated trust-file Close failure.
            /// </summary>
            public Exception? TrustCloseError { get; set; }

            /// <summary>
            /// Gets or sets a generation mutation immediately before CSR creation.
            /// </summary>
            public Action? BeforeCsr { get; set; }

            /// <summary>
            /// Gets or sets an assertion or lifecycle mutation immediately before installed-channel proof.
            /// </summary>
            public Func<ValueTask>? BeforeProof { get; set; }

            /// <summary>
            /// Gets the number of leases actually disposed.
            /// </summary>
            public int LeasesDisposed { get; private set; }

            /// <summary>
            /// Gets actual CSR operations rather than enrollment-provider calls.
            /// </summary>
            public int CsrCalls { get; private set; }

            /// <summary>
            /// Creates an existing shared candidate for recovery or singleton privilege tests.
            /// </summary>
            public async ValueTask SeedCandidateAsync()
            {
                m_record = Record();
                m_record.ApplicationId = ApplicationId;
                await Lifecycle.RecordCandidateAsync(Product, ApplicationId, Bootstrap, RequiresSoftware)
                    .ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Initial.DisposeAsync().ConfigureAwait(false);
                await Proof.DisposeAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Creates a real encrypted session over the scripted transport with the specified actual peer.
            /// </summary>
            private SessionRequestContextTestContext CreateSession(Certificate peer)
            {
                var session = new SessionRequestContextTestContext(securityMode: MessageSecurityMode.SignAndEncrypt);
                session.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(peer.RawData);
                session.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
                session.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
                return session;
            }

            /// <summary>
            /// Creates the explicit connection ownership boundary, independently of admission context disposal.
            /// </summary>
            private Mock<IDeviceRegistrarPushConnection> Lease(SessionRequestContextTestContext session)
            {
                var lease = new Mock<IDeviceRegistrarPushConnection>(MockBehavior.Strict);
                lease.SetupGet(value => value.Session).Returns(session.ManagedSession);
                lease.SetupGet(value => value.OfferedEndpoint)
                    .Returns(CoreUtils.Clone(session.Channel.Object.EndpointDescription)!);
                lease.SetupGet(value => value.ProvisionableDeviceId).Returns(s_deviceId);
                lease.SetupGet(value => value.IsSingletonId).Returns(new NodeId("singleton", 1));
                lease.SetupGet(value => value.ServerConfigurationId)
                    .Returns(new NodeId(Ua.ObjectIds.ServerConfiguration));
                lease.SetupGet(value => value.CertificateGroupId)
                    .Returns(new NodeId(Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup));
                lease.SetupGet(value => value.CertificateTypeId)
                    .Returns(new NodeId(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
                lease.SetupGet(value => value.TrustListId).Returns(new NodeId("trust-list", 1));
                lease.Setup(value => value.DisposeAsync()).Returns(() =>
                {
                    LeasesDisposed++;
                    if (CleanupError != null)
                    {
                        throw CleanupError;
                    }
                    return default;
                });
                return lease;
            }

            /// <summary>
            /// Answers generated method resolution and typed method calls without bypassing session dispatch.
            /// </summary>
            private IServiceResponse Respond(IServiceRequest request)
            {
                if (request is BrowseRequest)
                {
                    return new BrowseResponse { Results = [new BrowseResult { References = [] }] };
                }
                Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("original-token")));
                if (request is ReadRequest)
                {
                    return new ReadResponse { Results = [new DataValue { WrappedValue = Variant.From(Singleton) }] };
                }
                if (request is not CallRequest call)
                {
                    throw new InvalidOperationException("Unexpected push request.");
                }
                CallMethodRequest method = call.MethodsToCall[0];
                CallMethods.Add(method.MethodId);
                ArrayOf<Variant> outputs = [];
                if (method.ObjectId == s_deviceId)
                {
                    ArrayOf<string> tickets = ["signed-ticket"];
                    outputs = [Variant.From(tickets)];
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CreateSigningRequest)
                {
                    CsrCalls++;
                    outputs = [Variant.From(s_csr)];
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Open)
                {
                    outputs = [Variant.From(42u)];
                }
                else if (method.MethodId == Ua.MethodIds.TrustListType_CloseAndUpdate ||
                    method.MethodId == Ua.MethodIds.ServerConfigurationType_UpdateCertificate)
                {
                    outputs = [Variant.From(true)];
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_ApplyChanges && ApplyError != null)
                {
                    throw ApplyError;
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Write && TrustWriteError != null)
                {
                    throw TrustWriteError;
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Close && TrustCloseError != null)
                {
                    throw TrustCloseError;
                }
                return new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] };
            }

            /// <summary>
            /// Creates retained request evidence matching the shared immutable receipt.
            /// </summary>
            private DeviceRegistrarPushEnrollment Enrollment()
            {
                var enrollment = new DeviceRegistrarPushEnrollment(Identity, ApplicationId, RequestId,
                    "DefaultApplicationGroup", "RsaSha256",
                    new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All });
                return EnrollmentMutation switch
                {
                    "identity" => enrollment with
                    {
                        Identity = new OnboardingIdentitySelection(
                            Issued, new DeviceIdentityTicketType(), Product, "signed-ticket", string.Empty)
                    },
                    "ticket" => enrollment with
                    {
                        Identity = new OnboardingIdentitySelection(
                            Bootstrap, new DeviceIdentityTicketType(), Product, "substituted-ticket", string.Empty)
                    },
                    "application" => enrollment with { ApplicationId = new NodeId("other-application", 1) },
                    "request" => enrollment with { RequestId = NodeId.Null },
                    "group" => enrollment with { CertificateGroupId = "OtherGroup" },
                    "type" => enrollment with { CertificateTypeId = "OtherType" },
                    "partial-trust" => enrollment with
                    {
                        TrustList = new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.TrustedCertificates }
                    },
                    _ => enrollment
                };
            }

            /// <summary>
            /// Creates the host policy's valid shared DCA record.
            /// </summary>
            private static ApplicationRecordDataType Record()
            {
                return new ApplicationRecordDataType
                {
                    ApplicationUri = ApplicationUri,
                    ApplicationNames = [new LocalizedText("Push DCA")],
                    ApplicationType = ApplicationType.Server,
                    ProductUri = "urn:push-product",
                    DiscoveryUrls = ["opc.tcp://localhost:4840"],
                    ServerCapabilities = ["DA"]
                };
            }

            /// <summary>The product identity bound to the accepted ticket.</summary>
            public const string Product = "urn:push:device";

            /// <summary>The shared certificate-manager application identifier.</summary>
            public static readonly NodeId ApplicationId = new("push-application", 1);

            /// <summary>The retained request identifier.</summary>
            private static readonly NodeId RequestId = new("push-request", 1);

            /// <summary>The selected DCA application's URI.</summary>
            private const string ApplicationUri = "urn:push:application";

            /// <summary>The provisionable device object used by generated ticket calls.</summary>
            private static readonly NodeId s_deviceId = new("push-device", 1);

            /// <summary>The device-generated signing request returned over the admitted generation.</summary>
            private static readonly ByteString s_csr = ByteString.From(new byte[] { 1, 2, 3 });

            /// <summary>The shared application record created or seeded by this harness.</summary>
            private ApplicationRecordDataType? m_record;
        }

        /// <summary>The fixture-owned bootstrap identity.</summary>
        private Certificate m_bootstrap = null!;

        /// <summary>The fixture-owned issued identity.</summary>
        private Certificate m_issued = null!;
    }
}
