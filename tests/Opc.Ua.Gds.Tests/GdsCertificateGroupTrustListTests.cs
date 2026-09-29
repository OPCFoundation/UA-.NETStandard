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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Exercises the TrustList of the GDS DefaultApplicationGroup
    /// (Directory/CertificateGroups/DefaultApplicationGroup/TrustList): the
    /// OPC 10000-12 §7.8.2 certificate validation and the release of a write
    /// handle abandoned by a closed Session.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("TrustList")]
    [NonParallelizable]
    public sealed class GdsCertificateGroupTrustListTests
    {
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_server = await TestUtils.StartGDSAsync(clean: true).ConfigureAwait(false);
            m_gdsClient = await ConnectAdminClientAsync().ConfigureAwait(false);
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
                await m_server.DisposeAsync().ConfigureAwait(false);
                m_server = null;
            }
        }

        [Test]
        public async Task AddCertificateWithMissingIssuerIsRejectedAsync()
        {
            using Certificate caCert = CertificateBuilder
                .Create("CN=GDS Group TrustList Unknown CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=GDS Group TrustList Orphan Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // OPC 10000-12 §7.8.2.6: the issuer of the added certificate must
            // already be in the TrustList.
            CallMethodResult result = await CallTrustListMethodAsync(
                m_gdsClient.GDSClient.Session,
                Ua.MethodIds.TrustListType_AddCertificate,
                new Variant(leaf.RawData.ToByteString()),
                new Variant(true)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));
        }

        [Test]
        public async Task WriteOpenOfClosedSessionDoesNotLockTheTrustListAsync()
        {
            ISession session = m_gdsClient.GDSClient.Session;
            GlobalDiscoveryTestClient writer = await ConnectAdminClientAsync().ConfigureAwait(false);
            try
            {
                CallMethodResult writeOpen = await CallTrustListMethodAsync(
                    writer.GDSClient.Session,
                    Ua.MethodIds.FileType_Open,
                    new Variant((byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting)))
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(writeOpen.StatusCode), Is.True, writeOpen.StatusCode.ToString());

                CallMethodResult blocked = await CallTrustListMethodAsync(
                    session,
                    Ua.MethodIds.FileType_Open,
                    new Variant((byte)OpenFileMode.Read)).ConfigureAwait(false);
                Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));
            }
            finally
            {
                // The writer disappears without Close/CloseAndUpdate.
                await writer.DisconnectClientAsync().ConfigureAwait(false);
                writer.Dispose();
            }

            CallMethodResult reopen = await CallTrustListMethodAsync(
                session,
                Ua.MethodIds.FileType_Open,
                new Variant((byte)OpenFileMode.Read)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(reopen.StatusCode), Is.True, reopen.StatusCode.ToString());

            CallMethodResult close = await CallTrustListMethodAsync(
                session,
                Ua.MethodIds.FileType_Close,
                reopen.OutputArguments[0]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(close.StatusCode), Is.True, close.StatusCode.ToString());
        }

        private async Task<GlobalDiscoveryTestClient> ConnectAdminClientAsync()
        {
            var client = new GlobalDiscoveryTestClient(true, m_telemetry);
            await client.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            client.GDSClient.AdminCredentials = client.AdminUser;
            await client.GDSClient.ConnectAsync().ConfigureAwait(false);
            return client;
        }

        private static async Task<CallMethodResult> CallTrustListMethodAsync(
            ISession session,
            NodeId methodId,
            params Variant[] inputArguments)
        {
            CallResponse response = await session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = ExpandedNodeId.ToNodeId(
                            ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList,
                            session.NamespaceUris),
                        MethodId = methodId,
                        InputArguments = inputArguments.ToArrayOf()
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
