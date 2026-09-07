/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Covers §5.15 served-asset delivery: streaming layers through the Part 5
    /// FileType, digest verification, cache writing and the root-layer fallback.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorAssetsTests
    {
        private FakeAddressSpace m_space = null!;
        private Mock<ISession> m_session = null!;
        private MockUsdSink m_sink = null!;
        private string m_cacheDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_space = new FakeAddressSpace();
            m_session = FakeSession.Create(m_space);
            m_sink = new MockUsdSink();
            m_cacheDir = Path.Combine(Path.GetTempPath(), "usdassets-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_cacheDir))
            {
                Directory.Delete(m_cacheDir, recursive: true);
            }
        }

        private OpenUsdConnector Connector(OpenUsdConnectorOptions? options = null)
        {
            return new OpenUsdConnector(m_session.Object, m_sink,
                options ?? new OpenUsdConnectorOptions());
        }

        private NodeId RepresentationTypeId
            => new(OpenUsdModel.RepresentationTypeId, m_space.OpenUsdNamespaceIndex);

        private NodeId AssetTypeId
            => new(OpenUsdModel.AssetTypeId, m_space.OpenUsdNamespaceIndex);

        private NodeId AddRegistry()
        {
            NodeId facility = m_space.AddObject(Opc.Ua.ObjectIds.Server, "OpenUSD",
                browseNameNamespace: m_space.OpenUsdNamespaceIndex);
            return m_space.AddObject(facility, "Representations");
        }

        private NodeId AddRepresentation(string name = "Robot")
        {
            NodeId rep = m_space.AddObject(AddRegistry(), name, RepresentationTypeId);
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/" + name));
            return rep;
        }

        private NodeId AddStage(NodeId rep, string rootLayerIdentifier = "robot.usda")
        {
            NodeId stage = m_space.AddObject(rep, "Stage-Object");
            m_space.AddVariable(rep, "Stage", new Variant(stage));
            m_space.AddVariable(stage, "RootLayerIdentifier", new Variant(rootLayerIdentifier));
            return stage;
        }

        private NodeId AddAsset(
            NodeId assetsFolder,
            string identifier,
            byte[] content,
            OpenUsdAssetKind kind = OpenUsdAssetKind.SubLayer,
            ByteString digest = default,
            NodeId typeDefinition = default,
            bool withIdentifier = true)
        {
            NodeId asset = m_space.AddObject(assetsFolder, "Asset-" + identifier,
                typeDefinition.IsNull ? AssetTypeId : typeDefinition);
            if (withIdentifier)
            {
                m_space.AddVariable(asset, "AssetIdentifier", new Variant(identifier));
            }
            m_space.AddVariable(asset, "AssetKind", new Variant((int)kind));
            if (!digest.IsNull)
            {
                m_space.AddVariable(asset, "Digest", new Variant(digest));
                m_space.AddVariable(asset, "DigestAlgorithm",
                    new Variant((int)OpenUsdDigestAlgorithm.Sha256));
            }
            m_space.AddPart5File(asset, content);
            return asset;
        }

        private static ByteString Sha256Of(byte[] bytes)
        {
            return new ByteString(Hash(bytes, OpenUsdDigestAlgorithm.Sha256));
        }

        private static byte[] Hash(byte[] bytes, OpenUsdDigestAlgorithm algorithm)
        {
#pragma warning disable CA1850 // Prefer static HashData (net48 compatibility)
            using HashAlgorithm hash = algorithm switch
            {
                OpenUsdDigestAlgorithm.Sha384 => SHA384.Create(),
                OpenUsdDigestAlgorithm.Sha512 => SHA512.Create(),
                _ => SHA256.Create()
            };
            return hash.ComputeHash(bytes);
#pragma warning restore CA1850
        }

        [Test]
        public async Task FetchServedAssetsReturnsEmptyWhenNoRepresentationHasAStageAsync()
        {
            AddRepresentation();
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> assets =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(assets, Is.Empty);
            Assert.That(Directory.Exists(m_cacheDir), Is.True);
        }

        [Test]
        public async Task FetchServedAssetsReturnsEmptyWhenTheStageServesNoAssetsAsync()
        {
            AddStage(AddRepresentation());
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> assets =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(assets, Is.Empty);
        }

        [Test]
        public async Task FetchServedAssetsSkipsAssetsFolderChildrenOfAForeignTypeAsync()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "foreign.usda", Encoding.UTF8.GetBytes("#usda 1.0"),
                typeDefinition: new NodeId(9999));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Is.Empty);
        }

        [Test]
        public async Task FetchServedAssetsSkipsAssetsWithoutAnIdentifierAsync()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "nameless.usda", Encoding.UTF8.GetBytes("#usda 1.0"),
                withIdentifier: false);
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Is.Empty);
        }

        [Test]
        public async Task FetchServedAssetsWritesTheLayerAndReportsTheDigestVerifiedAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0\ndef Xform \"Robot\" {}\n");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "layers/robot.usda", content,
                OpenUsdAssetKind.RootLayer, Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(fetched[0].Identifier, Is.EqualTo("layers/robot.usda"));
            Assert.That(fetched[0].Kind, Is.EqualTo(OpenUsdAssetKind.RootLayer));
            Assert.That(fetched[0].Length, Is.EqualTo(content.Length));
            Assert.That(fetched[0].DigestVerified, Is.True);
            Assert.That(File.Exists(fetched[0].LocalPath), Is.True);
            Assert.That(File.ReadAllBytes(fetched[0].LocalPath), Is.EqualTo(content));
            Assert.That(m_space.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FetchServedAssetsDeduplicatesLayersSharedAcrossRepresentationsAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId shared = AddStage(AddRepresentation("A"));
            NodeId assets = m_space.AddObject(shared, "Assets");
            AddAsset(assets, "shared.usda", content, digest: Sha256Of(content));
            AddAsset(assets, "shared.usda", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenTheDigestDoesNotMatch()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "tampered.usda", content,
                digest: new ByteString(new byte[] { 9, 9, 9 }));
            OpenUsdConnector connector = Connector();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("failed digest verification"));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenADigestIsMissingAndDigestsAreRequired()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "undigested.usda", Encoding.UTF8.GetBytes("#usda 1.0"));
            OpenUsdConnector connector = Connector();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("without a digest"));
        }

        [Test]
        public async Task FetchServedAssetsSurfacesAnUnverifiedLayerWhenDigestsAreNotRequiredAsync()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "undigested.usda", Encoding.UTF8.GetBytes("#usda 1.0"));
            OpenUsdConnector connector = Connector(
                new OpenUsdConnectorOptions { RequireAssetDigests = false });

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(fetched[0].DigestVerified, Is.False);
        }

        [Test]
        public void FetchServedAssetsThrowsWhenTheClosureExceedsTheTotalSizeLimit()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "big.usda", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector(new OpenUsdConnectorOptions
            {
                MaxTotalAssetBytes = 2
            });

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("maximum total size"));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenALayerExceedsTheSingleAssetSizeLimit()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "big.usda", Encoding.UTF8.GetBytes("#usda 1.0 padding padding"));
            OpenUsdConnector connector = Connector(new OpenUsdConnectorOptions
            {
                MaxAssetBytes = 4
            });

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("maximum size"));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenTheAssetIsMissingPart5Methods()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            NodeId asset = m_space.AddObject(assets, "Asset-broken", AssetTypeId);
            m_space.AddVariable(asset, "AssetIdentifier", new Variant("broken.usda"));
            OpenUsdConnector connector = Connector();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("Part 5 Open/Read/Close"));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenOpenReturnsNoOutputArguments()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "nohandle.usda", Encoding.UTF8.GetBytes("#usda 1.0"));
            m_space.OpenResultOverride = [];
            OpenUsdConnector connector = Connector();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("no file handle"));
        }

        [Test]
        public void FetchServedAssetsThrowsWhenOpenReturnsANonNumericHandle()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "badhandle.usda", Encoding.UTF8.GetBytes("#usda 1.0"));
            m_space.OpenResultOverride = [new Variant("not-a-handle")];
            OpenUsdConnector connector = Connector();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None));

            Assert.That(ex!.Message, Does.Contain("no file handle"));
        }

        [Test]
        public async Task FetchServedAssetsCollapsesTraversingIdentifiersIntoTheCacheRootAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "@../../evil.usda@", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(Path.GetFileName(fetched[0].LocalPath), Is.EqualTo("evil.usda"));
            Assert.That(Path.GetDirectoryName(fetched[0].LocalPath),
                Is.EqualTo(Path.GetFullPath(m_cacheDir)));
        }

        [Test]
        public async Task FetchServedAssetsStripsAnchorSuffixesAndDriveQualifiersAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, @"C:\sub\cell.usda</World/Cell>", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(Path.GetFileName(fetched[0].LocalPath), Is.EqualTo("cell.usda"));
            Assert.That(fetched[0].LocalPath, Does.Contain("sub"));
        }

        [Test]
        public async Task FetchServedAssetsFallsBackToADefaultNameForAnEmptyIdentifierAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "..", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(Path.GetFileName(fetched[0].LocalPath), Is.EqualTo("asset.usda"));
        }

        [Test]
        public async Task FetchServedAssetsStreamsLayersLargerThanOneChunkAsync()
        {
            var content = new byte[8192 + 137];
            for (int i = 0; i < content.Length; i++)
            {
                content[i] = (byte)(i % 251);
            }
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "large.usda", content, digest: Sha256Of(content));
            OpenUsdConnector connector = Connector();

            List<OpenUsdConnector.FetchedAsset> fetched =
                await connector.FetchServedAssetsAsync(m_cacheDir, CancellationToken.None);

            Assert.That(fetched, Has.Count.EqualTo(1));
            Assert.That(fetched[0].Length, Is.EqualTo(content.Length));
            Assert.That(File.ReadAllBytes(fetched[0].LocalPath), Is.EqualTo(content));
        }

        [Test]
        public async Task TryReadRootLayerBytesReturnsNullWithoutAStageAsync()
        {
            AddRepresentation();
            OpenUsdConnector connector = Connector();
            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            byte[]? bytes = await connector.TryReadRootLayerBytesAsync(reps[0], CancellationToken.None);

            Assert.That(bytes, Is.Null);
        }

        [Test]
        public async Task TryReadRootLayerBytesReturnsNullWhenTheStageServesNoAssetsAsync()
        {
            AddStage(AddRepresentation());
            OpenUsdConnector connector = Connector();
            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            byte[]? bytes = await connector.TryReadRootLayerBytesAsync(reps[0], CancellationToken.None);

            Assert.That(bytes, Is.Null);
        }

        [Test]
        public async Task TryReadRootLayerBytesStreamsTheRootLayerAssetAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0\n");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "robot.usda", content, OpenUsdAssetKind.RootLayer);
            OpenUsdConnector connector = Connector();
            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            byte[]? bytes = await connector.TryReadRootLayerBytesAsync(reps[0], CancellationToken.None);

            Assert.That(bytes, Is.EqualTo(content));
        }

        [Test]
        public async Task TryReadRootLayerBytesFallsBackToTheMatchingIdentifierAsync()
        {
            byte[] content = Encoding.UTF8.GetBytes("#usda 1.0 root\n");
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            m_space.AddObject(assets, "Asset-foreign", new NodeId(9999));
            AddAsset(assets, "robot.usda", content, OpenUsdAssetKind.SubLayer);
            OpenUsdConnector connector = Connector();
            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            byte[]? bytes = await connector.TryReadRootLayerBytesAsync(reps[0], CancellationToken.None);

            Assert.That(bytes, Is.EqualTo(content));
        }

        [Test]
        public async Task TryReadRootLayerBytesReturnsNullWhenNoServedLayerMatchesAsync()
        {
            NodeId assets = m_space.AddObject(AddStage(AddRepresentation()), "Assets");
            AddAsset(assets, "other.usda", Encoding.UTF8.GetBytes("#usda 1.0"),
                OpenUsdAssetKind.SubLayer);
            OpenUsdConnector connector = Connector();
            List<OpenUsdConnector.RepresentationInfo> reps =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None);

            byte[]? bytes = await connector.TryReadRootLayerBytesAsync(reps[0], CancellationToken.None);

            Assert.That(bytes, Is.Null);
        }

        [Test]
        public void VerifyDeliveredAssetAcceptsAMatchingDigest()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("payload");

            bool verified = OpenUsdConnector.VerifyDeliveredAsset(
                "a.usda", bytes, Sha256Of(bytes), OpenUsdDigestAlgorithm.Sha256, true);

            Assert.That(verified, Is.True);
        }

        [Test]
        public void VerifyDeliveredAssetRejectsADigestOfTheWrongAlgorithm()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("payload");

            Assert.That(
                () => OpenUsdConnector.VerifyDeliveredAsset(
                    "a.usda", bytes, Sha256Of(bytes), OpenUsdDigestAlgorithm.Sha384, true),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [TestCase(OpenUsdDigestAlgorithm.Sha384)]
        [TestCase(OpenUsdDigestAlgorithm.Sha512)]
        public void VerifyDeliveredAssetSupportsTheStrongerDigestAlgorithms(
            OpenUsdDigestAlgorithm algorithm)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("payload");

            bool verified = OpenUsdConnector.VerifyDeliveredAsset(
                "a.usda", bytes, new ByteString(Hash(bytes, algorithm)), algorithm, true);

            Assert.That(verified, Is.True);
        }

        [Test]
        public void VerifyDeliveredAssetRejectsADigestOfTheSameLengthButDifferentContent()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("payload");
            byte[] wrong = Sha256Of(Encoding.UTF8.GetBytes("other")).ToArray();

            Assert.That(
                () => OpenUsdConnector.VerifyDeliveredAsset(
                    "a.usda", bytes, new ByteString(wrong), OpenUsdDigestAlgorithm.Sha256, true),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void VerifyDeliveredAssetTreatsAnEmptyDigestAsAbsent()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("payload");

            bool verified = OpenUsdConnector.VerifyDeliveredAsset(
                "a.usda", bytes, new ByteString(Array.Empty<byte>()), OpenUsdDigestAlgorithm.Sha256, false);

            Assert.That(verified, Is.False);
        }
    }
}
