/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// End-to-end test verifying that revoking an application certificate through the
    /// GDS cuts off a session that is already open with that certificate.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [NonParallelizable]
    public class RevokeCertificateSessionTest
    {
        private static readonly TimeSpan s_enforcementTimeout = TimeSpan.FromSeconds(30);

        private GlobalDiscoveryTestServer m_server = null!;
        private GlobalDiscoveryTestClient m_gdsClient = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_server = await TestUtils.StartGDSAsync(clean: true).ConfigureAwait(false);
            m_gdsClient = new GlobalDiscoveryTestClient(true, NUnitTelemetryContext.Create());
            await m_gdsClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
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

        [Test]
        public async Task RevokeCertificateCutsOffOpenSessionOfRevokedCertificateAsync()
        {
            ApplicationConfiguration configuration = m_gdsClient.Configuration;
            CertificateIdentifier adminCertificate =
                configuration.SecurityConfiguration.ApplicationCertificate!;

            // Issue a GDS certificate to the test client. This replaces the
            // application certificate the next session is created with.
            Assert.That(await m_gdsClient.RegisterTestClientAtGdsAsync().ConfigureAwait(false), Is.True);
            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;

            var victim = new GlobalDiscoveryServerClient(configuration, m_gdsClient.Anonymous)
            {
                Endpoint = new ConfiguredEndpoint(
                    null,
                    m_gdsClient.GDSClient.Endpoint!.Description,
                    EndpointConfiguration.Create(configuration))
            };
            try
            {
                // The certificate grants the session ApplicationSelfAdmin rights.
                NodeId applicationId = application.ApplicationRecord.ApplicationId;
                await victim.ConnectAsync().ConfigureAwait(false);
                ApplicationRecordDataType record =
                    await victim.GetApplicationAsync(applicationId).ConfigureAwait(false);
                Assert.That(record.ApplicationId, Is.EqualTo(applicationId));

                // Revoke from an admin session which uses the original certificate,
                // so that only the victim session holds the revoked certificate.
                configuration.SecurityConfiguration.ApplicationCertificate = adminCertificate;
                m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.AdminUser;
                await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);
                try
                {
                    await m_gdsClient.GDSClient.RevokeCertificateAsync(
                        applicationId,
                        application.Certificate.ToByteString()).ConfigureAwait(false);
                }
                finally
                {
                    await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
                }

                // The server revalidates the open SecureChannels asynchronously
                // after the CRL update and closes the one using the revoked
                // certificate, so the session can no longer be used.
                ServiceResultException? failure = null;
                DateTime deadline = DateTime.UtcNow + s_enforcementTimeout;
                while (failure == null && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        await victim.GetApplicationAsync(applicationId).ConfigureAwait(false);
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                    catch (ServiceResultException ex)
                    {
                        failure = ex;
                    }
                }
                Assert.That(
                    failure,
                    Is.Not.Null,
                    "The session using the revoked certificate can still be used.");
            }
            finally
            {
                await victim.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
