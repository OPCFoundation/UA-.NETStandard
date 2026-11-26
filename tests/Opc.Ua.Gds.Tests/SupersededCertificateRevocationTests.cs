// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// End-to-end tests for the certificates a GDS issued to an application
    /// over its lifetime: a replaced certificate no longer acts for the
    /// application and can still be revoked, and revocations that run at
    /// the same time all reach the CRL.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [NonParallelizable]
    public class SupersededCertificateRevocationTests
    {
        private const int kParallelRevocations = 8;

        private GlobalDiscoveryTestServer m_server = null!;
        private GlobalDiscoveryTestClient m_gdsClient = null!;
        private CertificateIdentifier m_adminCertificate = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_server = await TestUtils.StartGDSAsync(clean: true).ConfigureAwait(false);
            m_gdsClient = new GlobalDiscoveryTestClient(true, NUnitTelemetryContext.Create());
            await m_gdsClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            m_adminCertificate = m_gdsClient.Configuration.SecurityConfiguration.ApplicationCertificate!;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_gdsClient != null)
            {
                await m_gdsClient.DisconnectClientAsync().ConfigureAwait(false);
                m_gdsClient.Dispose();
                m_gdsClient = null!;
            }
            if (m_server != null)
            {
                await m_server.DisposeAsync().ConfigureAwait(false);
                m_server = null!;
            }
        }

        [SetUp]
        public void SetUp()
        {
            m_gdsClient.Configuration.SecurityConfiguration.ApplicationCertificate = m_adminCertificate;
        }

        [Test]
        public async Task SupersededCertificateLosesSelfAdminAndCanBeRevokedAsync()
        {
            ApplicationConfiguration configuration = m_gdsClient.Configuration;

            // The admin issues cert1; the test client now uses it.
            Assert.That(await m_gdsClient.RegisterTestClientAtGdsAsync().ConfigureAwait(false), Is.True);
            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;
            NodeId applicationId = application.ApplicationRecord.ApplicationId;
            CertificateIdentifier oldCertificateId = configuration.SecurityConfiguration.ApplicationCertificate!;
            ByteString oldCertificate = application.Certificate.ToByteString();

            // The application renews itself to cert2 over a cert1 channel
            // (anonymous, ApplicationSelfAdmin). The channel stays usable
            // until the application has installed cert2.
            ByteString newCertificate;
            await using (GlobalDiscoveryServerClient self = CreateClient(m_gdsClient.Anonymous))
            {
                await self.ConnectAsync().ConfigureAwait(false);
                NodeId requestId = await self.StartNewKeyPairRequestAsync(
                    applicationId,
                    default,
                    default,
                    application.Subject,
                    application.DomainNames,
                    "PEM",
                    []).ConfigureAwait(false);
                (newCertificate, ByteString privateKey, _) = await self.FinishRequestAsync(
                    applicationId,
                    requestId).ConfigureAwait(false);
                Assert.That(newCertificate.IsEmpty, Is.False);
                Assert.That(privateKey.IsEmpty, Is.False);
                ApplicationRecordDataType record = await self.GetApplicationAsync(applicationId)
                    .ConfigureAwait(false);
                Assert.That(record.ApplicationId, Is.EqualTo(applicationId));
            }
            Assert.That(newCertificate, Is.Not.EqualTo(oldCertificate));

            // A new session with the superseded cert1 no longer gets
            // ApplicationSelfAdmin.
            await using (GlobalDiscoveryServerClient old = CreateClient(m_gdsClient.Anonymous))
            {
                await old.ConnectAsync().ConfigureAwait(false);
                ServiceResultException? denied = null;
                try
                {
                    await old.GetApplicationAsync(applicationId).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    denied = ex;
                }
                Assert.That(denied, Is.Not.Null, "The superseded certificate still acts for the application.");
                Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            }

            // The CA admin can revoke cert1, although the record holds cert2.
            configuration.SecurityConfiguration.ApplicationCertificate = m_adminCertificate;
            await using (GlobalDiscoveryServerClient admin = CreateClient(m_gdsClient.AdminUser))
            {
                await admin.ConnectAsync().ConfigureAwait(false);
                await admin.RevokeCertificateAsync(applicationId, oldCertificate).ConfigureAwait(false);
                (StatusCode oldStatus, _) = await admin.CheckRevocationStatusAsync(oldCertificate)
                    .ConfigureAwait(false);
                Assert.That(oldStatus, Is.EqualTo((StatusCode)StatusCodes.BadCertificateRevoked));
                (StatusCode newStatus, _) = await admin.CheckRevocationStatusAsync(newCertificate)
                    .ConfigureAwait(false);
                Assert.That(newStatus, Is.EqualTo((StatusCode)StatusCodes.Good));
            }

            // The revoked cert1 can no longer open a SecureChannel. Only the
            // connect may fail here: cert1 already lost ApplicationSelfAdmin, so
            // a later call would fail even on an open channel.
            configuration.SecurityConfiguration.ApplicationCertificate = oldCertificateId;
            await using GlobalDiscoveryServerClient revoked = CreateClient(m_gdsClient.Anonymous);
            ServiceResultException? failure = null;
            try
            {
                await revoked.ConnectAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                failure = ex;
            }
            Assert.That(failure, Is.Not.Null, "The revoked certificate can still open a SecureChannel.");
        }

        [Test]
        public async Task RevokeCertificateRejectsCertificateOfAnotherApplicationAsync()
        {
            await using GlobalDiscoveryServerClient admin = CreateClient(m_gdsClient.AdminUser);
            await admin.ConnectAsync().ConfigureAwait(false);
            (_, ByteString firstCertificate) = await RegisterAndIssueAsync(admin, "Owner")
                .ConfigureAwait(false);
            (NodeId second, _) = await RegisterAndIssueAsync(admin, "Other").ConfigureAwait(false);

            ServiceResultException? ex = null;
            try
            {
                await admin.RevokeCertificateAsync(second, firstCertificate).ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                ex = e;
            }
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            (StatusCode status, _) = await admin.CheckRevocationStatusAsync(firstCertificate)
                .ConfigureAwait(false);
            Assert.That(status, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public async Task ParallelRevocationsAllReachTheCrlAsync()
        {
            var applications = new List<(NodeId Id, ByteString Certificate)>();
            await using (GlobalDiscoveryServerClient admin = CreateClient(m_gdsClient.AdminUser))
            {
                await admin.ConnectAsync().ConfigureAwait(false);
                for (int ii = 0; ii < kParallelRevocations; ii++)
                {
                    applications.Add(await RegisterAndIssueAsync(admin, "Parallel" + ii)
                        .ConfigureAwait(false));
                }
            }

            // One session per call, so the calls really run at the same time.
            var clients = new List<GlobalDiscoveryServerClient>();
            try
            {
                for (int ii = 0; ii < kParallelRevocations; ii++)
                {
                    GlobalDiscoveryServerClient client = CreateClient(m_gdsClient.AdminUser);
                    clients.Add(client);
                    await client.ConnectAsync().ConfigureAwait(false);
                }

                ServiceResult[] results = await Task.WhenAll(Enumerable.Range(0, kParallelRevocations)
                    .Select(async ii =>
                    {
                        try
                        {
                            await clients[ii].RevokeCertificateAsync(
                                applications[ii].Id,
                                applications[ii].Certificate).ConfigureAwait(false);
                            return ServiceResult.Good;
                        }
                        catch (ServiceResultException ex)
                        {
                            return new ServiceResult(ex);
                        }
                    })).ConfigureAwait(false);

                var statuses = new StatusCode[kParallelRevocations];
                for (int ii = 0; ii < kParallelRevocations; ii++)
                {
                    (StatusCode status, _) = await clients[0].CheckRevocationStatusAsync(
                        applications[ii].Certificate).ConfigureAwait(false);
                    statuses[ii] = status;
                }

                // Every call succeeded, and every certificate is on the CRL.
                Assert.Multiple(() =>
                {
                    for (int ii = 0; ii < kParallelRevocations; ii++)
                    {
                        Assert.That(results[ii].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good), $"call {ii}");
                        Assert.That(
                            statuses[ii],
                            Is.EqualTo((StatusCode)StatusCodes.BadCertificateRevoked),
                            $"certificate {ii}");
                    }
                });
            }
            finally
            {
                foreach (GlobalDiscoveryServerClient client in clients)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task UnregisterRevokesCurrentAndReplacedCertificatesAsync()
        {
            await using GlobalDiscoveryServerClient admin = CreateClient(m_gdsClient.AdminUser);
            await admin.ConnectAsync().ConfigureAwait(false);
            (NodeId applicationId, ByteString firstCertificate) = await RegisterAndIssueAsync(admin, "Unregister")
                .ConfigureAwait(false);
            ByteString secondCertificate = await IssueAsync(admin, applicationId, "CN=UnregisterRenewed,O=OPC Foundation")
                .ConfigureAwait(false);
            (_, ByteString otherCertificate) = await RegisterAndIssueAsync(admin, "Bystander").ConfigureAwait(false);

            await admin.UnregisterApplicationAsync(applicationId).ConfigureAwait(false);

            (StatusCode firstStatus, _) = await admin.CheckRevocationStatusAsync(firstCertificate)
                .ConfigureAwait(false);
            (StatusCode secondStatus, _) = await admin.CheckRevocationStatusAsync(secondCertificate)
                .ConfigureAwait(false);
            (StatusCode otherStatus, _) = await admin.CheckRevocationStatusAsync(otherCertificate)
                .ConfigureAwait(false);
            Assert.That(firstStatus, Is.EqualTo((StatusCode)StatusCodes.BadCertificateRevoked), "replaced certificate");
            Assert.That(secondStatus, Is.EqualTo((StatusCode)StatusCodes.BadCertificateRevoked), "current certificate");
            Assert.That(otherStatus, Is.EqualTo((StatusCode)StatusCodes.Good), "certificate of another application");
        }

        [Test]
        public async Task RevokeCertificateRejectsForgedCertificateInApplicationStoreAsync()
        {
            await using GlobalDiscoveryServerClient admin = CreateClient(m_gdsClient.AdminUser);
            await admin.ConnectAsync().ConfigureAwait(false);
            (NodeId applicationId, ByteString issuedCertificate) = await RegisterAndIssueAsync(admin, "Forged")
                .ConfigureAwait(false);
            ApplicationRecordDataType record = await admin.GetApplicationAsync(applicationId).ConfigureAwait(false);

            // A certificate with the application's URI and the GDS CA's
            // subject as issuer, signed by another key, in the GDS
            // application certificate store.
            using Certificate issued = Certificate.FromRawData(issuedCertificate);
            using Certificate otherCa = CertificateBuilder.Create(issued.Issuer).SetCAConstraint().CreateForRSA();
            using Certificate forged = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(record.ApplicationUri!, "Forged", "CN=Forged,O=OPC Foundation", ["localhost"])
                .SetIssuer(otherCa)
                .CreateForRSA();
            using var forgedPublic = Certificate.FromRawData(forged.RawData);
            GlobalDiscoveryServerConfiguration gdsConfiguration =
                m_server.Config.ParseExtension<GlobalDiscoveryServerConfiguration>()!;
            using (ICertificateStore store = new CertificateStoreIdentifier(
                gdsConfiguration.ApplicationCertificatesStorePath!).OpenStore(NUnitTelemetryContext.Create()))
            {
                await store.AddAsync(forgedPublic).ConfigureAwait(false);
            }

            ServiceResultException? ex = null;
            try
            {
                await admin.RevokeCertificateAsync(applicationId, ByteString.From(forged.RawData))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException e)
            {
                ex = e;
            }
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            (StatusCode status, _) = await admin.CheckRevocationStatusAsync(issuedCertificate)
                .ConfigureAwait(false);
            Assert.That(status, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        private GlobalDiscoveryServerClient CreateClient(IUserIdentity user)
        {
            ApplicationConfiguration configuration = m_gdsClient.Configuration;
            return new GlobalDiscoveryServerClient(configuration, user)
            {
                Endpoint = new ConfiguredEndpoint(
                    null,
                    m_gdsClient.GDSClient.Endpoint!.Description,
                    EndpointConfiguration.Create(configuration))
            };
        }

        private static async Task<(NodeId Id, ByteString Certificate)> RegisterAndIssueAsync(
            GlobalDiscoveryServerClient admin,
            string name)
        {
            string unique = name + "-" + Guid.NewGuid().ToString("N");
            NodeId applicationId = await admin.RegisterApplicationAsync(new ApplicationRecordDataType
            {
                ApplicationUri = "urn:localhost:opcfoundation.org:SupersededCertificate:" + unique,
                ApplicationType = ApplicationType.Client,
                ProductUri = "http://opcfoundation.org/UA/SupersededCertificate",
                ApplicationNames = [new LocalizedText("en-US", unique)]
            }).ConfigureAwait(false);
            ByteString certificate = await IssueAsync(admin, applicationId, "CN=" + unique + ",O=OPC Foundation")
                .ConfigureAwait(false);
            return (applicationId, certificate);
        }

        private static async Task<ByteString> IssueAsync(
            GlobalDiscoveryServerClient admin,
            NodeId applicationId,
            string subjectName)
        {
            NodeId requestId = await admin.StartNewKeyPairRequestAsync(
                applicationId,
                default,
                default,
                subjectName,
                ["localhost"],
                "PEM",
                []).ConfigureAwait(false);
            (ByteString certificate, _, _) = await admin.FinishRequestAsync(applicationId, requestId)
                .ConfigureAwait(false);
            Assert.That(certificate.IsEmpty, Is.False);
            return certificate;
        }
    }
}
