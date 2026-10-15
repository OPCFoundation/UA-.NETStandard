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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Programs;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Programs;
using Opc.Ua.Server.Programs;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.TestModels.DomainDownload;
using Model = Opc.Ua.TestModels.DomainDownload;
using ServerResult = Opc.Ua.Server.Programs.ProgramResultValue;

namespace Opc.Ua.Server.Tests.Programs
{
    /// <summary>
    /// Connects generated Program clients to a real UA server using an Annex A-style download model.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class ProgramIntegrationTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var definition = new ProgramDefinition(
                Model.ObjectTypeIds.DomainDownloadType,
                static parent => new DomainDownloadState(parent),
                new DelegateProgramProvider(static (_, _) =>
                    new ValueTask<IProgramExecution>(new DownloadExecution())),
                [ProgramControlMethod.Start, ProgramControlMethod.Suspend,
                    ProgramControlMethod.Resume, ProgramControlMethod.Halt],
                ProgramState.Ready, 4, 0)
            {
                Creatable = true,
                Deletable = true
            };
            m_creationAdapter = new NamedCreationAdapter();
            var named = new ProgramDefinition(Model.ObjectTypeIds.NamedProgramType,
                static parent => new NamedProgramState(parent),
                new DelegateProgramProvider((context, _) =>
                {
                    m_creationArguments = context.CreationArguments;
                    m_namedExecution = new DownloadExecution();
                    return new ValueTask<IProgramExecution>(m_namedExecution);
                }),
                [ProgramControlMethod.Start, ProgramControlMethod.Halt], ProgramState.Ready, 2, 0)
            {
                Creatable = true,
                Deletable = true,
                CreationAdapter = m_creationAdapter
            };
            m_factory = new CapturingFactory(new ProgramNodeManagerFactory(
                [definition, named],
                modelLoader: static context => new NodeStateCollection().AddOpcUaTestModelsDomainDownload(context)));
            m_serverFixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false
            };
            m_storeRoot = Path.Combine(Path.GetTempPath(), "ua-programs-" + Guid.NewGuid().ToString("N"));
            await m_serverFixture.LoadConfigurationAsync(m_storeRoot).ConfigureAwait(false);
            await m_serverFixture.StartAsync().ConfigureAwait(false);
            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            m_clientFixture.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await m_clientFixture.LoadClientConfigurationAsync(m_storeRoot).ConfigureAwait(false);
            m_session = await m_clientFixture.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_serverFixture.Port}"), SecurityPolicies.None).ConfigureAwait(false);
            m_programs = new ProgramClient(m_session, m_telemetry);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session is not null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                    m_session.Dispose();
                }
            }
            finally
            {
                try
                {
                    if (m_serverFixture is not null)
                    {
                        await m_serverFixture.StopAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_clientFixture?.Dispose();
                    if (m_storeRoot is not null && Directory.Exists(m_storeRoot))
                    {
                        Directory.Delete(m_storeRoot, true);
                    }
                }
            }
        }

        [Test]
        public async Task DownloadEventsRetainProgressAndTerminalResultsOverTheWireAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            (ProgramRuntime runtime, DomainDownloadTypeClient proxy) =
                await CreateInvocationAsync("Download", deadline.Token).ConfigureAwait(false);
            await using var streaming = new StreamingSubscription(m_session.RequireSubscriptionManager());
            await using IAsyncEnumerator<ProgramTransitionEventTypeRecord> events =
                proxy.ObserveProgramTransitionsAsync(
                    streaming, global::Opc.Ua.ObjectIds.Server,
                    onReady: ct => proxy.StartAsync("Domain", "memory:source", "memory:destination", ct),
                    ct: deadline.Token).GetAsyncEnumerator(deadline.Token);
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.ToStateId.TryGetValue(out NodeId running), Is.True);
            Assert.That(running, Is.EqualTo(global::Opc.Ua.ObjectIds.ProgramStateMachineType_Running));
            var node = (DomainDownloadState)runtime.Node;
            NodeId transfer = node.TransferStateMachine!.NodeId;
            await ReportAsync(runtime, transfer, Model.Objects.TransferStateMachineType_OpeningToSending, 0)
                .ConfigureAwait(false);
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            await ReportAsync(runtime, transfer, Model.Objects.TransferStateMachineType_SendingToSending, 32)
                .ConfigureAwait(false);
            await ReportAsync(runtime, transfer, Model.Objects.TransferStateMachineType_SendingToSending, 64)
                .ConfigureAwait(false);
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.IntermediateResult.TryGetValue(out int first), Is.True);
            Assert.That(first, Is.EqualTo(32));
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.IntermediateResult.TryGetValue(out int second), Is.True);
            Assert.That(second, Is.EqualTo(64));

            ushort ns = m_session.MessageContext.NamespaceUris.GetIndexOrAppend(Model.Namespaces.DomainDownloadModel);
            ArrayOf<ServerResult> results =
            [
                new([new QualifiedName("BytesTransferred", ns)], new DataValue(Variant.From(64u))),
                new([new QualifiedName("Completed", ns)], new DataValue(Variant.From(true))),
                new([new QualifiedName("ErrorCode", ns)], new DataValue(Variant.From(StatusCodes.Good)))
            ];
            ServiceResult completion = await runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(runtime.RunId, ProgramState.Halted,
                    Variant.From(64), results), deadline.Token).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(completion), Is.True);
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.ToStateId.TryGetValue(out NodeId halted), Is.True);
            Assert.That(halted, Is.EqualTo(global::Opc.Ua.ObjectIds.ProgramStateMachineType_Halted));

            ProgramFinalResults retained = await m_programs.ReadFinalResultsAsync(runtime.Node.NodeId, deadline.Token)
                .ConfigureAwait(false);
            Assert.That(retained.IsAvailable, Is.True);
            Assert.That(retained.Values, Has.Count.EqualTo(3));
            Client.Programs.ProgramResultValue bytes = retained.Values.ToArray()!
                .Single(value => value.BrowsePath[^1].Name == "BytesTransferred");
            Assert.That(bytes.Value.WrappedValue.TryGetValue(out uint count), Is.True);
            Assert.That(count, Is.EqualTo(64u));
            await m_programs.DeleteAsync(runtime.Node.NodeId, deadline.Token).ConfigureAwait(false);
            Assert.That(m_factory.Manager!.GetInstanceCount(Model.ObjectTypeIds.DomainDownloadType), Is.Zero);
        }

        [Test]
        public async Task SuspendResumePreservesTheNestedStateAndDoesNotRecycleAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            (ProgramRuntime runtime, DomainDownloadTypeClient proxy) =
                await CreateInvocationAsync("Suspend", deadline.Token).ConfigureAwait(false);
            var node = (DomainDownloadState)runtime.Node;
            Assert.That(node.TransferStateMachine!.CurrentState!.StatusCode,
                Is.EqualTo(StatusCodes.BadStateNotActive));
            await proxy.StartAsync("Domain", "memory:source", "memory:destination", deadline.Token)
                .ConfigureAwait(false);
            await ReportAsync(runtime, node.TransferStateMachine.NodeId,
                Model.Objects.TransferStateMachineType_OpeningToSending, 0).ConfigureAwait(false);
            NodeId beforeSuspend = node.TransferStateMachine.CurrentState.Id!.Value;
            await proxy.SuspendAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(node.TransferStateMachine.CurrentState.StatusCode, Is.EqualTo(StatusCodes.BadStateNotActive));
            ServiceResult inactive = await runtime.ApplyUpdateAsync(new ProgramExecutionUpdate(
                runtime.RunId, ProgramState.Running, SubStateMachineId: node.TransferStateMachine.NodeId,
                SubStateTransitionId: Model.Objects.TransferStateMachineType_SendingToSending)).ConfigureAwait(false);
            Assert.That(inactive.StatusCode, Is.EqualTo(StatusCodes.BadStateNotActive));
            await proxy.ResumeAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(node.TransferStateMachine.CurrentState.Id.Value, Is.EqualTo(beforeSuspend));
            Assert.That(node.TransferStateMachine.CurrentState.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(node.RecycleCount!.Value, Is.Zero);
            await proxy.HaltAsync(deadline.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task InvalidControlArgumentsAndUnencryptedDiagnosticsAreRejectedAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            (ProgramRuntime runtime, _) = await CreateInvocationAsync("Validation", deadline.Token)
                .ConfigureAwait(false);
            CallMethodResult error = await m_programs.CallWithResultAsync(
                runtime.Node.NodeId, Model.MethodIds.DomainDownloadType_Start, default, deadline.Token)
                .ConfigureAwait(false);
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
            Assert.That(runtime.CurrentState, Is.EqualTo(ProgramState.Ready));
            ProgramDiagnosticResult diagnostic = await m_programs.ReadDiagnosticsAsync(
                runtime.Node.NodeId, deadline.Token).ConfigureAwait(false);
            Assert.That(diagnostic.Value.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(diagnostic.Diagnostic, Is.Null);
        }

        [Test]
        public async Task VendorCreateSignaturesUseTheSameLifetimeRegistryAsync()
        {
            NodeId typeId = ExpandedNodeId.ToNodeId(
                Model.ObjectTypeIds.NamedProgramType, m_session.MessageContext.NamespaceUris);
            var proxy = new NamedProgramTypeClient(m_session, typeId, m_telemetry);
            (NodeId invocation, string label) = await proxy.CreateAsync("Named").ConfigureAwait(false);
            Assert.That(label, Is.EqualTo("Named"));
            Assert.That(m_creationArguments.Count, Is.EqualTo(1));
            Assert.That(m_creationArguments[0].TryGetValue(out string? argument), Is.True);
            Assert.That(argument, Is.EqualTo("Named"));
            Assert.That(m_factory.Manager!.TryGetRuntime(invocation, out ProgramRuntime? runtime), Is.True);
            Assert.That(runtime!.CurrentState, Is.EqualTo(ProgramState.Ready));
            Assert.That(m_factory.Manager.GetInstanceCount(Model.ObjectTypeIds.NamedProgramType), Is.EqualTo(1));
        }

        [Test]
        public void InvalidVendorCreateOutputsRollBackTheInvocation()
        {
            m_creationAdapter.InvalidOutput = true;
            NodeId typeId = ExpandedNodeId.ToNodeId(
                Model.ObjectTypeIds.NamedProgramType, m_session.MessageContext.NamespaceUris);
            var proxy = new NamedProgramTypeClient(m_session, typeId, m_telemetry);
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.CreateAsync("Rejected").ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(m_factory.Manager!.GetInstanceCount(Model.ObjectTypeIds.NamedProgramType), Is.Zero);
            Assert.That(m_namedExecution!.Disposed, Is.True);
        }

        [Test]
        public async Task DiagnosticsAreAvailableToTheirEncryptedCreatorButNotAnotherSessionAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using Client.ISession owner = await m_clientFixture.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_serverFixture.Port}"), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var programs = new ProgramClient(owner, m_telemetry);
            ProgramNodeManager manager = m_factory.Manager!;
            NodeId typeId = ExpandedNodeId.ToNodeId(
                Model.ObjectTypeIds.DomainDownloadType, owner.MessageContext.NamespaceUris);
            NodeId id = await programs.CreateAsync(typeId, manager.RootId,
                new QualifiedName("PrivateDiagnostic", manager.NamespaceIndex), deadline.Token).ConfigureAwait(false);
            var proxy = new DomainDownloadTypeClient(owner, id, m_telemetry);
            await proxy.StartAsync("Domain", "memory:source", "memory:destination", deadline.Token)
                .ConfigureAwait(false);
            ProgramDiagnosticResult diagnostic = await programs.ReadDiagnosticsAsync(id, deadline.Token)
                .ConfigureAwait(false);
            Assert.That(diagnostic.Value.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(diagnostic.Diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Diagnostic!.CreateSessionId, Is.EqualTo(owner.SessionId));
            Assert.That(diagnostic.Diagnostic.LastMethodSessionId, Is.EqualTo(owner.SessionId));
            Assert.That(diagnostic.Diagnostic.LastMethodInputValues.Count, Is.EqualTo(3));
            using Client.ISession other = await m_clientFixture.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_serverFixture.Port}"), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var otherPrograms = new ProgramClient(other, m_telemetry);
            ProgramDiagnosticResult denied = await otherPrograms.ReadDiagnosticsAsync(id, deadline.Token)
                .ConfigureAwait(false);
            Assert.That(denied.Value.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await other.CloseAsync().ConfigureAwait(false);
            await owner.CloseAsync().ConfigureAwait(false);
        }

        private async Task<(ProgramRuntime Runtime, DomainDownloadTypeClient Proxy)> CreateInvocationAsync(
            string name, CancellationToken cancellationToken)
        {
            ProgramNodeManager manager = m_factory.Manager!;
            NodeId typeId = ExpandedNodeId.ToNodeId(
                Model.ObjectTypeIds.DomainDownloadType, m_session.MessageContext.NamespaceUris);
            NodeId id = await m_programs.CreateAsync(typeId, manager.RootId,
                new QualifiedName(name, manager.NamespaceIndex), cancellationToken).ConfigureAwait(false);
            Assert.That(manager.TryGetRuntime(id, out ProgramRuntime? runtime), Is.True);
            return (runtime!, new DomainDownloadTypeClient(m_session, id, m_telemetry));
        }

        private static async Task ReportAsync(ProgramRuntime runtime, NodeId machine, uint transition, int progress)
        {
            ServiceResult result = await runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(runtime.RunId, ProgramState.Running, Variant.From(progress),
                    SubStateMachineId: machine, SubStateTransitionId: transition)).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        private sealed class CapturingFactory(ProgramNodeManagerFactory inner) : IAsyncNodeManagerFactory
        {
            public ProgramNodeManager? Manager { get; private set; }

            public ArrayOf<string> NamespacesUris => inner.NamespacesUris;

            public async ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                IAsyncNodeManager manager = await inner.CreateAsync(server, configuration, cancellationToken)
                    .ConfigureAwait(false);
                Manager = (ProgramNodeManager)manager;
                return manager;
            }
        }

        private sealed class DownloadExecution : IProgramExecution
        {
            public bool Disposed { get; private set; }

            public ValueTask<ProgramControlResult> ControlAsync(
                ProgramControlContext context, CancellationToken cancellationToken = default)
            {
                return new ValueTask<ProgramControlResult>(new ProgramControlResult(ServiceResult.Good, default));
            }

            public async IAsyncEnumerable<ProgramExecutionUpdate> ObserveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await foreach (ProgramExecutionUpdate update in
                    m_updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return update;
                }
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                m_updates.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ProgramExecutionUpdate> m_updates =
                Channel.CreateUnbounded<ProgramExecutionUpdate>();
        }

        private sealed class NamedCreationAdapter : IProgramCreationAdapter
        {
            public bool InvalidOutput { get; set; }

            public ValueTask<ProgramCreationRequest> ParseAsync(
                ProgramCreationContext context, ArrayOf<Variant> inputArguments,
                CancellationToken cancellationToken = default)
            {
                if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                return new ValueTask<ProgramCreationRequest>(new ProgramCreationRequest(
                    context.RootId, new QualifiedName(name, context.NamespaceIndex), inputArguments));
            }

            public ValueTask<ArrayOf<Variant>> CompleteAsync(
                NodeId invocationId, ArrayOf<Variant> inputArguments, CancellationToken cancellationToken = default)
            {
                ArrayOf<Variant> outputs =
                [
                    InvalidOutput ? Variant.From("not a NodeId") : Variant.From(invocationId),
                    inputArguments[0]
                ];
                return new ValueTask<ArrayOf<Variant>>(outputs);
            }
        }

        private ServerFixture<StandardServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private Client.ISession m_session = null!;
        private CapturingFactory m_factory = null!;
        private ProgramClient m_programs = null!;
        private ITelemetryContext m_telemetry = null!;
        private string m_storeRoot = null!;
        private NamedCreationAdapter m_creationAdapter = null!;
        private DownloadExecution? m_namedExecution;
        private ArrayOf<Variant> m_creationArguments;
    }
}
