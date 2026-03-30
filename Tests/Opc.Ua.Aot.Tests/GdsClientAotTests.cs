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

using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;

using Opc.Ua.Client;
namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for GDS client operations.
    /// </summary>
    [ClassDataSource<GdsTestFixture>(Shared = SharedType.PerTestSession)]
    public class GdsClientAotTests(GdsTestFixture fixture)
    {
        [Test]
        public async Task ConnectToGdsAsync()
        {
            GlobalDiscoveryServerClient client = fixture.GdsClient;
            await Assert.That(client.Session).IsNotNull();
            await Assert.That(client.Session.Connected).IsTrue();
        }

        [Test]
        public async Task RegisterApplicationAsync()
        {
            ApplicationRecordDataType appRecord = CreateTestAppRecord(
                "urn:localhost:OPCFoundation:AotTest:Register");

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            await Assert.That(id.IsNull).IsFalse();

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task FindApplicationAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:Find";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            ArrayOf<ApplicationRecordDataType> results = await fixture.GdsClient
                .FindApplicationAsync(appUri)
                .ConfigureAwait(false);

            await Assert.That(results.IsNull).IsFalse();
            await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task GetApplicationAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:Get";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            ApplicationRecordDataType result = await fixture.GdsClient
                .GetApplicationAsync(id)
                .ConfigureAwait(false);

            await Assert.That(result).IsNotNull();
            await Assert.That(result.ApplicationUri).IsEqualTo(appUri);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task UpdateApplicationAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:Update";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);
            appRecord.ApplicationId = id;

            string updatedUri = appUri + "/v2";
            appRecord.ApplicationUri = updatedUri;
            await fixture.GdsClient
                .UpdateApplicationAsync(appRecord)
                .ConfigureAwait(false);

            ApplicationRecordDataType result = await fixture.GdsClient
                .GetApplicationAsync(id)
                .ConfigureAwait(false);

            await Assert.That(result).IsNotNull();
            await Assert.That(result.ApplicationUri).IsEqualTo(updatedUri);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task QueryServersAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:QuerySrv";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            ArrayOf<ServerOnNetwork> servers = await fixture.GdsClient
                .QueryServersAsync(0, null, null, null, default)
                .ConfigureAwait(false);

            await Assert.That(servers.IsNull).IsFalse();
            await Assert.That(servers.Count).IsGreaterThanOrEqualTo(1);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task QueryApplicationsAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:QueryApp";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            (ArrayOf<ApplicationDescription> applications,
                DateTimeUtc _,
                uint _) = await fixture.GdsClient
                .QueryApplicationsAsync(
                    0, 0,
                    string.Empty, string.Empty,
                    0, string.Empty, [])
                .ConfigureAwait(false);

            await Assert.That(applications.IsNull).IsFalse();
            await Assert.That(applications.Count).IsGreaterThanOrEqualTo(1);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task GetCertificateGroupsAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:CertGroups";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);

            ArrayOf<NodeId> groups = await fixture.GdsClient
                .GetCertificateGroupsAsync(id)
                .ConfigureAwait(false);

            await Assert.That(groups.IsNull).IsFalse();
            await Assert.That(groups.Count).IsGreaterThanOrEqualTo(1);

            // cleanup
            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task UnregisterApplicationAsync()
        {
            string appUri = "urn:localhost:OPCFoundation:AotTest:Unreg";
            ApplicationRecordDataType appRecord = CreateTestAppRecord(appUri);

            NodeId id = await fixture.GdsClient
                .RegisterApplicationAsync(appRecord)
                .ConfigureAwait(false);
            await Assert.That(id.IsNull).IsFalse();

            await fixture.GdsClient
                .UnregisterApplicationAsync(id)
                .ConfigureAwait(false);

            ArrayOf<ApplicationRecordDataType> results = await fixture.GdsClient
                .FindApplicationAsync(appUri)
                .ConfigureAwait(false);

            await Assert.That(results.Count).IsEqualTo(0);
        }

        private static ApplicationRecordDataType CreateTestAppRecord(
            string applicationUri)
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = applicationUri,
                ApplicationType = ApplicationType.Server,
                ApplicationNames =
                    [new LocalizedText("en-US", "AOT Test Application")],
                ProductUri = "http://opcfoundation.org/UA/AotTest",
                DiscoveryUrls = [$"opc.tcp://localhost:4840/{applicationUri}"],
                ServerCapabilities = ["NA"]
            };
        }
    }
}
