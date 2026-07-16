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
using NUnit.Framework;
using Opc.Ua.Gds.Server.Database;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationsDatabaseBaseTests
    {
        [Test]
        public void RegisterApplicationNullThrows()
        {
            var database = new TestApplicationsDatabase();

            Assert.That(
                () => database.RegisterApplication(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterApplicationInvalidApplicationUriThrows()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationUri = "not a uri";

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterApplicationServerWithoutDiscoveryUrlsThrows()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.DiscoveryUrls = [];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterApplicationClientWithDiscoveryUrlsThrows()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationType = ApplicationType.Client;
            application.ServerCapabilities = [];
            application.DiscoveryUrls = ["opc.tcp://localhost:4840"];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterApplicationValidServerReturnsDefaultNodeId()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();

            NodeId applicationId = database.RegisterApplication(application);

            Assert.That(applicationId.IsNull, Is.True);
        }

        [Test]
        public void RegisterApplicationValidServerWithDiscoveryUrlsDoesNotThrow()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.Nothing);
        }

        [Test]
        public void FindApplicationsWhitespaceMatchesAll()
        {
            // Per OPC UA Part 12 §6.3.10, an empty or whitespace-only filter
            // matches all registered Applications (it is not BadInvalidArgument).
            var database = new TestApplicationsDatabase();

            ApplicationRecordDataType[]? results = database.FindApplications(" ");
            Assert.That(results, Is.Null.Or.Empty);
        }

        [Test]
        public void QueryApplicationsInvalidTypeThrows()
        {
            // applicationType filter values per OPC UA Part 12 §6.3.10 / Part 4:
            //   0 = ALL, 1 = SERVER, 2 = CLIENT, 3 = DISCOVERY_SERVER.
            // Anything outside this range is invalid.
            var database = new TestApplicationsDatabase();

            Assert.That(
                () => database.QueryApplications(
                    0,
                    10,
                    null,
                    null,
                    99,
                    null,
                    [],
                    out _,
                    out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("mainserver", "main%", true)]
        [TestCase("green", "%en%", true)]
        [TestCase("content", "%en%", true)]
        [TestCase("alpha", "%en%", false)]
        [TestCase("would", "_ould", true)]
        [TestCase("could", "_ould", true)]
        [TestCase("xould", "_ould", true)]
        [TestCase("mould", "_ould", true)]
        [TestCase("moulder", "_ould", false)]
        [TestCase("5%", "5[%]", true)]
        [TestCase("5_", "5[_]", true)]
        [TestCase("5a", "5[_]", false)]
        [TestCase("abc1", "abc[13-68]", true)]
        [TestCase("abc4", "abc[13-68]", true)]
        [TestCase("abc5", "abc[13-68]", true)]
        [TestCase("abc6", "abc[13-68]", true)]
        [TestCase("abc8", "abc[13-68]", true)]
        [TestCase("abc2", "abc[13-68]", false)]
        [TestCase("abc7", "abc[13-68]", false)]
        [TestCase("abc-", "abc[13-68]", false)]
        [TestCase("xyzc", "xyz[c-f]", true)]
        [TestCase("xyzf", "xyz[c-f]", true)]
        [TestCase("xyzg", "xyz[c-f]", false)]
        [TestCase("ABC2", "ABC[^13-5]", true)]
        [TestCase("ABC0", "ABC[^13-5]", true)]
        [TestCase("ABC6", "ABC[^13-5]", true)]
        [TestCase("ABC9", "ABC[^13-5]", true)]
        [TestCase("ABC1", "ABC[^13-5]", false)]
        [TestCase("ABC3", "ABC[^13-5]", false)]
        [TestCase("ABC4", "ABC[^13-5]", false)]
        [TestCase("ABC0", "ABC[^1-5]", true)]
        [TestCase("ABC3", "ABC[^1-5]", false)]
        [TestCase("xyza", "xyz[^dgh]", true)]
        [TestCase("xyzd", "xyz[^dgh]", false)]
        [TestCase("xyzg", "xyz[^dgh]", false)]
        [TestCase("xyzh", "xyz[^dgh]", false)]
        [TestCase("5%", "5\\%", true)]
        [TestCase("5_", "5\\_", true)]
        [TestCase("\\", "\\\\", true)]
        [TestCase("[", "\\[", true)]
        [TestCase("]", "\\]", true)]
        [TestCase("5a", "5\\%", false)]
        public void MatchImplementsUaWildcardSpecification(
            string target,
            string pattern,
            bool expected)
        {
            bool result = ApplicationsDatabaseBase.Match(target, pattern);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void RegisterApplicationClientWithRcpDiscoveryUrlsAndRcpCapabilityDoesNotThrow()
        {
            // Per OPC 10000-12 §6.5.5 a Client that supports reverse-connect
            // may register DiscoveryUrls beginning with the rcp+ prefix and
            // must list the RCP ServerCapability.
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationType = ApplicationType.Client;
            application.DiscoveryUrls = ["rcp+opc.tcp://localhost:4840"];
            application.ServerCapabilities = ["RCP"];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.Nothing);
        }

        [Test]
        public void RegisterApplicationClientWithRcpDiscoveryUrlsAndMissingCapabilityThrows()
        {
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationType = ApplicationType.Client;
            application.DiscoveryUrls = ["rcp+opc.tcp://localhost:4840"];
            application.ServerCapabilities = [];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterApplicationServerWithRcpDiscoveryUrlThrows()
        {
            // Servers must not register reverse-connect listening URLs; the
            // rcp+ prefix is reserved for Clients / ClientAndServer.
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.DiscoveryUrls = ["rcp+opc.tcp://localhost:4840"];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterApplicationClientAndServerWithMixedUrlsDoesNotThrow()
        {
            // ClientAndServer may include both regular Server DiscoveryUrls
            // and reverse-connect URLs; the RCP ServerCapability is required
            // when reverse-connect URLs are present.
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.DiscoveryUrls =
            [
                "opc.tcp://localhost:4840",
                "rcp+opc.tcp://localhost:4841"
            ];
            application.ServerCapabilities = ["LDS", "RCP"];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.Nothing);
        }

        [Test]
        public void RegisterApplicationClientAndServerWithOnlyRcpDiscoveryUrlsThrows()
        {
            // ClientAndServer must always expose at least one non-rcp+
            // Server DiscoveryUrl.
            var database = new TestApplicationsDatabase();
            ApplicationRecordDataType application = CreateValidServerApplication();
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.DiscoveryUrls = ["rcp+opc.tcp://localhost:4841"];
            application.ServerCapabilities = ["LDS", "RCP"];

            Assert.That(
                () => database.RegisterApplication(application),
                Throws.TypeOf<ArgumentException>());
        }

        private static ApplicationRecordDataType CreateValidServerApplication()
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = "urn:test:application",
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("en", "TestApp")],
                ProductUri = "urn:test:product",
                DiscoveryUrls = ["opc.tcp://localhost:4840"],
                ServerCapabilities = ["LDS"]
            };
        }

        private sealed class TestApplicationsDatabase : ApplicationsDatabaseBase;
    }
}
