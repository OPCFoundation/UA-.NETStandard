// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Discovery and Part 1 interoperability (§4.3, §10): a well-known stages folder is created
    /// (or the supplied Part 1 folder reused), and live-binding targets resolve by path.
    /// </summary>
    [TestFixture]
    public class DiscoveryInteropTests
    {
        [Test]
        public void EnsureStagesFolder_CreatesStandaloneRoot_WhenNoPart1Folder()
        {
            (SystemContext context, ushort ns, BaseObjectState server) =
                MaterializationHarness.NewContext();

            FolderState stages = context.EnsureStagesFolder(server, ns);

            Assert.That(stages, Is.Not.Null);
            Assert.That(stages.BrowseName.Name, Is.EqualTo(UsdSceneDiscovery.StagesFolderName));
            Assert.That(stages.NodeId.IsNull, Is.False);

            List<FolderState> roots =
                MaterializationHarness.ChildrenOfType<FolderState>(context, server);
            FolderState openUsdScene =
                roots.Single(f => f.BrowseName.Name == UsdSceneDiscovery.OpenUsdSceneRootName);

            List<FolderState> underRoot =
                MaterializationHarness.ChildrenOfType<FolderState>(context, openUsdScene);
            Assert.That(underRoot, Has.Count.EqualTo(1));
            Assert.That(underRoot[0].BrowseName.Name, Is.EqualTo(UsdSceneDiscovery.StagesFolderName));
        }

        [Test]
        public void EnsureStagesFolder_ReturnsSuppliedPart1Folder_AsIs()
        {
            (SystemContext context, ushort ns, BaseObjectState server) =
                MaterializationHarness.NewContext();
            var part1 = new FolderState(null)
            {
                BrowseName = new QualifiedName(UsdSceneDiscovery.StagesFolderName, ns),
                NodeId = new NodeId(9999u, ns)
            };

            FolderState result = context.EnsureStagesFolder(server, ns, part1);

            Assert.That(ReferenceEquals(result, part1), Is.True);
            // The standalone root is not created when Part 1 already provides one.
            List<FolderState> roots =
                MaterializationHarness.ChildrenOfType<FolderState>(context, server);
            Assert.That(
                roots.Any(f => f.BrowseName.Name == UsdSceneDiscovery.OpenUsdSceneRootName),
                Is.False);
        }

        [Test]
        public void EnsureStagesFolder_IsIdempotent()
        {
            (SystemContext context, ushort ns, BaseObjectState server) =
                MaterializationHarness.NewContext();

            FolderState first = context.EnsureStagesFolder(server, ns);
            FolderState second = context.EnsureStagesFolder(server, ns);

            Assert.That(ReferenceEquals(first, second), Is.True);

            List<FolderState> roots =
                MaterializationHarness.ChildrenOfType<FolderState>(context, server);
            Assert.That(
                roots.Count(f => f.BrowseName.Name == UsdSceneDiscovery.OpenUsdSceneRootName),
                Is.EqualTo(1));
        }

        [Test]
        public void TryResolveBindingTarget_ResolvesRealAttribute()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            bool resolved = ms.Result.TryResolveBindingTarget(
                "/Plant/Pumps/P101/Body", "radius", out UsdAttributeState? attribute);

            Assert.That(resolved, Is.True);
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute!.UsdTypeName!.Value, Is.EqualTo("double"));

            NodeId nodeId = ms.Result.ResolveBindingTargetNodeId("/Plant/Pumps/P101/Body", "radius");
            Assert.That(nodeId.IsNull, Is.False);
            Assert.That(nodeId, Is.EqualTo(attribute.NodeId));
        }

        [Test]
        public void TryResolveBindingTarget_FailsCleanly_ForBogusPath()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            bool resolved = ms.Result.TryResolveBindingTarget(
                "/Nope/DoesNotExist", "bogus", out UsdAttributeState? attribute);

            Assert.That(resolved, Is.False);
            Assert.That(attribute, Is.Null);

            NodeId nodeId = ms.Result.ResolveBindingTargetNodeId("/Nope/DoesNotExist", "bogus");
            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public void TryResolveBindingTarget_FailsCleanly_ForEmptyInputs()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            Assert.That(
                ms.Result.TryResolveBindingTarget(string.Empty, "radius", out _), Is.False);
            Assert.That(
                ms.Result.TryResolveBindingTarget("/Plant", string.Empty, out _), Is.False);
        }
    }
}
