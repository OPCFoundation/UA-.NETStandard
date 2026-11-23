// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Connector;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Server-supplied asset paths must never make native USD open files outside
    /// the fetched closure: composition references written by the file sink, and
    /// asset paths authored inside server-delivered layers.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UntrustedAssetPathTests
    {
        private string m_directory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "untrusted-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        [TestCase("@pump.usda@", "@pump.usda@")]
        [TestCase(" @pump.usda@</Pump> ", "@pump.usda@</Pump>")]
        [TestCase("@./sub/robot.usdc@</World/Robot>", "@./sub/robot.usdc@</World/Robot>")]
        public void RelativeLayerReferencesAreNormalized(string reference, string expected)
        {
            Assert.That(UsdAssetPaths.TryNormalizeReference(reference, out string normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase(@"@\\untrusted.invalid\share\foreign.usda@")]
        [TestCase("@//untrusted.invalid/share/foreign.usda@")]
        [TestCase(@"@C:\outside\foreign.usda@")]
        [TestCase("@C:/outside/foreign.usda@")]
        [TestCase("@/outside/foreign.usda@")]
        [TestCase("@https://untrusted.invalid/foreign.usda@")]
        [TestCase("@file:///C:/foreign.usda@")]
        [TestCase("@../foreign.usda@")]
        [TestCase("@./nested/../../foreign.usda@")]
        [TestCase("@a.usda@; customData = { string injected = 'yes' }")]
        [TestCase("@a.usda@</A> (offset = 1)")]
        [TestCase("@a.usda@</A/../B>")]
        [TestCase("@texture.png@")]
        [TestCase("@@@a.usda@@@")]
        [TestCase("@CON.usda@")]
        [TestCase("@`\"expr.usda\"`@")]
        [TestCase("pump.usda")]
        [TestCase("")]
        public void ReferencesThatCouldLeaveTheLayerDirectoryAreRefused(string reference)
        {
            Assert.That(UsdAssetPaths.TryNormalizeReference(reference, out _), Is.False);
        }

        [Test]
        public void FileSinkDropsReferencesOutsideItsDirectoryButKeepsThePrim()
        {
            string layer = Path.Combine(m_directory, "live.usda");
            var sink = new UsdFileSink(layer);
            sink.ComposePrim("/World/A", OpenUsdCompositionArc.Reference, @"@\\untrusted.invalid\share\f.usda@", true);
            sink.ComposePrim("/World/B", OpenUsdCompositionArc.Payload, "@C:/Windows/win.ini@", true);
            sink.ComposePrim("/World/C", OpenUsdCompositionArc.Reference, "@a.usda@; customData = { }", true);
            sink.ComposePrim("/World/D", OpenUsdCompositionArc.Reference, "@pump.usda@</Pump>", true);

            string text = File.ReadAllText(layer);
            Assert.That(text, Does.Not.Contain("untrusted"));
            Assert.That(text, Does.Not.Contain("win.ini"));
            Assert.That(text, Does.Not.Contain("customData"));
            Assert.That(text, Does.Contain("prepend references = @pump.usda@</Pump>"));
            Assert.That(text, Does.Contain("over \"A\"").And.Contain("over \"B\""));
        }

        [Test]
        public void FileSinkFilterLimitsReferencesToTheHostsClosure()
        {
            string layer = Path.Combine(m_directory, "live.usda");
            string admitted = Path.GetFullPath(Path.Combine(m_directory, "pump.usda"));
            var sink = new UsdFileSink(layer)
            {
                AssetPathFilter = path => string.Equals(path, admitted, StringComparison.OrdinalIgnoreCase)
            };
            sink.ComposePrim("/World/A", OpenUsdCompositionArc.Reference, "@leftover.usdc@", true);
            sink.ComposePrim("/World/B", OpenUsdCompositionArc.Reference, "@./pump.usda@</Pump>", true);

            string text = File.ReadAllText(layer);
            Assert.That(text, Does.Not.Contain("leftover"));
            Assert.That(text, Does.Contain("@./pump.usda@</Pump>"));
        }

        [Test]
        public void ScannerFindsAssetPathsEverywhereNativeUsdWould()
        {
            const string text = "#usda 1.0\n" +
                "/* licence \" header */\n" +
                "(\n    subLayers = [@./a.usda@]\n    doc = \"\"\"not @an.asset@\"\"\"\n)\n" +
                "// \" a line comment\n" +
                "def Xform \"World\" (prepend references = @b.usda@</B>)\n{\n" +
                "    asset inputs:file = @tex.png@ # @comment.usda@\n" +
                "    asset empty = @@\n" +
                "    rel material:binding = </World/Looks/M>\n}\n";
            string[] expected = ["./a.usda", "b.usda", "tex.png"];
            Assert.That(UsdAssetPaths.ScanUsdaAssetPaths(text), Is.EqualTo(expected));
        }

        [Test]
        public void ScannerSeesArcsThatCommentsWithQuotesUsedToHide()
        {
            // A lexer that does not know C comments reads the quote inside the first
            // comment as the start of a string and misses the sublayer. Native Sdf
            // composes it.
            string text = "#usda 1.0\n/* \" */ ( subLayers = [@//attacker/share/x.usdc@] ) /* \" */\n" +
                "def Xform \"World\" { }\n";
            string[] expected = ["//attacker/share/x.usdc"];
            Assert.That(UsdAssetPaths.ScanUsdaAssetPaths(text), Is.EqualTo(expected));
        }

        [TestCase("#usda 1.0\n#\r(\r    subLayers = [@x.usda@]\r)\r#\ndef Xform \"W\" {}\n")]
        [TestCase("#usda 1.0\ndef Xform \"W\" { double a = 1 / 2 }\n")]
        [TestCase("#usda 1.0\n/* never closed\n")]
        [TestCase("#usda 1.0\ndef Xform \"W\" (clips = { dictionary d = { string templateAssetPath = \"//x/#.usd\" } }) {}\n")]
        [TestCase("#usda 1.0\ndef Xform \"W\" (references = @@@x.usda@@@) {}\n")]
        [TestCase("#usda 1.0\ndef Xform \"W\x01\" {}\n")]
        [TestCase("#usda 1.0\ndef Xform \"W\n\" {}\n")]
        [TestCase("PXR-USDC")]
        public void ScannerRefusesTextNativeUsdCouldReadDifferently(string text)
        {
            Assert.That(() => UsdAssetPaths.ScanUsdaAssetPaths(text), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void FetchedClosureWithOnlyInternalAssetPathsIsAdmitted()
        {
            List<OpenUsdConnector.FetchedAsset> fetched =
            [
                Fetched("cell.usda", "#usda 1.0\n( subLayers = [@./parts/arm.usda@] )\n" +
                    "def Xform \"World\" { asset inputs:file = @tex.png@ }\n", OpenUsdAssetKind.RootLayer),
                Fetched("parts/arm.usda", "#usda 1.0\ndef Cube \"Arm\" {}\n", OpenUsdAssetKind.SubLayer),
                Fetched("tex.png", "png", OpenUsdAssetKind.Texture)
            ];

            Assert.That(
                OpenUsdConnectorRunner.TryValidateFetchedClosure(fetched, out ISet<string> closure, out string? refusal),
                Is.True,
                refusal);
            Assert.That(closure, Does.Contain(Path.GetFullPath(Path.Combine(m_directory, "parts", "arm.usda"))));
        }

        [TestCase("@//attacker/share/x.usdc@")]
        [TestCase("@C:/Users/victim/secret.usda@")]
        [TestCase("@missing.usda@")]
        [TestCase("@leftover.usdc@")]
        [TestCase("@../outside.usda@")]
        public void FetchedClosureReferencingAnythingElseIsRefused(string assetPath)
        {
            File.WriteAllText(Path.Combine(m_directory, "leftover.usdc"), "PXR-USDC");
            List<OpenUsdConnector.FetchedAsset> fetched =
            [
                Fetched("cell.usda", "#usda 1.0\n/* \" */ ( subLayers = [" + assetPath + "] ) /* \" */\n" +
                    "def Xform \"World\" {}\n", OpenUsdAssetKind.RootLayer)
            ];

            Assert.That(OpenUsdConnectorRunner.TryValidateFetchedClosure(fetched, out _, out string? refusal), Is.False);
            Assert.That(refusal, Does.Contain("cell.usda"));
        }

        [TestCase("cell.usdc", OpenUsdAssetKind.RootLayer)]
        [TestCase("cell.usd", OpenUsdAssetKind.RootLayer)]
        [TestCase("cell.usdz", OpenUsdAssetKind.Package)]
        [TestCase("tex.usd", OpenUsdAssetKind.Texture)]
        [TestCase("shader.mtlx", OpenUsdAssetKind.Texture)]
        public void FetchedLayersThatCannotBeInspectedAreRefused(string identifier, OpenUsdAssetKind kind)
        {
            List<OpenUsdConnector.FetchedAsset> fetched = [Fetched(identifier, "PXR-USDC", kind)];

            Assert.That(OpenUsdConnectorRunner.TryValidateFetchedClosure(fetched, out _, out _), Is.False);
        }

        [Test]
        public void FetchedAliasesOfOneCacheFileAreRefused()
        {
            OpenUsdConnector.FetchedAsset first = Fetched("cell.usda", "#usda 1.0\n", OpenUsdAssetKind.RootLayer);
            List<OpenUsdConnector.FetchedAsset> fetched =
            [
                first,
                new OpenUsdConnector.FetchedAsset
                {
                    Identifier = "./cell.usda",
                    Kind = OpenUsdAssetKind.SubLayer,
                    LocalPath = Path.Combine(m_directory, ".", "cell.usda")
                }
            ];

            Assert.That(OpenUsdConnectorRunner.TryValidateFetchedClosure(fetched, out _, out string? refusal), Is.False);
            Assert.That(refusal, Does.Contain("./cell.usda"));
        }

        [Test]
        public void FetchedLayerWithInvalidUtf8IsRefused()
        {
            string path = Path.Combine(m_directory, "cell.usda");
            File.WriteAllBytes(path, [.. "#usda 1.0\n"u8.ToArray(), 0xC3, 0x28]);
            List<OpenUsdConnector.FetchedAsset> fetched =
            [
                new OpenUsdConnector.FetchedAsset
                {
                    Identifier = "cell.usda",
                    Kind = OpenUsdAssetKind.RootLayer,
                    LocalPath = path
                }
            ];

            Assert.That(OpenUsdConnectorRunner.TryValidateFetchedClosure(fetched, out _, out _), Is.False);
        }

        private OpenUsdConnector.FetchedAsset Fetched(string identifier, string content, OpenUsdAssetKind kind)
        {
            string path = Path.Combine(m_directory, identifier.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return new OpenUsdConnector.FetchedAsset
            {
                Identifier = identifier,
                Kind = kind,
                LocalPath = path,
                Length = content.Length,
                DigestVerified = true
            };
        }
    }
}
