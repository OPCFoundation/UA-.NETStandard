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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Programs;
using Opc.Ua.Tests;
using ProgramModelIds = Opc.Ua.Programs.ObjectTypeIds;
using ProgramNamespaces = Opc.Ua.Programs.Namespaces;

namespace Opc.Ua.Core.Tests.Programs
{
    /// <summary>
    /// Verifies the standard Program identifiers and generated SDK information model.
    /// </summary>
    [TestFixture]
    [Category("Programs")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ProgramModelTests
    {
        /// <summary>
        /// Runtime state numbers agree with the namespace-zero model.
        /// </summary>
        [TestCase(ProgramState.Halted, Objects.ProgramStateMachineType_Halted, 11u)]
        [TestCase(ProgramState.Ready, Objects.ProgramStateMachineType_Ready, 12u)]
        [TestCase(ProgramState.Running, Objects.ProgramStateMachineType_Running, 13u)]
        [TestCase(ProgramState.Suspended, Objects.ProgramStateMachineType_Suspended, 14u)]
        public void StateMappingsAndRuntimeNumbersMatchStandard(ProgramState state, uint id, uint number)
        {
            SystemContext context = CreateContext();
            var machine = new ProgramStateMachineState(null);
            machine.Create(context, new NodeId("Invocation", 2), new QualifiedName("Invocation", 2), default, false);
            machine.SetState(context, id);

            Assert.Multiple(() =>
            {
                Assert.That(ProgramStateMachineModel.GetStateId(state), Is.EqualTo(new NodeId(id)));
                Assert.That(ProgramStateMachineModel.TryGetState(new NodeId(id), out ProgramState actual), Is.True);
                Assert.That(actual, Is.EqualTo(state));
                Assert.That((uint)state, Is.EqualTo(number));
                Assert.That(machine.CurrentState!.Number!.Value, Is.EqualTo(number));
                Assert.That(machine.CurrentState.Id!.Value, Is.EqualTo(new NodeId(id)));
            });
        }

        /// <summary>
        /// A generated subtype's element namespace must not relocate inherited standard Program states.
        /// </summary>
        [TestCase(ProgramState.Halted)]
        [TestCase(ProgramState.Ready)]
        [TestCase(ProgramState.Running)]
        [TestCase(ProgramState.Suspended)]
        public void GeneratedProgramSubtypesRetainNamespaceZeroBaseStateIds(ProgramState state)
        {
            SystemContext context = CreateContext();
            var machine = new ManagedProgramState(null);
            machine.Create(context, new NodeId("Derived", 2), new QualifiedName("Derived", 2), default, false);
            NodeId stateId = ProgramStateMachineModel.GetStateId(state);
            Assert.That(stateId.TryGetValue(out uint id), Is.True);
            machine.SetState(context, id);

            Assert.Multiple(() =>
            {
                Assert.That(machine.CurrentState!.Id!.Value, Is.EqualTo(stateId));
                Assert.That(machine.GetStateId(stateId), Is.EqualTo(id));
                Assert.That(machine.GetTransitionNodeId(Objects.ProgramStateMachineType_ReadyToRunning),
                    Is.EqualTo(ObjectIds.ProgramStateMachineType_ReadyToRunning));
            });
        }

        /// <summary>
        /// The nine transitions retain their standard identifiers and transition numbers.
        /// </summary>
        [TestCase(ProgramState.Halted, ProgramState.Ready, Objects.ProgramStateMachineType_HaltedToReady, 1u)]
        [TestCase(ProgramState.Ready, ProgramState.Running, Objects.ProgramStateMachineType_ReadyToRunning, 2u)]
        [TestCase(ProgramState.Running, ProgramState.Halted, Objects.ProgramStateMachineType_RunningToHalted, 3u)]
        [TestCase(ProgramState.Running, ProgramState.Ready, Objects.ProgramStateMachineType_RunningToReady, 4u)]
        [TestCase(ProgramState.Running, ProgramState.Suspended,
            Objects.ProgramStateMachineType_RunningToSuspended, 5u)]
        [TestCase(ProgramState.Suspended, ProgramState.Running,
            Objects.ProgramStateMachineType_SuspendedToRunning, 6u)]
        [TestCase(ProgramState.Suspended, ProgramState.Halted, Objects.ProgramStateMachineType_SuspendedToHalted, 7u)]
        [TestCase(ProgramState.Suspended, ProgramState.Ready, Objects.ProgramStateMachineType_SuspendedToReady, 8u)]
        [TestCase(ProgramState.Ready, ProgramState.Halted, Objects.ProgramStateMachineType_ReadyToHalted, 9u)]
        public void TransitionMappingsMatchRuntime(ProgramState from, ProgramState to, uint id, uint number)
        {
            SystemContext context = CreateContext();
            var machine = new ProgramStateMachineState(null);
            machine.Create(context, new NodeId("Invocation", 2), new QualifiedName("Invocation", 2), default, false);
            Assert.That(ProgramStateMachineModel.GetStateId(from).TryGetValue(out uint fromId), Is.True);
            machine.SetState(context, fromId);

            ServiceResult result = machine.DoTransition(context, id, 0, default, []);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(ProgramStateMachineModel.GetTransitionId(from, to), Is.EqualTo(new NodeId(id)));
                Assert.That(machine.CurrentState!.Id!.Value, Is.EqualTo(ProgramStateMachineModel.GetStateId(to)));
                Assert.That(machine.CurrentState.Number!.Value, Is.EqualTo((uint)to));
                Assert.That(machine.LastTransition!.Id!.Value, Is.EqualTo(new NodeId(id)));
                Assert.That(machine.LastTransition.Number!.Value, Is.EqualTo(number));
            });
        }

