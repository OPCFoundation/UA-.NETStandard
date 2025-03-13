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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    public class ClientTestServerQuotas : ClientTestFramework
    {
        const int MaxByteStringLengthForTest = 4096;
        public ClientTestServerQuotas() : base(Utils.UriSchemeOpcTcp)
        {
        }

        public ClientTestServerQuotas(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
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
        public new Task SetUp()
        {
            return base.SetUp();
        }

        public override async Task CreateReferenceServerFixture(bool enableTracing, bool disableActivityLogging, bool securityNone, TextWriter writer)
        {
            {
                IServiceCollection services = new ServiceCollection()
                    .AddConfigurationServices()
                    .AddServerServices()
                    .AddScoped<IReferenceServer, ReferenceServer>();

                IServiceProvider serviceProvider = services.BuildServiceProvider();

                // start Ref server
                ServerFixture = new ServerFixture<IReferenceServer>(
                    serviceProvider.GetRequiredService<IReferenceServer>(),
                    serviceProvider.GetRequiredService<IApplicationInstance>(),
                    enableTracing,
                    disableActivityLogging)
                {
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

            await ServerFixture.LoadConfiguration(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = MaxByteStringLengthForTest;
            ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Opc.Ua.Profiles.JwtUserToken });

            ReferenceServer = await ServerFixture.StartAsync(writer ?? TestContext.Out).ConfigureAwait(false);
            ReferenceServer.TokenValidator = this.TokenValidator;
            ServerFixturePort = ServerFixture.Port;
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

        #region Test Methods

        [Test, Order(100)]        
        public void ReadDictionaryByteString()
        {
            List<NodeId> dictionaryIds = new List<NodeId>();
            dictionaryIds.Add(VariableIds.OpcUa_BinarySchema);
            dictionaryIds.Add(GetTestDataDictionaryNodeId());

            Session theSession = ((Session)(((TraceableSession)Session).Session));

            foreach (NodeId dataDictionaryId in dictionaryIds)
            {
                ReferenceDescription referenceDescription = new ReferenceDescription();

                referenceDescription.NodeId = NodeId.ToExpandedNodeId(dataDictionaryId, theSession.NodeCache.NamespaceUris);

                // make sure the dictionary is too large to fit in a single message
                ReadValueId readValueId = new ReadValueId {
                    NodeId = dataDictionaryId,
                    AttributeId = Attributes.Value,
                    IndexRange = null,
                    DataEncoding = null
                };

                ReadValueIdCollection nodesToRead = new ReadValueIdCollection {
                readValueId
            };

                var x = Assert.Throws<ServiceResultException>(() => {
                    theSession.Read(null, 0, TimestampsToReturn.Neither, nodesToRead, out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos);
                });

                Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, x.StatusCode);

                // now ensure we get the dictionary in chunks
                DataDictionary dictionary = theSession.LoadDataDictionary(referenceDescription);
                Assert.IsNotNull(dictionary);

                // Sanity checks: verify that some well-known information is present
                Assert.AreEqual(dictionary.TypeSystemName, "OPC Binary");

                if (dataDictionaryId == dictionaryIds[0])
                {
                    Assert.IsTrue(dictionary.DataTypes.Count > 160);
                    Assert.IsTrue(dictionary.DataTypes.ContainsKey(VariableIds.OpcUa_BinarySchema_Union));
                    Assert.IsTrue(dictionary.DataTypes.ContainsKey(VariableIds.OpcUa_BinarySchema_OptionSet));
                    Assert.AreEqual("http://opcfoundation.org/UA/", dictionary.TypeDictionary.TargetNamespace);
                }
                else if (dataDictionaryId == dictionaryIds[1])
                {
                    Assert.IsTrue(dictionary.DataTypes.Count >= 10);
                    Assert.AreEqual("http://test.org/UA/Data/", dictionary.TypeDictionary.TargetNamespace);
                }
            }
        }


        [Test, Order(200)]
        public void TestBoundaryCaseForReadingChunks()
        {

            Session theSession = ((Session)(((TraceableSession)Session).Session));

            int NamespaceIndex = theSession.NamespaceUris.GetIndex("http://opcfoundation.org/Quickstarts/ReferenceServer");
            NodeId NodeId = new NodeId($"ns={NamespaceIndex};s=Scalar_Static_ByteString");

            Random random = new Random();

            byte[] chunk = new byte[MaxByteStringLengthForTest];
            random.NextBytes(chunk);

            WriteValue WriteValue = new WriteValue {
                NodeId = NodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue() { WrappedValue = new Variant(chunk) },
                IndexRange = null
            };
            WriteValueCollection writeValues = new WriteValueCollection {
                    WriteValue
                };
            theSession.Write(null, writeValues, out StatusCodeCollection results, out DiagnosticInfoCollection diagnosticInfos);

            if (results[0] != StatusCodes.Good)
            {
                Assert.Fail($"Write failed with status code {results[0]}");
            }

            byte[] readData = theSession.ReadByteStringInChunks(NodeId);

            Assert.IsTrue(Utils.IsEqual(chunk, readData));
        }
        #endregion // Test Methods
        #region // helper methods

        /// <summary>
        /// retrieve the node id of the test data dictionary without relying on
        /// hard coded identifiers
        /// </summary>
        /// <returns></returns>
        public NodeId GetTestDataDictionaryNodeId()
        {
            BrowseDescription browseDescription = new BrowseDescription() {
                NodeId = ObjectIds.OPCBinarySchema_TypeSystem,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = (uint) BrowseResultMask.All
            };
            BrowseDescriptionCollection browseDescriptions = new BrowseDescriptionCollection() { browseDescription };

            Session.Browse(null, null, 0, browseDescriptions, out BrowseResultCollection results, out DiagnosticInfoCollection diagnosticInfos);

            if (results[0] == null || results[0].StatusCode != StatusCodes.Good)
            {
                throw new Exception("cannot read the id of the test dictionary");
            }
            ReferenceDescription referenceDescription = results[0].References.FirstOrDefault(a => a.BrowseName.Name == "TestData");
            NodeId result = ExpandedNodeId.ToNodeId(referenceDescription.NodeId,Session.NamespaceUris);
            return result;


        }
        #endregion // helper methods

    }
}
