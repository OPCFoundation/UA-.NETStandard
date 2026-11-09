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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Re-parses actual exports so writable data cannot become USD composition syntax.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    [Category("SecurityRegression")]
    public sealed class SecurityF39UsdaArrayEncodingTests
    {
        /// <summary>
        /// Quotes, delimiters, control characters and comment markers stay in live array elements.
        /// </summary>
        [TestCase("string[]")]
        [TestCase("token[]")]
        [Category("SecurityRegression")]
        public async Task SecurityF39LiveArrayCannotAuthorUsdComposition(string typeName)
        {
            string[] elements = ["before", QuotedPayload, "after"];

            UsdStage reparsed = await WriteLiveArrayAndReparseAsync(typeName, elements).ConfigureAwait(false);

            AssertInertStage(reparsed);
            AssertTextElements(reparsed.RootPrims[0].Attributes.Single(a => a.Name == "label").Value, elements);
        }

        /// <summary>
        /// Asset-array leaves use the same rejecting-delimiter encoding as scalar assets.
        /// </summary>
        [TestCase("a@<b>c\rd\ne\"f", "abcdef")]
        [TestCase(
            "x@]\n}\nover \"Injected\" (\n    prepend payload = @./foreign.usda@\n)\n{\n    asset[] stolen = [@y",
            "x]}over Injected (    prepend payload = ./foreign.usda){    asset[] stolen = [y")]
        [Category("SecurityRegression")]
        public async Task SecurityF39LiveAssetArrayCannotAuthorUsdComposition(string payload, string encoded)
        {
            string[] elements = ["./before.usda", payload, "./after.usda"];

            UsdStage reparsed = await WriteLiveArrayAndReparseAsync("asset[]", elements).ConfigureAwait(false);

            AssertInertStage(reparsed);
            UsdValue value = reparsed.RootPrims[0].Attributes.Single(a => a.Name == "label").Value;
            AssertTextElements(value, ["./before.usda", encoded, "./after.usda"]);
            Assert.That(value.TryGetArray(out ArrayOf<UsdValue> items), Is.True);
            for (int index = 0; index < items.Count; index++)
            {
                Assert.That(items[index].Kind, Is.EqualTo(UsdValueKind.AssetPath));
            }
        }

        /// <summary>
        /// Empty, singleton and ordered multi-element arrays retain legitimate values on the live export path.
        /// </summary>
        [TestCase("string[]", 0)]
        [TestCase("string[]", 1)]
        [TestCase("string[]", 3)]
        [TestCase("token[]", 0)]
        [TestCase("token[]", 1)]
        [TestCase("token[]", 3)]
        [TestCase("asset[]", 0)]
        [TestCase("asset[]", 1)]
        [TestCase("asset[]", 3)]
        [Category("SecurityRegression")]
        public async Task SecurityF39LiveArraysPreserveValidElements(string typeName, int count)
        {
            string[] elements = [.. s_validElements.Take(count)];

            UsdStage reparsed = await WriteLiveArrayAndReparseAsync(typeName, elements).ConfigureAwait(false);

            AssertInertStage(reparsed);
            UsdAttribute attribute = reparsed.RootPrims[0].Attributes.Single(a => a.Name == "label");
            Assert.That(attribute.TypeName, Is.EqualTo(typeName));
            AssertTextElements(attribute.Value, elements);
        }

        /// <summary>
        /// Both the direct renderer and opaque materialization encode textual leaves recursively.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF39CompositeLeavesCannotAuthorUsdComposition(bool materialize)
        {
            var texts = UsdValue.FromTuple(new ArrayOf<UsdValue>(new[]
            {
                UsdValue.FromString(QuotedPayload),
                UsdValue.FromToken(QuotedPayload),
                UsdValue.FromAssetPath("a@<b>c\rd\ne\"f"),
                UsdValue.FromPathReference("/Tar>g<et@\r\n")
            }));
            var nested = UsdValue.FromArray(new ArrayOf<UsdValue>(new[]
            {
                texts,
                UsdValue.FromArray(new ArrayOf<UsdValue>(new[]
                {
                    UsdValue.FromMatrix(new ArrayOf<UsdValue>(new[] { texts }))
                }))
            }));
            UsdStage stage = CreateStage("opaque[]", nested);
            if (materialize)
            {
                MaterializedScene scene = MaterializationHarness.Materialize(stage);
                Assert.That(
                    scene.Attr("/Target.label").Value.TryGetValue(out string carried),
                    Is.True,
                    "The unknown composite must really traverse the opaque text transport.");
                Assert.That(carried, Does.StartWith("["));
                stage = scene.Context.ExportUsdStage(scene.Result);
            }

            string written = UsdaWriter.Write(stage);
            UsdStage reparsed = UsdaReader.Parse(written, stage.StageName);

            AssertInertStage(reparsed);
            var expectedTexts = UsdValue.FromTuple(new ArrayOf<UsdValue>(new[]
            {
                UsdValue.FromString(QuotedPayload),
                UsdValue.FromString(QuotedPayload),
                UsdValue.FromString("a@<b>c\rd\ne\"f"),
                UsdValue.FromString("/Tar>g<et@\r\n")
            }));
            var expected = UsdValue.FromArray(new ArrayOf<UsdValue>(new[]
            {
                expectedTexts,
                UsdValue.FromArray(new ArrayOf<UsdValue>(new[]
                {
                    UsdValue.FromTuple(new ArrayOf<UsdValue>(new[] { expectedTexts }))
                }))
            }));
            Assert.That(
                reparsed.RootPrims[0].Attributes.Single(a => a.Name == "label").Value,
                Is.EqualTo(expected),
                "Nested tuple, matrix and array shapes and their inert text must survive.");
        }

        /// <summary>
        /// Metadata dictionaries and their nested sequences cannot bypass quoted-leaf escaping.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF39MetadataCompositeLeavesStayInsideLiterals()
        {
            UsdStage stage = CreateStage("string[]", UsdTestHelpers.StringArray("safe"));
            stage.RootPrims[0].Metadata["customData"] = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>
                {
                    ["nested"] = UsdValue.FromDictionary(new Dictionary<string, UsdValue>
                    {
                        ["note"] = UsdValue.FromString(QuotedPayload),
                        ["rows"] = UsdTestHelpers.Array(
                            UsdValue.FromTuple(new ArrayOf<UsdValue>(new[] { UsdValue.FromString(QuotedPayload) })))
                    })
                });

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            AssertInertStage(reparsed);
            Assert.That(
                reparsed.RootPrims[0].Metadata["customData"].TryGetDictionary(
                    out IReadOnlyDictionary<string, UsdValue> customData),
                Is.True);
            Assert.That(
                customData["nested"].TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> nested),
                Is.True);
            UsdTestHelpers.AssertString(nested["note"], QuotedPayload);
            Assert.That(
                nested["rows"],
                Is.EqualTo(UsdTestHelpers.Array(
                    UsdValue.FromTuple(new ArrayOf<UsdValue>(new[] { UsdValue.FromString(QuotedPayload) })))));
        }

        /// <summary>
        /// Real composition metadata remains active while arc-looking attribute and metadata text stays inert.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF39LegitimateCompositionSurvivesInertMetadata()
        {
            UsdStage stage = CreateStage("token[]", UsdTestHelpers.StringArray("first", "second"));
            UsdPrim prim = stage.RootPrims[0];
            prim.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Reference)
            {
                AssetPath = "./trusted.usda",
                PrimPath = "/Authorized",
                ListPosition = UsdListOpTypeEnum.Append
            });
            prim.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Payload)
            {
                AssetPath = "./trusted-payload.usda",
                ListPosition = UsdListOpTypeEnum.Prepend
            });
            prim.Metadata["comment"] = UsdValue.FromString(QuotedPayload);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            Assert.That(reparsed.AllPrims().Select(value => value.Name), Is.EqualTo(s_expectedPrimNames));
            UsdPrim target = reparsed.RootPrims[0];
            Assert.That(
                target.Composition.Select(arc => (arc.ArcKind, arc.AssetPath, arc.PrimPath, arc.ListPosition)),
                Is.EqualTo(
                [
                    (UsdArcKindEnum.Reference, "./trusted.usda", "/Authorized", UsdListOpTypeEnum.Append),
                    (UsdArcKindEnum.Payload, "./trusted-payload.usda", string.Empty, UsdListOpTypeEnum.Prepend)
                ]));
            UsdTestHelpers.AssertString(target.Metadata["comment"], QuotedPayload);
            AssertTextElements(
                target.Attributes.Single(attribute => attribute.Name == "label").Value,
                ["first", "second"]);
            UsdTestHelpers.AssertInteger(
                target.Attributes.Single(attribute => attribute.Name == "neighbor7").Value, 7L);
        }

        /// <summary>
        /// Mutates a real live Variable, exports it and parses the resulting USDA.
        /// </summary>
        private static async Task<UsdStage> WriteLiveArrayAndReparseAsync(string typeName, string[] elements)
        {
            UsdStage stage = CreateStage(typeName, UsdTestHelpers.StringArray("initial"));
            MaterializedScene scene = MaterializationHarness.Materialize(stage);
            UsdAttributeState node = scene.Attr("/Target.label");
            var typeTable = new TypeTable(scene.Context.NamespaceUris);
            typeTable.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Ua.DataTypeIds.String, Ua.DataTypeIds.BaseDataType);
            if (node.DataType != Ua.DataTypeIds.String)
            {
                typeTable.AddSubtype(node.DataType, Ua.DataTypeIds.String);
            }
            scene.Context.TypeTable = typeTable;
            Assert.That(node.AccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero);
            var value = new DataValue(Variant.From(new ArrayOf<string>(elements)));

            ServiceResult result = await node.WriteAttributeAsync(
                scene.Context, Attributes.Value, default, value).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good), "The attacker controls ordinary Value writes.");
            Assert.That(node.Value.TryGetValue(out ArrayOf<string> stored), Is.True);
            Assert.That(stored.ToArray(), Is.EqualTo(elements), "The exporter must see the written value.");
            UsdStage exported = scene.Context.ExportUsdStage(scene.Result);
            Assert.That(exported.RootPrims[0].Attributes.Single(a => a.Name == "label").Live, Is.True);
            return UsdaReader.Parse(UsdaWriter.Write(exported), exported.StageName);
        }

        /// <summary>
        /// Creates one prim with a writable attribute followed by an independent control value.
        /// </summary>
        private static UsdStage CreateStage(string typeName, UsdValue value)
        {
            var stage = new UsdStage("SecurityF39") { DefaultPrim = "Target" };
            var prim = new UsdPrim("Target");
            prim.Attributes.Add(new UsdAttribute("label", typeName) { Live = true, Value = value });
            prim.Attributes.Add(new UsdAttribute("neighbor7", "int") { Value = UsdValue.From(7L) });
            stage.AddRootPrim(prim);
            return stage;
        }

        /// <summary>
        /// Checks parsed behavior rather than relying on a substring scan of the writer output.
        /// </summary>
        private static void AssertInertStage(UsdStage stage)
        {
            Assert.Multiple(() =>
            {
                Assert.That(stage.AllPrims().Select(prim => prim.Name), Is.EqualTo(s_expectedPrimNames));
                Assert.That(stage.AllPrims().SelectMany(prim => prim.Composition), Is.Empty);
                Assert.That(
                    stage.RootPrims[0].Attributes.Select(attribute => attribute.Name),
                    Is.EqualTo(s_expectedAttributeNames));
            });
            UsdTestHelpers.AssertInteger(
                stage.RootPrims[0].Attributes.Single(attribute => attribute.Name == "neighbor7").Value, 7L);
        }

        /// <summary>
        /// Compares concrete decoded array elements in order, including empty arrays.
        /// </summary>
        private static void AssertTextElements(UsdValue value, string[] expected)
        {
            Assert.That(value.TryGetArray(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(expected.Length));
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.That(items[index].TryGetText(out string text), Is.True);
                Assert.That(text, Is.EqualTo(expected[index]));
            }
        }

        /// <summary>Attempts to close both quote styles and author a prim and composition arc.</summary>
        private const string QuotedPayload =
            "x'\" ]\n}\nover \"Injected\" (\n    prepend references = @./foreign.usda@\n)\n" +
            "{\n    string[] stolen = [\"y\\\r\t#quoted";

        /// <summary>Ordinary array elements whose ordered prefixes exercise each supported array size.</summary>
        private static readonly string[] s_validElements =
            ["./first.usda", "folder with spaces/second.usda", "./third.usda"];

        /// <summary>The only prim legitimately authored by the regression fixture.</summary>
        private static readonly string[] s_expectedPrimNames = ["Target"];

        /// <summary>The writable attribute and its independent trailing control.</summary>
        private static readonly string[] s_expectedAttributeNames = ["label", "neighbor7"];
    }
}
