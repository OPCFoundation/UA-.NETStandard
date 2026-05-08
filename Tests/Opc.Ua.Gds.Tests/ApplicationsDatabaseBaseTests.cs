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
        public void FindApplicationsWhitespaceThrowsBadInvalidArgument()
        {
            var database = new TestApplicationsDatabase();

            Assert.That(
                () => database.FindApplications(" "),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void QueryApplicationsInvalidTypeThrowsBadInvalidArgument()
        {
            var database = new TestApplicationsDatabase();

            Assert.That(
                () => database.QueryApplications(
                    0,
                    10,
                    null,
                    null,
                    3,
                    null,
                    [],
                    out _,
                    out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidArgument));
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

        private sealed class TestApplicationsDatabase : ApplicationsDatabaseBase
        {
        }
    }
}
