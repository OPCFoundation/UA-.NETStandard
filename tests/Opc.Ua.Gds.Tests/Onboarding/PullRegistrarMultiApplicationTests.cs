// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using OnboardingClientSample;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using OnboardingObjectIds = Opc.Ua.Onboarding.ObjectIds;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    public sealed partial class PullRegistrarIntegrationTests
    {
        /// <summary>
        /// Provisions two independently keyed applications and rejects an unapproved application over the same channel.
        /// </summary>
        [Test]
        public async Task PullWorkflowProvisionsTwoManagedApplicationsAndRejectsAnUnapprovedApplication()
        {
            const string protocol = "urn:test:opc-ua";
            await using var first = new ClientFixture(telemetry: m_telemetry);
            await using var second = new ClientFixture(telemetry: m_telemetry);
            await first.LoadClientConfigurationAsync(Path.Combine(m_root, "managed-first"), "ManagedFirst")
                .ConfigureAwait(false);
            await second.LoadClientConfigurationAsync(Path.Combine(m_root, "managed-second"), "ManagedSecond")
                .ConfigureAwait(false);
            string firstUri = first.Config.ApplicationUri ??
                throw new AssertionException("The first managed application must have an application URI.");
            string secondUri = second.Config.ApplicationUri ??
                throw new AssertionException("The second managed application must have an application URI.");
            ByteString firstIdentity = GetIdentity(first);
            ByteString secondIdentity = GetIdentity(second);
            byte[] dcaKey = GetRsaModulus(m_identity);
            byte[] firstKey = GetRsaModulus(firstIdentity);
            byte[] secondKey = GetRsaModulus(secondIdentity);
            Assert.That(firstUri, Is.Not.EqualTo(secondUri));
            Assert.That(firstKey, Is.Not.EqualTo(secondKey));
            Assert.That(firstKey, Is.Not.EqualTo(dcaKey));
            Assert.That(secondKey, Is.Not.EqualTo(dcaKey));

            m_applicationAuthorization.Setup(value => value.CanManageAsync(
                It.IsAny<string>(), It.IsAny<ApplicationRecordDataType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string product, ApplicationRecordDataType application, string requestedProtocol,
                    CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(
                        string.Equals(product, m_product, StringComparison.Ordinal) &&
                        string.Equals(requestedProtocol, protocol, StringComparison.Ordinal) &&
                        (string.Equals(application.ApplicationUri, firstUri, StringComparison.Ordinal) ||
                            string.Equals(application.ApplicationUri, secondUri, StringComparison.Ordinal)));
                });

            await m_otherSession.CloseAsync().ConfigureAwait(false);
            await m_otherSession.DisposeAsync().ConfigureAwait(false);
            m_otherSession = await ConnectAsync(
                m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password)))
                .ConfigureAwait(false);
            var software = new Mock<IPullDcaSoftwareUpdateProvider>(MockBehavior.Strict);
            software.Setup(value => value.CompleteAsync(
                It.IsAny<PullDcaRegistrar>(), It.IsAny<PullDcaIdentitySelection>(),
                It.IsAny<ManagerDescription>(), It.IsAny<CancellationToken>()))
                .Returns(async (PullDcaRegistrar _, PullDcaIdentitySelection identity,
                    ManagerDescription manager, CancellationToken ct) =>
                {
                    Assert.That(identity.SelectedIdentity, Is.EqualTo(m_identity));
                    Assert.That(manager.PurposeUri, Is.EqualTo("urn:test:software-approval"));
                    await Registrar(m_otherSession).UpdateSoftwareStatusAsync(m_product, true, "approved", ct)
                        .ConfigureAwait(false);
                });

            Assert.That(m_provider.GetRequiredService<IUserDatabase>().CreateUser(
                "device-user", Encoding.UTF8.GetBytes(m_password), [Role.AuthenticatedUser]), Is.True);
            await using ManagedSession session = await ManagedSession.CreateAsync(
                m_device.Config, m_device.Endpoint, m_device.SessionFactory,
                identity: new UserIdentity("device-user", Encoding.UTF8.GetBytes(m_password)), telemetry: m_telemetry)
                .ConfigureAwait(false);
            session.DisableAutoReconnectOnCertificateChange = true;
            session.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            var registrar = new PullDcaRegistrar(
                session.ConfiguredEndpoint.EndpointUrl ??
                    throw new AssertionException("The connected registrar must have an endpoint URI."),
                ExpandedNodeId.ToNodeId(OnboardingObjectIds.DeviceRegistrar, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris));
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, session.NamespaceUris),
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            var dcaCredentials = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
            IApplicationsDatabase database = m_provider.GetRequiredService<IApplicationsDatabase>();
            var credentials = new ApplicationCredentialRouter(
                new DirectoryTypeClient(session, registrar.CertificateDirectoryId, m_telemetry),
                new Dictionary<string, IPullDcaCredentialProvider>(StringComparer.Ordinal)
                {
                    [m_product] = dcaCredentials,
                    [firstUri] = new OnboardingDeviceCredentials(first.Config, m_telemetry),
                    [secondUri] = new OnboardingDeviceCredentials(second.Config, m_telemetry)
                });
            var workflow = new PullDcaWorkflow(
                new PullDcaOptions { OperationTimeout = TimeSpan.FromSeconds(90) },
                (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<ManagedSession>(session);
                },
                dcaCredentials,
                credentials,
                m_telemetry,
                software.Object);
            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            var request = new PullDcaRequest(
                new PullDcaIdentityOffer([m_identity], [], [ticket]),
                profile,
                [
                    new PullDcaManagedApplication(CreateApplicationRecord(first.Config), protocol, profile),
                    new PullDcaManagedApplication(CreateApplicationRecord(second.Config), protocol, profile)
                ]);

            PullDcaResult result = await workflow.RunAsync(registrar, request).ConfigureAwait(false);

            Assert.That(result.Identity.SelectedIdentity, Is.EqualTo(m_identity));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(2));
            PullDcaProvisionedApplication firstResult = result.ManagedApplications[0];
            PullDcaProvisionedApplication secondResult = result.ManagedApplications[1];
            Assert.That(firstResult.ApplicationId.IsNull, Is.False);
            Assert.That(secondResult.ApplicationId.IsNull, Is.False);
            Assert.That(firstResult.ApplicationId, Is.Not.EqualTo(secondResult.ApplicationId));
            Assert.That(firstResult.ApplicationId, Is.Not.EqualTo(result.Identity.ApplicationId));
            Assert.That(secondResult.ApplicationId, Is.Not.EqualTo(result.Identity.ApplicationId));
            Assert.That(database.GetApplication(firstResult.ApplicationId)?.ApplicationUri, Is.EqualTo(firstUri));
            Assert.That(database.GetApplication(secondResult.ApplicationId)?.ApplicationUri, Is.EqualTo(secondUri));
            Assert.That(GetIdentity(m_device), Is.EqualTo(result.DcaCredentials.Certificate));
            Assert.That(GetIdentity(first), Is.EqualTo(firstResult.Credentials.Certificate));
            Assert.That(GetIdentity(second), Is.EqualTo(secondResult.Credentials.Certificate));
            Assert.That(result.DcaCredentials.Certificate, Is.Not.EqualTo(m_identity));
            Assert.That(firstResult.Credentials.Certificate, Is.Not.EqualTo(firstIdentity));
            Assert.That(secondResult.Credentials.Certificate, Is.Not.EqualTo(secondIdentity));
            Assert.That(firstResult.Credentials.Certificate, Is.Not.EqualTo(secondResult.Credentials.Certificate));
            Assert.That(GetRsaModulus(result.DcaCredentials.Certificate), Is.EqualTo(dcaKey));
            Assert.That(GetRsaModulus(firstResult.Credentials.Certificate), Is.EqualTo(firstKey));
            Assert.That(GetRsaModulus(secondResult.Credentials.Certificate), Is.EqualTo(secondKey));
            await AssertInstalledTrustListAsync(m_device, result.DcaCredentials.TrustList).ConfigureAwait(false);
            await AssertInstalledTrustListAsync(first, firstResult.Credentials.TrustList).ConfigureAwait(false);
            await AssertInstalledTrustListAsync(second, secondResult.Credentials.TrustList).ConfigureAwait(false);

            DeviceRegistrarDevice? device = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.DcaCertificate, Is.EqualTo(result.DcaCredentials.Certificate));
            Assert.That(device.PendingDcaCertificate.IsEmpty, Is.True);
            Assert.That(device.SoftwareStatus, Is.True);
            Assert.That(device.Applications.Count, Is.EqualTo(2));
            Assert.That(device.Applications[0], Is.EqualTo(new DeviceRegistrarApplication(
                firstResult.ApplicationId, protocol)));
            Assert.That(device.Applications[1], Is.EqualTo(new DeviceRegistrarApplication(
                secondResult.ApplicationId, protocol)));
            software.Verify(value => value.CompleteAsync(
                It.IsAny<PullDcaRegistrar>(), It.IsAny<PullDcaIdentitySelection>(),
                It.IsAny<ManagerDescription>(), It.IsAny<CancellationToken>()), Times.Once);

            const string deniedUri = "urn:test:unapproved-managed-application";
            var denied = new ApplicationRecordDataType
            {
                ApplicationUri = deniedUri,
                ApplicationType = ApplicationType.Client,
                ApplicationNames = [new LocalizedText("Unapproved application")],
                ProductUri = "urn:test:unapproved-product"
            };
            await AssertStatusAsync(StatusCodes.BadRequestNotAllowed, async () =>
                await Registrar(session).RegisterManagedApplicationAsync(denied, protocol).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(database.FindApplications(deniedUri), Is.Null.Or.Empty);
            m_applicationAuthorization.Verify(value => value.CanManageAsync(
                m_product, It.Is<ApplicationRecordDataType>(application => application.ApplicationUri == firstUri),
                protocol, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            m_applicationAuthorization.Verify(value => value.CanManageAsync(
                m_product, It.Is<ApplicationRecordDataType>(application => application.ApplicationUri == secondUri),
                protocol, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            m_applicationAuthorization.Verify(value => value.CanManageAsync(
                m_product, It.Is<ApplicationRecordDataType>(application => application.ApplicationUri == deniedUri),
                protocol, It.IsAny<CancellationToken>()), Times.Once);
            var (_, firstCertificates) = await DirectoryClient(session).GetCertificatesAsync(
                firstResult.ApplicationId, profile.CertificateGroupId).ConfigureAwait(false);
            Assert.That(firstCertificates.Contains(firstResult.Credentials.Certificate), Is.True);
            var (_, secondCertificates) = await DirectoryClient(session).GetCertificatesAsync(
                secondResult.ApplicationId, profile.CertificateGroupId).ConfigureAwait(false);
            Assert.That(secondCertificates.Contains(secondResult.Credentials.Certificate), Is.True);
            device = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(device!.Applications.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Creates the registration record from the managed application's own configuration.
        /// </summary>
        private static ApplicationRecordDataType CreateApplicationRecord(ApplicationConfiguration configuration)
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationType = ApplicationType.Client,
                ApplicationNames = [new LocalizedText(configuration.ApplicationName)],
                ProductUri = configuration.ProductUri
            };
        }

        /// <summary>
        /// Compares the installed trust material with the list received for one application.
        /// </summary>
        private static async Task AssertInstalledTrustListAsync(
            ClientFixture client,
            TrustListDataType expected)
        {
            Assert.That(expected.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            Assert.That(expected.TrustedCertificates.Count + expected.IssuerCertificates.Count, Is.GreaterThan(0));
            ICertificateManager certificates = client.Config.CertificateManager ??
                throw new AssertionException("The application must have a certificate manager.");
            using TrustListData installed = await certificates.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All).ConfigureAwait(false);
            Assert.That(installed.TrustedCertificates
                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                Is.EquivalentTo(expected.TrustedCertificates.ToList()));
            Assert.That(installed.IssuerCertificates
                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                Is.EquivalentTo(expected.IssuerCertificates.ToList()));
            Assert.That(installed.TrustedCrls
                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                Is.EquivalentTo(expected.TrustedCrls.ToList()));
            Assert.That(installed.IssuerCrls
                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                Is.EquivalentTo(expected.IssuerCrls.ToList()));
        }

        /// <summary>
        /// Extracts the public key modulus to distinguish keys from distinct certificate encodings.
        /// </summary>
        private static byte[] GetRsaModulus(ByteString rawCertificate)
        {
            using Certificate certificate = Certificate.FromRawData(rawCertificate);
            using RSA key = certificate.GetRSAPublicKey() ??
                throw new AssertionException("The application certificate must use RSA.");
            return key.ExportParameters(false).Modulus ??
                throw new AssertionException("The application certificate must contain an RSA modulus.");
        }

        /// <summary>
        /// Routes server-assigned application identifiers to separate local credential owners.
        /// </summary>
        private sealed class ApplicationCredentialRouter : IPullDcaCredentialProvider
        {
            /// <summary>
            /// Borrows the directory client and the credential owner for each allowed application URI.
            /// </summary>
            public ApplicationCredentialRouter(
                DirectoryTypeClient directory,
                Dictionary<string, IPullDcaCredentialProvider> providers)
            {
                m_directory = directory;
                m_providers = providers;
            }

            /// <inheritdoc/>
            public async ValueTask<ByteString> CreateSigningRequestAsync(
                NodeId applicationId,
                PullDcaCertificateProfile profile,
                CancellationToken cancellationToken = default)
            {
                IPullDcaCredentialProvider provider = await GetProviderAsync(applicationId, cancellationToken)
                    .ConfigureAwait(false);
                return await provider.CreateSigningRequestAsync(applicationId, profile, cancellationToken)
                    .ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask InstallAsync(
                NodeId applicationId,
                PullDcaCredentials credentials,
                CancellationToken cancellationToken = default)
            {
                IPullDcaCredentialProvider provider = await GetProviderAsync(applicationId, cancellationToken)
                    .ConfigureAwait(false);
                await provider.InstallAsync(applicationId, credentials, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Rejects an unknown identifier instead of selecting another application's private key.
            /// </summary>
            private async ValueTask<IPullDcaCredentialProvider> GetProviderAsync(
                NodeId applicationId,
                CancellationToken cancellationToken)
            {
                ApplicationRecordDataType application = await m_directory.GetApplicationAsync(
                    applicationId, cancellationToken).ConfigureAwait(false);
                string? applicationUri = application.ApplicationUri;
                if (application.ApplicationId != applicationId || applicationUri == null ||
                    !m_providers.TryGetValue(applicationUri, out var provider))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                return provider;
            }

            /// <summary>
            /// The authenticated directory connection.
            /// </summary>
            private readonly DirectoryTypeClient m_directory;

            /// <summary>
            /// Locally approved credential owners, keyed by application URI.
            /// </summary>
            private readonly Dictionary<string, IPullDcaCredentialProvider> m_providers;
        }

        /// <summary>
        /// The per-application authorization seam, delegating to the sample policy by default.
        /// </summary>
        private Mock<IDeviceRegistrarApplicationAuthorization> m_applicationAuthorization = null!;
    }
}
