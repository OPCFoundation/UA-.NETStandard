/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.TestFramework
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
            bool securityNone)
        {
            // start Ref server
            ServerFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t),
                enableTracing,
                disableActivityLogging)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas!.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = MaxByteStringLengthForTest;
            ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                };

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
        public async Task TestBoundaryCaseForReadingChunksAsync()
        {
            ISession theSession = Session;

            int namespaceIndex = theSession.NamespaceUris.GetIndex(
                "http://opcfoundation.org/Quickstarts/ReferenceServer");
            var nodeId = NodeId.Parse($"ns={namespaceIndex};s=Scalar_Static_ByteString");

            byte[] buffer = new byte[MaxByteStringLengthForTest];
            UnsecureRandom.Shared.NextBytes(buffer);
            var chunk = ByteString.From(buffer);

            var writeValue = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(chunk)),
                IndexRange = null
            };
            ArrayOf<WriteValue> writeValues = [writeValue];

            WriteResponse result = await theSession.WriteAsync(null, writeValues, default)
                .ConfigureAwait(false);
            ArrayOf<StatusCode> results = result.Results;

            if (results[0] != StatusCodes.Good)
            {
                Assert.Fail($"Write failed with status code {results[0]}");
            }

            ByteString readData = await theSession.ReadByteStringInChunksAsync(nodeId, default)
                .ConfigureAwait(false);
            Assert.That(readData, Is.EqualTo(chunk));
        }
    }
}
