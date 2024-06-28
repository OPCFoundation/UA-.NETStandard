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
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Castle.Core.Logging;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Client.Tests
{
    internal class ManagedBrowseExpectedResultValues
    {
        public uint InputMaxNumberOfContinuationPoints { get; set; } = 0;
        public uint InputMaxNumberOfReferencesPerNode { get; set; } = 0;

        public int ExpectedNumberOfPasses { get; set; } = 0;
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }
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

        new public void Dispose()
        {
            Dispose(true);
            base.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                m_writer?.Dispose();
                m_stream?.Dispose();
            } catch { }
        }
    };


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

        public void SetMaxNumberOfContinuationPoints(uint maxNumberOfContinuationPoints)
        {
            Configuration.ServerConfiguration.MaxBrowseContinuationPoints = (int)maxNumberOfContinuationPoints;
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
            TextWriter localWriter = enableTracing ? writer : null;
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
            
            if (writer != null && enableTracing)
            {
                ServerFixtureForThisUnitTest.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
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

            ReferenceServerForThisUnitTest = await ServerFixtureForThisUnitTest.StartAsync(localWriter).ConfigureAwait(false);
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
            //return base.OneTimeSetUpAsync(myWriter, false, true, false, true);
            return base.OneTimeSetUpAsync(null, true, false, false, true);
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
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);

            ManagedBrowseExpectedResultValues pass1ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 2,
                InputMaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 5,
                ExpectedNumberOfBadNoCPSCs = new List<int> { 15, 9, 5, 3, 1 }        
            };

            ManagedBrowseExpectedResultValues pass2ExpectedResults = new ManagedBrowseExpectedResultValues {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = new List<int>()
            };

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode; 

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();

            Session theSession = ((Session)(((TraceableSession)Session).Session));

            List<ReferenceDescriptionCollection> result = new List<ReferenceDescriptionCollection>();
            ByteStringCollection continuationPoints = new ByteStringCollection();
            List<ServiceResult> theErrors = new List<ServiceResult>();

            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionsPass1, out var errorsPass1);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass1.Count);

            List<String> memoryLogPass1 = memoryWriter.getEntries();
            VerifyExpectedResults(memoryLogPass1, pass1ExpectedResults);


            if (memoryLogPass1.Count > 0)
            {
                TestContext.WriteLine("*** begin: output from pass1 memory log ***");
                foreach (String s in memoryLogPass1)
                {
                    TestContext.Out.WriteLine(s);
                }
                TestContext.WriteLine("*** end: output from pass1 memory log ***");
            }
            else
            {
                TestContext.WriteLine("*** memory log from pass1 is empty ***");
            }

            memoryWriter.Close(); memoryWriter.Dispose();

            // reset memory log
            memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);


            // set log level to ensure we get all messages
            base.ClientFixture.SetTraceOutputLevel(Microsoft.Extensions.Logging.LogLevel.Trace);

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);

            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionsPass2, out var errorsPass2);
            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass2.Count);            

            List<String> memoryLogPass2 = memoryWriter.getEntries();

            VerifyExpectedResults(memoryLogPass2, pass2ExpectedResults);

            if (memoryLogPass2.Count > 0)
            {
                TestContext.WriteLine("*** begin: output from pass2 memory log ***");
                foreach (String s in memoryLogPass2)
                {
                    TestContext.Out.WriteLine(s);
                }
                TestContext.WriteLine("*** end: output from pass2 memory log ***");
            }
            else
            {
                TestContext.WriteLine("*** memory log from pass2 is empty ***");
            }

            memoryWriter.Close(); memoryWriter.Dispose();

            base.ClientFixture.SetTraceOutput(TestContext.Out);
            // reset the log level
            base.ClientFixture.SetTraceOutputLevel();



            // finally browse again with a simple browse service call.
            theSession.Browse(null, null, nodeIds, 0, BrowseDirection.Forward,
                ReferenceTypeIds.Organizes, true, 0,
                out var continuationPoints2ndBrowse,
                out var referenceDescriptions2ndBrowse,
                out var errors2ndBrowse );


            Random random = new Random();
            int index = 0;
            foreach (var referenceDescription in referenceDescriptionsPass1)
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

        private void VerifyExpectedResults(List<string> memoryLogPass,
            ManagedBrowseExpectedResultValues expectedResults)
        {
            List<string> messagesWithBadNoCPSC = memoryLogPass.Where(x => x.Contains("BadNoContinuationPoints")).ToList();

            Assert.IsTrue (messagesWithBadNoCPSC.Count == expectedResults.ExpectedNumberOfBadNoCPSCs.Count);

            int pass = 0;            
            foreach (String s in messagesWithBadNoCPSC)
            {
                // get the part of the error message after the time stamp:
                string msg = s.Substring(s.IndexOf("ManagedBrowse"));
                // create error message from expected results
                String expectedString =  String.Format(
                    "ManagedBrowse: in pass {0}, {1} {2} occured with a status code {3}",
                    pass,
                    expectedResults.ExpectedNumberOfBadNoCPSCs[pass],
                    expectedResults.ExpectedNumberOfBadNoCPSCs[pass] == 1 ? "error" :
                    "errors", "BadNoContinuationPoints.");
                Assert.IsTrue(msg.Equals(expectedString));
                pass++;
            }
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
