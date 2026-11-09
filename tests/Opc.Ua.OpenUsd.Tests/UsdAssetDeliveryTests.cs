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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd;

namespace Opc.Ua.OpenUsd.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UsdAssetDelivery"/> (spec §5.15, conformance unit
    /// OU-AssetDelivery): a served USD layer is materialised as an
    /// <c>OpenUsdAssetType</c> instance whose Part 5 <see cref="FileState"/> handlers
    /// stream the bytes read-only, hand out independent per-Open cursors, and fail closed
    /// for a write-mode Open, a stale handle, or a negative length.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdAssetDeliveryTests
    {
        private const byte ReadMode = 0x01;
        private const byte WriteMode = 0x02;

        [Test]
        public async Task AttachStageAssetsCreatesTheAssetsFolderAndOneAssetPerLayerAsync()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            ArrayOf<OpenUsdAssetState> created = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns,
                new ServedAsset[]
                {
                    new("Cell.usda", OpenUsdAssetKindEnum.RootLayer, Bytes("#usda 1.0")),
                    new("robot.usda", OpenUsdAssetKindEnum.Reference, Bytes("#usda 1.0\ndef Xform {}"))
                });

            Assert.That(created.Count, Is.EqualTo(2));
            Assert.That(
                root.FindChild(context, new QualifiedName("Artifacts", ns)),
                Is.Not.Null,
                "Served layers must be owned by the server-wide artifact registry.");
            Assert.That(stage.Assets, Is.Not.Null);
            Assert.That(created[0].AssetIdentifier!.Value, Is.EqualTo("Cell.usda"));
            Assert.That(created[0].AssetKind!.Value, Is.EqualTo(OpenUsdAssetKindEnum.RootLayer));
            Assert.That(created[0].MediaType!.Value, Is.EqualTo("model/vnd.usda"));
            Assert.That(created[1].AssetIdentifier!.Value, Is.EqualTo("robot.usda"));
            Assert.That(created[1].AssetKind!.Value, Is.EqualTo(OpenUsdAssetKindEnum.Reference));

            context.AssignInstanceChildNodeIds(root);
            var typeTable = new TypeTable(context.NamespaceUris);
            typeTable.AddReferenceSubtype(
                ReferenceTypeIds.HierarchicalReferences,
                NodeId.Null,
                QualifiedName.From(Opc.Ua.BrowseNames.HierarchicalReferences));
            typeTable.AddReferenceSubtype(
                ReferenceTypeIds.Organizes,
                ReferenceTypeIds.HierarchicalReferences,
                QualifiedName.From(Opc.Ua.BrowseNames.Organizes));
            context.TypeTable = typeTable;
            var references = new List<IReference>();
            using INodeBrowser browser = stage.Assets!.CreateBrowser(
                context, null, ReferenceTypeIds.Organizes, false, BrowseDirection.Forward, default, null, false);
            for (IReference? reference = await browser.NextAsync().ConfigureAwait(false);
                reference != null;
                reference = await browser.NextAsync().ConfigureAwait(false))
            {
                references.Add(reference);
            }

            Assert.That(references, Has.Count.EqualTo(created.Count));
            foreach (OpenUsdAssetState asset in created)
            {
                Assert.That(references.Exists(reference => reference.TargetId == asset.NodeId), Is.True);
            }
        }

        [Test]
        public void AttachStageAssetsReusesAnExistingAssetsFolder()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset("first.usda") });
            FolderState first = stage.Assets!;

            UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset("second.usda") });

            Assert.That(ReferenceEquals(stage.Assets, first), Is.True);
        }

        [Test]
        public void StageViewsShareRegistryResourcesWithoutOwningThem()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState first = OpenUsdAuthoringHarness.NewStage(context, root, ns, "First");
            OpenUsdStageState second = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Second");

            OpenUsdAssetState firstAsset = UsdAssetDelivery.AttachStageAssets(
                context, first, ns, new ServedAsset[] { Asset("./shared.usda") }, "pumps")[0];
            OpenUsdAssetState secondAsset = UsdAssetDelivery.AttachStageAssets(
                context, second, ns, new ServedAsset[] { Asset("shared.usda") }, "pumps")[0];
            context.AssignInstanceChildNodeIds(root);

            Assert.That(secondAsset, Is.SameAs(firstAsset));
            Assert.That(root.Artifacts, Is.Not.Null);
            Assert.That(((OpenUsdAssetGroupState)firstAsset.Parent!).Parent, Is.SameAs(root.Artifacts));
            var owned = new List<BaseInstanceState>();
            first.Assets!.GetChildren(context, owned);
            Assert.That(owned, Is.Empty, "A stage view must not own or duplicate registry resources.");

            first.Assets.RemoveReference(ReferenceTypeIds.Organizes, false, firstAsset.NodeId);
            Assert.That(
                firstAsset.Parent!.FindChild(context, firstAsset.BrowseName),
                Is.SameAs(firstAsset),
                "Removing view membership must leave the registry resource intact.");
            var references = new List<IReference>();
            second.Assets!.GetReferences(context, references);
            Assert.That(references.Exists(reference =>
                reference.ReferenceTypeId == ReferenceTypeIds.Organizes &&
                !reference.IsInverse && reference.TargetId == firstAsset.NodeId), Is.True);
        }

        [Test]
        public void RegistryResourcesHaveDistinctExactVersionsWithTheSameReadOnlyContent()
        {
            byte[] payload = Bytes("immutable layer");
            byte[] input = (byte[])payload.Clone();
            OpenUsdAssetState asset = SingleAsset(input, out SystemContext context);
            input[0] = (byte)'X';
            var children = new List<BaseInstanceState>();
            asset.Versions!.GetChildren(context, children);
            OpenUsdAssetState version = children.OfType<OpenUsdAssetState>().Single();
            var group = (OpenUsdAssetGroupState)asset.Parent!;

            Assert.That(
                asset.FindChild(context, new QualifiedName(
                    Opc.Ua.XRegistry.BrowseNames.Versions,
                    (ushort)context.NamespaceUris.GetIndex(Opc.Ua.XRegistry.Namespaces.xRegistry))),
                Is.SameAs(asset.Versions),
                "Generic xRegistry clients must discover the narrowed Versions component.");
            Assert.That(group.AssetContainerId!.Value, Is.EqualTo(group.GroupId!.Value));
            Assert.That(group.BrowseName.Name, Is.EqualTo(group.GroupId.Value));
            Assert.That(asset.ResourceId!.Value, Is.EqualTo("Cell.usda"));
            Assert.That(asset.Name!.Value, Is.EqualTo(asset.AssetIdentifier!.Value));
            Assert.That(asset.Xid!.Value, Is.EqualTo("/usdassetgroups/Cell/usdassets/Cell.usda"));
            Assert.That(version.Xid!.Value, Is.EqualTo(asset.Xid.Value + "/versions/1"));
            Assert.That(version.NodeId, Is.Not.EqualTo(asset.NodeId));
            Assert.That(version.BrowseName.Name, Is.EqualTo(version.VersionId!.Value));
            Assert.That(version.ResourceId!.Value, Is.EqualTo(asset.ResourceId.Value));
            Assert.That(version.AssetIdentifier!.Value, Is.EqualTo(asset.AssetIdentifier.Value));
            Assert.That(version.Name!.Value, Is.EqualTo(asset.Name.Value));
            Assert.That(version.ContentType!.Value, Is.EqualTo(asset.MediaType!.Value));
            Assert.That(version.Epoch!.Value, Is.EqualTo(1));
            Assert.That(version.Versions, Is.Null, "An exact Version must not have its own Versions folder.");
            Assert.That(version.Writable!.Value, Is.False);
            Assert.That(version.UserWritable!.Value, Is.False);
            Assert.That(version.Digest!.Value, Is.EqualTo(asset.Digest!.Value));

            foreach (OpenUsdAssetState file in new[] { asset, version })
            {
                uint handle = OpenHandle(file, context);
                ByteString data = default;
                Assert.That(file.Read!.OnCall!(
                    context, file.Read, file.NodeId, handle, payload.Length, ref data),
                    Is.EqualTo(ServiceResult.Good));
                Assert.That(data.Span.SequenceEqual(payload), Is.True);
            }
        }

        [Test]
        public void RegistryResourcesDisambiguateCollidingSymbolicIdentifiers()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            ArrayOf<OpenUsdAssetState> assets = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset("a b.usda"), Asset("a-b.usda") });

            Assert.That(assets[0].ResourceId!.Value, Is.Not.EqualTo(assets[1].ResourceId!.Value));
            Assert.That(assets[0].NodeId, Is.Not.EqualTo(assets[1].NodeId));
            OpenUsdAssetState repeated = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset("a-b.usda") })[0];
            Assert.That(repeated, Is.SameAs(assets[1]));
        }

        [TestCase("Cell A", "Cell-A")]
        [TestCase("Cell", "cell")]
        [TestCase("Cell", "Cell")]
        public void DefaultContainersDisambiguateStagesAndReuseTheirAllocations(string firstName, string secondName)
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState first = OpenUsdAuthoringHarness.NewStage(context, root, ns, firstName);
            OpenUsdStageState second = OpenUsdAuthoringHarness.NewStage(context, root, ns, secondName);
            var firstDefinition = new ServedAsset("Cell.usda", OpenUsdAssetKindEnum.RootLayer, Bytes("first layer"));
            var secondDefinition = new ServedAsset("Cell.usda", OpenUsdAssetKindEnum.RootLayer, Bytes("second layer"));

            OpenUsdAssetState firstAsset = UsdAssetDelivery.AttachStageAssets(
                context, first, ns, new[] { firstDefinition })[0];
            OpenUsdAssetState secondAsset = UsdAssetDelivery.AttachStageAssets(
                context, second, ns, new[] { secondDefinition })[0];

            Assert.That(secondAsset.Parent, Is.Not.SameAs(firstAsset.Parent));
            Assert.That(
                ((OpenUsdAssetGroupState)secondAsset.Parent!).GroupId!.Value,
                Is.Not.EqualTo(((OpenUsdAssetGroupState)firstAsset.Parent!).GroupId!.Value).IgnoreCase);
            Assert.That(
                UsdAssetDelivery.AttachStageAssets(context, first, ns, new[] { firstDefinition })[0],
                Is.SameAs(firstAsset));
            Assert.That(
                UsdAssetDelivery.AttachStageAssets(context, second, ns, new[] { secondDefinition })[0],
                Is.SameAs(secondAsset));
        }

        [Test]
        public void DefaultContainersDoNotImplicitlyShareExplicitContainers()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState first = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Explicit");
            OpenUsdStageState second = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            OpenUsdAssetState explicitAsset = UsdAssetDelivery.AttachStageAssets(
                context, first, ns, new ServedAsset[] { Asset("Cell.usda") }, "Cell")[0];
            OpenUsdAssetState defaultAsset = UsdAssetDelivery.AttachStageAssets(
                context, second, ns, new ServedAsset[] { Asset("Cell.usda") })[0];

            Assert.That(defaultAsset.Parent, Is.Not.SameAs(explicitAsset.Parent));
            string defaultContainer = ((OpenUsdAssetGroupState)defaultAsset.Parent!).GroupId!.Value;
            Assert.That(
                UsdAssetDelivery.AttachStageAssets(
                    context, first, ns, new ServedAsset[] { Asset("Cell.usda") }, defaultContainer)[0],
                Is.SameAs(defaultAsset),
                "Explicit container identifiers must still allow intentional sharing.");
        }

        [Test]
        public void NamespaceMetadataUsesStandardQualifiedBrowseNames()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaOpenUsd(context);
            NodeState metadata = nodes.Single(node => node.NodeId == new NodeId(6076u, ns));

            foreach (string name in new[]
            {
                Opc.Ua.BrowseNames.NamespaceUri,
                Opc.Ua.BrowseNames.NamespaceVersion,
                Opc.Ua.BrowseNames.NamespacePublicationDate,
                Opc.Ua.BrowseNames.IsNamespaceSubset
            })
            {
                BaseInstanceState? property = metadata.FindChild(context, new QualifiedName(name, 0));
                Assert.That(property, Is.Not.Null, $"The standard qualified browse path for {name} must resolve.");
                Assert.That(property!.NodeId.NamespaceIndex, Is.EqualTo(ns));
            }
        }

        [Test]
        public void ExactVersionPlaceholderUsesXRegistryQualifiedBrowseName()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaOpenUsd(context);
            NodeState versionsType = nodes.Single(node => node.NodeId == new NodeId(1015u, ns));
            BaseInstanceState? placeholder = versionsType.FindChild(
                context, new QualifiedName(
                    "<Version>", (ushort)context.NamespaceUris.GetIndex(Opc.Ua.XRegistry.Namespaces.xRegistry)));

            Assert.That(placeholder, Is.Not.Null);
            Assert.That(placeholder!.NodeId.NamespaceIndex, Is.EqualTo(ns));
            Assert.That(placeholder.TypeDefinitionId, Is.EqualTo(new NodeId(1006u, ns)));
        }

        [Test]
        public void RegistryBootstrapCapabilitiesAreReadableAsAReadOnlyFile()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            UsdAssetDelivery.AttachStageAssets(context, stage, ns, new ServedAsset[] { Asset("Cell.usda") });
            Assert.That(root.Artifacts!.SpecVersion!.Value, Is.EqualTo("0.1.0"));

            FileState capabilities = root.Artifacts.Capabilities!;
            uint metadataHandle = 0;
            Assert.That(capabilities.Open!.OnCall!(
                context, capabilities.Open, capabilities.NodeId, ReadMode, ref metadataHandle),
                Is.EqualTo(ServiceResult.Good));
            ByteString json = default;
            Assert.That(capabilities.Read!.OnCall!(
                context, capabilities.Read, capabilities.NodeId, metadataHandle, 1024, ref json),
                Is.EqualTo(ServiceResult.Good));
            using JsonDocument document = JsonDocument.Parse(json.Memory);
            JsonElement declaration = document.RootElement;
            Assert.That(declaration.GetProperty("available").GetProperty("entities").GetProperty("mutable").GetBoolean(),
                Is.False);
            Assert.That(declaration.GetProperty("mutable").GetArrayLength(), Is.Zero);
            Assert.That(declaration.GetProperty("specversions").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(new[] { root.Artifacts.SpecVersion.Value }));
            Assert.That(capabilities.Writable!.Value, Is.False);
            Assert.That(capabilities.UserWritable!.Value, Is.False);
        }

        [TestCase("different", OpenUsdAssetKindEnum.Reference, "model/vnd.usda")]
        [TestCase("#usda 1.0", OpenUsdAssetKindEnum.Payload, "model/vnd.usda")]
        [TestCase("#usda 1.0", OpenUsdAssetKindEnum.Reference, "model/vnd.usdc")]
        public void RegistryResourcesRejectConflictingBootstrapDefinitions(
            string content, OpenUsdAssetKindEnum kind, string mediaType)
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            OpenUsdAssetState original = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset("shared.usda") })[0];

            Assert.Throws<System.InvalidOperationException>(() => UsdAssetDelivery.AttachStageAssets(
                context, stage, ns,
                new ServedAsset[] { new("shared.usda", kind, Bytes(content), mediaType) }));
            Assert.That(
                original.Parent!.FindChild(context, original.BrowseName),
                Is.SameAs(original));
        }

        [Test]
        public void AttachStageAssetsPublishesTheContentDigest()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            byte[] payload = Bytes("#usda 1.0");
            byte[] expected;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                expected = sha.ComputeHash(payload);
            }
