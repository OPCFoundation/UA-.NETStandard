/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test GDS Registration and Client Pull.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest
    {
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        EndpointDescriptionCollection m_endpoints;
        Session m_session;
        string m_url;

        #region DataPointSources
        [DatapointSource]
        public static string[] Policies = SecurityPolicies.GetDisplayNames()
            .Select(displayName => SecurityPolicies.GetUri(displayName)).ToArray();
        #endregion


        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer>();
            m_clientFixture = new ClientFixture();
            m_serverFixture.AutoAccept = true;
            m_server = await m_serverFixture.StartAsync(TestContext.Out, true).ConfigureAwait(false);
            await m_clientFixture.LoadClientConfiguration();
            m_url = "opc.tcp://localhost:" + m_serverFixture.Port.ToString();
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_serverFixture.StopAsync();
            Thread.Sleep(1000);
        }

        [SetUp]
        public void SetUp()
        {
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }

        [TearDown]
        public void TearDown()
        {
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            OneTimeSetUpAsync().GetAwaiter().GetResult();
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicy).GetAwaiter().GetResult()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_clientFixture.Disconnect();
            OneTimeTearDownAsync().GetAwaiter().GetResult();
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        [Benchmark]
        public async Task GetEndpoints()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 1000;

            using (var client = DiscoveryClient.Create(new Uri(m_url), endpointConfiguration))
            {
                m_endpoints = await client.GetEndpointsAsync(null);
            }
        }

        [Theory, Order(200)]
        public async Task Connect(string securityPolicy)
        {
            await m_clientFixture.ConnectAsync(await GetEndpointAsync(m_url, securityPolicy));
            m_clientFixture.Disconnect();
        }

        [Theory, Order(300)]
        public async Task BrowseFullAddressSpace(string securityPolicy)
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = 10000;

            // Session
            Session session;
            if (securityPolicy != null)
            {
                session = await m_clientFixture.ConnectAsync(await GetEndpointAsync(m_url, securityPolicy));
            }
            else
            {
                session = m_session;
            }

            // Browse template
            var startingNode = Objects.RootFolder;
            var browseTemplate = new BrowseDescription {
                NodeId = startingNode,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            var browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                new NodeIdCollection(new NodeId[] { Objects.RootFolder }),
                browseTemplate);

            // Browse
            ResponseHeader response;
            uint requestedMaxReferencesPerNode = 5;
            var referenceDescriptions = new ReferenceDescriptionCollection();
            while (browseDescriptionCollection.Any())
            {
                BrowseResultCollection allResults = new BrowseResultCollection();

                requestHeader.Timestamp = DateTime.UtcNow;
                response = session.Browse(requestHeader, null,
                    requestedMaxReferencesPerNode, browseDescriptionCollection,
                    out var browseResultCollection, out var diagnosticsInfoCollection);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticsInfoCollection, browseDescriptionCollection);

                browseDescriptionCollection.Clear();
                allResults.AddRange(browseResultCollection);

                // Browse next
                var continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseResultCollection);
                while (continuationPoints.Any())
                {
                    response = session.BrowseNext(requestHeader, false, continuationPoints,
                        out var browseNextResultCollection, out diagnosticsInfoCollection);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticsInfoCollection, continuationPoints);
                    allResults.AddRange(browseNextResultCollection);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseNextResultCollection);
                }

                // build browse request for next level
                var browseTable = new NodeIdCollection();
                foreach (var result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (var reference in result.References)
                    {
                        browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, null));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate);
            }

            TestContext.Out.WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            foreach (var reference in referenceDescriptions)
            {
                TestContext.Out.WriteLine("NodeId {0} {1} {2}", reference.NodeId, reference.NodeClass, reference.BrowseName);
            }

            // TranslateBrowsePath
            var browsePaths = new BrowsePathCollection(
                referenceDescriptions.Select(r => new BrowsePath() { RelativePath = new RelativePath(r.BrowseName), StartingNode = startingNode })
                );
            response = session.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out var browsePathResults, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            if (securityPolicy != null)
            {
                m_clientFixture.Disconnect();
            }
        }
        #endregion

        #region Benchmarks
        /// <summary>
        /// Enumerator for security policies.
        /// </summary>
        public IEnumerable<string> BenchPolicies() { return Policies; }

        /// <summary>
        /// Helper variable for benchmark.
        /// </summary>
        [ParamsSource(nameof(BenchPolicies))]
        public string SecurityPolicy = SecurityPolicies.None;

        /// <summary>
        /// Benchmark wrapper for browse tests.
        /// </summary>
        [Benchmark]
        public async Task BrowseFullAddressSpaceBenchmark()
        {
            await BrowseFullAddressSpace(null).ConfigureAwait(false);
        }
        #endregion

        #region Private Methods
        private async Task<ConfiguredEndpoint> GetEndpointAsync(string url, string securityPolicy)
        {
            if (m_endpoints == null)
            {
                await GetEndpoints().ConfigureAwait(false);
            }
            var endpointDescription = SelectEndpoint(new Uri(url), securityPolicy);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            return new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        }

        private EndpointDescription SelectEndpoint(Uri url, string securityPolicy)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
            foreach (var endpoint in m_endpoints)
            {
                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme))
                {
                    // skip unsupported security policies
                    if (SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri) == null)
                    {
                        continue;
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                        continue;
                    }

                    if (selectedEndpoint?.SecurityMode < endpoint.SecurityMode &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }
            // return the selected endpoint.
            return selectedEndpoint;
        }
    }
    #endregion
}
