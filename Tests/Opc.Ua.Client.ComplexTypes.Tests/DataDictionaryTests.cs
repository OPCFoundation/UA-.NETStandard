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
using Opc.Ua.Client.Tests;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Data dictionary load tests
    /// </summary>
    public class DataDictionaryTests : ClientTestFramework
    {
        public const int MaxByteStringLengthForTest = 4096;

        public DataDictionaryTests()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        public DataDictionaryTests(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
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
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
            // start Ref server
            ServerFixture = new ServerFixture<ReferenceServer>(
                enableTracing,
                disableActivityLogging,
                Telemetry)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = MaxByteStringLengthForTest;
            ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies
                .Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                });

            ReferenceServer = await ServerFixture.StartAsync()
                .ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
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

        [Test]
        [Order(100)]
        public async Task ReadDictionaryByteStringAsync()
        {
            var dictionaryIds = new List<NodeId> {
                VariableIds.OpcUa_BinarySchema,
                await GetTestDataDictionaryNodeIdAsync().ConfigureAwait(false) };

            var theSession = (Session)((TraceableSession)Session).Session;

            foreach (NodeId dataDictionaryId in dictionaryIds)
            {
                var referenceDescription = new ReferenceDescription
                {
                    NodeId = NodeId.ToExpandedNodeId(
                        dataDictionaryId,
                        theSession.NodeCache.NamespaceUris)
                };

                // make sure the dictionary is too large to fit in a single message
                var readValueId = new ReadValueId
                {
                    NodeId = dataDictionaryId,
                    AttributeId = Attributes.Value,
                    IndexRange = null,
                    DataEncoding = null
                };

                var nodesToRead = new ReadValueIdCollection { readValueId };

                ServiceResultException x = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(() =>
                        theSession.ReadAsync(
                            null,
                            0,
                            TimestampsToReturn.Neither,
                            nodesToRead,
                            default));

                Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, x.StatusCode);

                // now ensure we get the dictionary in chunks
                DataDictionary dictionary = await LoadDataDictionaryAsync(
                    theSession,
                    referenceDescription).ConfigureAwait(false);
                Assert.IsNotNull(dictionary);

                // Sanity checks: verify that some well-known information is present
                Assert.AreEqual(dictionary.TypeSystemName, "OPC Binary");

                if (dataDictionaryId == dictionaryIds[0])
                {
                    Assert.IsTrue(dictionary.DataTypes.Count > 160);
                    Assert.IsTrue(
                        dictionary.DataTypes.ContainsKey(VariableIds.OpcUa_BinarySchema_Union));
                    Assert.IsTrue(
                        dictionary.DataTypes.ContainsKey(VariableIds.OpcUa_BinarySchema_OptionSet));
                    Assert.AreEqual(
                        "http://opcfoundation.org/UA/",
                        dictionary.TypeDictionary.TargetNamespace);
                }
                else if (dataDictionaryId == dictionaryIds[1])
                {
                    Assert.IsTrue(dictionary.DataTypes.Count >= 10);
                    Assert.AreEqual(
                        "http://test.org/UA/Data/",
                        dictionary.TypeDictionary.TargetNamespace);
                }
            }
        }

        /// <summary>
        /// Load the data dictionary from the server.
        /// </summary>
        public Task<DataDictionary> LoadDataDictionaryAsync(
            ISession session,
            ReferenceDescription dictionaryNode,
            CancellationToken ct = default)
        {
            // check if the dictionary has already been loaded.
            var dictionaryId = ExpandedNodeId.ToNodeId(
                dictionaryNode.NodeId,
                session.NamespaceUris);

            var nodeCacheResolver = new NodeCacheResolver(session, Telemetry);

            // load the dictionary.
            return nodeCacheResolver.LoadDictionaryAsync(dictionaryId, dictionaryNode.ToString(), ct);
        }

        /// <summary>
        /// retrieve the node id of the test data dictionary without relying on
        /// hard coded identifiers
        /// </summary>
        public async Task<NodeId> GetTestDataDictionaryNodeIdAsync(CancellationToken ct = default)
        {
            var browseDescription = new BrowseDescription
            {
                NodeId = ObjectIds.OPCBinarySchema_TypeSystem,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = (uint)BrowseResultMask.All
            };
            var browseDescriptions = new BrowseDescriptionCollection { browseDescription };

            Assert.NotNull(Session, "Client not connected to Server.");
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                browseDescriptions,
                ct).ConfigureAwait(false);

            BrowseResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            if (results[0] == null || results[0].StatusCode != StatusCodes.Good)
            {
                throw new Exception("cannot read the id of the test dictionary");
            }
            ReferenceDescription referenceDescription = results[0]
                .References.FirstOrDefault(a => a.BrowseName.Name == "TestData");
            return ExpandedNodeId.ToNodeId(referenceDescription.NodeId, Session.NamespaceUris);
        }
    }
}
