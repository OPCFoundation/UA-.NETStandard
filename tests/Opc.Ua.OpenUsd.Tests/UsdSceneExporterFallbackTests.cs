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
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Unit tests for the compatibility fallbacks of the exporter: a stage whose attributes
    /// carry no <c>ConnectionPaths</c> member — because it was materialized by an older build
    /// or by a foreign server — must still export its connections, first from the recorded side
    /// channel and, failing that, from the browsable <c>UsdConnection</c> edges. The exporter
    /// also has to fail fast on a missing argument and tolerate members a foreign address space
    /// simply does not have.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdSceneExporterFallbackTests
    {
        [Test]
        public void ExportUsdStageRejectsANullResult()
        {
            (SystemContext context, ushort _, BaseObjectState _) =
                MaterializationHarness.NewContext();
            UsdMaterializationResult result = null!;

            Assert.That(() => context.ExportUsdStage(result), Throws.ArgumentNullException);
        }

        [Test]
        public void ExportUsdStageRejectsANullContext()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(ConnectedScene());
            ISystemContext context = null!;

            Assert.That(
                () => context.ExportUsdStage(scene.Stage), Throws.ArgumentNullException);
        }

        [Test]
        public void ExportUsdStageRejectsANullStageNode()
        {
            (SystemContext context, ushort _, BaseObjectState _) =
                MaterializationHarness.NewContext();
            UsdStageState stageNode = null!;

            Assert.That(() => context.ExportUsdStage(stageNode), Throws.ArgumentNullException);
        }

        [Test]
        public void ConnectionsAreRecoveredFromTheRecordedSideChannel()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(ConnectedScene());
            DropConnectionPaths(scene);

            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);

            UsdAttribute radius = ExportedAttribute(exported, "radius");
            Assert.That(radius.Connections, Has.Count.EqualTo(2));
            Assert.That(radius.Connections[0], Is.EqualTo("/Mesh.size"));
            Assert.That(
                radius.Connections[1],
                Is.EqualTo("/Elsewhere.size"),
                "The recorded side channel keeps an out-of-subtree target.");
        }

        [Test]
        public void ConnectionsAreRebuiltFromTheBrowsableEdgesForABareStageNode()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(ConnectedScene());
            DropConnectionPaths(scene);

            UsdStage exported = scene.Context.ExportUsdStage(scene.Stage);

            UsdAttribute radius = ExportedAttribute(exported, "radius");
            Assert.That(
                radius.Connections,
                Has.Count.EqualTo(1),
                "Only the in-subtree target has a browsable edge to rebuild from.");
            Assert.That(radius.Connections[0], Does.StartWith("/Mesh."));
        }

        [Test]
        public void AMetadataEntryWithoutABrowseNameIsSkipped()
        {
            var prim = new UsdPrim("Mesh", "Mesh");
            prim.Metadata["author"] = UsdValue.FromString("Ada");
            var stage = new UsdStage("Test");
            stage.AddRootPrim(prim);
            MaterializedScene scene = MaterializationHarness.Materialize(stage);

            FolderState metadata = scene.Prim("/Mesh").Metadata!;
            var nameless = new PropertyState(metadata)
            {
                NodeId = new NodeId(90210u, scene.Namespace),
                BrowseName = new QualifiedName(string.Empty, scene.Namespace),
                DisplayName = new LocalizedText("nameless"),
                TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                DataType = Opc.Ua.DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant("ignored")
            };
            metadata.AddChild(nameless);

            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);

            UsdPrim exportedPrim = exported.RootPrims[0];
            Assert.That(exportedPrim.Metadata, Has.Count.EqualTo(1));
            Assert.That(exportedPrim.Metadata.ContainsKey("author"), Is.True);
        }

        [Test]
        public void AMissingEnumMemberFallsBackToItsDefault()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(ConnectedScene());
            UsdPrimState primNode = scene.Prim("/Mesh");
            primNode.Kind = null;

            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);

            Assert.That(exported.RootPrims[0].Kind, Is.EqualTo(UsdPrimKindEnum.Unspecified));
        }

        [Test]
        public void AnEnumMemberHoldingAnUnreadableValueFallsBackToItsDefault()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(ConnectedScene());
            UsdPrimState primNode = scene.Prim("/Mesh");
            primNode.Kind!.WrappedValue = new Variant("not-an-enum");

            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);

            Assert.That(exported.RootPrims[0].Kind, Is.EqualTo(UsdPrimKindEnum.Unspecified));
        }

        private static UsdStage ConnectedScene()
        {
            var prim = new UsdPrim("Mesh", "Mesh") { Kind = UsdPrimKindEnum.Component };
            prim.Attributes.Add(new UsdAttribute("size", "double") { Value = UsdValue.From(2.0) });
            var radius = new UsdAttribute("radius", "double") { Value = UsdValue.From(1.0) };
            radius.Connections.Add("/Mesh.size");
            radius.Connections.Add("/Elsewhere.size");
            prim.Attributes.Add(radius);

            var stage = new UsdStage("Test");
            stage.AddRootPrim(prim);
            return stage;
        }

        private static void DropConnectionPaths(MaterializedScene scene)
        {
            foreach (KeyValuePair<string, UsdAttributeState> entry in scene.Result.AttributesByPath)
            {
                entry.Value.ConnectionPaths = null;
            }
        }

        private static UsdAttribute ExportedAttribute(UsdStage stage, string name)
        {
            foreach (UsdPrim prim in stage.AllPrims())
            {
                foreach (UsdAttribute attribute in prim.Attributes)
                {
                    if (attribute.Name == name)
                    {
                        return attribute;
                    }
                }
            }
            Assert.Fail("The exported scene does not carry an attribute named " + name + ".");
            return null!;
        }
    }
}
