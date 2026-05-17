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

using System.Linq;
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
        public void RegisterApplicationDuplicateUriThrowsBadEntryExists()
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
        public void UpdateApplicationChangingUriThrowsBadWriteNotSupported()
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

            Assert.That(pagedServers.Length, Is.GreaterThan(0));
            Assert.That(pagedServers.All(server => server.RecordId > firstRecordId), Is.True);
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
