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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    internal sealed class OpenUsdWorkflowTestContext
    {
        public OpenUsdWorkflowTestContext(string id = "primary", int maxFields = 256)
        {
            Server = new CellProviderTestSession(ModelUri, maxFields: maxFields);
            SessionId = new NodeId(id + "-session", Server.NamespaceIndex);
            Endpoint = new EndpointDescription
            {
                EndpointUrl = $"opc.tcp://localhost:4840/{id}",
                Server = new ApplicationDescription { ApplicationUri = $"urn:ualens:openusd:{id}" },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = ByteString.From([1, 2, 3])
            };
            Server.Session.SetupGet(session => session.Connected).Returns(() => Connected);
            Server.Session.SetupGet(session => session.SessionId).Returns(() => SessionId);
            Server.Session.SetupGet(session => session.Identity).Returns(() => Identity);
            Server.Session.SetupGet(session => session.Endpoint).Returns(() => Endpoint);
            Target = new CompanionTarget("openusd", new NodeId(id + "-representation", Server.NamespaceIndex),
                id, "OpenUsdRepresentation");
            Representation = new OpenUsdConnector.RepresentationInfo
            {
                NodeId = Target.NodeId,
                StageNodeId = new NodeId(id + "-stage", Server.NamespaceIndex),
                PrimPath = "/World/Cell",
                RootLayerIdentifier = "cell.usda",
                DigestAlgorithm = OpenUsdDigestAlgorithm.Sha256,
                RootLayerDigest = ByteString.From(new byte[32])
            };
            Binding = new OpenUsdConnector.BindingInfo
            {
                SourceNodeId = new NodeId(id + "-source", Server.NamespaceIndex),
                PrimPath = "/World/Cell",
                PropertyName = "temperature",
                Kind = OpenUsdRenderTargetKind.Custom,
                BindingDefinitionId = new Guid("81ed8a26-8f34-48d1-9005-75a69c7debe9"),
                Scale = 2,
                Offset = 1
            };
            Representation.Bindings.Add(Binding);
            Reader.Setup(value => value.DiscoverAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Discoveries++;
                    OnDiscover?.Invoke();
                    return Task.FromResult<ArrayOf<OpenUsdConnector.RepresentationInfo>>([Representation]);
                });
            Reader.Setup(value => value.ReadValueAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId node, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    ReadNodes.Add(node);
                    return ReadValue(node, token);
                });
            Reader.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                ReaderDisposals++;
                return ValueTask.CompletedTask;
            });
            Exporter.Setup(value => value.WriteAsync(It.IsAny<CompanionContext>(),
                It.IsAny<OpenUsdWorkflowTaskInput>(), It.IsAny<ArrayOf<OpenUsdWorkflowOrigin>>(),
                It.IsAny<ArrayOf<OpenUsdConnector.ComponentInfo>>(), It.IsAny<ArrayOf<OpenUsdWorkflowSample>>(),
                It.IsAny<CancellationToken>()))
                .Returns((CompanionContext _, OpenUsdWorkflowTaskInput _, ArrayOf<OpenUsdWorkflowOrigin> origins,
                    ArrayOf<OpenUsdConnector.ComponentInfo> _, ArrayOf<OpenUsdWorkflowSample> samples,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Exports++;
                    ExportedSamples = samples;
                    ExportedOrigins = origins;
                    return Task.CompletedTask;
                });
            Source.Setup(value => value.ObserveAsync(It.IsAny<ArrayOf<NodeId>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeId> _, CancellationToken _) => Changes);
            Source.Setup(value => value.ReadHistoryAsync(It.IsAny<NodeId>(), It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, DateTime _, DateTime _, uint _, CancellationToken _) => History);
            Tasks = new OpenUsdWorkflowTasks(CreateReader, _ => Source.Object, Exporter.Object, null);
        }

        public CellProviderTestSession Server { get; }

        public CompanionContext Context => Server.Context;

        public CompanionTarget Target { get; }

        public OpenUsdConnector.RepresentationInfo Representation { get; }

        public OpenUsdConnector.BindingInfo Binding { get; }

        public Mock<IOpenUsdCompanionReader> Reader { get; } = new(MockBehavior.Strict);

        public Mock<IOpenUsdWorkflowSource> Source { get; } = new(MockBehavior.Strict);

        public Mock<IOpenUsdWorkflowExporter> Exporter { get; } = new(MockBehavior.Strict);

        public OpenUsdWorkflowTasks Tasks { get; }

        public EndpointDescription Endpoint { get; }

        public bool Connected { get; set; } = true;

        public NodeId SessionId { get; set; }

        public IUserIdentity Identity { get; set; } = new UserIdentity();

        public Func<NodeId, CancellationToken, Task<DataValue>> ReadValue { get; set; } =
            static (_, _) => Task.FromResult(Value(Variant.From(3d)));

        public IAsyncEnumerable<OpenUsdWorkflowChange> Changes { get; set; } =
            new CellProviderTestEntries<OpenUsdWorkflowChange>([]);

        public IAsyncEnumerable<DataValue> History { get; set; } = new CellProviderTestEntries<DataValue>([]);

        public Action? OnDiscover { get; set; }

        public int Discoveries { get; private set; }

        public int ReaderDisposals { get; private set; }

        public int Exports { get; private set; }

        public ArrayOf<OpenUsdWorkflowSample> ExportedSamples { get; private set; }

        public ArrayOf<OpenUsdWorkflowOrigin> ExportedOrigins { get; private set; }

        public List<NodeId> ReadNodes { get; } = [];

        public IOpenUsdCompanionReader CreateReader(CompanionContext context, OpenUsdConnectorOptions options)
        {
            NUnit.Framework.Assert.That(context.Session, NUnit.Framework.Is.SameAs(Server.Session.Object));
            NUnit.Framework.Assert.That(options.EnableCommands, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(options.RemoteSessionFactory, NUnit.Framework.Is.Null);
            return Reader.Object;
        }

        public async Task<OpenUsdWorkflowTaskInput> PrepareAsync(
            string operation = "read-bindings", ArrayOf<CompanionValue> inputs = default)
        {
            CompanionTaskInput prepared = await Tasks.PrepareAsync(
                Context, Target, operation, inputs, CancellationToken.None).ConfigureAwait(false);
            return prepared as OpenUsdWorkflowTaskInput ??
                throw new NUnit.Framework.AssertionException("The workflow did not return its typed prepared input.");
        }

        public Task<CompanionOperationResult> ExecuteAsync(
            OpenUsdWorkflowTaskInput task, CancellationToken cancellationToken = default)
        {
            return Tasks.ExecuteAsync(
                Context, Target, task.OperationId, task, null, cancellationToken).AsTask();
        }

        public static DataValue Value(Variant value, int seconds = 0)
        {
            return new DataValue(value)
                .WithSourceTimestamp(Start.Add(TimeSpan.FromSeconds(seconds)))
                .WithServerTimestamp(Start.Add(TimeSpan.FromSeconds(seconds + 1)))
                .WithSourcePicoseconds(123)
                .WithServerPicoseconds(456);
        }

        public static ArrayOf<CompanionValue> HistoryInputs(int maximum, string? destination = null)
        {
            ArrayOf<CompanionValue> inputs =
            [
                new("start", Variant.From(Start.Add(TimeSpan.FromSeconds(-1)))),
                new("end", Variant.From(Start.Add(TimeSpan.FromHours(1)))),
                new("maxSamples", Variant.From((uint)maximum))
            ];
            return destination is null ? inputs : [.. inputs, new("destination", Variant.From(destination))];
        }

        public const string ModelUri = "http://opcfoundation.org/UA/OpenUSD/";
        public static readonly DateTimeUtc Start = new(2026, 9, 13, 12);
    }
}
