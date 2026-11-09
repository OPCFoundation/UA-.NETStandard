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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Covers operation-derived direction and remote-parent authorization at the public mutation tools.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SecurityPubSubMutationAuthorizationTests
    {
        /// <summary>
        /// Rejects opposite-mode plans for each public writer and reader mutation.
        /// </summary>
        [TestCase("WriterGroup")]
        [TestCase("Writer")]
        [TestCase("ReaderGroup")]
        [TestCase("Reader")]
        [Category("SecurityRegression")]
        public async Task SecurityF32MutationDirectionCannotBeSelectedByCallerModeAsync(string kind)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind, false).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Input = peer.Input with { Mode = peer.Publisher ? "Subscriber" : "Publisher" };

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Requires the actual operation's direction grant even when Mode is otherwise valid.
        /// </summary>
        [TestCase("WriterGroup")]
        [TestCase("Writer")]
        [TestCase("ReaderGroup")]
        [TestCase("Reader")]
        [Category("SecurityRegression")]
        public async Task SecurityF32CorrectModeStillNeedsTheActualMutationDirectionGrantAsync(string kind)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind, false).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
            Assert.That(peer.Context.Remote.Reads, Is.Not.Empty,
                "The actual remote connection must be resolved before transport authorization.");
        }

        /// <summary>
        /// Prevents a caller-approved endpoint or profile from standing in for the actual remote parent.
        /// </summary>
        [TestCase("WriterGroup", "endpoint")]
        [TestCase("Writer", "endpoint")]
        [TestCase("ReaderGroup", "endpoint")]
        [TestCase("Reader", "endpoint")]
        [TestCase("WriterGroup", "profile")]
        [TestCase("Writer", "profile")]
        [TestCase("ReaderGroup", "profile")]
        [TestCase("Reader", "profile")]
        [Category("SecurityRegression")]
        public async Task SecurityF32MutationCannotSubstituteACallerAuthorizedParentAsync(string kind, string mismatch)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            if (mismatch == "endpoint")
            {
                peer.Context.Remote.Property(peer.Address, new QualifiedName(BrowseNames.Url),
                    Variant.From("mqtt://unapproved-parent.test:1883"));
            }
            else
            {
                peer.Context.Remote.Property(peer.Connection, new QualifiedName(BrowseNames.TransportProfileUri),
                    Variant.From(Profiles.PubSubUdpUadpTransport));
            }

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
            Assert.That(peer.Context.Remote.Reads, Is.Not.Empty);
        }

        /// <summary>
        /// Dispatches each authorized standard mutation exactly once with its real typed payload.
        /// </summary>
        [TestCase("WriterGroup")]
        [TestCase("Writer")]
        [TestCase("ReaderGroup")]
        [TestCase("Reader")]
        [Category("SecurityRegression")]
        public async Task SecurityF32AuthorizedMutationDispatchesTheExactPayloadOnceAsync(string kind)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            Assert.That(result.IsError, Is.False, RemoteModelTestSession.Payload(result).GetRawText());
            CallMethodRequest call = peer.Context.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(peer.Target));
            Assert.That(call.MethodId, Is.EqualTo(peer.Method));
            Assert.That(call.InputArguments, Has.Count.EqualTo(1));
            Assert.That(call.InputArguments[0].TryGetValue(out ExtensionObject body), Is.True);
            if (kind == "WriterGroup")
            {
                Assert.That(body.TryGetValue(out WriterGroupDataType? group), Is.True);
                Assert.That(group!.Name, Is.EqualTo("Added"));
                Assert.That(group.WriterGroupId, Is.EqualTo(peer.Input.WriterGroupId));
                Assert.That(group.Enabled, Is.True);
                Assert.That(group.TransportSettings.TryGetValue(
                    out BrokerWriterGroupTransportDataType? transport), Is.True);
                Assert.That(transport!.QueueName, Is.EqualTo("data"));
                AssertWriter(group.DataSetWriters.ToList().Single(), peer.Input, McpPubSubConfiguration.WriterName);
            }
            else if (kind == "ReaderGroup")
            {
                Assert.That(body.TryGetValue(out ReaderGroupDataType? group), Is.True);
                Assert.That(group!.Name, Is.EqualTo("Added"));
                Assert.That(group.Enabled, Is.True);
                AssertReader(group.DataSetReaders.ToList().Single(), peer.Input, McpPubSubConfiguration.ReaderName);
            }
            else if (kind == "Writer")
            {
                Assert.That(body.TryGetValue(out DataSetWriterDataType? writer), Is.True);
                AssertWriter(writer!, peer.Input, "Added");
                Assert.That(peer.Context.Remote.Reads.Any(
                    read => read.NodeId == peer.GroupQueue), Is.True,
                    "Authorization must include the existing group's aggregate queue, not the plan's group queue.");
            }
            else
            {
                Assert.That(body.TryGetValue(out DataSetReaderDataType? reader), Is.True);
                AssertReader(reader!, peer.Input, "Added");
            }
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("nodeId").GetString(),
                Is.EqualTo(peer.Context.Remote.Id("assigned").ToString()));
            peer.AssertNoNetwork();
        }

        /// <summary>
        /// Checks the existing parent group's aggregate queue rather than the new writer's caller-selected queue.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF32WriterAdditionNeedsTheActualParentAggregateQueueGrantAsync()
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync("Writer", groupQueueGrant: false)
                .ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
            Assert.That(peer.Context.Remote.Reads.Any(read => read.NodeId == peer.GroupQueue), Is.True);
        }

        /// <summary>
        /// Rejects missing or ambiguous connection ancestry before any writer or reader is added.
        /// </summary>
        [TestCase("Writer", false)]
        [TestCase("Writer", true)]
        [TestCase("Reader", false)]
        [TestCase("Reader", true)]
        [Category("SecurityRegression")]
        public async Task SecurityF32MissingOrAmbiguousParentCannotReachMutationAsync(string kind, bool ambiguous)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            ArrayOf<ReferenceDescription> parents = ambiguous
                ?
                [
                    peer.Context.Remote.Reference(peer.Connection, ObjectTypeIds.PubSubConnectionType),
                    peer.Context.Remote.Reference(peer.Context.Remote.Id("other"), ObjectTypeIds.PubSubConnectionType)
                ] : [];
            peer.Context.Remote.Browse(peer.Target, ReferenceTypeIds.HasComponent, parents, BrowseDirection.Inverse);

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Refuses missing or bad-quality remote endpoint properties instead of falling back to plan values.
        /// </summary>
        [TestCase("WriterGroup", "missing")]
        [TestCase("ReaderGroup", "bad-quality")]
        [TestCase("Writer", "missing")]
        [TestCase("Reader", "bad-quality")]
        [Category("SecurityRegression")]
        public async Task SecurityF32UnreadableParentEndpointCannotFallBackToThePlanAsync(string kind, string failure)
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync(kind).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            if (failure == "missing")
            {
                peer.Context.Remote.Translations.Remove((peer.Address, new QualifiedName(BrowseNames.Url)));
            }
            else
            {
                peer.Context.Remote.Property(peer.Address, new QualifiedName(BrowseNames.Url),
                    DataValue.FromStatusCode(StatusCodes.BadNotReadable));
            }

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Preserves strict input validation for unsupported operation modes.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF32UnsupportedModeCannotReachMutationAsync()
        {
            await using MutationPeer peer = await MutationPeer.CreateAsync("Writer").ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Input = peer.Input with { Mode = "Unsupported" };

            CallToolResult result = await peer.InvokeAsync().ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(peer.Context.Remote.Calls, Is.Empty);
            peer.AssertNoNetwork();
        }

        /// <summary>
        /// Checks the generated writer's identity and independent data/metadata destinations.
        /// </summary>
        private static void AssertWriter(DataSetWriterDataType writer, PubSubApplicationInput input, string name)
        {
            Assert.That(writer.Name, Is.EqualTo(name));
            Assert.That(writer.Enabled, Is.True);
            Assert.That(writer.DataSetName, Is.EqualTo(input.DataSetName));
            Assert.That(writer.DataSetWriterId, Is.EqualTo(input.DataSetWriterId));
            Assert.That(writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? transport),
                Is.True);
            Assert.That(transport!.QueueName, Is.EqualTo("data"));
            Assert.That(transport.MetaDataQueueName, Is.EqualTo("metadata"));
        }

        /// <summary>
        /// Checks the generated reader's filters, metadata and independent subscription destinations.
        /// </summary>
        private static void AssertReader(DataSetReaderDataType reader, PubSubApplicationInput input, string name)
        {
            Assert.That(reader.Name, Is.EqualTo(name));
            Assert.That(reader.Enabled, Is.True);
            Assert.That(reader.WriterGroupId, Is.EqualTo(input.WriterGroupId));
            Assert.That(reader.DataSetWriterId, Is.EqualTo(input.DataSetWriterId));
            Assert.That(reader.PublisherId.TryGetValue(out ushort publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(42));
            Assert.That(reader.DataSetMetaData.Fields, Has.Count.EqualTo(input.Fields.Count));
            Assert.That(reader.TransportSettings.TryGetValue(out BrokerDataSetReaderTransportDataType? transport),
                Is.True);
            Assert.That(transport!.QueueName, Is.EqualTo("data"));
            Assert.That(transport.MetaDataQueueName, Is.EqualTo("metadata"));
        }

        /// <summary>
        /// Binds one public tool to the real policy and an address-space-backed parent connection.
        /// </summary>
        private sealed class MutationPeer : IAsyncDisposable, IMcpDnsResolver, IMcpTcpConnector
        {
            /// <summary>
            /// Sets up the actual remote parent independently from the caller's connection plan.
            /// </summary>
            private MutationPeer(
                PubSubExpansionTestContext context, string kind, McpPubSubOptions options,
                bool directionGrant, bool groupQueueGrant)
            {
                Context = context;
                Kind = kind;
                Publisher = kind.StartsWith("Writer", StringComparison.Ordinal);
                Input = PubSubExpansionTestContext.Input(mode: Publisher ? "Publisher" : "Subscriber") with
                {
                    Connection = new PubSubConnectionInput
                    {
                        Transport = PubSubTransportKind.Mqtt,
                        Encoding = PubSubMessageEncoding.Json,
                        Endpoint = "mqtt://broker.test:1883",
                        Topic = "data",
                        MetadataTopic = "metadata"
                    }
                };
                Target = context.Remote.Id("target");
                bool group = kind.EndsWith("Group", StringComparison.Ordinal);
                Connection = group ? Target : context.Remote.Id("connection");
                context.Remote.Type(Connection, ObjectTypeIds.PubSubConnectionType);
                if (!group)
                {
                    context.Remote.Type(Target,
                        Publisher ? ObjectTypeIds.WriterGroupType : ObjectTypeIds.ReaderGroupType);
                    context.Remote.Browse(Target, ReferenceTypeIds.HasComponent,
                        [context.Remote.Reference(Connection, ObjectTypeIds.PubSubConnectionType)],
                        BrowseDirection.Inverse);
                }
                context.Remote.Property(Connection, new QualifiedName(BrowseNames.TransportProfileUri),
                    Variant.From(Profiles.PubSubMqttJsonTransport));
                Address = context.Remote.Id("address");
                context.Remote.Child(Connection, new QualifiedName(BrowseNames.Address), Address);
                context.Remote.Property(Address, new QualifiedName(BrowseNames.Url),
                    Variant.From(Input.Connection.Endpoint));
                context.Remote.Property(Address, new QualifiedName(BrowseNames.NetworkInterface),
                    Variant.From(string.Empty));
                NodeId transport = context.Remote.Id("group-transport");
                context.Remote.Child(Target, new QualifiedName(BrowseNames.TransportSettings), transport);
                GroupQueue = context.Remote.Property(transport, new QualifiedName(BrowseNames.QueueName),
                    Variant.From("parent/aggregate"));
                context.Remote.Reply(StatusCodes.Good, Variant.From(context.Remote.Id("assigned")));
                var rule = new McpNetworkRule
                {
                    Scheme = "mqtt",
                    Host = "broker.test",
                    Port = 1883,
                    Networks = ["127.0.0.0/8"],
                    Capability = McpCapability.PubSubConfiguration,
                    Operations = ["configure-connect"]
                };
                string direction = Publisher == directionGrant ? "configure-publish" : "configure-subscribe";
                var network = new McpNetworkPolicy(context.Services.GetRequiredService<IMcpOperationPolicy>(),
                    [
                        rule,
                        rule with
                        {
                            Operations = [direction],
                            Resources = groupQueueGrant
                                ? ["data", "metadata", "parent/aggregate"] : ["data", "metadata"]
                        }
                    ], this, this);
                Tools = new PubSubConfigurationTools(context.Sessions, options,
                    context.Services.GetRequiredService<McpFileTransfers>(), network);
                Method = kind switch
                {
                    "WriterGroup" => MethodIds.PubSubConnectionType_AddWriterGroup,
                    "ReaderGroup" => MethodIds.PubSubConnectionType_AddReaderGroup,
                    "Writer" => MethodIds.WriterGroupType_AddDataSetWriter,
                    _ => MethodIds.ReaderGroupType_AddDataSetReader
                };
            }

            /// <summary>
            /// Gets the real policy, registry and fake session context.
            /// </summary>
            internal PubSubExpansionTestContext Context { get; }

            /// <summary>
            /// Gets the selected tool kind.
            /// </summary>
            internal string Kind { get; }

            /// <summary>
            /// Gets whether the actual tool publishes.
            /// </summary>
            internal bool Publisher { get; }

            /// <summary>
            /// Gets or sets the caller-controlled plan.
            /// </summary>
            internal PubSubApplicationInput Input { get; set; }

            /// <summary>
            /// Gets the actual remote mutation target.
            /// </summary>
            internal NodeId Target { get; }

            /// <summary>
            /// Gets the actual remote connection.
            /// </summary>
            internal NodeId Connection { get; }

            /// <summary>
            /// Gets the actual remote address object.
            /// </summary>
            internal NodeId Address { get; }

            /// <summary>
            /// Gets the actual remote writer-group queue variable.
            /// </summary>
            internal NodeId GroupQueue { get; }

            /// <summary>
            /// Gets the generated method expected at the mutation sink.
            /// </summary>
            internal NodeId Method { get; }

            /// <summary>
            /// Gets the public tool under test.
            /// </summary>
            internal PubSubConfigurationTools Tools { get; }

            /// <summary>
            /// Creates an authorized named-session fixture without any live endpoint.
            /// </summary>
            internal static async Task<MutationPeer> CreateAsync(
                string kind, bool directionGrant = true, bool groupQueueGrant = true)
            {
                string operation = kind switch
                {
                    "WriterGroup" => "pubsub_configuration_add_writer_group",
                    "ReaderGroup" => "pubsub_configuration_add_reader_group",
                    "Writer" => "pubsub_configuration_add_writer",
                    _ => "pubsub_configuration_add_reader"
                };
                var options = new McpPubSubOptions
                {
                    ModelRules =
                    [
                        new McpPubSubModelRule
                        {
                            SessionName = PubSubExpansionTestContext.SessionName,
                            NodeId = "nsu=urn:test:remote-model;s=target",
                            Operations = [operation]
                        }
                    ]
                };
                PubSubExpansionTestContext context = await PubSubExpansionTestContext.CreateAsync(options: options)
                    .ConfigureAwait(false);
                try
                {
                    return new MutationPeer(context, kind, options, directionGrant, groupQueueGrant);
                }
                catch
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Invokes the real public mutation overload selected by the tool rather than by Mode.
            /// </summary>
            internal Task<CallToolResult> InvokeAsync()
            {
                return Kind switch
                {
                    "WriterGroup" => Tools.AddWriterGroupAsync(Target.ToString(), "Added", Input, Session),
                    "ReaderGroup" => Tools.AddReaderGroupAsync(Target.ToString(), "Added", Input, Session),
                    "Writer" => Tools.AddWriterAsync(Target.ToString(), "Added", Input, Session),
                    _ => Tools.AddReaderAsync(Target.ToString(), "Added", Input, Session)
                };
            }

            /// <summary>
            /// Proves security rejection precedes every remote mutation and local transport activation.
            /// </summary>
            internal void AssertDenied(CallToolResult result)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
                Assert.That(Context.Remote.Calls, Is.Empty);
                AssertNoNetwork();
            }

            /// <summary>
            /// Proves parent discovery and authorization did not activate a local network backend.
            /// </summary>
            internal void AssertNoNetwork()
            {
                Assert.That(m_networkAttempts, Is.Zero);
                Assert.That(Context.Transports.Factories, Is.Zero);
            }

            /// <inheritdoc/>
            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                m_networkAttempts++;
                throw new InvalidOperationException("Remote configuration must not resolve broker addresses.");
            }

            /// <inheritdoc/>
            public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
            {
                m_networkAttempts++;
                throw new InvalidOperationException("Remote configuration must not connect to a broker.");
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return Context.DisposeAsync();
            }

            private const string Session = PubSubExpansionTestContext.SessionName;
            private int m_networkAttempts;
        }
    }
}
#endif
