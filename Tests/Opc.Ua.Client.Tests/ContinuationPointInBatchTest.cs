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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Opc.Ua.Server;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Client.Tests
{
    public class ManagedBrowseExpectedResultValues
    {
        public uint InputMaxNumberOfContinuationPoints { get; set; } = 0;
        public uint InputMaxNumberOfReferencesPerNode { get; set; } = 0;
        public int ExpectedNumberOfPasses { get; set; } = 0;
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }

    }

    public class ManagedBrowseTestDataProvider : IFormattable
    {
        public uint MaxNumberOfContinuationPoints { get; set; } = 0;
        public uint MaxNumberOfReferencesPerNode { get; set; } = 0;
        public int ExpectedNumberOfPasses { get; set; } = 0;
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"{MaxNumberOfContinuationPoints}:{MaxNumberOfReferencesPerNode}";
        }
    }




    internal class CPBatchTestMemoryWriter : TextWriter
    {
        private MemoryStream m_stream = new MemoryStream(64000);
        private StreamWriter m_writer = null;

        public override Encoding Encoding => Encoding.Default;
        public CPBatchTestMemoryWriter()
        {
            m_writer = new StreamWriter(m_stream);
            m_writer.AutoFlush = true;
        }
        public override void Write(char value)
        {
            m_writer.Write(value);
        }

        public List<String> getEntries()
        {
            m_stream.Position = 0;
            List<string> entries = new List<string>();
            using (var sr = new StreamReader(m_stream))
            {
                string line;
                while ((line = sr.ReadLine()) != null) { entries.Add(line); }
            }
            // get entries closes the stream.
            m_stream = new MemoryStream(64000);
            m_writer = new StreamWriter(m_stream);
            m_writer.AutoFlush = true;
            return entries;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                m_writer?.Dispose();
                m_stream?.Dispose();
            }
            catch { }
        }
    };


                // check for error.   
                result.StatusCode = error.StatusCode;

                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    DiagnosticInfo diagnosticInfo = null;

                    if (error != null && error.Code != StatusCodes.Good)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }

                    diagnosticInfos.Add(diagnosticInfo);
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }


    }

    public class ReferenceServerForThisUnitTest : ReferenceServer
    {
        public uint Test_MaxBrowseReferencesPerNode { get; set; } = 10u;
        private MasterNodeManager MasterNodeManagerReference { get; set; }
        private SessionManagerForTest SessionManagerForTest { get; set; }

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

        public void SetMaxNumberOfContinuationPoints(uint maxNumberOfContinuationPoints)
        {
            Configuration.ServerConfiguration.MaxBrowseContinuationPoints = (int)maxNumberOfContinuationPoints;
            ((MasterNodeManagerForThisUnitTest)MasterNodeManagerReference).MaxContinuationPointsPerBrowseForUnitTest = maxNumberOfContinuationPoints;
            List<Opc.Ua.Server.Session> theServerSideSessions = SessionManagerForTest.GetSessions().ToList();
            foreach (Opc.Ua.Server.Session session in theServerSideSessions)
            {
                try
                {
                    ((ServerSessionForTest)session).SetMaxNumberOfContinuationPoints(maxNumberOfContinuationPoints);
                }
                catch { }
            }

        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Utils.LogInfo(Utils.TraceMasks.StartStop, "Creating the Reference Server Node Manager.");

            IList<INodeManager> nodeManagers = new List<INodeManager>();

            // create the custom node manager.
            nodeManagers.Add(new ReferenceNodeManager(server, configuration));

            foreach (var nodeManagerFactory in NodeManagerFactories)
            {
                nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
            }
            //this.MasterNodeManagerReference = new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
            this.MasterNodeManagerReference = new MasterNodeManagerForThisUnitTest(server, configuration, null, nodeManagers.ToArray());
            // create master node manager.
            return this.MasterNodeManagerReference;
        }

        protected override SessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            this.SessionManagerForTest = new SessionManagerForTest(server, configuration);
            return this.SessionManagerForTest;
        }
    }

    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client"), Category("ManagedBrowseWithBrowseNext")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(CPFixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]

    public class ContinuationPointInBatchTest : ClientTestFramework
    {

        public static readonly object[] CPFixtureArgs = {
            new object [] { Utils.UriSchemeOpcTcp}
        };

        [DatapointSource]
        public IEnumerable<ManagedBrowseTestDataProvider> ManagedBrowseTestDataValues()
        {
            yield return new ManagedBrowseTestDataProvider {
                MaxNumberOfContinuationPoints = 2,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 5,
                ExpectedNumberOfBadNoCPSCs = new List<int> { 15, 9, 5, 3, 1 }
            };
            yield return new ManagedBrowseTestDataProvider {
                MaxNumberOfContinuationPoints = 4,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 2,
                ExpectedNumberOfBadNoCPSCs = new List<int> { 5, 1 }
            };
            yield return new ManagedBrowseTestDataProvider {
                MaxNumberOfContinuationPoints = 20,
                MaxNumberOfReferencesPerNode = 50,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };
            yield return new ManagedBrowseTestDataProvider {
                MaxNumberOfContinuationPoints = 5,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };
        }

        public ReferenceServerWithLimits ReferenceServerWithLimits { get; set; }
        public ServerFixture<ReferenceServerWithLimits> ServerFixtureWithLimits { get; set; }
        public override async Task CreateReferenceServerFixture(
                bool enableTracing,
                bool disableActivityLogging,
                bool securityNone,
                TextWriter writer
            )
        {
            TextWriter localWriter = enableTracing ? writer : null;
            {

                // start Ref server                                
                ServerFixtureWithLimits = new ServerFixture<ReferenceServerWithLimits>(enableTracing, disableActivityLogging) {
                    UriScheme = UriScheme,
                    SecurityNone = securityNone,
                    AutoAccept = true,
                    AllNodeManagers = true,
                    OperationLimits = true
                };
            }

            if (writer != null && enableTracing)
            {
                ServerFixtureWithLimits.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
            }

            await ServerFixtureWithLimits.LoadConfiguration(PkiRoot).ConfigureAwait(false);
            ServerFixtureWithLimits.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixtureWithLimits.Config.TransportQuotas.MaxByteStringLength =
            ServerFixtureWithLimits.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;

            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Opc.Ua.Profiles.JwtUserToken });

            ServerFixtureWithLimits.Config.ServerConfiguration.MaxBrowseContinuationPoints = 2;
            ServerFixtureWithLimits.Config.ServerConfiguration.OperationLimits.MaxNodesPerBrowse = 5;

            ReferenceServerWithLimits = await ServerFixtureWithLimits.StartAsync(localWriter).ConfigureAwait(false);
            ReferenceServerWithLimits.TokenValidator = this.TokenValidator;
            ReferenceServer = ReferenceServerWithLimits;
            ServerFixturePort = ServerFixtureWithLimits.Port;
        }
        public ContinuationPointInBatchTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        { }

        public ContinuationPointInBatchTest() { }

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
            return base.OneTimeSetUpAsync(null, false, false, false, true);
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
            Session.NodeCache.Clear();
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
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// 
        /// Browse all variables in the objects folder.
        /// </summary>
        [Theory, Order(100)]
        public void MBNodeCache_BrowseAllVariables(ManagedBrowseTestDataProvider testData)
        {
            Session theSession = ((Session)(((TraceableSession)Session).Session));
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

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

        /// <summary>
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// Browse all variables in the objects folder.
        /// </summary>
        [Theory, Order(110)]
        public void MBNodeCache_BrowseAllVariables_MultipleNodes(ManagedBrowseTestDataProvider testData,
            ContinuationPointPolicy policy)
        {
            Session theSession = ((Session)(((TraceableSession)Session).Session));
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = policy;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            Session.FetchTypeTree(ReferenceTypeIds.References);
            var referenceTypeIds = new NodeIdCollection() { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                try
                {
                    var organizers = Session.NodeCache.FindReferences(
                        nodesToBrowse,
                        referenceTypeIds,
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
                        TestContext.Out.WriteLine($"Access denied: Skipped node.");
                    }
                }
                nodesToBrowse = new ExpandedNodeIdCollection(nextNodesToBrowse.Distinct());
                TestContext.Out.WriteLine("Found {0} duplicates", nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        /// <summary>
        /// For each entry in the datapoint source, the test browses some folders in the reference
        /// server which have 100 subnodes each (see method getMassFolderNodesToBrowse())
        /// The server is configured with a certain number of allowed nodes per browse service call
        /// Depending on the number of allowed browse continuation points, the following
        /// results are expected and verified:
        /// i) if the number of allowed browse continuation points is less than the number
        /// of nodes per browse, the ManagedBrowse method will trigger the status codes 
        /// BadNoContinuationPoint several times, since it uses the number of nodes per browse for
        /// creating packages. It will also have to rebrowse many nodes. With an increasing number
        /// of allowed browse continuation points the number of results with status code
        /// BadNoContinuationPoints is reduced, as well as the number of retry attempts needed
        /// to get all browse results.
        /// ii) if the number of allowed browse continuation points is equal to or greater than
        /// the number of nodes per browse, there will be no status codes which are bad.
        ///
        /// In all cases, the browse will succeed in the end, and the test verifies that
        /// the number of returned nodes from the ManagedBrowse method is correct and also that the reuslts
        /// are returned in the correct sequence. This is done by comparing the results with those
        /// from a plain browse service call with no limit on the max number of browse continuation
        /// points.
        ///
        /// No return value should have the status code BadContinuationPointInvalid, since there is
        /// no attempt to allocate continuation points in parallel from more than one service call.
        /// </summary>        
        [Theory, Order(200)]
        public void ManagedBrowseWithManyContinuationPoints(ManagedBrowseTestDataProvider testData)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));
            theSession.FetchOperationLimits();

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;


            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ManagedBrowseExpectedResultValues pass2ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse =
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();

            // browse with test settings
            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionCollectionsPass1, out var errorsPass1);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionsPass1.Count);

            List<String> memoryLogPass1 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass1, "memoryLogPass1");
