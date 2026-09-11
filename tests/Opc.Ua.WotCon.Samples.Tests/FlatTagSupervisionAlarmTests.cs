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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FlatTagServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// The upstream flat-tag servers are the only thing in the sample that can
    /// originate an event, so everything the aggregating server is meant to
    /// project depends on them raising real OPC UA alarm conditions rather than
    /// exposing a boolean tag.
    /// </summary>
    /// <remarks>
    /// These tests talk to a source server directly, not through the
    /// aggregating server. That is deliberate: they establish that the origin
    /// behaves before any aggregation is layered on top, so a later failure can
    /// be attributed to the aggregation rather than to the source.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [NonParallelizable]
    public sealed class FlatTagSupervisionAlarmTests
    {
        private const string kSourceANamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:SourceA";

        private SourceConnection? m_source;

        /// <summary>
        /// One source server serves the whole fixture. Each test drives the
        /// signal it examines to a known state first, so they do not need
        /// isolation from one another - and starting a server per test adds
        /// enough churn to destabilise the other suites sharing this run.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            m_source = await SourceConnection.StartAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the shared flat-tag source and releases its client connection after the fixture completes.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_source is not null)
            {
                await m_source.DisposeAsync().ConfigureAwait(false);
                m_source = null;
            }
        }

        /// <summary>
        /// Writing the supervision tag must raise the alarm condition, because
        /// the tag and the condition are two views of one signal.
        /// </summary>
        [Test]
        public async Task TrippingASupervisionTagRaisesTheAlarmConditionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            SourceConnection source = m_source!;

            ushort ns = ResolveNamespace(source.Session, kSourceANamespaceUri);
            var cavitation = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation", ns);
            var alarm = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation.Alarm", ns);

            // The environment seeds this signal, so drive it to a known state
            // first: the test is about the transition, not about the seed.
            await WriteBooleanAsync(source.Session, cavitation, value: false, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadActiveStateAsync(source.Session, alarm, timeout.Token)
                .ConfigureAwait(false), Is.False,
                "Clearing the tag must leave the alarm inactive.");

            await WriteBooleanAsync(source.Session, cavitation, value: true, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(await ReadActiveStateAsync(source.Session, alarm, timeout.Token)
                .ConfigureAwait(false), Is.True,
                "Writing the supervision tag must drive the alarm condition active.");
            Assert.That(await ReadBooleanAsync(source.Session, cavitation, timeout.Token)
                .ConfigureAwait(false), Is.True,
                "The tag itself must reflect the write, since a flat-tag client reads it.");
        }

        /// <summary>
        /// The condition must be acknowledgeable, because the aggregating server
        /// propagates a client's acknowledgement to this server; without a
        /// working Acknowledge there is nothing for that round trip to reach.
        /// </summary>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public async Task ATrippedAlarmRetainsOperatorObligationsUntilConfirmedAsync(string pumpName)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            SourceConnection source = m_source!;

            ushort ns = ResolveNamespace(source.Session, kSourceANamespaceUri);
            var cavitation = new NodeId(
                pumpName + ".Events.SupervisionProcessFluid.Cavitation", ns);
            var alarm = new NodeId(
                pumpName + ".Events.SupervisionProcessFluid.Cavitation.Alarm", ns);

            await WriteBooleanAsync(source.Session, cavitation, value: false, timeout.Token)
                .ConfigureAwait(false);
            await CompleteOperatorAttentionAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false);

            await WriteBooleanAsync(source.Session, cavitation, value: true, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(await ReadTwoStateAsync(
                    source.Session, alarm, "AckedState", timeout.Token)
                .ConfigureAwait(false), Is.False,
                "A newly tripped alarm must require operator attention.");
            Assert.That(await ReadRetainAsync(source.Session, alarm, timeout.Token)
                .ConfigureAwait(false), Is.True,
                "A tripped alarm must be retained so a ConditionRefresh replays it.");
            ByteString tripId = await ReadEventIdAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false);

            await WriteBooleanAsync(source.Session, cavitation, value: false, timeout.Token).ConfigureAwait(false);

            Assert.That(await ReadTwoStateAsync(source.Session, alarm, "AckedState", timeout.Token)
                .ConfigureAwait(false), Is.False, "Returning to normal does not acknowledge an occurrence.");
            Assert.That(await ReadTwoStateAsync(source.Session, alarm, "ConfirmedState", timeout.Token)
                .ConfigureAwait(false), Is.False);
            Assert.That(await ReadRetainAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false), Is.True);
            Assert.That(await ReadEventIdAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false),
                Is.Not.EqualTo(tripId), "A state transition produces a new occurrence identity.");

            await CallConditionAsync(source.Session, alarm, "Acknowledge", timeout.Token).ConfigureAwait(false);
            Assert.That(await ReadTwoStateAsync(source.Session, alarm, "AckedState", timeout.Token)
                .ConfigureAwait(false), Is.True);
            Assert.That(await ReadRetainAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false), Is.True);

            await CallConditionAsync(source.Session, alarm, "Confirm", timeout.Token).ConfigureAwait(false);
            Assert.That(await ReadTwoStateAsync(source.Session, alarm, "ConfirmedState", timeout.Token)
                .ConfigureAwait(false), Is.True);
            Assert.That(await ReadRetainAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Reset is one of the management actions the asset projects, so it has
        /// to actually clear the condition it claims to.
        /// </summary>
        [Test]
        public async Task ResetClearsATrippedSupervisionSignalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            SourceConnection source = m_source!;

            ushort ns = ResolveNamespace(source.Session, kSourceANamespaceUri);
            var pump = new NodeId("Pump1", ns);
            var cavitation = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation", ns);
            var alarm = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation.Alarm", ns);

            await WriteBooleanAsync(source.Session, cavitation, value: true, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadActiveStateAsync(source.Session, alarm, timeout.Token)
                .ConfigureAwait(false), Is.True);

            await source.Session.CallAsync(
                    pump, new NodeId("Pump1.Reset", ns), timeout.Token)
                .ConfigureAwait(false);

            Assert.That(await ReadActiveStateAsync(source.Session, alarm, timeout.Token)
                .ConfigureAwait(false), Is.False,
                "Reset must return the supervision signal to normal.");
            Assert.That(await ReadBooleanAsync(source.Session, cavitation, timeout.Token)
                .ConfigureAwait(false), Is.False,
                "Reset must clear the tag too, not only the condition.");
            Assert.That(await ReadTwoStateAsync(source.Session, alarm, "AckedState", timeout.Token)
                .ConfigureAwait(false), Is.False, "Reset must not acknowledge an alarm.");
            Assert.That(await ReadRetainAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false), Is.True);
            await CompleteOperatorAttentionAsync(source.Session, alarm, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that an event identifier from another pump cannot acknowledge the first pump's alarm.
        /// </summary>
        [Test]
        public async Task AnEventIdFromAnotherPumpCannotAcknowledgeAnAlarmAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            ManagedSession session = m_source!.Session;
            ushort ns = ResolveNamespace(session, kSourceANamespaceUri);
            var first = new NodeId("Pump1.Events.SupervisionProcessFluid.Cavitation.Alarm", ns);
            var second = new NodeId("Pump2.Events.SupervisionProcessFluid.Cavitation.Alarm", ns);
            await WriteBooleanAsync(
                session, new NodeId("Pump1.Events.SupervisionProcessFluid.Cavitation", ns), false, timeout.Token)
                .ConfigureAwait(false);
            await WriteBooleanAsync(
                session, new NodeId("Pump1.Events.SupervisionProcessFluid.Cavitation", ns), true, timeout.Token)
                .ConfigureAwait(false);
            ByteString wrongEventId = await ReadEventIdAsync(session, second, timeout.Token).ConfigureAwait(false);
            NodeId acknowledge = await TranslateAsync(session, first, "Acknowledge", timeout.Token)
                .ConfigureAwait(false);

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session.CallAsync(
                    first, acknowledge, timeout.Token, Variant.From(wrongEventId), Variant.From(LocalizedText.Null))
                    .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(await ReadTwoStateAsync(session, first, "AckedState", timeout.Token).ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// Verifies each pump's manufacturer, serial number, and product-instance URI retain their expected OPC UA
        /// types.
        /// </summary>
        [TestCase("Pump1", "SN-001")]
        [TestCase("Pump2", "SN-002")]
        public async Task BothPumpsExposeTypedIdentityPropertiesAsync(string pumpName, string serialNumber)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            ManagedSession session = m_source!.Session;
            ushort ns = ResolveNamespace(session, kSourceANamespaceUri);
            DataValue manufacturer = await session.ReadValueAsync(
                new NodeId(pumpName + ".Identification.Manufacturer", ns), timeout.Token).ConfigureAwait(false);
            DataValue serial = await session.ReadValueAsync(
                new NodeId(pumpName + ".Identification.SerialNumber", ns), timeout.Token).ConfigureAwait(false);
            DataValue uri = await session.ReadValueAsync(
                new NodeId(pumpName + ".Identification.ProductInstanceUri", ns), timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manufacturer.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(manufacturer.WrappedValue.TryGetValue(out LocalizedText name), Is.True);
                Assert.That(name.Text, Is.EqualTo("SimPump Corp"));
                Assert.That(serial.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(serial.WrappedValue.TryGetValue(out string? number), Is.True);
                Assert.That(number, Is.EqualTo(serialNumber));
                Assert.That(uri.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(uri.WrappedValue.TryGetValue(out string? identity), Is.True);
                Assert.That(identity, Is.EqualTo("urn:simdevice:SimPump:PumpX-2000:" + serialNumber));
            });
        }

        private static async Task CompleteOperatorAttentionAsync(
            ManagedSession session, NodeId alarm, CancellationToken cancellationToken)
        {
            if (!await ReadTwoStateAsync(session, alarm, "AckedState", cancellationToken).ConfigureAwait(false))
            {
                await CallConditionAsync(session, alarm, "Acknowledge", cancellationToken).ConfigureAwait(false);
            }
            if (!await ReadTwoStateAsync(session, alarm, "ConfirmedState", cancellationToken).ConfigureAwait(false))
            {
                await CallConditionAsync(session, alarm, "Confirm", cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task CallConditionAsync(
            ManagedSession session, NodeId alarm, string name, CancellationToken cancellationToken)
        {
            ByteString eventId = await ReadEventIdAsync(session, alarm, cancellationToken).ConfigureAwait(false);
            NodeId method = await TranslateAsync(session, alarm, name, cancellationToken).ConfigureAwait(false);
            ArrayOf<Variant> result = await session.CallAsync(
                alarm, method, cancellationToken,
                Variant.From(eventId), Variant.From(new LocalizedText("Demo operator")))
                .ConfigureAwait(false);
            Assert.That(result, Is.Empty);
        }

        private static async Task<ByteString> ReadEventIdAsync(
            ManagedSession session, NodeId alarm, CancellationToken cancellationToken)
        {
            NodeId nodeId = await TranslateAsync(session, alarm, "EventId", cancellationToken).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ByteString eventId), Is.True);
            Assert.That(eventId.IsEmpty, Is.False);
            return eventId;
        }

        /// <summary>
        /// Start and Stop are the other two management actions, and they must
        /// have an observable effect for the management group to be worth
        /// projecting.
        /// </summary>
        [Test]
        public async Task StopAndStartDriveTheRunningStateAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            SourceConnection source = m_source!;

            ushort ns = ResolveNamespace(source.Session, kSourceANamespaceUri);
            var pump = new NodeId("Pump1", ns);
            var running = new NodeId("Pump1.Running", ns);

            Assert.That(await ReadBooleanAsync(source.Session, running, timeout.Token)
                .ConfigureAwait(false), Is.True);

            await source.Session.CallAsync(
                    pump, new NodeId("Pump1.Stop", ns), timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadBooleanAsync(source.Session, running, timeout.Token)
                .ConfigureAwait(false), Is.False,
                "Stop must be observable, not a no-op.");

            await source.Session.CallAsync(
                    pump, new NodeId("Pump1.Start", ns), timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadBooleanAsync(source.Session, running, timeout.Token)
                .ConfigureAwait(false), Is.True);
        }

        private static ushort ResolveNamespace(ManagedSession session, string namespaceUri)
        {
            int index = session.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(index, Is.GreaterThan(0), $"'{namespaceUri}' must be registered.");
            return (ushort)index;
        }

        private static Task<bool> ReadActiveStateAsync(
            ManagedSession session,
            NodeId alarm,
            CancellationToken cancellationToken)
        {
            return ReadTwoStateAsync(session, alarm, "ActiveState", cancellationToken);
        }

        private static async Task<bool> ReadTwoStateAsync(
            ManagedSession session,
            NodeId alarm,
            string stateBrowseName,
            CancellationToken cancellationToken)
        {
            NodeId stateId = await TranslateAsync(
                    session, alarm, stateBrowseName + "/Id", cancellationToken)
                .ConfigureAwait(false);
            return await ReadBooleanAsync(session, stateId, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<bool> ReadRetainAsync(
            ManagedSession session,
            NodeId alarm,
            CancellationToken cancellationToken)
        {
            NodeId retainId = await TranslateAsync(
                    session, alarm, "Retain", cancellationToken)
                .ConfigureAwait(false);
            return await ReadBooleanAsync(session, retainId, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<NodeId> TranslateAsync(
            ManagedSession session,
            NodeId start,
            string relativePath,
            CancellationToken cancellationToken)
        {
            var browsePath = new BrowsePath
            {
                StartingNode = start,
                RelativePath = RelativePath.Parse(relativePath, session.TypeTree)
            };
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, [browsePath], cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<BrowsePathResult> results = response.Results;
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(results[0].StatusCode), Is.True,
                $"'{relativePath}' must resolve from {start}; got {results[0].StatusCode}.");
            Assert.That(results[0].Targets, Has.Count.GreaterThan(0));
            return ExpandedNodeId.ToNodeId(results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static async Task<bool> ReadBooleanAsync(
            ManagedSession session,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DataValue value = await session
                .ReadValueAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                $"Reading {nodeId} must succeed; got {value.StatusCode}.");
            Assert.That(value.WrappedValue.TryGetValue(out bool result), Is.True,
                $"{nodeId} must carry a Boolean.");
            return result;
        }

        private static async Task WriteBooleanAsync(
            ManagedSession session,
            NodeId nodeId,
            bool value,
            CancellationToken cancellationToken)
        {
            var write = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(value))
            };
            WriteResponse response = await session
                .WriteAsync(null, [write], cancellationToken)
                .ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True,
                $"Writing {nodeId} must succeed; got {response.Results[0]}.");
        }

        /// <summary>
        /// One flat-tag source server and a session connected straight to it.
        /// </summary>
        /// <remarks>
        /// Only the source is started. These tests are about the origin of the
        /// events, so booting the aggregating server and the second source would
        /// add two servers of load and a dependency on aggregation that the
        /// assertions do not use.
        /// </remarks>
        private sealed class SourceConnection : IAsyncDisposable
        {
            private SourceConnection(
                string root,
                IHost serverHost,
                IHost clientHost,
                ManagedSession session)
            {
                m_root = root;
                m_serverHost = serverHost;
                m_clientHost = clientHost;
                Session = session;
            }

            /// <summary>
            /// Gets the managed session connected directly to the flat-tag source under test.
            /// </summary>
            public ManagedSession Session { get; }

            /// <summary>
            /// Starts an isolated source server and connects after its transport and server state are ready.
            /// </summary>
            public static async Task<SourceConnection> StartAsync(
                CancellationToken cancellationToken)
            {
                string id = Guid.NewGuid().ToString("N");
                string root = Path.Combine(
                    Path.GetTempPath(), "FlatTagAlarmTests", id);
                Directory.CreateDirectory(root);

                int port = TestPorts.GetFreePorts(1)[0];
                string endpointUrl = $"opc.tcp://127.0.0.1:{port}/SourceA";

                IHost serverHost = FlatTagServerHost.Build(new FlatTagServerOptions
                {
                    EndpointUrl = endpointUrl,
                    SourceNamespaceUri = FlatTagServerOptions.SourceANamespaceUri,
                    ApplicationName = "FlatTagAlarmSource" + id,
                    IncludeUnsecurePolicyNone = true,
                    InstanceName = "SourceA",
                    PkiRoot = Path.Combine(root, "Server", "pki"),
                    Values = new FlatTagValues { Cavitation = false }
                });

                IHost? clientHost = null;
                try
                {
                    await serverHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    await WaitForTcpAsync(port, cancellationToken).ConfigureAwait(false);

                    clientHost = BuildClientHost(root, endpointUrl);
                    await clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    Func<CancellationToken, Task<ManagedSession>> connect =
                        clientHost.Services.GetRequiredService<
                            Func<CancellationToken, Task<ManagedSession>>>();

                    // Accepting a TCP connection only means the transport is
                    // listening; the server answers BadServerHalted until its
                    // state reaches Running, so connecting on the first accept
                    // races startup.
                    ManagedSession session = await ConnectWithRetryAsync(
                        connect, cancellationToken).ConfigureAwait(false);
                    await session.FetchNamespaceTablesAsync(cancellationToken)
                        .ConfigureAwait(false);
                    session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
                    return new SourceConnection(root, serverHost, clientHost, session);
                }
                catch
                {
                    if (clientHost is not null)
                    {
                        await StopAsync(clientHost).ConfigureAwait(false);
                    }
                    await StopAsync(serverHost).ConfigureAwait(false);
                    TryDelete(root);
                    throw;
                }
            }

            /// <summary>
            /// Stops the client and source hosts and attempts to remove their temporary test state.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await StopAsync(m_clientHost).ConfigureAwait(false);
                await StopAsync(m_serverHost).ConfigureAwait(false);
                TryDelete(m_root);
            }

            private static async Task StopAsync(IHost host)
            {
                try
                {
                    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TestContext.Out.WriteLine("Ignoring teardown failure: {0}", ex.Message);
                }
                finally
                {
                    host.Dispose();
                }
            }

            private static void TryDelete(string root)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
            }

            private static async Task<ManagedSession> ConnectWithRetryAsync(
                Func<CancellationToken, Task<ManagedSession>> connect,
                CancellationToken cancellationToken)
            {
                ServiceResultException? last = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        return await connect(cancellationToken).ConfigureAwait(false);
                    }
                    catch (ServiceResultException ex) when (
                        ex.StatusCode == StatusCodes.BadServerHalted ||
                        ex.StatusCode == StatusCodes.BadNotConnected ||
                        ex.StatusCode == StatusCodes.BadInternalError)
                    {
                        last = ex;
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }
                }
                if (last is not null)
                {
                    throw last;
                }
                throw new InvalidOperationException(
                    "The source server never reached a connectable state.");
            }

            private static async Task WaitForTcpAsync(
                int port,
                CancellationToken cancellationToken)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var client = new TcpClient();
                    try
                    {
                        await client.ConnectAsync("127.0.0.1", port, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                    catch (SocketException)
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            private static IHost BuildClientHost(string root, string endpointUrl)
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                string name = "FlatTagAlarmClient" + Guid.NewGuid().ToString("N");
                builder.Services
                    .AddOpcUa()
                    .AddOpcTcpTransport()
                    .AddClient(client =>
                    {
                        client.ApplicationName = name;
                        client.ApplicationUri = "urn:localhost:OPCFoundation:" + name;
                        client.ProductUri = "uri:opcfoundation.org:FlatTagAlarmClient";
                        client.PkiRoot = Path.Combine(root, "Client", "pki");
                        client.AutoAcceptUntrustedCertificates = true;
                        client.Session = new ManagedSessionOptions
                        {
                            SessionName = name,
                            SessionTimeout = TimeSpan.FromSeconds(60)
                        };
                    })
                    .AddDiscoveryAndConnect(discovery =>
                    {
                        discovery.DiscoveryUrl = endpointUrl;
                        discovery.SecurityMode = MessageSecurityMode.None;
                        discovery.SecurityPolicyUri = SecurityPolicies.None;
                    });
                return builder.Build();
            }

            private readonly string m_root;
            private readonly IHost m_serverHost;
            private readonly IHost m_clientHost;
        }
    }
}
