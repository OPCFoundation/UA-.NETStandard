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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Part 1 interoperability (§10): a Server hosting several materialized stages disambiguates
    /// a binding target by its <c>TargetStage</c>, and can author Part 1's optional
    /// <c>TargetNodeId</c> alongside the mandatory path form so NodeId-resolving and
    /// path-resolving connectors agree. The <c>TargetNodeId</c> is authored generically (by
    /// BrowseName), so Part 2 has no compile-time dependency on the Part 1 model (§4.2).
    /// </summary>
    [TestFixture]
    public class TargetNodeIdAuthoringTests
    {
        // ---- Stage-aware resolution (§10) ----------------------------------------------

        [Test]
        public void StageAware_Resolves_OnlyForMatchingStage()
        {
            (_, _, UsdMaterializationResult a, UsdMaterializationResult b) = TwoStages();

            // Result A answers for its own stage.
            Assert.That(
                a.TryResolveBindingTarget(a.Stage.NodeId, "/Shared", "value", out UsdAttributeState? fromA),
                Is.True);
            Assert.That(ReferenceEquals(fromA, a.AttributesByPath["/Shared.value"]), Is.True);

            // Result A must NOT answer for a different stage even though the SdfPath exists in it.
            Assert.That(
                a.TryResolveBindingTarget(b.Stage.NodeId, "/Shared", "value", out UsdAttributeState? none),
                Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void StageAware_NullStage_FailsClosed()
        {
            (_, _, UsdMaterializationResult a, _) = TwoStages();

            // The stage-aware form requires a stage; a null NodeId never silently matches.
            Assert.That(a.TryResolveBindingTarget(NodeId.Null, "/Shared", "value", out _), Is.False);
            Assert.That(
                new[] { a }.ResolveBindingTargetNodeId(NodeId.Null, "/Shared", "value").IsNull,
                Is.True);
        }

        [Test]
        public void MultiStage_DisambiguatesByStage_ReturningTheCorrectStagesNode()
        {
            (_, _, UsdMaterializationResult a, UsdMaterializationResult b) = TwoStages();
            var all = new[] { a, b };

            Assert.That(
                all.TryResolveBindingTarget(a.Stage.NodeId, "/Shared", "value", out UsdAttributeState? rA),
                Is.True);
            Assert.That(
                all.TryResolveBindingTarget(b.Stage.NodeId, "/Shared", "value", out UsdAttributeState? rB),
                Is.True);

            // Same SdfPath, different stage -> different materialized node instance.
            Assert.That(ReferenceEquals(rA, a.AttributesByPath["/Shared.value"]), Is.True);
            Assert.That(ReferenceEquals(rB, b.AttributesByPath["/Shared.value"]), Is.True);
            Assert.That(ReferenceEquals(rA, rB), Is.False);
        }

        [Test]
        public void MultiStage_UnknownStage_ResolvesToNull()
        {
            (_, ushort ns, UsdMaterializationResult a, UsdMaterializationResult b) = TwoStages();
            var all = new[] { a, b };

            var unknownStage = new NodeId(999999u, ns);
            Assert.That(
                all.ResolveBindingTargetNodeId(unknownStage, "/Shared", "value").IsNull, Is.True);
            Assert.That(
                all.TryResolveBindingTarget(unknownStage, "/Shared", "value", out _), Is.False);
        }

        // ---- Generic TargetNodeId authoring (§10, §4.2) --------------------------------

        [Test]
        public void TryAuthorBindingTargetNodeId_AuthorsResolvedNodeId_OnGenericBinding()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            BaseObjectState binding = NewBinding(ms.Namespace);

            NodeId expected = ms.Result.ResolveBindingTargetNodeId("/Shared", "value");
            Assert.That(expected.IsNull, Is.False);

            bool authored = ms.Result.TryAuthorBindingTargetNodeId(
                ms.Context, binding, "/Shared", "value", ms.Namespace);
            Assert.That(authored, Is.True);

            BaseVariableState? member = FindTargetNodeId(ms.Context, binding, ms.Namespace);
            Assert.That(member, Is.Not.Null);
            // Authored as a NodeId-typed Variable carrying exactly the resolved node.
            Assert.That(member!.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.NodeId));
            Assert.That(member.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(member.Value.AsBoxedObject(), Is.EqualTo(expected));
            Assert.That(member.NodeId.IsNull, Is.False);
        }

        [Test]
        public void TryAuthorBindingTargetNodeId_BogusPath_FailsClosed_AuthorsNothing()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            BaseObjectState binding = NewBinding(ms.Namespace);

            bool authored = ms.Result.TryAuthorBindingTargetNodeId(
                ms.Context, binding, "/Nope/DoesNotExist", "bogus", ms.Namespace);

            // A binding must never gain a TargetNodeId naming a node that is not in the address space.
            Assert.That(authored, Is.False);
            Assert.That(FindTargetNodeId(ms.Context, binding, ms.Namespace), Is.Null);
        }

        [Test]
        public void TryAuthorBindingTargetNodeId_ReusesExistingMember_Idempotently()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            BaseObjectState binding = NewBinding(ms.Namespace);

            // A generated Part 1 binding already carries a TargetNodeId member; authoring must
            // update it in place rather than adding a duplicate.
            var preexisting = new PropertyState(binding)
            {
                BrowseName = new QualifiedName(UsdSceneDiscovery.TargetNodeIdName, ms.Namespace),
                DisplayName = new LocalizedText(UsdSceneDiscovery.TargetNodeIdName),
                TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                DataType = Opc.Ua.DataTypeIds.NodeId,
                ValueRank = Opc.Ua.ValueRanks.Scalar,
                Value = Variant.From(new NodeId(1234u, ms.Namespace))
            };
            binding.AddChild(preexisting);
            preexisting.NodeId = new NodeId(5678u, ms.Namespace);

            bool authored = ms.Result.TryAuthorBindingTargetNodeId(
                ms.Context, binding, "/Shared", "value", ms.Namespace);
            Assert.That(authored, Is.True);

            List<BaseVariableState> members =
                MaterializationHarness.ChildrenOfType<BaseVariableState>(ms.Context, binding)
                    .Where(v => v.BrowseName.Name == UsdSceneDiscovery.TargetNodeIdName)
                    .ToList();
            Assert.That(members, Has.Count.EqualTo(1));
            Assert.That(ReferenceEquals(members[0], preexisting), Is.True);

            NodeId expected = ms.Result.ResolveBindingTargetNodeId("/Shared", "value");
            Assert.That(members[0].Value.AsBoxedObject(), Is.EqualTo(expected));
        }

        [Test]
        public void TryAuthorBindingTargetNodeId_StageAware_AuthorsForNamedStage()
        {
            (SystemContext context, ushort ns, UsdMaterializationResult a, UsdMaterializationResult b) =
                TwoStages();
            var all = new[] { a, b };
            BaseObjectState binding = NewBinding(ns);

            bool authored = all.TryAuthorBindingTargetNodeId(
                context, binding, b.Stage.NodeId, "/Shared", "value", ns);
            Assert.That(authored, Is.True);

            BaseVariableState? member = FindTargetNodeId(context, binding, ns);
            Assert.That(member, Is.Not.Null);
            Assert.That(
                member!.Value.AsBoxedObject(),
                Is.EqualTo(b.ResolveBindingTargetNodeId("/Shared", "value")));
        }

        [Test]
        public void TryAuthorBindingTargetNodeId_StageAware_WrongStage_FailsClosed()
        {
            (SystemContext context, ushort ns, UsdMaterializationResult a, UsdMaterializationResult b) =
                TwoStages();
            var all = new[] { a, b };
            BaseObjectState binding = NewBinding(ns);

            var unknownStage = new NodeId(987654u, ns);
            bool authored = all.TryAuthorBindingTargetNodeId(
                context, binding, unknownStage, "/Shared", "value", ns);

            Assert.That(authored, Is.False);
            Assert.That(FindTargetNodeId(context, binding, ns), Is.Null);
        }

        // ---- helpers -------------------------------------------------------------------

        private static UsdStage NamedStage(string name)
        {
            var stage = new UsdStage(name) { DefaultPrim = "Shared" };
            var prim = new UsdPrim("Shared", "Xform");
            prim.Attributes.Add(new UsdAttribute("value", "double") { Value = UsdValue.From(1.0) });
            stage.AddRootPrim(prim);
            return stage;
        }

        private static UsdStage SingleStage()
        {
            return NamedStage("Solo");
        }

        private static (SystemContext Context, ushort Namespace,
            UsdMaterializationResult A, UsdMaterializationResult B) TwoStages()
        {
            // Both stages are materialized in ONE context so their UsdStageType Objects receive
            // distinct instance NodeIds — the disambiguation key a real multi-stage Server relies on.
            (SystemContext context, ushort ns, BaseObjectState root) =
                MaterializationHarness.NewContext();
            UsdMaterializationResult a = context.MaterializeUsdStage(root, NamedStage("StageA"), ns);
            UsdMaterializationResult b = context.MaterializeUsdStage(root, NamedStage("StageB"), ns);
            return (context, ns, a, b);
        }

        private static BaseObjectState NewBinding(ushort ns)
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("Binding", ns),
                BrowseName = new QualifiedName("Binding", ns),
                DisplayName = new LocalizedText("Binding"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
        }

        private static BaseVariableState? FindTargetNodeId(
            ISystemContext context, NodeState binding, ushort ns)
        {
            return binding.FindChild(
                context, new QualifiedName(UsdSceneDiscovery.TargetNodeIdName, ns)) as BaseVariableState;
        }
    }
}
