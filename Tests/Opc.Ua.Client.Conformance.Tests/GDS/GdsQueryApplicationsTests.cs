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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for GDS QueryApplications service,
    /// including filtering, pagination, and counter reset time.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("GDS")]
    [Category("GDSQueryApplications")]
    public class GdsQueryApplicationsTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task QueryApplicationsSetUp()
        {
            m_directoryNodeId = ToNodeId(Gds.ObjectIds.Directory);

            // Register several test applications for query tests
            for (int i = 1; i <= 5; i++)
            {
                ApplicationRecordDataType appRecord = CreateTestApplicationRecord(
                    $"Query{i}",
                    i <= 3 ? ApplicationType.Server : ApplicationType.Client);
                if (i == 2)
                {
                    appRecord.ServerCapabilities = new string[] { "DA", "HDA" }.ToArrayOf();
                }
                if (i == 3)
                {
                    appRecord.ServerCapabilities = new string[] { "AC" }.ToArrayOf();
                }
                NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
                m_registeredAppIds.Add(appId);
            }
        }

        [OneTimeTearDown]
        public async Task QueryApplicationsTearDown()
        {
            foreach (NodeId appId in m_registeredAppIds)
            {
                try
                {
                    await UnregisterApplicationAsync(appId).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
            m_registeredAppIds.Clear();
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsWithNoFilterReturnsAllRegisteredAsync()
        {
            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            Assert.That(applications, Has.Count.GreaterThanOrEqualTo(m_registeredAppIds.Count),
                "QueryApplications with no filter should return at least the registered apps.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsWithApplicationUriFilterAsync()
        {
            const string targetUri = "urn:opcfoundation.org:ctt:test:app:Query1";

            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: targetUri,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            Assert.That(applications, Is.Not.Empty);
            Assert.That(applications.Any(a => a.ApplicationUri == targetUri), Is.True,
                "Expected application not found in filtered results.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "008")]
        public async Task QueryApplicationsWithApplicationNameFilterAsync()
        {
            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: "Test Application Query1",
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            Assert.That(applications, Is.Not.Empty,
                "Should find at least one app matching the name filter.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "028")]
        public async Task QueryApplicationsWithApplicationTypeFilterServerAsync()
        {
            // ApplicationType.Server = 0 in the enum, but QueryApplications uses a bitmask:
            // Bit 0 = Server, Bit 1 = Client, Bit 2 = ClientAndServer, Bit 3 = DiscoveryServer
            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 1, // Server bit
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            foreach (ApplicationDescription app in applications)
            {
                Assert.That(
                    app.ApplicationType,
                    Is.EqualTo(ApplicationType.Server)
                        .Or.EqualTo(ApplicationType.ClientAndServer),
                    "Filtered results should only contain Server or ClientAndServer types.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsWithProductUriFilterAsync()
        {
            const string targetProductUri = "urn:opcfoundation.org:ctt:test:product:Query2";

            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: targetProductUri,
                serverCapabilities: null).ConfigureAwait(false);

            Assert.That(applications, Is.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsWithServerCapabilityFilterAsync()
        {
            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: new string[] { "HDA" }.ToArrayOf()).ConfigureAwait(false);

            Assert.That(applications, Is.Not.Empty,
                "Should find at least one app with HDA capability.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsVerifyLastCounterResetTimeAsync()
        {
            (List<ApplicationDescription> _, DateTime lastCounterResetTime, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 10,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            // LastCounterResetTime should be a valid timestamp
            Assert.That(lastCounterResetTime, Is.Not.EqualTo(DateTime.MinValue),
                "LastCounterResetTime should be a valid timestamp.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "004")]
        public async Task QueryApplicationsWithPaginationMaxRecordsAsync()
        {
            (List<ApplicationDescription> applications, _, _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 2,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            Assert.That(applications, Has.Count.LessThanOrEqualTo(2),
                "Should return at most MaxRecordsToReturn applications.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "004")]
        public async Task QueryApplicationsContinuationWithNextRecordIdAsync()
        {
            // First page
            (List<ApplicationDescription> firstPage, DateTime _, uint nextRecordId) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 2,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            if (firstPage.Count < 2 || nextRecordId == 0)
            {
                Assert.Fail("Not enough applications to test pagination continuation.");
            }

            // Second page
            (List<ApplicationDescription> secondPage, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: nextRecordId,
                maxRecordsToReturn: 2,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            // Verify pages don't overlap
            var firstUris = firstPage.Select(a => a.ApplicationUri).ToHashSet();
            foreach (ApplicationDescription app in secondPage)
            {
                Assert.That(firstUris, Does.Not.Contain(app.ApplicationUri),
                    $"Application {app.ApplicationUri} appears in both pages.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task RegisterMultipleAppsThenQueryAllReturnedAsync()
        {
            (List<ApplicationDescription> applications, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);

            var registeredUris = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
            {
                registeredUris.Add($"urn:opcfoundation.org:ctt:test:app:Query{i}");
            }

            var returnedUris = new HashSet<string>(
                applications.Select(a => a.ApplicationUri));

            foreach (string uri in registeredUris)
            {
                Assert.That(returnedUris, Does.Contain(uri),
                    $"Registered app {uri} not found in QueryApplications results.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "GDS Query Applications")]
        [Property("Tag", "001")]
        public async Task QueryApplicationsAfterUnregisterAppNotInResultsAsync()
        {
            ApplicationRecordDataType appRecord = CreateTestApplicationRecord("QueryUnreg");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            // Verify it appears
            (List<ApplicationDescription> before, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: appRecord.ApplicationUri,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);
            Assert.That(before.Any(a => a.ApplicationUri == appRecord.ApplicationUri), Is.True);

            // Unregister
            await UnregisterApplicationAsync(appId).ConfigureAwait(false);

            // Verify it's gone
            (List<ApplicationDescription> after, DateTime _, uint _) = await QueryApplicationsAsync(
                startingRecordId: 0,
                maxRecordsToReturn: 100,
                applicationName: string.Empty,
                applicationUri: appRecord.ApplicationUri,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: null).ConfigureAwait(false);
            Assert.That(after.Any(a => a.ApplicationUri == appRecord.ApplicationUri), Is.False,
                "Unregistered app should not appear in QueryApplications results.");
        }

        private readonly List<NodeId> m_registeredAppIds = [];

        private async Task<NodeId> RegisterApplicationAsync(
            ApplicationRecordDataType appRecord,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(new ExtensionObject(appRecord))
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(response.Results[0].StatusCode);
            }
            return (NodeId)response.Results[0].OutputArguments[0];
        }

        private async Task UnregisterApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(applicationId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(response.Results[0].StatusCode);
            }
        }

        private async Task<(
            List<ApplicationDescription> applications,
            DateTime lastCounterResetTime,
            uint nextRecordId)> QueryApplicationsAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string>? serverCapabilities,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_QueryApplications);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(startingRecordId),
                            new(maxRecordsToReturn),
                            new(applicationName ?? string.Empty),
                            new(applicationUri ?? string.Empty),
                            new(applicationType),
                            new(productUri ?? string.Empty),
                            new(serverCapabilities.HasValue ? serverCapabilities.Value.ToArray() : [])
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"QueryApplications failed: {response.Results[0].StatusCode}");

            ArrayOf<Variant> outputs = response.Results[0].OutputArguments;
            Assert.That(outputs.Count, Is.GreaterThanOrEqualTo(3),
                "QueryApplications should return at least 3 output arguments.");

            var lastCounterResetTime = ((DateTimeUtc)outputs[0]).ToDateTime();
            uint nextRecordId = (uint)outputs[1];

            // Extract the array of ApplicationDescription from ExtensionObjects
            var applicationsList = new List<ApplicationDescription>();
            if (outputs[2].TryGetValue(out ArrayOf<ExtensionObject> eoArray))
            {
                foreach (ExtensionObject eo in eoArray)
                {
                    if (eo.TryGetValue(out ApplicationDescription appDesc, Session.MessageContext))
                    {
                        applicationsList.Add(appDesc);
                    }
                }
            }

            return (applicationsList, lastCounterResetTime, nextRecordId);
        }

        private NodeId m_directoryNodeId;
    }
}
