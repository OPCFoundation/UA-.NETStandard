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
using NUnit.Framework;
using Opc.Ua.OpenUsd;

namespace Opc.Ua.OpenUsd.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OpenUsdRepresentationAuthoring"/>: attaching an
    /// <c>OpenUsdRepresentation</c> AddIn to any Object (spec §4.2), registering it in the
    /// well-known discovery registry, and the fail-fast argument validation every authoring
    /// helper performs before it touches the address space.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdRepresentationAuthoringTests
    {
        [Test]
        public void CreateRepresentationAttachesABrowsableAddInToTheOwner()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");

            OpenUsdRepresentationState rep = context.CreateRepresentation(
                owner, stage.NodeId, "/Cell/Robots/R1", ns);

            Assert.That(rep.BrowseName.Name, Is.EqualTo("OpenUsdRepresentation"));
            Assert.That(rep.BrowseName.NamespaceIndex, Is.EqualTo(ns));
            Assert.That(
                rep.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasAddIn),
                "The representation is an AddIn and must be mounted with HasAddIn.");
            Assert.That(rep.NodeId.IsNull, Is.False);
            Assert.That(rep.Stage!.Value, Is.EqualTo(stage.NodeId));
            Assert.That(rep.PrimPath!.Value, Is.EqualTo("/Cell/Robots/R1"));

            var children = new List<BaseInstanceState>();
            owner.GetChildren(context, children);
            Assert.That(children, Has.Member(rep));
        }

        [Test]
        public void RepresentedInterfacePlaceholderIsMountedWithHasAddIn()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            var represented = new IOpenUsdRepresentedState(null);

            OpenUsdRepresentationState rep = represented.AddOpenUsdRepresentation_Placeholder(
                context,
                new QualifiedName("OpenUsdRepresentation", ns));

            Assert.That(rep.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
        }

        [Test]
        public void CreateRepresentationAssignsDistinctInstanceNodeIds()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            BaseObjectState firstOwner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");
            BaseObjectState secondOwner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot2");

            OpenUsdRepresentationState first = context.CreateRepresentation(
                firstOwner, stage.NodeId, "/Cell/Robots/R1", ns);
            OpenUsdRepresentationState second = context.CreateRepresentation(
                secondOwner, stage.NodeId, "/Cell/Robots/R2", ns);

            Assert.That(first.NodeId, Is.Not.EqualTo(second.NodeId));
        }

        [Test]
        public void CreateRepresentationRejectsAContextWithoutANodeIdFactory()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");
            context.NodeIdFactory = null!;

            Assert.That(
                () => context.CreateRepresentation(
                    owner, new NodeId(1u, ns), "/Cell/Robots/R1", ns),
                Throws.InvalidOperationException);
        }

        [Test]
        public void CreateRepresentationRejectsANullContext()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");

            Assert.That(
                () => ((ISystemContext)null!).CreateRepresentation(
                    owner, new NodeId(1u, ns), "/Cell", ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateRepresentationRejectsANullOwner()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();

            Assert.That(
                () => context.CreateRepresentation(null!, new NodeId(1u, ns), "/Cell", ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterInDiscoveryAddsTheOrganizesReferencePair()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");
            OpenUsdRepresentationState rep = context.CreateRepresentation(
                owner, stage.NodeId, "/Cell/Robots/R1", ns);
            FolderState registry = root.Representations!;

            rep.RegisterInDiscovery(registry);

            Assert.That(
                registry.ReferenceExists(ReferenceTypeIds.Organizes, false, rep.NodeId), Is.True);
            Assert.That(
                rep.ReferenceExists(ReferenceTypeIds.Organizes, true, registry.NodeId), Is.True);
        }

        [Test]
        public void RegisterInDiscoveryIgnoresAMissingRegistry()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Robot1");
            OpenUsdRepresentationState rep = context.CreateRepresentation(
                owner, stage.NodeId, "/Cell/Robots/R1", ns);

            Assert.That(() => rep.RegisterInDiscovery(null!), Throws.Nothing);

            var references = new List<IReference>();
            rep.GetReferences(context, references, ReferenceTypeIds.Organizes, false);
            Assert.That(references, Is.Empty);
        }

        [Test]
        public void RegisterInDiscoveryRejectsANullRepresentation()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);

            Assert.That(
                () => ((OpenUsdRepresentationState)null!).RegisterInDiscovery(root.Representations!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddLiveBindingRejectsANullRepresentation()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();

            Assert.That(
                () => ((OpenUsdRepresentationState)null!).AddLiveBinding(
                    context, ns, new NodeId(1u, ns), "Pose", Guid.NewGuid(), new NodeId(2u, ns),
                    "/Cell/Robots/R1", "xformOp:rotateZ", "double",
                    OpenUsdRenderTargetKindEnum.Rotation, 1.0),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddLiveBindingRejectsANullContext()
        {
            OpenUsdRepresentationState rep = NewRepresentation(out _, out ushort ns);

            Assert.That(
                () => rep.AddLiveBinding(
                    null!, ns, new NodeId(1u, ns), "Pose", Guid.NewGuid(), new NodeId(2u, ns),
                    "/Cell/Robots/R1", "xformOp:rotateZ", "double",
                    OpenUsdRenderTargetKindEnum.Rotation, 1.0),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddComponentBindingRejectsANullRepresentation()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();

            Assert.That(
                () => ((OpenUsdRepresentationState)null!).AddComponentBinding(
                    context, ns, "Tool", Guid.NewGuid(), OpenUsdCardinalityEnum.One,
                    OpenUsdCompositionArcEnum.Reference, "/Cell/Robots/R1/Tool"),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddComponentBindingRejectsANullContext()
        {
            OpenUsdRepresentationState rep = NewRepresentation(out _, out ushort ns);

            Assert.That(
                () => rep.AddComponentBinding(
                    null!, ns, "Tool", Guid.NewGuid(), OpenUsdCardinalityEnum.One,
                    OpenUsdCompositionArcEnum.Reference, "/Cell/Robots/R1/Tool"),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddLiveBindingAuthorsTheMandatoryBaseMembers()
        {
            OpenUsdRepresentationState rep = NewRepresentation(
                out SystemContext context, out ushort ns);
            var stageId = new NodeId(4242u, ns);
            var sourceId = new NodeId(77u, ns);
            Guid definition = Guid.NewGuid();

            OpenUsdLiveBindingState binding = rep.AddLiveBinding(
                context, ns, stageId, "Pose", definition, sourceId,
                "/Cell/Robots/R1", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 2.5);

            Assert.That(binding.BindingDefinitionId!.Value, Is.EqualTo(new Uuid(definition)));
            Assert.That(binding.Enabled!.Value, Is.True);
            Assert.That(binding.TargetStage!.Value, Is.EqualTo(stageId));
            Assert.That(binding.TargetPrimPath!.Value, Is.EqualTo("/Cell/Robots/R1"));
            Assert.That(binding.TargetPropertyName!.Value, Is.EqualTo("xformOp:rotateZ"));
            Assert.That(binding.TargetUsdTypeName!.Value, Is.EqualTo("double"));
            Assert.That(binding.SourceNodeId!.Value, Is.EqualTo(sourceId));
            Assert.That(binding.Scale!.Value, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(
                binding.RenderTargetKind!.Value, Is.EqualTo(OpenUsdRenderTargetKindEnum.Rotation));
            Assert.That(
                binding.BadQualityAction!.Value, Is.EqualTo(OpenUsdBadQualityActionEnum.Skip));
        }

        [Test]
        public void AddComponentBindingAuthorsTheMandatoryBaseMembers()
        {
            OpenUsdRepresentationState rep = NewRepresentation(
                out SystemContext context, out ushort ns);
            Guid definition = Guid.NewGuid();

            OpenUsdComponentBindingState binding = rep.AddComponentBinding(
                context, ns, "Tool", definition, OpenUsdCardinalityEnum.Many,
                OpenUsdCompositionArcEnum.Reference, "/Cell/Robots/R1/Tool",
                assetReference: "@tool.usda@</Tool>", dynamic: true);

            Assert.That(binding.BindingDefinitionId!.Value, Is.EqualTo(new Uuid(definition)));
            Assert.That(binding.Enabled!.Value, Is.True);
            Assert.That(binding.Cardinality!.Value, Is.EqualTo(OpenUsdCardinalityEnum.Many));
            Assert.That(
                binding.CompositionArc!.Value, Is.EqualTo(OpenUsdCompositionArcEnum.Reference));
            Assert.That(binding.TargetPrimPath!.Value, Is.EqualTo("/Cell/Robots/R1/Tool"));
            Assert.That(
                binding.ComponentAssetReference!.Value, Is.EqualTo("@tool.usda@</Tool>"));
            Assert.That(binding.Dynamic!.Value, Is.True);
        }

        private static OpenUsdRepresentationState NewRepresentation(
            out SystemContext context, out ushort ns)
        {
            (SystemContext created, ushort companionNs) = OpenUsdAuthoringHarness.NewContext();
            context = created;
            ns = companionNs;
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(created, companionNs);
            OpenUsdStageState stage =
                OpenUsdAuthoringHarness.NewStage(created, root, companionNs, "Cell");
            BaseObjectState owner =
                OpenUsdAuthoringHarness.NewOwner(created, companionNs, "Robot1");
            return created.CreateRepresentation(
                owner, stage.NodeId, "/Cell/Robots/R1", companionNs);
        }
    }
}
