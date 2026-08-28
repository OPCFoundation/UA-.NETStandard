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
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for <see cref="StateMachineBuilder{TState}.WithSubStateMachine"/>
    /// — the hierarchical state-machine builder sugar. Verifies the
    /// HasSubStateMachine wiring, parent-state-entry activation, the
    /// suspend-on-exit semantics, and the per-parent-state reset
    /// behavior (with the <c>preserveOnReentry</c> opt-out).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderSubStateTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void WithSubStateMachineAddsHasComponentChild()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddState(11, "ChildRunning")
                        .AddTransition(100, "IdleToRunning", from: 10, to: 11))
                .StateMachine;

            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            BaseInstanceState child = children.Find(c => c.BrowseName.Name == "ChildSm")!;

            Assert.That(child, Is.Not.Null);
            // HasSubStateMachine is non-hierarchical, so the parent /
            // child relationship uses HasComponent — matching the
            // standard NodeSets.
            Assert.That(child.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(child, Is.InstanceOf<FluentFiniteStateMachineState>());
        }

        [Test]
        public void WithSubStateMachineReferencesSubMachineFromParentStateNode()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .StateMachine;

            BaseInstanceState child = GetChild(parent, "ChildSm");
            BaseInstanceState stateNode = GetChild(parent, "ParentA");

            // Part 16 §4.4.16: the reference hangs off the parent STATE
            // node, not the state machine root.
            var forward = new List<IReference>();
            stateNode.GetReferences(
                m_context, forward, ReferenceTypeIds.HasSubStateMachine, false);
            Assert.That(forward, Has.Count.EqualTo(1));
            Assert.That(forward[0].TargetId, Is.EqualTo((ExpandedNodeId)child.NodeId));

            // ... and the inverse (SubStateMachineOf) browses back.
            var inverse = new List<IReference>();
            child.GetReferences(
                m_context, inverse, ReferenceTypeIds.HasSubStateMachine, true);
            Assert.That(inverse, Has.Count.EqualTo(1));
            Assert.That(inverse[0].TargetId, Is.EqualTo((ExpandedNodeId)stateNode.NodeId));

            // The state machine root must NOT carry the reference.
            var fromRoot = new List<IReference>();
            parent.GetReferences(
                m_context, fromRoot, ReferenceTypeIds.HasSubStateMachine, false);
            Assert.That(fromRoot, Is.Empty);
        }

        [Test]
        public void SubStateMachineIsDiscoverableThroughTheNodeBrowser()
        {
            // Drives the real NodeBrowser along the exact path a client
            // takes: hierarchical browse of the machine to reach the
            // state node and the sub-SM, then HasSubStateMachine from
            // the state node. HasSubStateMachine is non-hierarchical,
            // so using it as the parent/child reference would leave the
            // sub-SM invisible to the first of those browses.
            //
            // NodeState.PopulateBrowser only walks children when the
            // TypeTable can tell it the browsed reference type is
            // hierarchical, so the bare test table needs that one fact.
            var typeTable = (TypeTable)m_context.TypeTable;
            typeTable.AddReferenceSubtype(
                ReferenceTypeIds.HierarchicalReferences,
                NodeId.Null,
                new QualifiedName(BrowseNames.HierarchicalReferences));
            typeTable.AddReferenceSubtype(
                ReferenceTypeIds.HasComponent,
                ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName(BrowseNames.HasComponent));

            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .StateMachine;

            List<NodeId> components = BrowseTargets(
                parent, ReferenceTypeIds.HasComponent, BrowseDirection.Forward);
            NodeId stateNodeId = GetChild(parent, "ParentA").NodeId;
            NodeId childNodeId = GetChild(parent, "ChildSm").NodeId;

            Assert.That(components, Does.Contain(stateNodeId));
            Assert.That(components, Does.Contain(childNodeId));

            List<NodeId> subMachines = BrowseTargets(
                GetChild(parent, "ParentA"),
                ReferenceTypeIds.HasSubStateMachine,
                BrowseDirection.Forward);

            Assert.That(subMachines, Is.EqualTo(new[] { childNodeId }));
        }

        private List<NodeId> BrowseTargets(
            NodeState node,
            NodeId referenceTypeId,
            BrowseDirection direction)
        {
            var targets = new List<NodeId>();
            using INodeBrowser browser = node.CreateBrowser(
                m_context,
                null,
                referenceTypeId,
                false,
                direction,
                default,
                null,
                false);
            for (IReference reference = browser.Next();
                reference != null;
                reference = browser.Next())
            {
                targets.Add(ExpandedNodeId.ToNodeId(
                    reference.TargetId, m_context.NamespaceUris));
            }
            return targets;
        }

        [Test]
        public void WithSubStateMachineRejectsBrowseNameCollidingWithState()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder =
                BuildParent().WithInitialState(1);

            // The sub-SM NodeId is composed like the element NodeIds,
            // so a browse name shared with a state would collide.
            Assert.Throws<ArgumentException>(() =>
                builder.WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ParentA", 1),
                    configure: c => c.AddState(10, "ChildIdle", isInitial: true)));
        }

        [Test]
        public void WithSubStateMachineRejectsUnknownParentState()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder =
                BuildParent().WithInitialState(1);

            Assert.Throws<InvalidOperationException>(() =>
                builder.WithSubStateMachine(
                    parentStateId: 99,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c.AddState(10, "ChildIdle", isInitial: true)));
        }

        [Test]
        public void SubStateMachineActivatesWhenInitialStateIsSetAfterwards()
        {
            // WithInitialState goes through SetState, which fires no
            // enter handler — the sub-SM must still activate, so this
            // call order behaves the same as the reverse one.
            FluentFiniteStateMachineState parent = BuildParent()
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddState(11, "ChildRunning")
                        .AddTransition(100, "IdleToRunning", from: 10, to: 11))
                .WithInitialState(1)
                .StateMachine;

            var child = (FluentFiniteStateMachineState)GetChild(parent, "ChildSm");

            Assert.Multiple(() =>
            {
                Assert.That(child.IsSuspended, Is.False);
                Assert.That(CurrentStateId(child), Is.EqualTo(10u));
            });
        }

        [Test]
        public void SubStateMachineStaysSuspendedWhenInitialStateIsElsewhere()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .WithInitialState(2)
                .StateMachine;

            var child = (FluentFiniteStateMachineState)GetChild(parent, "ChildSm");

            Assert.Multiple(() =>
            {
                Assert.That(child.IsSuspended, Is.True);
                // Part 16: an inactive sub-state machine publishes no
                // current state — its declared initial state is only
                // applied once the parent enters the attached state.
                Assert.That(child.CurrentState!.Value.IsNullOrEmpty, Is.True);
                // ... and OPC 10000-16 §4.4.6: CurrentState and
                // LastTransition of an inactive sub-SM read with
                // Bad_StateNotActive.
                Assert.That(child.CurrentState.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
                Assert.That(child.LastTransition!.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            });
        }

        [Test]
        public void ResumedSubStateMachineReadsGoodAgain()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .WithInitialState(2)
                .StateMachine;
            var child = (FluentFiniteStateMachineState)GetChild(parent, "ChildSm");
            Assert.That(child.CurrentState!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));

            // Parent enters the attached state → the sub-SM activates
            // and its state variables read Good with the seeded state.
            parent.DoTransition(m_context, 21, 0, default, []);

            Assert.Multiple(() =>
            {
                Assert.That(child.IsSuspended, Is.False);
                Assert.That(child.CurrentState.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(CurrentStateId(child), Is.EqualTo(10u));
            });
        }

        [Test]
        public void PreserveOnReentryChildSurvivesALaterWithInitialState()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> builder =
                BuildParent()
                    .WithInitialState(1)
                    .WithSubStateMachine(
                        parentStateId: 1,
                        browseName: new QualifiedName("ChildSm", 1),
                        configure: c => c
                            .AddState(10, "ChildIdle", isInitial: true)
                            .AddState(11, "ChildRunning")
                            .AddTransition(100, "IdleToRunning", from: 10, to: 11),
                        preserveOnReentry: true);
            FluentFiniteStateMachineState parent = builder.StateMachine;
            var child = (FluentFiniteStateMachineState)GetChild(parent, "ChildSm");

            // Move the child off its initial state, then re-apply the
            // parent's initial state — a preserveOnReentry child must
            // keep its state instead of being reset.
            child.DoTransition(m_context, 100, 0, default, []);
            Assert.That(CurrentStateId(child), Is.EqualTo(11u));

            builder.WithInitialState(1);

            Assert.That(CurrentStateId(child), Is.EqualTo(11u));
        }

        [Test]
        public void WithSubStateMachineActivatesOnParentStateEntry()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(2) // parent starts in state 2 (sub-SM is NOT attached here)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .StateMachine;

            var child = (FluentFiniteStateMachineState)
                GetChild(parent, "ChildSm");

            // Sub-SM starts suspended (parent is in state 2, not 1).
            Assert.That(child.IsSuspended, Is.True);

            // Move parent to state 1 → sub-SM activates.
            parent.DoTransition(m_context, 21, 0, default, []);
            Assert.That(child.IsSuspended, Is.False);
        }

        [Test]
        public void WithSubStateMachineSuspendsOnParentStateExit()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .StateMachine;

            var child = (FluentFiniteStateMachineState)
                GetChild(parent, "ChildSm");
            Assert.That(child.IsSuspended, Is.False);

            // Move parent OFF state 1 → sub-SM suspended.
            parent.DoTransition(m_context, 12, 0, default, []);
            Assert.That(child.IsSuspended, Is.True);
        }

        [Test]
        public void SuspendedSubStateMachineRejectsTransitionsWithBadStateNotActive()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(2) // parent in state 2, sub-SM suspended
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddTransition(100, "Loop", from: 10, to: 10))
                .StateMachine;

            var child = (FluentFiniteStateMachineState)
                GetChild(parent, "ChildSm");

            ServiceResult result = child.DoTransition(m_context, 100, 0, default, []);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadStateNotActive));
        }

        [Test]
        public void ResetOnReentryRestoresInitialState()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddState(11, "ChildRunning")
                        .AddTransition(100, "IdleToRunning", from: 10, to: 11)
                        .OnCause(1000, from: 10, transition: 100))
                .StateMachine;

            var child = (FluentFiniteStateMachineState)
                GetChild(parent, "ChildSm");

            // Move child to "Running" state.
            child.DoTransition(m_context, 100, 1000, default, []);
            Assert.That(CurrentStateId(child), Is.EqualTo(11u));

            // Move parent off state 1 and back.
            parent.DoTransition(m_context, 12, 0, default, []);
            parent.DoTransition(m_context, 21, 0, default, []);

            // Child should have reset to its initial state.
            Assert.That(CurrentStateId(child), Is.EqualTo(10u));
            Assert.That(child.IsSuspended, Is.False);
        }

        [Test]
        public void PreserveOnReentryRetainsChildState()
        {
            FluentFiniteStateMachineState parent = BuildParent()
                .WithInitialState(1)
                .WithSubStateMachine(
                    parentStateId: 1,
                    browseName: new QualifiedName("ChildSm", 1),
                    configure: c => c
                        .AddState(10, "ChildIdle", isInitial: true)
                        .AddState(11, "ChildRunning")
                        .AddTransition(100, "IdleToRunning", from: 10, to: 11)
                        .OnCause(1000, from: 10, transition: 100),
                    preserveOnReentry: true)
                .StateMachine;

            var child = (FluentFiniteStateMachineState)
                GetChild(parent, "ChildSm");

            child.DoTransition(m_context, 100, 1000, default, []);
            Assert.That(CurrentStateId(child), Is.EqualTo(11u));

            parent.DoTransition(m_context, 12, 0, default, []);
            parent.DoTransition(m_context, 21, 0, default, []);

            // Child retained its "Running" state across the
            // parent-exit / re-entry cycle.
            Assert.That(CurrentStateId(child), Is.EqualTo(11u));
            Assert.That(child.IsSuspended, Is.False);
        }

        [Test]
        public void WithSubStateMachineThrowsInLifecycleMode()
        {
            FluentFiniteStateMachineState sm = BuildParent().StateMachine;
            var lifecycle =
                StateMachineBuilder.For(sm, m_context);

            Assert.That(() => lifecycle.WithSubStateMachine(
                parentStateId: 1,
                browseName: new QualifiedName("ChildSm", 1),
                configure: c => c.AddState(10, "X", isInitial: true)),
                Throws.InvalidOperationException);
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> BuildParent()
        {
            return StateMachineTestFixtures.NewBuilder(m_context)
                .AddState(1, "ParentA", isInitial: true)
                .AddState(2, "ParentB")
                .AddTransition(12, "AToB", from: 1, to: 2)
                .AddTransition(21, "BToA", from: 2, to: 1);
        }

        private static BaseInstanceState GetChild(FluentFiniteStateMachineState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            return children.Find(c => c.BrowseName.Name == browseName)!;
        }

        private static uint CurrentStateId(FluentFiniteStateMachineState sm)
        {
            if (sm.CurrentState?.Id?.Value is { } id && !id.IsNull)
            {
                // Resolve through the machine's own mapping so
                // materialized state NodeIds are understood too.
                return sm.GetStateId(id);
            }
            return 0;
        }
    }
}
