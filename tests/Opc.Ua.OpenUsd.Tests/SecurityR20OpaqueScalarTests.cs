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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Exercises opaque values through materialization, ordinary UA writes, export and parsing.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SecurityR20OpaqueScalarTests
    {
        /// <summary>
        /// The legitimate structured value, not a pre-rendered String.
        /// </summary>
        private static UsdValue OriginalTint => UsdTestHelpers.NumberTuple(0.1, 0.2, 0.3, 1.0);

        /// <summary>
        /// An enabled live opaque scalar never grants a remote client layer-authoring authority.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR20OpaqueLiveScalarCannotAuthorSiblingPrimOrCompositionAsync(bool historize)
        {
            MaterializedScene scene = CreateScene(historize);
            UsdAttributeState node = scene.Attr("/Target.tint");
            Variant original = node.Value;

            ServiceResult result = await node.WriteAttributeAsync(
                scene.Context, Attributes.Value, default, new DataValue(Variant.From(Injection)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(node.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            Assert.That(node.UserAccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            Assert.That(node.Value, Is.EqualTo(original));
            Assert.That(node.Historizing, Is.EqualTo(historize));
            Assert.That((node.AccessLevel & AccessLevels.HistoryRead) != 0, Is.EqualTo(historize));
            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);
            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(exported), exported.StageName);
            AssertSingleTarget(reparsed);
            Assert.That(reparsed.RootPrims[0].Attributes[0].Value, Is.EqualTo(OriginalTint));
        }

        /// <summary>
        /// Trusted structured opaque data and validated live numeric values still round trip.
        /// </summary>
        [Test]
        public async Task SecurityR20ValidStructuredOpaqueAndTypedValuesRemainExportableAsync()
        {
            MaterializedScene scene = CreateScene(historize: false);
            UsdAttributeState speed = scene.Attr("/Target.speed");

            ServiceResult result = await speed.WriteAttributeAsync(
                scene.Context, Attributes.Value, default, new DataValue(Variant.From(42.0)))
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(speed.Value.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.0));
            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);
            Assert.That(exported.RootPrims[0].Attributes[1].Live, Is.True);
            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(exported), exported.StageName);
            AssertSingleTarget(reparsed);
            Assert.That(reparsed.RootPrims[0].Attributes[0].TypeName, Is.EqualTo("color4f"));
            Assert.That(reparsed.RootPrims[0].Attributes[0].Value, Is.EqualTo(OriginalTint));
            Assert.That(reparsed.RootPrims[0].Attributes[1].Value, Is.EqualTo(UsdValue.From(42.0)));
        }

        /// <summary>
        /// Every remote opaque replacement is denied, including syntactically valid literals.
        /// </summary>
        [TestCase("")]
        [TestCase(" \t\r\n")]
        [TestCase("(1, 0, 0, 1)")]
        [TestCase("(1, 0, 0, 1) # trailing comment\n" + Injection)]
        [TestCase("(1, 0, 0, 1) trailing")]
        [TestCase("[(\"nested\", [\"leaf\"])]")]
        [TestCase(Injection)]
        public async Task SecurityR20OpaqueScalarTrailingSyntaxAndNestedLeavesStayInertAsync(string replacement)
        {
            MaterializedScene scene = CreateScene(historize: false);
            UsdAttributeState node = scene.Attr("/Target.tint");
            Variant original = node.Value;

            ServiceResult result = await node.WriteAttributeAsync(
                scene.Context, Attributes.Value, default, new DataValue(Variant.From(replacement)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(node.Value, Is.EqualTo(original));
            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);
            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(exported), exported.StageName);
            AssertSingleTarget(reparsed);
            Assert.That(reparsed.RootPrims[0].Attributes[0].Value, Is.EqualTo(OriginalTint));
        }

        /// <summary>
        /// Even host-side opaque text replacement cannot bypass the writer's whole-literal boundary.
        /// </summary>
        [TestCase(Injection)]
        [TestCase("(1, 0, 0, 1) trailing")]
        [TestCase("(1, 0, 0, 1) # comment\n}\ndef Xform \"Injected\" {\n")]
        [TestCase("(\"unterminated\\")]
        [TestCase("(1, 2]")]
        [TestCase("(1, 2) (3, 4)")]
        [TestCase("1; def Xform \"Injected\" {}")]
        [TestCase("")]
        [TestCase(" \t")]
        public void SecurityR20WriterQuotesIncompleteOrTrailingOpaqueSyntax(string replacement)
        {
            UsdStage stage = CreateStage();
            stage.RootPrims[0].Attributes[0].Value = UsdValue.FromString(replacement);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            AssertSingleTarget(reparsed);
            Assert.That(reparsed.RootPrims[0].Attributes[0].Value.TryGetText(out string text), Is.True);
            Assert.That(text, Is.EqualTo(replacement));
        }

        /// <summary>
        /// Valid complete opaque literals retain their spelling, including structured nested leaves.
        /// </summary>
        [TestCase("(0.1, 0.2, 0.3, 1.0)")]
        [TestCase("[(1, 2), (3, 4)]")]
        [TestCase("((\"safe\\\" leaf\", [true, false]),)")]
        [TestCase("[]")]
        [TestCase("()")]
        [TestCase("18446744073709551615")]
        public void SecurityR20WriterPreservesCompleteOpaqueLiteral(string literal)
        {
            UsdStage stage = CreateStage();
            stage.RootPrims[0].Attributes[0].Value = UsdValue.FromString(literal);

            string written = UsdaWriter.Write(stage);
            UsdStage reparsed = UsdaReader.Parse(written, stage.StageName);

            Assert.That(written, Does.Contain("color4f tint = " + literal));
            AssertSingleTarget(reparsed);
        }

        /// <summary>
        /// The bounded recognizer accepts its final supported depth and quotes deeper data.
        /// </summary>
        [TestCase(64, true)]
        [TestCase(65, false)]
        public void SecurityR20WriterBoundsOpaqueLiteralNesting(int depth, bool supported)
        {
            string literal = new string('(', depth) + "1" + new string(')', depth);
            UsdStage stage = CreateStage();
            stage.RootPrims[0].Attributes[0].Value = UsdValue.FromString(literal);

            string written = UsdaWriter.Write(stage);
            UsdStage reparsed = UsdaReader.Parse(written, stage.StageName);

            AssertSingleTarget(reparsed);
            if (supported)
            {
                Assert.That(written, Does.Contain("color4f tint = " + literal));
                Assert.That(reparsed.RootPrims[0].Attributes[0].Value.Kind, Is.EqualTo(UsdValueKind.Tuple));
            }
            else
            {
                Assert.That(reparsed.RootPrims[0].Attributes[0].Value.TryGetText(out string text), Is.True);
                Assert.That(text, Is.EqualTo(literal));
            }
        }

        /// <summary>
        /// Creates a real address-space subtree with both opaque and validated live values.
        /// </summary>
        private static MaterializedScene CreateScene(bool historize)
        {
            MaterializedScene scene = MaterializationHarness.Materialize(
                CreateStage(), new UsdMaterializationOptions { HistorizeLiveAttributes = historize });
            var typeTable = new TypeTable(scene.Context.NamespaceUris);
            typeTable.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Ua.DataTypeIds.String, Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Ua.DataTypeIds.Double, Ua.DataTypeIds.BaseDataType);
            scene.Context.TypeTable = typeTable;
            return scene;
        }

        /// <summary>
        /// Creates the legitimate scene before the attacker supplies a replacement value.
        /// </summary>
        private static UsdStage CreateStage()
        {
            var stage = new UsdStage("SecurityR20");
            var prim = new UsdPrim("Target");
            prim.Attributes.Add(new UsdAttribute("tint", "color4f") { Live = true, Value = OriginalTint });
            prim.Attributes.Add(new UsdAttribute("speed", "double") { Live = true, Value = UsdValue.From(7.0) });
            stage.AddRootPrim(prim);
            return stage;
        }

        /// <summary>
        /// Verifies parsed structure and the independent trailing property.
        /// </summary>
        private static void AssertSingleTarget(UsdStage stage)
        {
            Assert.That(stage.AllPrims().Select(prim => prim.Name), Is.EqualTo(s_expectedPrimNames));
            Assert.That(stage.AllPrims().SelectMany(prim => prim.Composition), Is.Empty);
            Assert.That(stage.RootPrims[0].Attributes.Select(attribute => attribute.Name),
                Is.EqualTo(s_expectedAttributeNames));
            Assert.That(stage.RootPrims[0].Attributes[1].TypeName, Is.EqualTo("double"));
        }

        /// <summary>A replacement that closes the target and adds an externally composed sibling.</summary>
        private const string Injection =
            "(1, 0, 0, 1)\n}\ndef Xform \"Injected\" (\n    prepend references = @foreign.usda@\n)\n{\n";

        /// <summary>The exact scene root names.</summary>
        private static readonly string[] s_expectedPrimNames = ["Target"];

        /// <summary>The exact legitimate property names in declaration order.</summary>
        private static readonly string[] s_expectedAttributeNames = ["tint", "speed"];
    }
}
