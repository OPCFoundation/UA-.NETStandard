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
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Regression tests for the reader/writer asymmetry the H4/M writer fixes introduced: the
    /// writer emits a multi-target <c>.connect</c> as a bracketed path-reference list and an
    /// <c>asset[]</c> as a bracketed <c>@…@</c> list, and the reader must now parse both so the
    /// writer's own output round-trips under the §7.4 signature contract.
    /// </summary>
    [TestFixture]
    public class ConversionAsymmetryTests
    {
        private static string Wrap(string primBody)
        {
            return string.Join("\n",
                "#usda 1.0",
                "(",
                "    defaultPrim = \"P\"",
                "    metersPerUnit = 1.0",
                "    upAxis = \"Z\"",
                ")",
                string.Empty,
                "def Xform \"P\"",
                "{",
                primBody,
                "}",
                string.Empty);
        }

        private static UsdAttribute AttributeNamed(UsdStage stage, string primPath, string name)
        {
            UsdPrim prim = stage.Find(primPath)!;
            return prim.Attributes.First(a => a.Name == name);
        }

        private static void AssertRoundTripSignature(UsdStage expected)
        {
            string written = UsdaWriter.Write(expected);
            UsdStage reparsed = UsdaReader.Parse(written, expected.StageName);

            string? difference = UsdSceneSignature.FirstDifference(expected, reparsed);
            Assert.That(
                UsdSceneSignature.Compute(reparsed),
                Is.EqualTo(UsdSceneSignature.Compute(expected)),
                difference ?? "signatures are unexpectedly equal");
        }

        // ---- Task 1.1: multiple connection targets ------------------------------------

        [Test]
        public void ParseValue_BracketedPathReferenceList_ParsesEachTarget()
        {
            UsdValue value = UsdaReader.ParseValue("[</P/A.outputs:surface>, </P/B.outputs:surface>]");

            UsdTestHelpers.AssertTextItems(value, "/P/A.outputs:surface", "/P/B.outputs:surface");
        }

        [Test]
        public void TwoConnectionAttribute_ParsesBothTargetsInOrder()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = [</P/A.outputs:surface>, </P/B.outputs:surface>]"),
                "Conn");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:surface");
            Assert.That(attr.Connections, Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void TwoConnectionAttribute_RoundTripsThroughWriteAndReparse()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = [</P/A.outputs:surface>, </P/B.outputs:surface>]"),
                "Conn");

            AssertRoundTripSignature(stage);
        }

        [Test]
        public void SingleConnectionAttribute_StillParsesAsOneTarget()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = </P/A.outputs:surface>"),
                "Conn");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:surface");
            Assert.That(attr.Connections, Is.EqualTo(s_stringValues2));
        }

        [Test]
        public void WriterMultiConnectionOutput_ReparsesToTheSameTargets()
        {
            // Reproduces the asymmetry directly: an in-memory attribute with two connections is
            // written (as a bracketed list) and must re-parse to the same two connections.
            var stage = new UsdStage("Conn") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var attr = new UsdAttribute("inputs:surface", "token");
            attr.Connections.Add("/P/A.outputs:surface");
            attr.Connections.Add("/P/B.outputs:surface");
            prim.Attributes.Add(attr);
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdAttribute reparsedAttr = AttributeNamed(reparsed, "/P", "inputs:surface");
            Assert.That(reparsedAttr.Connections, Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void EmptyConnectionList_ParsesToNoTargets()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = []"),
                "Conn");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:surface");
            Assert.That(attr.Connections, Is.Empty);
        }

        [Test]
        public void QuotedRelationshipTarget_StillParsesAsATarget()
        {
            // A relationship target authored as a quoted string rather than a </path> reference
            // is read from the value's text rather than dropped.
            UsdStage stage = UsdaReader.Parse(
                Wrap("    rel material:binding = \"/P/Mat\""),
                "Rel");

            UsdRelationship rel = UsdTestHelpers.RequireRelationship(
                UsdTestHelpers.RequirePrim(stage, "/P"), "material:binding");
            Assert.That(rel.Targets, Is.EqualTo(s_stringValues3));
        }

        [Test]
        public void QuotedRelationshipTargetList_ParsesEachTarget()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    rel material:binding = [\"/P/MatA\", \"/P/MatB\"]"),
                "Rel");

            UsdRelationship rel = UsdTestHelpers.RequireRelationship(
                UsdTestHelpers.RequirePrim(stage, "/P"), "material:binding");
            Assert.That(rel.Targets, Is.EqualTo(s_stringValues4));
        }

        [Test]
        public void QuotedConnectionTarget_StillParsesAsATarget()
        {
            // A target authored as a quoted string rather than a </path> reference is still a
            // target: the reader falls back to reading the value's text rather than dropping it.
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = \"/P/A.outputs:surface\""),
                "Conn");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:surface");
            Assert.That(attr.Connections, Is.EqualTo(s_stringValues2));
        }

        [Test]
        public void QuotedConnectionTargetList_ParsesEachTarget()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    token inputs:surface.connect = [\"/P/A.outputs:surface\", \"/P/B.outputs:surface\"]"),
                "Conn");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:surface");
            Assert.That(attr.Connections, Is.EqualTo(s_stringValues1));
        }

        // ---- Task 1.2: asset arrays ---------------------------------------------------

        [Test]
        public void ParseValue_AssetArray_ParsesEachElementUnwrapped()
        {
            UsdValue value = UsdaReader.ParseValue("[@./a.usda@, @./b.usda@]");

            UsdTestHelpers.AssertTextItems(value, "./a.usda", "./b.usda");
        }

        [Test]
        public void AssetArrayAttribute_ParsesToUnwrappedPaths()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    custom asset[] inputs:files = [@./a.usda@, @./b.usda@]"),
                "Assets");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:files");
            UsdTestHelpers.AssertTextItems(attr.Value, "./a.usda", "./b.usda");
        }

        [Test]
        public void AssetArrayAttribute_RoundTripsThroughWriteAndReparse()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    custom asset[] inputs:files = [@./a.usda@, @./b.usda@]"),
                "Assets");

            AssertRoundTripSignature(stage);
        }

        [Test]
        public void WriterAssetArrayOutput_ReparsesToTheSamePaths()
        {
            var stage = new UsdStage("Assets") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("inputs:files", "asset[]")
            {
                Value = UsdTestHelpers.AssetArray("./a.usda", "./b.usda"),
            });
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdAttribute reparsedAttr = AttributeNamed(reparsed, "/P", "inputs:files");
            Assert.That(reparsedAttr.Value.TryGetArray(out ArrayOf<UsdValue> paths), Is.True);
            Assert.That(paths.ToArray()!.Select(p => p.TryGetAssetPath(out string text) ? text : string.Empty).ToArray(),
                Is.EqualTo(s_stringValues5));
        }

        [Test]
        public void SingleAssetScalar_StillParsesUnwrapped()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap("    asset inputs:file = @./pump.usda@"),
                "Assets");

            UsdAttribute attr = AttributeNamed(stage, "/P", "inputs:file");
            UsdTestHelpers.AssertAssetPath(attr.Value, "./pump.usda");
        }

        private static readonly string[] s_stringValues1 = ["/P/A.outputs:surface", "/P/B.outputs:surface"];
        private static readonly string[] s_stringValues2 = ["/P/A.outputs:surface"];
        private static readonly string[] s_stringValues3 = ["/P/Mat"];
        private static readonly string[] s_stringValues4 = ["/P/MatA", "/P/MatB"];
        private static readonly string[] s_stringValues5 = ["./a.usda", "./b.usda"];
    }
}