        /// <summary>
        /// Control method mappings preserve standard declaration identifiers.
        /// </summary>
        [TestCase(ProgramControlMethod.Start, Methods.ProgramStateMachineType_Start)]
        [TestCase(ProgramControlMethod.Suspend, Methods.ProgramStateMachineType_Suspend)]
        [TestCase(ProgramControlMethod.Resume, Methods.ProgramStateMachineType_Resume)]
        [TestCase(ProgramControlMethod.Halt, Methods.ProgramStateMachineType_Halt)]
        [TestCase(ProgramControlMethod.Reset, Methods.ProgramStateMachineType_Reset)]
        public void ControlMethodsRoundTrip(ProgramControlMethod method, uint id)
        {
            Assert.That(ProgramStateMachineModel.GetMethodId(method), Is.EqualTo(new NodeId(id)));
            Assert.That(ProgramStateMachineModel.TryGetControlMethod(new NodeId(id), out ProgramControlMethod actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(method));
        }

        /// <summary>
        /// Bad enum values and unsupported transitions are not silently mapped to a valid identifier.
        /// </summary>
        [Test]
        public void InvalidMappingsAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => ProgramStateMachineModel.GetStateId((ProgramState)0),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ProgramStateMachineModel.GetMethodId((ProgramControlMethod)99),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ProgramStateMachineModel.GetTransitionId((ProgramState)0, ProgramState.Ready),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ProgramStateMachineModel.GetTransitionId(ProgramState.Ready, (ProgramState)0),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ProgramStateMachineModel.GetTransitionId(ProgramState.Ready, ProgramState.Ready),
                    Throws.ArgumentException);
                Assert.That(() => ProgramStateMachineModel.GetTransitionId(ProgramState.Halted, ProgramState.Running),
                    Throws.ArgumentException);
            });
            foreach (NodeId id in new NodeId[]
            {
                NodeId.Null,
                new(Objects.ProgramStateMachineType_Ready, 2),
                new(Methods.ProgramStateMachineType_Start, 2),
                new("2400", 0),
                new(999999u)
            })
            {
                Assert.That(ProgramStateMachineModel.TryGetState(id, out _), Is.False);
                Assert.That(ProgramStateMachineModel.TryGetControlMethod(id, out _), Is.False);
            }
        }

        /// <summary>
        /// The legacy low-level Reset from Suspended remains supported.
        /// </summary>
        [Test]
        public void LegacyResetFromSuspendedRemainsSupported()
        {
            SystemContext context = CreateContext();
            var machine = new ProgramStateMachineState(null);
            machine.Create(context, new NodeId("Invocation", 2), new QualifiedName("Invocation", 2), default, false);
            machine.SetState(context, Objects.ProgramStateMachineType_Suspended);
            var reset = new MethodState(machine) { BrowseName = QualifiedName.From(BrowseNames.Reset) };

            ServiceResult result = machine.DoCause(context, reset, Methods.ProgramStateMachineType_Reset, default, []);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(machine.CurrentState!.Id!.Value, Is.EqualTo(ObjectIds.ProgramStateMachineType_Ready));
            Assert.That(machine.CurrentState.Number!.Value, Is.EqualTo(12u));
        }

        /// <summary>
        /// The SDK declares concrete optional methods and a type-only Create with its generated signature.
        /// </summary>
        [Test]
        public void ManagedProgramTypeDeclaresOptionalControlsAndTypeOnlyCreate()
        {
            SystemContext context = CreateContext();
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaPrograms(context);
            NodeId typeId = ExpandedNodeId.ToNodeId(ProgramModelIds.ManagedProgramType, context.NamespaceUris);
            var type = (BaseObjectTypeState)nodes.Single(n => n.NodeId == typeId);
            var children = new List<BaseInstanceState>();
            type.GetChildren(context, children);
            MethodState[] controls = [.. children.OfType<MethodState>().Where(m => m.BrowseName.Name != "Create")];
            Assert.That(controls.Select(m => m.BrowseName.Name),
                Is.EquivalentTo(s_controlNames));
            foreach (MethodState control in controls)
            {
                Assert.That(control.BrowseName.NamespaceIndex, Is.Zero);
                Assert.That(control.ModellingRuleId, Is.EqualTo(ObjectIds.ModellingRule_Optional));
                Assert.That(control.InputArguments?.Value.Count ?? 0, Is.Zero);
                Assert.That(control.OutputArguments?.Value.Count ?? 0, Is.Zero);
            }
            MethodState create = children.OfType<MethodState>().Single(m => m.BrowseName.Name == "Create");
            Assert.Multiple(() =>
            {
                Assert.That(type.SuperTypeId, Is.EqualTo(ObjectTypeIds.ProgramStateMachineType));
                Assert.That(type.IsAbstract, Is.False);
                Assert.That(create.ModellingRuleId.IsNull, Is.True);
                Assert.That(create.InputArguments!.Value.ToArray()!.Select(a => a.Name),
                    Is.EqualTo(s_createInputNames));
                Assert.That(create.InputArguments.Value.ToArray()!.Select(a => a.DataType),
                    Is.EqualTo(new[] { DataTypeIds.NodeId, DataTypeIds.QualifiedName }));
                Assert.That(create.OutputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(create.OutputArguments.Value[0].Name, Is.EqualTo("InvocationId"));
                Assert.That(create.OutputArguments.Value[0].DataType, Is.EqualTo(DataTypeIds.NodeId));
            });
        }

        /// <summary>
        /// Instances inherit diagnostics/results support without a Create method or type-level policy properties.
        /// </summary>
        [Test]
        public void ManagedProgramInstanceDoesNotInstantiateTypeOnlyMembers()
        {
            SystemContext context = CreateContext();
            ManagedProgramState machine = context.CreateInstanceOfManagedProgramType();
            machine.CreateAsPredefinedNode(context);
            var children = new List<BaseInstanceState>();
            machine.GetChildren(context, children);

            Assert.Multiple(() =>
            {
                Assert.That(children.Any(c => c.BrowseName.Name == "Create"), Is.False);
                Assert.That(children.Any(c => c.BrowseName.Name == BrowseNames.Creatable), Is.False);
                Assert.That(children.Any(c => c.BrowseName.Name == BrowseNames.InstanceCount), Is.False);
                Assert.That(machine.CurrentState!.Number!.Value, Is.EqualTo(12u));
                Assert.That(machine.Start, Is.Null);
                Assert.That(machine.ProgramDiagnostic, Is.Null);
                Assert.That(machine.FinalResultData, Is.Null);
            });

            MethodState start = machine.CreateOrReplaceStart(context, null, false);
            machine.WireCauseMethods(context);
            Assert.That(machine.Start, Is.SameAs(start));
            Assert.That(start.OnCallMethod, Is.Not.Null);
            Assert.That(start.BrowseName.NamespaceIndex, Is.Zero);
            Assert.That(machine.CreateOrReplaceProgramDiagnostic(context, null, false),
                Is.TypeOf<ProgramDiagnostic2State>());
            Assert.That(machine.CreateOrReplaceFinalResultData(context, null, false), Is.TypeOf<BaseObjectState>());
        }

        /// <summary>
        /// SDK event declarations are concrete and retain the required standard ancestry.
        /// </summary>
        [Test]
        public void SdkEventsAreConcreteStandardSubtypes()
        {
            SystemContext context = CreateContext();
            BaseObjectTypeState transition = context.CreateManagedProgramTransitionEventType();
            BaseObjectTypeState audit = context.CreateManagedProgramAuditEventType();
            Assert.Multiple(() =>
            {
                Assert.That(transition.IsAbstract, Is.False);
                Assert.That(transition.SuperTypeId, Is.EqualTo(ObjectTypeIds.ProgramTransitionEventType));
                Assert.That(audit.IsAbstract, Is.False);
                Assert.That(audit.SuperTypeId, Is.EqualTo(ObjectTypeIds.AuditProgramTransitionEventType));
                Assert.That(transition.NodeId.NamespaceIndex, Is.EqualTo(2));
                Assert.That(audit.NodeId.NamespaceIndex, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Generated event fields capture IDs and intermediate results without rereading mutable nodes.
        /// </summary>
        [Test]
        public void GeneratedTransitionRecordsDecodeCapturedStateIdsAndIntermediateResults()
        {
            SystemContext context = CreateContext();
            var registry = new EventRecordDecoderRegistry().RegisterOpcUaProgramsDecoders(context.NamespaceUris);
            QualifiedName[][] paths = registry.StandardFields;
            var fields = new Variant[paths.Length];
            NodeId eventTypeId = ExpandedNodeId.ToNodeId(
                ProgramModelIds.ManagedProgramTransitionEventType, context.NamespaceUris);
            fields[FindField(paths, BrowseNames.EventType)] = Variant.From(eventTypeId);
            fields[FindField(paths, BrowseNames.SourceNode)] = Variant.From(new NodeId("Invocation", 2));
            fields[FindField(paths, BrowseNames.FromState, BrowseNames.Id)] =
                Variant.From(ObjectIds.ProgramStateMachineType_Running);
            fields[FindField(paths, BrowseNames.ToState, BrowseNames.Id)] =
                Variant.From(ObjectIds.ProgramStateMachineType_Suspended);
            fields[FindField(paths, BrowseNames.Transition, BrowseNames.Id)] =
                Variant.From(ObjectIds.ProgramStateMachineType_RunningToSuspended);
            fields[FindField(paths, BrowseNames.IntermediateResult)] = Variant.From(42);

            var record = registry.Decode(fields) as ManagedProgramTransitionEventTypeRecord;

            Assert.That(record, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(record!.FromStateId, Is.EqualTo(Variant.From(ObjectIds.ProgramStateMachineType_Running)));
                Assert.That(record.ToStateId, Is.EqualTo(Variant.From(ObjectIds.ProgramStateMachineType_Suspended)));
                Assert.That(record.TransitionId,
                    Is.EqualTo(Variant.From(ObjectIds.ProgramStateMachineType_RunningToSuspended)));
                Assert.That(record.IntermediateResult, Is.EqualTo(Variant.From(42)));
                Assert.That(record.EventType, Is.EqualTo(eventTypeId));
            });
        }

        private static int FindField(QualifiedName[][] paths, string name, string? child = null)
        {
            int index = Array.FindIndex(paths,
                path => path.Length == (child == null ? 1 : 2) &&
                    path[0] == QualifiedName.From(name) &&
                    (child == null || path[1] == QualifiedName.From(child)));
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Missing event select path {name}/{child}");
            return index;
        }

        private static SystemContext CreateContext()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:namespace-remapping-test");
            namespaces.Append(ProgramNamespaces.OpcUaPrograms);
            return new SystemContext(NUnitTelemetryContext.Create()) { NamespaceUris = namespaces };
        }

        private static readonly string[] s_controlNames = ["Start", "Suspend", "Resume", "Halt", "Reset"];
        private static readonly string[] s_createInputNames = ["ParentId", "BrowseName"];
    }
}
