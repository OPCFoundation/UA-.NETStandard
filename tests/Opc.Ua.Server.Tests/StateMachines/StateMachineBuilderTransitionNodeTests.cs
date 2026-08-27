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
    /// Unit tests for the <c>TransitionType</c> nodes that
    /// <see cref="FluentFiniteStateMachineState"/> materializes for its
    /// declared transitions — the Part 16 §B.4 counterpart to the
    /// <c>StateType</c> nodes, which makes <c>LastTransition/Id</c>
    /// resolve to a real node and populates <c>AvailableTransitions</c>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderTransitionNodeTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void EveryDeclaredTransitionGetsATransitionTypeNode()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            var offToOn = (BaseObjectState)GetChild(sm, "OffToOn");

            Assert.Multiple(() =>
            {
                Assert.That(offToOn.TypeDefinitionId,
                    Is.EqualTo(ObjectTypeIds.TransitionType));
                Assert.That(offToOn.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(GetChild(sm, "OnToOff"), Is.Not.Null);
            });
        }

        [Test]
        public void TransitionNodeCarriesTransitionNumberProperty()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.Multiple(() =>
            {
                Assert.That(TransitionNumber(sm, "OffToOn"), Is.EqualTo(10u));
                Assert.That(TransitionNumber(sm, "OnToOff"), Is.EqualTo(20u));
            });
        }

        [Test]
        public void TransitionNodeLinksItsFromAndToStates()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            BaseInstanceState offToOn = GetChild(sm, "OffToOn");

            Assert.Multiple(() =>
            {
                Assert.That(Targets(offToOn, ReferenceTypeIds.FromState),
                    Is.EqualTo(new[] { GetChild(sm, "Off").NodeId }));
                Assert.That(Targets(offToOn, ReferenceTypeIds.ToState),
                    Is.EqualTo(new[] { GetChild(sm, "On").NodeId }));
            });
        }

        [Test]
        public void StateNodesCarryTheInverseFromAndToStateReferences()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            // Inverse-browsing FromState/ToState from a state node is
            // how a §B.4 client discovers the state's transitions.
            Assert.Multiple(() =>
            {
                Assert.That(
                    InverseTargets(GetChild(sm, "Off"), ReferenceTypeIds.FromState),
                    Is.EqualTo(new[] { GetChild(sm, "OffToOn").NodeId }));
                Assert.That(
                    InverseTargets(GetChild(sm, "On"), ReferenceTypeIds.ToState),
                    Is.EqualTo(new[] { GetChild(sm, "OffToOn").NodeId }));
            });
        }

        private NodeId[] InverseTargets(BaseInstanceState node, NodeId referenceTypeId)
        {
            var references = new List<IReference>();
            node.GetReferences(m_context, references, referenceTypeId, true);
            return [.. references.Select(r =>
                ExpandedNodeId.ToNodeId(r.TargetId, m_context.NamespaceUris))];
        }

        [Test]
        public void HasEffectFollowsTheTransitionDeclaration()
        {
            FluentFiniteStateMachineState sm = StateMachineTestFixtures
                .NewBuilder(m_context)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1, hasEffect: false)
                .StateMachine;

            Assert.Multiple(() =>
            {
                Assert.That(
                    Targets(GetChild(sm, "OffToOn"), ReferenceTypeIds.HasEffect),
                    Is.EqualTo(new[] { ObjectTypeIds.TransitionEventType }));
                Assert.That(
                    Targets(GetChild(sm, "OnToOff"), ReferenceTypeIds.HasEffect),
                    Is.Empty);
            });
        }

        [Test]
        public void LastTransitionIsMaterializedAlongsideTheTransitions()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            // Optional on FiniteStateMachineType, so nothing creates it
            // by default — but without it the base class has nowhere to
            // record a transition and clients have nothing to observe.
            Assert.That(sm.LastTransition, Is.Not.Null);
            Assert.That(sm.LastTransition!.BrowseName,
                Is.EqualTo(new QualifiedName(BrowseNames.LastTransition)));
        }

        [Test]
        public void LastTransitionIdResolvesToTheMaterializedNode()
        {
            FluentFiniteStateMachineState sm = Build()
                .WithInitialState(1)
                .StateMachine;

            sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That(sm.LastTransition!.Id!.Value,
                Is.EqualTo(GetChild(sm, "OffToOn").NodeId));
        }

        [Test]
        public void GetTransitionIdRoundTripsThroughGetTransitionNodeId()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.Multiple(() =>
            {
                Assert.That(sm.GetTransitionId(sm.GetTransitionNodeId(10)),
                    Is.EqualTo(10u));
                Assert.That(sm.GetTransitionId(sm.GetTransitionNodeId(20)),
                    Is.EqualTo(20u));
                Assert.That(sm.GetTransitionId(new NodeId("nonsense", 1)), Is.Zero);
            });
        }

        [Test]
        public void AvailableTransitionsListsTheMaterializedTransitionNodes()
        {
            FluentFiniteStateMachineState sm = Build().StateMachine;

            Assert.That(sm.AvailableTransitions, Is.Not.Null);
            NodeId[] published = [.. sm.AvailableTransitions!.Value];

            Assert.That(published, Is.EqualTo(new[]
            {
                GetChild(sm, "OffToOn").NodeId,
                GetChild(sm, "OnToOff").NodeId
            }));

            // The standard NodeSet models AvailableTransitions as a
            // HasComponent BaseDataVariable, and that is the reference
            // type GetAvailableTransitionsAsync translates the path with.
            Assert.That(sm.AvailableTransitions.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(sm.AvailableTransitions.BrowseName,
                Is.EqualTo(new QualifiedName(BrowseNames.AvailableTransitions)));
        }

        [Test]
        public void WithCauseLinksTheMethodToEveryTransitionItTriggers()
        {
            var methodNodeId = new NodeId(7001u, 1);

            StateMachineBuilder<FluentFiniteStateMachineState> builder = Build();
            FluentFiniteStateMachineState sm = builder.StateMachine;

            // A cause method has to be a child of the machine before
            // WithCause can resolve it.
            var start = new MethodState(sm)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = methodNodeId,
                BrowseName = new QualifiedName("Start", 1),
                DisplayName = new LocalizedText("Start"),
                Executable = true,
                UserExecutable = true
            };
            sm.AddChild(start);

            builder.WithCause(methodNodeId);

            Assert.Multiple(() =>
            {
                Assert.That(Targets(GetChild(sm, "OffToOn"), ReferenceTypeIds.HasCause),
                    Is.EqualTo(new[] { methodNodeId }));
                // The cause only maps to transition 10.
                Assert.That(Targets(GetChild(sm, "OnToOff"), ReferenceTypeIds.HasCause),
                    Is.Empty);

                // ... and the method carries the inverse edge, so
                // inverse-browsing HasCause from the method finds the
                // transition it triggers.
                var inverse = new List<IReference>();
                start.GetReferences(
                    m_context, inverse, ReferenceTypeIds.HasCause, true);
                Assert.That(inverse, Has.Count.EqualTo(1));
                Assert.That(inverse[0].TargetId,
                    Is.EqualTo((ExpandedNodeId)GetChild(sm, "OffToOn").NodeId));
            });
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> Build()
        {
            return StateMachineTestFixtures.NewBuilder(m_context)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1)
                .OnCause(7001, from: 1, transition: 10);
        }

        private BaseInstanceState GetChild(NodeState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            return children.Find(c => c.BrowseName.Name == browseName)!;
        }

        private NodeId[] Targets(BaseInstanceState node, NodeId referenceTypeId)
        {
            var references = new List<IReference>();
            node.GetReferences(m_context, references, referenceTypeId, false);
            return [.. references.Select(r =>
                ExpandedNodeId.ToNodeId(r.TargetId, m_context.NamespaceUris))];
        }

        private uint TransitionNumber(NodeState sm, string transitionBrowseName)
        {
            var properties = new List<BaseInstanceState>();
            GetChild(sm, transitionBrowseName).GetChildren(m_context, properties);
            var number = (PropertyState<uint>)properties
                .First(p => p.BrowseName.Name == BrowseNames.TransitionNumber);
            return number.Value;
        }
    }
}
