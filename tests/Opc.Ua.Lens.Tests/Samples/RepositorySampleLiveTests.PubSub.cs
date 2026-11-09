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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using UaLens.Connection;
using UaLens.Diagnostics.Capture;
using UaLens.Plugins.PubSub;
using UaLens.Tests.Companions;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        [Test]
        [Explicit("Hosts a private secure UA server and an owned UDP structured source/write-back loop.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivatePubSubAdapterPreservesMatricesStructuresAndNamespacesThroughWriteBackAsync()
        {
            string root = OpenUsdTestPaths.NewDestination();
            string serverPki = Path.Combine(root, "server");
            Directory.CreateDirectory(root);
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            string endpointUrl = $"opc.tcp://localhost:{number}/UaLensOwnedCompanion";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var started = new OwnedCompanionStartup();
            try
            {
                using IHost host = CreatePrivateCompanionHost(false, serverPki, endpointUrl, started,
                    server => server.AddNodeManager(kPubSubModel, ConfigurePubSubNodes));
                try
                {
                    await host.StartAsync(deadline.Token).ConfigureAwait(false);
                    await started.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
                    ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
                    var backend = new StackConnectionBackend(telemetry,
                        token => AppConfig.BuildAsync(telemetry, Path.Combine(root, "client"), token));
                    await using (backend.ConfigureAwait(false))
                    {
                        var connection = new ConnectionService(
                            telemetry, null, backend, new ProfileCredentialProvider());
                        await using (connection.ConfigureAwait(false))
                        {
                            ApplicationConfiguration client = await connection.GetConfigAsync(deadline.Token)
                                .ConfigureAwait(false);
                            ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                                client, endpointUrl, deadline.Token).ConfigureAwait(false);
                            EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                                candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                                candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                            using ICertificateStore own = new CertificateStoreIdentifier(
                                serverPki, CertificateStoreType.Directory).OpenStore(telemetry);
                            using CertificateCollection certificates = await own.EnumerateAsync(deadline.Token)
                                .ConfigureAwait(false);
                            Assert.That(certificates.Any(certificate =>
                                certificate.RawData.AsSpan().SequenceEqual(endpoint.ServerCertificate.Span)), Is.True);
                            await TrustOwnedPeersAsync(client, endpoint, serverPki, telemetry, deadline.Token)
                                .ConfigureAwait(false);
                            var adapter = new ServerSession(new ServerConnectionOptions
                            {
                                EndpointUrl = endpointUrl,
                                ApplicationConfiguration = client,
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                            }, telemetry);
                            await using (adapter.ConfigureAwait(false))
                            {
                                await adapter.ConnectAsync(deadline.Token).ConfigureAwait(false);
                                await ExerciseStructuredPubSubAdapterAsync(adapter, telemetry, deadline.Token)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task ExerciseStructuredPubSubAdapterAsync(
            ServerSession adapter, ITelemetryContext telemetry, CancellationToken token)
        {
            ArrayOf<DataValue> namespaceValues = await adapter.ReadAsync(
                [new ReadValueId { NodeId = VariableIds.Server_NamespaceArray, AttributeId = Attributes.Value }], token)
                .ConfigureAwait(false);
            Assert.That(namespaceValues[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(namespaceValues[0].WrappedValue.TryGetValue(out ArrayOf<string> namespaces), Is.True);
            var remote = PubSubFieldSchema.CreateContext(telemetry);
            remote.NamespaceUris = new NamespaceTable(namespaces.ToList());
            ushort remoteIndex = checked((ushort)remote.NamespaceUris.GetIndex(kPubSubModel));
            Assert.That(remoteIndex, Is.GreaterThan(1));
            int udpPort;
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                udpPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
            PubSubConfiguration configuration = PubSubAdapterConfiguration() with
            {
                Endpoint = $"opc.udp://127.0.0.1:{udpPort}",
                NetworkInterface = "127.0.0.1",
                SecurityMode = MessageSecurityMode.None,
                Publication = PubSubPublication.ServerSource,
                AdapterProviderId = "owned-structured",
                WriteBackEnabled = true,
                PublisherFilter = 2,
                MaxPublishedMessages = 1,
                DurationSeconds = 5,
                PublishingIntervalMs = 250
            };
            var provider = new ConfiguredPubSubAdapterProvider(configuration.AdapterProviderId, false, (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new PubSubAdapterLease(adapter, (id, cancellation) =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(ExpandedNodeId.ToNodeId(id, remote.NamespaceUris));
                }, ownsSession: false, messageContext: remote));
            });
            ArrayOf<ReadValueId> targetReads = configuration.Fields.ConvertAll(field => new ReadValueId
            {
                NodeId = PubSubStructureSchema.Resolve(field.TargetNodeId, remote.NamespaceUris),
                AttributeId = Attributes.Value
            });
            ArrayOf<DataValue> before = await adapter.ReadAsync(targetReads, token).ConfigureAwait(false);
            Assert.That(before[0].WrappedValue.TryGetValue(out MatrixOf<int> initial), Is.True);
            Assert.That(initial.Span.ToArray(), Is.All.Zero);
            var captures = new DiagnosticCaptureWorkspace(telemetry);
            var factory = new PubSubRuntimeFactory(telemetry, adapterProviders: [provider], captures: captures);
            var receiver = new PubSubWorkspace(factory, telemetry);
            var publisher = new PubSubWorkspace(factory, telemetry);
            await using (captures.ConfigureAwait(false))
            await using (receiver.ConfigureAwait(false))
            await using (publisher.ConfigureAwait(false))
            {
                await receiver.ConfigureAsync(configuration with { Publication = PubSubPublication.Disabled }, token)
                    .ConfigureAwait(false);
                await publisher.ConfigureAsync(
                    configuration with { ReceiveEnabled = false, WriteBackEnabled = false }, token)
                    .ConfigureAwait(false);
                await receiver.StartAsync(new PubSubStartAuthorization(
                    AllowUnsecured: true, AllowWriteBack: true), token).ConfigureAwait(false);
                DiagnosticCaptureSource captureSource = captures.Sources.ToList().Single();
                await captures.StartAsync(captureSource.Id, new DiagnosticCaptureLimits(5, 128), true, token)
                    .ConfigureAwait(false);
                await publisher.StartAsync(new PubSubStartAuthorization(
                    AllowUnsecured: true, AllowPublication: true), token).ConfigureAwait(false);
                PubSubWorkspaceSnapshot snapshot;
                do
                {
                    await Task.Delay(25, token).ConfigureAwait(false);
                    snapshot = receiver.Snapshot();
                }
                while (snapshot.Observations.AcceptedDataSets == 0 &&
                    snapshot.Phase is PubSubDocumentPhase.Running or PubSubDocumentPhase.Starting);
                await receiver.StopAsync().ConfigureAwait(false);
                await publisher.StopAsync().ConfigureAwait(false);
                snapshot = receiver.Snapshot();
                PubSubWorkspaceSnapshot published = publisher.Snapshot();
                string evidence = snapshot.Status +
                    " " +
                    string.Join("; ", snapshot.Observations.Evidence.ToList()) +
                    " Publisher: " +
                    string.Join("; ", published.Observations.Evidence.ToList());
                Assert.That(snapshot.Phase, Is.EqualTo(PubSubDocumentPhase.Offline), evidence);
                Assert.That(published.SourceSamples, Is.EqualTo(1), evidence);
                Assert.That(snapshot.Observations.AcceptedDataSets, Is.EqualTo(1), evidence);
                Assert.That(snapshot.Observations.RejectedDataSets, Is.Zero, evidence);
                DiagnosticCaptureSnapshot capture = captures.Snapshot();
                Assert.That(capture.Phase, Is.EqualTo(DiagnosticCapturePhase.Completed), capture.Status);
                Assert.That(capture.Kind, Is.EqualTo(DiagnosticCaptureKind.PubSub));
                Assert.That(capture.Frames, Is.GreaterThan(0));
                Assert.That(capture.Packets.Count, Is.EqualTo(capture.Frames));
                Assert.That(captures.Sources.IsEmpty, Is.True, "Stopped runtimes must remove their capture targets.");
                Assert.That(snapshot.Observations.Values[2].Value.TryGetValue(out ArrayOf<NodeId> observed), Is.True);
                Assert.That(observed, Is.EqualTo((ArrayOf<NodeId>)[new NodeId(7001u, 1), new NodeId(8001u, 1)]));
            }
            Assert.That(adapter.IsConnected, Is.True,
                "Disposing the document must not dispose its borrowed UA adapter.");
            ArrayOf<DataValue> after = await adapter.ReadAsync(targetReads, token).ConfigureAwait(false);
            Assert.That(after.ToList().Select(value => value.StatusCode), Is.All.EqualTo(StatusCodes.Good));
            Assert.That(after[0].WrappedValue.TryGetValue(out MatrixOf<int> matrix), Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo((ArrayOf<int>)[2, 3]));
            int[] expected = [10, 20, 30, 40, 50, 60];
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(expected));
            var remoteTypes = new PubSubConfiguredValueService(
                configuration, telemetry, namespaces: remote.NamespaceUris);
            Assert.That(after[1].WrappedValue.TryGetValue<IEncodeable>(
                out IEncodeable? body, remoteTypes.MessageContext), Is.True);
            Assert.That(((IStructure)body!)["Temperature"], Is.EqualTo(Variant.From(17.25)));
            Assert.That(((IStructure)body)["Source"], Is.EqualTo(Variant.From(new NodeId(7001u, remoteIndex))));
            Assert.That(after[2].WrappedValue.TryGetValue(out ArrayOf<NodeId> references), Is.True);
            Assert.That(references,
                Is.EqualTo((ArrayOf<NodeId>)[new NodeId(7001u, remoteIndex), new NodeId(8001u, remoteIndex)]));
        }

        private static void ConfigurePubSubNodes(INodeManagerBuilder builder)
        {
            PubSubConfiguration configuration = PubSubAdapterConfiguration();
            ushort index = builder.DefaultNamespaceIndex;
            var values = new PubSubConfiguredValueService(configuration, builder.Context.Telemetry,
                namespaces: builder.Context.NamespaceUris);
            var typeId = new NodeId(5001u, index);
            var encodingId = new NodeId(5002u, index);
            Assert.That(values.MessageContext.Factory.TryGetEncodeableType(
                new ExpandedNodeId(typeId), out IEncodeableType? adapter), Is.True);
            builder.Context.EncodeableFactory.Builder.AddEncodeableType(adapter!)
                .AddEncodeableType(new ExpandedNodeId(typeId), adapter!)
                .AddEncodeableType(new ExpandedNodeId(encodingId), adapter!).Commit();
            var definition = new DataTypeState
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("ReadingType", index),
                DisplayName = new LocalizedText("ReadingType"),
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(
                    PubSubStructureSchema.Definition(configuration.Structures[0], builder.Context.NamespaceUris))
            };
            definition.AddReference(ReferenceTypeIds.HasEncoding, false, encodingId);
            builder.AddRoot(definition);
            var encoding = new BaseObjectState(null)
            {
                NodeId = encodingId,
                BrowseName = new QualifiedName(BrowseNames.DefaultBinary),
                DisplayName = new LocalizedText(BrowseNames.DefaultBinary),
                TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType
            };
            encoding.AddReference(ReferenceTypeIds.HasEncoding, true, typeId);
            builder.AddRoot(encoding);
            IEncodeable body = adapter!.CreateInstance();
            var structure = (IStructure)body;
            structure["Temperature"] = Variant.From(17.25);
            structure["Source"] = Variant.From(new NodeId(7001u, index));
            int[] elements = [10, 20, 30, 40, 50, 60];
            Variant[] sources =
            [
                Variant.From(elements.ToMatrixOf([2, 3])),
                Variant.FromStructure(body),
                Variant.From((ArrayOf<NodeId>)[new NodeId(7001u, index), new NodeId(8001u, index)])
            ];
            for (int fieldIndex = 0; fieldIndex < configuration.Fields.Count; fieldIndex++)
            {
                PubSubFieldConfiguration field = configuration.Fields[fieldIndex];
                Add(field.SourceNodeId, sources[fieldIndex], "Source" + fieldIndex);
                Add(field.TargetNodeId, fieldIndex == 0
                    ? Variant.From(new int[6].ToMatrixOf([2, 3])) : Variant.Null, "Target" + fieldIndex);

                void Add(string identifier, Variant value, string name)
                {
                    builder.Add(new BaseDataVariableState(null)
                    {
                        NodeId = PubSubStructureSchema.Resolve(identifier, builder.Context.NamespaceUris),
                        BrowseName = new QualifiedName(name, index),
                        DisplayName = new LocalizedText(name),
                        TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                        DataType = PubSubStructureSchema.FieldType(field, builder.Context.NamespaceUris),
                        ValueRank = field.ValueRank,
                        ArrayDimensions = field.ArrayDimensions,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = value,
                        StatusCode = StatusCodes.Good
                    });
                }
            }
        }

        private static PubSubConfiguration PubSubAdapterConfiguration()
        {
            return new PubSubConfiguration
            {
                Namespaces = [Namespaces.OpcUa, kPubSubModel],
                Fields =
                [
                    new() { Name = "Samples", Type = BuiltInType.Int32, ValueRank = 2, ArrayDimensions = [2u, 3u],
                        SourceNodeId = "nsu=" + kPubSubModel + ";i=7001",
                        TargetNodeId = "nsu=" + kPubSubModel + ";i=7002" },
                    new() { Name = "Reading", Type = BuiltInType.ExtensionObject,
                        DataTypeId = "nsu=" + kPubSubModel + ";i=5001",
                        SourceNodeId = "nsu=" + kPubSubModel + ";i=8001",
                        TargetNodeId = "nsu=" + kPubSubModel + ";i=8002" },
                    new() { Name = "References", Type = BuiltInType.NodeId, ValueRank = 1, ArrayDimensions = [2u],
                        SourceNodeId = "nsu=" + kPubSubModel + ";i=9001",
                        TargetNodeId = "nsu=" + kPubSubModel + ";i=9002" }
                ],
                Structures = [new()
                {
                    Name = "ReadingType", DataTypeId = "nsu=" + kPubSubModel + ";i=5001",
                    BinaryEncodingId = "nsu=" + kPubSubModel + ";i=5002",
                    Fields = [new() { Name = "Temperature", DataTypeId = "i=11" },
                        new() { Name = "Source", DataTypeId = "i=17" }]
                }]
            };
        }

        private const string kPubSubModel = "urn:ualens:test:live-pubsub";
    }
}