#if DEBUG
            VerifyExpectedResults(memoryLogPass1, pass1ExpectedResults);
#endif

            memoryWriter.Close(); memoryWriter.Dispose();

            // reset memory log
            memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);


            //set log level to ensure we get all messages
            base.ClientFixture.SetTraceOutputLevel(Microsoft.Extensions.Logging.LogLevel.Trace);


            // now reset the server qutas to get a browse scenario without continuation points. This allows
            // to verify the result from the first browse service call (with quotas in place).
            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse =
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints;


            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionsPass2, out var errorsPass2);
            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass2.Count);

            List<String> memoryLogPass2 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass2, "memoryLogPass2");
#if DEBUG
            // since there is no randomness in this test, we can verify the results directly
            VerifyExpectedResults(memoryLogPass2, pass2ExpectedResults);
#endif
            memoryWriter.Close(); memoryWriter.Dispose();

            base.ClientFixture.SetTraceOutput(TestContext.Out);
            // reset the log level
            base.ClientFixture.SetTraceOutputLevel();



            // finally browse again with a simple browse service call.
            theSession.Browse(null, null, nodeIds, 0, BrowseDirection.Forward,
                ReferenceTypeIds.Organizes, true, 0,
                out var continuationPoints2ndBrowse,
                out var referenceDescriptionCollections2ndBrowse,
                out var errors2ndBrowse);


            Random random = new Random();
            int index = 0;
            foreach (var referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                Assert.That(referenceDescriptionCollection.Count, Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the type of the nodes are the same, once for each list of reference descriptions
                String randomNodeName =
                    referenceDescriptionCollection[random.Next(0, referenceDescriptionCollection.Count - 1)].DisplayName.Text;
                String suffix = getSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(referenceDescriptionCollection.Count, referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(referenceDescriptionCollection[ii].NodeId, referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }

        }



        /// <summary>
        /// For each entry in the datapoint source, the test browses some folders in the reference
        /// server which have 100 subnodes each (see method getMassFolderNodesToBrowse())
        ///
        /// The server is configured with a certain number of allowed nodes per browse service call
        ///
        /// The ManagedBrowse method is called with the ContinuationPointPolicy 'Balanced'
        /// which forces the method to create packages which have at most
        /// min(maxBrowseContinuationPoints, maxNodesPerBrowse)
        /// nodes.
        /// 
        /// The following results are expected and verified:
        ///
        /// In all cases, the browse will succeed without a status code BadNoContinuationPoints
        /// The test also verifies that the number of returned nodes from the ManagedBrowse method
        /// is correct and also that the reuslts are returned in the correct sequence.
        /// This is done by comparing the results with those
        /// from a plain browse service call with no limit on the max number of browse continuation
        /// points.
        ///
        /// No return value should have the status code BadContinuationPointInvalid, since there is
        /// no attempt to allocate continuation points in parallel from more than one service call.
        /// </summary>
        [Theory, Order(210)]
        public void BalancedManagedBrowseWithManyContinuationPoints(ManagedBrowseTestDataProvider testData)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Balanced;

            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ManagedBrowseExpectedResultValues pass2ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();


            // browse with test settings
            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionCollectionsPass1, out var errorsPass1);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionsPass1.Count);

            List<String> memoryLogPass1 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass1, "memoryLogPass1");
            // this is no typo - we expect no error, hence we use pass2ExpectedResults
            VerifyExpectedResults(memoryLogPass1, pass2ExpectedResults);

            memoryWriter.Close(); memoryWriter.Dispose();

            // reset memory log
            memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);


            //set log level to ensure we get all messages
            base.ClientFixture.SetTraceOutputLevel(Microsoft.Extensions.Logging.LogLevel.Trace);


            // now reset the server qutas to get a browse scenario without continuation points. This allows
            // to verify the result from the first browse service call (with quotas in place).
            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass2ExpectedResults.InputMaxNumberOfContinuationPoints;

            theSession.ContinuationPointPolicy =
                ContinuationPointPolicy.Balanced;

            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionsPass2, out var errorsPass2);
            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass2.Count);

            List<String> memoryLogPass2 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass2, "memoryLogPass2");

            // since there is no randomness in this test, we can verify the results directly
            VerifyExpectedResults(memoryLogPass2, pass2ExpectedResults);

            memoryWriter.Close(); memoryWriter.Dispose();

            base.ClientFixture.SetTraceOutput(TestContext.Out);
            // reset the log level
            base.ClientFixture.SetTraceOutputLevel();



            // finally browse again with a simple browse service call.
            theSession.Browse(null, null, nodeIds, 0, BrowseDirection.Forward,
                ReferenceTypeIds.Organizes, true, 0,
                out var continuationPoints2ndBrowse,
                out var referenceDescriptionCollections2ndBrowse,
                out var errors2ndBrowse);


            Random random = new Random();
            int index = 0;
            foreach (var referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                Assert.That(referenceDescriptionCollection.Count, Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the type of the nodes are the same, once for each list of reference descriptions
                String randomNodeName =
                    referenceDescriptionCollection[random.Next(0, referenceDescriptionCollection.Count - 1)].DisplayName.Text;
                String suffix = getSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(referenceDescriptionCollection.Count, referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(referenceDescriptionCollection[ii].NodeId, referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }

        }

        /// <summary>
        /// in this test the service result BadContinuationPoint invalid in (an unpredictable subset)
        /// of the return values from the ManagedBrowse method call is enforced, by
        /// concurrently executing the method on two sets of nodes both of which
        /// require the allocation of BrowseContinuationPoints in the server
        ///
        /// The following results are expected:
        /// on a system which supports parallel execution of threads, at least one of the parallel calls
        /// (usually all of them) to method ManagedBrowse will produce results with status code
        /// BadContinuationPointInvalid
        ///
        /// In the worst case the two calls to ManagedBrowse could end up in an endless loop (to prevent this
        /// an upper bound for the number of rebrowse attempts would be needed, or a session wide management
        /// of the continuation points the server must potentially allocate for the service calls
        /// from the client).
        /// 
        /// The result with regards to the BadNoContinuationPoint should be similar to the one from
        /// the ManagedBrowseWithManyContinuationPoints test case
        /// </summary>
        /// <param name="testData"></param>

        [Theory, Order(300)]
        public void ParallelManagedBrowseWithManyContinuationPoints(ManagedBrowseTestDataProvider testData,
            ContinuationPointPolicy policy)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));

            theSession.ContinuationPointPolicy = policy;


            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ManagedBrowseExpectedResultValues pass2ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();
            List<NodeId> nodeIds1 = nodeIds.GetRange(0, nodeIds.Count / 2);
            List<NodeId> nodeIds2 = nodeIds.Skip(nodeIds.Count / 2).ToList();

            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass1 = new List<ReferenceDescriptionCollection>();
            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass2 = new List<ReferenceDescriptionCollection>();

            IList<ServiceResult> errorsPass1 = new List<ServiceResult>();
            IList<ServiceResult> errorsPass2 = new List<ServiceResult>();

            Parallel.Invoke(
                () => theSession.ManagedBrowse(
                null, null, nodeIds1, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out referenceDescriptionCollectionsPass1, out errorsPass1),
                () => theSession.ManagedBrowse(
                null, null, nodeIds2, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out referenceDescriptionCollectionsPass2, out errorsPass2)
                );

            Assert.AreEqual(nodeIds1.Count, referenceDescriptionCollectionsPass1.Count);
            Assert.AreEqual(nodeIds2.Count, referenceDescriptionCollectionsPass2.Count);

            List<String> memoryLogPass1 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass1, "memoryLogPass1");

            memoryWriter.Close(); memoryWriter.Dispose();

            // reset memory log
            memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);

            ((List<ReferenceDescriptionCollection>)referenceDescriptionCollectionsPass1).AddRange(referenceDescriptionCollectionsPass2);
            ((List<ServiceResult>)errorsPass1).AddRange(errorsPass2);

            // finally browse again with a simple browse service call.
            // reset server quotas first:

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass2ExpectedResults.InputMaxNumberOfContinuationPoints;


            theSession.Browse(null, null, nodeIds, 0, BrowseDirection.Forward,
                ReferenceTypeIds.Organizes, true, 0,
                out var continuationPoints2ndBrowse,
                out var referenceDescriptionCollections2ndBrowse,
                out var errors2ndBrowse);


            Random random = new Random();
            int index = 0;
            foreach (var referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                Assert.That(referenceDescriptionCollection.Count, Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the types of the nodes are the same, once for each list of reference descriptions
                String randomNodeName =
                    referenceDescriptionCollection[random.Next(0, referenceDescriptionCollection.Count - 1)].DisplayName.Text;
                String suffix = getSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(referenceDescriptionCollection.Count, referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(referenceDescriptionCollection[ii].NodeId, referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }

        }
#endregion Tests

        #region async tests

        /// <summary>
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// 
        /// Browse all variables in the objects folder.
        /// </summary>
        [Theory, Order(400)]
        public async Task MBNodeCache_BrowseAllVariablesAsync(ManagedBrowseTestDataProvider testData)
        {
            Session theSession = ((Session)(((TraceableSession)Session).Session));
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References, new CancellationToken()).ConfigureAwait(false);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = await Session.NodeCache.FindReferencesAsync(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true, new CancellationToken()).ConfigureAwait(false);
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


        /// <summary>
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// Browse all variables in the objects folder.
        /// </summary>
        [Theory, Order(410)]
        public async Task MBNodeCache_BrowseAllVariables_MultipleNodesAsync(ManagedBrowseTestDataProvider testData,
            ContinuationPointPolicy policy)
        {
            Session theSession = ((Session)(((TraceableSession)Session).Session));
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = policy;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References, new CancellationToken()).ConfigureAwait(false);

            var referenceTypeIds = new NodeIdCollection() { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                try
                {
                    var organizers = await Session.NodeCache.FindReferencesAsync(
                        nodesToBrowse,
                        referenceTypeIds,
                        false,
                        true,
                        new CancellationToken()
                        ).ConfigureAwait(false);
                    nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                    var objectNodes = organizers.Where(n => n is ObjectNode);
                    var variableNodes = organizers.Where(n => n is VariableNode);
                    result.AddRange(variableNodes);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        TestContext.Out.WriteLine($"Access denied: Skipped node.");
                    }
                }
                nodesToBrowse = new ExpandedNodeIdCollection(nextNodesToBrowse.Distinct());
                TestContext.Out.WriteLine("Found {0} duplicates", nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }


        /// <summary>
        /// same as the ManagedBrowseWithManyContinuationPoints, but
        /// with the ContinuationPointPolicy set to 'Default'. Instead of calling
        /// ManagedBrowse, ManagedBrowseAsync is called directly.
        /// </summary>
        /// <param name="testData"></param>
        /// <returns></returns>
        [Theory, Order(420)]
        public async Task ManagedBrowseWithManyContinuationPointsAsync(ManagedBrowseTestDataProvider testData)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ManagedBrowseExpectedResultValues pass2ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };

            ReferenceServerWithLimits.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse
                = pass1ExpectedResults.InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();
            // browse with test settings
            (
                IList<ReferenceDescriptionCollection> referenceDescriptionCollectionPass1,
                IList<ServiceResult> errorsPass1
                ) =

            await theSession.ManagedBrowseAsync(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true,
                0, new CancellationToken()).ConfigureAwait(false);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionPass1.Count);

