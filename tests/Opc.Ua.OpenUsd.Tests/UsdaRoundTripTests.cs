// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Reproduces <c>roundtrip_check.py</c>: parse a composed layer, write it back to a single
    /// flattened <c>.usda</c> layer, re-parse it, and assert the §7.4 scene signatures are equal.
    /// </summary>
    [TestFixture]
    public class UsdaRoundTripTests
    {
        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void SignatureIsPreservedThroughWriteAndReparse(string asset)
        {
            UsdStage expected = UsdaReader.ParseFile(TestAssets.PathTo(asset));

            string written = UsdaWriter.Write(expected);
            UsdStage reparsed = UsdaReader.Parse(written, expected.StageName);

            string? difference = UsdSceneSignature.FirstDifference(expected, reparsed);
            Assert.That(
                UsdSceneSignature.Compute(reparsed),
                Is.EqualTo(UsdSceneSignature.Compute(expected)),
                difference ?? "signatures are unexpectedly equal");
        }

        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void PrimCountIsPreservedThroughWriteAndReparse(string asset)
        {
            UsdStage expected = UsdaReader.ParseFile(TestAssets.PathTo(asset));
            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(expected), expected.StageName);

            Assert.That(reparsed.AllPrims().Count(), Is.EqualTo(expected.AllPrims().Count()));
        }

        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void WriterOutputIsAFixedPoint(string asset)
        {
            UsdStage expected = UsdaReader.ParseFile(TestAssets.PathTo(asset));

            string firstWrite = UsdaWriter.Write(expected);
            string secondWrite = UsdaWriter.Write(UsdaReader.Parse(firstWrite, expected.StageName));

            Assert.That(secondWrite, Is.EqualTo(firstWrite));
        }

        [Test]
        public void WriteToFile_ProducesSameTextAsWrite()
        {
            UsdStage stage = TestAssets.Load("Plant.usda");
            string expectedText = UsdaWriter.Write(stage);

            string target = Path.Combine(TestContext.CurrentContext.WorkDirectory, "roundtrip_plant.usda");
            try
            {
                UsdaWriter.WriteToFile(stage, target);
                string onDisk = File.ReadAllText(target);
                Assert.That(onDisk, Is.EqualTo(expectedText));
            }
            finally
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
        }

        [Test]
        public void ReparsedFileWithoutOverlays_MatchesComposedSignature()
        {
            UsdStage expected = UsdaReader.ParseFile(TestAssets.PathTo("Cell.usda"));

            string target = Path.Combine(TestContext.CurrentContext.WorkDirectory, "roundtrip_cell.usda");
            try
            {
                UsdaWriter.WriteToFile(expected, target);
                UsdStage reparsed = UsdaReader.ParseFile(target, expected.StageName, applyExampleOverlays: false);

                string? difference = UsdSceneSignature.FirstDifference(expected, reparsed);
                Assert.That(
                    UsdSceneSignature.Compute(reparsed),
                    Is.EqualTo(UsdSceneSignature.Compute(expected)),
                    difference ?? "signatures are unexpectedly equal");
            }
            finally
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
        }
    }
}
