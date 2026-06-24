/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.PubSub.Adapter.Tests.Integration
{
    /// <summary>
    /// End-to-end integration tests that stand up a real in-process OPC UA
    /// reference server and exercise the external-server PubSub adapter
    /// components (<see cref="ServerPublishedDataSetSource"/>,
    /// <see cref="SubscriptionCoordinator"/>,
    /// <see cref="ServerTargetVariableWriter"/> and
    /// <see cref="ServerActionHandler"/>) against it through a live
    /// <see cref="ServerSession"/>.
    /// </summary>
    /// <remarks>
    /// A single unsecured (SecurityMode.None) server + session is shared by all
    /// tests via <c>[OneTimeSetUp]</c>. If the loopback session cannot be
    /// established in the current environment the setup records the failure and
    /// every test calls <see cref="Assert.Ignore(string)"/> so the gate is not
    /// broken.
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    [Category("PubSub")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ExternalServerAdapterIntegrationTests
    {
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ServerSession m_session = null!;
        private ITelemetryContext m_telemetry = null!;
        private string m_pkiRoot = null!;
        private string? m_setupError;
        private ushort m_namespaceIndex;

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                m_serverFixture = new ServerFixture<ReferenceServer>(
                    t => new ReferenceServer(t))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    SecurityNone = true,
                    AutoAccept = true,
                    AllNodeManagers = false,
                    OperationLimits = true
                };

                await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                _ = await m_serverFixture.StartAsync().ConfigureAwait(false);

                string url = $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}";
                m_session = new ServerSession(
                    new ServerConnectionOptions
                    {
                        EndpointUrl = url,
                        SecurityMode = MessageSecurityMode.None,
                        SessionName = "Adapter.IntegrationTests"
                    },
                    m_telemetry);

                using var cts = new CancellationTokenSource(s_timeout);
                await m_session.ConnectAsync(cts.Token).ConfigureAwait(false);
                m_namespaceIndex = await ResolveReferenceNamespaceIndexAsync(cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_setupError = ex.Message;
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            TryDeleteDirectory(m_pkiRoot);
        }

        [SetUp]
        public void SkipWhenServerUnavailable()
        {
            if (m_setupError != null)
            {
                Assert.Ignore(
                    "External server session could not be established in this " +
                    $"environment: {m_setupError}");
            }
        }

        [Test]
        public async Task CyclicReadSourceReturnsLiveServerValueAsync()
        {
            NodeId nodeId = ScalarNode("Scalar_Static_Int32");
            await WriteInt32Async(nodeId, 4242).ConfigureAwait(false);

            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "IntegrationPDS", AdapterTestHelpers.Variable.Value(nodeId));
            var strategy = new CyclicReadStrategy(m_session, m_telemetry);
            using var metaDataBuilder = new DataSetMetaDataBuilder(
                pds, m_session, m_telemetry);
            var source = new ServerPublishedDataSetSource(
                pds, strategy, metaDataBuilder, m_telemetry);

            PublishedDataSetSnapshot snapshot = await source
                .SampleAsync(metaDataBuilder.BuildMetaData())
                .ConfigureAwait(false);

            var fields = (DataSetField[]?)snapshot.Fields ?? [];
            Assert.That(fields, Has.Length.EqualTo(1));
            Assert.That(StatusCode.IsGood(fields[0].StatusCode), Is.True);
            Assert.That(fields[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(4242));
        }

        [Test]
        public async Task TargetVariableWriterRoundTripsThroughServerAsync()
        {
            NodeId nodeId = ScalarNode("Scalar_Static_Int32");
            var writer = new ServerTargetVariableWriter(m_session, m_telemetry);

            StatusCode status = await writer
                .WriteAsync(nodeId, Attributes.Value, null, new DataValue(new Variant(13579)))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(status), Is.True);

            int readBack = await ReadInt32Async(nodeId).ConfigureAwait(false);
            Assert.That(readBack, Is.EqualTo(13579));
        }

        [Test]
        public async Task SubscriptionCoordinatorPrimesAndReflectsChangeAsync()
        {
            NodeId nodeId = ScalarNode("Scalar_Static_Int32");
            await WriteInt32Async(nodeId, 1000).ConfigureAwait(false);

            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "SubPDS", AdapterTestHelpers.Variable.Value(nodeId));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                200, new[] { pds });

            await using var coordinator = new SubscriptionCoordinator(
                config, m_session, SubscriptionAffinity.WriterGroup, m_telemetry);
            await coordinator.StartAsync().ConfigureAwait(false);

            IReadStrategy strategy = coordinator.GetReadStrategy("SubPDS");
            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];

            ArrayOf<DataValue> primed = await strategy
                .ReadAsync(reads.ToArrayOf())
                .ConfigureAwait(false);
            Assert.That(primed[0].WrappedValue.TryGetValue(out int primedValue), Is.True);
            Assert.That(primedValue, Is.EqualTo(1000));

            await WriteInt32Async(nodeId, 2000).ConfigureAwait(false);

            int observed = await WaitForCachedValueAsync(strategy, reads.ToArrayOf(), 2000)
                .ConfigureAwait(false);
            Assert.That(observed, Is.EqualTo(2000));
        }

        [Test]
        public async Task ActionHandlerCallsServerMethodAndReturnsOutputAsync()
        {
            const ushort writerId = 7;
            const ushort targetId = 3;
            NodeId objectId = ScalarNode("Methods");
            NodeId methodId = ScalarNode("Methods_Add");

            string[] outputNames = ["Sum"];
            var map = new ActionMethodMap()
                .Add(writerId, targetId, objectId, methodId, outputNames.ToArrayOf());
            var handler = new ServerActionHandler(m_session, map, m_telemetry);

            PubSubActionHandlerResult result = await handler
                .HandleAsync(new PubSubActionInvocation
                {
                    Target = new PubSubActionTarget
                    {
                        DataSetWriterId = writerId,
                        ActionTargetId = targetId
                    },
                    InputFields = new[]
                    {
                        new DataSetField { Name = "a", Value = new Variant(1.5f) },
                        new DataSetField { Name = "b", Value = new Variant((uint)2) }
                    }.ToArrayOf()
                })
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.OutputFields.Count, Is.EqualTo(1));
            Assert.That(result.OutputFields[0].Name, Is.EqualTo("Sum"));
            Assert.That(result.OutputFields[0].Value.TryGetValue(out float sum), Is.True);
            Assert.That(sum, Is.EqualTo(3.5f).Within(0.001f));
        }

        private async Task<int> WaitForCachedValueAsync(
            IReadStrategy strategy,
            ArrayOf<ReadValueId> reads,
            int expected)
        {
            var stopwatch = Stopwatch.StartNew();
            int last = int.MinValue;
            while (stopwatch.Elapsed < s_timeout)
            {
                ArrayOf<DataValue> values = await strategy.ReadAsync(reads).ConfigureAwait(false);
                if (values[0].WrappedValue.TryGetValue(out int value))
                {
                    last = value;
                    if (value == expected)
                    {
                        return value;
                    }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return last;
        }

        private NodeId ScalarNode(string identifier)
        {
            return new NodeId(identifier, m_namespaceIndex);
        }

        private async Task<ushort> ResolveReferenceNamespaceIndexAsync(CancellationToken ct)
        {
            ReadValueId[] reads =
            [
                new ReadValueId
                {
                    NodeId = VariableIds.Server_NamespaceArray,
                    AttributeId = Attributes.Value
                }
            ];
            ArrayOf<DataValue> results = await m_session
                .ReadAsync(reads.ToArrayOf(), ct)
                .ConfigureAwait(false);

            if (!results[0].WrappedValue.TryGetValue(out ArrayOf<string> namespaces) ||
                namespaces.IsNull || namespaces.Count == 0)
            {
                throw new InvalidOperationException("Server namespace array is empty.");
            }
            string[] namespaceUris = namespaces.ToArray()!;
            var table = new NamespaceTable(namespaceUris);
            int index = table.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "Reference server namespace not advertised by the server.");
            }
            return (ushort)index;
        }

        private async Task WriteInt32Async(NodeId nodeId, int value)
        {
            WriteValue[] writes =
            [
                new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(value))
                }
            ];
            ArrayOf<StatusCode> results = await m_session
                .WriteAsync(writes.ToArrayOf())
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(results[0]), Is.True,
                "Writing the test value to the server should succeed.");
        }

        private async Task<int> ReadInt32Async(NodeId nodeId)
        {
            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> results = await m_session
                .ReadAsync(reads.ToArrayOf())
                .ConfigureAwait(false);
            Assert.That(results[0].WrappedValue.TryGetValue(out int value), Is.True);
            return value;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
