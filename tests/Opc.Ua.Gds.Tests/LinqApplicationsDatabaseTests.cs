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
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
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

            ServerOnNetwork[] allServers = database.QueryServers(0, 0, null!, null!, null!, [], out _);
            uint firstRecordId = allServers.Min(server => server.RecordId);

            ServerOnNetwork[] pagedServers = database.QueryServers(firstRecordId, 0, null!, null!, null!, [], out _);

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

            ServerOnNetwork[] allServers = database.QueryServers(0, 0, null!, null!, null!, [], out _);
            Assert.That(allServers, Has.Length.EqualTo(5));
            Assert.That(allServers.Select(server => server.RecordId), Is.Unique.And.Ordered);

            var paged = new System.Collections.Generic.List<ServerOnNetwork>();
            uint startingRecordId = 0;
            for (int ii = 0; ii < 10; ii++)
            {
                ServerOnNetwork[] page = database.QueryServers(
                    startingRecordId, maxRecordsToReturn, null!, null!, null!, [], out _);
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
            uint recordId = database.QueryServers(0, 0, null!, null!, null!, [], out _).Single().RecordId;

            application.ApplicationId = applicationId;
            application.DiscoveryUrls = ["opc.tcp://updated:4840"];
            database.UpdateApplication(application);

            ServerOnNetwork updated = database.QueryServers(0, 0, null!, null!, null!, [], out _).Single();
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
                uint[] recordIds = [.. database.QueryServers(0, 0, null!, null!, null!, [], out _)
                    .Select(server => server.RecordId)];
                Assert.That(recordIds, Is.Unique.And.Ordered);

                // Reload: identifiers are persisted and new ones continue above them.
                database = JsonApplicationsDatabase.Load(fileName);
                Assert.That(
                    database.QueryServers(0, 0, null!, null!, null!, [], out _).Select(server => server.RecordId),
                    Is.EqualTo(recordIds));
                database.RegisterApplication(CreateServerApplication("urn:test:server-3", "ServerThree"));
                Assert.That(
                    database.QueryServers(recordIds[recordIds.Length - 1], 0, null!, null!, null!, [], out _).Single().DiscoveryUrl,
                    Is.EqualTo("opc.tcp://localhost:4840/ServerThree"));

                // A database saved before endpoints had an identifier.
                var json = JsonNode.Parse(File.ReadAllText(fileName))!.AsObject();
                json.Remove("LastServerEndpointId");
                foreach (JsonNode? endpoint in json["ServerEndpoints"]!.AsArray())
                {
                    endpoint!.AsObject().Remove("ID");
                }
                File.WriteAllText(fileName, json.ToJsonString());

                // Loading migrates and persists the identifiers, without a query or a write.
                database = JsonApplicationsDatabase.Load(fileName);
                var migrated = JsonNode.Parse(File.ReadAllText(fileName))!.AsObject();
                Assert.That((uint)migrated["LastServerEndpointId"]!, Is.EqualTo(4u));
                Assert.That(
                    migrated["ServerEndpoints"]!.AsArray().Select(endpoint => (uint)endpoint!["ID"]!),
                    Is.Unique.And.All.Not.Zero);

                ServerOnNetwork[] legacy = database.QueryServers(0, 1, null!, null!, null!, [], out _);
                Assert.That(legacy, Has.Length.EqualTo(1));
                Assert.That(legacy[0].RecordId, Is.Not.Zero);
                uint[] legacyIds = [.. database.QueryServers(0, 0, null!, null!, null!, [], out _)
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
                0, 0, applicationName, applicationUri, null!, [], out _);
            ApplicationDescription[] applications = database.QueryApplications(
                0, 0, applicationName, applicationUri, 0, null!, [], out _, out _);

            Assert.That(servers, Has.Length.EqualTo(expectedServers));
            Assert.That(applications, Has.Length.EqualTo(expectedApplications));
        }

        [TestCase(uint.MaxValue)]
        [TestCase((uint)int.MaxValue + 1)]
        public void QueryApplicationsStartingRecordIdAboveInt32ReturnsNothing(uint startingRecordId)
        {
            LinqApplicationsDatabase database = CreateConformanceTestDatabase();

            ApplicationDescription[] applications = database.QueryApplications(
                startingRecordId, 0, null!, null!, 0, null!, [], out _, out uint nextRecordId);

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
        /// OPC 10000-12 §7.9.5: FinishRequest returns Bad_InvalidArgument when
        /// the RequestId does not reference a request of the application.
        /// </summary>
        [Test]
        public void FinishRequestOfAnotherApplicationThrowsBadInvalidArgument()
        {
            var database = new LinqApplicationsDatabase();
            NodeId applicationA = database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));
            NodeId applicationB = database.RegisterApplication(CreateServerApplication("urn:test:b", "ServerB"));
            NodeId requestB = database.StartSigningRequest(
                applicationB, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                ByteString.From([1, 2, 3]), "admin");
            database.ApproveRequest(requestB, false);

            Assert.That(
                () => database.FinishRequest(applicationA, requestB, out _, out _, out _, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(
                () => database.ReadRequest(applicationA, requestB, out _, out _, out _, out _, out _, out _, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(
                database.FinishRequest(applicationB, requestB, out _, out _, out _, out _),
                Is.EqualTo(CertificateRequestState.Approved));
        }

        [Test]
        public void StartRequestForAnotherGroupKeepsFirstRequestPending()
        {
            var database = new LinqApplicationsDatabase();
            NodeId application = database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));
            NodeId rsaRequest = database.StartSigningRequest(
                application, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                ByteString.From([1, 2, 3]), "admin");
            NodeId httpsRequest = database.StartSigningRequest(
                application, "DefaultHttpsGroup", "HttpsCertificateType",
                ByteString.From([4, 5, 6]), "admin");

            Assert.That(httpsRequest, Is.Not.EqualTo(rsaRequest));
            database.ApproveRequest(rsaRequest, false);
            Assert.That(
                database.FinishRequest(application, rsaRequest, out string? groupId, out string? typeId, out _, out _),
                Is.EqualTo(CertificateRequestState.Approved));
            Assert.That(groupId, Is.EqualTo("DefaultApplicationGroup"));
            Assert.That(typeId, Is.EqualTo("RsaSha256ApplicationCertificateType"));
            Assert.That(
                database.FinishRequest(application, httpsRequest, out _, out _, out _, out _),
                Is.EqualTo(CertificateRequestState.New));

            // A new request for the same group and type supersedes the old one.
            NodeId renewedRequest = database.StartSigningRequest(
                application, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                ByteString.From([7, 8, 9]), "admin");
            Assert.That(renewedRequest, Is.Not.EqualTo(rsaRequest));
            Assert.That(
                () => database.FinishRequest(application, rsaRequest, out _, out _, out _, out _),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ApplicationCertificatesSurviveJsonReload()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var database = JsonApplicationsDatabase.Load(fileName);
                NodeId application = database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));
                database.SetApplicationCertificate(
                    application, "RsaSha256ApplicationCertificateType", ByteString.From([1, 2, 3]));
                database.SetApplicationTrustLists(
                    application, "RsaSha256ApplicationCertificateType", "pki/trusted");

                database = JsonApplicationsDatabase.Load(fileName);

                Assert.That(
                    database.GetApplicationCertificate(
                        application, "RsaSha256ApplicationCertificateType", out ByteString certificate),
                    Is.True);
                Assert.That(certificate.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(
                    database.GetApplicationTrustLists(
                        application, "RsaSha256ApplicationCertificateType", out string? trustListId),
                    Is.True);
                Assert.That(trustListId, Is.EqualTo("pki/trusted"));
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Test]
        public void LoadOfCorruptDatabaseThrowsAndKeepsTheFile()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                const string corrupt = "{ \"Applications\": [ { \"ApplicationUri\": ";
                File.WriteAllText(fileName, corrupt);

                Assert.That(() => JsonApplicationsDatabase.Load(fileName), Throws.TypeOf<InvalidDataException>());
                Assert.That(File.ReadAllText(fileName), Is.EqualTo(corrupt));

                // An empty file is an empty database.
                File.WriteAllText(fileName, string.Empty);
                Assert.That(JsonApplicationsDatabase.Load(fileName).FindApplications("urn:test:a"), Is.Empty);
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// A database file that cannot be replaced by another file (on Unix a
        /// single-file bind mount; here a handle without delete sharing) is
        /// updated in place.
        /// </summary>
        [Test]
        public void SaveUpdatesDatabaseFileThatCannotBeReplaced()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var database = JsonApplicationsDatabase.Load(fileName);
                database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));

                using (new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    database.RegisterApplication(CreateServerApplication("urn:test:b", "ServerB"));
                }

                database = JsonApplicationsDatabase.Load(fileName);
                Assert.That(database.FindApplications("urn:test:a"), Has.Length.EqualTo(1));
                Assert.That(database.FindApplications("urn:test:b"), Has.Length.EqualTo(1));
                Assert.That(File.Exists(fileName + ".tmp"), Is.False);
            }
            finally
            {
                File.Delete(fileName);
                File.Delete(fileName + ".tmp");
            }
        }

