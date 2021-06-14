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
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NUnit.Framework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test GDS Registration and Client Pull.
    /// </summary>
    [TestFixture, Category("Server")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ReferenceServerTests
    {
        ServerFixture<ReferenceServer> m_fixture;
        ReferenceServer m_server;
        RequestHeader m_requestHeader;

        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>();
            m_server = await m_fixture.StartAsync(TestContext.Out, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync();
            Thread.Sleep(1000);
        }

        [SetUp]
        public void SetUp()
        {
            m_fixture.SetTraceOutput(TestContext.Out);
            m_requestHeader = m_server.CreateAndActivateSession(TestContext.CurrentContext.Test.Name);
        }

        [TearDown]
        public void TearDown()
        {
            m_requestHeader.Timestamp = DateTime.UtcNow;
            m_server.CloseSession(m_requestHeader);
            m_requestHeader = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>();
            m_server = m_fixture.StartAsync(null, true).GetAwaiter().GetResult();
            m_requestHeader = m_server.CreateAndActivateSession("Bench");
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_server.CloseSession(m_requestHeader);
            m_fixture.StopAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Test for expected exceptions.
        /// </summary>
        [Test]
        public void ServiceResultException()
        {
            // test invalid timestamp
            var sre = Assert.Throws<ServiceResultException>(() => m_server.CloseSession(m_requestHeader, false));
            Assert.AreEqual(StatusCodes.BadInvalidTimestamp, sre.StatusCode);
        }

        /// <summary>
        /// Get Endpoints.
        /// </summary>
        [Test]
        public void GetEndpoints()
        {
            var endpoints = m_server.GetEndpoints();
            Assert.NotNull(endpoints);
        }

        /// <summary>
        /// Browse address space.
        /// </summary>
        [Test]
        [Benchmark]
        public void Read()
        {
            // Read
            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToRead = new ReadValueIdCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            foreach (var attributeId in ServerFixtureUtils.AttributesIds.Keys)
            {
                nodesToRead.Add(new ReadValueId() { NodeId = nodeId, AttributeId = attributeId });
            }
            double maxAge = 1000;
            var response = m_server.Read(requestHeader, maxAge, TimestampsToReturn.Neither, nodesToRead,
                out var dataValues, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, dataValues);
        }

        /// <summary>
        /// Browse address space.
        /// </summary>
        [Test]
        [Benchmark]
        public void Write()
        {
            // Write
            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToWrite = new WriteValueCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            nodesToWrite.Add(new WriteValue() { NodeId = nodeId, AttributeId = Attributes.Value, Value = new DataValue(1234) });
            var response = m_server.Write(requestHeader, nodesToWrite,
                out var dataValues, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, dataValues);
        }

        /// <summary>
        /// Browse full address space.
        /// </summary>
        [Test]
        [Benchmark]
        public void BrowseFullAddressSpace()
        {
            // Session
            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = 10000;

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
                response = m_server.Browse(requestHeader, null,
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
                    response = m_server.BrowseNext(requestHeader, false, continuationPoints,
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
            response = m_server.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out var browsePathResults, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);
        }
        #endregion
    }
}
