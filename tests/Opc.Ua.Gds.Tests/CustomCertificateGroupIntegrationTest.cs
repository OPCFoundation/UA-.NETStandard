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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Ua = Opc.Ua;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Integration tests verifying that custom certificate groups are loaded from
    /// configuration and correctly instantiated in the GDS address space.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CustomCertificateGroupIntegrationTest
    {
        private const string kCustomGroupId = "MyCustomGroup";
        private const string kCustomGroupSubjectName = "CN=GDS Custom CA, O=OPC Foundation";
        private const string kHttpsGroupSubjectName = "CN=GDS HTTPS CA, O=OPC Foundation";
        private const string kUserGroupSubjectName = "CN=GDS User CA, O=OPC Foundation";

        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            string customBasePath = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "CA", "custom");
            var customGroup = new CertificateGroupConfiguration
            {
                Id = kCustomGroupId,
                CertificateTypes = ["RsaSha256ApplicationCertificateType"],
                SubjectName = kCustomGroupSubjectName,
                BaseStorePath = customBasePath,
                DefaultCertificateLifetime = 12,
                DefaultCertificateKeySize = 2048,
                DefaultCertificateHashSize = 256,
                CACertificateLifetime = 60,
                CACertificateKeySize = 4096,
                CACertificateHashSize = 512
            };
            var httpsGroup = new CertificateGroupConfiguration
            {
                Id = "DefaultHttpsGroup",
                CertificateTypes = [nameof(Ua.ObjectTypeIds.HttpsCertificateType)],
                SubjectName = kHttpsGroupSubjectName,
                BaseStorePath = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "CA", "https"),
                DefaultCertificateLifetime = 12,
                DefaultCertificateKeySize = 2048,
                DefaultCertificateHashSize = 256,
                CACertificateLifetime = 60,
                CACertificateKeySize = 4096,
                CACertificateHashSize = 512
            };
            var userGroup = new CertificateGroupConfiguration
            {
                Id = "DefaultUserTokenGroup",
                CertificateTypes = [nameof(Ua.ObjectTypeIds.UserCertificateType)],
                SubjectName = kUserGroupSubjectName,
                BaseStorePath = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "CA", "users"),
                DefaultCertificateLifetime = 12,
                DefaultCertificateKeySize = 2048,
                DefaultCertificateHashSize = 256,
                CACertificateLifetime = 60,
                CACertificateKeySize = 4096,
                CACertificateHashSize = 512
            };

            m_server = await TestUtils.StartGDSAsync(
                clean: true,
                additionalCertGroups: [customGroup, httpsGroup, userGroup]).ConfigureAwait(false);

            m_gdsClient = new GlobalDiscoveryTestClient(true, m_telemetry);
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
        public void ServerRegistersGdsEncodeableTypesOnStartup()
        {
            Assert.That(m_server.Server.MessageContext.Factory.ContainsEncodeableType(
                DataTypeIds.ApplicationRecordDataType), Is.True);
        }

        [Test]
        public async Task CustomCertificateGroupAppearsInGetCertificateGroupsAsync()
        {
            // Connect as admin and register an application
            m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.AdminUser;
            await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);

            try
            {
                var appTestDataGenerator = new ApplicationTestDataGenerator(1, m_telemetry);
                ApplicationTestData appData = appTestDataGenerator.ApplicationTestSet(1, false)[0];
                NodeId appId = await m_gdsClient.GDSClient
                    .RegisterApplicationAsync(appData.ApplicationRecord)
                    .ConfigureAwait(false);

                Assert.That(appId.IsNull, Is.False);

                // GetCertificateGroups should return the default groups and the custom group.
                ArrayOf<NodeId> groups = await m_gdsClient.GDSClient
                    .GetCertificateGroupsAsync(appId)
                    .ConfigureAwait(false);

                Assert.That(groups.IsNull, Is.False);
                Assert.That(groups.Count, Is.EqualTo(4),
                    "Expected 4 certificate groups: default, HTTPS, user-token, and custom");

                // Verify that each group's TrustList is accessible
                foreach (NodeId groupId in groups.ToList())
                {
                    NodeId trustListId = await m_gdsClient.GDSClient
                        .GetTrustListAsync(appId, groupId)
                        .ConfigureAwait(false);

                    Assert.That(trustListId.IsNull, Is.False,
                        $"TrustList NodeId must not be null for group {groupId}");
                }

                await m_gdsClient.GDSClient.UnregisterApplicationAsync(appId).ConfigureAwait(false);
            }
            finally
            {
                await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CustomCertificateGroupNodeExistsInAddressSpaceAsync()
        {
            // Connect as admin to browse the address space
            m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.AdminUser;
            await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);

            try
            {
                var appTestDataGenerator = new ApplicationTestDataGenerator(2, m_telemetry);
                ApplicationTestData appData = appTestDataGenerator.ApplicationTestSet(1, false)[0];
                NodeId appId = await m_gdsClient.GDSClient
                    .RegisterApplicationAsync(appData.ApplicationRecord)
                    .ConfigureAwait(false);

                ArrayOf<NodeId> groups = await m_gdsClient.GDSClient
                    .GetCertificateGroupsAsync(appId)
                    .ConfigureAwait(false);

                // The default application group has a well-known NodeId (predefined in the GDS NodeSet)
                // The custom group has a dynamically generated NodeId outside that namespace
                // ExpandedNodeId.ToNodeId is a known IDE0007/IDE0008 var-vs-explicit
                // oscillation site; keep the explicit type and suppress IDE0007.
#pragma warning disable IDE0007 // Use implicit type
                NodeId defaultGroupId = ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    m_gdsClient.GDSClient.Session!.NamespaceUris);
#pragma warning restore IDE0007 // Use implicit type

                // Verify the custom group NodeId is among the returned groups
                NodeId customGroupNodeId = groups.ToList().FirstOrDefault(g => !Utils.IsEqual(g, defaultGroupId));
                Assert.That(customGroupNodeId.IsNull, Is.False,
                    "The custom group NodeId must not be null");

                // The identifier itself is minted by the server and may change,
                // but the namespace it is minted in is part of the GDS contract:
                // server-owned instance nodes live in the application record
                // namespace, not in the companion model's.
                Assert.That(
                    m_gdsClient.GDSClient.Session.NamespaceUris
                        .GetString(customGroupNodeId.NamespaceIndex),
                    Is.EqualTo("http://opcfoundation.org/UA/GDS/applications/"),
                    "The custom group node must live in the application record namespace");

                // Read the BrowseName of the custom group node from the address space
                Node customGroupNode = await m_gdsClient.GDSClient.Session
                    .ReadNodeAsync(customGroupNodeId)
                    .ConfigureAwait(false);

                Assert.That(customGroupNode, Is.Not.Null,
                    "The custom certificate group node must exist in the address space");
                Assert.That(customGroupNode.BrowseName.Name, Is.EqualTo(kCustomGroupId),
                    $"BrowseName of the custom group node must be '{kCustomGroupId}'");

                await m_gdsClient.GDSClient.UnregisterApplicationAsync(appId).ConfigureAwait(false);
            }
            finally
            {
                await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DefaultApplicationGroupAdvertisesConfiguredCertificateTypesAsync()
        {
            m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.AdminUser;
            await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);

            try
            {
                ISession session = m_gdsClient.GDSClient.Session!;

                NodeId certificateTypesNodeId = ExpandedNodeId.ToNodeId(
                    VariableIds.Directory_CertificateGroups_DefaultApplicationGroup_CertificateTypes,
                    session.NamespaceUris);

                DataValue value = await session
                    .ReadValueAsync(certificateTypesNodeId)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(value.StatusCode),
                    Is.True,
                    $"Reading CertificateTypes failed with status {value.StatusCode}.");

                Assert.That(
                    value.WrappedValue.TryGetValue(out ArrayOf<NodeId> certificateTypes),
                    Is.True,
                    "CertificateTypes is not a NodeId array.");

                // OPC 10000-12 requires the DefaultApplicationGroup to advertise the concrete
                // subtypes of ApplicationCertificateType which the group supports.
                Assert.That(
                    certificateTypes.Contains(t => t == Opc.Ua.ObjectTypeIds.ApplicationCertificateType),
                    Is.False,
                    "The abstract ApplicationCertificateType must not be advertised.");
                Assert.That(
                    certificateTypes.Contains(
                        t => t == Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    Is.True,
                    "The configured RsaSha256ApplicationCertificateType must be advertised.");
            }
            finally
            {
                await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RevokingHttpsAndUserCertificatesUpdatesTheirIssuerStoresAndNotifiesScopesAsync()
        {
            m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.AdminUser;
            await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);

            var issuerStores = new[]
            {
                m_server.Config.SecurityConfiguration.TrustedIssuerCertificates,
                m_server.Config.SecurityConfiguration.HttpsIssuerCertificates!,
                m_server.Config.SecurityConfiguration.UserIssuerCertificates!
            };
            Assert.That(
                issuerStores.Select(store => Path.GetFullPath(store.StorePath!)).Distinct().Count(),
                Is.EqualTo(issuerStores.Length),
                "Peer, HTTPS, and user-token issuer stores must be distinct.");

            try
            {
                var appTestDataGenerator = new ApplicationTestDataGenerator(3, m_telemetry);
                ApplicationTestData appData = appTestDataGenerator.ApplicationTestSet(1, false)[0];
                appData.ApplicationRecord.ApplicationType = ApplicationType.Server;
                appData.ApplicationRecord.DiscoveryUrls = ["https://localhost:4843"];
                NodeId appId = await m_gdsClient.GDSClient
                    .RegisterApplicationAsync(appData.ApplicationRecord)
                    .ConfigureAwait(false);

                var revocationCases = new[]
                {
                    (
                        GroupId: "DefaultHttpsGroup",
                        CertificateType: Ua.ObjectTypeIds.HttpsCertificateType,
                        Scope: TrustListIdentifier.Https),
                    (
                        GroupId: "DefaultUserTokenGroup",
                        CertificateType: Ua.ObjectTypeIds.UserCertificateType,
                        Scope: TrustListIdentifier.Users)
                };

                foreach ((string groupId, NodeId certificateType, TrustListIdentifier scope) in revocationCases)
                {
                    NodeId certificateGroupId = GetCertificateGroupId(groupId);
                    NodeId requestId = await m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                        appId,
                        certificateGroupId,
                        certificateType,
                        appData.Subject,
                        appData.DomainNames,
                        appData.PrivateKeyFormat,
                        appData.PrivateKeyPassword).ConfigureAwait(false);

                    (Certificate certificate, Certificate issuer) =
                        await FinishRequestAsync(appId, requestId).ConfigureAwait(false);
                    try
                    {
                        var observer = new Mock<IObserver<CertificateChangeEvent>>();
                        using IDisposable subscription = m_server.Config.CertificateManager.CertificateChanges
                            .Subscribe(observer.Object);

                        await m_gdsClient.GDSClient
                            .RevokeCertificateAsync(appId, ByteString.From(certificate.RawData))
                            .ConfigureAwait(false);

                        observer.Verify(
                            item => item.OnNext(It.Is<CertificateChangeEvent>(
                                change => change.Kind == CertificateChangeKind.CrlUpdated &&
                                    change.TrustList == TrustListIdentifier.Peers)),
                            Times.Once);
                        observer.Verify(
                            item => item.OnNext(It.Is<CertificateChangeEvent>(
                                change => change.Kind == CertificateChangeKind.CrlUpdated &&
                                    change.TrustList == scope)),
                            Times.Once);

                        await AssertRevocationCrlExistsAsync(
                            TrustListIdentifier.Peers,
                            issuer,
                            certificate)
                            .ConfigureAwait(false);
                        await AssertRevocationCrlExistsAsync(scope, issuer, certificate).ConfigureAwait(false);
                    }
                    finally
                    {
                        certificate.Dispose();
                        issuer.Dispose();
                    }
                }

                await m_gdsClient.GDSClient.UnregisterApplicationAsync(appId).ConfigureAwait(false);
            }
            finally
            {
                await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Resolves a predefined certificate group using the active GDS session namespace table.
        /// </summary>
        private NodeId GetCertificateGroupId(string groupId)
        {
            return groupId switch
            {
                "DefaultHttpsGroup" => ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup,
                    m_gdsClient.GDSClient.Session!.NamespaceUris),
                "DefaultUserTokenGroup" => ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup,
                    m_gdsClient.GDSClient.Session!.NamespaceUris),
                _ => throw new ArgumentOutOfRangeException(nameof(groupId))
            };
        }

        /// <summary>
        /// Waits for the GDS to finish issuing a certificate for the request.
        /// </summary>
        private async Task<(Certificate certificate, Certificate issuer)> FinishRequestAsync(
            NodeId appId,
            NodeId requestId)
        {
            DateTime deadline = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < deadline)
            {
                (ByteString certificate, _, ArrayOf<ByteString> issuerCertificates) = await m_gdsClient.GDSClient
                    .FinishRequestAsync(appId, requestId).ConfigureAwait(false);
                if (!certificate.IsEmpty)
                {
                    return (
                        Certificate.FromRawData(certificate),
                        Certificate.FromRawData(issuerCertificates[0]));
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for the GDS certificate request to finish.");
        }

        /// <summary>
        /// Verifies that an issuer store contains the CRL for a revoked certificate.
        /// </summary>
        private async Task AssertRevocationCrlExistsAsync(
            TrustListIdentifier scope,
            Certificate issuer,
            Certificate certificate)
        {
            using ICertificateStore issuerStore = m_server.Config.CertificateManager.OpenIssuerStore(scope)!;
            X509CRLCollection crls = await issuerStore
                .EnumerateCRLsAsync(issuer).ConfigureAwait(false);
            Assert.That(crls, Has.Count.EqualTo(1), $"Expected a CRL in the {scope} issuer store.");
            Assert.That(crls[0].RevokedCertificates, Has.Count.EqualTo(1));
            Assert.That(
                crls[0].RevokedCertificates[0].UserCertificate,
                Is.EqualTo(certificate.GetSerialNumber()));
        }
    }
}
