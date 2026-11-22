// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Deterministic tests of the independent pull-DCA workflow and fluent registration.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullDcaWorkflowTests
    {
        /// <summary>
        /// An external DCA proceeds directly to managers and ordinary managed-application signing and installation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalHandoffSkipsTicketSelectionAndReusesManagedApplicationProvisioning(bool managed)
        {
            var fixture = new Fixture();
            PullDcaManagedApplication application = CreateApplication();
            PullDcaExternalAuthenticationResult result = await fixture.Create(includeSoftware: false)
                .RunExternalAuthenticationAsync(kRegistrar, managed ? [application] : []).ConfigureAwait(false);

            Assert.That(fixture.Calls.Take(2), Is.EqualTo(s_connectManagersCalls));
            Assert.That(fixture.Calls, Does.Not.Contain("provide"));
            Assert.That(fixture.Calls, Does.Not.Contain("identity"));
            Assert.That(fixture.Calls, Does.Not.Contain("software"));
            Assert.That(fixture.Calls, Does.Not.Contain("reconnect"));
            Assert.That(result.Managers, Is.EqualTo(fixture.Connection.Managers));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(managed ? 1 : 0));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(managed ? new[] { kManagedId } : []));
            if (managed)
            {
                Assert.That(result.ManagedApplications[0].ApplicationId, Is.EqualTo(kManagedId));
                Assert.That(result.ManagedApplications[0].Credentials.Certificate, Is.EqualTo(kCertificate));
                Assert.That(fixture.Connection.RegisteredApplication!.Application,
                    Is.Not.SameAs(application.Application));
            }
        }

        /// <summary>
        /// A failed registrar admission cannot reach registration or installation.
        /// </summary>
        [Test]
        public async Task ExternalHandoffPropagatesManagerAdmissionFailureBeforeAnyCertificateRequest()
        {
            var fixture = new Fixture();
            fixture.FailingOperation = "managers";

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunExternalAuthenticationAsync(kRegistrar, [CreateApplication()])
                    .ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Is.EqualTo(s_connectManagersCalls));
        }

        [Test]
        public async Task PullManagementProvesIdentityCompletesGateAndProvisionsEveryApplicationInOrder()
        {
            var fixture = new Fixture();
            var manager = new ManagerDescription
            {
                IsRequired = true,
                PurposeUri = "urn:software:update",
                EndpointUrls = ["opc.tcp://update:4840"]
            };
            fixture.Connection.Selection = fixture.Connection.Selection with { SoftwareUpdateManager = manager };
            var progress = new ProgressRecorder();
            PullDcaManagedApplication application = CreateApplication();
            PullDcaRequest request = CreateRequest() with { ManagedApplications = [application] };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request, progress)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(s_fullWorkflowCalls));
            Assert.That(fixture.Host.ActivatedIdentity, Is.EqualTo(kIdentity));
            Assert.That(fixture.Host.SoftwareIdentity, Is.Not.SameAs(result.Identity));
            Assert.That(fixture.Host.SoftwareIdentity!.ApplicationId, Is.EqualTo(result.Identity.ApplicationId));
            Assert.That(fixture.Host.SoftwareIdentity.SelectedIdentity, Is.EqualTo(result.Identity.SelectedIdentity));
            Assert.That(fixture.Host.SoftwareManager, Is.Not.SameAs(manager));
            Assert.That(fixture.Host.SoftwareManager!.PurposeUri, Is.EqualTo(manager.PurposeUri));
            Assert.That(fixture.Host.SoftwareRegistrar, Is.EqualTo(kRegistrar));
            Assert.That(fixture.Connection.Offer, Is.Not.SameAs(request.IdentityOffer));
            Assert.That(fixture.Connection.Offer, Is.EqualTo(request.IdentityOffer));
            Assert.That(fixture.Connection.RegisteredApplication, Is.Not.SameAs(application));
            Assert.That(fixture.Connection.RegisteredApplication!.Application, Is.Not.SameAs(application.Application));
            Assert.That(fixture.Connection.RegisteredApplication.Application.ApplicationUri,
                Is.EqualTo(application.Application.ApplicationUri));
            Assert.That(fixture.Connection.RegisteredApplication.ProtocolUri, Is.EqualTo(application.ProtocolUri));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.DcaCredentials.TrustList, Is.Not.SameAs(fixture.Connection.TrustList));
            Assert.That(result.Managers, Is.EqualTo(fixture.Connection.Managers));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(1));
            Assert.That(result.ManagedApplications[0].ApplicationId, Is.EqualTo(kManagedId));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId, kManagedId }));
            Assert.That(progress.Items[^1].Stage, Is.EqualTo(PullDcaStage.Completed));
            PullDcaProgress pending = progress.Items.First(p => p.Stage == PullDcaStage.FinishingCertificate);
            Assert.That(pending.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(pending.RequestId, Is.EqualTo(kRequestId));
        }

        [Test]
        public async Task ProgressCannotMutateOpaqueIdentifiersUsedForCertificateCompletion()
        {
            var fixture = new Fixture();
            var applicationId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1);
            var requestId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1);
            int mutations = 0;
            var progress = new ProgressRecorder
            {
                OnReport = value =>
                {
                    if (value.Stage == PullDcaStage.FinishingCertificate)
                    {
                        MutateOpaqueIdentifier(value.ApplicationId);
                        MutateOpaqueIdentifier(value.RequestId);
                        mutations++;
                    }
                }
            };

            await fixture.Create().CompleteCertificateRequestAsync(
                kRegistrar, applicationId, requestId, kProfile, progress).ConfigureAwait(false);

            Assert.That(mutations, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { requestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
        }

        [Test]
        public async Task IdentityProviderCannotMutateSelectedIdentity()
        {
            var fixture = new Fixture();
            fixture.Host.OnActivate = MutateBytes;

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(result.Identity.SelectedIdentity, Is.EqualTo(kIdentity));
        }

        [Test]
        public async Task SoftwareUpdateProviderCannotMutateActiveRegistrarOrIdentity()
        {
            var registrar = kRegistrar with
            {
                RegistrarId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1),
                CertificateDirectoryId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1)
            };
            var applicationId = new NodeId(ByteString.From(new byte[] { 5, 6 }), 1);
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                ApplicationId = applicationId,
                SoftwareUpdateManager = new ManagerDescription()
            };
            fixture.Host.OnSoftwareUpdate = (receivedRegistrar, identity, _) =>
            {
                MutateOpaqueIdentifier(receivedRegistrar.RegistrarId);
                MutateOpaqueIdentifier(receivedRegistrar.CertificateDirectoryId);
                MutateOpaqueIdentifier(identity.ApplicationId);
                MutateBytes(identity.SelectedIdentity);
            };

            PullDcaResult result = await fixture.Create().RunAsync(registrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(fixture.ConnectedRegistrar, Is.EqualTo(registrar));
            Assert.That(result.Identity.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(result.Identity.SelectedIdentity, Is.EqualTo(kIdentity));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
        }

        /// <summary>
        /// Connection and provider mutations do not alter the selected software-update manager.
        /// </summary>
        [Test]
        public async Task SoftwareUpdateManagerIsDetachedFromConnectionAndProvider()
        {
            var fixture = new Fixture();
            var translations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["en"] = "Manager {0}"
            };
            object[] arguments = ["original"];
            string[] endpoints = ["opc.tcp://original:4840"];
            var manager = new ManagerDescription
            {
                Name = new LocalizedText(translations, new TranslationInfo("manager", "en", "Manager {0}", arguments)),
                IsRequired = true,
                PurposeUri = "urn:original:purpose",
                ProtocolUri = "urn:original:protocol",
                EndpointUrls = endpoints
            };
            fixture.Connection.Selection = fixture.Connection.Selection with { SoftwareUpdateManager = manager };
            fixture.Host.OnActivate = _ =>
            {
                translations["en"] = "Changed {0}";
                arguments[0] = "changed";
                endpoints[0] = "opc.tcp://changed:4840";
                manager.PurposeUri = "urn:changed:purpose";
            };
            fixture.Host.OnSoftwareUpdate = (_, identity, receivedManager) =>
            {
                Assert.That(identity.SoftwareUpdateManager, Is.SameAs(receivedManager));
                Assert.That(receivedManager.Name.FilterByPreferredLocales(["en"]).Text,
                    Is.EqualTo("Manager original"));
                Assert.That(receivedManager.EndpointUrls[0], Is.EqualTo("opc.tcp://original:4840"));
                Assert.That(receivedManager.PurposeUri, Is.EqualTo("urn:original:purpose"));
                receivedManager.Name = new LocalizedText("Provider changed");
                receivedManager.EndpointUrls = ["opc.tcp://provider:4840"];
                receivedManager.PurposeUri = "urn:provider:purpose";
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            ManagerDescription selected = result.Identity.SoftwareUpdateManager!;
            Assert.That(selected.Name.FilterByPreferredLocales(["en"]).Text, Is.EqualTo("Manager original"));
            Assert.That(selected.EndpointUrls[0], Is.EqualTo("opc.tcp://original:4840"));
            Assert.That(selected.PurposeUri, Is.EqualTo("urn:original:purpose"));
            Assert.That(selected.ProtocolUri, Is.EqualTo("urn:original:protocol"));
            Assert.That(selected.IsRequired, Is.True);
        }

        /// <summary>
        /// Extended manager descriptions are not silently truncated during snapshotting.
        /// </summary>
        [Test]
        public async Task DerivedSoftwareUpdateManagerIsRejectedBeforeActivation()
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                SoftwareUpdateManager = new DerivedManagerDescription()
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Calls, Is.EqualTo(s_connectProvideCalls));
        }

        /// <summary>
        /// The connection and software-update provider cannot mutate the retained ticket or its nested data.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingTicketIsDetachedFromConnectionAndProvider(bool composite)
        {
            var fixture = new Fixture();
            byte[] authorityBytes = [1, 2];
            byte[] issuerBytes = [3, 4];
            string[] devices = ["urn:device"];
            string[] composites = ["urn:composite"];
            BaseTicketType ticket = composite
                ? new CompositeIdentityTicketType
                {
                    CompositeInstanceUri = "urn:original",
                    Devices = devices,
                    Composites = composites
                }
                : new DeviceIdentityTicketType { ProductInstanceUri = "urn:original" };
            ticket.ManufacturerName = "manufacturer";
            ticket.ModelName = "model";
            ticket.ModelVersion = "model version";
            ticket.HardwareRevision = "hardware";
            ticket.SoftwareRevision = "software";
            ticket.SerialNumber = "original";
            ticket.ManufactureDate = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            ticket.Authorities = [new CertificateAuthorityType
            {
                AuthorityCertificate = ByteString.From(authorityBytes),
                IssuerCertificates = [ByteString.From(issuerBytes)]
            }];
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                MatchingTicket = ticket,
                SoftwareUpdateManager = new ManagerDescription()
            };
            fixture.Host.OnActivate = _ =>
            {
                ticket.SerialNumber = "connection changed";
                authorityBytes[0] = 9;
                issuerBytes[0] = 9;
                devices[0] = "urn:connection";
                composites[0] = "urn:connection";
            };
            fixture.Host.OnSoftwareUpdate = (_, identity, _) =>
            {
                BaseTicketType received = identity.MatchingTicket;
                Assert.That(received.SerialNumber, Is.EqualTo("original"));
                Assert.That(received.Authorities[0].AuthorityCertificate,
                    Is.EqualTo(ByteString.From(new byte[] { 1, 2 })));
                Assert.That(received.Authorities[0].IssuerCertificates[0],
                    Is.EqualTo(ByteString.From(new byte[] { 3, 4 })));
                if (received is CompositeIdentityTicketType receivedComposite)
                {
                    Assert.That(receivedComposite.Devices[0], Is.EqualTo("urn:device"));
                    Assert.That(receivedComposite.Composites[0], Is.EqualTo("urn:composite"));
                    receivedComposite.Devices = ["urn:provider"];
                }
                received.SerialNumber = "provider changed";
                MutateBytes(received.Authorities[0].AuthorityCertificate);
                MutateBytes(received.Authorities[0].IssuerCertificates[0]);
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            BaseTicketType selected = result.Identity.MatchingTicket;
            Assert.That(selected.SerialNumber, Is.EqualTo("original"));
            Assert.That(selected.ManufacturerName, Is.EqualTo("manufacturer"));
            Assert.That(selected.ModelName, Is.EqualTo("model"));
            Assert.That(selected.ModelVersion, Is.EqualTo("model version"));
            Assert.That(selected.HardwareRevision, Is.EqualTo("hardware"));
            Assert.That(selected.SoftwareRevision, Is.EqualTo("software"));
            Assert.That(selected.ManufactureDate, Is.EqualTo(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(selected.Authorities[0].AuthorityCertificate,
                Is.EqualTo(ByteString.From(new byte[] { 1, 2 })));
            Assert.That(selected.Authorities[0].IssuerCertificates[0],
                Is.EqualTo(ByteString.From(new byte[] { 3, 4 })));
            if (composite)
            {
                Assert.That(selected, Is.TypeOf<CompositeIdentityTicketType>());
                var selectedComposite = (CompositeIdentityTicketType)selected;
                Assert.That(selectedComposite.CompositeInstanceUri, Is.EqualTo("urn:original"));
                Assert.That(selectedComposite.Devices[0], Is.EqualTo("urn:device"));
                Assert.That(selectedComposite.Composites[0], Is.EqualTo("urn:composite"));
            }
            else
            {
                Assert.That(selected, Is.TypeOf<DeviceIdentityTicketType>());
                Assert.That(((DeviceIdentityTicketType)selected).ProductInstanceUri, Is.EqualTo("urn:original"));
            }
        }

        /// <summary>
        /// Ticket and authority extensions must not be silently truncated.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DerivedTicketOrAuthorityIsRejectedBeforeActivation(bool derivedAuthority)
        {
            var fixture = new Fixture();
            BaseTicketType ticket = derivedAuthority
                ? new DeviceIdentityTicketType { Authorities = [new DerivedCertificateAuthority()] }
                : new DerivedDeviceIdentityTicket();
            fixture.Connection.Selection = fixture.Connection.Selection with { MatchingTicket = ticket };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Calls, Is.EqualTo(s_connectProvideCalls));
        }

        /// <summary>
        /// An invalid null ticket authority is rejected before identity activation.
        /// </summary>
        [Test]
        public async Task NullTicketAuthorityIsRejectedBeforeActivation()
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                MatchingTicket = new DeviceIdentityTicketType { Authorities = [null!] }
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Calls, Is.EqualTo(s_connectProvideCalls));
        }

        /// <summary>
        /// Ticket array snapshots preserve the values exposed by generated setters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingTicketPreservesGeneratedArrayValues(bool isNull)
        {
            var fixture = new Fixture();
            var ticket = new CompositeIdentityTicketType
            {
                Authorities = isNull ? default : ArrayOf<CertificateAuthorityType>.Empty,
                Devices = isNull ? default : ArrayOf<string>.Empty,
                Composites = isNull ? default : ArrayOf<string>.Empty
            };
            bool authoritiesAreNull = ticket.Authorities.IsNull;
            bool devicesAreNull = ticket.Devices.IsNull;
            bool compositesAreNull = ticket.Composites.IsNull;
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                MatchingTicket = ticket,
                SoftwareUpdateManager = new ManagerDescription()
            };
            fixture.Host.OnSoftwareUpdate = (_, identity, _) =>
            {
                var received = (CompositeIdentityTicketType)identity.MatchingTicket;
                Assert.That(received.Authorities.IsNull, Is.EqualTo(authoritiesAreNull));
                Assert.That(received.Devices.IsNull, Is.EqualTo(devicesAreNull));
                Assert.That(received.Composites.IsNull, Is.EqualTo(compositesAreNull));
                Assert.That(received.Authorities.Count, Is.Zero);
                Assert.That(received.Devices.Count, Is.Zero);
                Assert.That(received.Composites.Count, Is.Zero);
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            var selected = (CompositeIdentityTicketType)result.Identity.MatchingTicket;
            Assert.That(selected.Authorities.IsNull, Is.EqualTo(authoritiesAreNull));
            Assert.That(selected.Devices.IsNull, Is.EqualTo(devicesAreNull));
            Assert.That(selected.Composites.IsNull, Is.EqualTo(compositesAreNull));
            Assert.That(selected.Authorities.Count, Is.Zero);
            Assert.That(selected.Devices.Count, Is.Zero);
            Assert.That(selected.Composites.Count, Is.Zero);
        }

        /// <summary>
        /// Issuer array snapshots preserve the values exposed by generated setters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MatchingTicketPreservesGeneratedAuthorityIssuers(bool isNull)
        {
            var fixture = new Fixture();
            var authority = new CertificateAuthorityType
            {
                IssuerCertificates = isNull ? default : ArrayOf<ByteString>.Empty
            };
            bool issuersAreNull = authority.IssuerCertificates.IsNull;
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                MatchingTicket = new DeviceIdentityTicketType
                {
                    Authorities = [authority]
                },
                SoftwareUpdateManager = new ManagerDescription()
            };
            fixture.Host.OnSoftwareUpdate = (_, identity, _) =>
            {
                ArrayOf<ByteString> issuers = identity.MatchingTicket.Authorities[0].IssuerCertificates;
                Assert.That(issuers.IsNull, Is.EqualTo(issuersAreNull));
                Assert.That(issuers.Count, Is.Zero);
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(
                result.Identity.MatchingTicket.Authorities[0].IssuerCertificates.IsNull, Is.EqualTo(issuersAreNull));
            Assert.That(result.Identity.MatchingTicket.Authorities[0].IssuerCertificates.Count, Is.Zero);
        }

        [Test]
        public async Task SigningRequestProviderCannotMutateActiveApplicationOrProfile()
        {
            var applicationId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1);
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(new byte[] { 3, 4 }), 1),
                new NodeId(ByteString.From(new byte[] { 5, 6 }), 1));
            var fixture = new Fixture { ExpectedProfile = profile };
            fixture.Connection.Selection = fixture.Connection.Selection with { ApplicationId = applicationId };
            fixture.Host.OnCreateSigningRequest = (receivedApplication, receivedProfile) =>
            {
                MutateOpaqueIdentifier(receivedApplication);
                MutateOpaqueIdentifier(receivedProfile.CertificateGroupId);
                MutateOpaqueIdentifier(receivedProfile.CertificateTypeId);
            };

            PullDcaResult result = await fixture.Create().RunAsync(
                kRegistrar, CreateRequest() with { DcaCertificateProfile = profile }).ConfigureAwait(false);

            Assert.That(result.Identity.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
        }

        [Test]
        public async Task SigningKeyEvidenceProviderCannotMutateDispatchedRequest()
        {
            var applicationId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1);
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(new byte[] { 3, 4 }), 1),
                new NodeId(ByteString.From(new byte[] { 5, 6 }), 1));
            var fixture = new Fixture { ExpectedProfile = profile };
            var binding = new PullDcaSigningBinding(
                kRegistrar, applicationId, kRequestId, profile, kCsr, kCertificate,
                default, default, string.Empty, default, Guid.NewGuid());
            fixture.Connection = new FakeBoundConnection(fixture, binding);
            fixture.Connection.Selection = fixture.Connection.Selection with { ApplicationId = applicationId };
            fixture.Host.OnKeyEvidence = (receivedApplication, receivedProfile, csr) =>
            {
                MutateOpaqueIdentifier(receivedApplication);
                MutateOpaqueIdentifier(receivedProfile.CertificateGroupId);
                MutateOpaqueIdentifier(receivedProfile.CertificateTypeId);
                MutateBytes(csr);
            };
            var journal = new Mock<IPullDcaSigningJournal>(MockBehavior.Strict);
            journal.Setup(value => value.GetReceivedAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PullDcaCredentials?>((PullDcaCredentials?)null));
            journal.Setup(value => value.RecordReceivedAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<PullDcaCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            journal.Setup(value => value.RecordInstalledAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(store => store.GetOrCreateAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<PullDcaCertificateProfile>(),
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    PullDcaRegistrar _, NodeId _, NodeId _, PullDcaCertificateProfile _,
                    Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive, CancellationToken ct) =>
                    receive(ct));

            PullDcaResult result = await fixture.Create(receipts: receipts.Object, signingJournal: journal.Object)
                .RunAsync(kRegistrar, CreateRequest() with { DcaCertificateProfile = profile }).ConfigureAwait(false);

            Assert.That(result.Identity.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CredentialInstallerCannotMutateWorkflowIdentity(bool newKeyPair)
        {
            var applicationId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1);
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with { ApplicationId = applicationId };
            fixture.Host.OnInstall = MutateOpaqueIdentifier;
            fixture.Host.OnInstallNewKeyPair = (receivedApplication, _) => MutateOpaqueIdentifier(receivedApplication);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = newKeyPair
                    ? PullDcaCertificateRequestMode.NewKeyPairRequest
                    : PullDcaCertificateRequestMode.SigningRequest
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(result.Identity.ApplicationId, Is.EqualTo(applicationId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CredentialInstallerCannotMutateWorkflowCredentials(bool newKeyPair)
        {
            var fixture = new Fixture();
            fixture.Connection.NewKeyPairCertificate = kCertificate.Copy();
            fixture.Connection.TrustList.TrustedCertificates = [ByteString.From(new byte[] { 1 })];
            fixture.Connection.TrustList.TrustedCrls = [ByteString.From(new byte[] { 2 })];
            fixture.Connection.TrustList.IssuerCertificates = [ByteString.From(new byte[] { 3 })];
            fixture.Connection.TrustList.IssuerCrls = [ByteString.From(new byte[] { 4 })];
            fixture.Host.OnCredentialsInstalled = credentials =>
            {
                MutateBytes(credentials.Certificate);
                MutateBytes(credentials.IssuerCertificates[0]);
                MutateBytes(credentials.TrustList.TrustedCertificates[0]);
                MutateBytes(credentials.TrustList.TrustedCrls[0]);
                MutateBytes(credentials.TrustList.IssuerCertificates[0]);
                MutateBytes(credentials.TrustList.IssuerCrls[0]);
            };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = newKeyPair
                    ? PullDcaCertificateRequestMode.NewKeyPairRequest
                    : PullDcaCertificateRequestMode.SigningRequest
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.InstalledChannelCertificate, Is.EqualTo(kCertificate));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.DcaCredentials.IssuerCertificates[0], Is.EqualTo(kIssuer));
            Assert.That(result.DcaCredentials.TrustList.TrustedCertificates[0],
                Is.EqualTo(ByteString.From(new byte[] { 1 })));
            Assert.That(result.DcaCredentials.TrustList.TrustedCrls[0], Is.EqualTo(ByteString.From(new byte[] { 2 })));
            Assert.That(result.DcaCredentials.TrustList.IssuerCertificates[0],
                Is.EqualTo(ByteString.From(new byte[] { 3 })));
            Assert.That(result.DcaCredentials.TrustList.IssuerCrls[0], Is.EqualTo(ByteString.From(new byte[] { 4 })));
        }

        [Test]
        public async Task NewKeyPairCertificateIsDetachedBeforeTrustDownload()
        {
            var fixture = new Fixture();
            byte[] certificate = kCertificate.ToArray();
            fixture.Connection.NewKeyPairCertificate = ByteString.From(certificate);
            fixture.Connection.TrustWait = _ =>
            {
                certificate[0] ^= 0xff;
                return Task.CompletedTask;
            };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Connection.InstalledChannelCertificate, Is.EqualTo(kCertificate));
        }

        [Test]
        public async Task SigningJournalCannotMutateActiveBindingOrCredentials()
        {
            var applicationId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1);
            var requestId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1);
            var fixture = new Fixture();
            var binding = new PullDcaSigningBinding(
                kRegistrar, applicationId, requestId, kProfile, kCsr, kCertificate,
                default, default, string.Empty, default, Guid.NewGuid());
            fixture.Connection = new FakeBoundConnection(fixture, binding);
            fixture.Connection.TrustList.TrustedCertificates = [ByteString.From(new byte[] { 5 })];
            var journal = new Mock<IPullDcaSigningJournal>(MockBehavior.Strict);
            journal.Setup(value => value.GetReceivedAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns((PullDcaSigningBinding received, CancellationToken _) =>
                {
                    MutateOpaqueIdentifier(received.ApplicationId);
                    return new ValueTask<PullDcaCredentials?>((PullDcaCredentials?)null);
                });
            journal.Setup(value => value.RecordReceivedAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<PullDcaCredentials>(), It.IsAny<CancellationToken>()))
                .Returns((PullDcaSigningBinding received, PullDcaCredentials credentials, CancellationToken _) =>
                {
                    Assert.That(received.ApplicationId, Is.EqualTo(applicationId));
                    MutateOpaqueIdentifier(received.RequestId);
                    MutateBytes(credentials.Certificate);
                    MutateBytes(credentials.IssuerCertificates[0]);
                    MutateBytes(credentials.TrustList.TrustedCertificates[0]);
                    return default;
                });
            journal.Setup(value => value.RecordInstalledAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns((PullDcaSigningBinding received, CancellationToken _) =>
                {
                    Assert.That(received.RequestId, Is.EqualTo(requestId));
                    MutateOpaqueIdentifier(received.RequestId);
                    return default;
                });
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(store => store.GetOrCreateAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<PullDcaCertificateProfile>(),
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    PullDcaRegistrar _, NodeId _, NodeId _, PullDcaCertificateProfile _,
                    Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive, CancellationToken ct) =>
                    receive(ct));

            PullDcaCredentials result = await fixture.Create(receipts: receipts.Object, signingJournal: journal.Object)
                .CompleteCertificateRequestAsync(kRegistrar, applicationId, requestId, kProfile)
                .ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.IssuerCertificates[0], Is.EqualTo(kIssuer));
            Assert.That(result.TrustList.TrustedCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 5 })));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
            journal.VerifyAll();
        }

        [Test]
        public async Task ResumedCredentialsAreDetachedFromInstallerAndJournal()
        {
            var fixture = new Fixture();
            var binding = new PullDcaSigningBinding(
                kRegistrar, kDcaId, kRequestId, kProfile, kCsr, kCertificate,
                default, default, string.Empty, default, Guid.NewGuid());
            fixture.Connection = new FakeBoundConnection(fixture, binding);
            var retained = new PullDcaCredentials(
                kCertificate.Copy(), [kIssuer.Copy()],
                new TrustListDataType { TrustedCertificates = [ByteString.From(new byte[] { 5 })] });
            fixture.Host.OnCredentialsInstalled = credentials =>
            {
                MutateBytes(credentials.Certificate);
                MutateBytes(credentials.IssuerCertificates[0]);
                MutateBytes(credentials.TrustList.TrustedCertificates[0]);
            };
            var journal = new Mock<IPullDcaSigningJournal>(MockBehavior.Strict);
            journal.Setup(value => value.GetReceivedAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PullDcaCredentials?>(retained));
            journal.Setup(value => value.RecordInstalledAsync(
                    It.IsAny<PullDcaSigningBinding>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            PullDcaCredentials result = await fixture.Create(signingJournal: journal.Object)
                .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.IssuerCertificates[0], Is.EqualTo(kIssuer));
            Assert.That(result.TrustList.TrustedCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 5 })));
            Assert.That(retained.Certificate, Is.EqualTo(kCertificate));
            Assert.That(retained.IssuerCertificates[0], Is.EqualTo(kIssuer));
            Assert.That(retained.TrustList.TrustedCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 5 })));
            journal.VerifyAll();
        }

        [Test]
        public async Task InitialPullCanInstallANewKeyPairWithoutPublishingItToSigningReceipts()
        {
            var fixture = new Fixture();
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create(receipts: receipts.Object)
                .RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(s_initialNewKeyPairCalls));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(fixture.Host.ReceivedPrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(
                fixture.Connection.LastReturnedPrivateKeyPfx.ToArray(),
                Is.EqualTo(new byte[kPrivateKeyPfx.Length]));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            receipts.Verify(store => store.GetOrCreateAsync(
                It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                It.IsAny<PullDcaCertificateProfile>(),
                It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task InstalledManagerOperationRejectsDcaKeyPairBeforeConnecting()
        {
            var fixture = new Fixture();
            var managerOperation = new Mock<IPullDcaInstalledManagerOperation>(MockBehavior.Strict);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request, null, managerOperation.Object)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.Empty);
            managerOperation.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ManagedApplicationCanSelectNewKeyPairWithoutChangingDcaSigningPath()
        {
            var fixture = new Fixture();
            PullDcaManagedApplication application = CreateApplication() with
            {
                CertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create().RunAsync(
                kRegistrar, CreateRequest() with { ManagedApplications = [application] }).ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(s_managedApplicationNewKeyPairCalls));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kManagedId }));
            Assert.That(
                fixture.Connection.LastReturnedPrivateKeyPfx.ToArray(),
                Is.EqualTo(new byte[kPrivateKeyPfx.Length]));
            Assert.That(result.ManagedApplications[0].ApplicationId, Is.EqualTo(kManagedId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NewKeyPairModeRequiresRecoveryProviderBeforeConnecting(bool managedApplication)
        {
            var fixture = new Fixture();
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = managedApplication
                    ? PullDcaCertificateRequestMode.SigningRequest
                    : PullDcaCertificateRequestMode.NewKeyPairRequest,
                ManagedApplications = managedApplication
                    ? [CreateApplication() with
                    {
                        CertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
                    }]
                    : []
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(includeNewKeyPairRecovery: false).RunAsync(kRegistrar, request)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.Zero);
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.Empty);
        }

        [Test]
        public async Task KeyPairRecoveryIsPersistedBeforeStartAndFinishOutputBeforeTrust()
        {
            var fixture = new Fixture();
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            AssertLifecycleOrder(
                fixture.Lifecycle,
                "recovery:dispatch",
                "transport:start-keypair",
                "start-keypair:dca",
                "recovery:started",
                "transport:finish-keypair",
                "finish-keypair:dca",
                "recovery:received",
                "trust:dca",
                "install-keypair:dca",
                "installer:verified",
                "recovery:complete");
            Assert.That(fixture.Recovery.LastRecordedPrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(fixture.Recovery.CompleteCount, Is.EqualTo(1));
        }

        [Test]
        public void KeyPairRecoveryAttemptToStringRedactsKnownPfxBytes()
        {
            var scope = new PullDcaNewKeyPairRecoveryScope(
                kRegistrar,
                kDcaId,
                kProfile,
                ByteString.From(new byte[] { 31, 32, 33 }),
                ByteString.From(new byte[] { 41, 42, 43 }),
                "urn:registrar:application",
                ["urn:namespace:registrar"]);
            var attempt = new PullDcaNewKeyPairRecoveryAttempt(
                new PullDcaNewKeyPairRecoveryBinding(scope, kRequestId, Guid.NewGuid()),
                PullDcaNewKeyPairRecoveryState.Received,
                default,
                default,
                ByteString.From(new byte[] { 0x50, 0x46, 0x58, 0x2D, 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 }));

            Assert.That(attempt.ToString(), Does.Not.Contain("5046582D534543524554"));
        }

        [Test]
        public async Task KeyPairDispatchPersistenceFailurePreventsStartRequest()
        {
            var fixture = new Fixture { FailingOperation = "recovery:dispatch" };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.Zero);
            Assert.That(fixture.Calls, Does.Not.Contain("start-keypair:dca"));
            Assert.That(fixture.Recovery.PendingAttempt, Is.Null);
        }

        [Test]
        public async Task ReceivedKeyPairPersistenceFailurePreventsTrustAndInstallation()
        {
            var fixture = new Fixture { FailingOperation = "recovery:received" };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("install-keypair:dca"));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.Empty);
        }

        [Test]
        public async Task ReceivedKeyPairRecoveryResumesWithoutFinishAndInstallsFinalTrustList()
        {
            var fixture = new Fixture { FailingOperation = "trust:dca" };
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.TrustedCertificates = [ByteString.From(new byte[] { 51 })];
            fixture.Connection.TrustList.TrustedCrls = [ByteString.From(new byte[] { 52 })];
            fixture.Connection.TrustList.IssuerCertificates = [ByteString.From(new byte[] { 53 })];
            fixture.Connection.TrustList.IssuerCrls = [ByteString.From(new byte[] { 54 })];
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));
            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
            PullDcaNewKeyPairRecoveryAttempt durableAttempt = fixture.Recovery.PendingAttempt!;
            fixture.Recovery = new FakeNewKeyPairRecoveryProvider(fixture);
            fixture.Recovery.Seed(durableAttempt);
            fixture.FailingOperation = null;
            fixture.Calls.Clear();
            fixture.Lifecycle.Clear();
            PullDcaCredentials? installed = null;
            fixture.Host.OnCredentialsInstalled = credentials => installed = credentials;

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Calls, Is.EqualTo(s_resumedKeyPairCalls));
            Assert.That(fixture.Host.ReceivedPrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(installed, Is.Not.Null);
            Assert.That(installed!.Certificate, Is.EqualTo(kCertificate));
            Assert.That(installed.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            Assert.That(installed.TrustList.TrustedCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 51 })));
            Assert.That(installed.TrustList.TrustedCrls[0], Is.EqualTo(ByteString.From(new byte[] { 52 })));
            Assert.That(installed.TrustList.IssuerCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 53 })));
            Assert.That(installed.TrustList.IssuerCrls[0], Is.EqualTo(ByteString.From(new byte[] { 54 })));
            Assert.That(result.DcaCredentials.TrustList.IssuerCrls[0], Is.EqualTo(ByteString.From(new byte[] { 54 })));
            Assert.That(fixture.Recovery.CompleteCount, Is.EqualTo(1));
            Assert.That(fixture.Recovery.PendingAttempt, Is.Null);
        }

        [TestCase("endpoint")]
        [TestCase("registrar")]
        [TestCase("directory")]
        [TestCase("client-channel")]
        [TestCase("registrar-channel")]
        [TestCase("registrar-application")]
        [TestCase("namespace")]
        [TestCase("application")]
        [TestCase("certificate-group")]
        [TestCase("certificate-type")]
        public async Task KeyPairRecoveryRejectsMismatchedScopeBeforeTrustOrInstall(string mismatch)
        {
            var fixture = new Fixture { FailingOperation = "trust:dca" };
            fixture.Recovery.ReturnPendingAcrossScopes = true;
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));
            fixture.FailingOperation = null;
            fixture.Calls.Clear();
            fixture.Lifecycle.Clear();
            PullDcaRegistrar registrar = kRegistrar;

            switch (mismatch)
            {
                case "endpoint":
                    registrar = registrar with { EndpointUrl = new Uri("opc.tcp://other:4840") };
                    break;
                case "registrar":
                    registrar = registrar with { RegistrarId = new NodeId("other-registrar", 2) };
                    break;
                case "directory":
                    registrar = registrar with { CertificateDirectoryId = new NodeId("other-directory", 2) };
                    break;
                case "client-channel":
                    fixture.Connection.RecoveryScopeTransform = scope =>
                        scope with { ClientChannelCertificate = ByteString.From(new byte[] { 61 }) };
                    break;
                case "registrar-channel":
                    fixture.Connection.RecoveryScopeTransform = scope =>
                        scope with { RegistrarChannelCertificate = ByteString.From(new byte[] { 62 }) };
                    break;
                case "registrar-application":
                    fixture.Connection.RecoveryScopeTransform = scope =>
                        scope with { RegistrarApplicationUri = "urn:registrar:other" };
                    break;
                case "namespace":
                    fixture.Connection.RecoveryScopeTransform = scope =>
                        scope with { NamespaceUris = ["urn:namespace:other"] };
                    break;
                case "application":
                    fixture.Connection.Selection = fixture.Connection.Selection with
                    {
                        ApplicationId = new NodeId("other-dca", 2)
                    };
                    break;
                case "certificate-group":
                    request = request with
                    {
                        DcaCertificateProfile = kProfile with
                        {
                            CertificateGroupId = new NodeId("other-group", 2)
                        }
                    };
                    fixture.ExpectedProfile = request.DcaCertificateProfile;
                    break;
                case "certificate-type":
                    request = request with
                    {
                        DcaCertificateProfile = kProfile with
                        {
                            CertificateTypeId = new NodeId("other-type", 2)
                        }
                    };
                    fixture.ExpectedProfile = request.DcaCertificateProfile;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mismatch));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(registrar, request).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Calls, Does.Not.Contain("finish-keypair:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("install-keypair:dca"));
            Assert.That(fixture.Recovery.CompleteCount, Is.Zero);
            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
        }

        [Test]
        public async Task StartedKeyPairRecoveryFinishesThePersistedRequestWithoutAnotherStart()
        {
            var fixture = new Fixture();
            var scope = new PullDcaNewKeyPairRecoveryScope(
                kRegistrar,
                kDcaId,
                kProfile,
                ByteString.From(new byte[] { 31, 32, 33 }),
                ByteString.From(new byte[] { 41, 42, 43 }),
                "urn:registrar:application",
                ["urn:namespace:registrar"]);
            fixture.Recovery.Seed(new PullDcaNewKeyPairRecoveryAttempt(
                new PullDcaNewKeyPairRecoveryBinding(scope, kRequestId, Guid.NewGuid()),
                PullDcaNewKeyPairRecoveryState.Started,
                default,
                default,
                default));
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.NewKeyPairRequests, Is.Zero);
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Recovery.CompleteCount, Is.EqualTo(1));
            Assert.That(fixture.Lifecycle, Does.Not.Contain("recovery:dispatch"));
        }

        [Test]
        public async Task DispatchingKeyPairRecoveryFailsClosedWithoutAnotherStart()
        {
            var fixture = new Fixture();
            var scope = new PullDcaNewKeyPairRecoveryScope(
                kRegistrar,
                kDcaId,
                kProfile,
                ByteString.From(new byte[] { 31, 32, 33 }),
                ByteString.From(new byte[] { 41, 42, 43 }),
                "urn:registrar:application",
                ["urn:namespace:registrar"]);
            fixture.Recovery.Seed(new PullDcaNewKeyPairRecoveryAttempt(
                new PullDcaNewKeyPairRecoveryBinding(scope, NodeId.Null, Guid.NewGuid()),
                PullDcaNewKeyPairRecoveryState.Dispatching,
                default,
                default,
                default));
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.Zero);
            Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            Assert.That(fixture.Calls, Is.EqualTo(s_identityCalls));
        }

        [Test]
        public async Task KeyPairInstallFailurePreservesReceivedAttemptWithoutCompletingIt()
        {
            var fixture = new Fixture { FailingOperation = "install-keypair:dca" };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
            Assert.That(fixture.Recovery.PendingAttempt?.PrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(fixture.Recovery.CompleteCount, Is.Zero);
            Assert.That(fixture.Lifecycle, Does.Not.Contain("installer:verified"));
        }

        [Test]
        public async Task KeyPairStartedPersistenceFailureFailsClosedAcrossRestart()
        {
            var fixture = new Fixture { FailingOperation = "recovery:started" };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Dispatching));

            fixture.FailingOperation = null;
            fixture.Calls.Clear();
            fixture.Lifecycle.Clear();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Dispatching));
        }

        [Test]
        public async Task KeyPairCompletionFailureRetriesInstallationWithoutRepeatingFinish()
        {
            var fixture = new Fixture { FailingOperation = "recovery:complete" };
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(fixture.Recovery.PendingAttempt?.State,
                Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
            Assert.That(fixture.Recovery.CompleteCount, Is.Zero);
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kDcaId }));

            fixture.FailingOperation = null;
            fixture.Calls.Clear();
            fixture.Lifecycle.Clear();
            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kDcaId, kDcaId }));
            Assert.That(fixture.Host.ReceivedPrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Recovery.CompleteCount, Is.EqualTo(1));
            Assert.That(fixture.Recovery.PendingAttempt, Is.Null);
        }

        [Test]
        public async Task LostAcceptedKeyPairFinishResponseUsesFreshRequestWithoutSigningRecovery()
        {
            var fixture = new Fixture();
            fixture.Connection.LoseAcceptedFinishResponse = true;
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadInvalidArgument));
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            var signingRecovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create(
                options: new PullDcaOptions
                {
                    FinishRetryDelay = TimeSpan.Zero,
                    MaximumNewKeyPairRestarts = 1
                },
                receipts: receipts.Object,
                signingRecovery: signingRecovery.Object).RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.LostAcceptedFinishResponses, Is.EqualTo(1));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(2));
            Assert.That(fixture.Connection.FinishedRequestIds,
                Is.EqualTo(new[] { kRequestId, kRequestId, kRestartedRequestId }));
            Assert.That(fixture.Recovery.RetiredLostRequestCount, Is.EqualTo(1));
            Assert.That(fixture.Recovery.CompleteCount, Is.EqualTo(1));
            AssertLifecycleOrder(
                fixture.Lifecycle,
                "recovery:retire-lost",
                "recovery:dispatch",
                "transport:start-keypair",
                "start-keypair:dca");
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls, Does.Not.Contain("certificates:dca"));
            receipts.VerifyNoOtherCalls();
            signingRecovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Missing installers reject both initial and managed-application key-pair modes before connecting.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NewKeyPairModeRequiresInstallerBeforeConnecting(bool managedApplication)
        {
            var fixture = new Fixture();
            var credentialProvider = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            Func<PullDcaRegistrar, CancellationToken, ValueTask<IPullDcaConnection>> connectionFactory =
                (_, _) => throw new AssertionException("The workflow connected without a key-pair installer.");
            var workflow = new PullDcaWorkflow(
                new PullDcaOptions(),
                connectionFactory,
                fixture.Host,
                credentialProvider.Object);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = managedApplication
                    ? PullDcaCertificateRequestMode.SigningRequest
                    : PullDcaCertificateRequestMode.NewKeyPairRequest,
                ManagedApplications = managedApplication
                    ? [
                        CreateApplication() with
                        {
                            CertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
                        }
                    ]
                    : []
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await workflow.RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.Empty);
            credentialProvider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// External authentication validates the managed key-pair installer before opening a registrar connection.
        /// </summary>
        [Test]
        public async Task ExternalManagedKeyPairModeRequiresInstallerBeforeConnecting()
        {
            var fixture = new Fixture();
            var credentialProvider = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            Func<PullDcaRegistrar, CancellationToken, ValueTask<IPullDcaConnection>> connectionFactory =
                (_, _) => throw new AssertionException("The workflow connected without a key-pair installer.");
            var workflow = new PullDcaWorkflow(
                new PullDcaOptions(),
                connectionFactory,
                fixture.Host,
                credentialProvider.Object);
            PullDcaManagedApplication application = CreateApplication() with
            {
                CertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await workflow.RunExternalAuthenticationAsync(kRegistrar, [application]).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.Empty);
            credentialProvider.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ConsumedKeyPairFinishRestartsWithFreshRequestInsteadOfSigningRecovery()
        {
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadInvalidArgument));
            var signingRecovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create(
                options: new PullDcaOptions { MaximumNewKeyPairRestarts = 1 },
                signingRecovery: signingRecovery.Object).RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count(call => call == "start-keypair:dca"), Is.EqualTo(2));
            Assert.That(fixture.Connection.FinishedRequestIds,
                Is.EqualTo(new[] { kRequestId, kRestartedRequestId }));
            Assert.That(fixture.Calls, Does.Not.Contain("certificates:dca"));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            signingRecovery.VerifyNoOtherCalls();
        }

        [Test]
        public async Task TransientKeyPairFinishThenConsumedRequestRestartsWithFreshRequest()
        {
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadInvalidArgument));
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            var signingRecovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            PullDcaResult result = await fixture.Create(
                options: new PullDcaOptions
                {
                    FinishRetryDelay = TimeSpan.Zero,
                    MaximumNewKeyPairRestarts = 1
                },
                receipts: receipts.Object,
                signingRecovery: signingRecovery.Object).RunAsync(kRegistrar, request).ConfigureAwait(false);

            Assert.That(fixture.Connection.FinishedRequestIds,
                Is.EqualTo(new[] { kRequestId, kRequestId, kRestartedRequestId }));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(2));
            Assert.That(fixture.Calls, Is.EqualTo(s_restartedNewKeyPairCalls));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(fixture.Host.ReceivedPrivateKeyPfx, Is.EqualTo(kPrivateKeyPfx));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            receipts.VerifyNoOtherCalls();
            signingRecovery.VerifyNoOtherCalls();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NewKeyPairFinishRejectsIncompleteKeyPair(bool missingCertificate)
        {
            var fixture = new Fixture();
            if (missingCertificate)
            {
                fixture.Connection.NewKeyPairCertificate = default;
            }
            else
            {
                fixture.Connection.NewKeyPairPrivateKeyPfx = default;
            }
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Host.KeyPairInstalledIds, Is.Empty);
        }

        [TestCase("trust:dca", PullDcaStage.ReadingTrustList)]
        [TestCase("install-keypair:dca", PullDcaStage.InstallingCredentials)]
        public async Task NewKeyPairFailureAfterFinishReportsInterruptedStage(
            string operation,
            PullDcaStage expectedStage)
        {
            var fixture = new Fixture { FailingOperation = operation };
            var progress = new ProgressRecorder();
            PullDcaRequest request = CreateRequest() with
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request, progress).ConfigureAwait(false));

            Assert.That(failure!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Calls[^1], Is.EqualTo(operation));
            Assert.That(fixture.Connection.NewKeyPairRequests, Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(progress.Items[^1].Stage, Is.EqualTo(expectedStage));
            Assert.That(progress.Items[^1].ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(progress.Items[^1].RequestId, Is.EqualTo(kRequestId));
            Assert.That(
                fixture.Connection.LastReturnedPrivateKeyPfx.ToArray(),
                Is.EqualTo(new byte[kPrivateKeyPfx.Length]));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
        }

        [Test]
        public async Task NoSoftwareGateOrManagedApplicationsRequiresNoSoftwareProvider()
        {
            var fixture = new Fixture();
            PullDcaResult result = await fixture.Create(includeSoftware: false).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(result.ManagedApplications.IsEmpty, Is.True);
            Assert.That(fixture.Calls.Count(c => c == "reconnect"), Is.EqualTo(2));
            Assert.That(fixture.Calls, Does.Not.Contain("software"));
            Assert.That(fixture.Calls, Does.Not.Contain("register"));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// Caller mutations while identity selection is pending cannot change the approved request or registrations.
        /// </summary>
        [Test]
        public async Task InFlightRequestRetainsOfferedBytesTicketsAndManagedApplicationSnapshot()
        {
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Connection.IdentityWait = async ct =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            };
            byte[] identity = [1, 2, 3];
            byte[] issuer = [4, 5, 6];
            string[] tickets = ["ticket"];
            string[] urls = ["opc.tcp://managed:4840"];
            string[] capabilities = ["DA"];
            LocalizedText[] names = [new LocalizedText("Managed application")];
            byte[] applicationIdentifier = [41, 42];
            byte[] groupIdentifier = [43, 44];
            var profile = kProfile with
            {
                CertificateGroupId = new NodeId(ByteString.From(groupIdentifier), 1)
            };
            fixture.ExpectedProfile = kProfile with
            {
                CertificateGroupId = new NodeId(ByteString.From(groupIdentifier.AsSpan()), 1)
            };
            PullDcaManagedApplication application = CreateApplication() with { CertificateProfile = profile };
            application.Application.ApplicationId = new NodeId(ByteString.From(applicationIdentifier), 1);
            application.Application.ApplicationType = ApplicationType.ClientAndServer;
            application.Application.ApplicationNames = names;
            application.Application.DiscoveryUrls = urls;
            application.Application.ServerCapabilities = capabilities;
            application.Application.ProductUri = "urn:original:product";
            PullDcaManagedApplication[] applications = [application];
            PullDcaRequest request = CreateRequest() with
            {
                IdentityOffer = new PullDcaIdentityOffer(
                    [ByteString.From(identity)], [ByteString.From(issuer)], tickets),
                DcaCertificateProfile = profile,
                ManagedApplications = applications
            };
            Task<PullDcaResult> running = fixture.Create().RunAsync(kRegistrar, request).AsTask();
            await entered.Task.ConfigureAwait(false);
            try
            {
                identity[0] = 99;
                issuer[0] = 99;
                tickets[0] = "changed";
                urls[0] = "opc.tcp://changed:4840";
                capabilities[0] = "changed";
                names[0] = new LocalizedText("Changed");
                applicationIdentifier[0] = 99;
                groupIdentifier[0] = 99;
                application.Application.ApplicationId = NodeId.Null;
                application.Application.ApplicationUri = "urn:changed:application";
                application.Application.ApplicationType = ApplicationType.DiscoveryServer;
                application.Application.ProductUri = "urn:changed:product";
                applications[0] = application with { ProtocolUri = "urn:changed:protocol" };
            }
            finally
            {
                release.SetResult(true);
            }
            PullDcaResult result = await running.ConfigureAwait(false);

            Assert.That(fixture.Connection.Offer!.Identities[0], Is.EqualTo(kIdentity));
            Assert.That(fixture.Connection.Offer.Issuers[0], Is.EqualTo(kIssuer));
            Assert.That(fixture.Connection.Offer.Tickets, Is.EqualTo(s_expectedTickets));
            PullDcaManagedApplication registered = fixture.Connection.RegisteredApplication!;
            Assert.That(registered.Application.ApplicationId,
                Is.EqualTo(new NodeId(ByteString.From(new byte[] { 41, 42 }), 1)));
            Assert.That(registered.CertificateProfile, Is.EqualTo(fixture.ExpectedProfile));
            Assert.That(registered.Application.ApplicationUri, Is.EqualTo("urn:managed:application"));
            Assert.That(registered.Application.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(registered.Application.ProductUri, Is.EqualTo("urn:original:product"));
            Assert.That(registered.Application.ApplicationNames,
                Is.EqualTo(new[] { new LocalizedText("Managed application") }.ToArrayOf()));
            Assert.That(registered.Application.DiscoveryUrls,
                Is.EqualTo(s_expectedDiscoveryUrls));
            Assert.That(registered.Application.ServerCapabilities, Is.EqualTo(s_expectedCapabilities));
            Assert.That(registered.ProtocolUri, Is.EqualTo("urn:opcua"));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId, kManagedId }));
        }

        /// <summary>
        /// Both authentication paths detach translations and formatting arguments before connection admission.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConnectionAdmissionRetainsNestedApplicationNameState(bool externalAuthentication)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.ConnectionWait = async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            var translations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["de"] = "Anwendung {0}",
                ["en"] = "Application {0}"
            };
            object[] arguments = ["original"];
            PullDcaManagedApplication application = CreateApplication();
            application.Application.ApplicationNames =
            [
                new LocalizedText(translations,
                    new TranslationInfo("application-name", "en", "Application {0}", arguments))
            ];
            PullDcaWorkflow workflow = fixture.Create();
            Task running = externalAuthentication
                ? workflow.RunExternalAuthenticationAsync(kRegistrar, [application], cancellationToken: timeout.Token)
                    .AsTask()
                : workflow.RunAsync(kRegistrar, CreateRequest() with { ManagedApplications = [application] },
                    cancellationToken: timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                translations["de"] = "Changed {0}";
                translations["en"] = "Changed {0}";
                arguments[0] = "changed";
                release.TrySetResult(true);
                await running.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                LocalizedText registered =
                    fixture.Connection.RegisteredApplication!.Application.ApplicationNames[0];
                Assert.That(registered.FilterByPreferredLocales(["de"]).Text, Is.EqualTo("Anwendung original"));
                Assert.That(registered.FilterByPreferredLocales(["en"]).Text, Is.EqualTo("Application original"));
            }
            finally
            {
                release.TrySetResult(true);
                timeout.Cancel();
                await running.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Unsupported name ownership is rejected before connection or host effects on either authentication path.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task UnsupportedApplicationNameCopyPrecedesSideEffects(
            bool externalAuthentication, bool unsupportedDictionary)
        {
            var fixture = new Fixture();
            var argument = new Mock<IFormattable>(MockBehavior.Strict);
            PullDcaManagedApplication application = CreateApplication();
            application.Application.ApplicationNames =
            [
                unsupportedDictionary
                    ? new LocalizedText(new SortedDictionary<string, string> { ["en"] = "Application" })
                    : new LocalizedText("Application {0}").WithTranslationInfo(
                        new TranslationInfo("application-name", null, "Application {0}", argument.Object))
            ];
            PullDcaWorkflow workflow = fixture.Create();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (externalAuthentication)
                {
                    await workflow.RunExternalAuthenticationAsync(kRegistrar, [application]).ConfigureAwait(false);
                }
                else
                {
                    await workflow.RunAsync(kRegistrar,
                        CreateRequest() with { ManagedApplications = [application] }).ConfigureAwait(false);
                }
            });

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.ConnectedRegistrar, Is.Null);
            Assert.That(fixture.Connection.RegisteredApplication, Is.Null);
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(argument.Invocations, Is.Empty);
        }

        /// <summary>
        /// Request snapshotting preserves the application-name value exposed by the generated record.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public async Task RequestSnapshotPreservesManagedApplicationNameArrayValue(
            bool nullNames, bool externalAuthentication)
        {
            var fixture = new Fixture();
            PullDcaManagedApplication application = CreateApplication();
            application.Application.ApplicationNames = nullNames ? default : [];
            ArrayOf<LocalizedText> suppliedNames = application.Application.ApplicationNames;
            Assert.That(suppliedNames.IsEmpty, Is.True);
            PullDcaRequest request = CreateRequest() with { ManagedApplications = [application] };

            PullDcaWorkflow workflow = fixture.Create();
            if (externalAuthentication)
            {
                await workflow.RunExternalAuthenticationAsync(kRegistrar, [application]).ConfigureAwait(false);
            }
            else
            {
                await workflow.RunAsync(kRegistrar, request).ConfigureAwait(false);
            }

            ArrayOf<LocalizedText> registeredNames =
                fixture.Connection.RegisteredApplication!.Application.ApplicationNames;
            Assert.That(registeredNames.IsNull, Is.EqualTo(suppliedNames.IsNull));
            Assert.That(registeredNames, Is.EqualTo(suppliedNames));
        }

        /// <summary>
        /// Initial, external, and resumed requests retain opaque identifiers across connection admission.
        /// </summary>
        [TestCase("fresh")]
        [TestCase("external")]
        [TestCase("resume")]
        public async Task ConnectionAdmissionRetainsOpaqueRequestIdentifiers(string path)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.ConnectionWait = async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            byte[] registrarBytes = [21, 22];
            byte[] directoryBytes = [23, 24];
            byte[] applicationBytes = [25, 26];
            byte[] requestBytes = [27, 28];
            byte[] groupBytes = [29, 30];
            var registrar = kRegistrar with
            {
                RegistrarId = new NodeId(ByteString.From(registrarBytes), 1),
                CertificateDirectoryId = new NodeId(ByteString.From(directoryBytes), 1)
            };
            var applicationId = new NodeId(ByteString.From(applicationBytes), 1);
            var requestId = new NodeId(ByteString.From(requestBytes), 1);
            var profile = kProfile with
            {
                CertificateGroupId = new NodeId(ByteString.From(groupBytes), 1)
            };
            fixture.ExpectedProfile = kProfile with
            {
                CertificateGroupId = new NodeId(ByteString.From(groupBytes.AsSpan()), 1)
            };
            PullDcaManagedApplication application = CreateApplication() with { CertificateProfile = profile };
            application.Application.ApplicationId = applicationId;
            PullDcaWorkflow workflow = fixture.Create();

            async Task RunAsync()
            {
                if (path == "resume")
                {
                    await workflow.CompleteCertificateRequestAsync(
                        registrar, applicationId, requestId, profile, cancellationToken: timeout.Token)
                        .ConfigureAwait(false);
                }
                else if (path == "external")
                {
                    await workflow.RunExternalAuthenticationAsync(
                        registrar, [application], cancellationToken: timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    await workflow.RunAsync(registrar, CreateRequest() with
                    {
                        DcaCertificateProfile = profile,
                        ManagedApplications = [application]
                    }, cancellationToken: timeout.Token).ConfigureAwait(false);
                }
            }

            Task running = RunAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                registrarBytes[0] = 99;
                directoryBytes[0] = 99;
                applicationBytes[0] = 99;
                requestBytes[0] = 99;
                groupBytes[0] = 99;
                release.TrySetResult(true);
                await running.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                PullDcaRegistrar connected = fixture.ConnectedRegistrar ??
                    throw new AssertionException("The workflow did not acquire a connection.");
                Assert.That(connected.RegistrarId,
                    Is.EqualTo(new NodeId(ByteString.From(new byte[] { 21, 22 }), 1)));
                Assert.That(connected.CertificateDirectoryId,
                    Is.EqualTo(new NodeId(ByteString.From(new byte[] { 23, 24 }), 1)));
                if (path == "resume")
                {
                    Assert.That(fixture.Connection.FinishedRequestIds,
                        Is.EqualTo(new[] { new NodeId(ByteString.From(new byte[] { 27, 28 }), 1) }));
                    Assert.That(fixture.Host.InstalledIds,
                        Is.EqualTo(new[] { new NodeId(ByteString.From(new byte[] { 25, 26 }), 1) }));
                }
                else
                {
                    PullDcaManagedApplication registered = fixture.Connection.RegisteredApplication ??
                        throw new AssertionException("The workflow did not register the managed application.");
                    Assert.That(registered.Application.ApplicationId,
                        Is.EqualTo(new NodeId(ByteString.From(new byte[] { 25, 26 }), 1)));
                    Assert.That(registered.CertificateProfile,
                        Is.EqualTo(fixture.ExpectedProfile));
                    Assert.That(fixture.Host.InstalledIds,
                        Is.EqualTo(path == "fresh" ? new[] { kDcaId, kManagedId } : new[] { kManagedId }));
                }
            }
            finally
            {
                release.TrySetResult(true);
                timeout.Cancel();
                await running.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Copying an offer retains OPC UA null and empty values independently of buffer ownership.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task IdentityOfferSnapshotPreservesNullAndEmptyIssuers(int shape)
        {
            var fixture = new Fixture();
            ArrayOf<ByteString> issuers = shape switch
            {
                0 => default,
                1 => [],
                2 => [default(ByteString)],
                _ => [ByteString.From(ReadOnlySpan<byte>.Empty)]
            };
            await fixture.Create().RunAsync(kRegistrar, CreateRequest() with
            {
                IdentityOffer = new PullDcaIdentityOffer([kIdentity], issuers, ["ticket"])
            }).ConfigureAwait(false);

            ArrayOf<ByteString> captured = fixture.Connection.Offer!.Issuers;
            Assert.That(captured.IsNull, Is.EqualTo(shape == 0));
            Assert.That(captured.Count, Is.EqualTo(shape < 2 ? 0 : 1));
            if (shape >= 2)
            {
                Assert.That(captured[0].IsNull, Is.EqualTo(shape == 2));
                Assert.That(captured[0].IsEmpty, Is.True);
            }
        }

        [Test]
        public async Task RequiredSoftwareProviderCannotBeReplacedByFabricatedSuccess()
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                SoftwareUpdateManager = new ManagerDescription { IsRequired = true }
            };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(includeSoftware: false).RunAsync(kRegistrar, CreateRequest())
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.EqualTo(s_identityCalls));
        }

        [TestCase("identity")]
        [TestCase("reconnect")]
        [TestCase("software")]
        [TestCase("csr:dca")]
        [TestCase("start:dca")]
        [TestCase("finish:dca")]
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        [TestCase("managers")]
        [TestCase("register")]
        [TestCase("install:managed")]
        public async Task ProviderOrServiceFailurePropagatesWithoutCompletionOrRetries(string failingOperation)
        {
            var fixture = new Fixture { FailingOperation = failingOperation };
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                SoftwareUpdateManager = new ManagerDescription { IsRequired = true }
            };
            var progress = new ProgressRecorder();
            PullDcaRequest request = CreateRequest() with { ManagedApplications = [CreateApplication()] };

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request, progress).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Calls[^1], Is.EqualTo(failingOperation));
            Assert.That(fixture.Calls.Count(c => c == failingOperation), Is.EqualTo(1));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task InvalidIdentitySelectionNeverActivatesCertificateOrRequestsCredentials(int invalidSelection)
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = invalidSelection switch
            {
                0 => fixture.Connection.Selection with { SelectedIdentity = ByteString.From(new byte[] { 99 }) },
                1 => fixture.Connection.Selection with { ApplicationId = NodeId.Null },
                _ => fixture.Connection.Selection with { MatchingTicket = null! }
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Calls, Is.EqualTo(s_provideCalls));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingCertificateIsBoundedAndPreservesApplicationAndRequestIds(bool emptyResponse)
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 10;
            fixture.Connection.EmptyPendingResponse = emptyResponse;
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            PullDcaCertificatePendingException? error =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(error.RequestId, Is.EqualTo(kRequestId));
            Assert.That(fixture.Calls.Count(c => c == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(2));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
        }

        [Test]
        public async Task PendingCertificateThenApprovalReusesRequestAndInstallsOnlyAfterApproval()
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 1;
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.Zero };

            await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count(c => c == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(2));
            Assert.That(fixture.Calls.Count(c => c == "install:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
        }

        /// <summary>
        /// Transport failure retries finish against the existing signing request without regenerating its key.
        /// </summary>
        [TestCase("timeout")]
        [TestCase("communication")]
        [TestCase("connection")]
        [TestCase("channel")]
        [TestCase("no-communication")]
        public async Task FinishTransportFailureRetriesOnlyTheOriginalSigningRequest(string scenario)
        {
            StatusCode status = scenario switch
            {
                "timeout" => StatusCodes.BadTimeout,
                "communication" => StatusCodes.BadCommunicationError,
                "connection" => StatusCodes.BadConnectionClosed,
                "channel" => StatusCodes.BadSecureChannelClosed,
                "no-communication" => StatusCodes.BadNoCommunication,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(status));
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            PullDcaResult result = await fixture.Create(options).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls.Count(call => call == "csr:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// Exhausting the shared finish budget preserves the final transport diagnostic rather than reporting pending.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task ExhaustedFinishTransportBudgetPreservesLastErrorWithoutInstallation(int maximumAttempts)
        {
            var fixture = new Fixture();
            for (int i = 1; i < maximumAttempts; i++)
            {
                fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            }
            var last = new ServiceResultException(StatusCodes.BadConnectionClosed, "Final transport diagnostic.");
            fixture.Connection.FinishFailures.Enqueue(last);
            var options = new PullDcaOptions
            {
                MaximumFinishAttempts = maximumAttempts,
                FinishRetryDelay = TimeSpan.Zero
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(last));
            Assert.That(fixture.Connection.FinishedRequestIds,
                Is.EqualTo(Enumerable.Repeat(kRequestId, maximumAttempts)));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Cancellation after a transport failure prevents a second finish call without waiting out the retry delay.
        /// </summary>
        [Test]
        public async Task FinishTransportRetryHonorsCancellationBeforeAnotherDispatch()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Connection.AfterFinish = cancellation.Cancel;
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.FromSeconds(30) };

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Create(options).RunAsync(
                    kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// A lost response followed by a consumed request cannot bypass request-bound recovery validation.
        /// </summary>
        [Test]
        public async Task LostFinishResponseFollowedByConsumedRequestRequiresExplicitRequestValidation()
        {
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.IssuerCertificates = [kIssuer];
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.Setup(value => value.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.Is<PullDcaCredentials>(credentials => credentials.Certificate == kCertificate),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            await fixture.Create(options, signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(fixture.Calls.Count(call => call == "certificates:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            recovery.VerifyAll();
            recovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A journaling recovery provider learns the exact started request before the first FinishRequest.
        /// </summary>
        [Test]
        public async Task SigningRequestObserverRecordsStartedRequestBeforeFirstFinish()
        {
            var fixture = new Fixture();
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            Mock<IPullDcaSigningRequestObserver> observer = recovery.As<IPullDcaSigningRequestObserver>();
            observer.Setup(value => value.OnSigningRequestStartedAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()))
                .Callback(() => fixture.Calls.Add("observe:dca"))
                .Returns(default(ValueTask));

            await fixture.Create(signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            int started = fixture.Calls.IndexOf("start:dca");
            Assert.That(fixture.Calls.IndexOf("observe:dca"), Is.EqualTo(started + 1));
            Assert.That(fixture.Calls.IndexOf("finish:dca"), Is.EqualTo(started + 2));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            observer.Verify(value => value.OnSigningRequestStartedAsync(
                kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()), Times.Once);
            recovery.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SigningRequestObserverCannotMutateActiveRequestBinding()
        {
            var registrar = kRegistrar with
            {
                RegistrarId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1),
                CertificateDirectoryId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1)
            };
            var applicationId = new NodeId(ByteString.From(new byte[] { 5, 6 }), 1);
            var requestId = new NodeId(ByteString.From(new byte[] { 7, 8 }), 1);
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(new byte[] { 9, 10 }), 1),
                new NodeId(ByteString.From(new byte[] { 11, 12 }), 1));
            var fixture = new Fixture { ExpectedProfile = profile };
            fixture.Connection.Selection = fixture.Connection.Selection with { ApplicationId = applicationId };
            fixture.Connection.SigningRequestId = requestId;
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.As<IPullDcaSigningRequestObserver>().Setup(observer => observer.OnSigningRequestStartedAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<PullDcaCertificateProfile>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Callback<PullDcaRegistrar, NodeId, NodeId, PullDcaCertificateProfile, ByteString, CancellationToken>(
                    (receivedRegistrar, receivedApplication, receivedRequest, receivedProfile, _, _) =>
                    {
                        MutateOpaqueIdentifier(receivedRegistrar.RegistrarId);
                        MutateOpaqueIdentifier(receivedRegistrar.CertificateDirectoryId);
                        MutateOpaqueIdentifier(receivedApplication);
                        MutateOpaqueIdentifier(receivedRequest);
                        MutateOpaqueIdentifier(receivedProfile.CertificateGroupId);
                        MutateOpaqueIdentifier(receivedProfile.CertificateTypeId);
                    })
                .Returns(default(ValueTask));

            PullDcaResult result = await fixture.Create(signingRecovery: recovery.Object).RunAsync(
                registrar, CreateRequest() with { DcaCertificateProfile = profile }).ConfigureAwait(false);

            Assert.That(fixture.ConnectedRegistrar, Is.EqualTo(registrar));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { requestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
            Assert.That(result.Identity.ApplicationId, Is.EqualTo(applicationId));
        }

        /// <summary>
        /// A request that could not be journaled is never finished, so its certificate is never left unrecoverable.
        /// </summary>
        [Test]
        public async Task SigningRequestObserverFailurePreventsFinish()
        {
            var fixture = new Fixture();
            var failure = new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.As<IPullDcaSigningRequestObserver>()
                .Setup(value => value.OnSigningRequestStartedAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(failure)));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                    .ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(fixture.Calls, Does.Contain("start:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("finish:dca"));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// A host can resume a persisted request without repeating identity selection, key generation or submission.
        /// </summary>
        [Test]
        public async Task CompletePersistedRequestDoesNotCreateAnotherKeyOrCertificateRequest()
        {
            var fixture = new Fixture();
            PullDcaWorkflow workflow = fixture.Create(new PullDcaOptions
            {
                MaximumFinishAttempts = 1,
                FinishRetryDelay = TimeSpan.Zero
            });
            fixture.Connection.PendingResponses = 1;
            PullDcaCertificatePendingException? pending =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));
            fixture.Calls.Clear();
            var progress = new ProgressRecorder();

            PullDcaCredentials credentials = await workflow.CompleteCertificateRequestAsync(
                kRegistrar, pending!.ApplicationId, pending.RequestId, CreateRequest().DcaCertificateProfile, progress)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls,
                Is.EqualTo(s_finishAndInstallCalls));
            Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(progress.Items.Any(p => p.Stage is PullDcaStage.RequestingCertificate or
                PullDcaStage.ProvidingIdentities or PullDcaStage.Completed), Is.False);
        }

        /// <summary>
        /// Resumption remains bounded and retains the original request when approval is still pending.
        /// </summary>
        [Test]
        public async Task CompletePersistedRequestRetainsIdentifiersWhenStillPending()
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 10;
            PullDcaWorkflow workflow = fixture.Create(new PullDcaOptions
            {
                MaximumFinishAttempts = 2,
                FinishRetryDelay = TimeSpan.Zero
            });

            PullDcaCertificatePendingException? pending =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await workflow.CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile).ConfigureAwait(false));

            Assert.That(pending!.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(pending.RequestId, Is.EqualTo(kRequestId));
            Assert.That(fixture.Calls, Is.EqualTo(s_pendingFinishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Missing persisted identifiers are rejected before accessing a session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompletePersistedRequestRequiresBothIdentifiers(bool missingApplication)
        {
            var fixture = new Fixture();
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(
                    kRegistrar, missingApplication ? NodeId.Null : kDcaId,
                    missingApplication ? kRequestId : NodeId.Null,
                    CreateRequest().DcaCertificateProfile).ConfigureAwait(false));
            Assert.That(fixture.Calls, Is.Empty);
        }

        /// <summary>
        /// Provider failures stop resumption and do not produce a workflow completion notification.
        /// </summary>
        [TestCase("finish:dca")]
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        public async Task CompletePersistedRequestPropagatesServiceAndInstallationFailures(string operation)
        {
            var fixture = new Fixture { FailingOperation = operation };
            var progress = new ProgressRecorder();
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile, progress)
                    .ConfigureAwait(false));
            Assert.That(failure!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Calls[^1], Is.EqualTo(operation));
            Assert.That(fixture.Calls, Does.Not.Contain("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
        }

        /// <summary>
        /// A received certificate survives later failures and a new workflow never repeats accepted FinishRequest.
        /// This recreates adapters over a retained in-memory backend, not a power-loss durability test.
        /// </summary>
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        public async Task RetainedSigningOutputResumesAfterTrustOrInstallationFailure(string operation)
        {
            var fixture = new Fixture { FailingOperation = operation };
            var secrets = new InMemorySecretStore();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unused");
            namespaces.Append("urn:registrar:model");
            using (var first = new SecretStorePullDcaCertificateReceiptStore(
                secrets, namespaces, "device", NUnitTelemetryContext.Create()))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await fixture.Create(receipts: first).CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));
            }
            fixture.Calls.Clear();
            fixture.FailingOperation = null;
            fixture.Connection.CertificateError = StatusCodes.BadRequestNotAllowed;
            using var restarted = new SecretStorePullDcaCertificateReceiptStore(
                secrets, namespaces, "device", NUnitTelemetryContext.Create());

            PullDcaCredentials result = await fixture.Create(receipts: restarted).CompleteCertificateRequestAsync(
                kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls, Is.EqualTo(s_installCalls));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// A receipt provider cannot change installed certificate bytes while trust download is suspended.
        /// </summary>
        [Test]
        public async Task SigningReceiptIsDetachedBeforeTrustDownload()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Connection.TrustWait = async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            byte[] certificate = kCertificate.ToArray();
            byte[] issuer = kIssuer.ToArray();
            ByteString[] issuers = [ByteString.From(issuer)];
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(store => store.GetOrCreateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PullDcaCertificateReceipt(ByteString.From(certificate), issuers));
            Task<PullDcaCredentials> running = fixture.Create(receipts: receipts.Object)
                .CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, cancellationToken: timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                certificate[0] = 99;
                issuer[0] = 99;
                issuers[0] = default;
                release.TrySetResult(true);
                PullDcaCredentials result = await running.ConfigureAwait(false);

                Assert.That(result.Certificate, Is.EqualTo(kCertificate));
                Assert.That(result.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
                Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
                timeout.Cancel();
                await running.ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReceiptStoreCannotMutateActiveRequestBinding()
        {
            var registrar = kRegistrar with
            {
                RegistrarId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1),
                CertificateDirectoryId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1)
            };
            var applicationId = new NodeId(ByteString.From(new byte[] { 5, 6 }), 1);
            var requestId = new NodeId(ByteString.From(new byte[] { 7, 8 }), 1);
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(new byte[] { 9, 10 }), 1),
                new NodeId(ByteString.From(new byte[] { 11, 12 }), 1));
            var fixture = new Fixture { ExpectedProfile = profile };
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(store => store.GetOrCreateAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<PullDcaCertificateProfile>(),
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    PullDcaRegistrar receivedRegistrar,
                    NodeId receivedApplication,
                    NodeId receivedRequest,
                    PullDcaCertificateProfile receivedProfile,
                    Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive,
                    CancellationToken ct) =>
                {
                    MutateOpaqueIdentifier(receivedRegistrar.RegistrarId);
                    MutateOpaqueIdentifier(receivedRegistrar.CertificateDirectoryId);
                    MutateOpaqueIdentifier(receivedApplication);
                    MutateOpaqueIdentifier(receivedRequest);
                    MutateOpaqueIdentifier(receivedProfile.CertificateGroupId);
                    MutateOpaqueIdentifier(receivedProfile.CertificateTypeId);
                    return receive(ct);
                });

            await fixture.Create(receipts: receipts.Object).CompleteCertificateRequestAsync(
                registrar, applicationId, requestId, profile).ConfigureAwait(false);

            Assert.That(fixture.ConnectedRegistrar, Is.EqualTo(registrar));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { requestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
        }

        /// <summary>
        /// Failure to retain output stops the workflow before trust download or installation.
        /// </summary>
        [Test]
        public async Task ReceiptPublicationFailureDoesNotInstallCredentials()
        {
            var fixture = new Fixture();
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            var failure = new InvalidOperationException("Receipt publication failed.");
            receipts.Setup(s => s.GetOrCreateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (PullDcaRegistrar _, NodeId _, NodeId _, PullDcaCertificateProfile _,
                    Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive, CancellationToken ct) =>
                {
                    await receive(ct).ConfigureAwait(false);
                    throw failure;
                });

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create(receipts: receipts.Object).CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(fixture.Calls, Is.EqualTo(s_finishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Invalid retained output is not interpreted as installed credentials or permission to finish again.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidReceiptDoesNotFinishDownloadTrustOrInstall(bool missingReceipt)
        {
            var fixture = new Fixture();
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(s => s.GetOrCreateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(missingReceipt ? null! : new PullDcaCertificateReceipt(default, []));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(receipts: receipts.Object).CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.EqualTo(s_connectCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public async Task EmptySigningRequestFailsBeforeCallingCertificateDirectory()
        {
            var fixture = new Fixture();
            fixture.Host.SigningRequest = default;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls[^1], Is.EqualTo("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
        }

        /// <summary>
        /// Authorization, certificate validation, and invalid-request errors do not enter transport retry.
        /// </summary>
        [TestCase("authorization")]
        [TestCase("certificate")]
        [TestCase("invalid-request")]
        public async Task NonTransientCertificateFailureIsNeverRetried(string scenario)
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = scenario switch
            {
                "authorization" => StatusCodes.BadRequestNotAllowed,
                "certificate" => StatusCodes.BadCertificateInvalid,
                "invalid-request" => StatusCodes.BadInvalidArgument,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(fixture.Connection.CertificateError));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("certificates:dca"));
        }

        /// <summary>
        /// Part 12 signing recovery requires the original binding and explicit host validation before installation.
        /// </summary>
        [Test]
        public async Task ConsumedSigningRequestRecoversOnlyAfterRequestBoundValidation()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.IssuerCertificates = [kIssuer];
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.Setup(r => r.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Callback<PullDcaRegistrar, NodeId, NodeId, PullDcaCertificateProfile,
                    PullDcaCredentials, CancellationToken>((_, _, _, _, credentials, ct) =>
                {
                    fixture.Record("validate-recovery", ct);
                    Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
                    Assert.That(credentials.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                    Assert.That(credentials.TrustList, Is.Not.SameAs(fixture.Connection.TrustList));
                    Assert.That(credentials.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
                    Assert.That(fixture.Host.InstalledIds, Is.Empty);
                })
                .Returns(default(ValueTask));

            PullDcaCredentials result = await fixture.Create(signingRecovery: recovery.Object)
                .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls, Is.EqualTo(s_recoveryCalls));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            recovery.VerifyAll();
        }

        [Test]
        public async Task SigningRecoveryValidatorCannotMutateActiveBindingOrRecoveredCredentials()
        {
            var registrar = kRegistrar with
            {
                RegistrarId = new NodeId(ByteString.From(new byte[] { 1, 2 }), 1),
                CertificateDirectoryId = new NodeId(ByteString.From(new byte[] { 3, 4 }), 1)
            };
            var applicationId = new NodeId(ByteString.From(new byte[] { 5, 6 }), 1);
            var requestId = new NodeId(ByteString.From(new byte[] { 7, 8 }), 1);
            var profile = new PullDcaCertificateProfile(
                new NodeId(ByteString.From(new byte[] { 9, 10 }), 1),
                new NodeId(ByteString.From(new byte[] { 11, 12 }), 1));
            var fixture = new Fixture { ExpectedProfile = profile };
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.AssignedTypes = [profile.CertificateTypeId];
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.TrustedCertificates = [ByteString.From(new byte[] { 13, 14 })];
            fixture.Connection.TrustList.TrustedCrls = [ByteString.From(new byte[] { 15, 16 })];
            fixture.Connection.TrustList.IssuerCertificates = [kIssuer.Copy()];
            fixture.Connection.TrustList.IssuerCrls = [ByteString.From(new byte[] { 17, 18 })];
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.Setup(value => value.ValidateAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<PullDcaCertificateProfile>(), It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Callback<PullDcaRegistrar, NodeId, NodeId, PullDcaCertificateProfile,
                    PullDcaCredentials, CancellationToken>(
                    (receivedRegistrar, receivedApplication, receivedRequest, receivedProfile, credentials, _) =>
                    {
                        MutateOpaqueIdentifier(receivedRegistrar.RegistrarId);
                        MutateOpaqueIdentifier(receivedRegistrar.CertificateDirectoryId);
                        MutateOpaqueIdentifier(receivedApplication);
                        MutateOpaqueIdentifier(receivedRequest);
                        MutateOpaqueIdentifier(receivedProfile.CertificateGroupId);
                        MutateOpaqueIdentifier(receivedProfile.CertificateTypeId);
                        MutateBytes(credentials.Certificate);
                        MutateBytes(credentials.IssuerCertificates[0]);
                        MutateBytes(credentials.TrustList.TrustedCertificates[0]);
                        MutateBytes(credentials.TrustList.TrustedCrls[0]);
                        MutateBytes(credentials.TrustList.IssuerCertificates[0]);
                        MutateBytes(credentials.TrustList.IssuerCrls[0]);
                        credentials.TrustList.SpecifiedLists = 0;
                    })
                .Returns(default(ValueTask));

            PullDcaCredentials result = await fixture.Create(signingRecovery: recovery.Object)
                .CompleteCertificateRequestAsync(registrar, applicationId, requestId, profile).ConfigureAwait(false);

            Assert.That(fixture.ConnectedRegistrar, Is.EqualTo(registrar));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { applicationId }));
            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
            Assert.That(result.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            Assert.That(result.TrustList.TrustedCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 13, 14 })));
            Assert.That(result.TrustList.TrustedCrls[0], Is.EqualTo(ByteString.From(new byte[] { 15, 16 })));
            Assert.That(result.TrustList.IssuerCertificates[0], Is.EqualTo(kIssuer));
            Assert.That(result.TrustList.IssuerCrls[0], Is.EqualTo(ByteString.From(new byte[] { 17, 18 })));
        }

        /// <summary>
        /// Missing validation is never replaced with an implicit acceptance of an assigned certificate.
        /// </summary>
        [Test]
        public async Task ConsumedSigningRequestWithoutRecoveryProviderDoesNotReadAssignments()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(fixture.Calls, Is.EqualTo(s_finishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Assignment shape, type and trust checks stop before the host may validate or install recovered output.
        /// </summary>
        [TestCase("length")]
        [TestCase("empty")]
        [TestCase("type")]
        [TestCase("duplicate")]
        [TestCase("trust")]
        public async Task SigningRecoveryRejectsInvalidAssignmentsAndIncompleteTrust(string failure)
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            switch (failure)
            {
                case "length":
                    fixture.Connection.AssignedTypes = [];
                    break;
                case "empty":
                    fixture.Connection.AssignedCertificates = [default];
                    break;
                case "type":
                    fixture.Connection.AssignedTypes = [new NodeId("other-type", 0)];
                    break;
                case "duplicate":
                    fixture.Connection.AssignedTypes = [kProfile.CertificateTypeId, kProfile.CertificateTypeId];
                    fixture.Connection.AssignedCertificates = [kCertificate, kCertificate];
                    break;
                case "trust":
                    fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.TrustedCertificates;
                    break;
            }
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(signingRecovery: recovery.Object)
                    .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            StatusCode expected = failure switch
            {
                "length" => StatusCodes.BadUnexpectedError,
                "type" => StatusCodes.BadNotFound,
                _ => StatusCodes.BadSecurityChecksFailed
            };
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
            recovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Host rejection of stale material, a wrong key, or mismatched retained binding cannot publish a receipt.
        /// </summary>
        [Test]
        public async Task RejectedSigningRecoveryDoesNotPublishReceiptOrInstall()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            var failure = new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            recovery.Setup(r => r.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Throws(failure);
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unused");
            namespaces.Append("urn:registrar:model");
            using var receipts = new SecretStorePullDcaCertificateReceiptStore(
                secrets.Object, namespaces, "device", NUnitTelemetryContext.Create());

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(receipts: receipts, signingRecovery: recovery.Object)
                    .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            recovery.VerifyAll();
        }

        [Test]
        public async Task PreCancelledRunDoesNotBorrowSession()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task CancellationDuringApprovalWaitPreventsTrustDownloadAndInstallation()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Connection.AfterFinish = cancellation.Cancel;
            fixture.Connection.PendingResponses = 10;
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.FromSeconds(1) };

            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await fixture.Create(options).RunAsync(
                    kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public async Task ConcurrentRunIsRejectedAndFailureReleasesWorkflowForNextRun()
        {
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Connection.IdentityWait = async ct =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("first run");
            };
            PullDcaWorkflow workflow = fixture.Create();
            Task<PullDcaResult> first = workflow.RunAsync(kRegistrar, CreateRequest()).AsTask();
            await entered.Task.ConfigureAwait(false);
            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? resume = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await workflow.CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile).ConfigureAwait(false));
                Assert.That(resume!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                release.SetResult(true);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await first.ConfigureAwait(false));
            fixture.Connection.IdentityWait = null;

            PullDcaResult result = await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(result.Identity.ApplicationId, Is.EqualTo(kDcaId));
        }

        [Test]
        public async Task DiscoveryReturnsConfiguredCandidatesWithoutConnecting()
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions { Registrars = [kRegistrar] };

            ArrayOf<PullDcaRegistrar> registrars = await fixture.Create(options).DiscoverRegistrarsAsync()
                .ConfigureAwait(false);

            Assert.That(registrars.Count, Is.EqualTo(1));
            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task DiscoveryUsesExplicitHostProviderWithoutConnectingToUnselectedCandidates()
        {
            var fixture = new Fixture();
            fixture.Host.DiscoveredRegistrars = [kRegistrar];

            ArrayOf<PullDcaRegistrar> registrars = await fixture.Create(includeDiscovery: true)
                .DiscoverRegistrarsAsync()
                .ConfigureAwait(false);

            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(fixture.Calls, Is.EqualTo(s_discoveryCalls));
            Assert.That(fixture.Host.DiscoveryToken.CanBeCanceled, Is.True);
        }

        [Test]
        public async Task OperationDeadlineCancelsCooperativeProviderWithoutClaimingCompletion()
        {
            var fixture = new Fixture();
            fixture.Connection.IdentityWait = ct => Task.Delay(Timeout.Infinite, ct);
            var options = new PullDcaOptions { OperationTimeout = TimeSpan.FromMilliseconds(20) };
            var progress = new ProgressRecorder();

            await Assert.ThatAsync(async () =>
                await fixture.Create(options).RunAsync(kRegistrar, CreateRequest(), progress).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(fixture.Calls, Is.SubsetOf(s_provideCalls));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public void DirectConstructionRequiresSessionAccessorTelemetryAndRealHostProviders()
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> accessor = (_, _) => default;

            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, null!, fixture.Host, fixture.Host, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, fixture.Host, fixture.Host, null!));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, null!, fixture.Host, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, fixture.Host, null!, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(null!, accessor, fixture.Host, fixture.Host, telemetry));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ConstructorRejectsInvalidTimeAndTrustListBudgets(int invalidBudget)
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions();
            switch (invalidBudget)
            {
                case 0:
                    options.OperationTimeout = TimeSpan.Zero;
                    break;
                case 1:
                    options.FinishRetryDelay = TimeSpan.FromMilliseconds(-1);
                    break;
                case 2:
                    options.FinishRetryDelay = TimeSpan.FromMinutes(2);
                    break;
                default:
                    options.MaximumTrustListSize = 0;
                    break;
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Create(options));
        }

        [TestCase(0)]
        [TestCase(101)]
        public void ConstructorRejectsUnboundedOrEmptyAttemptBudget(int attempts)
        {
            var fixture = new Fixture();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                fixture.Create(new PullDcaOptions { MaximumFinishAttempts = attempts }));
        }

        [Test]
        public async Task InvalidRequestAndRegistrarFailBeforeBorrowingSession()
        {
            var fixture = new Fixture();
            PullDcaWorkflow workflow = fixture.Create();
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await workflow.RunAsync(null!, CreateRequest()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar with { RegistrarId = NodeId.Null }, CreateRequest())
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar, CreateRequest() with
                {
                    IdentityOffer = new PullDcaIdentityOffer(default, default, default)
                }).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar, CreateRequest() with
                {
                    DcaCertificateProfile = kProfile with { CertificateTypeId = NodeId.Null }
                }).ConfigureAwait(false));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task FluentRegistrationRetainsHostAccessorAndUsesExplicitProvidersWithoutConnecting()
        {
            var fixture = new Fixture();
            var services = new ServiceCollection();
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> accessor =
                (_, _) => throw new InvalidOperationException("must not connect during registration or discovery");
            services.AddSingleton(accessor);
            services.AddSingleton<IPullDcaIdentityProvider>(fixture.Host);
            services.AddSingleton<IPullDcaCredentialProvider>(fixture.Host);
            services.AddSingleton(NUnitTelemetryContext.Create());
            IGdsClientBuilder builder = services.AddOpcUa().AddGdsClient();
            Assert.That(builder.AddPullDcaWorkflow(o => o.Registrars = [kRegistrar]), Is.SameAs(builder));
            builder.AddPullDcaWorkflow();
            using ServiceProvider provider = services.BuildServiceProvider();

            PullDcaWorkflow workflow = provider.GetRequiredService<PullDcaWorkflow>();
            ArrayOf<PullDcaRegistrar> registrars = await workflow.DiscoverRegistrarsAsync().ConfigureAwait(false);

            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(provider.GetRequiredService<
                Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(), Is.SameAs(accessor));
            Assert.That(provider.GetRequiredService<PullDcaWorkflow>(), Is.SameAs(workflow));
            Assert.That(services.Count(s => s.ServiceType == typeof(PullDcaWorkflow)), Is.EqualTo(1));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public void FluentRegistrationDoesNotInventMissingHostProviders()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(
                (_, _) => throw new InvalidOperationException("unused"));
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddGdsClient().AddPullDcaWorkflow();
            using ServiceProvider provider = services.BuildServiceProvider();

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<PullDcaWorkflow>());

            Assert.That(error!.Message, Does.Contain(nameof(IPullDcaIdentityProvider)));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaGdsClientBuilderExtensions.AddPullDcaWorkflow(null!));
        }

        private static PullDcaRequest CreateRequest()
        {
            return new PullDcaRequest(new PullDcaIdentityOffer([kIdentity], [kIssuer], ["ticket"]), kProfile, default);
        }

        private static void AssertLifecycleOrder(List<string> actual, params string[] expected)
        {
            int previousIndex = -1;
            foreach (string operation in expected)
            {
                int currentIndex = actual.IndexOf(operation, previousIndex + 1);
                Assert.That(
                    currentIndex,
                    Is.GreaterThan(previousIndex),
                    $"Missing or reordered operation: {operation}");
                previousIndex = currentIndex;
            }
        }

        private static PullDcaManagedApplication CreateApplication()
        {
            return new PullDcaManagedApplication(
                new ApplicationRecordDataType { ApplicationUri = "urn:managed:application" }, "urn:opcua", kProfile);
        }

        private static void MutateOpaqueIdentifier(NodeId identifier)
        {
            Assert.That(identifier.TryGetValue(out ByteString bytes), Is.True);
            MutateBytes(bytes);
        }

        private static void MutateBytes(ByteString bytes)
        {
            Assert.That(MemoryMarshal.TryGetArray(bytes.Memory, out ArraySegment<byte> segment), Is.True);
            Assert.That(segment.Array, Is.Not.Null);
            segment.Array![segment.Offset] ^= 0xff;
        }

        private sealed class ProgressRecorder : IProgress<PullDcaProgress>
        {
            public List<PullDcaProgress> Items { get; } = [];

            public Action<PullDcaProgress>? OnReport { get; init; }

            public void Report(PullDcaProgress value)
            {
                Items.Add(value);
                OnReport?.Invoke(value);
            }
        }

        private sealed class Fixture
        {
            public Fixture()
            {
                Connection = new FakeConnection(this);
                Host = new FakeHost(this);
                Recovery = new FakeNewKeyPairRecoveryProvider(this);
            }

            public List<string> Calls { get; } = [];
            public List<string> Lifecycle { get; } = [];
            public FakeConnection Connection { get; set; }
            public FakeHost Host { get; }
            public FakeNewKeyPairRecoveryProvider Recovery { get; set; }
            public string? FailingOperation { get; set; }

            /// <summary>
            /// Suspends connection admission before the workflow can use retained request inputs.
            /// </summary>
            public Func<CancellationToken, Task>? ConnectionWait { get; set; }

            /// <summary>
            /// Gets the registrar passed to the borrowed connection accessor.
            /// </summary>
            public PullDcaRegistrar? ConnectedRegistrar { get; private set; }

            /// <summary>
            /// Gets or sets the certificate profile required by the fixture's signing and trust services.
            /// </summary>
            public PullDcaCertificateProfile ExpectedProfile { get; set; } = kProfile;

            public PullDcaWorkflow Create(
                PullDcaOptions? options = null, bool includeSoftware = true, bool includeDiscovery = false,
                IPullDcaCertificateReceiptStore? receipts = null,
                IPullDcaSigningRecoveryProvider? signingRecovery = null,
                IPullDcaSigningJournal? signingJournal = null,
                bool includeNewKeyPairRecovery = true)
            {
                return new PullDcaWorkflow(
                    options ?? new PullDcaOptions(),
                    async (registrar, ct) =>
                    {
                        Record("connect", ct);
                        ConnectedRegistrar = registrar;
                        if (ConnectionWait != null)
                        {
                            await ConnectionWait(ct).ConfigureAwait(false);
                        }
                        return Connection;
                    },
                    Host, Host, includeSoftware ? Host : null, includeDiscovery ? Host : null,
                    receipts, signingRecovery, signingJournal,
                    newKeyPairRecoveryProvider: includeNewKeyPairRecovery ? Recovery : null);
            }

            public void Record(string operation, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Calls.Add(operation);
                RecordLifecycle(operation, ct);
            }

            public void RecordLifecycle(string operation, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Lifecycle.Add(operation);
                if (operation == FailingOperation)
                {
                    throw new InvalidOperationException("host failure");
                }
            }
        }

        private sealed class FakeNewKeyPairRecoveryProvider : IPullDcaNewKeyPairRecoveryProvider
        {
            public FakeNewKeyPairRecoveryProvider(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public PullDcaNewKeyPairRecoveryAttempt? PendingAttempt { get; private set; }
            public bool ReturnPendingAcrossScopes { get; set; }
            public int RetiredLostRequestCount { get; private set; }
            public int CompleteCount { get; private set; }
            public ByteString LastRecordedPrivateKeyPfx { get; private set; }

            public ValueTask<PullDcaNewKeyPairRecoveryAttempt?> GetPendingAsync(
                PullDcaNewKeyPairRecoveryScope scope,
                CancellationToken cancellationToken = default)
            {
                m_fixture.RecordLifecycle("recovery:get", cancellationToken);
                if (PendingAttempt == null)
                {
                    return new ValueTask<PullDcaNewKeyPairRecoveryAttempt?>(
                        (PullDcaNewKeyPairRecoveryAttempt?)null);
                }
                if (!SameScope(PendingAttempt.Binding.Scope, scope) && !ReturnPendingAcrossScopes)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                return new ValueTask<PullDcaNewKeyPairRecoveryAttempt?>(Clone(PendingAttempt));
            }

            public ValueTask RecordDispatchAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                CancellationToken cancellationToken = default)
            {
                Assert.That(binding.RequestId.IsNull, Is.True);
                Assert.That(binding.AttemptId, Is.Not.EqualTo(Guid.Empty));
                m_fixture.RecordLifecycle("recovery:dispatch", cancellationToken);
                PendingAttempt = new PullDcaNewKeyPairRecoveryAttempt(
                    Clone(binding), PullDcaNewKeyPairRecoveryState.Dispatching, default, default, default);
                return default;
            }

            public ValueTask RecordStartedAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                CancellationToken cancellationToken = default)
            {
                m_fixture.RecordLifecycle("recovery:started", cancellationToken);
                Assert.That(PendingAttempt?.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Dispatching));
                Assert.That(binding.RequestId.IsNull, Is.False);
                PendingAttempt = new PullDcaNewKeyPairRecoveryAttempt(
                    Clone(binding), PullDcaNewKeyPairRecoveryState.Started, default, default, default);
                return default;
            }

            public ValueTask RecordReceivedAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                ByteString certificate,
                ArrayOf<ByteString> issuerCertificates,
                ByteString privateKeyPfx,
                CancellationToken cancellationToken = default)
            {
                m_fixture.RecordLifecycle("recovery:received", cancellationToken);
                LastRecordedPrivateKeyPfx = ByteString.From(privateKeyPfx.Span);
                Assert.That(PendingAttempt?.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Started));
                PendingAttempt = new PullDcaNewKeyPairRecoveryAttempt(
                    Clone(binding),
                    PullDcaNewKeyPairRecoveryState.Received,
                    ByteString.From(certificate.Span),
                    issuerCertificates.IsNull ? ArrayOf<ByteString>.Null : issuerCertificates.Span.ToArray(),
                    ByteString.From(privateKeyPfx.Span));
                return default;
            }

            public ValueTask RetireLostRequestAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                CancellationToken cancellationToken = default)
            {
                m_fixture.RecordLifecycle("recovery:retire-lost", cancellationToken);
                Assert.That(PendingAttempt?.Binding.AttemptId, Is.EqualTo(binding.AttemptId));
                Assert.That(PendingAttempt?.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Started));
                PendingAttempt = null;
                RetiredLostRequestCount++;
                return default;
            }

            public ValueTask CompleteAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                ByteString installedCertificate,
                CancellationToken cancellationToken = default)
            {
                m_fixture.RecordLifecycle("recovery:complete", cancellationToken);
                Assert.That(PendingAttempt?.Binding.AttemptId, Is.EqualTo(binding.AttemptId));
                Assert.That(PendingAttempt?.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
                Assert.That(PendingAttempt?.Certificate, Is.EqualTo(installedCertificate));
                PendingAttempt = null;
                CompleteCount++;
                return default;
            }

            public void Seed(PullDcaNewKeyPairRecoveryAttempt attempt)
            {
                PendingAttempt = Clone(attempt);
            }

            private static PullDcaNewKeyPairRecoveryAttempt Clone(
                PullDcaNewKeyPairRecoveryAttempt attempt)
            {
                return attempt with
                {
                    Binding = Clone(attempt.Binding),
                    Certificate = attempt.Certificate.IsNull ? default : ByteString.From(attempt.Certificate.Span),
                    IssuerCertificates = attempt.IssuerCertificates.IsNull
                        ? ArrayOf<ByteString>.Null
                        : attempt.IssuerCertificates.Span.ToArray(),
                    PrivateKeyPfx = attempt.PrivateKeyPfx.IsNull
                        ? default
                        : ByteString.From(attempt.PrivateKeyPfx.Span)
                };
            }

            private static PullDcaNewKeyPairRecoveryBinding Clone(
                PullDcaNewKeyPairRecoveryBinding binding)
            {
                return PullDcaConnection.SnapshotRecoveryBinding(binding);
            }

            private static bool SameScope(
                PullDcaNewKeyPairRecoveryScope left,
                PullDcaNewKeyPairRecoveryScope right)
            {
                return left.Registrar.EndpointUrl == right.Registrar.EndpointUrl &&
                    left.Registrar.RegistrarId == right.Registrar.RegistrarId &&
                    left.Registrar.CertificateDirectoryId == right.Registrar.CertificateDirectoryId &&
                    left.ApplicationId == right.ApplicationId &&
                    left.Profile.CertificateGroupId == right.Profile.CertificateGroupId &&
                    left.Profile.CertificateTypeId == right.Profile.CertificateTypeId &&
                    left.ClientChannelCertificate.Span.SequenceEqual(right.ClientChannelCertificate.Span) &&
                    left.RegistrarChannelCertificate.Span.SequenceEqual(right.RegistrarChannelCertificate.Span) &&
                    left.RegistrarApplicationUri == right.RegistrarApplicationUri &&
                    left.NamespaceUris.Span.SequenceEqual(right.NamespaceUris.Span);
            }

            private readonly Fixture m_fixture;
        }

        /// <summary>
        /// Represents an extended manager description that cannot be safely snapshotted.
        /// </summary>
        private sealed class DerivedManagerDescription : ManagerDescription
        {
        }

        /// <summary>
        /// Represents a ticket extension whose fields cannot be safely snapshotted.
        /// </summary>
        private sealed class DerivedDeviceIdentityTicket : DeviceIdentityTicketType
        {
        }

        /// <summary>
        /// Represents an authority extension whose fields cannot be safely snapshotted.
        /// </summary>
        private sealed class DerivedCertificateAuthority : CertificateAuthorityType
        {
        }

        private class FakeConnection : IPullDcaConnection, IPullDcaRecoverableNewKeyPairConnection
        {
            public FakeConnection(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public PullDcaIdentitySelection Selection { get; set; } =
                new(kIdentity, new DeviceIdentityTicketType(), kDcaId, null);
            public PullDcaIdentityOffer? Offer { get; private set; }
            public PullDcaManagedApplication? RegisteredApplication { get; private set; }
            public TrustListDataType TrustList { get; } = new();
            public ArrayOf<ManagerDescription> Managers { get; } =
                [new ManagerDescription { Name = new LocalizedText("manager") }];
            /// <summary>
            /// Gets the certificate supplied for the installed channel.
            /// </summary>
            public ByteString InstalledChannelCertificate { get; private set; }
            public int PendingResponses { get; set; }
            public bool EmptyPendingResponse { get; set; }
            public StatusCode CertificateError { get; set; } = StatusCodes.Good;
            public Queue<Exception> FinishFailures { get; } = new();
            public List<NodeId> FinishedRequestIds { get; } = [];
            public NodeId SigningRequestId { get; set; } = kRequestId;

            /// <summary>
            /// Gets the number of new key-pair requests started by the workflow.
            /// </summary>
            public int NewKeyPairRequests { get; private set; }

            /// <summary>
            /// Gets or sets the certificate returned by a new key-pair request.
            /// </summary>
            public ByteString NewKeyPairCertificate { get; set; } = kCertificate;

            /// <summary>
            /// Gets or sets the PFX returned by a new key-pair request.
            /// </summary>
            public ByteString NewKeyPairPrivateKeyPfx { get; set; } = kPrivateKeyPfx;

            /// <summary>
            /// Gets the last PFX buffer returned to the workflow.
            /// </summary>
            public ByteString LastReturnedPrivateKeyPfx { get; private set; }

            public Func<PullDcaNewKeyPairRecoveryScope, PullDcaNewKeyPairRecoveryScope>?
                RecoveryScopeTransform { get; set; }

            public bool LoseAcceptedFinishResponse { get; set; }
            public int LostAcceptedFinishResponses { get; private set; }

            public Action? AfterFinish { get; set; }
            public Func<CancellationToken, Task>? IdentityWait { get; set; }

            /// <summary>
            /// Suspends trust download after a signing receipt has been selected.
            /// </summary>
            public Func<CancellationToken, Task>? TrustWait { get; set; }

            public ArrayOf<NodeId> AssignedTypes { get; set; } = [kProfile.CertificateTypeId];
            public ArrayOf<ByteString> AssignedCertificates { get; set; } = [kCertificate];

            public async ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
                PullDcaIdentityOffer offer, CancellationToken ct)
            {
                m_fixture.Record("provide", ct);
                Offer = offer;
                if (IdentityWait != null)
                {
                    await IdentityWait(ct).ConfigureAwait(false);
                }
                return Selection;
            }

            public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
            {
                m_fixture.Record("reconnect", ct);
                return default;
            }

            public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
            {
                m_fixture.Record("managers", ct);
                return new ValueTask<ArrayOf<ManagerDescription>>(Managers);
            }

            public ValueTask<ArrayOf<ManagerDescription>> GetManagersOnInstalledChannelAsync(
                PullDcaSigningBinding? binding,
                ByteString installedCertificate,
                CancellationToken ct)
            {
                m_fixture.Record("managers", ct);
                InstalledChannelCertificate = installedCertificate.Copy();
                return new ValueTask<ArrayOf<ManagerDescription>>(Managers);
            }

            public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
            {
                m_fixture.Record("register", ct);
                RegisteredApplication = application;
                return new ValueTask<NodeId>(kManagedId);
            }

            public ValueTask<NodeId> StartAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
            {
                m_fixture.Record("start:" + applicationId.IdentifierAsString, ct);
                Assert.That(profile, Is.EqualTo(m_fixture.ExpectedProfile));
                Assert.That(csr, Is.EqualTo(kCsr));
                return new ValueTask<NodeId>(SigningRequestId);
            }

            public ValueTask<NodeId> StartNewKeyPairAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, CancellationToken ct)
            {
                m_fixture.Record("start-keypair:" + applicationId.IdentifierAsString, ct);
                Assert.That(profile, Is.EqualTo(m_fixture.ExpectedProfile));
                return new ValueTask<NodeId>(
                    NewKeyPairRequests++ == 0 ? kRequestId : kRestartedRequestId);
            }

            public ValueTask<PullDcaNewKeyPairRecoveryScope> CaptureNewKeyPairRecoveryScopeAsync(
                NodeId applicationId,
                PullDcaCertificateProfile profile,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                m_fixture.Lifecycle.Add("connection:capture-recovery-scope");
                return new ValueTask<PullDcaNewKeyPairRecoveryScope>(
                    CreateRecoveryScope(applicationId, profile));
            }

            public async ValueTask<PullDcaNewKeyPairRecoveryBinding> StartRecoverableNewKeyPairAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                Func<PullDcaNewKeyPairRecoveryBinding, CancellationToken, ValueTask> recordDispatchAsync,
                CancellationToken ct)
            {
                await recordDispatchAsync(binding, ct).ConfigureAwait(false);
                m_fixture.Lifecycle.Add("transport:start-keypair");
                NodeId requestId = await StartNewKeyPairAsync(
                    binding.Scope.ApplicationId, binding.Scope.Profile, ct).ConfigureAwait(false);
                return binding with { RequestId = requestId };
            }

            public async ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
                FinishRecoverableNewKeyPairAsync(
                    PullDcaNewKeyPairRecoveryBinding binding,
                    CancellationToken ct)
            {
                m_fixture.Lifecycle.Add("transport:finish-keypair");
                if (LoseAcceptedFinishResponse)
                {
                    LoseAcceptedFinishResponse = false;
                    LostAcceptedFinishResponses++;
                    FinishedRequestIds.Add(binding.RequestId);
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }
                return await FinishNewKeyPairAsync(
                    binding.Scope.ApplicationId, binding.RequestId, ct).ConfigureAwait(false);
            }

            public ValueTask ValidateNewKeyPairRecoveryBindingAsync(
                PullDcaNewKeyPairRecoveryBinding binding,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                m_fixture.Lifecycle.Add("connection:validate-recovery-binding");
                PullDcaNewKeyPairRecoveryScope current = CreateRecoveryScope(
                    binding.Scope.ApplicationId, binding.Scope.Profile);
                if (!ScopesEqual(current, binding.Scope))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                }
                return default;
            }

            public void SetInstalledNewKeyPairBinding(PullDcaNewKeyPairRecoveryBinding binding)
            {
                m_fixture.Lifecycle.Add("connection:retain-installed-binding");
            }

            public ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
                NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                m_fixture.Record("finish:" + applicationId.IdentifierAsString, ct);
                FinishedRequestIds.Add(requestId);
                AfterFinish?.Invoke();
                if (FinishFailures.Count != 0)
                {
                    throw FinishFailures.Dequeue();
                }
                if (StatusCode.IsBad(CertificateError))
                {
                    throw new ServiceResultException(CertificateError);
                }
                if (PendingResponses-- > 0)
                {
                    if (!EmptyPendingResponse)
                    {
                        throw new ServiceResultException(StatusCodes.BadNothingToDo);
                    }
                    return new ValueTask<(ByteString, ArrayOf<ByteString>)>(
                        (default, ArrayOf<ByteString>.Null));
                }
                return new ValueTask<(ByteString, ArrayOf<ByteString>)>((kCertificate, new[] { kIssuer }.ToArrayOf()));
            }

            public ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
                FinishNewKeyPairAsync(NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                m_fixture.Record("finish-keypair:" + applicationId.IdentifierAsString, ct);
                FinishedRequestIds.Add(requestId);
                if (FinishFailures.Count != 0)
                {
                    throw FinishFailures.Dequeue();
                }
                if (StatusCode.IsBad(CertificateError))
                {
                    throw new ServiceResultException(CertificateError);
                }
                if (PendingResponses-- > 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }
                LastReturnedPrivateKeyPfx = NewKeyPairPrivateKeyPfx.Copy();
                return new ValueTask<(ByteString, ByteString, ArrayOf<ByteString>)>(
                    (NewKeyPairCertificate, LastReturnedPrivateKeyPfx, new[] { kIssuer.Copy() }.ToArrayOf()));
            }

            public async ValueTask<TrustListDataType> ReadTrustListAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                m_fixture.Record("trust:" + applicationId.IdentifierAsString, ct);
                Assert.That(certificateGroupId, Is.EqualTo(m_fixture.ExpectedProfile.CertificateGroupId));
                if (TrustWait != null)
                {
                    await TrustWait(ct).ConfigureAwait(false);
                }
                return TrustList;
            }

            public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                m_fixture.Record("certificates:" + applicationId.IdentifierAsString, ct);
                Assert.That(certificateGroupId, Is.EqualTo(m_fixture.ExpectedProfile.CertificateGroupId));
                return new ValueTask<(ArrayOf<NodeId>, ArrayOf<ByteString>)>(
                    (AssignedTypes, AssignedCertificates));
            }

            private PullDcaNewKeyPairRecoveryScope CreateRecoveryScope(
                NodeId applicationId,
                PullDcaCertificateProfile profile)
            {
                var scope = new PullDcaNewKeyPairRecoveryScope(
                    m_fixture.ConnectedRegistrar ?? kRegistrar,
                    applicationId,
                    profile,
                    ByteString.From(new byte[] { 31, 32, 33 }),
                    ByteString.From(new byte[] { 41, 42, 43 }),
                    "urn:registrar:application",
                    ["urn:namespace:registrar"]);
                return RecoveryScopeTransform?.Invoke(scope) ?? scope;
            }

            private static bool ScopesEqual(
                PullDcaNewKeyPairRecoveryScope left,
                PullDcaNewKeyPairRecoveryScope right)
            {
                return left.Registrar.EndpointUrl == right.Registrar.EndpointUrl &&
                    left.Registrar.RegistrarId == right.Registrar.RegistrarId &&
                    left.Registrar.CertificateDirectoryId == right.Registrar.CertificateDirectoryId &&
                    left.ApplicationId == right.ApplicationId &&
                    left.Profile.CertificateGroupId == right.Profile.CertificateGroupId &&
                    left.Profile.CertificateTypeId == right.Profile.CertificateTypeId &&
                    left.ClientChannelCertificate.Span.SequenceEqual(right.ClientChannelCertificate.Span) &&
                    left.RegistrarChannelCertificate.Span.SequenceEqual(right.RegistrarChannelCertificate.Span) &&
                    left.RegistrarApplicationUri == right.RegistrarApplicationUri &&
                    left.NamespaceUris.Span.SequenceEqual(right.NamespaceUris.Span);
            }

            private readonly Fixture m_fixture;
        }

        private sealed class FakeBoundConnection : FakeConnection, IPullDcaBoundSigningConnection
        {
            public FakeBoundConnection(Fixture fixture, PullDcaSigningBinding binding)
                : base(fixture)
            {
                m_fixture = fixture;
                m_binding = binding;
            }

            public ValueTask<NodeId> StartWithEvidenceAsync(
                NodeId applicationId,
                PullDcaCertificateProfile profile,
                ByteString csr,
                ByteString signingKeyCertificate,
                CancellationToken ct)
            {
                m_fixture.Record("start:" + applicationId.IdentifierAsString, ct);
                Assert.That(applicationId, Is.EqualTo(m_binding.ApplicationId));
                Assert.That(profile, Is.EqualTo(m_fixture.ExpectedProfile));
                Assert.That(csr, Is.EqualTo(kCsr));
                Assert.That(signingKeyCertificate, Is.EqualTo(kCertificate));
                return new ValueTask<NodeId>(m_binding.RequestId);
            }

            public ValueTask<PullDcaSigningBinding> GetSigningBindingAsync(
                NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                Assert.That(applicationId, Is.EqualTo(m_binding.ApplicationId));
                Assert.That(requestId, Is.EqualTo(m_binding.RequestId));
                return new ValueTask<PullDcaSigningBinding>(m_binding);
            }

            private readonly Fixture m_fixture;
            private readonly PullDcaSigningBinding m_binding;
        }

        private sealed class FakeHost :
            IPullDcaIdentityProvider, IPullDcaCredentialProvider, IPullDcaNewKeyPairCredentialProvider,
            IPullDcaSigningKeyEvidenceProvider, IPullDcaSoftwareUpdateProvider, IPullDcaRegistrarDiscovery
        {
            public FakeHost(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public ByteString ActivatedIdentity { get; private set; }
            public PullDcaIdentitySelection? SoftwareIdentity { get; private set; }
            public ManagerDescription? SoftwareManager { get; private set; }
            public PullDcaRegistrar? SoftwareRegistrar { get; private set; }
            public ByteString SigningRequest { get; set; } = kCsr;
            /// <summary>
            /// Gets or sets an action invoked with the identity selected for activation.
            /// </summary>
            public Action<ByteString>? OnActivate { get; set; }

            /// <summary>
            /// Gets or sets an action invoked with the software-update binding.
            /// </summary>
            public Action<PullDcaRegistrar, PullDcaIdentitySelection,             ManagerDescription>? OnSoftwareUpdate { get; set; }
            public Action<NodeId, PullDcaCertificateProfile>? OnCreateSigningRequest { get; set; }
            public Action<NodeId, PullDcaCertificateProfile, ByteString>? OnKeyEvidence { get; set; }
            public Action<NodeId>? OnInstall { get; set; }
            public Action<NodeId, PullDcaCertificateProfile>? OnInstallNewKeyPair { get; set; }
            /// <summary>
            /// Gets or sets an action invoked with credentials passed to the installer.
            /// </summary>
            public Action<PullDcaCredentials>? OnCredentialsInstalled { get; set; }
            public List<NodeId> InstalledIds { get; } = [];
            public List<NodeId> KeyPairInstalledIds { get; } = [];
            public ByteString ReceivedPrivateKeyPfx { get; private set; }
            public ArrayOf<PullDcaRegistrar> DiscoveredRegistrars { get; set; }
            public CancellationToken DiscoveryToken { get; private set; }

            public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken)
            {
                m_fixture.Record("identity", cancellationToken);
                ActivatedIdentity = selectedIdentity;
                OnActivate?.Invoke(selectedIdentity);
                return default;
            }

            public ValueTask CompleteAsync(
                PullDcaRegistrar registrar, PullDcaIdentitySelection identity,
                ManagerDescription manager, CancellationToken cancellationToken)
            {
                m_fixture.Record("software", cancellationToken);
                SoftwareIdentity = identity;
                SoftwareManager = manager;
                SoftwareRegistrar = registrar;
                OnSoftwareUpdate?.Invoke(registrar, identity, manager);
                return default;
            }

            public ValueTask<ByteString> CreateSigningRequestAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, CancellationToken cancellationToken)
            {
                m_fixture.Record("csr:" + applicationId.IdentifierAsString, cancellationToken);
                Assert.That(profile, Is.EqualTo(m_fixture.ExpectedProfile));
                OnCreateSigningRequest?.Invoke(applicationId, profile);
                return new ValueTask<ByteString>(SigningRequest);
            }

            public ValueTask InstallAsync(
                NodeId applicationId, PullDcaCredentials credentials, CancellationToken cancellationToken)
            {
                m_fixture.Record("install:" + applicationId.IdentifierAsString, cancellationToken);
                Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
                Assert.That(credentials.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                Assert.That(credentials.TrustList.SpecifiedLists,
                    Is.EqualTo(m_fixture.Connection.TrustList.SpecifiedLists));
                InstalledIds.Add(applicationId);
                OnInstall?.Invoke(applicationId);
                OnCredentialsInstalled?.Invoke(credentials);
                return default;
            }

            public ValueTask<ByteString> GetSigningKeyCertificateAsync(
                NodeId applicationId,
                PullDcaCertificateProfile profile,
                ByteString signingRequest,
                CancellationToken cancellationToken)
            {
                m_fixture.Record("evidence:" + applicationId.IdentifierAsString, cancellationToken);
                OnKeyEvidence?.Invoke(applicationId, profile, signingRequest);
                return new ValueTask<ByteString>(kCertificate);
            }

            public ValueTask InstallNewKeyPairAsync(
                NodeId applicationId,
                PullDcaCertificateProfile profile,
                PullDcaCredentials credentials,
                ByteString privateKeyPfx,
                CancellationToken cancellationToken)
            {
                m_fixture.Record("install-keypair:" + applicationId.IdentifierAsString, cancellationToken);
                Assert.That(profile, Is.EqualTo(m_fixture.ExpectedProfile));
                Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
                Assert.That(credentials.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                Assert.That(credentials.TrustList.SpecifiedLists,
                    Is.EqualTo(m_fixture.Connection.TrustList.SpecifiedLists));
                KeyPairInstalledIds.Add(applicationId);
                ReceivedPrivateKeyPfx = privateKeyPfx.Copy();
                OnInstallNewKeyPair?.Invoke(applicationId, profile);
                OnCredentialsInstalled?.Invoke(credentials);
                m_fixture.Lifecycle.Add("installer:verified");
                return default;
            }

            public ValueTask<ArrayOf<PullDcaRegistrar>> DiscoverAsync(CancellationToken cancellationToken)
            {
                m_fixture.Record("discover", cancellationToken);
                DiscoveryToken = cancellationToken;
                return new ValueTask<ArrayOf<PullDcaRegistrar>>(DiscoveredRegistrars);
            }

            private readonly Fixture m_fixture;
        }

        private static readonly string[] s_fullWorkflowCalls =
        [
            "connect", "provide", "identity", "reconnect", "software",
            "csr:dca", "start:dca", "finish:dca", "trust:dca", "install:dca",
            "reconnect", "managers", "register", "csr:managed", "start:managed",
            "finish:managed", "trust:managed", "install:managed"
        ];
        private static readonly string[] s_identityCalls = ["connect", "provide", "identity", "reconnect"];
        private static readonly string[] s_resumedKeyPairCalls =
        [
            "connect", "provide", "identity", "reconnect", "trust:dca", "install-keypair:dca",
            "reconnect", "managers"
        ];
        private static readonly string[] s_provideCalls = ["connect", "provide"];
        private static readonly string[] s_finishAndInstallCalls =
            ["connect", "finish:dca", "trust:dca", "install:dca"];
        private static readonly string[] s_pendingFinishCalls = ["connect", "finish:dca", "finish:dca"];
        private static readonly string[] s_installCalls = ["connect", "trust:dca", "install:dca"];
        private static readonly string[] s_finishCalls = ["connect", "finish:dca"];
        private static readonly string[] s_connectCalls = ["connect"];
        private static readonly string[] s_connectManagersCalls = ["connect", "managers"];
        private static readonly string[] s_connectProvideCalls = ["connect", "provide"];
        private static readonly string[] s_recoveryCalls =
        [
            "connect", "finish:dca", "certificates:dca", "trust:dca", "validate-recovery",
            "trust:dca", "install:dca"
        ];
        private static readonly string[] s_discoveryCalls = ["discover"];

        /// <summary>The calls made by an initial DCA new-key-pair request.</summary>
        private static readonly string[] s_initialNewKeyPairCalls =
        [
            "connect", "provide", "identity", "reconnect",
            "start-keypair:dca", "finish-keypair:dca", "trust:dca", "install-keypair:dca",
            "reconnect", "managers"
        ];

        /// <summary>The calls made when a managed application requests a new key pair.</summary>
        private static readonly string[] s_managedApplicationNewKeyPairCalls =
        [
            "connect", "provide", "identity", "reconnect",
            "csr:dca", "start:dca", "finish:dca", "trust:dca", "install:dca",
            "reconnect", "managers", "register",
            "start-keypair:managed", "finish-keypair:managed", "trust:managed", "install-keypair:managed"
        ];

        /// <summary>The calls made after a consumed new-key-pair request is restarted.</summary>
        private static readonly string[] s_restartedNewKeyPairCalls =
        [
            "connect", "provide", "identity", "reconnect",
            "start-keypair:dca", "finish-keypair:dca", "finish-keypair:dca",
            "start-keypair:dca", "finish-keypair:dca", "trust:dca", "install-keypair:dca",
            "reconnect", "managers"
        ];

        private static readonly ArrayOf<string> s_expectedTickets = ["ticket"];
        private static readonly ArrayOf<string> s_expectedDiscoveryUrls = ["opc.tcp://managed:4840"];
        private static readonly ArrayOf<string> s_expectedCapabilities = ["DA"];
        private static readonly PullDcaRegistrar kRegistrar = new(
            new Uri("opc.tcp://registrar:4840"), new NodeId("registrar", 2), new NodeId("directory", 2));
        private static readonly NodeId kDcaId = new("dca", 2);
        private static readonly NodeId kManagedId = new("managed", 2);
        private static readonly NodeId kRequestId = new("request", 2);
        private static readonly NodeId kRestartedRequestId = new("restarted-request", 2);
        private static readonly PullDcaCertificateProfile kProfile = new(
            new NodeId("group", 2), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
        private static readonly ByteString kIdentity = ByteString.From(new byte[] { 1, 2, 3 });
        private static readonly ByteString kIssuer = ByteString.From(new byte[] { 4, 5, 6 });
        private static readonly ByteString kCsr = ByteString.From(new byte[] { 7, 8, 9 });
        private static readonly ByteString kCertificate = ByteString.From(new byte[] { 10, 11, 12 });
        private static readonly ByteString kPrivateKeyPfx = ByteString.From(new byte[] { 13, 14, 15 });
    }

    /// <summary>
    /// Verifies the production transport uses generated native method proxies and bounded file transfer.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullDcaConnectionTests
    {
        [Test]
        public async Task KeyPairFinishAllowsRecreatedSessionWithSameClientCertificate()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            byte[] clientCertificate = [17, 18, 19];
            byte[] serverCertificate = [20, 21, 22];
            fixture.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(clientCertificate);
            fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(serverCertificate);
            var applicationId = new NodeId("app", 0);
            var requestId = new NodeId("request", 0);
            var registrar = new PullDcaRegistrar(
                new Uri("opc.tcp://localhost:4840"), new NodeId("registrar", 0), new NodeId("directory", 0));
            fixture.Handler = (request, _) =>
            {
                if (request is BrowseRequest)
                {
                    return new ValueTask<IServiceResponse>(
                        new BrowseResponse { Results = [new BrowseResult { References = [] }] });
                }
                if (request is CallRequest call)
                {
                    ArrayOf<Variant> output = call.MethodsToCall[0].MethodId ==
                        Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest,
                            (ServiceMessageContext)fixture.MessageContext)
                        ? [Variant.From(requestId)]
                        : [Variant.From(ByteString.From(new byte[] { 1 })),
                            Variant.From(ByteString.From(new byte[] { 2 })), Variant.From(ArrayOf<ByteString>.Empty)];
                    return new ValueTask<IServiceResponse>(
                        new CallResponse { Results = [new CallMethodResult { OutputArguments = output }] });
                }
                throw new InvalidOperationException("Unexpected key-pair request.");
            };
            var adapter = new PullDcaConnection(fixture.ManagedSession, registrar, fixture.Telemetry, 1024);
            NodeId started = await adapter.StartNewKeyPairAsync(
                applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
            Assert.That(started, Is.EqualTo(requestId));
            int callsBeforeRecreate = fixture.Requests.Count;
            NodeId previousSessionId = fixture.ManagedSession.SessionId;
            fixture.Recreate();

            Assert.Multiple(() =>
            {
                Assert.That(fixture.ManagedSession.SessionId, Is.Not.EqualTo(previousSessionId));
                Assert.That(
                    fixture.ManagedSession.TransportChannel.ClientChannelCertificate,
                    Is.EqualTo(clientCertificate));
            });
            var (certificate, privateKey, _) = await adapter.FinishNewKeyPairAsync(
                applicationId, requestId, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(certificate.IsEmpty, Is.False);
                    Assert.That(privateKey.IsEmpty, Is.False);
                    Assert.That(fixture.Requests, Has.Count.GreaterThan(callsBeforeRecreate));
                });
            }
            finally
            {
                ClearReturnedPrivateKey(privateKey);
            }
        }

        /// <summary>
        /// FinishRequest is rejected before dispatch when either channel certificate differs from StartRequest.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task KeyPairFinishRejectsChangedChannelCertificateBeforeDispatch(bool changeClientCertificate)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            byte[] clientCertificate = [17, 18, 19];
            byte[] registrarCertificate = [20, 21, 22];
            fixture.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(clientCertificate);
            fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(registrarCertificate);
            var applicationId = new NodeId("app", 0);
            var requestId = new NodeId("request", 0);
            var registrar = new PullDcaRegistrar(
                new Uri("opc.tcp://localhost:4840"), new NodeId("registrar", 0), new NodeId("directory", 0));
            fixture.Handler = (request, _) =>
            {
                if (request is BrowseRequest)
                {
                    return new ValueTask<IServiceResponse>(
                        new BrowseResponse { Results = [new BrowseResult { References = [] }] });
                }
                if (request is CallRequest call)
                {
                    ArrayOf<Variant> output = call.MethodsToCall[0].MethodId ==
                        Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest,
                            (ServiceMessageContext)fixture.MessageContext)
                        ? [Variant.From(requestId)]
                        : [Variant.From(ByteString.From(new byte[] { 1 })),
                            Variant.From(ByteString.From(new byte[] { 2 })), Variant.From(ArrayOf<ByteString>.Empty)];
                    return new ValueTask<IServiceResponse>(
                        new CallResponse { Results = [new CallMethodResult { OutputArguments = output }] });
                }
                throw new InvalidOperationException("Unexpected key-pair request.");
            };
            var adapter = new PullDcaConnection(fixture.ManagedSession, registrar, fixture.Telemetry, 1024);
            await adapter.StartNewKeyPairAsync(applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
            int callsBeforeFinish = fixture.Requests.Count;
            if (changeClientCertificate)
            {
                fixture.Channel.SetupGet(channel => channel.ClientChannelCertificate)
                    .Returns(new byte[] { 23, 24, 25 });
            }
            else
            {
                fixture.Channel.SetupGet(channel => channel.ServerChannelCertificate)
                    .Returns(new byte[] { 23, 24, 25 });
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.FinishNewKeyPairAsync(applicationId, requestId, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Requests, Has.Count.EqualTo(callsBeforeFinish));
        }

        /// <summary>
        /// Pending key-pair responses retain the authenticated request binding until output is returned.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task KeyPairFinishRetainsBindingWhileCertificateIsPending(bool pendingStatus)
        {
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            int calls = 0;
            await using var session = CreateConfidentialSession(_ =>
            {
                calls++;
                return calls switch
                {
                    1 => Good(requestId),
                    2 when pendingStatus => new CallMethodResult { StatusCode = StatusCodes.BadNothingToDo },
                    2 => Good(default(ByteString), default(ByteString), Variant.From(ArrayOf<ByteString>.Empty)),
                    _ => Good(ByteString.From(new byte[] { 10 }), ByteString.From(new byte[] { 11 }),
                        Variant.From(ArrayOf<ByteString>.Empty))
                };
            });
            var adapter = new PullDcaConnection(session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);
            Assert.That(await adapter.StartNewKeyPairAsync(applicationId, kProfile, CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(requestId));
            if (pendingStatus)
            {
                ServiceResultException? pending = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await adapter.FinishNewKeyPairAsync(applicationId, requestId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(pending!.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
            }
            else
            {
                var (pendingCertificate, pendingKey, _) = await adapter.FinishNewKeyPairAsync(
                    applicationId, requestId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(pendingCertificate.IsEmpty, Is.True);
                Assert.That(pendingKey.IsEmpty, Is.True);
            }
            var (certificate, privateKey, _) = await adapter.FinishNewKeyPairAsync(
                applicationId, requestId, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(certificate, Is.EqualTo(ByteString.From(new byte[] { 10 })));
                Assert.That(privateKey, Is.EqualTo(ByteString.From(new byte[] { 11 })));
                Assert.That(calls, Is.EqualTo(3));
                ServiceResultException? consumed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await adapter.FinishNewKeyPairAsync(applicationId, requestId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(consumed!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(calls, Is.EqualTo(3));
            }
            finally
            {
                ClearReturnedPrivateKey(privateKey);
            }
        }

        /// <summary>
        /// A consumed key-pair request must be restarted instead of retried as a signing request.
        /// </summary>
        [Test]
        public async Task KeyPairFinishBadInvalidArgumentRequiresNewStart()
        {
            var applicationId = new NodeId("app", 2);
            var firstRequestId = new NodeId("first-request", 2);
            var secondRequestId = new NodeId("second-request", 2);
            var requests = new List<CallMethodRequest>();
            int calls = 0;
            await using var session = CreateConfidentialSession(request =>
            {
                requests.Add(request);
                calls++;
                return calls switch
                {
                    1 => Good(firstRequestId),
                    2 => new CallMethodResult { StatusCode = StatusCodes.BadInvalidArgument },
                    3 => Good(secondRequestId),
                    4 => Good(
                        ByteString.From(new byte[] { 10 }),
                        ByteString.From(new byte[] { 11 }),
                        Variant.From(ArrayOf<ByteString>.Empty)),
                    _ => throw new AssertionException("Unexpected key-pair request.")
                };
            });
            var adapter = new PullDcaConnection(
                session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);

            Assert.That(await adapter.StartNewKeyPairAsync(applicationId, kProfile, CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(firstRequestId));
            ServiceResultException? consumed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.FinishNewKeyPairAsync(applicationId, firstRequestId, CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(consumed!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

            int callsBeforeOldRequestRetry = calls;
            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.FinishNewKeyPairAsync(applicationId, firstRequestId, CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(calls, Is.EqualTo(callsBeforeOldRequestRetry));

            Assert.That(await adapter.StartNewKeyPairAsync(applicationId, kProfile, CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(secondRequestId));
            var (certificate, privateKey, _) = await adapter.FinishNewKeyPairAsync(
                applicationId, secondRequestId, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(certificate, Is.EqualTo(ByteString.From(new byte[] { 10 })));
                Assert.That(privateKey, Is.EqualTo(ByteString.From(new byte[] { 11 })));
                NodeId[] expectedMethods =
                [
                    Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest,
                        (ServiceMessageContext)session.MessageContext),
                    Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest,
                        (ServiceMessageContext)session.MessageContext),
                    Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest,
                        (ServiceMessageContext)session.MessageContext),
                    Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest,
                        (ServiceMessageContext)session.MessageContext)
                ];
                Assert.That(requests.Select(request => request.MethodId), Is.EqualTo(expectedMethods));
            }
            finally
            {
                ClearReturnedPrivateKey(privateKey);
            }
        }

        [Test]
        public async Task RecoverableKeyPairStartPersistsBindingBeforeOpcUaDispatch()
        {
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            var order = new List<string>();
            var requests = new List<CallMethodRequest>();
            await using var session = CreateConfidentialSession(request =>
            {
                order.Add("opcua-start");
                requests.Add(request);
                return Good(requestId);
            });
            var adapter = new PullDcaConnection(
                session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);
            PullDcaNewKeyPairRecoveryScope scope = await adapter.CaptureNewKeyPairRecoveryScopeAsync(
                applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
            var candidate = new PullDcaNewKeyPairRecoveryBinding(scope, NodeId.Null, Guid.NewGuid());

            PullDcaNewKeyPairRecoveryBinding started = await adapter.StartRecoverableNewKeyPairAsync(
                candidate,
                (binding, _) =>
                {
                    order.Add("persist-dispatch");
                    Assert.That(binding.RequestId.IsNull, Is.True);
                    Assert.That(binding.AttemptId, Is.EqualTo(candidate.AttemptId));
                    Assert.That(binding.Scope.ApplicationId, Is.EqualTo(applicationId));
                    Assert.That(binding.Scope.Profile, Is.EqualTo(kProfile));
                    return default;
                },
                CancellationToken.None).ConfigureAwait(false);

            string[] expectedOrder = ["persist-dispatch", "opcua-start"];
            Assert.That(order, Is.EqualTo(expectedOrder));
            Assert.That(started.RequestId, Is.EqualTo(requestId));
            Assert.That(started.AttemptId, Is.EqualTo(candidate.AttemptId));
            Assert.That(started.Scope.Registrar, Is.EqualTo(kRegistrar));
            Assert.That(started.Scope.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(started.Scope.Profile, Is.EqualTo(kProfile));
            Assert.That(started.Scope.ClientChannelCertificate, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
            Assert.That(started.Scope.RegistrarChannelCertificate, Is.EqualTo(ByteString.From(new byte[] { 4, 5, 6 })));
            Assert.That(
                started.Scope.RegistrarApplicationUri,
                Is.EqualTo(session.Channel.Object.EndpointDescription.Server?.ApplicationUri));
            Assert.That(started.Scope.NamespaceUris, Is.EqualTo(session.InnerSession.NamespaceUris.ToArrayOf()));
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(
                requests[0].MethodId,
                Is.EqualTo(Resolve(
                    Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest,
                    (ServiceMessageContext)session.MessageContext)));
        }

        [Test]
        public async Task RecoverableKeyPairStartPersistenceFailurePreventsOpcUaDispatch()
        {
            var applicationId = new NodeId("app", 2);
            int calls = 0;
            await using var session = CreateConfidentialSession(_ =>
            {
                calls++;
                return Good(new NodeId("request", 2));
            });
            var adapter = new PullDcaConnection(
                session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);
            PullDcaNewKeyPairRecoveryScope scope = await adapter.CaptureNewKeyPairRecoveryScopeAsync(
                applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
            var candidate = new PullDcaNewKeyPairRecoveryBinding(scope, NodeId.Null, Guid.NewGuid());
            var failure = new InvalidOperationException("durable dispatch write failed");

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await adapter.StartRecoverableNewKeyPairAsync(
                    candidate,
                    (_, _) => throw failure,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(calls, Is.Zero);
        }

        [TestCase("endpoint")]
        [TestCase("registrar")]
        [TestCase("directory")]
        [TestCase("application")]
        [TestCase("profile-group")]
        [TestCase("profile-type")]
        [TestCase("client-channel")]
        [TestCase("registrar-channel")]
        [TestCase("registrar-application")]
        [TestCase("namespace")]
        public async Task RecoverableKeyPairBindingRejectsScopeMismatchBeforeTransport(string mismatch)
        {
            int calls = 0;
            await using var session = CreateConfidentialSession(_ =>
            {
                calls++;
                return Good(new NodeId("unexpected", 2));
            });
            var adapter = new PullDcaConnection(
                session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);
            PullDcaNewKeyPairRecoveryScope scope = await adapter.CaptureNewKeyPairRecoveryScopeAsync(
                new NodeId("app", 2), kProfile, CancellationToken.None).ConfigureAwait(false);
            scope = mismatch switch
            {
                "endpoint" => scope with
                {
                    Registrar = scope.Registrar with { EndpointUrl = new Uri("opc.tcp://other:4840") }
                },
                "registrar" => scope with
                {
                    Registrar = scope.Registrar with { RegistrarId = new NodeId("other-registrar", 2) }
                },
                "directory" => scope with
                {
                    Registrar = scope.Registrar with { CertificateDirectoryId = new NodeId("other-directory", 2) }
                },
                "application" => scope with { ApplicationId = new NodeId("other-app", 2) },
                "profile-group" => scope with
                {
                    Profile = scope.Profile with { CertificateGroupId = new NodeId("other-group", 2) }
                },
                "profile-type" => scope with
                {
                    Profile = scope.Profile with { CertificateTypeId = new NodeId("other-type", 2) }
                },
                "client-channel" => scope with
                {
                    ClientChannelCertificate = ByteString.From(new byte[] { 70, 71, 72 })
                },
                "registrar-channel" => scope with
                {
                    RegistrarChannelCertificate = ByteString.From(new byte[] { 73, 74, 75 })
                },
                "registrar-application" => scope with { RegistrarApplicationUri = "urn:registrar:other" },
                "namespace" => scope with { NamespaceUris = ["urn:other:namespace"] },
                _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
            };
            var binding = new PullDcaNewKeyPairRecoveryBinding(
                scope, new NodeId("request", 2), Guid.NewGuid());

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ValidateNewKeyPairRecoveryBindingAsync(binding, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(
                error!.StatusCode,
                Is.EqualTo(mismatch == "namespace"
                    ? StatusCodes.BadNodeIdUnknown
                    : StatusCodes.BadSecurityChecksFailed));
            Assert.That(calls, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NewKeyPairRequestsRequireConfidentialDispatch(bool finish)
        {
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            await using var session = CreateConfidentialSession(_ => Good(requestId));
            bool rejectCapture = !finish;
            var adapter = new PullDcaConnection(
                session.InnerSession,
                kRegistrar,
                session.Telemetry,
                1024,
                _ => default,
                ct => rejectCapture
                    ? throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient)
                    : SessionRequestContext.CaptureConfidentialAsync(session.ManagedSession, ct));
            if (finish)
            {
                await adapter.StartNewKeyPairAsync(applicationId, kProfile, CancellationToken.None)
                    .ConfigureAwait(false);
                rejectCapture = true;
            }
            int callsBeforeRejection = session.Requests.Count;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (finish)
                {
                    await adapter.FinishNewKeyPairAsync(
                        applicationId, requestId, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await adapter.StartNewKeyPairAsync(
                        applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
                }
            });

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(session.Requests, Has.Count.EqualTo(callsBeforeRejection));
        }

        [Test]
        public async Task ProductionAdapterUsesGeneratedRegistrarAndCertificateDirectoryMethods()
        {
            var requests = new List<CallMethodRequest>();
            var manager = new ManagerDescription
            {
                PurposeUri = "urn:manager",
                EndpointUrls = ["opc.tcp://host:4840"]
            };
            var ticket = new DeviceIdentityTicketType { ProductInstanceUri = "urn:device" };
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            ByteString certificate = ByteString.From(new byte[] { 1, 2, 3 });
            ArrayOf<ByteString> issuers = [certificate];
            ArrayOf<ManagerDescription> managers = [manager];
            await using var session = CreateConfidentialSession(request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(
                        certificate, Variant.FromStructure(ticket), applicationId, Variant.FromStructure(manager)),
                    2 => Good(Variant.FromStructure(managers)),
                    3 => Good(applicationId),
                    4 => Good(requestId),
                    5 => Good(certificate, default(ByteString), Variant.From(issuers)),
                    _ => Good(Variant.From(new[] { kProfile.CertificateTypeId }.ToArrayOf()), Variant.From(issuers))
                };
            });
            var context = (ServiceMessageContext)session.MessageContext;
            int reconnects = 0;
            var adapter = new PullDcaConnection(session.InnerSession, kRegistrar, session.Telemetry, 1024, _ =>
            {
                reconnects++;
                return default;
            });
            var offer = new PullDcaIdentityOffer([certificate], issuers, ["ticket"]);
            var application = new PullDcaManagedApplication(
                new ApplicationRecordDataType { ApplicationUri = "urn:managed" },
                "urn:opcua", kProfile);

            PullDcaIdentitySelection identity = await adapter.ProvideIdentitiesAsync(offer, CancellationToken.None)
                .ConfigureAwait(false);
            await adapter.ReloadAndReconnectAsync(CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ManagerDescription> actualManagers = await adapter.GetManagersAsync(CancellationToken.None)
                .ConfigureAwait(false);
            NodeId actualApplicationId = await adapter.RegisterAsync(application, CancellationToken.None)
                .ConfigureAwait(false);
            NodeId actualRequestId = await adapter.StartAsync(
                applicationId, kProfile, certificate, CancellationToken.None)
                .ConfigureAwait(false);
            var (issued, actualIssuers) = await adapter.FinishAsync(applicationId, requestId, CancellationToken.None)
                .ConfigureAwait(false);
            var (assignedTypes, assignedCertificates) = await adapter.GetCertificatesAsync(
                applicationId, kProfile.CertificateGroupId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(identity.SelectedIdentity, Is.EqualTo(certificate));
            Assert.That(identity.MatchingTicket, Is.SameAs(ticket));
            Assert.That(identity.SoftwareUpdateManager, Is.SameAs(manager));
            Assert.That(actualManagers, Is.EqualTo(managers));
            Assert.That(actualApplicationId, Is.EqualTo(applicationId));
            Assert.That(actualRequestId, Is.EqualTo(requestId));
            Assert.That(issued, Is.EqualTo(certificate));
            Assert.That(actualIssuers, Is.EqualTo(issuers));
            Assert.That(assignedTypes, Is.EqualTo(new[] { kProfile.CertificateTypeId }.ToArrayOf()));
            Assert.That(assignedCertificates, Is.EqualTo(issuers));
            Assert.That(reconnects, Is.EqualTo(1));
            Assert.That(requests.Select(r => r.MethodId), Is.EqualTo(new[]
            {
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_ProvideIdentities, context),
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_GetManagers, context),
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_RegisterManagedApplication, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, context)
            }));
            Assert.That(requests.Take(3).All(r => r.ObjectId == kRegistrar.RegistrarId), Is.True);
            Assert.That(requests.Skip(3).All(r => r.ObjectId == kRegistrar.CertificateDirectoryId), Is.True);
            Assert.That(requests[0].InputArguments[0].TryGetValue(out ArrayOf<ByteString> sentIdentities), Is.True);
            Assert.That(sentIdentities, Is.EqualTo(offer.Identities));
            Assert.That(requests[0].InputArguments[2].TryGetValue(out ArrayOf<string> sentTickets), Is.True);
            Assert.That(sentTickets, Is.EqualTo(offer.Tickets));
            Assert.That(requests[3].InputArguments[0].TryGetValue(out NodeId sentApplicationId), Is.True);
            Assert.That(sentApplicationId, Is.EqualTo(applicationId));
            Assert.That(requests[3].InputArguments[3].TryGetValue(out ByteString sentCsr), Is.True);
            Assert.That(sentCsr, Is.EqualTo(certificate));
            Assert.That(session.InnerSession.Connected, Is.True);
        }

        [Test]
        public async Task ProductionAdapterUsesGeneratedNewKeyPairAndFinishProxies()
        {
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            ByteString certificate = ByteString.From(new byte[] { 1, 2 });
            ByteString privateKeyPfx = ByteString.From(new byte[] { 3, 4 });
            ArrayOf<ByteString> issuers = [ByteString.From(new byte[] { 5, 6 })];
            var requests = new List<CallMethodRequest>();
            await using var session = CreateConfidentialSession(request =>
            {
                requests.Add(request);
                return requests.Count == 1
                    ? Good(requestId)
                    : Good(certificate, privateKeyPfx, Variant.From(issuers));
            });
            var context = (ServiceMessageContext)session.MessageContext;
            var adapter = new PullDcaConnection(session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);

            NodeId actualRequestId = await adapter.StartNewKeyPairAsync(
                applicationId, kProfile, CancellationToken.None).ConfigureAwait(false);
            var (actualCertificate, actualPrivateKeyPfx, actualIssuers) = await adapter.FinishNewKeyPairAsync(
                applicationId, requestId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(actualRequestId, Is.EqualTo(requestId));
            Assert.That(actualCertificate, Is.EqualTo(certificate));
            Assert.That(actualPrivateKeyPfx, Is.EqualTo(privateKeyPfx));
            Assert.That(actualIssuers, Is.EqualTo(issuers));
            Assert.That(requests.Select(request => request.MethodId), Is.EqualTo(new[]
            {
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartNewKeyPairRequest, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest, context)
            }));
            Assert.That(requests[0].InputArguments[0].TryGetValue(out NodeId sentApplicationId), Is.True);
            Assert.That(sentApplicationId, Is.EqualTo(applicationId));
            Assert.That(requests[0].InputArguments[1].TryGetValue(out NodeId sentGroupId), Is.True);
            Assert.That(sentGroupId, Is.EqualTo(kProfile.CertificateGroupId));
            Assert.That(requests[0].InputArguments[2].TryGetValue(out NodeId sentTypeId), Is.True);
            Assert.That(sentTypeId, Is.EqualTo(kProfile.CertificateTypeId));
            Assert.That(requests[0].InputArguments[3].TryGetValue(out string subjectName), Is.True);
            Assert.That(subjectName, Is.Empty);
            Assert.That(requests[0].InputArguments[4].TryGetValue(out ArrayOf<string> domains), Is.True);
            Assert.That(domains.IsEmpty, Is.True);
            Assert.That(requests[0].InputArguments[5].TryGetValue(out string privateKeyFormat), Is.True);
            Assert.That(privateKeyFormat, Is.EqualTo("PFX"));
            Assert.That(requests[0].InputArguments[6].TryGetValue(out string privateKeyPassword), Is.True);
            Assert.That(privateKeyPassword, Is.Empty);
            Assert.That(session.InnerSession.Connected, Is.True);
        }

        [Test]
        public async Task SigningResponseCannotUnexpectedlySupplyPrivateKeyMaterial()
        {
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            ByteString certificate = ByteString.From(new byte[] { 1 });
            byte[] privateKey = [2, 3, 4];
            NodeId startSigningRequestMethodId = NodeId.Null;
            await using var session = CreateConfidentialSession(request =>
                request.ObjectId == kRegistrar.CertificateDirectoryId
                    ? request.MethodId == startSigningRequestMethodId
                        ? Good(requestId)
                        : Good(certificate, ByteString.From(privateKey), Variant.From(ArrayOf<ByteString>.Null))
                    : throw new AssertionException("Unexpected certificate-directory object."));
            startSigningRequestMethodId = Resolve(
                Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest,
                (ServiceMessageContext)session.MessageContext);
            var adapter = new PullDcaConnection(
                session.InnerSession, kRegistrar, session.Telemetry, 1024, _ => default);
            await ((IPullDcaBoundSigningConnection)adapter).StartWithEvidenceAsync(
                applicationId, kProfile, certificate, certificate, CancellationToken.None).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.FinishAsync(applicationId, requestId, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(privateKey, Is.EqualTo(new byte[3]));
        }

        [Test]
        public async Task TrustDownloadUsesGeneratedFileMethodsEnforcesBoundAndClosesHandleWithoutDisposingSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            var requests = new List<CallMethodRequest>();
            var trustListId = new NodeId("trust", 2);
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(trustListId),
                    2 => Good(42u),
                    3 => Good(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })),
                    _ => Good()
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 4, _ => default);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ReadTrustListAsync(
                    new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(requests.Select(r => r.MethodId), Is.EqualTo(new NodeId[]
            {
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetTrustList, context),
                Opc.Ua.MethodIds.FileType_Open,
                Opc.Ua.MethodIds.FileType_Read,
                Opc.Ua.MethodIds.FileType_Close
            }));
            Assert.That(requests.Skip(1).All(r => r.ObjectId == trustListId), Is.True);
            Assert.That(requests[^1].InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42u));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// File cleanup failure cannot hide the original trust-list transfer rejection.
        /// </summary>
        [Test]
        public async Task TrustDownloadAndHandleCleanupFailuresAreBothReported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            var requests = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(new NodeId("trust", 2)),
                    2 => Good(42u),
                    3 => Good(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })),
                    _ => new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied }
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 4, _ => default);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await adapter.ReadTrustListAsync(
                    new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(error.InnerExceptions[1], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(requests[^1].MethodId, Is.EqualTo(Opc.Ua.MethodIds.FileType_Close));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Transfer cancellation and transport errors retain their original exception when cleanup succeeds,
        /// and remain observable together with a cleanup error when both operations fail.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TrustDownloadPreservesTransferExceptionAndIndependentCleanupFailure(
            bool canceled, bool cleanupFails)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            Exception transfer = canceled
                ? new OperationCanceledException("Transfer canceled.")
                : new ServiceResultException(StatusCodes.BadCommunicationError);
            var cleanup = new ServiceResultException(StatusCodes.BadConnectionClosed);
            var requests = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(new NodeId("trust", 2)),
                    2 => Good(42u),
                    3 => throw transfer,
                    _ when cleanupFails => throw cleanup,
                    _ => Good()
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 1024, _ => default);

            if (cleanupFails)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error!.InnerExceptions, Is.EqualTo(new[] { transfer, cleanup }));
            }
            else if (canceled)
            {
                OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transfer));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transfer));
            }
            Assert.That(requests, Has.Count.EqualTo(4));
            Assert.That(requests[3].MethodId, Is.EqualTo(Opc.Ua.MethodIds.FileType_Close));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Supplies a real confidential dispatch owner for certificate-directory transport tests.
        /// </summary>
        private static SessionRequestContextTestContext CreateConfidentialSession(
            Func<CallMethodRequest, CallMethodResult> handler)
        {
            var session = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            session.MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            session.Channel.Object.EndpointDescription.EndpointUrl = kRegistrar.EndpointUrl.AbsoluteUri;
            session.Channel.SetupGet(channel => channel.ClientChannelCertificate).Returns(new byte[] { 1, 2, 3 });
            session.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(new byte[] { 4, 5, 6 });
            session.Handler = (request, _) => request switch
            {
                BrowseRequest => new ValueTask<IServiceResponse>(
                    new BrowseResponse { Results = [new BrowseResult { References = [] }] }),
                CallRequest call => new ValueTask<IServiceResponse>(
                    new CallResponse { Results = [handler(call.MethodsToCall[0])] }),
                _ => throw new AssertionException("Unexpected certificate-directory transport request.")
            };
            return session;
        }

        private static Mock<ISession> CreateSession(
            ServiceMessageContext context,
            Func<CallMethodRequest, CallMethodResult> handler)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(context);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) => new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [handler(requests[0])]
                    }));
            return session;
        }

        private static CallMethodResult Good(params Variant[] outputs)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs };
        }

        private static NodeId Resolve(ExpandedNodeId methodId, ServiceMessageContext context)
        {
            return ExpandedNodeId.ToNodeId(methodId, context.NamespaceUris);
        }

        private static void ClearReturnedPrivateKey(ByteString privateKey)
        {
            if (MemoryMarshal.TryGetArray(privateKey.Memory, out ArraySegment<byte> bytes) && bytes.Array != null)
            {
                Array.Clear(bytes.Array, bytes.Offset, bytes.Count);
            }
        }

        private static readonly PullDcaRegistrar kRegistrar = new(
            new Uri("opc.tcp://registrar:4840"), new NodeId("registrar", 2), new NodeId("directory", 2));
        private static readonly PullDcaCertificateProfile kProfile = new(
            new NodeId("group", 2), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
    }
}
