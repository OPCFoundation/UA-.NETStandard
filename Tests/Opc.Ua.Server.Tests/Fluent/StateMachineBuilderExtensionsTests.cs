/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for the fluent state-machine builder exercising
    /// <see cref="ProgramStateMachineState"/> — a real stack-shipped
    /// subclass with full cause/transition/state tables.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class StateMachineBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(global::Opc.Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, ProgramStateMachineState Machine,
            MethodState StartMethod)
            CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var machine = new ProgramStateMachineState(parent: null);
            machine.NodeId = new NodeId("Machine", kNs);
            machine.BrowseName = new QualifiedName("Machine", kNs);
            machine.DisplayName = new LocalizedText("Machine");
            machine.Create(
                ctx, machine.NodeId, machine.BrowseName,
                displayName: machine.DisplayName,
                assignNodeIds: false);

            var startMethod = new MethodState(machine)
            {
                NodeId = new NodeId("Machine_Start", kNs),
                BrowseName = new QualifiedName("Start", kNs),
                DisplayName = new LocalizedText("Start")
            };
            machine.AddChild(startMethod);

            var roots = new Dictionary<QualifiedName, NodeState>
            {
                [machine.BrowseName] = machine
            };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [machine.NodeId] = machine,
                [startMethod.NodeId] = startMethod
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, machine, startMethod);
        }

        private static IStateMachineBuilder<ProgramStateMachineState> ResolveMachine(
            NodeManagerBuilder builder)
        {
            return builder
                .Node<ProgramStateMachineState>(new NodeId("Machine", kNs))
                .AsStateMachine();
        }

        [Test]
        public void WithInitialStateSetsCurrentStateId()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();

            ResolveMachine(b).WithInitialState(Objects.ProgramStateMachineType_Ready);

            uint actual = ExtractCurrentStateId(m);
            Assert.That(actual, Is.EqualTo(Objects.ProgramStateMachineType_Ready));
        }

        [Test]
        public void WithInitialStateZeroThrows()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            IStateMachineBuilder<ProgramStateMachineState> sb = ResolveMachine(b);

            Assert.Throws<ArgumentOutOfRangeException>(() => sb.WithInitialState(0));
        }

        [Test]
        public void OnEnterStateFiresOnArrival()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();
            ProgramStateMachineState? observed = null;

            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .OnEnterState(Objects.ProgramStateMachineType_Running,
                    (ctx, sm) => observed = sm);

            ServiceResult result = m.DoTransition(
                CreateContext(),
                Objects.ProgramStateMachineType_ReadyToRunning,
                causeId: 0,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(ServiceResult.IsGood(result), $"Transition failed: {result}");
            Assert.That(observed, Is.SameAs(m));
        }

        [Test]
        public void OnExitStateFiresWhenLeavingState()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();
            int exitFires = 0;

            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .OnExitState(Objects.ProgramStateMachineType_Ready,
                    (ctx, sm) => exitFires++);

            m.DoTransition(
                CreateContext(),
                Objects.ProgramStateMachineType_ReadyToRunning,
                causeId: 0,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(exitFires, Is.EqualTo(1));
        }

        [Test]
        public void OnTransitionReceivesFromAndToStateIds()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();
            uint observedFrom = 0;
            uint observedTo = 0;

            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .OnTransition((ctx, sm, from, to) =>
                {
                    observedFrom = from;
                    observedTo = to;
                });

            m.DoTransition(
                CreateContext(),
                Objects.ProgramStateMachineType_ReadyToRunning,
                causeId: 0,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(observedFrom, Is.EqualTo(Objects.ProgramStateMachineType_Ready));
            Assert.That(observedTo, Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public void OnBeforeTransitionGuardCancelsTransition()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();

            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .OnBeforeTransition((ctx, sm, fromStateId) =>
                    StatusCodes.BadUserAccessDenied);

            ServiceResult result = m.DoTransition(
                CreateContext(),
                Objects.ProgramStateMachineType_ReadyToRunning,
                causeId: 0,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(ServiceResult.IsBad(result));
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            // Should remain in Ready
            Assert.That(ExtractCurrentStateId(m),
                Is.EqualTo(Objects.ProgramStateMachineType_Ready));
        }

        [Test]
        public void OnBeforeTransitionComposesWithExistingHandler()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();
            int existingCalls = 0;
            m.OnBeforeTransition = (ctx, sm, t, c, i, o) =>
            {
                existingCalls++;
                return ServiceResult.Good;
            };

            int fluentCalls = 0;
            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .OnBeforeTransition((ctx, sm, fromStateId) =>
                {
                    fluentCalls++;
                    return ServiceResult.Good;
                });

            m.DoTransition(
                CreateContext(),
                Objects.ProgramStateMachineType_ReadyToRunning,
                causeId: 0,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(existingCalls, Is.EqualTo(1),
                "Pre-existing OnBeforeTransition handler must still run.");
            Assert.That(fluentCalls, Is.EqualTo(1),
                "Fluent guard must run after existing handler.");
        }

        [Test]
        public void WithCauseWiresMethodToDoCause()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, MethodState start) = CreateBuilder();

            ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready)
                .WithCause(
                    start.NodeId,
                    Methods.ProgramStateMachineType_Start);

            Assert.That(start.OnCallMethod2, Is.Not.Null);

            ServiceResult result = start.OnCallMethod2!(
                CreateContext(),
                start,
                objectId: m.NodeId,
                inputArguments: default,
                outputArguments: new List<Variant>());

            Assert.That(ServiceResult.IsGood(result),
                $"DoCause via wired method must succeed: {result}");
            Assert.That(ExtractCurrentStateId(m),
                Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public void WithCauseRejectsAlreadyWiredMethod()
        {
            (NodeManagerBuilder b, _, MethodState start) = CreateBuilder();
            start.OnCallMethod2 = (ctx, m, oid, inputs, outputs) => ServiceResult.Good;

            IStateMachineBuilder<ProgramStateMachineState> sb = ResolveMachine(b)
                .WithInitialState(Objects.ProgramStateMachineType_Ready);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => sb.WithCause(start.NodeId, Methods.ProgramStateMachineType_Start))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void WithCauseUnknownMethodThrows()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            IStateMachineBuilder<ProgramStateMachineState> sb = ResolveMachine(b);

            Assert.Throws<ServiceResultException>(
                () => sb.WithCause(new NodeId("UnknownMethod", kNs),
                    Methods.ProgramStateMachineType_Start));
        }

        [Test]
        public void ConfigureStateMachineExposesEscapeHatch()
        {
            (NodeManagerBuilder b, ProgramStateMachineState m, _) = CreateBuilder();

            ResolveMachine(b)
                .ConfigureStateMachine(sm => sm.SuppressTransitionEvents = true);

            Assert.That(m.SuppressTransitionEvents, Is.True);
        }

        [Test]
        public void DoneReturnsOwningNodeBuilder()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder<ProgramStateMachineState> nb = b
                .Node<ProgramStateMachineState>(new NodeId("Machine", kNs));

            INodeBuilder returned = nb.AsStateMachine().Done();
            Assert.That(returned, Is.SameAs(nb));
        }

        [Test]
        public void CreateProgramStateMachineMaterialisesUnderParent()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder parent = b.Node(new NodeId("Machine", kNs));

            IStateMachineBuilder<ProgramStateMachineState> sm =
                parent.CreateProgramStateMachine(new QualifiedName("Cycle", kNs));

            Assert.That(sm.StateMachine, Is.Not.Null);
            Assert.That(sm.StateMachine.BrowseName,
                Is.EqualTo(new QualifiedName("Cycle", kNs)));
            Assert.That(sm.StateMachine.Parent,
                Is.SameAs(parent.Node));
            Assert.That(sm.StateMachine.NodeId.IdentifierAsString,
                Is.EqualTo("Machine_Cycle"));
        }

        [Test]
        public void CreateShelvedStateMachineMaterialisesUnderParent()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder parent = b.Node(new NodeId("Machine", kNs));

            IStateMachineBuilder<ShelvedStateMachineState> sm =
                parent.CreateShelvedStateMachine(new QualifiedName("Shelv", kNs));

            Assert.That(sm.StateMachine.BrowseName,
                Is.EqualTo(new QualifiedName("Shelv", kNs)));
            Assert.That(sm.StateMachine.Parent, Is.SameAs(parent.Node));
        }

        [Test]
        public void CreateExclusiveLimitStateMachineMaterialisesUnderParent()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder parent = b.Node(new NodeId("Machine", kNs));

            IStateMachineBuilder<ExclusiveLimitStateMachineState> sm =
                parent.CreateExclusiveLimitStateMachine(
                    new QualifiedName("ExLim", kNs));

            Assert.That(sm.StateMachine.BrowseName,
                Is.EqualTo(new QualifiedName("ExLim", kNs)));
            Assert.That(sm.StateMachine.Parent, Is.SameAs(parent.Node));
        }

        [Test]
        public void CreateStateMachineRejectsNullArgs()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder parent = b.Node(new NodeId("Machine", kNs));

            Assert.Throws<ArgumentNullException>(
                () => parent.CreateProgramStateMachine(default));
        }

        private static uint ExtractCurrentStateId(ProgramStateMachineState m)
        {
            NodeId value = m.CurrentState!.Id!.Value;
            if (value.IsNull) { return 0; }
            return value.TryGetValue(out uint numericId) ? numericId : 0;
        }
    }
}