#pragma warning restore CA1850

            ArrayOf<OpenUsdAssetState> created = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns,
                new ServedAsset[] { new("Cell.usda", OpenUsdAssetKindEnum.RootLayer, payload) });

            OpenUsdAssetState asset = created[0];
            Assert.That(
                asset.DigestAlgorithm!.Value, Is.EqualTo(OpenUsdDigestAlgorithmEnum.Sha256));
            ByteString digest = asset.Digest!.Value;
            Assert.That(digest.IsNull, Is.False);
            Assert.That(digest.Length, Is.EqualTo(expected.Length));
            Assert.That(digest.Span[0], Is.EqualTo(expected[0]));
            Assert.That(digest.Span[^1], Is.EqualTo(expected[^1]));
        }

        [Test]
        public void AttachStageAssetsMarksTheFileReadOnlyAndPublishesItsSize()
        {
            byte[] payload = Bytes("#usda 1.0\n");
            OpenUsdAssetState asset = SingleAsset(payload, out SystemContext _);

            Assert.That(asset.Size!.Value, Is.EqualTo((ulong)payload.Length));
            Assert.That(asset.Writable!.Value, Is.False);
            Assert.That(asset.UserWritable!.Value, Is.False);
        }

        [Test]
        public void AttachStageAssetsCarriesTheSuppliedMediaType()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            ArrayOf<OpenUsdAssetState> created = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns,
                new ServedAsset[]
                {
                    new("Cell.usdc", OpenUsdAssetKindEnum.RootLayer, Bytes("binary"),
                        "model/vnd.usdc")
                });

            Assert.That(created[0].MediaType!.Value, Is.EqualTo("model/vnd.usdc"));
        }

        [TestCase("robot.usda", "robot.usda")]
        [TestCase("assets/sub dir/tool.usda", "assets.sub-dir.tool.usda")]
        [TestCase("_private", "_private")]
        [TestCase("3rdParty", "3rdParty")]
        public void AttachStageAssetsSanitisesTheIdentifierIntoABrowseName(
            string identifier, string expected)
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            ArrayOf<OpenUsdAssetState> created = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset(identifier) });

            Assert.That(created[0].BrowseName.Name, Is.EqualTo(expected));
            Assert.That(created[0].BrowseName.NamespaceIndex, Is.EqualTo(ns));
        }

        [Test]
        public void AttachStageAssetsRejectsAnEmptyIdentifier()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            Assert.Throws<System.ArgumentException>(() => UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset(string.Empty) }));
        }

        [Test]
        public void OpenThenReadStreamsTheWholePayload()
        {
            byte[] payload = Bytes("#usda 1.0\ndef Xform \"Root\" {}\n");
            OpenUsdAssetState asset = SingleAsset(payload, out SystemContext context);

            uint handle = 0;
            Assert.That(
                asset.Open!.OnCall!(context, asset.Open, asset.NodeId, ReadMode, ref handle),
                Is.EqualTo(ServiceResult.Good));
            Assert.That(handle, Is.Not.Zero);

            ByteString data = default;
            Assert.That(
                asset.Read!.OnCall!(
                    context, asset.Read, asset.NodeId, handle, payload.Length, ref data),
                Is.EqualTo(ServiceResult.Good));
            Assert.That(data.Length, Is.EqualTo(payload.Length));
            Assert.That(data.Span[0], Is.EqualTo(payload[0]));

            // A second read past the end returns an empty buffer rather than failing.
            ByteString tail = default;
            Assert.That(
                asset.Read.OnCall!(context, asset.Read, asset.NodeId, handle, 16, ref tail),
                Is.EqualTo(ServiceResult.Good));
            Assert.That(tail.Length, Is.Zero);
        }

        [Test]
        public void OpenRejectsAWriteMode()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            uint handle = 7;
            ServiceResult result = asset.Open!.OnCall!(
                context, asset.Open, asset.NodeId, WriteMode | ReadMode, ref handle);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public void OpenRejectsAModeWithoutTheReadBit()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            uint handle = 7;
            ServiceResult result = asset.Open!.OnCall!(
                context, asset.Open, asset.NodeId, 0x00, ref handle);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(handle, Is.Zero);
        }

        [Test]
        public void OpenRejectsMoreThanTheSupportedNumberOfConcurrentHandles()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            for (int i = 0; i < 8; i++)
            {
                uint accepted = 0;
                Assert.That(
                    asset.Open!.OnCall!(context, asset.Open, asset.NodeId, ReadMode, ref accepted),
                    Is.EqualTo(ServiceResult.Good));
            }

            uint refused = 99;
            ServiceResult result = asset.Open!.OnCall!(
                context, asset.Open, asset.NodeId, ReadMode, ref refused);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(refused, Is.Zero);
        }

        [Test]
        public void ReadRejectsANegativeLength()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);
            uint handle = OpenHandle(asset, context);

            ByteString data = default;
            ServiceResult result = asset.Read!.OnCall!(
                context, asset.Read, asset.NodeId, handle, -1, ref data);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void ReadRejectsAnUnknownHandle()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            ByteString data = default;
            ServiceResult result = asset.Read!.OnCall!(
                context, asset.Read, asset.NodeId, 4242u, 4, ref data);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseInvalidatesTheHandle()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);
            uint handle = OpenHandle(asset, context);

            Assert.That(
                asset.Close!.OnCall!(context, asset.Close, asset.NodeId, handle),
                Is.EqualTo(ServiceResult.Good));

            ByteString data = default;
            Assert.That(
                asset.Read!.OnCall!(context, asset.Read, asset.NodeId, handle, 4, ref data)
                    .StatusCode.Code,
                Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseIsToleratedForAnUnknownHandle()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            Assert.That(
                asset.Close!.OnCall!(context, asset.Close, asset.NodeId, 4242u),
                Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void GetPositionTracksTheCursorOfItsOwnHandle()
        {
            byte[] payload = Bytes("0123456789");
            OpenUsdAssetState asset = SingleAsset(payload, out SystemContext context);
            uint first = OpenHandle(asset, context);
            uint second = OpenHandle(asset, context);

            ByteString data = default;
            asset.Read!.OnCall!(context, asset.Read, asset.NodeId, first, 4, ref data);

            ulong firstPosition = 0;
            ulong secondPosition = 0;
            Assert.That(
                asset.GetPosition!.OnCall!(
                    context, asset.GetPosition, asset.NodeId, first, ref firstPosition),
                Is.EqualTo(ServiceResult.Good));
            Assert.That(
                asset.GetPosition.OnCall!(
                    context, asset.GetPosition, asset.NodeId, second, ref secondPosition),
                Is.EqualTo(ServiceResult.Good));

            Assert.That(firstPosition, Is.EqualTo(4UL));
            Assert.That(secondPosition, Is.Zero);
        }

        [Test]
        public void GetPositionRejectsAnUnknownHandle()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            ulong position = 0;
            ServiceResult result = asset.GetPosition!.OnCall!(
                context, asset.GetPosition, asset.NodeId, 4242u, ref position);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void SetPositionMovesTheCursor()
        {
            byte[] payload = Bytes("0123456789");
            OpenUsdAssetState asset = SingleAsset(payload, out SystemContext context);
            uint handle = OpenHandle(asset, context);

            Assert.That(
                asset.SetPosition!.OnCall!(context, asset.SetPosition, asset.NodeId, handle, 6UL),
                Is.EqualTo(ServiceResult.Good));

            ByteString data = default;
            asset.Read!.OnCall!(context, asset.Read, asset.NodeId, handle, 16, ref data);
            Assert.That(data.Length, Is.EqualTo(4));
            Assert.That(data.Span[0], Is.EqualTo((byte)'6'));
        }

        [Test]
        public void SetPositionClampsBeyondTheEndOfTheFile()
        {
            byte[] payload = Bytes("0123456789");
            OpenUsdAssetState asset = SingleAsset(payload, out SystemContext context);
            uint handle = OpenHandle(asset, context);

            Assert.That(
                asset.SetPosition!.OnCall!(
                    context, asset.SetPosition, asset.NodeId, handle, ulong.MaxValue),
                Is.EqualTo(ServiceResult.Good));

            ulong position = 0;
            asset.GetPosition!.OnCall!(
                context, asset.GetPosition, asset.NodeId, handle, ref position);
            Assert.That(position, Is.EqualTo((ulong)payload.Length));
        }

        [Test]
        public void SetPositionRejectsAnUnknownHandle()
        {
            OpenUsdAssetState asset = SingleAsset(Bytes("payload"), out SystemContext context);

            ServiceResult result = asset.SetPosition!.OnCall!(
                context, asset.SetPosition, asset.NodeId, 4242u, 0UL);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static ServedAsset Asset(string identifier)
        {
            return new ServedAsset(identifier, OpenUsdAssetKindEnum.Reference, Bytes("#usda 1.0"));
        }

        private static uint OpenHandle(OpenUsdAssetState asset, SystemContext context)
        {
            uint handle = 0;
            asset.Open!.OnCall!(context, asset.Open, asset.NodeId, ReadMode, ref handle);
            return handle;
        }

        private static OpenUsdAssetState SingleAsset(byte[] payload, out SystemContext context)
        {
            (SystemContext created, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            context = created;
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(created, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(created, root, ns, "Cell");
            return UsdAssetDelivery.AttachStageAssets(
                created, stage, ns,
                new ServedAsset[]
                {
                    new("Cell.usda", OpenUsdAssetKindEnum.RootLayer, payload)
                })[0];
        }
    }
}
