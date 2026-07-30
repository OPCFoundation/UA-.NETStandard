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

using System.Text;
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
        public void AttachStageAssetsCreatesTheAssetsFolderAndOneAssetPerLayer()
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
            Assert.That(stage.Assets, Is.Not.Null);
            Assert.That(created[0].AssetIdentifier!.Value, Is.EqualTo("Cell.usda"));
            Assert.That(created[0].AssetKind!.Value, Is.EqualTo(OpenUsdAssetKindEnum.RootLayer));
            Assert.That(created[0].MediaType!.Value, Is.EqualTo("model/vnd.usda"));
            Assert.That(created[1].AssetIdentifier!.Value, Is.EqualTo("robot.usda"));
            Assert.That(created[1].AssetKind!.Value, Is.EqualTo(OpenUsdAssetKindEnum.Reference));
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
        public void AttachStageAssetsPublishesTheContentDigest()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            byte[] payload = Bytes("#usda 1.0");
            byte[] expected = System.Security.Cryptography.SHA256.HashData(payload);

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

        [TestCase("robot.usda", "robot_usda")]
        [TestCase("assets/sub dir/tool.usda", "assets_sub_dir_tool_usda")]
        [TestCase("_private", "_private")]
        [TestCase("3rdParty", "_3rdParty")]
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
        public void AttachStageAssetsFallsBackToAConstantBrowseNameForAnEmptyIdentifier()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");

            ArrayOf<OpenUsdAssetState> created = UsdAssetDelivery.AttachStageAssets(
                context, stage, ns, new ServedAsset[] { Asset(string.Empty) });

            Assert.That(created[0].BrowseName.Name, Is.EqualTo("Asset"));
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
