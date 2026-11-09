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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
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
            m_bootstrap = CertificateBuilder.Create("CN=Bootstrap Push Device")
                .AddExtension(new X509SubjectAltNameExtension("urn:push:application", ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
            m_issued = CertificateBuilder.Create("CN=Issued Push Device")
                .AddExtension(new X509SubjectAltNameExtension("urn:push:application", ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
            m_managedA = CertificateBuilder.Create("CN=Managed Client A")
                .AddExtension(new X509SubjectAltNameExtension(ManagedUriA, ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
            m_managedB = CertificateBuilder.Create("CN=Managed Client B")
                .AddExtension(new X509SubjectAltNameExtension(ManagedUriB, ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Releases fixture-owned certificate handles.
        /// </summary>
        [OneTimeTearDown]
        public void TearDown()
        {
            m_bootstrap.Dispose();
            m_issued.Dispose();
            m_managedA.Dispose();
            m_managedB.Dispose();
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
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
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
                missing == 8 ? null! : lifecycle,
                missing == 9 ? null! : new Mock<IDeviceRegistrarAuthorization>().Object,
                missing == 10 ? null! : telemetry,
                missing == 11 ? null! : TimeProvider.System));
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
            Assert.That(harness.CallMethods, Does.Contain(Ua.MethodIds.FileType_Open));
            Assert.That(harness.CallMethods, Does.Contain(Ua.MethodIds.FileType_Write));
            Assert.That(harness.CallMethods, Does.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(harness.CallMethods,
                Does.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(harness.CallMethods.LastIndexOf(Ua.MethodIds.ServerConfigurationType_ApplyChanges),
                Is.LessThan(harness.CallMethods.LastIndexOf(Ua.MethodIds.FileType_Read)));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
            Assert.That(harness.ApplyCalls, Is.EqualTo(2));
            Assert.That(harness.CallMethods.IndexOf(Ua.MethodIds.ServerConfigurationType_ApplyChanges),
                Is.LessThan(harness.CallMethods.IndexOf(
                    Ua.MethodIds.ServerConfigurationType_UpdateCertificate)));
            Assert.That(harness.Initial.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            Assert.That(harness.Proof.Requests.Exists(request => request is CloseSessionRequest), Is.False);
            harness.Identities.Verify(selector => selector.SelectAsync(
                It.Is<ArrayOf<ByteString>>(values => values.Count == 1 && values[0] == harness.Bootstrap),
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// External certificate evidence uses the ordinary retained issuance and managed-application pipeline.
        /// </summary>
        [Test]
        public async Task ExternalHandoffPushProvisionsWithoutInventingTickets()
        {
            await using var harness = new Harness(m_bootstrap, m_issued, externalAuthentication: true);
            harness.AddManaged("managed-a", ManagedUriA, m_managedA);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.Applications.Count, Is.EqualTo(1));
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.Identity.IsExternalAuthentication, Is.True);
            Assert.That(harness.Identity.EncodedTicket, Is.Empty);
            harness.Identities.VerifyNoOtherCalls();
            harness.ExternalAuthentication.Verify(value => value.EvaluateAsync(
                harness.Bootstrap, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        /// <summary>
        /// Losing external approval before CSR creation cannot fall back to a ticket or mutate the device.
        /// </summary>
        [Test]
        public async Task ExternalHandoffPushRechecksApprovalBeforeCsrEffects()
        {
            await using var harness = new Harness(m_bootstrap, m_issued, externalAuthentication: true);
            harness.BeforeCsr = () => harness.ExternalAuthentication
                .Setup(value => value.EvaluateAsync(harness.Bootstrap, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceRegistrarExternalAuthenticationResult?)null);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.ApplyCalls, Is.Zero);
            harness.Identities.VerifyNoOtherCalls();
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
            Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(1));
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
        /// A non-singleton device's managed Client applications are registered, authorized, associated and
        /// provisioned through their own ApplicationConfiguration after the DCA credential is proven.
        /// </summary>
        [Test]
        public async Task PushProvisionsEachManagedClientApplicationAfterDcaProof()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication first = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            first.InFolder = true;
            ManagedApplication second = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            second.OnDevice = false;
            second.InFolder = true;
            second.IncludeNames = false;
            ManagedApplication dca = harness.AddManaged(
                "managed-dca", harness.Application.ApplicationUri!, m_issued);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(first.Installed, Is.True);
            Assert.That(second.Installed, Is.True);
            Assert.That(first.CsrCalls, Is.EqualTo(1));
            Assert.That(second.CsrCalls, Is.EqualTo(1));
            Assert.That(dca.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA, ManagedUriB }));
            Assert.That(harness.RegisteredName(first), Is.EqualTo("managed-a"));
            Assert.That(harness.RegisteredName(second), Is.EqualTo(ManagedUriB));
            Assert.That(first.Calls.IndexOf(Ua.MethodIds.ServerConfigurationType_ApplyChanges),
                Is.LessThan(first.Calls.IndexOf(Ua.MethodIds.ServerConfigurationType_UpdateCertificate)));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            var associated = new List<NodeId>();
            foreach (DeviceRegistrarApplication application in device.Applications)
            {
                associated.Add(application.ApplicationId);
            }
            Assert.That(associated, Is.EquivalentTo(new[] { first.Id, second.Id }));
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.ApplyCalls, Is.EqualTo(2));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
        }

        /// <summary>
        /// A rejected managed application is reported without blocking the others or the confirmed DCA.
        /// </summary>
        [Test]
        public async Task DeniedManagedApplicationIsReportedWithoutBlockingOthers()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication denied = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication allowed = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            harness.DeniedUris.Add(ManagedUriA);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(denied.CsrCalls, Is.Zero);
            Assert.That(denied.Installed, Is.False);
            Assert.That(harness.RegisteredName(denied), Is.Null);
            Assert.That(allowed.Installed, Is.True);
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.Applications.Count, Is.EqualTo(1));
            Assert.That(device.Applications[0].ApplicationId, Is.EqualTo(allowed.Id));
        }

        /// <summary>
        /// A denied managed server resolves metadata for authorization but does not open its application channel.
        /// </summary>
        [Test]
        public async Task DeniedManagedServerIsRejectedBeforeChannelVerification()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication denied = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            harness.ConfigureManagedTarget(denied);
            harness.DeniedUris.Add(ManagedUriA);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
            Assert.That(denied.Calls, Is.Empty);
            Assert.That(harness.RegisteredName(denied), Is.Null);
            harness.ManagedApplications.Verify(
                provider => provider.GetTargetAsync(ManagedUriA, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A DNS restriction authorizes the resolved discovery URL before registry lookup or credential verification.
        /// </summary>
        [Test]
        public async Task DeniedManagedServerDnsRestrictionSeesResolvedMetadataBeforeRegistryLookup()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server) with
            {
                DiscoveryUrls = ["opc.tcp://blocked.example.test:4840"],
                ServerCapabilities = ["DA", "AC"]
            };
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));
            ApplicationRecordDataType? authorizedRecord = null;
            harness.Authorization.Setup(value => value.RequireApplicationAsync(
                Harness.Product,
                It.Is<ApplicationRecordDataType>(record => record.ApplicationUri == ManagedUriA),
                string.Empty,
                It.IsAny<CancellationToken>()))
                .Returns<string, ApplicationRecordDataType, string, CancellationToken>((_, record, _, _) =>
                {
                    authorizedRecord = record;
                    Uri discoveryUri = new(record.DiscoveryUrls[0], UriKind.Absolute);
                    if (discoveryUri.DnsSafeHost == "blocked.example.test")
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                    }
                    return default;
                });

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(authorizedRecord, Is.Not.Null);
            Assert.That(authorizedRecord!.DiscoveryUrls.ToArray(),
                Is.EqualTo(s_blockedDiscoveryUrls));
            Assert.That(authorizedRecord.ServerCapabilities.ToArray(), Is.EqualTo(s_authorizedCapabilities));
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
            Assert.That(server.Calls, Is.Empty);
            Assert.That(harness.RegisteredName(server), Is.Null);
            harness.Database.Verify(database => database.FindApplications(ManagedUriA), Times.Never);
            harness.ManagedApplicationChannels.Verify(channel => channel.VerifyAsync(
                It.IsAny<ApplicationDescription>(),
                It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()), Times.Never);
            harness.ManagedApplications.Verify(
                provider => provider.GetTargetAsync(ManagedUriA, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Failed managed-server channel proof does not register or associate the application.
        /// </summary>
        [Test]
        public async Task FailedManagedServerChannelProofPreventsRegistration()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            harness.ConfigureManagedTarget(server);
            harness.ManagedApplicationChannels.Setup(verifier => verifier.VerifyAsync(
                It.IsAny<ApplicationDescription>(),
                It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadCertificateInvalid));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
            Assert.That(harness.RegisteredName(server), Is.Null);
            Assert.That(server.Calls,
                Is.EqualTo(new[] { Ua.MethodIds.ServerConfigurationType_GetCertificates }));
        }

        /// <summary>
        /// A singleton device's DCA never registers or configures other applications.
        /// </summary>
        [Test]
        public async Task SingletonDeviceDoesNotProvisionManagedApplications()
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { Singleton = true };
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.Empty);
            Assert.That(harness.RegisteredName(application), Is.Null);
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.Applications.Count, Is.Zero);
        }

        /// <summary>
        /// A managed server remains unsupported unless the host supplies authoritative metadata and channel proof.
        /// </summary>
        [Test]
        public async Task ManagedServerWithoutEndpointProvidersIsNotSupported()
        {
            await using var harness = new Harness(m_bootstrap, m_issued, managedApplicationProviders: false);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(server.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.Empty);
            Assert.That(harness.RegisteredName(server), Is.Null);
        }

        /// <summary>
        /// Managed Server and ClientAndServer records use provider-owned URLs and verify the installed peer twice.
        /// </summary>
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedServerUsesAuthoritativeMetadataAndSecureChannelProof(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            server.Installed = true;
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            ApplicationRecordDataType record = harness.RegisteredRecord(server);
            Assert.That(record.ApplicationType, Is.EqualTo(type));
            Assert.That(record.DiscoveryUrls.ToArray(), Is.EqualTo(target.DiscoveryUrls.ToArray()));
            Assert.That(record.ServerCapabilities.ToArray(), Is.EqualTo(target.ServerCapabilities.ToArray()));
            Assert.That(harness.ManagedApplicationProofs, Is.EqualTo(new[] { server.Certificate, server.Certificate }));
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
        }

        /// <summary>
        /// Discovery URLs are registered independently of the host-selected connection endpoint.
        /// </summary>
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedServerAcceptsDistinctDiscoveryAndConnectionUrls(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            server.Installed = true;
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server) with
            {
                DiscoveryUrls = ["https://discovery.example.test/managed"]
            };
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(harness.RegisteredRecord(server).DiscoveryUrls.ToArray(),
                Is.EqualTo(target.DiscoveryUrls.ToArray()));
            Assert.That(target.DiscoveryUrls.ToList(), Does.Not.Contain(target.Endpoint.Description.EndpointUrl));
            Assert.That(harness.ManagedApplicationProofs, Is.EqualTo(new[] { server.Certificate, server.Certificate }));
        }

        /// <summary>
        /// Captured provider metadata remains detached during proof and registration despite Clone overrides.
        /// </summary>
        [Test]
        public async Task ProviderMetadataMutationDuringProofCannotChangeRegisteredManagedApplication()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            server.Installed = true;
            DeviceRegistrarManagedApplicationTarget original = harness.ConfigureManagedTarget(server);
            string[] urls = ["https://discovery.example.test/managed"];
            string[] capabilities = ["DA"];
            var application = new SelfCloningApplicationDescription { ApplicationUri = ManagedUriA };
            var policy = new SelfCloningUserTokenPolicy { PolicyId = "captured" };
            var description = new SelfCloningEndpointDescription
            {
                EndpointUrl = original.Endpoint.Description.EndpointUrl,
                Server = application,
                UserIdentityTokens = [policy],
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var quotas = new SelfCloningEndpointConfiguration { OperationTimeout = 12345 };
            DeviceRegistrarManagedApplicationTarget target = original with
            {
                DiscoveryUrls = urls.ToArrayOf(),
                ServerCapabilities = capabilities.ToArrayOf(),
                Endpoint = new ConfiguredEndpoint(null, description, quotas)
            };
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));
            harness.ManagedApplicationChannels.Setup(verifier => verifier.VerifyAsync(
                It.IsAny<ApplicationDescription>(), It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns<ApplicationDescription, DeviceRegistrarManagedApplicationTarget,
                    ByteString, CancellationToken>(
                    async (capturedApplication, captured, expected, _) =>
                    {
                        await Task.Yield();
                        urls[0] = "relative";
                        capabilities[0] = "mutated";
                        description.EndpointUrl = "opc.tcp://wrong-peer:4840";
                        application.ApplicationUri = "urn:wrong:application";
                        policy.PolicyId = "mutated";
                        quotas.OperationTimeout = 1;
                        EndpointConfiguration capturedQuotas = captured.Endpoint.Configuration ??
                            throw new InvalidOperationException("The captured endpoint configuration is missing.");

                        Assert.That(captured.Endpoint.Description, Is.Not.SameAs(description));
                        Assert.That(captured.Endpoint.Description.Server, Is.Not.SameAs(application));
                        Assert.That(captured.Endpoint.Description.UserIdentityTokens[0], Is.Not.SameAs(policy));
                        Assert.That(capturedQuotas, Is.Not.SameAs(quotas));
                        Assert.That(captured.Endpoint.Description.EndpointUrl,
                            Is.EqualTo(original.Endpoint.Description.EndpointUrl));
                        Assert.That(captured.Endpoint.Description.Server.ApplicationUri, Is.EqualTo(ManagedUriA));
                        Assert.That(captured.Endpoint.Description.UserIdentityTokens[0].PolicyId,
                            Is.EqualTo("captured"));
                        Assert.That(capturedQuotas.OperationTimeout, Is.EqualTo(12345));
                        Assert.That(capturedApplication.DiscoveryUrls[0],
                            Is.EqualTo("https://discovery.example.test/managed"));
                        harness.ManagedApplicationProofs.Add(expected);
                    });

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            ApplicationRecordDataType record = harness.RegisteredRecord(server);
            Assert.That(record.ApplicationUri, Is.EqualTo(ManagedUriA));
            Assert.That(record.DiscoveryUrls.ToArray(), Is.EqualTo(s_managedDiscoveryUrls));
            Assert.That(record.ServerCapabilities.ToArray(), Is.EqualTo(s_serverCapabilities));
            Assert.That(harness.ManagedApplicationProofs, Is.EqualTo(new[] { server.Certificate, server.Certificate }));
        }

        /// <summary>
        /// Missing or blank authoritative capabilities prevent registration and remote changes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagedServerRejectsInvalidCapabilitiesBeforeRegistration(bool blank)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.ClientAndServer);
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server);
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(
                    target with { ServerCapabilities = blank ? [" "] : ArrayOf<string>.Empty }));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(((ServiceResultException)error!.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(harness.RegisteredName(server), Is.Null);
            Assert.That(server.Calls, Is.Empty);
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
        }

        /// <summary>
        /// A restart after certificate rotation resumes only the exact retained issuance despite a stale operator pin.
        /// </summary>
        [Test]
        public async Task ManagedServerRestartResumesRetainedCertificateProofAfterRotation()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            server.CurrentCertificate = ByteString.From([0x21, 0x22, 0x23]);
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server);
            bool interruptAfterRotation = true;
            harness.ManagedApplicationChannels.Setup(verifier => verifier.VerifyAsync(
                It.IsAny<ApplicationDescription>(),
                It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()))
                .Callback<ApplicationDescription, DeviceRegistrarManagedApplicationTarget, ByteString,
                    CancellationToken>((_, _, expected, _) =>
                {
                    harness.ManagedApplicationProofs.Add(expected);
                    if (expected == server.Certificate && interruptAfterRotation)
                    {
                        interruptAfterRotation = false;
                        throw new ServiceResultException(StatusCodes.BadNotConnected);
                    }
                })
                .Returns(new ValueTask());

            AggregateException? interrupted = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(((ServiceResultException)interrupted!.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(server.CurrentCertificate, Is.EqualTo(server.Certificate));
            Assert.That(server.Installed, Is.True);
            int callsBeforeRestart = server.Calls.Count;
            int csrCallsBeforeRestart = server.CsrCalls;

            harness.RestartProcessor();
            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(harness.ManagedApplicationProofs,
                Is.EqualTo(new[] { target.InstalledCertificate, server.Certificate, server.Certificate }));
            Assert.That(server.CurrentCertificate, Is.EqualTo(server.Certificate));
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA, ManagedUriA }));
            Assert.That(server.CsrCalls, Is.EqualTo(csrCallsBeforeRestart));
            Assert.That(server.Calls.GetRange(callsBeforeRestart, server.Calls.Count - callsBeforeRestart),
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
        }

        /// <summary>
        /// A restart rejects a managed certificate that is neither the operator pin nor the retained issuance.
        /// </summary>
        [Test]
        public async Task ManagedServerRestartRejectsCertificateOutsideRetainedIssuance()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            server.CurrentCertificate = ByteString.From([0x31, 0x32, 0x33]);
            harness.ConfigureManagedTarget(server);
            bool interruptAfterRotation = true;
            harness.ManagedApplicationChannels.Setup(verifier => verifier.VerifyAsync(
                It.IsAny<ApplicationDescription>(),
                It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()))
                .Callback<ApplicationDescription, DeviceRegistrarManagedApplicationTarget, ByteString,
                    CancellationToken>((_, _, expected, _) =>
                {
                    harness.ManagedApplicationProofs.Add(expected);
                    if (expected == server.Certificate && interruptAfterRotation)
                    {
                        interruptAfterRotation = false;
                        throw new ServiceResultException(StatusCodes.BadNotConnected);
                    }
                })
                .Returns(new ValueTask());

            await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));
            server.CurrentCertificate = ByteString.From([0x41, 0x42, 0x43]);
            harness.RestartProcessor();

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(((ServiceResultException)error!.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(harness.ManagedApplicationProofs,
                Is.EqualTo(new[] { ByteString.From([0x31, 0x32, 0x33]), server.Certificate }));
        }

        /// <summary>
        /// A provider credential that differs from the configuration's installed certificate cannot be replaced.
        /// </summary>
        [Test]
        public async Task ManagedServerRejectsProviderCertificateMismatchBeforeCreatingRequest()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            server.Installed = true;
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server);
            harness.ManagedApplications
                .Setup(provider => provider.GetTargetAsync(ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(
                    target with { InstalledCertificate = ByteString.From(new byte[] { 1, 2, 3 }) }));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(((ServiceResultException)error!.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(server.CsrCalls, Is.Zero);
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
        }

        /// <summary>
        /// Endpoint data that cannot name an absolute advertised endpoint is rejected before application registration.
        /// </summary>
        [Test]
        public async Task ManagedServerRejectsInvalidDiscoveryUrlsBeforeRegistration()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication server = harness.AddManaged(
                "managed-a", ManagedUriA, m_managedA, ApplicationType.Server);
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(server);
            harness.ManagedApplications
                .Setup(provider => provider.GetTargetAsync(ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(
                    target with { DiscoveryUrls = ["relative"] }));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(((ServiceResultException)error!.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(harness.RegisteredName(server), Is.Null);
            Assert.That(harness.ManagedApplicationProofs, Is.Empty);
        }

        /// <summary>
        /// An application that already reports the issued certificate receives no TrustList or certificate update.
        /// </summary>
        [Test]
        public async Task ManagedApplicationHoldingIssuedCertificateIsNotUpdated()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.Installed = true;
            application.InstallTrustList(application.CreateTrustList());

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.Calls, Does.Contain(Ua.MethodIds.ServerConfigurationType_GetCertificates));
            Assert.That(application.Calls, Does.Contain(Ua.MethodIds.FileType_Read));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.FileType_Write));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(application.Calls,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
        }

        /// <summary>
        /// Application setup is the certificate and the TrustList, so an application that holds the issued
        /// certificate with a missing or stale TrustList receives only the TrustList.
        /// </summary>
        [TestCase("missing")]
        [TestCase("different")]
        public async Task ManagedApplicationHoldingCertificateReceivesMissingOrStaleTrustList(string trust)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.Installed = true;
            if (trust == "different")
            {
                application.InstallTrustList(new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [ByteString.From(m_managedB.RawData.AsSpan())]
                });
            }

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.Calls, Does.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(application.Calls, Does.Contain(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
            Assert.That(application.Calls,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(application.HasPendingChanges, Is.False);
            Assert.That(application.InstalledTrustList.TrustedCertificates.ToList(),
                Is.EqualTo(new[] { application.Certificate }));
        }

        /// <summary>
        /// A revocation while the application stages its certificate cancels the staged change instead of
        /// applying it.
        /// </summary>
        [Test]
        public async Task ManagedRevocationDuringCertificateUpdateCancelsStagedCredential()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.OnUpdateCertificate = () => SetSoftwareStatusAsync(harness, false, "revoked");

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            int update = application.Calls.IndexOf(Ua.MethodIds.ServerConfigurationType_UpdateCertificate);
            Assert.That(update, Is.GreaterThanOrEqualTo(0));
            List<NodeId> after = application.Calls.GetRange(update + 1, application.Calls.Count - update - 1);
            Assert.That(after, Does.Contain(Ua.MethodIds.ServerConfigurationType_CancelChanges));
            Assert.That(after, Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
            Assert.That(application.Installed, Is.False);
            Assert.That(application.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// A rejected application's staged changes are cancelled before the next application runs, so its
        /// ApplyChanges cannot commit them.
        /// </summary>
        [Test]
        public async Task CancelledManagedChangesDoNotReachTheNextApplication()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication rejected = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            rejected.OnUpdateCertificate = () => SetSoftwareStatusAsync(harness, false, "revoked");
            rejected.OnCancelChanges = () => SetSoftwareStatusAsync(harness, true, "restored");

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(rejected.Installed, Is.False);
            Assert.That(rejected.HasPendingChanges, Is.False);
            Assert.That(next.Installed, Is.True);
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA, ManagedUriB }));
        }

        /// <summary>
        /// A server that cannot cancel staged changes stops the push, keeping the original and cleanup errors,
        /// so no later ApplyChanges commits the rejected application's changes.
        /// </summary>
        [Test]
        public async Task UncancellableManagedChangesStopLaterApplications()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication rejected = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            rejected.OnUpdateCertificate = () => SetSoftwareStatusAsync(harness, false, "revoked");
            rejected.CancelError = new ServiceResultException(StatusCodes.BadNotSupported);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<AggregateException>());
            var cleanup = (AggregateException)error.InnerExceptions[0];
            Assert.That(cleanup.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((ServiceResultException)cleanup.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(((ServiceResultException)cleanup.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(rejected.HasPendingChanges, Is.True);
            Assert.That(rejected.Installed, Is.False);
            Assert.That(next.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
        }

        /// <summary>
        /// A CloseAndUpdate or UpdateCertificate reply can fail after the server staged the change, so the
        /// session's changes are cancelled and the lease is retired before another application's ApplyChanges.
        /// </summary>
        [TestCase("trust")]
        [TestCase("certificate")]
        public async Task AmbiguousManagedStagingFailureRetiresSessionBeforeTheNextApplication(string stage)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication failed = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            SetStagedError(failed, stage, new ServiceResultException(StatusCodes.BadTimeout));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(failed.Calls, Does.Contain(Ua.MethodIds.ServerConfigurationType_CancelChanges));
            Assert.That(failed.HasPendingChanges, Is.False);
            Assert.That(failed.Installed, Is.False);
            Assert.That(next.Installed, Is.False);
            Assert.That(next.Calls, Is.Empty);
            Assert.That(next.HasPendingChanges, Is.False);
            Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
        }

        /// <summary>
        /// An ambiguous staging failure that cannot be cancelled keeps both errors and stops before another
        /// application's ApplyChanges can commit the uncertain change.
        /// </summary>
        [TestCase("trust")]
        [TestCase("certificate")]
        public async Task UncancellableAmbiguousManagedStagingStopsLaterApplications(string stage)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication failed = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            SetStagedError(failed, stage, new ServiceResultException(StatusCodes.BadTimeout));
            failed.CancelError = new ServiceResultException(StatusCodes.BadNotSupported);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<AggregateException>());
            var cleanup = (AggregateException)error.InnerExceptions[0];
            Assert.That(cleanup.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((ServiceResultException)cleanup.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(((ServiceResultException)cleanup.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(failed.HasPendingChanges, Is.True);
            Assert.That(failed.Installed, Is.False);
            Assert.That(next.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
        }

        /// <summary>
        /// Bad_NothingToDo from CancelChanges means the session has no transaction, so the original error is
        /// reported alone. It does not prove that the earlier request has finished, so the lease is retired.
        /// </summary>
        [Test]
        public async Task CancelChangesWithoutTransactionReportsOnlyTheOriginalErrorAndRetiresSession()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication failed = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            SetStagedError(failed, "certificate", new ServiceResultException(StatusCodes.BadTimeout));
            failed.CancelError = new ServiceResultException(StatusCodes.BadNothingToDo);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(failed.Calls, Does.Contain(Ua.MethodIds.ServerConfigurationType_CancelChanges));
            Assert.That(next.Installed, Is.False);
            Assert.That(next.Calls, Is.Empty);
            Assert.That(harness.AuthorizedUris, Is.EqualTo(new[] { ManagedUriA }));
        }

        /// <summary>
        /// A timed-out server request can stage its change after cancellation replies. The lease is retired
        /// even after Good or BadNothingToDo, preventing another application's ApplyChanges from committing it.
        /// </summary>
        [TestCase("trust", false)]
        [TestCase("trust", true)]
        [TestCase("certificate", false)]
        [TestCase("certificate", true)]
        public async Task LateManagedStagingAfterCancelCannotReachAnotherApplicationsApply(
            string stage, bool nothingToDo)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication failed = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            ManagedApplication next = harness.AddManaged("managed-b", ManagedUriB, m_managedB);
            SetStagedError(failed, stage, new ServiceResultException(StatusCodes.BadTimeout));
            if (nothingToDo)
            {
                failed.CancelError = new ServiceResultException(StatusCodes.BadNothingToDo);
            }
            Func<ValueTask> clearBeforeTimeout = () =>
            {
                failed.Discard();
                return default;
            };
            if (stage == "trust")
            {
                failed.OnCloseAndUpdate = clearBeforeTimeout;
            }
            else
            {
                failed.OnUpdateCertificate = clearBeforeTimeout;
            }
            var releaseLateRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task lateRequest = StageLateAsync();
            next.OnUpdateCertificate = CompleteLateRequestAsync;
            harness.BeforeLeaseDisposal = CompleteLateRequestAsync;
            try
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

                Assert.That(lateRequest.IsCompleted, Is.True);
                await lateRequest.ConfigureAwait(false);
                Assert.That(error!.Flatten().InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(((ServiceResultException)error.Flatten().InnerExceptions[0]).StatusCode,
                    Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That(failed.Calls, Does.Contain(Ua.MethodIds.ServerConfigurationType_CancelChanges));
                Assert.That(failed.HasPendingChanges, Is.True);
                Assert.That(failed.Installed, Is.False);
                Assert.That(next.Calls, Is.Empty);
                Assert.That(next.Installed, Is.False);
                Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
            }
            finally
            {
                releaseLateRequest.TrySetResult(true);
                await lateRequest.ConfigureAwait(false);
            }

            async Task StageLateAsync()
            {
                await releaseLateRequest.Task.ConfigureAwait(false);
                failed.StageLateChange(stage);
            }

            async ValueTask CompleteLateRequestAsync()
            {
                // Both entry points occur after CancelChanges has returned to the processor.
                if (failed.Calls.Contains(Ua.MethodIds.ServerConfigurationType_CancelChanges))
                {
                    releaseLateRequest.TrySetResult(true);
                    await lateRequest.ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// GetCertificates returns parallel arrays, so a response with fewer or more CertificateTypeIds than
        /// Certificates is rejected instead of being read as proof, and the application is left unchanged.
        /// </summary>
        [TestCase(0)]
        [TestCase(2)]
        public async Task NonParallelGetCertificatesResponseIsRejectedWithoutMutation(int reportedTypes)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.Installed = true;
            TrustListDataType trustList = application.CreateTrustList();
            application.InstallTrustList(trustList);
            application.ReportedTypeCount = reportedTypes;

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadUnknownResponse));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.FileType_Write));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(application.Calls,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(application.Calls, Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
            Assert.That(application.Installed, Is.True);
            Assert.That(application.HasPendingChanges, Is.False);
            Assert.That(application.InstalledTrustList.TrustedCertificates.ToList(),
                Is.EqualTo(trustList.TrustedCertificates.ToList()));
        }

        /// <summary>
        /// GetCertificates names the resolved group, so a server that rejects a null group is still verified.
        /// </summary>
        [Test]
        public async Task ManagedVerificationNamesTheResolvedCertificateGroup()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.RejectNullGroup = true;

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.Installed, Is.True);
            Assert.That(application.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// An application whose group does not support the issued certificate type is rejected before a CSR.
        /// </summary>
        [Test]
        public async Task ManagedGroupWithoutIssuedCertificateTypeIsRejectedBeforeCsr()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);
            application.CertificateTypes = [Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType];

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(application.CsrCalls, Is.Zero);
            Assert.That(application.Installed, Is.False);
        }

        /// <summary>
        /// A host-selected per-application certificate type is used for CSR, receipt validation and installation.
        /// </summary>
        [TestCase(ApplicationType.Client)]
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedApplicationUsesItsSelectedCertificateType(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.SelectedCertificateTypeId = Ua.ObjectTypeIds.RsaMinApplicationCertificateType;
            application.CertificateTypes = [application.SelectedCertificateTypeId];
            application.CurrentCertificate = harness.Bootstrap;
            harness.ConfigureManagedTarget(application);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.RequestedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(application.UpdatedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(application.Installed, Is.True);
            Assert.That(application.HasPendingChanges, Is.False);
            Assert.That(harness.EnrollmentCertificateTypes[application.Id],
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                It.IsAny<OnboardingIdentitySelection>(), Harness.ApplicationId,
                It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An absent override uses the registrar default, never the first advertised group type.
        /// </summary>
        [TestCase(ApplicationType.Client)]
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedApplicationWithoutOverrideUsesConfiguredDefault(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.CertificateTypes =
            [
                Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
            ];
            application.CurrentCertificate = harness.Bootstrap;
            harness.ConfigureManagedTarget(application);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.RequestedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
            Assert.That(application.UpdatedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
            Assert.That(harness.EnrollmentCertificateTypes[application.Id],
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
            Assert.That(application.Installed, Is.True);
        }

        /// <summary>
        /// A client without a metadata provider retains the configured default and legacy enrollment contract.
        /// </summary>
        [Test]
        public async Task ManagedClientWithoutMetadataProviderUsesConfiguredDefault()
        {
            await using var harness = new Harness(m_bootstrap, m_issued, managedApplicationProviders: false);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(application.RequestedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
            Assert.That(application.Installed, Is.True);
            harness.ManagedApplications.Verify(
                provider => provider.GetTargetAsync(ManagedUriA, It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A selected type absent from the managed application's group is rejected before creating a CSR.
        /// </summary>
        [TestCase(ApplicationType.Client)]
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedApplicationRejectsUnsupportedSelectedCertificateTypeBeforeCsr(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.SelectedCertificateTypeId = Ua.ObjectTypeIds.RsaMinApplicationCertificateType;
            application.CertificateTypes = [Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType];
            harness.ConfigureManagedTarget(application);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(application.CsrCalls, Is.Zero);
            Assert.That(application.Installed, Is.False);
            Assert.That(harness.RegisteredName(application), Is.Null);
            Assert.That(application.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// A recovered enrollment cannot use a retained receipt issued under the previous host-selected type.
        /// </summary>
        [TestCase(ApplicationType.Client)]
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedApplicationRejectsRecoveryWithChangedCertificateType(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.SelectedCertificateTypeId = Ua.ObjectTypeIds.RsaMinApplicationCertificateType;
            application.CertificateTypes =
            [
                Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
            ];
            application.RetainedCertificateTypeId = nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            harness.ConfigureManagedTarget(application);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(application.CsrCalls, Is.Zero);
            Assert.That(application.Installed, Is.False);
        }

        /// <summary>
        /// A configured metadata provider's missing target or request failure cannot select the default profile.
        /// </summary>
        [TestCase(ApplicationType.Client, false)]
        [TestCase(ApplicationType.Client, true)]
        [TestCase(ApplicationType.Server, false)]
        [TestCase(ApplicationType.Server, true)]
        [TestCase(ApplicationType.ClientAndServer, false)]
        [TestCase(ApplicationType.ClientAndServer, true)]
        public async Task ManagedApplicationRejectsMissingOrFailedConfiguredTarget(
            ApplicationType type,
            bool providerThrows)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            var failure = new ServiceResultException(StatusCodes.BadNotFound);
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(() => providerThrows
                    ? throw failure
                    : new ValueTask<DeviceRegistrarManagedApplicationTarget>(
                        (DeviceRegistrarManagedApplicationTarget)null!));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(providerThrows ? StatusCodes.BadNotFound : StatusCodes.BadConfigurationError));
            if (providerThrows)
            {
                Assert.That(error.InnerExceptions[0], Is.SameAs(failure));
            }
            Assert.That(harness.RegisteredName(application), Is.Null);
            Assert.That(application.CsrCalls, Is.Zero);
            Assert.That(application.HasPendingChanges, Is.False);
            Assert.That(application.Installed, Is.False);
            harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                It.IsAny<OnboardingIdentitySelection>(), application.Id,
                It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A namespace-URI override resolves to the same standard certificate type used by the enrollment service.
        /// </summary>
        [TestCase(ApplicationType.Client)]
        [TestCase(ApplicationType.Server)]
        [TestCase(ApplicationType.ClientAndServer)]
        public async Task ManagedApplicationResolvesNamespaceUriCertificateType(ApplicationType type)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.SelectedCertificateTypeId = Ua.ObjectTypeIds.RsaMinApplicationCertificateType;
            application.CertificateTypes = [application.SelectedCertificateTypeId];
            application.CurrentCertificate = harness.Bootstrap;
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(application) with
            {
                CertificateTypeId = new ExpandedNodeId(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType, Ua.Namespaces.OpcUa)
            };
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(harness.EnrollmentCertificateTypes[application.Id],
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(application.RequestedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(application.UpdatedCertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaMinApplicationCertificateType));
            Assert.That(application.Installed, Is.True);
        }

        /// <summary>
        /// Remote, index-only and unknown-namespace overrides cannot be reinterpreted as a local certificate type.
        /// </summary>
        [TestCase(ApplicationType.Client, "remote")]
        [TestCase(ApplicationType.Client, "index")]
        [TestCase(ApplicationType.Client, "unknown")]
        [TestCase(ApplicationType.Server, "remote")]
        [TestCase(ApplicationType.Server, "index")]
        [TestCase(ApplicationType.Server, "unknown")]
        [TestCase(ApplicationType.ClientAndServer, "remote")]
        [TestCase(ApplicationType.ClientAndServer, "index")]
        [TestCase(ApplicationType.ClientAndServer, "unknown")]
        public async Task ManagedApplicationRejectsNonlocalCertificateTypeBeforeRegistration(
            ApplicationType type,
            string address)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, certificateEnrollmentRouting: true);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            DeviceRegistrarManagedApplicationTarget target = harness.ConfigureManagedTarget(application) with
            {
                CertificateTypeId = address switch
                {
                    "remote" => new ExpandedNodeId(
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, Ua.Namespaces.OpcUa, 1),
                    "index" => new ExpandedNodeId(
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType.WithNamespaceIndex(1)),
                    _ => new ExpandedNodeId(
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, "urn:unknown:certificate-types")
                }
            };
            harness.ManagedApplications.Setup(provider => provider.GetTargetAsync(
                ManagedUriA, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(harness.RegisteredName(application), Is.Null);
            Assert.That(harness.EnrollmentCertificateTypes, Is.Empty);
            Assert.That(application.CsrCalls, Is.Zero);
            Assert.That(application.HasPendingChanges, Is.False);
        }

        /// <summary>
        /// A legacy enrollment provider accepts the default but cannot start an unroutable nondefault request.
        /// </summary>
        [TestCase(ApplicationType.Client, false)]
        [TestCase(ApplicationType.Client, true)]
        [TestCase(ApplicationType.Server, false)]
        [TestCase(ApplicationType.Server, true)]
        [TestCase(ApplicationType.ClientAndServer, false)]
        [TestCase(ApplicationType.ClientAndServer, true)]
        public async Task LegacyEnrollmentProviderRejectsOnlyNondefaultCertificateSelection(
            ApplicationType type,
            bool nondefault)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            ManagedApplication application = harness.AddManaged("managed-a", ManagedUriA, m_managedA, type);
            application.SelectedCertificateTypeId = nondefault
                ? Ua.ObjectTypeIds.RsaMinApplicationCertificateType
                : Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
            application.CertificateTypes = [application.SelectedCertificateTypeId];
            application.CurrentCertificate = harness.Bootstrap;
            harness.ConfigureManagedTarget(application);

            if (nondefault)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

                Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                    Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(harness.RegisteredName(application), Is.Null);
                Assert.That(application.CsrCalls, Is.Zero);
                Assert.That(application.HasPendingChanges, Is.False);
                harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), application.Id,
                    It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            else
            {
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

                Assert.That(application.CsrCalls, Is.EqualTo(1));
                Assert.That(application.RequestedCertificateTypeId,
                    Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
                Assert.That(application.Installed, Is.True);
            }
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(1));
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
                    Harness.Product, new NodeId("later-managed", 1), string.Empty).ConfigureAwait(false));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(1));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(1));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(1));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(2));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// Repeated host disposal prevents new attempts without contacting a device.
        /// </summary>
        [Test]
        public async Task DisposedProcessorRejectsNewAttemptsWithoutDeviceCalls()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.Processor.Dispose();
            harness.Processor.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(harness.CallMethods, Is.Empty);
            Assert.That(harness.LeasesDisposed, Is.Zero);
            harness.Identities.VerifyNoOtherCalls();
            harness.Enrollments.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Direct callers cannot overlap device key or trust changes, and cancellation releases a waiting caller.
        /// </summary>
        [Test]
        public async Task ConcurrentAttemptCancellationDoesNotStartAnotherDeviceRequest()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.BeforeCsrAsync = async _ =>
            {
                entered.SetResult(true);
                await resume.Task.ConfigureAwait(false);
            };
            Task first = harness.Processor.ProcessAsync(harness.Application).AsTask();
            try
            {
                Task started = await Task.WhenAny(first, entered.Task).ConfigureAwait(false);
                Assert.That(started, Is.SameAs(entered.Task));
                await entered.Task.ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                Task second = harness.Processor.ProcessAsync(harness.Application, cancellation.Token).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                cancellation.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await second.ConfigureAwait(false));
                harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), Harness.ApplicationId,
                    It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.That(harness.CsrCalls, Is.Zero);
            }
            finally
            {
                resume.SetResult(true);
                await first.ConfigureAwait(false);
            }
            Assert.That(harness.CsrCalls, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(3));
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
            Assert.That(harness.CallMethods, Has.Count.EqualTo(2));
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
        [TestCase("missing-csr")]
        [TestCase("malformed-csr")]
        [TestCase("wrong-csr-key")]
        [TestCase("trailing-certificate")]
        public async Task InvalidEnrollmentEvidenceFailsBeforeCredentialMutation(string substitution)
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { EnrollmentMutation = substitution };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(harness.CallMethods, Has.Count.EqualTo(2));
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
            Assert.That(harness.CallMethods[harness.CallMethods.Count - 2],
                Is.EqualTo(Ua.MethodIds.FileType_Close));
            Assert.That(harness.CallMethods[harness.CallMethods.Count - 1],
                Is.EqualTo(Ua.MethodIds.ServerConfigurationType_CancelChanges));
            Assert.That(harness.HasPendingChanges, Is.False);
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
        }

        /// <summary>
        /// A DCA CloseAndUpdate or UpdateCertificate reply can fail after the device staged the change, so the
        /// session's changes are cancelled and the credential stays pending.
        /// </summary>
        [TestCase("trust")]
        [TestCase("certificate")]
        public async Task AmbiguousDcaStagingFailureIsCancelled(string stage)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            if (stage == "trust")
            {
                harness.TrustStagedError = failure;
            }
            else
            {
                harness.CertificateStagedError = failure;
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(harness.CancelCalls, Is.EqualTo(1));
            Assert.That(harness.HasPendingChanges, Is.False);
            Assert.That(harness.ApplyCalls, Is.EqualTo(stage == "trust" ? 0 : 1));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.DcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// A DCA staging failure that cannot be cancelled reports both errors and never applies the change.
        /// </summary>
        [Test]
        public async Task UncancellableDcaStagingFailurePreservesBothErrors()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.TrustStagedError = new ServiceResultException(StatusCodes.BadTimeout);
            harness.CancelError = new ServiceResultException(StatusCodes.BadNotSupported);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(((ServiceResultException)error.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(harness.HasPendingChanges, Is.True);
            Assert.That(harness.ApplyCalls, Is.Zero);
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
        }

        /// <summary>
        /// A DCA that already presents the issued certificate but reports a different TrustList receives only
        /// the retained TrustList before the credential is confirmed on a new connection.
        /// </summary>
        [TestCase("trusted")]
        [TestCase("issuer")]
        public async Task InstalledCredentialWithStaleTrustListReceivesTrustListBeforeConfirmation(string trust)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.RecordIssuedDcaCertificateAsync(
                Harness.Product, Harness.ApplicationId, harness.Issued).ConfigureAwait(false);
            harness.Initial.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);
            var stale = new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All };
            if (trust == "trusted")
            {
                stale.TrustedCertificates = [harness.Issued];
            }
            else
            {
                stale.IssuerCertificates = [harness.Issued];
            }
            harness.InstallTrustList(stale);

            await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false);

            Assert.That(harness.CallMethods, Does.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(harness.ApplyCalls, Is.EqualTo(1));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.HasPendingChanges, Is.False);
            Assert.That(harness.InstalledTrustList.TrustedCertificates.Count, Is.Zero);
            Assert.That(harness.InstalledTrustList.IssuerCertificates.Count, Is.Zero);
            Assert.That(harness.LeasesDisposed, Is.EqualTo(2));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.PendingDcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// The new connection reads the TrustList back before confirmation, so a TrustList replaced after
        /// ApplyChanges leaves the credential pending.
        /// </summary>
        [Test]
        public async Task TrustListReplacedBeforeProofLeavesCredentialPending()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeProof = () =>
            {
                harness.InstallTrustList(new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [harness.Bootstrap]
                });
                return default;
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(device.PendingDcaCertificate, Is.EqualTo(harness.Issued));
        }

        /// <summary>
        /// An uncertain apply result retains pending issuance and never triggers blind reconnect or promotion.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task ApplyFailureRetainsPendingReceiptForExplicitRecovery(int failedApply)
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var failure = new ServiceResultException(StatusCodes.BadTimeout);
            harness.ApplyError = failure;
            harness.ApplyErrorOnCall = failedApply;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(failedApply));
            Assert.That(harness.ApplyCalls, Is.EqualTo(failedApply));
            DeviceRegistrarDevice? device = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(device!.PendingDcaCertificate, Is.EqualTo(harness.Issued));
            Assert.That(device.DcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// Trust application may close the first channel, but a different peer cannot receive the certificate.
        /// </summary>
        [Test]
        public async Task WrongActualConfiguringPeerStopsBeforeCertificateUpdate()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.Update.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            Assert.That(harness.ApplyCalls, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(2));
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            DeviceRegistrarDevice? state = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(state!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(state.PendingDcaCertificate, Is.EqualTo(harness.Issued));
        }

        /// <summary>
        /// The fresh certificate-transfer session rechecks software approval after trust was committed.
        /// </summary>
        [Test]
        public async Task SoftwareRevocationBeforeCertificateUpdateStopsAfterTrustApply()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            harness.BeforeUpdate = async () =>
                await harness.Lifecycle.UpdateSoftwareStatusAsync(Harness.Product, false, "revoked")
                    .ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(harness.ApplyCalls, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(2));
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            DeviceRegistrarDevice? state = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(state!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(state.PendingDcaCertificate, Is.EqualTo(harness.Issued));
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
            AssertNoDcaMutation(harness);
            Assert.That(harness.CallMethods, Does.Contain(Ua.MethodIds.FileType_Read));
            harness.Identities.VerifyNoOtherCalls();
            harness.Enrollments.Verify(provider => provider.GetOrCreateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<NodeId>(),
                It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Installed-peer proof cannot promote a receipt with missing, malformed or different-key request evidence.
        /// </summary>
        [TestCase("missing-csr")]
        [TestCase("malformed-csr")]
        [TestCase("wrong-csr-key")]
        [TestCase("trailing-certificate")]
        public async Task InstalledPeerRecoveryRejectsSubstitutedSigningEvidence(string substitution)
        {
            await using var harness = new Harness(m_bootstrap, m_issued) { EnrollmentMutation = substitution };
            await harness.SeedCandidateAsync().ConfigureAwait(false);
            await harness.Lifecycle.RecordIssuedDcaCertificateAsync(
                Harness.Product, Harness.ApplicationId, harness.Issued).ConfigureAwait(false);
            harness.Initial.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_issued.RawData);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(harness.CsrCalls, Is.Zero);
            Assert.That(harness.CallMethods, Is.Empty);
            DeviceRegistrarDevice? state = await harness.Lifecycle.FindDeviceAsync(Harness.Product)
                .ConfigureAwait(false);
            Assert.That(state!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(state.PendingDcaCertificate, Is.EqualTo(harness.Issued));
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
        /// A nonresponding cancellation request has an independent deadline even when transport timeouts are
        /// infinite and the caller cancels. Its timeout and any lease disposal error retain the primary failure.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(int.MaxValue, false)]
        [TestCase(int.MaxValue, true)]
        public async Task NonrespondingCancellationExpiresAndDisposesLease(int operationTimeout, bool failDisposal)
        {
            await using var harness = new Harness(m_bootstrap, m_issued, resolveFromServices: true);
            harness.Initial.Channel.SetupGet(channel => channel.OperationTimeout).Returns(operationTimeout);
            harness.Initial.InnerSession.OperationTimeout = operationTimeout;
            var primary = new ServiceResultException(StatusCodes.BadTimeout);
            var disposal = new IOException("lease disposal failed");
            harness.TrustStagedError = primary;
            if (failDisposal)
            {
                harness.CleanupError = disposal;
            }
            using var caller = new CancellationTokenSource();
            var entered = new TaskCompletionSource<CancellationToken>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.WaitForCancellation = async token =>
            {
                var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = token.Register(
                    () => response.TrySetCanceled(token));
                entered.TrySetResult(token);
                await response.Task.ConfigureAwait(false);
            };
            Task processing = harness.Processor.ProcessAsync(harness.Application, caller.Token).AsTask();
            CancellationToken cleanup = await entered.Task.ConfigureAwait(false);
            caller.Cancel();
            Assert.That(cleanup.CanBeCanceled, Is.True);
            Assert.That(cleanup.IsCancellationRequested, Is.False);
            harness.CleanupTime.Advance(TimeSpan.FromMilliseconds(4999));
            Assert.That(cleanup.IsCancellationRequested, Is.False);
            Assert.That(processing.IsCompleted, Is.False);
            Assert.That(harness.LeasesDisposed, Is.Zero);
            harness.CleanupTime.Advance(TimeSpan.FromMilliseconds(1));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await processing.ConfigureAwait(false));

            Assert.That(cleanup.IsCancellationRequested, Is.True);
            Assert.That(error!.Flatten().InnerExceptions, Does.Contain(primary));
            Assert.That(error.Flatten().InnerExceptions, Has.Exactly(1).InstanceOf<OperationCanceledException>());
            Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(failDisposal ? 3 : 2));
            if (failDisposal)
            {
                Assert.That(error.Flatten().InnerExceptions, Does.Contain(disposal));
            }
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
            Assert.That(harness.Update.Requests, Is.Empty);
            Assert.That(harness.Proof.Requests, Is.Empty);
        }

        /// <summary>
        /// Staging, transaction cancellation and lease cleanup errors survive together; no later lease is opened.
        /// </summary>
        [Test]
        public async Task StagingCancellationAndLeaseCleanupFailuresAreAllPreserved()
        {
            await using var harness = new Harness(m_bootstrap, m_issued);
            var primary = new ServiceResultException(StatusCodes.BadTimeout);
            var cancellation = new ServiceResultException(StatusCodes.BadNotSupported);
            var disposal = new InvalidOperationException("owned session cleanup failed");
            harness.TrustStagedError = primary;
            harness.CancelError = cancellation;
            harness.CleanupError = disposal;

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Processor.ProcessAsync(harness.Application).ConfigureAwait(false));

            Assert.That(error!.Flatten().InnerExceptions, Is.EquivalentTo(new Exception[]
            {
                primary, cancellation, disposal
            }));
            Assert.That(harness.CancelCalls, Is.EqualTo(1));
            Assert.That(harness.LeasesDisposed, Is.EqualTo(1));
            Assert.That(harness.Update.Requests, Is.Empty);
            Assert.That(harness.Proof.Requests, Is.Empty);
            Assert.That(harness.ApplyCalls, Is.Zero);
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
        [TestCase(false)]
        [TestCase(true)]
        public void FluentRegistrationRequiresExplicitProvidersAndSharesTheHostedQueue(bool registeredClock)
        {
            var services = new ServiceCollection();
            if (registeredClock)
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            }
            Assert.Throws<InvalidOperationException>(() => services.AddDeviceRegistrarPush());
            services.AddSingleton(new Mock<IDeviceRegistrarPushConnectionProvider>().Object);
            services.AddSingleton(new Mock<IDeviceRegistrarPushEnrollmentProvider>().Object);
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            services.AddSingleton<IDeviceRegistrarCertificateLifecycle>(lifecycle);
            services.AddSingleton(new Mock<ICertificateRequestIssuanceStore>().Object);
            Assert.Throws<InvalidOperationException>(() => services.AddDeviceRegistrarPush());
            services.AddSingleton<IDeviceRegistrarLifecycle>(lifecycle);
            services.AddSingleton(new Mock<IDeviceRegistrarAuthorization>().Object);
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
        /// Changes the product's software approval from inside a scripted device call.
        /// </summary>
        private static async ValueTask SetSoftwareStatusAsync(
            Harness harness, bool status, string softwareRevision)
        {
            bool updated = await harness.Lifecycle.UpdateSoftwareStatusAsync(
                Harness.Product, status, softwareRevision).ConfigureAwait(false);
            Assert.That(updated, Is.True);
        }

        /// <summary>
        /// Encodes a TrustList as the body of a TrustList file.
        /// </summary>
        private static byte[] Encode(TrustListDataType trustList)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, s_context, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes the body of a TrustList file.
        /// </summary>
        private static TrustListDataType Decode(byte[] content)
        {
            using var stream = new MemoryStream(content);
            using var decoder = new BinaryDecoder(stream, s_context, true);
            return decoder.ReadEncodeable<TrustListDataType>(null);
        }

        /// <summary>
        /// Asserts that the DCA received no TrustList write, certificate update or ApplyChanges.
        /// </summary>
        private static void AssertNoDcaMutation(Harness harness)
        {
            Assert.That(harness.CallMethods, Does.Not.Contain(Ua.MethodIds.FileType_Write));
            Assert.That(harness.CallMethods, Does.Not.Contain(Ua.MethodIds.TrustListType_CloseAndUpdate));
            Assert.That(harness.CallMethods,
                Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(harness.CallMethods, Does.Not.Contain(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
        }

        /// <summary>
        /// Makes a managed application's CloseAndUpdate or UpdateCertificate fail after staging the change.
        /// </summary>
        private static void SetStagedError(ManagedApplication application, string stage, Exception error)
        {
            switch (stage)
            {
                case "trust":
                    application.TrustStagedError = error;
                    break;
                case "certificate":
                    application.CertificateStagedError = error;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        /// <summary>
        /// Owns deterministic sessions and real lifecycle state while mocking only host and issuance boundaries.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates one bootstrap generation, one installed-proof generation, and a correlated issued receipt.
            /// </summary>
            public Harness(
                Certificate bootstrap,
                Certificate issued,
                bool scopeCapability = true,
                bool resolveFromServices = false,
                bool managedApplicationProviders = true,
                bool certificateEnrollmentRouting = false,
                bool externalAuthentication = false)
            {
                if (certificateEnrollmentRouting)
                {
                    Enrollments.As<IDeviceRegistrarPushCertificateEnrollmentProvider>()
                        .Setup(provider => provider.GetOrCreateAsync(
                            It.IsAny<OnboardingIdentitySelection>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                            It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns<OnboardingIdentitySelection, NodeId, NodeId,
                            Func<CancellationToken, ValueTask<ByteString>>, CancellationToken>(
                            (identity, applicationId, certificateType, create, ct) =>
                            {
                                EnrollmentCertificateTypes.Add(applicationId, certificateType);
                                return Enrollments.Object.GetOrCreateAsync(identity, applicationId, create, ct);
                            });
                }
                Bootstrap = ByteString.From(bootstrap.RawData.AsSpan());
                Issued = ByteString.From(issued.RawData.AsSpan());
                m_csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(issued).AsSpan());
                m_otherCsr = ByteString.From(
                    DefaultCertificateFactory.Instance.CreateSigningRequest(bootstrap).AsSpan());
                Identity = externalAuthentication
                    ? OnboardingIdentitySelection.FromExternalAuthentication(Bootstrap, Product)
                    : new OnboardingIdentitySelection(
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
                Update = CreateSession(bootstrap);
                Proof = CreateSession(issued);
                InitialLease = Lease(Initial);
                Mock<IDeviceRegistrarPushConnection> updateLease = Lease(Update);
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
                        if (expected == Bootstrap)
                        {
                            if (BeforeUpdate != null)
                            {
                                await BeforeUpdate().ConfigureAwait(false);
                            }
                            return updateLease.Object;
                        }
                        Assert.That(expected, Is.EqualTo(Issued));
                        if (BeforeProof != null)
                        {
                            await BeforeProof().ConfigureAwait(false);
                        }
                        return proofLease.Object;
                    });
                Mock<IApplicationsDatabase> database = Database;
                database.Setup(value => value.FindApplications(It.IsAny<string>()))
                    .Returns<string>(uri =>
                    {
                        if (uri == ApplicationUri)
                        {
                            return m_record == null ? [] : [m_record];
                        }
                        return m_managedRecords.TryGetValue(uri, out ApplicationRecordDataType? found) ? [found] : [];
                    });
                database.Setup(value => value.RegisterApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Returns<ApplicationRecordDataType>(record =>
                    {
                        ApplicationRecordDataType stored = CoreUtils.Clone(record)!;
                        if (record.ApplicationUri == ApplicationUri)
                        {
                            stored.ApplicationId = ApplicationId;
                            m_record = stored;
                            return ApplicationId;
                        }
                        stored.ApplicationId = FindManaged(record.ApplicationUri!).Id;
                        m_managedRecords[record.ApplicationUri!] = stored;
                        return stored.ApplicationId;
                    });
                database.Setup(value => value.UpdateApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Returns<ApplicationRecordDataType>(record =>
                    {
                        if (record.ApplicationUri != ApplicationUri)
                        {
                            m_managedRecords[record.ApplicationUri!] = CoreUtils.Clone(record)!;
                        }
                        return record.ApplicationId;
                    });
                Authorization.Setup(value => value.RequireApplicationAsync(
                    Product, It.IsAny<ApplicationRecordDataType>(), string.Empty, It.IsAny<CancellationToken>()))
                    .Returns<string, ApplicationRecordDataType, string, CancellationToken>((_, record, _, _) =>
                    {
                        AuthorizedUris.Add(record.ApplicationUri!);
                        if (DeniedUris.Contains(record.ApplicationUri!))
                        {
                            throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
                        }
                        return default;
                    });
                ManagedApplicationChannels
                    .Setup(provider => provider.VerifyAsync(
                        It.IsAny<ApplicationDescription>(),
                        It.IsAny<DeviceRegistrarManagedApplicationTarget>(),
                        It.IsAny<ByteString>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<ApplicationDescription, DeviceRegistrarManagedApplicationTarget, ByteString,
                        CancellationToken>((_, _, expected, _) => ManagedApplicationProofs.Add(expected))
                    .Returns(new ValueTask());
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
                ExternalAuthentication.Setup(value => value.EvaluateAsync(
                    Bootstrap, It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<DeviceRegistrarExternalAuthenticationResult?>(
                        new DeviceRegistrarExternalAuthenticationResult(Product,
                            new DeviceRegistrarOnboardingDecision(Record(), RequiresSoftware,
                                RequiresSoftware ? new ManagerDescription
                                {
                                    PurposeUri = "urn:software-update",
                                    EndpointUrls = ["opc.tcp://software-manager:4840"]
                                } : null))));
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
                            if (BeforeCsrAsync != null)
                            {
                                await BeforeCsrAsync(ct).ConfigureAwait(false);
                            }
                            ByteString csr = await create(ct).ConfigureAwait(false);
                            Assert.That(csr, Is.EqualTo(m_csr));
                            if (RepeatCsr)
                            {
                                await create(ct).ConfigureAwait(false);
                            }
                            return Enrollment();
                        });
                Enrollments.Setup(provider => provider.FindAsync(Product, ApplicationId,
                    It.IsAny<CancellationToken>())).Returns(() =>
                        new ValueTask<DeviceRegistrarPushEnrollment?>(MissingRetained ? null : Enrollment()));
                ICertificateRequestIssuanceStore issuances = m_issuances.Object;
                m_issuances.Setup(store => store.GetOrCreateAsync(
                    ApplicationId, RequestId,
                    It.IsAny<Func<CancellationToken, ValueTask<CertificateRequestIssuance>>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<NodeId, NodeId, Func<CancellationToken, ValueTask<CertificateRequestIssuance>>,
                        CancellationToken>((_, _, create, ct) =>
                        MissingReceipt ? create(ct) : new ValueTask<CertificateRequestIssuance>(
                            new CertificateRequestIssuance(
                                "DefaultApplicationGroup",
                                nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                                EnrollmentMutation == "trailing-certificate"
                                    ? ByteString.From([.. Issued.ToArray(), 0])
                                    : Issued,
                                EnrollmentMutation == "private-key" ? m_csr : default, [Issued])));
                var validator = new OnboardingPushIdentityValidator(Identities.Object, Initial.Telemetry);
                IDeviceRegistrarCertificateLifecycle certificates = scopeCapability ? Lifecycle :
                    new Mock<IDeviceRegistrarCertificateLifecycle>(MockBehavior.Strict).Object;
                IDeviceRegistrarManagedApplicationProvider? applicationProvider =
                    managedApplicationProviders ? ManagedApplications.Object : null;
                IDeviceRegistrarManagedApplicationChannelVerifier? channelVerifier =
                    managedApplicationProviders ? ManagedApplicationChannels.Object : null;
                if (resolveFromServices)
                {
                    var services = new ServiceCollection();
                    services.AddSingleton(connections.Object);
                    services.AddSingleton(Enrollments.Object);
                    services.AddSingleton(Identities.Object);
                    services.AddSingleton(validator);
                    if (externalAuthentication)
                    {
                        services.AddSingleton(ExternalAuthentication.Object);
                    }
                    services.AddSingleton(policy.Object);
                    services.AddSingleton(database.Object);
                    services.AddSingleton<IDeviceRegistrarIdentityLifecycle>(Lifecycle);
                    services.AddSingleton(certificates);
                    services.AddSingleton(issuances);
                    services.AddSingleton<IDeviceRegistrarLifecycle>(Lifecycle);
                    services.AddSingleton(Authorization.Object);
                    if (applicationProvider != null)
                    {
                        services.AddSingleton(applicationProvider);
                    }
                    if (channelVerifier != null)
                    {
                        services.AddSingleton(channelVerifier);
                    }
                    services.AddSingleton(Initial.Telemetry);
                    services.AddSingleton<TimeProvider>(CleanupTime);
                    services.AddDeviceRegistrarPush();
                    m_services = services.BuildServiceProvider();
                    Processor = (DeviceRegistrarPushProcessor)m_services
                        .GetRequiredService<IDeviceRegistrarPushProcessor>();
                }
                else
                {
                    Func<DeviceRegistrarPushProcessor> processorFactory =
                        () => new DeviceRegistrarPushProcessor(connections.Object, Enrollments.Object,
                        validator, policy.Object, database.Object, Lifecycle, certificates,
                        issuances, Lifecycle, Authorization.Object, Initial.Telemetry, CleanupTime,
                        applicationProvider, channelVerifier,
                        externalAuthentication ? ExternalAuthentication.Object : null);
                    m_processorFactory = processorFactory;
                    Processor = processorFactory();
                }
            }

            /// <summary>
            /// Gets the concrete processor under test.
            /// </summary>
            public DeviceRegistrarPushProcessor Processor { get; private set; }

            /// <summary>
            /// Recreates the processor while retaining the enrollment and issuance stores across the simulated restart.
            /// </summary>
            public void RestartProcessor()
            {
                if (m_processorFactory == null)
                {
                    throw new InvalidOperationException(
                        "The service-owned processor cannot be restarted by this harness.");
                }
                Processor.Dispose();
                Processor = m_processorFactory();
            }

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
            /// Gets the independent certificate-transfer transport after committing the TrustList.
            /// </summary>
            public SessionRequestContextTestContext Update { get; }

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
            /// Gets the explicit external identity handoff boundary.
            /// </summary>
            public Mock<IDeviceRegistrarExternalAuthenticationProvider> ExternalAuthentication { get; } =
                new(MockBehavior.Strict);

            /// <summary>
            /// Gets the host's strict durable request boundary.
            /// </summary>
            public Mock<IDeviceRegistrarPushEnrollmentProvider> Enrollments { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the certificate types routed to the profile-aware enrollment provider.
            /// </summary>
            public Dictionary<NodeId, NodeId> EnrollmentCertificateTypes { get; } = [];

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
            /// Gets or sets a controlled asynchronous enrollment boundary before the device creates its CSR.
            /// </summary>
            public Func<CancellationToken, Task>? BeforeCsrAsync { get; set; }

            /// <summary>
            /// Gets or sets one concrete enrollment-evidence substitution.
            /// </summary>
            public string EnrollmentMutation { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets an explicit ApplyChanges transport failure.
            /// </summary>
            public Exception? ApplyError { get; set; }

            /// <summary>
            /// Gets or sets the one-based apply call that loses its response.
            /// </summary>
            public int ApplyErrorOnCall { get; set; } = 1;

            /// <summary>
            /// Gets the number of trust and certificate apply requests.
            /// </summary>
            public int ApplyCalls { get; private set; }

            /// <summary>
            /// Gets or sets a provider lease cleanup failure.
            /// </summary>
            public Exception? CleanupError { get; set; }

            /// <summary>
            /// Gets or sets work after processing finishes and before the provider releases its lease.
            /// </summary>
            public Func<ValueTask>? BeforeLeaseDisposal { get; set; }

            /// <summary>
            /// Gets the clock that advances cancellation cleanup without waiting for wall time.
            /// </summary>
            public FakeTimeProvider CleanupTime { get; } = new();

            /// <summary>
            /// Gets or sets a cancellation request that waits for its independent deadline.
            /// </summary>
            public Func<CancellationToken, ValueTask>? WaitForCancellation { get; set; }

            /// <summary>
            /// Gets or sets an explicit generated trust-file Write failure.
            /// </summary>
            public Exception? TrustWriteError { get; set; }

            /// <summary>
            /// Gets or sets an explicit generated trust-file Close failure.
            /// </summary>
            public Exception? TrustCloseError { get; set; }

            /// <summary>
            /// Gets or sets a DCA CloseAndUpdate failure raised after the TrustList is staged.
            /// </summary>
            public Exception? TrustStagedError { get; set; }

            /// <summary>
            /// Gets or sets a DCA UpdateCertificate failure raised after the certificate is staged.
            /// </summary>
            public Exception? CertificateStagedError { get; set; }

            /// <summary>
            /// Gets or sets a DCA CancelChanges failure.
            /// </summary>
            public Exception? CancelError { get; set; }

            /// <summary>
            /// Gets the number of DCA CancelChanges requests.
            /// </summary>
            public int CancelCalls { get; private set; }

            /// <summary>
            /// Gets whether the DCA holds a staged TrustList or certificate change.
            /// </summary>
            public bool HasPendingChanges => m_trustPending != null || m_certificatePending;

            /// <summary>
            /// Gets the committed TrustList the DCA reports.
            /// </summary>
            public TrustListDataType InstalledTrustList
                => Decode(m_trustBytes ?? Encode(Enrollment().TrustList));

            /// <summary>
            /// Replaces the committed TrustList the DCA reports.
            /// </summary>
            public void InstallTrustList(TrustListDataType trustList)
            {
                m_trustBytes = Encode(trustList);
            }

            /// <summary>
            /// Gets or sets a generation mutation immediately before CSR creation.
            /// </summary>
            public Action? BeforeCsr { get; set; }

            /// <summary>
            /// Gets or sets an assertion or lifecycle mutation immediately before installed-channel proof.
            /// </summary>
            public Func<ValueTask>? BeforeProof { get; set; }

            /// <summary>
            /// Gets or sets work after trust application and before the independent certificate-transfer session.
            /// </summary>
            public Func<ValueTask>? BeforeUpdate { get; set; }

            /// <summary>
            /// Gets the number of leases actually disposed.
            /// </summary>
            public int LeasesDisposed { get; private set; }

            /// <summary>
            /// Gets actual CSR operations rather than enrollment-provider calls.
            /// </summary>
            public int CsrCalls { get; private set; }

            /// <summary>
            /// Gets the per-application authorization policy invoked for every managed application.
            /// </summary>
            public Mock<IDeviceRegistrarAuthorization> Authorization { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the registry database mock used to verify authorization ordering.
            /// </summary>
            public Mock<IApplicationsDatabase> Database { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets managed application URIs the authorization policy rejects.
            /// </summary>
            public HashSet<string> DeniedUris { get; } = [];

            /// <summary>
            /// Gets managed application URIs submitted to the authorization policy, in order.
            /// </summary>
            public List<string> AuthorizedUris { get; } = [];

            /// <summary>
            /// Gets the scripted managed applications exposed by the device.
            /// </summary>
            public List<ManagedApplication> Managed { get; } = [];

            /// <summary>
            /// Provides authoritative managed-server endpoint metadata for configured test applications.
            /// </summary>
            public Mock<IDeviceRegistrarManagedApplicationProvider> ManagedApplications { get; } =
                new(MockBehavior.Strict);

            /// <summary>Records the installed certificate identities checked by the channel verifier.</summary>
            public List<ByteString> ManagedApplicationProofs { get; } = [];

            /// <summary>The mocked stack-backed managed-application channel verifier.</summary>
            public Mock<IDeviceRegistrarManagedApplicationChannelVerifier> ManagedApplicationChannels { get; } =
                new(MockBehavior.Strict);

            /// <summary>
            /// Exposes one scripted ApplicationConfiguration and its retained enrollment and issuance.
            /// </summary>
            public ManagedApplication AddManaged(
                string name,
                string applicationUri,
                Certificate certificate,
                ApplicationType type = ApplicationType.Client)
            {
                var application = new ManagedApplication(name, applicationUri, certificate, type);
                Managed.Add(application);
                if (type == ApplicationType.Client)
                {
                    ConfigureManagedTarget(application);
                }
                DeviceRegistrarPushEnrollment? retained = null;
                Enrollments.Setup(provider => provider.GetOrCreateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), application.Id,
                    It.IsAny<Func<CancellationToken, ValueTask<ByteString>>>(), It.IsAny<CancellationToken>()))
                    .Returns<OnboardingIdentitySelection, NodeId,
                        Func<CancellationToken, ValueTask<ByteString>>, CancellationToken>(
                        async (_, _, create, ct) =>
                        {
                            if (retained != null)
                            {
                                return retained;
                            }
                            if (application.RetainedCertificateTypeId is { } retainedCertificateTypeId)
                            {
                                retained = new DeviceRegistrarPushEnrollment(
                                    Identity,
                                    application.Id,
                                    application.RequestId,
                                    "DefaultApplicationGroup",
                                    retainedCertificateTypeId,
                                    application.CreateTrustList(),
                                    application.Csr);
                                return retained;
                            }
                            ByteString csr = await create(ct).ConfigureAwait(false);
                            Assert.That(csr, Is.EqualTo(application.Csr));
                            retained = new DeviceRegistrarPushEnrollment(Identity, application.Id,
                                application.RequestId, "DefaultApplicationGroup",
                                GetCertificateTypeName(application.EffectiveCertificateTypeId),
                                application.CreateTrustList(), csr);
                            return retained;
                        });
                Enrollments.Setup(provider => provider.FindAsync(
                    Product, application.Id, It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<DeviceRegistrarPushEnrollment?>(
                        MissingRetained ? null : retained));
                m_issuances.Setup(store => store.GetOrCreateAsync(
                    application.Id, application.RequestId,
                    It.IsAny<Func<CancellationToken, ValueTask<CertificateRequestIssuance>>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<CertificateRequestIssuance>(
                        new CertificateRequestIssuance(
                            "DefaultApplicationGroup",
                            application.RetainedCertificateTypeId ??
                                GetCertificateTypeName(application.EffectiveCertificateTypeId),
                            application.Certificate, default, [application.Certificate])));
                return application;
            }

            /// <summary>
            /// Adds the operator-owned URLs, certificate and endpoint authentication for one managed server.
            /// </summary>
            public DeviceRegistrarManagedApplicationTarget ConfigureManagedTarget(ManagedApplication application)
            {
                string url = $"opc.tcp://{application.Name}:4840";
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = url,
                    Server = new ApplicationDescription { ApplicationUri = application.ApplicationUri },
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                };
                var target = new DeviceRegistrarManagedApplicationTarget(
                    [url],
                    application.CurrentCertificate,
                    new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create()),
                    new UserIdentity(),
                    ["DA"])
                {
                    CertificateTypeId = new ExpandedNodeId(application.SelectedCertificateTypeId)
                };
                ManagedApplications.Setup(provider => provider.GetTargetAsync(
                    application.ApplicationUri, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<DeviceRegistrarManagedApplicationTarget>(target));
                return target;
            }

            /// <summary>
            /// Gets the issuance-store browse name for the RSA certificate types used by this scripted server.
            /// </summary>
            private static string GetCertificateTypeName(NodeId certificateTypeId)
            {
                if (certificateTypeId == Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    return nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
                }
                if (certificateTypeId == Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
                {
                    return nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType);
                }
                throw new ArgumentOutOfRangeException(nameof(certificateTypeId));
            }

            /// <summary>
            /// Gets the registered name submitted for a managed application, or null when it was not registered.
            /// </summary>
            public string? RegisteredName(ManagedApplication application)
            {
                return m_managedRecords.TryGetValue(application.ApplicationUri, out ApplicationRecordDataType? record)
                    ? record.ApplicationNames[0].Text
                    : null;
            }

            /// <summary>
            /// Gets the registered managed-application record.
            /// </summary>
            public ApplicationRecordDataType RegisteredRecord(ManagedApplication application)
            {
                return m_managedRecords[application.ApplicationUri];
            }

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
                Processor.Dispose();
                if (m_services != null)
                {
                    await m_services.DisposeAsync().ConfigureAwait(false);
                }
                await Initial.DisposeAsync().ConfigureAwait(false);
                await Update.DisposeAsync().ConfigureAwait(false);
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
                session.Handler = (request, ct) => RespondAsync(request, ct);
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
                    .Returns(Ua.ObjectIds.ServerConfiguration);
                lease.SetupGet(value => value.CertificateGroupId)
                    .Returns(Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup);
                lease.SetupGet(value => value.CertificateTypeId)
                    .Returns(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
                lease.SetupGet(value => value.TrustListId).Returns(new NodeId("trust-list", 1));
                lease.Setup(value => value.DisposeAsync()).Returns(async () =>
                {
                    LeasesDisposed++;
                    if (BeforeLeaseDisposal != null)
                    {
                        await BeforeLeaseDisposal().ConfigureAwait(false);
                    }
                    if (CleanupError != null)
                    {
                        throw CleanupError;
                    }
                });
                return lease;
            }

            /// <summary>
            /// Answers generated method resolution and typed method calls without bypassing session dispatch.
            /// </summary>
            private async ValueTask<IServiceResponse> RespondAsync(IServiceRequest request, CancellationToken ct)
            {
                if (request is BrowseRequest browse)
                {
                    return new BrowseResponse
                    {
                        Results = [new BrowseResult { References = Browse(browse.NodesToBrowse[0].NodeId) }]
                    };
                }
                Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("original-token", 0)));
                if (request is ReadRequest read)
                {
                    return new ReadResponse { Results = [new DataValue(ReadValue(read.NodesToRead[0].NodeId))] };
                }
                if (request is not CallRequest call)
                {
                    throw new InvalidOperationException("Unexpected push request.");
                }
                CallMethodRequest method = call.MethodsToCall[0];
                foreach (ManagedApplication application in Managed)
                {
                    if (method.ObjectId == application.Node || method.ObjectId == application.TrustList)
                    {
                        ArrayOf<Variant> results = await application.CallAsync(method).ConfigureAwait(false);
                        // ApplyChanges and CancelChanges act on every change queued in the session.
                        foreach (ManagedApplication queued in Managed)
                        {
                            if (method.MethodId == Ua.MethodIds.ServerConfigurationType_ApplyChanges)
                            {
                                queued.Commit();
                            }
                            else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CancelChanges)
                            {
                                queued.Discard();
                            }
                        }
                        return new CallResponse
                        {
                            Results = [new CallMethodResult { OutputArguments = results }]
                        };
                    }
                }
                CallMethods.Add(method.MethodId);
                ArrayOf<Variant> outputs = [];
                if (method.ObjectId == s_deviceId)
                {
                    ArrayOf<string> tickets = ["signed-ticket"];
                    outputs = [Variant.From(tickets)];
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CreateSigningRequest)
                {
                    Assert.That(method.InputArguments[3].TryGetValue(out bool regenerate), Is.True);
                    Assert.That(regenerate, Is.True);
                    Assert.That(method.InputArguments[4].TryGetValue(out ByteString nonce), Is.True);
                    Assert.That(nonce.Length, Is.GreaterThanOrEqualTo(32));
                    CsrCalls++;
                    outputs = [Variant.From(m_csr)];
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Open)
                {
                    Assert.That(method.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    if ((mode & (byte)OpenFileMode.Write) != 0)
                    {
                        m_trustWritten = [];
                    }
                    else
                    {
                        m_trustReadOffset = 0;
                    }
                    outputs = [Variant.From(42u)];
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Read)
                {
                    Assert.That(method.InputArguments[1].TryGetValue(out int length), Is.True);
                    m_trustBytes ??= Encode(Enrollment().TrustList);
                    int count = Math.Min(length, m_trustBytes.Length - m_trustReadOffset);
                    ByteString chunk = count > 0
                        ? ByteString.From(m_trustBytes.AsSpan(m_trustReadOffset, count))
                        : ByteString.Empty;
                    m_trustReadOffset += count;
                    outputs = [Variant.From(chunk)];
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Write)
                {
                    if (TrustWriteError != null)
                    {
                        throw TrustWriteError;
                    }
                    Assert.That(method.InputArguments[1].TryGetValue(out ByteString data), Is.True);
                    m_trustWritten.AddRange(data.Span.ToArray());
                }
                else if (method.MethodId == Ua.MethodIds.TrustListType_CloseAndUpdate)
                {
                    m_trustPending = m_trustWritten.ToArray();
                    if (TrustStagedError != null)
                    {
                        // The server staged the TrustList, but the reply is lost.
                        throw TrustStagedError;
                    }
                    outputs = [Variant.From(true)];
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_UpdateCertificate)
                {
                    if (!m_trustApplied)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateChainIncomplete);
                    }
                    m_certificatePending = true;
                    if (CertificateStagedError != null)
                    {
                        throw CertificateStagedError;
                    }
                    outputs = [Variant.From(true)];
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_ApplyChanges)
                {
                    ApplyCalls++;
                    if (ApplyError != null && ApplyCalls == ApplyErrorOnCall)
                    {
                        throw ApplyError;
                    }
                    if (m_trustPending != null)
                    {
                        m_trustBytes = m_trustPending;
                        m_trustApplied = true;
                    }
                    m_trustPending = null;
                    m_certificatePending = false;
                }
                else if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CancelChanges)
                {
                    CancelCalls++;
                    if (WaitForCancellation != null)
                    {
                        await WaitForCancellation(ct).ConfigureAwait(false);
                    }
                    if (CancelError != null)
                    {
                        throw CancelError;
                    }
                    m_trustPending = null;
                    m_certificatePending = false;
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Close && TrustCloseError != null)
                {
                    throw TrustCloseError;
                }
                return new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] };
            }

            /// <summary>
            /// Lists managed applications below the device object and the standard ManagedApplications folder,
            /// and the standard children of each scripted ApplicationConfiguration.
            /// </summary>
            private ArrayOf<ReferenceDescription> Browse(NodeId node)
            {
                var references = new List<ReferenceDescription>();
                foreach (ManagedApplication application in Managed)
                {
                    if ((node == s_deviceId && application.OnDevice) ||
                        (node == s_managedApplicationsFolderId && application.InFolder))
                    {
                        references.Add(Reference(application.Node, application.Name, NodeClass.Object,
                            Ua.ObjectTypeIds.ApplicationConfigurationType));
                    }
                    else if (node == application.Node)
                    {
                        references.Add(Reference(application.UriNode, Ua.BrowseNames.ApplicationUri,
                            NodeClass.Variable, Ua.VariableTypeIds.PropertyType));
                        references.Add(Reference(application.ProductNode, Ua.BrowseNames.ProductUri,
                            NodeClass.Variable, Ua.VariableTypeIds.PropertyType));
                        references.Add(Reference(application.TypeNode, Ua.BrowseNames.ApplicationType,
                            NodeClass.Variable, Ua.VariableTypeIds.PropertyType));
                        if (application.IncludeNames)
                        {
                            references.Add(Reference(application.NamesNode, Ua.BrowseNames.ApplicationNames,
                                NodeClass.Variable, Ua.VariableTypeIds.PropertyType));
                        }
                        references.Add(Reference(application.Groups, BrowseNames.CertificateGroups,
                            NodeClass.Object, Ua.ObjectTypeIds.CertificateGroupFolderType));
                    }
                    else if (node == application.Groups)
                    {
                        references.Add(Reference(application.Group, Ua.BrowseNames.DefaultApplicationGroup,
                            NodeClass.Object, Ua.ObjectTypeIds.CertificateGroupType));
                    }
                    else if (node == application.Group)
                    {
                        references.Add(Reference(application.TrustList, Ua.BrowseNames.TrustList,
                            NodeClass.Object, Ua.ObjectTypeIds.TrustListType));
                        references.Add(Reference(application.CertificateTypesNode, Ua.BrowseNames.CertificateTypes,
                            NodeClass.Variable, Ua.VariableTypeIds.PropertyType));
                    }
                }
                return references.ToArrayOf();
            }

            /// <summary>
            /// Reads scripted managed-application properties; every other value is the device's IsSingleton.
            /// </summary>
            private Variant ReadValue(NodeId node)
            {
                foreach (ManagedApplication application in Managed)
                {
                    if (node == application.UriNode)
                    {
                        return Variant.From(application.ApplicationUri);
                    }
                    if (node == application.ProductNode)
                    {
                        return Variant.From(ManagedProduct);
                    }
                    if (node == application.TypeNode)
                    {
                        return Variant.From((int)application.Type);
                    }
                    if (node == application.NamesNode)
                    {
                        ArrayOf<LocalizedText> names = [new LocalizedText(application.Name)];
                        return Variant.From(names);
                    }
                    if (node == application.CertificateTypesNode)
                    {
                        return Variant.From(application.CertificateTypes);
                    }
                }
                return Variant.From(Singleton);
            }

            /// <summary>
            /// Finds the scripted managed application that owns a registered URI.
            /// </summary>
            private ManagedApplication FindManaged(string applicationUri)
            {
                foreach (ManagedApplication application in Managed)
                {
                    if (application.ApplicationUri == applicationUri)
                    {
                        return application;
                    }
                }
                throw new InvalidOperationException("Unexpected managed application registration.");
            }

            /// <summary>
            /// Creates one forward hierarchical reference with a standard OPC UA browse name.
            /// </summary>
            private static ReferenceDescription Reference(
                NodeId target,
                string browseName,
                NodeClass nodeClass,
                NodeId typeDefinition)
            {
                return new ReferenceDescription
                {
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                    IsForward = true,
                    NodeId = target,
                    BrowseName = new QualifiedName(browseName, 0),
                    DisplayName = new LocalizedText(browseName),
                    NodeClass = nodeClass,
                    TypeDefinition = typeDefinition
                };
            }

            /// <summary>
            /// Creates retained request evidence matching the shared immutable receipt.
            /// </summary>
            private DeviceRegistrarPushEnrollment Enrollment()
            {
                var enrollment = new DeviceRegistrarPushEnrollment(Identity, ApplicationId, RequestId,
                    "DefaultApplicationGroup",
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All }, m_csr);
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
                    "missing-csr" => enrollment with { CertificateRequest = ByteString.Empty },
                    "malformed-csr" => enrollment with { CertificateRequest = ByteString.From(new byte[] { 1, 2, 3 }) },
                    "wrong-csr-key" => enrollment with { CertificateRequest = m_otherCsr },
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

            /// <summary>The standard ManagedApplications folder of a Part 21 device.</summary>
            private static readonly NodeId s_managedApplicationsFolderId = new(16706u);

            /// <summary>The product of every scripted managed application.</summary>
            private const string ManagedProduct = "urn:push:managed-product";

            /// <summary>The strict retained-issuance boundary shared by the DCA and managed applications.</summary>
            private readonly Mock<ICertificateRequestIssuanceStore> m_issuances = new(MockBehavior.Strict);

            /// <summary>Optional owning container for fluent-resolution deadline tests.</summary>
            private readonly ServiceProvider? m_services;

            /// <summary>Creates a new processor over the same durable dependency instances.</summary>
            private readonly Func<DeviceRegistrarPushProcessor>? m_processorFactory;

            /// <summary>Shared records registered for managed applications, keyed by ApplicationUri.</summary>
            private readonly Dictionary<string, ApplicationRecordDataType> m_managedRecords = [];

            /// <summary>The device-generated signing request returned over the admitted generation.</summary>
            private readonly ByteString m_csr;

            /// <summary>A correctly signed request for a different key of the same application.</summary>
            private readonly ByteString m_otherCsr;

            /// <summary>The shared application record created or seeded by this harness.</summary>
            private ApplicationRecordDataType? m_record;

            /// <summary>The committed DCA TrustList file content, or null for the enrollment TrustList.</summary>
            private byte[]? m_trustBytes;

            /// <summary>The staged DCA TrustList file content awaiting ApplyChanges, or null.</summary>
            private byte[]? m_trustPending;

            /// <summary>The bytes written to the open DCA TrustList writer.</summary>
            private List<byte> m_trustWritten = [];

            /// <summary>The position of the open DCA TrustList reader.</summary>
            private int m_trustReadOffset;

            /// <summary>Whether an accepted DCA certificate still needs ApplyChanges.</summary>
            private bool m_certificatePending;

            /// <summary>Whether UpdateCertificate can validate against committed issuer stores.</summary>
            private bool m_trustApplied;
        }

        /// <summary>
        /// Scripts one managed Client or Server ApplicationConfiguration with its own key pair, CSR,
        /// TrustList transfer, staged certificate and ApplyChanges semantics.
        /// </summary>
        private sealed class ManagedApplication
        {
            /// <summary>
            /// Creates an application whose standard children use node identifiers derived from its name.
            /// </summary>
            public ManagedApplication(string name, string applicationUri, Certificate certificate, ApplicationType type)
            {
                Name = name;
                ApplicationUri = applicationUri;
                Type = type;
                Node = new NodeId(name, 1);
                UriNode = new NodeId(name + ":uri", 1);
                ProductNode = new NodeId(name + ":product", 1);
                TypeNode = new NodeId(name + ":type", 1);
                NamesNode = new NodeId(name + ":names", 1);
                Groups = new NodeId(name + ":groups", 1);
                Group = new NodeId(name + ":group", 1);
                TrustList = new NodeId(name + ":trust-list", 1);
                CertificateTypesNode = new NodeId(name + ":certificate-types", 1);
                Id = new NodeId(name + ":application", 1);
                RequestId = new NodeId(name + ":request", 1);
                Certificate = ByteString.From(certificate.RawData.AsSpan());
                CurrentCertificate = Certificate;
                Csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(certificate).AsSpan());
            }

            /// <summary>
            /// Gets the browse and application name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the ApplicationUri reported by the configuration.
            /// </summary>
            public string ApplicationUri { get; }

            /// <summary>
            /// Gets the reported ApplicationType.
            /// </summary>
            public ApplicationType Type { get; }

            /// <summary>
            /// Gets the ApplicationConfiguration object.
            /// </summary>
            public NodeId Node { get; }

            /// <summary>
            /// Gets the ApplicationUri property.
            /// </summary>
            public NodeId UriNode { get; }

            /// <summary>
            /// Gets the ProductUri property.
            /// </summary>
            public NodeId ProductNode { get; }

            /// <summary>
            /// Gets the ApplicationType property.
            /// </summary>
            public NodeId TypeNode { get; }

            /// <summary>
            /// Gets the optional ApplicationNames property.
            /// </summary>
            public NodeId NamesNode { get; }

            /// <summary>
            /// Gets the CertificateGroups folder.
            /// </summary>
            public NodeId Groups { get; }

            /// <summary>
            /// Gets the DefaultApplicationGroup.
            /// </summary>
            public NodeId Group { get; }

            /// <summary>
            /// Gets the DefaultApplicationGroup TrustList.
            /// </summary>
            public NodeId TrustList { get; }

            /// <summary>
            /// Gets the DefaultApplicationGroup CertificateTypes property.
            /// </summary>
            public NodeId CertificateTypesNode { get; }

            /// <summary>
            /// Gets or sets the certificate types the DefaultApplicationGroup supports.
            /// </summary>
            public ArrayOf<NodeId> CertificateTypes { get; set; } =
                [Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType];

            /// <summary>
            /// Gets or sets the optional certificate type selected by the host for this application.
            /// </summary>
            public NodeId SelectedCertificateTypeId { get; set; }

            /// <summary>
            /// Gets or sets a retained issuance type to simulate a recovery after the host selection changed.
            /// </summary>
            public string? RetainedCertificateTypeId { get; set; }

            /// <summary>Gets the certificate type used by the scripted server when no host override is set.</summary>
            public NodeId EffectiveCertificateTypeId => SelectedCertificateTypeId.IsNull
                ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                : SelectedCertificateTypeId;

            /// <summary>Gets the certificate type requested by the processor's CSR call.</summary>
            public NodeId RequestedCertificateTypeId { get; private set; }

            /// <summary>Gets the certificate type submitted to UpdateCertificate.</summary>
            public NodeId UpdatedCertificateTypeId { get; private set; }

            /// <summary>
            /// Gets or sets whether GetCertificates rejects a null CertificateGroupId.
            /// </summary>
            public bool RejectNullGroup { get; set; }

            /// <summary>
            /// Gets or sets the error CancelChanges returns, for a server without transactions.
            /// </summary>
            public ServiceResultException? CancelError { get; set; }

            /// <summary>
            /// Gets or sets work after UpdateCertificate stages the credential.
            /// </summary>
            public Func<ValueTask>? OnUpdateCertificate { get; set; }

            /// <summary>
            /// Gets or sets work after CancelChanges succeeds.
            /// </summary>
            public Func<ValueTask>? OnCancelChanges { get; set; }

            /// <summary>
            /// Gets or sets work after CloseAndUpdate stages its TrustList.
            /// </summary>
            public Func<ValueTask>? OnCloseAndUpdate { get; set; }

            /// <summary>
            /// Gets or sets a CloseAndUpdate failure raised after the TrustList is staged.
            /// </summary>
            public Exception? TrustStagedError { get; set; }

            /// <summary>
            /// Gets or sets an UpdateCertificate failure raised after the certificate is staged.
            /// </summary>
            public Exception? CertificateStagedError { get; set; }

            /// <summary>
            /// Gets or sets how many CertificateTypeIds GetCertificates reports for an installed certificate,
            /// to script arrays that are not parallel to the single certificate.
            /// </summary>
            public int ReportedTypeCount { get; set; } = 1;

            /// <summary>
            /// Gets whether staged TrustList or certificate changes await ApplyChanges.
            /// </summary>
            public bool HasPendingChanges => m_trustPending != null || m_certificatePending;

            /// <summary>
            /// Gets the committed TrustList the application reports.
            /// </summary>
            public TrustListDataType InstalledTrustList => Decode(m_trustBytes);

            /// <summary>
            /// Gets the shared registry identifier assigned on registration.
            /// </summary>
            public NodeId Id { get; }

            /// <summary>
            /// Gets the retained request identifier.
            /// </summary>
            public NodeId RequestId { get; }

            /// <summary>
            /// Gets the issued certificate for this application's key.
            /// </summary>
            public ByteString Certificate { get; }

            /// <summary>
            /// Gets or sets the certificate currently installed in this application.
            /// </summary>
            public ByteString CurrentCertificate { get; set; }

            /// <summary>
            /// Gets the application-generated signing request.
            /// </summary>
            public ByteString Csr { get; }

            /// <summary>
            /// Gets or sets whether the application exposes the optional ApplicationNames.
            /// </summary>
            public bool IncludeNames { get; set; } = true;

            /// <summary>
            /// Gets or sets whether the device object references the application.
            /// </summary>
            public bool OnDevice { get; set; } = true;

            /// <summary>
            /// Gets or sets whether the ManagedApplications folder references the application.
            /// </summary>
            public bool InFolder { get; set; }

            /// <summary>
            /// Gets or sets whether the application already uses the issued certificate.
            /// </summary>
            public bool Installed { get; set; }

            /// <summary>
            /// Gets the methods called on this application, in order.
            /// </summary>
            public List<NodeId> Calls { get; } = [];

            /// <summary>
            /// Gets the number of signing requests the application created.
            /// </summary>
            public int CsrCalls { get; private set; }

            /// <summary>
            /// Creates the TrustList that trusts this application's issued certificate.
            /// </summary>
            public TrustListDataType CreateTrustList()
            {
                return new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [Certificate]
                };
            }

            /// <summary>
            /// Replaces the committed TrustList the application reports.
            /// </summary>
            public void InstallTrustList(TrustListDataType trustList)
            {
                m_trustBytes = Encode(trustList);
                m_trustApplied = true;
            }

            /// <summary>
            /// Commits the staged TrustList and certificate, as ApplyChanges does for the whole session.
            /// </summary>
            public void Commit()
            {
                if (m_trustPending != null)
                {
                    m_trustBytes = m_trustPending;
                    m_trustApplied = true;
                }
                if (m_certificatePending)
                {
                    CurrentCertificate = m_pendingCertificate;
                    Installed = true;
                }
                Discard();
            }

            /// <summary>
            /// Discards the staged TrustList and certificate, as CancelChanges does for the whole session.
            /// </summary>
            public void Discard()
            {
                m_trustPending = null;
                m_certificatePending = false;
            }

            /// <summary>
            /// Completes a remote staging request after its client has already observed a timeout.
            /// </summary>
            public void StageLateChange(string stage)
            {
                switch (stage)
                {
                    case "trust":
                        m_trustPending = m_written.ToArray();
                        break;
                    case "certificate":
                        m_certificatePending = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(stage));
                }
            }

            /// <summary>
            /// Applies one generated method call with staged TrustList and certificate semantics.
            /// </summary>
            public async ValueTask<ArrayOf<Variant>> CallAsync(CallMethodRequest method)
            {
                Calls.Add(method.MethodId);
                if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CreateSigningRequest)
                {
                    CsrCalls++;
                    Assert.That(method.InputArguments[1].TryGetValue(out NodeId requestedType), Is.True);
                    RequestedCertificateTypeId = requestedType;
                    return [Variant.From(Csr)];
                }
                if (method.MethodId == Ua.MethodIds.ServerConfigurationType_GetCertificates)
                {
                    NodeId group = method.InputArguments[0].TryGetValue(out NodeId value) ? value : NodeId.Null;
                    if (group.IsNull)
                    {
                        if (RejectNullGroup)
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                        }
                    }
                    else
                    {
                        Assert.That(group, Is.EqualTo(Group));
                    }
                    var reported = new List<NodeId>();
                    bool reportsCertificate = Installed ||
                        Type is ApplicationType.Server or ApplicationType.ClientAndServer;
                    for (int index = 0; reportsCertificate && index < ReportedTypeCount; index++)
                    {
                        reported.Add(EffectiveCertificateTypeId);
                    }
                    ArrayOf<NodeId> types = reported.ToArrayOf();
                    ArrayOf<ByteString> certificates = reportsCertificate ? [CurrentCertificate] :
                        ArrayOf<ByteString>.Empty;
                    return [Variant.From(types), Variant.From(certificates)];
                }
                if (method.MethodId == Ua.MethodIds.FileType_Open)
                {
                    Assert.That(method.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    if ((mode & (byte)OpenFileMode.Write) != 0)
                    {
                        m_written = [];
                    }
                    else
                    {
                        m_readOffset = 0;
                    }
                    return [Variant.From(42u)];
                }
                if (method.MethodId == Ua.MethodIds.FileType_Read)
                {
                    Assert.That(method.InputArguments[1].TryGetValue(out int length), Is.True);
                    int count = Math.Min(length, m_trustBytes.Length - m_readOffset);
                    ByteString chunk = count > 0
                        ? ByteString.From(m_trustBytes.AsSpan(m_readOffset, count))
                        : ByteString.Empty;
                    m_readOffset += count;
                    return [Variant.From(chunk)];
                }
                if (method.MethodId == Ua.MethodIds.FileType_Write)
                {
                    Assert.That(method.InputArguments[1].TryGetValue(out ByteString data), Is.True);
                    m_written.AddRange(data.Span.ToArray());
                    return [];
                }
                if (method.MethodId == Ua.MethodIds.TrustListType_CloseAndUpdate)
                {
                    m_trustPending = m_written.ToArray();
                    if (OnCloseAndUpdate != null)
                    {
                        await OnCloseAndUpdate().ConfigureAwait(false);
                    }
                    if (TrustStagedError != null)
                    {
                        throw TrustStagedError;
                    }
                    return [Variant.From(true)];
                }
                if (method.MethodId == Ua.MethodIds.ServerConfigurationType_UpdateCertificate)
                {
                    if (!m_trustApplied)
                    {
                        throw new ServiceResultException(StatusCodes.BadCertificateChainIncomplete);
                    }
                    m_certificatePending = true;
                    Assert.That(method.InputArguments[1].TryGetValue(out NodeId updatedType), Is.True);
                    UpdatedCertificateTypeId = updatedType;
                    Assert.That(method.InputArguments[2].TryGetValue(out m_pendingCertificate), Is.True);
                    if (OnUpdateCertificate != null)
                    {
                        await OnUpdateCertificate().ConfigureAwait(false);
                    }
                    if (CertificateStagedError != null)
                    {
                        throw CertificateStagedError;
                    }
                    return [Variant.From(true)];
                }
                if (method.MethodId == Ua.MethodIds.ServerConfigurationType_CancelChanges)
                {
                    if (CancelError != null)
                    {
                        throw CancelError;
                    }
                    if (OnCancelChanges != null)
                    {
                        await OnCancelChanges().ConfigureAwait(false);
                    }
                }
                return [];
            }

            /// <summary>The committed TrustList file content.</summary>
            private byte[] m_trustBytes = Encode(new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All });

            /// <summary>The staged TrustList file content awaiting ApplyChanges, or null.</summary>
            private byte[]? m_trustPending;

            /// <summary>The bytes written to the open TrustList writer.</summary>
            private List<byte> m_written = [];

            /// <summary>The position of the open TrustList reader.</summary>
            private int m_readOffset;

            /// <summary>Whether UpdateCertificate can validate against the committed TrustList.</summary>
            private bool m_trustApplied;

            /// <summary>Whether an accepted certificate still needs ApplyChanges.</summary>
            private bool m_certificatePending;

            /// <summary>The certificate staged for the next ApplyChanges call.</summary>
            private ByteString m_pendingCertificate;
        }

        /// <summary>The first managed application's URI.</summary>
        private const string ManagedUriA = "urn:push:managed:a";

        /// <summary>The second managed application's URI.</summary>
        private const string ManagedUriB = "urn:push:managed:b";

        /// <summary>The message context used to encode scripted TrustList files.</summary>
        private static readonly ServiceMessageContext s_context =
            ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

        /// <summary>
        /// Expected discovery URLs retained for rejected authorization.
        /// </summary>
        private static readonly string[] s_blockedDiscoveryUrls = ["opc.tcp://blocked.example.test:4840"];

        /// <summary>
        /// Expected capabilities retained for authorization.
        /// </summary>
        private static readonly string[] s_authorizedCapabilities = ["DA", "AC"];

        /// <summary>
        /// Expected discovery URLs retained across asynchronous endpoint proof.
        /// </summary>
        private static readonly string[] s_managedDiscoveryUrls = ["https://discovery.example.test/managed"];

        /// <summary>
        /// Expected capabilities of the managed server.
        /// </summary>
        private static readonly string[] s_serverCapabilities = ["DA"];

        /// <summary>The fixture-owned bootstrap identity.</summary>
        private Certificate m_bootstrap = null!;

        /// <summary>The fixture-owned issued identity.</summary>
        private Certificate m_issued = null!;

        /// <summary>The fixture-owned first managed application identity.</summary>
        private Certificate m_managedA = null!;

        /// <summary>The fixture-owned second managed application identity.</summary>
        private Certificate m_managedB = null!;
    }
}
