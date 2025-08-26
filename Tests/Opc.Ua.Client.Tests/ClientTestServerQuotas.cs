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
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    public class ClientTestServerQuotas : ClientTestFramework
    {
        internal const int MaxByteStringLengthForTest = 4096;

        public ClientTestServerQuotas()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        public ClientTestServerQuotas(string uriScheme = Utils.UriSchemeOpcTcp)
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
            bool securityNone,
            TextWriter writer)
        {
            // start Ref server
            ServerFixture = new ServerFixture<ReferenceServer>(
                enableTracing,
                disableActivityLogging)
            {
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

            ReferenceServer = await ServerFixture.StartAsync(writer ?? TestContext.Out)
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
        [Order(200)]
        public void TestBoundaryCaseForReadingChunks()
        {
            var theSession = (Session)((TraceableSession)Session).Session;

            int namespaceIndex = theSession.NamespaceUris.GetIndex(
                "http://opcfoundation.org/Quickstarts/ReferenceServer");
            var nodeId = new NodeId($"ns={namespaceIndex};s=Scalar_Static_ByteString");

            var random = new Random();

            byte[] chunk = new byte[MaxByteStringLengthForTest];
            random.NextBytes(chunk);

            var writeValue = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue { WrappedValue = new Variant(chunk) },
                IndexRange = null
            };
            var writeValues = new WriteValueCollection { writeValue };

            theSession.Write(null, writeValues, out StatusCodeCollection results, out _);

            if (results[0] != StatusCodes.Good)
            {
                NUnit.Framework.Assert.Fail($"Write failed with status code {results[0]}");
            }

            byte[] readData = theSession.ReadByteStringInChunks(nodeId);

            Assert.IsTrue(Utils.IsEqual(chunk, readData));
        }

        [Test]
        [Order(210)]
        public async Task TestBoundaryCaseForReadingChunksAsync()
        {
            var theSession = (Session)((TraceableSession)Session).Session;

            int namespaceIndex = theSession.NamespaceUris.GetIndex(
                "http://opcfoundation.org/Quickstarts/ReferenceServer");
            var nodeId = new NodeId($"ns={namespaceIndex};s=Scalar_Static_ByteString");

            var random = new Random();

            byte[] chunk = new byte[MaxByteStringLengthForTest];
            random.NextBytes(chunk);

            var writeValue = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue { WrappedValue = new Variant(chunk) },
                IndexRange = null
            };
            var writeValues = new WriteValueCollection { writeValue };

            WriteResponse result = await theSession.WriteAsync(null, writeValues, default)
                .ConfigureAwait(false);
            StatusCodeCollection results = result.Results;

            _ = result.DiagnosticInfos;
            if (results[0] != StatusCodes.Good)
            {
                NUnit.Framework.Assert.Fail($"Write failed with status code {results[0]}");
            }

            byte[] readData = await theSession.ReadByteStringInChunksAsync(nodeId, default)
                .ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(chunk, readData));
        }
    }
}
