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
using NUnit.Framework.Internal;
using Opc.Ua.Server.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    public class ManagedBrowseExpectedResultValues
    {
        public uint InputMaxNumberOfContinuationPoints { get; set; }
        public uint InputMaxNumberOfReferencesPerNode { get; set; }
        public int ExpectedNumberOfPasses { get; set; }
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }
    }

    public class ManagedBrowseTestDataProvider : IFormattable
    {
        public uint MaxNumberOfContinuationPoints { get; set; }
        public uint MaxNumberOfReferencesPerNode { get; set; }
        public int ExpectedNumberOfPasses { get; set; }
        public List<int> ExpectedNumberOfBadNoCPSCs { get; set; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"{MaxNumberOfContinuationPoints}:{MaxNumberOfReferencesPerNode}";
        }
    }

    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedBrowseWithBrowseNext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(CPFixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ContinuationPointInBatchTest : ClientTestFramework
    {
        public static readonly object[] CPFixtureArgs = [new object[] { Utils.UriSchemeOpcTcp }];

        [DatapointSource]
        public IEnumerable<ManagedBrowseTestDataProvider> ManagedBrowseTestDataValues()
        {
            yield return new ManagedBrowseTestDataProvider
            {
                MaxNumberOfContinuationPoints = 2,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 5,
                ExpectedNumberOfBadNoCPSCs = [15, 9, 5, 3, 1]
            };
            yield return new ManagedBrowseTestDataProvider
            {
                MaxNumberOfContinuationPoints = 4,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 2,
                ExpectedNumberOfBadNoCPSCs = [5, 1]
            };
            yield return new ManagedBrowseTestDataProvider
            {
                MaxNumberOfContinuationPoints = 20,
                MaxNumberOfReferencesPerNode = 50,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = []
            };
            yield return new ManagedBrowseTestDataProvider
            {
                MaxNumberOfContinuationPoints = 5,
                MaxNumberOfReferencesPerNode = 10,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = []
            };
        }

        public ReferenceServerWithLimits ReferenceServerWithLimits { get; set; }
        public ServerFixture<ReferenceServerWithLimits> ServerFixtureWithLimits { get; set; }

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
            // start Ref server
            ServerFixtureWithLimits = new ServerFixture<ReferenceServerWithLimits>(
                enableTracing,
                disableActivityLogging)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            await ServerFixtureWithLimits.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixtureWithLimits.Config.TransportQuotas.MaxMessageSize
                = TransportQuotaMaxMessageSize;
            ServerFixtureWithLimits.Config.TransportQuotas.MaxByteStringLength
                = ServerFixtureWithLimits
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;

            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName));
            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixtureWithLimits.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                });

            ServerFixtureWithLimits.Config.ServerConfiguration.MaxBrowseContinuationPoints = 2;
            ServerFixtureWithLimits.Config.ServerConfiguration.OperationLimits.MaxNodesPerBrowse
                = 5;

            ReferenceServerWithLimits = await ServerFixtureWithLimits.StartAsync()
                .ConfigureAwait(false);
            ReferenceServerWithLimits.TokenValidator = TokenValidator;
            ReferenceServer = ReferenceServerWithLimits;
            ServerFixturePort = ServerFixtureWithLimits.Port;
        }

        public ContinuationPointInBatchTest(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        public ContinuationPointInBatchTest()
        {
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            // create a new session for every test
            SingleSession = false;
            return OneTimeSetUpCoreAsync(false, false, false, true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override async Task SetUpAsync()
        {
            await base.SetUpAsync().ConfigureAwait(false);
            Session.NodeCache.Clear();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }

        /// <summary>
        /// <para>
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// </para>
        /// <para>Browse all variables in the objects folder.</para>
        /// </summary>
        [Theory]
        [Order(100)]
        public async Task MBNodeCacheBrowseAllVariablesAsync(ManagedBrowseTestDataProvider testData)
        {
            ISession theSession = Session;
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            var pass1ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection { ObjectIds.ObjectsFolder };

            await Session.FetchTypeTreeAsync(ReferenceTypeIds.References).ConfigureAwait(false);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (ExpandedNodeId node in nodesToBrowse)
                {
                    try
                    {
                        IList<INode> organizers = await Session.NodeCache.FindReferencesAsync(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true).ConfigureAwait(false);
                        nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                        IEnumerable<INode> objectNodes = organizers.Where(n => n is ObjectNode);
                        IEnumerable<INode> variableNodes = organizers.Where(n => n is VariableNode);
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
                nodesToBrowse = [.. nextNodesToBrowse.Distinct()];
                TestContext.Out.WriteLine(
                    "Found {0} duplicates",
                    nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        /// <summary>
        /// <para>
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
        /// </para>
        /// <para>
        /// In all cases, the browse will succeed in the end, and the test verifies that
        /// the number of returned nodes from the ManagedBrowse method is correct and also that the reuslts
        /// are returned in the correct sequence. This is done by comparing the results with those
        /// from a plain browse service call with no limit on the max number of browse continuation
        /// points.
        /// </para>
        /// <para>
        /// No return value should have the status code BadContinuationPointInvalid, since there is
        /// no attempt to allocate continuation points in parallel from more than one service call.
        /// </para>
        /// </summary>
        [Theory]
        [Order(200)]
        public async Task ManagedBrowseWithManyContinuationPointsAsync(
            ManagedBrowseTestDataProvider testData)
        {
            var theSession = (Session)Session;
            await theSession.FetchOperationLimitsAsync().ConfigureAwait(false);

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            var pass1ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            var pass2ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = []
            };

            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass1ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = GetMassFolderNodesToBrowse();

            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass1;
            // browse with test settings
            (referenceDescriptionCollectionsPass1, _) = await theSession.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0).ConfigureAwait(false);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionsPass1.Count);

            // now reset the server qutas to get a browse scenario without continuation points. This allows
            // to verify the result from the first browse service call (with quotas in place).
            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass2ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            IList<ReferenceDescriptionCollection> referenceDescriptionsPass2;
            (referenceDescriptionsPass2, _) = await theSession.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0).ConfigureAwait(false);
            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass2.Count);

            // finally browse again with a simple browse service call.
            IList<ReferenceDescriptionCollection> referenceDescriptionCollections2ndBrowse;

            (_, _, referenceDescriptionCollections2ndBrowse, _) =
                await theSession.BrowseAsync(
                    null,
                    null,
                    nodeIds,
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.Organizes,
                    true,
                    0).ConfigureAwait(false);

            var random = new Random();
            int index = 0;
            foreach (
                ReferenceDescriptionCollection referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                NUnit.Framework.Assert.That(
                    referenceDescriptionCollection.Count,
                    Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the type of the nodes are the same, once for each list of reference descriptions
                string randomNodeName = referenceDescriptionCollection[
                    random.Next(0, referenceDescriptionCollection.Count - 1)
                ]
                    .DisplayName
                    .Text;
                string suffix = GetSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(
                    referenceDescriptionCollection.Count,
                    referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(
                    referenceDescriptionCollection[ii].NodeId,
                    referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }
        }

        /// <summary>
        /// <para>
        /// For each entry in the datapoint source, the test browses some folders in the reference
        /// server which have 100 subnodes each (see method getMassFolderNodesToBrowse())
        /// </para>
        /// <para>The server is configured with a certain number of allowed nodes per browse service call</para>
        /// <para>
        /// The ManagedBrowse method is called with the ContinuationPointPolicy 'Balanced'
        /// which forces the method to create packages which have at most
        /// min(maxBrowseContinuationPoints, maxNodesPerBrowse)
        /// nodes.
        /// </para>
        /// <para>The following results are expected and verified:</para>
        /// <para>
        /// In all cases, the browse will succeed without a status code BadNoContinuationPoints
        /// The test also verifies that the number of returned nodes from the ManagedBrowse method
        /// is correct and also that the reuslts are returned in the correct sequence.
        /// This is done by comparing the results with those
        /// from a plain browse service call with no limit on the max number of browse continuation
        /// points.
        /// </para>
        /// <para>
        /// No return value should have the status code BadContinuationPointInvalid, since there is
        /// no attempt to allocate continuation points in parallel from more than one service call.
        /// </para>
        /// </summary>
        [Theory]
        [Order(210)]
        public async Task BalancedManagedBrowseWithManyContinuationPointsAsync(
            ManagedBrowseTestDataProvider testData)
        {
            var theSession = (Session)Session;

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Balanced;

            var pass1ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            var pass2ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = []
            };

            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass1ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = GetMassFolderNodesToBrowse();

            // browse with test settings
            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass1;
            (referenceDescriptionCollectionsPass1, _) = await theSession.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0).ConfigureAwait(false);

            Assert.AreEqual(nodeIds.Count, referenceDescriptionCollectionsPass1.Count);

            // now reset the server qutas to get a browse scenario without continuation points. This allows
            // to verify the result from the first browse service call (with quotas in place).
            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass2ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            theSession.ContinuationPointPolicy = ContinuationPointPolicy.Balanced;

            IList<ReferenceDescriptionCollection> referenceDescriptionsPass2;
            (referenceDescriptionsPass2, _) = await theSession.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0).ConfigureAwait(false);
            Assert.AreEqual(nodeIds.Count, referenceDescriptionsPass2.Count);

            IList<ReferenceDescriptionCollection> referenceDescriptionCollections2ndBrowse;
            // finally browse again with a simple browse service call.
            (_, _, referenceDescriptionCollections2ndBrowse, _) = await theSession.BrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Forward,
                ReferenceTypeIds.Organizes,
                true,
                0).ConfigureAwait(false);

            var random = new Random();
            int index = 0;
            foreach (
                ReferenceDescriptionCollection referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                NUnit.Framework.Assert.That(
                    referenceDescriptionCollection.Count,
                    Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the type of the nodes are the same, once for each list of reference descriptions
                string randomNodeName = referenceDescriptionCollection[
                    random.Next(0, referenceDescriptionCollection.Count - 1)
                ]
                    .DisplayName
                    .Text;
                string suffix = GetSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(
                    referenceDescriptionCollection.Count,
                    referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(
                    referenceDescriptionCollection[ii].NodeId,
                    referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }
        }

        /// <summary>
        /// <para>
        /// in this test the service result BadContinuationPoint invalid in (an unpredictable subset)
        /// of the return values from the ManagedBrowse method call is enforced, by
        /// concurrently executing the method on two sets of nodes both of which
        /// require the allocation of BrowseContinuationPoints in the server
        /// </para>
        /// <para>
        /// The following results are expected:
        /// on a system which supports parallel execution of threads, at least one of the parallel calls
        /// (usually all of them) to method ManagedBrowse will produce results with status code
        /// BadContinuationPointInvalid
        /// </para>
        /// <para>
        /// In the worst case the two calls to ManagedBrowse could end up in an endless loop (to prevent this
        /// an upper bound for the number of rebrowse attempts would be needed, or a session wide management
        /// of the continuation points the server must potentially allocate for the service calls
        /// from the client).
        /// </para>
        /// <para>
        /// The result with regards to the BadNoContinuationPoint should be similar to the one from
        /// the ManagedBrowseWithManyContinuationPoints test case
        /// </para>
        /// </summary>
        [Theory]
        [Order(300)]
        public async Task ParallelManagedBrowseWithManyContinuationPointsAsync(
            ManagedBrowseTestDataProvider testData,
            ContinuationPointPolicy policy)
        {
            var theSession = (Session)Session;

            theSession.ContinuationPointPolicy = policy;

            var pass1ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            var pass2ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = 0,
                InputMaxNumberOfReferencesPerNode = 1000,
                ExpectedNumberOfPasses = 1,
                ExpectedNumberOfBadNoCPSCs = []
            };

            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass1ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            List<NodeId> nodeIds = GetMassFolderNodesToBrowse();
            List<NodeId> nodeIds1 = nodeIds.GetRange(0, nodeIds.Count / 2);
            var nodeIds2 = nodeIds.Skip(nodeIds.Count / 2).ToList();

            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass1 = [];
            IList<ReferenceDescriptionCollection> referenceDescriptionCollectionsPass2 = [];

            IList<ServiceResult> errorsPass1 = [];
            IList<ServiceResult> errorsPass2 = [];

            Func<Task>[] tasks = [
                async () =>
                    (referenceDescriptionCollectionsPass1, errorsPass1) = await theSession.ManagedBrowseAsync(
                        null,
                        null,
                        nodeIds1,
                        0,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.Organizes,
                        true,
                        0).ConfigureAwait(false),
                async () =>
                    (referenceDescriptionCollectionsPass2, errorsPass2) = await theSession.ManagedBrowseAsync(
                        null,
                        null,
                        nodeIds2,
                        0,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.Organizes,
                        true,
                        0).ConfigureAwait(false)
            ];

            await Task.WhenAll([.. tasks.Select(t => t.Invoke())]).ConfigureAwait(false);

            Assert.AreEqual(nodeIds1.Count, referenceDescriptionCollectionsPass1.Count);
            Assert.AreEqual(nodeIds2.Count, referenceDescriptionCollectionsPass2.Count);

            ((List<ReferenceDescriptionCollection>)referenceDescriptionCollectionsPass1).AddRange(
                referenceDescriptionCollectionsPass2);
            ((List<ServiceResult>)errorsPass1).AddRange(errorsPass2);

            // finally browse again with a simple browse service call.
            // reset server quotas first:

            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass2ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass2ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass2ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            ByteStringCollection continuationPoints2ndBrowse;
            IList<ReferenceDescriptionCollection> referenceDescriptionCollections2ndBrowse;
            IList<ServiceResult> errors2ndBrowse;
            (_, continuationPoints2ndBrowse, referenceDescriptionCollections2ndBrowse, errors2ndBrowse) =
                await theSession.BrowseAsync(
                    null,
                    null,
                    nodeIds,
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.Organizes,
                    true,
                    0).ConfigureAwait(false);

            var random = new Random();
            int index = 0;
            foreach (
                ReferenceDescriptionCollection referenceDescriptionCollection in referenceDescriptionCollectionsPass1)
            {
                NUnit.Framework.Assert.That(
                    referenceDescriptionCollection.Count,
                    Is.EqualTo(referenceDescriptionCollections2ndBrowse[index].Count));

                // now verify that the types of the nodes are the same, once for each list of reference descriptions
                string randomNodeName = referenceDescriptionCollection[
                    random.Next(0, referenceDescriptionCollection.Count - 1)
                ]
                    .DisplayName
                    .Text;
                string suffix = GetSuffixesForMassFolders()[index];
                Assert.IsTrue(randomNodeName.StartsWith(suffix));

                int ii = random.Next(0, referenceDescriptionCollection.Count - 1);

                Assert.AreEqual(
                    referenceDescriptionCollection.Count,
                    referenceDescriptionCollections2ndBrowse[index].Count);
                Assert.AreEqual(
                    referenceDescriptionCollection[ii].NodeId,
                    referenceDescriptionCollections2ndBrowse[index][ii].NodeId);

                index++;
            }
        }

        /// <summary>
        /// This test is taken from the node cache unit test.
        /// Instead of the original test, there are now restrctions
        /// on the maximum number of continuation points supported
        /// by the server, and the maximum number of nodes allowed
        /// in a browse service call.
        /// Browse all variables in the objects folder.
        /// </summary>
        [Theory]
        [Order(400)]
        public async Task MBNodeCacheBrowseAllVariablesMultipleNodesAsync(
            ManagedBrowseTestDataProvider testData,
            ContinuationPointPolicy policy)
        {
            var theSession = (Session)Session;
            theSession.NodeCache.Clear();

            theSession.ContinuationPointPolicy = policy;

            // the ExpectedNumber* parameters are not relevant/correct for this test.
            var pass1ExpectedResults = new ManagedBrowseExpectedResultValues
            {
                InputMaxNumberOfContinuationPoints = testData.MaxNumberOfContinuationPoints,
                InputMaxNumberOfReferencesPerNode = testData.MaxNumberOfReferencesPerNode,
                ExpectedNumberOfPasses = testData.ExpectedNumberOfPasses,
                ExpectedNumberOfBadNoCPSCs = testData.ExpectedNumberOfBadNoCPSCs
            };

            ReferenceServerWithLimits.TestMaxBrowseReferencesPerNode =
                pass1ExpectedResults.InputMaxNumberOfReferencesPerNode;

            ReferenceServerWithLimits.SetMaxNumberOfContinuationPoints(
                pass1ExpectedResults.InputMaxNumberOfContinuationPoints);
            theSession.ServerMaxContinuationPointsPerBrowse = pass1ExpectedResults
                .InputMaxNumberOfContinuationPoints;

            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection { ObjectIds.ObjectsFolder };

            await Session
                .FetchTypeTreeAsync(ReferenceTypeIds.References, new CancellationToken())
                .ConfigureAwait(false);

            var referenceTypeIds = new NodeIdCollection { ReferenceTypeIds.HierarchicalReferences };
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                try
                {
                    IList<INode> organizers = await Session
                        .NodeCache.FindReferencesAsync(
                            nodesToBrowse,
                            referenceTypeIds,
                            false,
                            true,
                            new CancellationToken())
                        .ConfigureAwait(false);
                    nextNodesToBrowse.AddRange(organizers.Select(n => n.NodeId));
                    IEnumerable<INode> objectNodes = organizers.Where(n => n is ObjectNode);
                    IEnumerable<INode> variableNodes = organizers.Where(n => n is VariableNode);
                    result.AddRange(variableNodes);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        TestContext.Out.WriteLine("Access denied: Skipped node.");
                    }
                }
                nodesToBrowse = [.. nextNodesToBrowse.Distinct()];
                TestContext.Out.WriteLine(
                    "Found {0} duplicates",
                    nextNodesToBrowse.Count - nodesToBrowse.Count);
            }

            TestContext.Out.WriteLine("Found {0} variables", result.Count);
        }

        private List<NodeId> GetMassFolderNodesToBrowse()
        {
            const string massFolderPrefix = "Scalar_Simulation_Mass_";

            var nodesToBrowse = new List<string>();
            foreach (string suffix in GetSuffixesForMassFolders())
            {
                nodesToBrowse.Add(massFolderPrefix + suffix);
            }

            int nsi = Session.NamespaceUris
                .GetIndex("http://opcfoundation.org/Quickstarts/ReferenceServer");
            var result = new List<NodeId>();
            foreach (string nodeString in nodesToBrowse)
            {
                result.Add(new NodeId(nodeString, (ushort)nsi));
            }
            return result;
        }

        private static List<string> GetSuffixesForMassFolders()
        {
            return
            [
                "Boolean",
                "Byte",
                "ByteString",
                "DateTime",
                "Double",
                "Duration",
                "Float",
                "Guid",
                "Int16",
                "Int32",
                "Int64",
                "Integer",
                "LocaleId",
                "LocalizedText",
                "NodeId",
                "Number",
                "QualifiedName",
                "SByte",
                "String",
                "UInt16",
                "UInt32",
                "UInt64",
                "UInteger",
                "UtcTime",
                "Variant",
                "XmlElement"
            ];
        }
    }
}
