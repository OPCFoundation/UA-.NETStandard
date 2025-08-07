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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
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
            // start Ref server
            ServerFixture = new ServerFixture<ReferenceServer>(enableTracing, disableActivityLogging) {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };
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

        [Test, Order(210)]
        public async Task TestBoundaryCaseForReadingChunksAsync()
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

            var result = await theSession.WriteAsync(null, writeValues, default).ConfigureAwait(false);
            StatusCodeCollection results = result.Results;
            DiagnosticInfoCollection diagnosticInfos = result.DiagnosticInfos;
            if (results[0] != StatusCodes.Good)
            {
                Assert.Fail($"Write failed with status code {results[0]}");
            }

            byte[] readData = await theSession.ReadByteStringInChunksAsync(NodeId, default).ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(chunk, readData));
        }
        #endregion // Test Methods
    }
}
