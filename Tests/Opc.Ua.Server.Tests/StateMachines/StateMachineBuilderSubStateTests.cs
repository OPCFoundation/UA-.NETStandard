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
        public void WithSubStateMachineAddsHasSubStateMachineChild()
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
            Assert.That(child.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasSubStateMachine));
            Assert.That(child, Is.InstanceOf<FluentFiniteStateMachineState>());
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
        public void SuspendedSubStateMachineRejectsTransitionsWithBadInvalidState()
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
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadInvalidState));
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

        private static uint CurrentStateId(FiniteStateMachineState sm)
        {
            if (sm.CurrentState?.Id?.Value is { } id &&
                !id.IsNull &&
                id.TryGetValue(out uint stateId))
            {
                return stateId;
            }
            return 0;
        }
    }
}
