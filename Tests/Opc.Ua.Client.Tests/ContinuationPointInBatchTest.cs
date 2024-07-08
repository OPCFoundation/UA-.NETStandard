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
    public class ManagedBrowseExpectedResultValues
    {
        public uint InputMaxNumberOfContinuationPoints { get; set; } = 0;
        public uint InputMaxNumberOfReferencesPerNode { get; set; } = 0;
        public int ExpectedNumberOfPasses { get; set; } = 0;
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }

    }

    public class ManagedBrowsTestDataProvider
    {
        public uint MaxNumberOfContinuationPoints { get; set; } = 0;
        public uint MaxNumberOfReferencesPerNode { get; set; } = 0;
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

        [DatapointSource]
        public IEnumerable<ManagedBrowsTestDataProvider> ManagedBrowseTestDataValues()
        {
            yield return new ManagedBrowsTestDataProvider {
                MaxNumberOfContinuationPoints = 2,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 5,
                ExpectedNumberOfBadNoCPSCs = new List<int> { 15, 9, 5, 3, 1 }
            };
            yield return new ManagedBrowsTestDataProvider {
                MaxNumberOfContinuationPoints = 7,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 5,
                ExpectedNumberOfBadNoCPSCs = new List<int> { 15, 9, 5, 3, 1 }
            };
        }
        [Theory, Order(300)]
        public void ManagedBrowseWithManyContinuationPoints(ManagedBrowsTestDataProvider testData)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));

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

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode; 

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();            

            // browse with test settings
            theSession.ManagedBrowse(
                null, null, nodeIds, 0, BrowseDirection.Forward, ReferenceTypeIds.Organizes, true, 0,
                out var referenceDescriptionCollectionsPass1, out var errorsPass1);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionsPass1.Count);

            List<String> memoryLogPass1 = memoryWriter.getEntries();
            WriteMemoryLogToTextOut(memoryLogPass1, "memoryLogPass1");
            VerifyExpectedResults(memoryLogPass1, pass1ExpectedResults);

            memoryWriter.Close(); memoryWriter.Dispose();

            // reset memory log
            memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);


            //set log level to ensure we get all messages
            base.ClientFixture.SetTraceOutputLevel(Microsoft.Extensions.Logging.LogLevel.Trace);


            // now reset the server qutas to get a browse scenario without continuation points. This allows
            // to verify the result from the first browse service call (with quotas in place).
            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);

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
                out var errors2ndBrowse );


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


            int dummy = 0;
            dummy++;
        }


        [Theory, Order(400)]
        public void ParallelManagedBrowseWithManyContinuationPoints(ManagedBrowsTestDataProvider testData)
        {
            CPBatchTestMemoryWriter memoryWriter = new CPBatchTestMemoryWriter();
            base.ClientFixture.SetTraceOutput(memoryWriter);
            Session theSession = ((Session)(((TraceableSession)Session).Session));

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

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);

            List<NodeId> nodeIds = getMassFolderNodesToBrowse();
            List<NodeId> nodeIds1 = nodeIds.GetRange(0, nodeIds.Count / 2);
            List<NodeId> nodeIds2 = nodeIds.Skip(nodeIds.Count / 2).ToList();

            List<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass1 = new List<ReferenceDescriptionCollection>();
            List<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass2 = new List<ReferenceDescriptionCollection>();

            List<ServiceResult> errorsPass1 = new List<ServiceResult>();
            List<ServiceResult> errorsPass2 = new List<ServiceResult>();

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

            referenceDescriptionCollectionsPass1.AddRange(referenceDescriptionCollectionsPass2);
            errorsPass1.AddRange(errorsPass2);

            // finally browse again with a simple browse service call.
            // reset server quotas first:

            ReferenceServerForThisUnitTest.Test_MaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerForThisUnitTest.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);

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


            int dummy = 0;
            dummy++;

        }


            private void WriteMemoryLogToTextOut(List<String> memoryLog, string contextInfo)
        {           
            if (memoryLog.Count > 0)
            {
                TestContext.WriteLine($"<!-- begin: output from memory log from context {contextInfo} -->");
                foreach (String s in memoryLog)
                {
                    TestContext.Out.WriteLine(s);
                }
                TestContext.WriteLine($"<-- end: output from memory log from context {contextInfo} -->");
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
