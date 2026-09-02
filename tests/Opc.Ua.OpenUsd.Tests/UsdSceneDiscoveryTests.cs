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
    /// Fail-fast argument validation and folder-reuse behaviour of
    /// <see cref="UsdSceneDiscovery"/>. Every discovery helper is an extension method, so a
    /// <c>null</c> receiver reaches the body instead of throwing at the call site: each one
    /// must reject it explicitly rather than surface a <see cref="System.NullReferenceException"/>.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdSceneDiscoveryTests
    {
        [Test]
        public void EnsureStagesFolderRejectsANullContext()
        {
            (_, ushort ns, BaseObjectState server) = MaterializationHarness.NewContext();

            Assert.That(
                () => ((ISystemContext)null!).EnsureStagesFolder(server, ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void EnsureStagesFolderRejectsANullServer()
        {
            (SystemContext context, ushort ns, _) = MaterializationHarness.NewContext();

            Assert.That(
                () => context.EnsureStagesFolder(null!, ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void EnsureStagesFolderSkipsUnrelatedSiblingFolders()
        {
            (SystemContext context, ushort ns, BaseObjectState server) =
                MaterializationHarness.NewContext();
            var unrelated = new FolderState(server)
            {
                BrowseName = new QualifiedName("SomethingElse", ns),
                DisplayName = new LocalizedText("SomethingElse"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent
            };
            server.AddChild(unrelated);

            FolderState stages = context.EnsureStagesFolder(server, ns);

            Assert.That(stages.BrowseName.Name, Is.EqualTo(UsdSceneDiscovery.StagesFolderName));
            List<FolderState> roots =
                MaterializationHarness.ChildrenOfType<FolderState>(context, server);
            Assert.That(roots, Has.Count.EqualTo(2));
            Assert.That(
                roots.Count(f => f.BrowseName.Name == UsdSceneDiscovery.OpenUsdSceneRootName),
                Is.EqualTo(1));
        }

        [Test]
        public void TryResolveBindingTargetRejectsANullResult()
        {
            Assert.That(
                () => ((UsdMaterializationResult)null!)
                    .TryResolveBindingTarget("/Shared", "value", out _),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StageAwareTryResolveBindingTargetRejectsANullResult()
        {
            Assert.That(
                () => ((UsdMaterializationResult)null!)
                    .TryResolveBindingTarget(new NodeId(1u, 1), "/Shared", "value", out _),
                Throws.ArgumentNullException);
        }

        [Test]
        public void MultiStageTryResolveBindingTargetRejectsANullSequence()
        {
            Assert.That(
                () => ((IEnumerable<UsdMaterializationResult>)null!)
                    .TryResolveBindingTarget(new NodeId(1u, 1), "/Shared", "value", out _),
                Throws.ArgumentNullException);
        }

        [Test]
        public void MultiStageTryResolveBindingTargetSkipsNullEntries()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            var results = new UsdMaterializationResult?[] { null, ms.Result };

            bool resolved = results!.TryResolveBindingTarget(
                ms.Stage.NodeId, "/Shared", "value", out UsdAttributeState? attribute);

            Assert.That(resolved, Is.True);
            Assert.That(attribute, Is.Not.Null);
        }

        [Test]
        public void TryAuthorBindingTargetNodeIdRejectsANullResult()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());

            Assert.That(
                () => ((UsdMaterializationResult)null!).TryAuthorBindingTargetNodeId(
                    ms.Context, NewBinding(ms.Namespace), "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        [Test]
        public void TryAuthorBindingTargetNodeIdRejectsANullContext()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());

            Assert.That(
                () => ms.Result.TryAuthorBindingTargetNodeId(
                    null!, NewBinding(ms.Namespace), "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        [Test]
        public void TryAuthorBindingTargetNodeIdRejectsANullBinding()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());

            Assert.That(
                () => ms.Result.TryAuthorBindingTargetNodeId(
                    ms.Context, null!, "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StageAwareTryAuthorBindingTargetNodeIdRejectsANullSequence()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());

            Assert.That(
                () => ((IEnumerable<UsdMaterializationResult>)null!).TryAuthorBindingTargetNodeId(
                    ms.Context, NewBinding(ms.Namespace), ms.Stage.NodeId,
                    "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StageAwareTryAuthorBindingTargetNodeIdRejectsANullContext()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            var results = new[] { ms.Result };

            Assert.That(
                () => results.TryAuthorBindingTargetNodeId(
                    null!, NewBinding(ms.Namespace), ms.Stage.NodeId,
                    "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StageAwareTryAuthorBindingTargetNodeIdRejectsANullBinding()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(SingleStage());
            var results = new[] { ms.Result };

            Assert.That(
                () => results.TryAuthorBindingTargetNodeId(
                    ms.Context, null!, ms.Stage.NodeId, "/Shared", "value", ms.Namespace),
                Throws.ArgumentNullException);
        }

        private static UsdStage SingleStage()
        {
            var stage = new UsdStage("Solo") { DefaultPrim = "Shared" };
            var prim = new UsdPrim("Shared", "Xform");
            prim.Attributes.Add(new UsdAttribute("value", "double") { Value = UsdValue.From(1.0) });
            stage.AddRootPrim(prim);
            return stage;
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
    }
}
