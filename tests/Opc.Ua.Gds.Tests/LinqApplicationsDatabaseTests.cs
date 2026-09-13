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

using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Database.Linq;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class LinqApplicationsDatabaseTests
    {
        [Test]
        public void RegisterApplicationWithDuplicateUriThrowsBadEntryExists()
        {
            var database = new LinqApplicationsDatabase();
            ApplicationRecordDataType application = CreateServerApplication("urn:test:duplicate", "ServerOne");
            database.RegisterApplication(application);

            Assert.That(
                () => database.RegisterApplication(CreateServerApplication("urn:test:duplicate", "ServerTwo")),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadEntryExists));
        }

        [Test]
        public void UpdateApplicationWithChangedUriThrowsBadWriteNotSupported()
        {
            var database = new LinqApplicationsDatabase();
            ApplicationRecordDataType application = CreateServerApplication("urn:test:update-original", "ServerOne");
            NodeId applicationId = database.RegisterApplication(application);
            ApplicationRecordDataType updatedApplication = CreateServerApplication("urn:test:update-new", "ServerOneUpdated");
            updatedApplication.ApplicationId = applicationId;

            Assert.That(
                () => database.UpdateApplication(updatedApplication),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadWriteNotSupported));
        }

        [Test]
        public void QueryServersStartingRecordIdReturnsStrictlyGreaterRecords()
        {
            var database = new LinqApplicationsDatabase();
            database.RegisterApplication(CreateServerApplication("urn:test:server-1", "ServerOne"));
            database.RegisterApplication(CreateServerApplication("urn:test:server-2", "ServerTwo"));

            ServerOnNetwork[] allServers = database.QueryServers(0, 0, null, null, null, [], out _);
            uint firstRecordId = allServers.Min(server => server.RecordId);

            ServerOnNetwork[] pagedServers = database.QueryServers(firstRecordId, 0, null, null, null, [], out _);

            Assert.That(pagedServers, Has.Length.GreaterThan(0));
            Assert.That(pagedServers.All(server => server.RecordId > firstRecordId), Is.True);
        }

        /// <summary>
        /// OPC 10000-12 §6.5.11: QueryServers returns one record per DiscoveryUrl,
        /// and every record needs its own RecordId. With the application id as
        /// RecordId, paging with StartingRecordId skipped the remaining
        /// DiscoveryUrls of an application once a page ended inside it
        /// (CTT GDS Application Directory 045.js, 075.js).
        /// </summary>
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        public void QueryServersPagingReturnsEveryDiscoveryUrlOnce(uint maxRecordsToReturn)
        {
            var database = new LinqApplicationsDatabase();
            database.RegisterApplication(CreateServerApplication("urn:test:server-1", "ServerOne"));
            ApplicationRecordDataType multiUrl = CreateServerApplication("urn:test:server-2", "ServerTwo");
            multiUrl.DiscoveryUrls = ["opc.tcp://two:1", "opc.tcp://two:2"];
            database.RegisterApplication(multiUrl);
            ApplicationRecordDataType clientAndServer = CreateServerApplication("urn:test:server-3", "ServerThree");
            clientAndServer.ApplicationType = ApplicationType.ClientAndServer;
            clientAndServer.DiscoveryUrls = ["opc.tcp://three:1", "opc.tcp://three:2"];
            clientAndServer.ServerCapabilities = ["DA", "RCP"];
            database.RegisterApplication(clientAndServer);

            ServerOnNetwork[] allServers = database.QueryServers(0, 0, null, null, null, [], out _);
            Assert.That(allServers, Has.Length.EqualTo(5));
            Assert.That(allServers.Select(server => server.RecordId), Is.Unique.And.Ordered);

            var paged = new System.Collections.Generic.List<ServerOnNetwork>();
            uint startingRecordId = 0;
            for (int ii = 0; ii < 10; ii++)
            {
                ServerOnNetwork[] page = database.QueryServers(
                    startingRecordId, maxRecordsToReturn, null, null, null, [], out _);
                if (page.Length == 0)
                {
                    break;
                }
                Assert.That(page, Has.Length.LessThanOrEqualTo(maxRecordsToReturn));
                Assert.That(page.All(server => server.RecordId > startingRecordId), Is.True);
                paged.AddRange(page);
                startingRecordId = page[page.Length - 1].RecordId;
            }

            Assert.That(
                paged.Select(server => server.DiscoveryUrl),
                Is.EqualTo(allServers.Select(server => server.DiscoveryUrl)));
        }

        [Test]
        public void QueryServersRecordIdsChangeWhenApplicationIsUpdated()
        {
            var database = new LinqApplicationsDatabase();
            ApplicationRecordDataType application = CreateServerApplication("urn:test:server-1", "ServerOne");
            NodeId applicationId = database.RegisterApplication(application);
            uint recordId = database.QueryServers(0, 0, null, null, null, [], out _).Single().RecordId;

            application.ApplicationId = applicationId;
            application.DiscoveryUrls = ["opc.tcp://updated:4840"];
            database.UpdateApplication(application);

            ServerOnNetwork updated = database.QueryServers(0, 0, null, null, null, [], out _).Single();
            Assert.That(updated.DiscoveryUrl, Is.EqualTo("opc.tcp://updated:4840"));
            Assert.That(updated.RecordId, Is.GreaterThan(recordId));
        }

        [Test]
        public void QueryServersRecordIdsSurviveJsonReloadAndLegacyDatabases()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var database = JsonApplicationsDatabase.Load(fileName);
                ApplicationRecordDataType multiUrl = CreateServerApplication("urn:test:server-1", "ServerOne");
                multiUrl.DiscoveryUrls = ["opc.tcp://one:1", "opc.tcp://one:2"];
                database.RegisterApplication(multiUrl);
                database.RegisterApplication(CreateServerApplication("urn:test:server-2", "ServerTwo"));
                uint[] recordIds = [.. database.QueryServers(0, 0, null, null, null, [], out _)
                    .Select(server => server.RecordId)];
                Assert.That(recordIds, Is.Unique.And.Ordered);

                // Reload: identifiers are persisted and new ones continue above them.
                database = JsonApplicationsDatabase.Load(fileName);
                Assert.That(
                    database.QueryServers(0, 0, null, null, null, [], out _).Select(server => server.RecordId),
                    Is.EqualTo(recordIds));
                database.RegisterApplication(CreateServerApplication("urn:test:server-3", "ServerThree"));
                Assert.That(
                    database.QueryServers(recordIds[recordIds.Length - 1], 0, null, null, null, [], out _).Single().DiscoveryUrl,
                    Is.EqualTo("opc.tcp://localhost:4840/ServerThree"));

                // A database saved before endpoints had an identifier.
                var json = JsonNode.Parse(File.ReadAllText(fileName))!.AsObject();
                json.Remove("LastServerEndpointId");
                foreach (JsonNode endpoint in json["ServerEndpoints"]!.AsArray())
                {
                    endpoint!.AsObject().Remove("ID");
                }
                File.WriteAllText(fileName, json.ToJsonString());

                database = JsonApplicationsDatabase.Load(fileName);
                ServerOnNetwork[] legacy = database.QueryServers(0, 1, null, null, null, [], out _);
                Assert.That(legacy, Has.Length.EqualTo(1));
                Assert.That(legacy[0].RecordId, Is.Not.Zero);
                uint[] legacyIds = [.. database.QueryServers(0, 0, null, null, null, [], out _)
                    .Select(server => server.RecordId)];
                Assert.That(legacyIds, Has.Length.EqualTo(4));
                Assert.That(legacyIds, Is.Unique.And.Ordered);
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// The CTT GDS Application Directory / Query Applications filters used
        /// to return no or all records (066.js, 068.js, 071.js, 073.js; Query
        /// Applications 013.js, 017.js, 019.js, 022.js, 024.js).
        /// </summary>
        [TestCase("%_erver%", "", 5, 3)]
        [TestCase("%\\_%", "", 2, 1)]
        [TestCase("%[q-s]", "", 3, 2)]
        [TestCase("%[^q-s]", "", 2, 2)]
        [TestCase("%e_", "", 3, 2)]
        [TestCase("", "%_ompliance%", 3, 3)]
        [TestCase("", "%e_", 3, 2)]
        [TestCase("", "%\\_%", 2, 1)]
        [TestCase("", "%[q-s]", 3, 2)]
        [TestCase("", "%[^q-s]", 2, 2)]
        [TestCase("", "[_]", 0, 0)]
        public void QueryFiltersUseLikePatterns(
            string applicationUri,
            string applicationName,
            int expectedServers,
            int expectedApplications)
        {
            LinqApplicationsDatabase database = CreateConformanceTestDatabase();

            ServerOnNetwork[] servers = database.QueryServers(
                0, 0, applicationName, applicationUri, null, [], out _);
            ApplicationDescription[] applications = database.QueryApplications(
                0, 0, applicationName, applicationUri, 0, null, [], out _, out _);

            Assert.That(servers, Has.Length.EqualTo(expectedServers));
            Assert.That(applications, Has.Length.EqualTo(expectedApplications));
        }

        [TestCase(uint.MaxValue)]
        [TestCase((uint)int.MaxValue + 1)]
        public void QueryApplicationsStartingRecordIdAboveInt32ReturnsNothing(uint startingRecordId)
        {
            LinqApplicationsDatabase database = CreateConformanceTestDatabase();

            ApplicationDescription[] applications = database.QueryApplications(
                startingRecordId, 0, null, null, 0, null, [], out _, out uint nextRecordId);

            Assert.That(applications, Is.Empty);
            Assert.That(nextRecordId, Is.Zero);
        }

        [TestCase("")]
        [TestCase(" ")]
        public void FindApplicationsWithEmptyUriReturnsNoRecord(string applicationUri)
        {
            LinqApplicationsDatabase database = CreateConformanceTestDatabase();

            Assert.That(database.FindApplications(applicationUri), Is.Empty);
        }

        /// <summary>
        /// The applications the CTT GDS Application Directory and Query
        /// Applications units register (ServerCapabilities simplified).
        /// </summary>
        private static LinqApplicationsDatabase CreateConformanceTestDatabase()
        {
            var database = new LinqApplicationsDatabase();
            database.RegisterApplication(new ApplicationRecordDataType
            {
                ApplicationUri = "urn:OPCFoundation:ComplianceTestTool",
                ApplicationType = ApplicationType.Client,
                ApplicationNames = [new LocalizedText("en-US", "OPC Foundation Compliance Test Tool")],
                ProductUri = "urn:opcfoundation/uactt",
                ServerCapabilities = ["RCP"]
            });
            database.RegisterApplication(new ApplicationRecordDataType
            {
                ApplicationUri = "urn:OPCFoundation:ComplianceTestToolEmbeddedServer",
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("en-US", "OPC Foundation Compliance Test Tool - Embedded Server")],
                ProductUri = "urn:opcfoundation/uactt_embedded_server",
                DiscoveryUrls = ["opc.tcp://localhost:4842"],
                ServerCapabilities = ["NA"]
            });
            database.RegisterApplication(new ApplicationRecordDataType
            {
                ApplicationUri = "urn:OPCFoundation:ServerApplication",
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("en-US", "OPC Foundation Compliance Test Tool - Server Application")],
                ProductUri = "urn:opcfoundation/uactt_server_application",
                DiscoveryUrls = ["opc.tcp://ServerApplication:12345", "opc.tcp://ServerApplication:12346"],
                ServerCapabilities = ["DA"]
            });
            database.RegisterApplication(new ApplicationRecordDataType
            {
                ApplicationUri = "cab:other_foundation:ClientAndServer",
                ApplicationType = ApplicationType.ClientAndServer,
                ApplicationNames = [new LocalizedText("en-US", "Example_Vendor - ClientAndServer")],
                ProductUri = "cab:some_name/client_and_server",
                DiscoveryUrls = ["opc.tcp://ClientAndServer:12345", "opc.tcp://ClientAndServer:12346"],
                ServerCapabilities = ["AC", "DA", "RCP", "HD"]
            });
            return database;
        }

        private static ApplicationRecordDataType CreateServerApplication(string applicationUri, string name)
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = applicationUri,
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("en", name)],
                ProductUri = "urn:test:product",
                DiscoveryUrls = [$"opc.tcp://localhost:4840/{name}"],
                ServerCapabilities = ["LDS"]
            };
        }
    }
}
