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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Tests;

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

            m_server = await TestUtils.StartGDSAsync(
                clean: true,
                additionalCertGroups: [customGroup]).ConfigureAwait(false);

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
                m_gdsClient = null;
            }
            if (m_server != null)
            {
                await m_server.StopServerAsync().ConfigureAwait(false);
                m_server = null;
            }
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

                Assert.That(appId, Is.Not.Null);
                Assert.That(appId.IsNull, Is.False);

                // GetCertificateGroups should return both the default group and the custom group
                NodeId[] groups = await m_gdsClient.GDSClient
                    .GetCertificateGroupsAsync(appId)
                    .ConfigureAwait(false);

                Assert.That(groups, Is.Not.Null);
                Assert.That(groups.Length, Is.EqualTo(2),
                    "Expected 2 certificate groups: default + custom");

                // Verify that each group's TrustList is accessible
                foreach (NodeId groupId in groups)
                {
                    NodeId trustListId = await m_gdsClient.GDSClient
                        .GetTrustListAsync(appId, groupId)
                        .ConfigureAwait(false);

                    Assert.That(trustListId, Is.Not.Null);
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

                NodeId[] groups = await m_gdsClient.GDSClient
                    .GetCertificateGroupsAsync(appId)
                    .ConfigureAwait(false);

                // The default application group has a well-known NodeId (predefined in the GDS NodeSet)
                // The custom group has a dynamically generated NodeId outside that namespace
                NodeId defaultGroupId = ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    m_gdsClient.GDSClient.Session.NamespaceUris);

                // Verify the custom group NodeId is among the returned groups
                NodeId customGroupNodeId = groups.FirstOrDefault(g => !Utils.IsEqual(g, defaultGroupId));
                Assert.That(customGroupNodeId, Is.Not.Null,
                    "A group NodeId other than DefaultApplicationGroup must be present");
                Assert.That(customGroupNodeId.IsNull, Is.False,
                    "The custom group NodeId must not be null");

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
    }
}