#if DEBUG
            List<String> memoryLogPass1 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass1, "memoryLogPass1");
            VerifyExpectedResults(memoryLogPass1, pass1ExpectedResults);
#endif

            memoryWriter.Close(); memoryWriter.Dispose();
        }
        #endregion async tests

        #region helper methods
        private void WriteMemoryLogToTextOut(List<String> memoryLog, string contextInfo)
        {
            Session theSession = ((Session)(((TraceableSession)Session).Session));

            TestContext.WriteLine($"Note: the clients ServerMaxContinuationPointsPerBrowse was set to {theSession.ServerMaxContinuationPointsPerBrowse}");

            if (memoryLog.Count > 0)
            {
                TestContext.WriteLine($"<!-- begin: output from memory log from context {contextInfo} -->");
                foreach (String s in memoryLog)
                {
                    TestContext.Out.WriteLine(s);
                }
                TestContext.WriteLine($"<!-- end: output from memory log from context {contextInfo} -->");
            }
            else
            {
                TestContext.WriteLine($"<!-- memory log from context {contextInfo} is empty -->");
            }
        }

        private void VerifyExpectedResults(List<string> memoryLogPass,
            ManagedBrowseExpectedResultValues expectedResults)
        {
            List<string> messagesWithBadNoCPSC = memoryLogPass.Where(x => x.Contains("BadNoContinuationPoints")).ToList();

            Assert.IsTrue(messagesWithBadNoCPSC.Count == expectedResults.ExpectedNumberOfBadNoCPSCs.Count);

            int pass = 0;
            foreach (String s in messagesWithBadNoCPSC)
            {
                // get the part of the error message after the time stamp:
                string msg = s.Substring(s.IndexOf("ManagedBrowse"));
                // create error message from expected results
                String expectedString = String.Format(
                    "ManagedBrowse: in pass {0}, {1} {2} occured with a status code {3}.",
                    pass,
                    expectedResults.ExpectedNumberOfBadNoCPSCs[pass],
                    expectedResults.ExpectedNumberOfBadNoCPSCs[pass] == 1 ? "error" :
                    "errors", nameof(StatusCodes.BadNoContinuationPoints));
                Assert.IsTrue(msg.Equals(expectedString));
                pass++;
            }
        }

        List<NodeId> getMassFolderNodesToBrowse()
        {

            String MassFolderPrefix = "Scalar_Simulation_Mass_";

            List<String> nodesToBrowse = new List<String>();
            foreach (string suffix in getSuffixesForMassFolders())
            {
                nodesToBrowse.Add(MassFolderPrefix + suffix);
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

        private void BrowseFullAddressSpace()
        {
            var requestHeader = new RequestHeader {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Session
            var clientTestServices = new ClientTestServices(Session);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader);
        }
        #endregion

    }
}
