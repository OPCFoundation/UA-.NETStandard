// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that the Read service of a running server answers with a response whose
    /// values come from the pooled read buffer, and that they survive the encoding the
    /// transport does before it returns the buffer.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ReadResultPoolingServerTests
    {
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = true,
                DurableSubscriptionsEnabled = false,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            m_requestHeader.TimeoutHint = 10000;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The values of a pooled Read response, including an operation error with its
        /// diagnostics, are encoded intact, and reusing the response returns its buffer
        /// so that the next Read gets correct values again.
        /// </summary>
        [Test]
        public async Task PooledReadResponseSurvivesEncodingAndReuseAsync()
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = new NodeId("NoSuchNode", 0), AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints, AttributeId = Attributes.Value }
            ];

            for (int round = 0; round < 3; round++)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                m_requestHeader.ReturnDiagnostics = (uint)DiagnosticsMasks.OperationSymbolicId;
                ReadResponse response = await m_server.ReadAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    RequestLifetime.None).ConfigureAwait(false);

                Assert.That(response, Is.TypeOf<PooledReadResponse>(), $"round {round}");
                byte[] encoded = Encode(response);
                ((IPooledEncodeable)response).Reuse();
                Assert.That(response.Results.Count, Is.Zero, "a reused response shows no results");

                ReadResponse decoded = Decode(encoded);
                Assert.Multiple(() =>
                {
                    Assert.That(decoded.Results.Count, Is.EqualTo(4), $"round {round}");
                    Assert.That(decoded.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(decoded.Results[0].WrappedValue.TryGetValue(out int state), Is.True);
                    Assert.That((ServerState)state, Is.EqualTo(ServerState.Running));
                    Assert.That(decoded.Results[1].WrappedValue.TryGetValue(out QualifiedName browseName), Is.True);
                    Assert.That(browseName.Name, Is.EqualTo(BrowseNames.State));
                    Assert.That(decoded.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(decoded.Results[3].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(decoded.DiagnosticInfos.Count, Is.EqualTo(4));
                    Assert.That(decoded.Results[0].ServerTimestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
                });
            }
        }

        private byte[] Encode(ReadResponse response)
        {
            using var encoder = new BinaryEncoder(m_server.CurrentInstance.MessageContext);
            response.Encode(encoder);
            return encoder.CloseAndReturnBuffer()!;
        }

        private ReadResponse Decode(byte[] encoded)
        {
            using var decoder = new BinaryDecoder(encoded, m_server.CurrentInstance.MessageContext);
            var decoded = new ReadResponse();
            decoded.Decode(decoder);
            return decoded;
        }
    }
}
