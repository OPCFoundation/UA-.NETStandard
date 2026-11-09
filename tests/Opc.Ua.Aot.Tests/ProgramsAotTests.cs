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

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Opc.Ua.Client;
using Opc.Ua.Programs;
using Opc.Ua.Server.Programs;
using Opc.Ua.TestModels.DomainDownload;
using Model = Opc.Ua.TestModels.DomainDownload;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises generated Program subtypes, execution, diagnostics and event decoding after NativeAOT publishing.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class ProgramsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task GeneratedProgramExecutesAndPreservesNestedStateAsync()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(global::Opc.Ua.Programs.Namespaces.OpcUaPrograms);
            ushort modelNamespace = namespaces.GetIndexOrAppend(Model.Namespaces.DomainDownloadModel);
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = namespaces,
                TypeTable = new TypeTable(namespaces)
            };
            var node = new DomainDownloadState(null);
            node.Create(context, new NodeId("NativeProgram", modelNamespace),
                new QualifiedName("NativeProgram", modelNamespace), default, false);
            var definition = new ProgramDefinition(Model.ObjectTypeIds.DomainDownloadType,
                static parent => new DomainDownloadState(parent),
                new DelegateProgramProvider(static (_, _) =>
                    new ValueTask<IProgramExecution>(new NativeExecution())),
                [ProgramControlMethod.Start, ProgramControlMethod.Suspend,
                    ProgramControlMethod.Resume, ProgramControlMethod.Halt],
                ProgramState.Ready, 1, 0);
            await using ProgramRuntime runtime = await ProgramRuntime.AttachAsync(
                node, definition, context, TimeProvider.System).ConfigureAwait(false);
            MethodInvocationResult start = await runtime.ControlAsync(context, ProgramControlMethod.Start,
                [Variant.From("Domain"), Variant.From("memory:source"), Variant.From("memory:target")])
                .ConfigureAwait(false);
            await Assert.That(start.OperationResult.StatusCode).IsEqualTo(StatusCodes.Good);
            ServiceResult progress = await runtime.ApplyUpdateAsync(
                new ProgramExecutionUpdate(runtime.RunId, ProgramState.Running, Variant.From(50),
                    SubStateMachineId: node.TransferStateMachine!.NodeId,
                    SubStateTransitionId: Model.Objects.TransferStateMachineType_OpeningToSending))
                .ConfigureAwait(false);
            await Assert.That(progress.StatusCode).IsEqualTo(StatusCodes.Good);
            NodeId sending = node.TransferStateMachine.CurrentState!.Id!.Value;
            await runtime.ControlAsync(context, ProgramControlMethod.Suspend).ConfigureAwait(false);
            await Assert.That(node.TransferStateMachine.CurrentState.StatusCode)
                .IsEqualTo(StatusCodes.BadStateNotActive);
            await runtime.ControlAsync(context, ProgramControlMethod.Resume).ConfigureAwait(false);
            await Assert.That(node.TransferStateMachine.CurrentState.Id.Value).IsEqualTo(sending);
            await Assert.That(node.RecycleCount!.Value).IsEqualTo(0);
            await Assert.That(node.ProgramDiagnostic!.Value.LastMethodCall).IsEqualTo("Resume");
            await runtime.ControlAsync(context, ProgramControlMethod.Halt).ConfigureAwait(false);
            await Assert.That(runtime.CurrentState).IsEqualTo(ProgramState.Halted);
        }

        [Test]
        public async Task ConcreteProgramEventsDecodeCapturedIdentifiersAsync()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(global::Opc.Ua.Programs.Namespaces.OpcUaPrograms);
            var registry = new EventRecordDecoderRegistry();
            registry.RegisterOpcUaProgramsDecoders(namespaces);
            QualifiedName[][] paths = registry.StandardFields;
            var fields = new Variant[paths.Length];
            for (int index = 0; index < paths.Length; index++)
            {
                QualifiedName[] path = paths[index];
                if (path.Length == 1 && path[0].Name == "EventType")
                {
                    fields[index] = ExpandedNodeId.ToNodeId(
                        global::Opc.Ua.Programs.ObjectTypeIds.ManagedProgramTransitionEventType, namespaces);
                }
                else if (path.Length == 2 && path[0].Name == "ToState" && path[1].Name == "Id")
                {
                    fields[index] = ProgramStateMachineModel.GetStateId(ProgramState.Running);
                }
                else if (path.Length == 1 && path[0].Name == "IntermediateResult")
                {
                    fields[index] = Variant.From(50);
                }
            }
            EventRecord? decoded = registry.Decode(fields);
            await Assert.That(decoded is ProgramTransitionEventTypeRecord).IsTrue();
            var transition = (ProgramTransitionEventTypeRecord)decoded!;
            await Assert.That(transition.ToStateId.TryGetValue(out NodeId state)).IsTrue();
            await Assert.That(state).IsEqualTo(ProgramStateMachineModel.GetStateId(ProgramState.Running));
            await Assert.That(transition.IntermediateResult.TryGetValue(out int percentage)).IsTrue();
            await Assert.That(percentage).IsEqualTo(50);
        }

        private sealed class NativeExecution : IProgramExecution
        {
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
                m_updates.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ProgramExecutionUpdate> m_updates =
                Channel.CreateUnbounded<ProgramExecutionUpdate>();
        }
    }
}
