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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Client.Tests
{
    public class ReferenceServerForThisUnitTest : ReferenceServer
    {
        public uint Test_MaxBrowseReferencesPerNode { get; set; } = 10u;
        public override ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return base.Browse(
                requestHeader,
                view,
                Test_MaxBrowseReferencesPerNode,
                nodesToBrowse,
                out results,
                out diagnosticInfos
                );
            
        }

        public void SetMaxNumberOfContinuationPoints( uint maxNumberOfContinuationPoints )
        {
            Configuration.ServerConfiguration.MaxBrowseContinuationPoints = (int) maxNumberOfContinuationPoints;
        }
        
    }


    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client"), Category("NodeCache")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]

    public class ContinuationPointInBatchTest : ClientTestFramework
    {
        public ReferenceServerForThisUnitTest ReferenceServerForThisUnitTest { get; set; }
        public ServerFixture<ReferenceServerForThisUnitTest> ServerFixtureForThisUnitTest { get; set; }
        public override async Task CreateReferenceServerFixture(
                bool enableTracing,
                bool disableActivityLogging,
                bool securityNone,
                TextWriter writer
            )
        {
            {
                // start Ref server
                ServerFixtureForThisUnitTest = new ServerFixture<ReferenceServerForThisUnitTest>(enableTracing, disableActivityLogging) {
                    UriScheme = UriScheme,
                    SecurityNone = securityNone,
                    AutoAccept = true,
                    AllNodeManagers = true,
                    OperationLimits = true
                };
            }
            
            if (writer != null)
            {
                ServerFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
            }

            await ServerFixtureForThisUnitTest.LoadConfiguration(PkiRoot).ConfigureAwait(false);
            ServerFixtureForThisUnitTest.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixtureForThisUnitTest.Config.TransportQuotas.MaxByteStringLength =
            ServerFixtureForThisUnitTest.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;            

            ServerFixtureForThisUnitTest.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixtureForThisUnitTest.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixtureForThisUnitTest.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Opc.Ua.Profiles.JwtUserToken });

            ServerFixtureForThisUnitTest.Config.ServerConfiguration.MaxBrowseContinuationPoints = 2;
            ServerFixtureForThisUnitTest.Config.ServerConfiguration.OperationLimits.MaxNodesPerBrowse = 5;

            ReferenceServerForThisUnitTest = await ServerFixtureForThisUnitTest.StartAsync(writer ?? TestContext.Out).ConfigureAwait(false);
            ReferenceServerForThisUnitTest.TokenValidator = this.TokenValidator;
            ReferenceServer = ReferenceServerForThisUnitTest;
            ServerFixturePort = ServerFixtureForThisUnitTest.Port;
        }
        public ContinuationPointInBatchTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)       
        { }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            // create a new session for every test
            SingleSession = false;
            return base.OneTimeSetUpAsync(null, true, false, false);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new async Task SetUp()
        {
            await base.SetUp().ConfigureAwait(false);

            // clear node cache
            Session.NodeCache.Clear();
            // for Debugging
            Session.KeepAliveInterval = 45000; // ms?
            Session.OperationTimeout = 45000;
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public new void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public new void GlobalCleanup()
        {
            base.GlobalCleanup();
        }
        #endregion

        #region Tests

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>        
        [Test, Order(100)]
        public void NodeCache_BrowseAllVariables()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            Session.FetchTypeTree(ReferenceTypeIds.References);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = Session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true);
                        nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                        var objectNodes = organizers.Where(n => n is ObjectNode);
                        var variableNodes = organizers.Where(n => n is VariableNode);
                        result.AddRange(variableNodes);
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                        {
                            TestContext.Out.WriteLine($"Access denied: Skip node {node}.");
                        }
                    }
                }
                nodesToBrowse = new ExpandedNodeIdCollection(nextNodesToBrowse.Distinct());
                TestContext.Out.WriteLine("Found {0} duplicates", nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }
        [Test, Order(200)]
        public void BrowseWithManyContinuationPoints_SessionClientBatched()
        {
            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode = 10;
            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(2);

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();

            BrowseDescriptionCollection browseDescriptions = new BrowseDescriptionCollection();
            foreach (NodeId nodeId in nodeIds)
            {
                BrowseDescription bd = new BrowseDescription() {
                    NodeId = nodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseDirection = BrowseDirection.Forward,
                    IncludeSubtypes = true
                };
                browseDescriptions.Add(bd);
            }
            

            BrowseResultCollection resultsWithContstraints = new BrowseResultCollection();
            DiagnosticInfoCollection diagnosticInfosWithConstraints = new DiagnosticInfoCollection();
            Session.Browse(
                null,
                null,
                0u,
                browseDescriptions,
                out resultsWithContstraints,
                out diagnosticInfosWithConstraints
                );

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode = 1000;
            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(10);

            BrowseResultCollection resultsWithDefaultSettings = new BrowseResultCollection();
            DiagnosticInfoCollection diagnosticInfosWithDefaultSettings = new DiagnosticInfoCollection();

            Session.Browse(
                null,
                null,
                0u,
                browseDescriptions,
                out resultsWithDefaultSettings,
                out diagnosticInfosWithDefaultSettings
                );

            Assert.AreEqual(resultsWithDefaultSettings.Count, resultsWithContstraints.Count);
            for(int i = 0; i < resultsWithDefaultSettings.Count; i++)
            {
                Assert.AreEqual(resultsWithDefaultSettings[i].References.Count, resultsWithContstraints[i].References.Count);   
            }

        }

        [Test, Order(250)]
        public void BrowseWithManyContinuationPoints()
        {
            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode = 10;
            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(2);

            ByteStringCollection ContinuationPoints = new ByteStringCollection();
            IList<ReferenceDescriptionCollection> referenceDescriptions = new List<ReferenceDescriptionCollection>();
            IList<ServiceResult> errors = new List<ServiceResult>();

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();

            Session theSession = ((Session)(((TraceableSession)Session).Session));

            // ISession does not now this session method with this signature
            List<NodeId> firstBatch = nodeIds.Take(5).ToList();
            List<NodeId> secondBatch = nodeIds.Skip(5).ToList();
            List<ReferenceDescriptionCollection> result = new List<ReferenceDescriptionCollection>();
            ByteStringCollection continuationPoints = new ByteStringCollection();
            List<ServiceResult> theErrors = new List< ServiceResult>();
            theSession.Browse(
           //((Session) (TraceableSession)Session.Ses).Browse(
                null,
                null,
                firstBatch,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0,
                out ContinuationPoints,
                out referenceDescriptions,
                out errors
                );

            result.AddRange(referenceDescriptions);
            continuationPoints.AddRange(ContinuationPoints);
            theErrors.AddRange(errors);

            theSession.Browse(
    //((Session) (TraceableSession)Session.Ses).Browse(
                null,
                null,
                secondBatch,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0,
                out ContinuationPoints,
                out referenceDescriptions,
                out errors
                );

            result.AddRange(referenceDescriptions);
            continuationPoints.AddRange(ContinuationPoints);
            theErrors.AddRange(errors);

            theSession.BrowseNext(
                null,
                false,
                continuationPoints,
                out ContinuationPoints,
                out referenceDescriptions,
                out errors);
        }

        [Test, Order(300)]
        public void ManagedBrowseWithManyContinuationPoints()
        {
            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode = 10;
            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(2);

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();

            Session theSession = ((Session)(((TraceableSession)Session).Session));

            List<ReferenceDescriptionCollection> result = new List<ReferenceDescriptionCollection>();
            ByteStringCollection continuationPoints = new ByteStringCollection();
            List<ServiceResult> theErrors = new List<ServiceResult>();

            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptions1, out var errors1);

            Assert.AreEqual(nodeIds.Count, referenceDescriptions1.Count);

            Random random = new Random();



            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode = 1000;
            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(0);
            theSession.Browse(null, null, nodeIds, 0, BrowseDirection.Forward,
                ReferenceTypeIds.Organizes, true, 0,
                out var continuationPoints2ndBrowse,
                out var referenceDescriptions2ndBrowse,
                out var errors2ndBrowse );

            int index = 0;
            foreach (var referenceDescription in referenceDescriptions1)
            {
                String randomNodeName =
                    referenceDescription[random.Next(0, referenceDescription.Count - 1)].DisplayName.Text;
                String suffix = getSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));                

                int ii = random.Next(0, referenceDescription.Count - 1);

                Assert.AreEqual(referenceDescription.Count, referenceDescriptions2ndBrowse[index].Count);
                Assert.AreEqual(referenceDescription[ii].NodeId, referenceDescriptions2ndBrowse[index][ii].NodeId);

                index++;
            }


            int dummy = 0;
            dummy++;
        }

            #endregion
            List<NodeId> getMassFolderNodesToBrowse()
        {
            
            String MassFolderPrefix = "Scalar_Simulation_Mass_";

            List<String> nodesToBrowse = new List<String>();
            foreach(string suffix in getSuffixesForMassFolders())
            {
                nodesToBrowse.Add(MassFolderPrefix+suffix);
            }

            int nsi = Session.NamespaceUris.GetIndex("http://opcfoundation.org/Quickstarts/ReferenceServer");
            List<NodeId> result = new List<NodeId>();
            foreach (String nodeString in nodesToBrowse)
            {
                result.Add(new NodeId(nodeString, (ushort)nsi));                
            }
            return result;
        }

        List<string> getSuffixesForMassFolders()
        {
            return new List<string>
            {
                "Boolean", "Byte", "ByteString", "DateTime", "Double", "Duration", "Float", "Guid",
                "Int16", "Int32", "Int64", "Integer", "LocaleId", "LocalizedText", "NodeId", "Number",
                "QualifiedName", "SByte", "String", "UInt16", "UInt32", "UInt64", "UInteger", "UtcTime",
                "Variant", "XmlElement"
            };
        }

    }
}