#if NET8_0_OR_GREATER // .NET Framework has no link/mode APIs
        /// <summary>
        /// A database file that is a symbolic link keeps being a link: the
        /// save updates the link target.
        /// </summary>
        [Test]
        public void SaveThroughSymbolicLinkUpdatesTheTarget()
        {
            string target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            string link = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                File.WriteAllText(target, string.Empty);
                try
                {
                    File.CreateSymbolicLink(link, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Assert.Ignore("Creating a symbolic link is not permitted: " + ex.Message);
                }

                var database = JsonApplicationsDatabase.Load(link);
                database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));

                Assert.That(new FileInfo(link).LinkTarget, Is.Not.Null);
                Assert.That(
                    JsonApplicationsDatabase.Load(target).FindApplications("urn:test:a"),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                File.Delete(link);
                File.Delete(target);
            }
        }

        /// <summary>
        /// The save keeps the file mode an administrator set on the database.
        /// </summary>
        [Test]
        public void SaveKeepsUnixFileMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes do not exist on Windows.");
                return;
            }

            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                File.WriteAllText(fileName, string.Empty);
                const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                File.SetUnixFileMode(fileName, mode);

                var database = JsonApplicationsDatabase.Load(fileName);
                database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));

                Assert.That(File.GetUnixFileMode(fileName), Is.EqualTo(mode));
            }
            finally
            {
                File.Delete(fileName);
            }
        }
