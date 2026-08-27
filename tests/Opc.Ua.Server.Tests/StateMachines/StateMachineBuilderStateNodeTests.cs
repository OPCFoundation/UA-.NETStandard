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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for the <c>StateType</c> nodes that
    /// <see cref="FluentFiniteStateMachineState"/> materializes for its
    /// declared states — the address-space representation Part 16 §B.3
    /// requires so that <c>CurrentState/Id</c> resolves to a real node
    /// and <c>HasSubStateMachine</c> has a state node to hang off.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderStateNodeTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void EveryDeclaredStateGetsAStateTypeNode()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            BaseInstanceState off = GetChild(sm, "Off");
            BaseInstanceState on = GetChild(sm, "On");

            Assert.Multiple(() =>
            {
                Assert.That(off, Is.InstanceOf<BaseObjectState>());
                Assert.That(((BaseObjectState)off).TypeDefinitionId,
                    Is.EqualTo(ObjectTypeIds.StateType));
                Assert.That(off.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(((BaseObjectState)on).TypeDefinitionId,
                    Is.EqualTo(ObjectTypeIds.StateType));
            });
        }

        [Test]
        public void StateNodeCarriesStateNumberProperty()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.Multiple(() =>
            {
                Assert.That(StateNumber(sm, "Off"), Is.EqualTo(1u));
                Assert.That(StateNumber(sm, "On"), Is.EqualTo(2u));
            });
        }

        [Test]
        public void CurrentStateIdResolvesToTheMaterializedNode()
        {
            FluentFiniteStateMachineState sm = Build()
                .WithInitialState(1)
                .StateMachine;

            Assert.That(sm.CurrentState!.Id!.Value,
                Is.EqualTo(GetChild(sm, "Off").NodeId));

            sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That(sm.CurrentState.Id.Value,
                Is.EqualTo(GetChild(sm, "On").NodeId));
        }

        [Test]
        public void GetStateIdRoundTripsThroughGetStateNodeId()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.Multiple(() =>
            {
                Assert.That(sm.GetStateId(sm.GetStateNodeId(1)), Is.EqualTo(1u));
                Assert.That(sm.GetStateId(sm.GetStateNodeId(2)), Is.EqualTo(2u));
                Assert.That(sm.GetStateId(new NodeId("nonsense", 1)), Is.Zero);
            });
        }

        [Test]
        public void GetStateIdIsAuthoritativeAfterMaterialization()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            // A foreign numeric NodeId must not resolve to a state of
            // this machine once the materialized map exists — the old
            // numeric fallback would have returned 2930 here.
            Assert.Multiple(() =>
            {
                Assert.That(sm.GetStateId(new NodeId(2930u, 0)), Is.Zero);
                Assert.That(sm.GetStateId(new NodeId(1u, 0)), Is.Zero);
                Assert.That(sm.GetTransitionId(new NodeId(10u, 0)), Is.Zero);
            });
        }

        [Test]
        public void LifecycleStateExtractionFallsBackToNumericIds()
        {
            // A lifecycle-mode machine whose CurrentState/Id is written
            // by an external dispatcher in a vendor namespace must still
            // resolve numerically — the builder has always honoured the
            // namespace-agnostic numeric convention for adopted FSMs.
            var sm = new ExclusiveLimitStateMachineState(null);
            sm.Create(
                m_context,
                new NodeId(4300, 1),
                new QualifiedName("LimitState", 1),
                new LocalizedText("LimitState"),
                true);
            sm.CurrentState!.Id!.Value = new NodeId(
                Objects.ExclusiveLimitStateMachineType_Low, 5);

            Assert.That(
                StateMachineBuilder.ResolveStateId(sm, sm.CurrentState.Id.Value),
                Is.EqualTo(Objects.ExclusiveLimitStateMachineType_Low));
        }

        [Test]
        public void DuplicateElementBrowseNamesAreRejected()
        {
            Assert.Multiple(() =>
            {
                // Two states sharing a browse name would mint the same
                // materialized NodeId.
                Assert.That(() => StateMachineTestFixtures.NewBuilder(m_context)
                        .AddState(1, "Idle", isInitial: true)
                        .AddState(2, "Idle")
                        .StateMachine,
                    Throws.InvalidOperationException);

                // A state and a transition sharing a browse name collide
                // the same way.
                Assert.That(() => StateMachineTestFixtures.NewBuilder(m_context)
                        .AddState(1, "Reset", isInitial: true)
                        .AddTransition(10, "Reset", from: 1, to: 1)
                        .StateMachine,
                    Throws.InvalidOperationException);

                // Standard FiniteStateMachineType children are reserved.
                Assert.That(() => StateMachineTestFixtures.NewBuilder(m_context)
                        .AddState(1, BrowseNames.AvailableStates, isInitial: true)
                        .StateMachine,
                    Throws.InvalidOperationException);
            });
        }

        [Test]
        public void AvailableStatesListsTheMaterializedStateNodes()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.That(sm.AvailableStates, Is.Not.Null);
            NodeId[] published = [.. sm.AvailableStates!.Value];

            Assert.That(published, Is.EqualTo(new[]
            {
                GetChild(sm, "Off").NodeId,
                GetChild(sm, "On").NodeId
            }));

            // The standard NodeSet models AvailableStates as a
            // HasComponent BaseDataVariable, and that is the reference
            // type GetAvailableStatesAsync translates the path with.
            Assert.That(sm.AvailableStates.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(sm.AvailableStates.BrowseName,
                Is.EqualTo(new QualifiedName(BrowseNames.AvailableStates)));
        }

        [Test]
        public void TwoMachinesFromOneDefinitionGetDistinctStateNodeIds()
        {
            FluentFiniteStateMachineState first =
                Build(nodeIdNumber: 5000).StateMachine;
            FluentFiniteStateMachineState second =
                Build(nodeIdNumber: 5001).StateMachine;

            Assert.That(GetChild(first, "Off").NodeId,
                Is.Not.EqualTo(GetChild(second, "Off").NodeId));
        }

        [Test]
        public void StateNodesLandInTheMachineNamespaceByDefault()
        {
            // The element namespace defaults to the OPC UA namespace
            // (index 0), which must never host vendor nodes.
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.That(GetChild(sm, "Off").NodeId.NamespaceIndex,
                Is.EqualTo(sm.NodeId.NamespaceIndex));
            Assert.That(GetChild(sm, "Off").NodeId.NamespaceIndex, Is.Not.Zero);
        }

        [Test]
        public void UseElementNamespaceControlsTheStateNodeNamespace()
        {
            const string customUri = "urn:test:custom-ns";
            ushort registeredIndex = m_context.NamespaceUris.GetIndexOrAppend(customUri);

            FluentFiniteStateMachineState sm = StateMachineTestFixtures
                .NewBuilder(m_context)
                .UseElementNamespace(customUri)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .StateMachine;

            Assert.That(GetChild(sm, "Off").NodeId.NamespaceIndex,
                Is.EqualTo(registeredIndex));
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> Build(
            uint nodeIdNumber = 5000)
        {
            return StateMachineTestFixtures.NewBuilder(m_context, nodeIdNumber)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1);
        }

        private BaseInstanceState GetChild(NodeState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            return children.Find(c => c.BrowseName.Name == browseName)!;
        }

        private uint StateNumber(NodeState sm, string stateBrowseName)
        {
            var properties = new List<BaseInstanceState>();
            GetChild(sm, stateBrowseName).GetChildren(m_context, properties);
            var number = (PropertyState<uint>)properties
                .First(p => p.BrowseName.Name == BrowseNames.StateNumber);
            return number.Value;
        }
    }
}
