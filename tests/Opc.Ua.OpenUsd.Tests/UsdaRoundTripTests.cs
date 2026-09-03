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