#endif

        /// <summary>
        /// OPC 10000-12 §7.9.4: the private key password shall not be persisted.
        /// </summary>
        [Test]
        public void NewKeyPairRequestPasswordIsNotPersisted()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var database = JsonApplicationsDatabase.Load(fileName);
                NodeId application = database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));
                NodeId request = database.StartNewKeyPairRequest(
                    application, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                    "CN=ServerA", ["localhost"], "PFX", "secret-password".AsSpan(), "admin");
                database.ApproveRequest(request, false);

                Assert.That(File.ReadAllText(fileName), Does.Not.Contain("secret-password"));
                Assert.That(
                    database.ReadRequest(application, request, out _, out _, out _, out _, out _, out _,
                        out ReadOnlySpan<char> password),
                    Is.EqualTo(CertificateRequestState.Approved));
                Assert.That(password.ToString(), Is.EqualTo("secret-password"));

                // After a reload the password is gone, so the request cannot be
                // completed with an unprotected private key.
                database = JsonApplicationsDatabase.Load(fileName);
                Assert.That(
                    database.FinishRequest(application, request, out _, out _, out _, out _),
                    Is.EqualTo(CertificateRequestState.Rejected));
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// A request persisted with its password by an earlier version keeps the
        /// password across the upgrade (in memory only) instead of completing with
        /// an unprotected key, and the next save no longer writes it.
        /// </summary>
        [Test]
        public void LegacyPersistedPrivateKeyPasswordIsRestoredAndNoLongerWritten()
        {
            string fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try
            {
                var database = JsonApplicationsDatabase.Load(fileName);
                NodeId application = database.RegisterApplication(CreateServerApplication("urn:test:a", "ServerA"));
                NodeId request = database.StartNewKeyPairRequest(
                    application, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                    "CN=ServerA", ["localhost"], "PFX", "secret-password".AsSpan(), "admin");
                database.ApproveRequest(request, false);

                // Rewrite the file the way earlier versions stored the request.
                var json = JsonNode.Parse(File.ReadAllText(fileName))!.AsObject();
                JsonObject stored = json["CertificateRequests"]!.AsArray()[0]!.AsObject();
                stored.Remove("HasPrivateKeyPassword");
                var legacyPassword = new JsonArray();
                foreach (char c in "secret-password")
                {
                    legacyPassword.Add(c.ToString());
                }
                stored["PrivateKeyPassword"] = legacyPassword;
                File.WriteAllText(fileName, json.ToJsonString());

                database = JsonApplicationsDatabase.Load(fileName);
                Assert.That(
                    database.ReadRequest(application, request, out _, out _, out _, out _, out _, out _,
                        out ReadOnlySpan<char> password),
                    Is.EqualTo(CertificateRequestState.Approved));
                Assert.That(password.ToString(), Is.EqualTo("secret-password"));

                database.RegisterApplication(CreateServerApplication("urn:test:b", "ServerB"));
                Assert.That(File.ReadAllText(fileName), Does.Not.Contain("\"PrivateKeyPassword\""));
            }
            finally
            {
                File.Delete(fileName);
            }
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
