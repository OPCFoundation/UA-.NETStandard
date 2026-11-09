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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exercises imported effective destinations through the real file tool and authority over a fake session.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SecurityPubSubImportAuthorizationTests
    {
        /// <summary>
        /// Refuses group-free and empty-group broker imports despite grants for their ordinary topics.
        /// </summary>
        [TestCase(PubSubTransportKind.Mqtt, "none")]
        [TestCase(PubSubTransportKind.Kafka, "none")]
        [TestCase(PubSubTransportKind.Mqtt, "writer")]
        [TestCase(PubSubTransportKind.Mqtt, "reader")]
        [TestCase(PubSubTransportKind.Kafka, "writer")]
        [TestCase(PubSubTransportKind.Kafka, "reader")]
        [Category("SecurityRegression")]
        public async Task SecurityF08GroupFreeImportCannotActivateUnauthorizedBrokerAsync(
            PubSubTransportKind transport, string groups)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                transport, false, ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.EmptyGroups(groups);

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Refuses group-free datagram and Ethernet imports without native transport activation authority.
        /// </summary>
        [TestCase(PubSubTransportKind.Udp)]
        [TestCase(PubSubTransportKind.Dtls)]
        [TestCase(PubSubTransportKind.Ethernet)]
        [Category("SecurityRegression")]
        public async Task SecurityF08GroupFreeNonTcpImportStillRequiresTransportAuthorityAsync(
            PubSubTransportKind transport)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(transport, false).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.EmptyGroups("none");

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Preserves explicitly granted group-free configurations for every supported transport.
        /// </summary>
        [TestCase(PubSubTransportKind.Mqtt)]
        [TestCase(PubSubTransportKind.Kafka)]
        [TestCase(PubSubTransportKind.Udp)]
        [TestCase(PubSubTransportKind.Dtls)]
        [TestCase(PubSubTransportKind.Ethernet)]
        [Category("SecurityRegression")]
        public async Task SecurityF08ExplicitConnectionGrantAllowsGroupFreeImportAsync(PubSubTransportKind transport)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(transport, true).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.EmptyGroups("none");

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            Assert.That(peer.Applied!.Connections[0].TransportProfileUri,
                Is.EqualTo(peer.Model.Connections[0].TransportProfileUri));
            Assert.That(peer.Applied.Connections[0].WriterGroups, Is.Empty);
            Assert.That(peer.Applied.Connections[0].ReaderGroups, Is.Empty);
        }

        /// <summary>
        /// Allows empty broker groups when connection defaults and any explicit group queue are authorized.
        /// </summary>
        [TestCase("writer")]
        [TestCase("reader")]
        [Category("SecurityRegression")]
        public async Task SecurityF08ExplicitActivationAndQueueGrantsAllowEmptyBrokerGroupsAsync(string groups)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.EmptyGroups(groups);

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            Assert.That(peer.Applied!.Connections[0].WriterGroups.Count, Is.EqualTo(groups == "writer" ? 1 : 0));
            Assert.That(peer.Applied.Connections[0].ReaderGroups.Count, Is.EqualTo(groups == "reader" ? 1 : 0));
        }

        /// <summary>
        /// Preserves a normal reader configuration with independently authorized subscription queues.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF08AuthorizedReaderImportRetainsTheExactSubscribeQueuesAsync()
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, subscribeTopics: ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Model.Connections[0].WriterGroups = [];
            peer.Model.Connections[0].ReaderGroups =
            [
                McpPubSubConfiguration.ReaderGroup(peer.Input, peer.Model.PublishedDataSets[0].DataSetMetaData)
            ];

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            Assert.That(peer.Applied!.Connections[0].WriterGroups, Is.Empty);
            Assert.That(peer.Applied.Connections[0].ReaderGroups[0].DataSetReaders[0].TransportSettings.TryGetValue(
                out BrokerDataSetReaderTransportDataType? reader), Is.True);
            Assert.That(reader!.QueueName, Is.EqualTo("data"));
            Assert.That(reader.MetaDataQueueName, Is.EqualTo("metadata"));
        }

        /// <summary>
        /// Requires a separate grant for every connection, including dormant connections that can later activate.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [Category("SecurityRegression")]
        public async Task SecurityF08EveryConnectionNeedsItsOwnActivationGrantAsync(bool enabled)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            var attacker = (PubSubConnectionDataType)peer.Model.Connections[0].Clone();
            attacker.Name = "unapproved";
            attacker.Enabled = enabled;
            attacker.Address = new ExtensionObject(new NetworkAddressUrlDataType
            {
                Url = "mqtt://attacker.test:1883"
            });
            attacker.WriterGroups = [];
            attacker.ReaderGroups = [];
            peer.Model.Connections = [peer.Model.Connections[0], attacker];

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Does not require network authority when the imported configuration contains no connections.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF08EmptyConfigurationNeedsNoNetworkGrantAsync()
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(PubSubTransportKind.Mqtt, false)
                .ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Model.Connections = [];

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            Assert.That(peer.Applied!.Connections, Is.Empty);
        }

        /// <summary>
        /// Refuses an endpoint whose scheme does not implement its configured broker transport profile.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF08ConnectionGrantCannotAuthorizeADifferentTransportProfileAsync()
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(PubSubTransportKind.Mqtt, true)
                .ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.EmptyGroups("none");
            peer.Model.Connections[0].TransportProfileUri = PubSubInputValidation.Profile(new PubSubConnectionInput
            {
                Transport = PubSubTransportKind.Kafka,
                Encoding = PubSubMessageEncoding.Json,
                Endpoint = "kafka://broker.test:1883"
            });

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Requires the group publication queue even when every individual writer destination is permitted.
        /// </summary>
        [TestCase(PubSubTransportKind.Mqtt, 0)]
        [TestCase(PubSubTransportKind.Mqtt, 1)]
        [TestCase(PubSubTransportKind.Mqtt, 2)]
        [TestCase(PubSubTransportKind.Kafka, 2)]
        [Category("SecurityRegression")]
        public async Task SecurityF31AggregateWriterQueueNeedsPublishGrantAsync(
            PubSubTransportKind transport, int writers)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                transport, true, ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Writers(writers, "restricted/group");

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Commits an authorized aggregate and verifies the real transport selects its group fallback queue.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF31AuthorizedAggregateUsesTheActualGroupQueueAsync()
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data", "metadata", "approved/group"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Writers(2, "approved/group");

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            PubSubConnectionDataType connection = peer.Applied!.Connections[0];
            WriterGroupDataType group = connection.WriterGroups[0];
            Assert.That(group.DataSetWriters, Has.Count.EqualTo(2));
            await using MqttBrokerTransport broker = TopicTransport(peer);
            PubSub.Encoding.PublisherId publisher = PubSubInputValidation.Publisher(
                peer.Input.PublisherId, peer.Input.Connection.Encoding);
            Assert.That(broker.BuildDataTopic(publisher, group, null), Is.EqualTo("approved/group"),
                "A combined frame has no single DataSetWriterId and selects the group queue.");
            Assert.That(broker.BuildDataTopic(publisher, group, group.DataSetWriters[0].DataSetWriterId),
                Is.EqualTo("data"));
            Assert.That(broker.BuildMetaDataTopic(
                publisher, group.WriterGroupId, group.DataSetWriters[1].DataSetWriterId), Is.EqualTo("metadata"));
            Assert.That(broker.IsConnected, Is.False);
        }

        /// <summary>
        /// Rejects an unapproved second writer's data or metadata queue before remote staging begins.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF31EachWriterQueueMustBeAuthorizedBeforeImportAsync(bool metadata)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data", "metadata", "approved/group"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Writers(2, "approved/group");
            peer.Model.Connections[0].WriterGroups[0].DataSetWriters[1].TransportSettings =
                new ExtensionObject(new BrokerDataSetWriterTransportDataType
                {
                    QueueName = metadata ? "data" : "restricted/writer",
                    MetaDataQueueName = metadata ? "restricted/metadata" : "metadata"
                });

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Clearly rejects group fallback destinations that depend on unknown remote topic-generation defaults.
        /// </summary>
        [TestCase("missing", 1)]
        [TestCase("null", 2)]
        [TestCase("", 2)]
        [TestCase(" ", 0)]
        [Category("SecurityRegression")]
        public async Task SecurityF31UnknownGeneratedGroupFallbackIsRejectedBeforeImportAsync(
            string queue, int writers)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Writers(writers, queue == "null" ? null : queue);
            if (queue == "missing")
            {
                peer.Model.Connections[0].WriterGroups[0].TransportSettings = ExtensionObject.Null;
            }

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Supports a writer's empty data queue only through its explicit authorized group fallback.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [Category("SecurityRegression")]
        public async Task SecurityF31WriterDataFallbackRequiresTheAuthorizedGroupQueueAsync(string? writerQueue)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["approved/group", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Writers(1, "approved/group");
            peer.Model.Connections[0].WriterGroups[0].DataSetWriters[0].TransportSettings =
                new ExtensionObject(new BrokerDataSetWriterTransportDataType
                {
                    QueueName = writerQueue,
                    MetaDataQueueName = "metadata"
                });

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertApplied(result);
            Assert.That(peer.Applied!.Connections[0].WriterGroups[0].TransportSettings.TryGetValue(
                out BrokerWriterGroupTransportDataType? settings), Is.True);
            Assert.That(settings!.QueueName, Is.EqualTo("approved/group"));
            await using MqttBrokerTransport broker = TopicTransport(peer);
            PubSub.Encoding.PublisherId publisher = PubSubInputValidation.Publisher(
                peer.Input.PublisherId, peer.Input.Connection.Encoding);
            WriterGroupDataType group = peer.Applied.Connections[0].WriterGroups[0];
            Assert.That(broker.BuildDataTopic(publisher, group, group.DataSetWriters[0].DataSetWriterId),
                Is.EqualTo("approved/group"));
            Assert.That(broker.IsConnected, Is.False);
        }

        /// <summary>
        /// Rejects generated metadata topics instead of assuming that a data-topic grant covers them.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [Category("SecurityRegression")]
        public async Task SecurityF31UnknownMetadataFallbackCannotReachImportAsync(string? metadataQueue)
        {
            await using ImportPeer peer = await ImportPeer.CreateAsync(
                PubSubTransportKind.Mqtt, true, ["data", "metadata"]).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Model.Connections[0].WriterGroups[0].DataSetWriters[0].TransportSettings =
                new ExtensionObject(new BrokerDataSetWriterTransportDataType
                {
                    QueueName = "data",
                    MetaDataQueueName = metadataQueue
                });

            CallToolResult result = await peer.ImportAsync().ConfigureAwait(false);

            peer.AssertDenied(result);
        }

        /// <summary>
        /// Uses the real MQTT topic selector without opening its transport or creating a client adapter.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// No committed configuration is available, or its connection has no transport profile.
        /// </exception>
        private static MqttBrokerTransport TopicTransport(ImportPeer peer)
        {
            PubSubConfigurationDataType applied = peer.Applied ??
                throw new InvalidOperationException("A committed configuration is required for topic selection.");
            PubSubConnectionDataType connection = applied.Connections[0];
            string profile = connection.TransportProfileUri ??
                throw new InvalidOperationException("The committed connection must declare its transport profile.");
            Assert.That(profile, Is.EqualTo(PubSubInputValidation.Profile(peer.Input.Connection)));
            var factory = MqttPubSubTransportFactory.CreateDefault(
                profile, Options.Create(new MqttConnectionOptions
                {
                    Endpoint = peer.Input.Connection.Endpoint,
                    IncludeDiscoverySubscriptions = false
                }));
            return (MqttBrokerTransport)factory.Create(
                connection, peer.Context.Remote.Context.Telemetry, peer.Context.Clock);
        }

        /// <summary>
        /// A standard configuration upload peer below the real generated client and file transfer.
        /// </summary>
        private sealed class ImportPeer : IAsyncDisposable
        {
            /// <summary>
            /// Binds observable remote staging and a network seam that refuses every I/O attempt.
            /// </summary>
            private ImportPeer(
                PubSubExpansionTestContext context, string root, PubSubApplicationInput input,
                McpPubSubOptions options, ArrayOf<McpNetworkRule> rules)
            {
                Context = context;
                Root = root;
                Input = input;
                var source = new McpPubSubDataSetSource(input, options, context.Remote.Context, context.Clock);
                Model = McpPubSubConfiguration.Build(input, source.BuildMetaData());
                var network = new McpNetworkPolicy(
                    context.Services.GetRequiredService<IMcpOperationPolicy>(), rules, m_network, m_network);
                Tools = new PubSubConfigurationTools(context.Sessions, options,
                    context.Services.GetRequiredService<McpFileTransfers>(), network);
                context.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
                NodeId file = context.Remote.Id("configuration");
                context.Remote.Child(ObjectIds.PublishSubscribe, new QualifiedName(BrowseNames.PubSubConfiguration),
                    file);
                context.Remote.Type(file, ObjectTypeIds.PubSubConfigurationType);
                context.Remote.OnCall = Call;
            }

            /// <summary>
            /// Gets the existing session and authority fixture.
            /// </summary>
            internal PubSubExpansionTestContext Context { get; }

            /// <summary>
            /// Gets the test-owned transfer root.
            /// </summary>
            internal string Root { get; }

            /// <summary>
            /// Gets the typed plan used to construct the imported model.
            /// </summary>
            internal PubSubApplicationInput Input { get; }

            /// <summary>
            /// Gets the real tool under test.
            /// </summary>
            internal PubSubConfigurationTools Tools { get; }

            /// <summary>
            /// Gets the configuration serialized into the import artifact.
            /// </summary>
            internal PubSubConfigurationDataType Model { get; }

            /// <summary>
            /// Gets the configuration actually committed by the remote method.
            /// </summary>
            internal PubSubConfigurationDataType? Applied { get; private set; }

            /// <summary>
            /// Creates a confined transfer and explicit transport grants without network setup.
            /// </summary>
            internal static async Task<ImportPeer> CreateAsync(
                PubSubTransportKind transport, bool activate,
                ArrayOf<string> publishTopics = default, ArrayOf<string> subscribeTopics = default)
            {
                string root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                    "SecurityPubSubImport-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                PubSubConnectionInput connection = transport switch
                {
                    PubSubTransportKind.Mqtt or PubSubTransportKind.Kafka => new PubSubConnectionInput
                    {
                        Transport = transport,
                        Encoding = PubSubMessageEncoding.Json,
                        Endpoint = transport == PubSubTransportKind.Mqtt
                            ? "mqtt://broker.test:1883" : "kafka://broker.test:1883",
                        Topic = "data",
                        MetadataTopic = "metadata"
                    },
                    PubSubTransportKind.Ethernet => new PubSubConnectionInput
                    {
                        Transport = transport,
                        Endpoint = "opc.eth://01-1B-19-00-00-01?vid=7&pcp=3",
                        NetworkInterface = "test-interface"
                    },
                    _ => new PubSubConnectionInput
                    {
                        Transport = transport,
                        Endpoint = transport == PubSubTransportKind.Dtls
                            ? "opc.dtls://127.0.0.1:4849" : "opc.udp://127.0.0.1:4849",
                        NetworkInterface = "test-interface"
                    }
                };
                PubSubApplicationInput input = PubSubExpansionTestContext.Input() with { Connection = connection };
                var options = new McpPubSubOptions
                {
                    ModelRules =
                    [
                        new McpPubSubModelRule
                        {
                            SessionName = Session,
                            NodeId = ObjectIds.PublishSubscribe.ToString(),
                            Operations = ["pubsub_configuration_import"]
                        }
                    ],
                    DatagramRules = activate && transport is PubSubTransportKind.Udp or PubSubTransportKind.Dtls
                        ?
                        [
                            new McpPubSubDatagramRule
                            {
                                Endpoint = connection.Endpoint,
                                NetworkInterface = "test-interface",
                                LocalAddress = "127.0.0.1",
                                Operations = ["configure-connect", "configure-publish", "configure-subscribe"]
                            }
                        ] : [],
                    EthernetRules = activate && transport == PubSubTransportKind.Ethernet
                        ?
                        [
                            new McpPubSubEthernetRule
                            {
                                Endpoint = connection.Endpoint,
                                NetworkInterface = "test-interface",
                                LocalMac = "02-00-00-00-00-01",
                                Operations = ["configure-connect", "configure-publish", "configure-subscribe"]
                            }
                        ] : []
                };
                var rules = new List<McpNetworkRule>();
                if (transport is PubSubTransportKind.Mqtt or PubSubTransportKind.Kafka)
                {
                    var rule = new McpNetworkRule
                    {
                        Scheme = transport == PubSubTransportKind.Mqtt ? "mqtt" : "kafka",
                        Host = "broker.test",
                        Port = 1883,
                        Networks = ["127.0.0.0/8"],
                        Capability = McpCapability.PubSubConfiguration,
                        Operations = ["configure-connect"]
                    };
                    if (activate)
                    {
                        rules.Add(rule);
                    }
                    if (publishTopics.Count != 0)
                    {
                        rules.Add(rule with { Operations = ["configure-publish"], Resources = publishTopics });
                    }
                    if (subscribeTopics.Count != 0)
                    {
                        rules.Add(rule with { Operations = ["configure-subscribe"], Resources = subscribeTopics });
                    }
                }
                PubSubExpansionTestContext? context = null;
                try
                {
                    context = await PubSubExpansionTestContext.CreateAsync(
                        options: options, transferRoot: root).ConfigureAwait(false);
                    return new ImportPeer(context, root, input, options, new ArrayOf<McpNetworkRule>(rules.ToArray()));
                }
                catch
                {
                    if (context is not null)
                    {
                        await context.DisposeAsync().ConfigureAwait(false);
                    }
                    Directory.Delete(root, recursive: true);
                    throw;
                }
            }

            /// <summary>
            /// Replaces child groups without removing the effective transport connection.
            /// </summary>
            internal void EmptyGroups(string groups)
            {
                PubSubConnectionDataType connection = Model.Connections[0];
                connection.WriterGroups = groups == "writer" ? connection.WriterGroups : [];
                if (groups == "writer")
                {
                    connection.WriterGroups[0].DataSetWriters = [];
                }
                connection.ReaderGroups = groups == "reader"
                    ? [McpPubSubConfiguration.ReaderGroup(Input, Model.PublishedDataSets[0].DataSetMetaData)] : [];
                if (groups == "reader")
                {
                    connection.ReaderGroups[0].DataSetReaders = [];
                }
            }

            /// <summary>
            /// Models aggregate-capable writers independently from their group fallback queue.
            /// </summary>
            internal void Writers(int count, string? queue)
            {
                WriterGroupDataType group = Model.Connections[0].WriterGroups[0];
                DataSetWriterDataType prototype = group.DataSetWriters[0];
                group.TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType
                {
                    QueueName = queue
                });
                var writers = new DataSetWriterDataType[count];
                for (int index = 0; index < count; index++)
                {
                    writers[index] = (DataSetWriterDataType)prototype.Clone();
                    writers[index].Name = "writer" + index;
                    writers[index].DataSetWriterId = checked((ushort)(index + 1));
                }
                group.DataSetWriters = new ArrayOf<DataSetWriterDataType>(writers);
            }

            /// <summary>
            /// Serializes an actual XML artifact and invokes the public import tool.
            /// </summary>
            internal async Task<CallToolResult> ImportAsync()
            {
                using var source = new MemoryStream();
                UaPubSubConfigurationHelper.SaveConfiguration(Model, source, Context.Remote.Context.Telemetry);
                m_artifact = source.ToArray();
                await File.WriteAllBytesAsync(Path.Combine(Root, "import.xml"), m_artifact).ConfigureAwait(false);
                return await Tools.ImportAsync("import.xml", true, Session).ConfigureAwait(false);
            }

            /// <summary>
            /// Proves denial precedes even opening the remote file or creating a local transport.
            /// </summary>
            internal void AssertDenied(CallToolResult result)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
                Assert.That(Context.Remote.Calls, Is.Empty);
                Assert.That(Applied, Is.Null);
                Assert.That(m_uploaded.Length, Is.Zero);
                AssertNoNetwork();
            }

            /// <summary>
            /// Proves the exact uploaded artifact commits once through the generated standard method.
            /// </summary>
            internal void AssertApplied(CallToolResult result)
            {
                Assert.That(result.IsError, Is.False, RemoteModelTestSession.Payload(result).GetRawText());
                Assert.That(Applied, Is.Not.Null);
                Assert.That(m_uploaded.ToArray(), Is.EqualTo(m_artifact));
                Assert.That(Context.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Open),
                    Is.EqualTo(1));
                Assert.That(Context.Remote.Calls.Count(
                    call => call.MethodId == MethodIds.PubSubConfigurationType_CloseAndUpdate), Is.EqualTo(1));
                Assert.That(Context.Remote.Calls[^1].MethodId,
                    Is.EqualTo(MethodIds.PubSubConfigurationType_CloseAndUpdate));
                Assert.That(RemoteModelTestSession.Payload(result)
                    .GetProperty("serverReportedChangesApplied").GetBoolean(), Is.True);
                AssertNoNetwork();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_uploaded.Dispose();
                try
                {
                    await Context.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            /// <summary>
            /// Handles only the real standard upload methods; all other remote mutations fail the test.
            /// </summary>
            private CallMethodResult Call(CallMethodRequest request)
            {
                Assert.That(request.ObjectId, Is.EqualTo(Context.Remote.Id("configuration")));
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    Assert.That(request.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(mode, Is.EqualTo(6));
                    return Good(Variant.From(17u));
                }
                Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
                Assert.That(handle, Is.EqualTo(17u));
                if (request.MethodId == MethodIds.FileType_Write)
                {
                    Assert.That(request.InputArguments[1].TryGetValue(out ByteString block), Is.True);
                    m_uploaded.Write(block.Span);
                    return Good();
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.PubSubConfigurationType_CloseAndUpdate));
                m_uploaded.Position = 0;
                Applied = UaPubSubConfigurationHelper.LoadConfiguration(m_uploaded, Context.Remote.Context.Telemetry);
                return Good(Variant.From(true), Variant.From(ArrayOf<StatusCode>.Empty),
                    Variant.From(ArrayOf<ExtensionObject>.Empty), Variant.From(ArrayOf<NodeId>.Empty));
            }

            /// <summary>
            /// Checks both production transport admission and actual DNS/socket seams.
            /// </summary>
            private void AssertNoNetwork()
            {
                Assert.That(m_network.Attempts, Is.Zero);
                Assert.That(Context.Transports.Factories, Is.Zero);
                Assert.That(Context.Transports.Created, Is.Empty);
            }

            /// <summary>
            /// Creates one successful standard method response with typed output arguments.
            /// </summary>
            private static CallMethodResult Good(params Variant[] values)
            {
                return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = values.ToArrayOf() };
            }

            private const string Session = PubSubExpansionTestContext.SessionName;
            private readonly ForbiddenNetwork m_network = new();
            private readonly MemoryStream m_uploaded = new();
            private byte[] m_artifact = [];
        }

        /// <summary>
        /// Rejects and records DNS or TCP use by a configuration-only operation.
        /// </summary>
        private sealed class ForbiddenNetwork : IMcpDnsResolver, IMcpTcpConnector
        {
            /// <summary>
            /// Gets the number of attempted network operations.
            /// </summary>
            internal int Attempts { get; private set; }

            /// <inheritdoc/>
            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                Attempts++;
                throw new InvalidOperationException("Remote configuration must not resolve broker addresses.");
            }

            /// <inheritdoc/>
            public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
            {
                Attempts++;
                throw new InvalidOperationException("Remote configuration must not open broker connections.");
            }
        }
    }
}
#endif
