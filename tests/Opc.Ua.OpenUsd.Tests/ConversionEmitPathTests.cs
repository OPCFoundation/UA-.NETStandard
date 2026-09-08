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
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Regression tests that compose the full export path — build a stage, serialise it with
    /// <see cref="UsdaWriter"/>, then assert the emitted <c>.usda</c> text and/or re-parse and
    /// compare with <see cref="UsdSceneSignature"/>. This is the path the verification review found
    /// no existing test exercised, which is why the H-1 array-emit regression survived: after the
    /// document model was corrected, <see cref="UsdValueCoercion.Decoerce"/> hands the writer an
    /// <c>object?[]</c> for every array, and the writer keyed its array rendering on the CLR
    /// container (<c>List&lt;object?&gt;</c>) instead of the attribute type, so every exported array
    /// fell through to the scalar/tuple path and was corrupted into a parenthesised tuple.
    /// </summary>
    [TestFixture]
    public class ConversionEmitPathTests
    {
        private static string EmitRootAttribute(string primType, UsdAttribute attribute)
        {
            var stage = new UsdStage("Assets") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", primType);
            prim.Attributes.Add(attribute);
            stage.AddRootPrim(prim);
            return UsdaWriter.Write(stage);
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

        // ---- H-1: an exported array is emitted as "[...]", never a parenthesised tuple ----

        [Test]
        public void TokenArray_FromDecoercedShape_EmitsBracketedDoubleQuotedElements()
        {
            // UsdValueCoercion.Decoerce hands the writer an object?[] for a token[] array.
            UsdValue decoerced = UsdValueCoercion.Decoerce(
                Variant.From((ArrayOf<string>)s_stringValues1));

            string usda = EmitRootAttribute(
                "Xform",
                new UsdAttribute("xformOpOrder", "token[]")
                {
                    Variability = UsdVariabilityEnum.Uniform,
                    Value = decoerced,
                });

            Assert.That(usda, Does.Contain("uniform token[] xformOpOrder = [\"xformOp:translate\"]"));
            // The H-1 corruption emitted a single-quoted parenthesised tuple instead of an array.
            Assert.That(usda, Does.Not.Contain("('xformOp:translate')"));
            Assert.That(usda, Does.Not.Contain("(\"xformOp:translate\")"));
        }

        [Test]
        public void TokenArray_FromObjectArrayShape_EmitsBracketedArray()
        {
            string usda = EmitRootAttribute(
                "Xform",
                new UsdAttribute("xformOpOrder", "token[]")
                {
                    Variability = UsdVariabilityEnum.Uniform,
                    Value = UsdTestHelpers.TokenArray("xformOp:translate", "xformOp:scale"),
                });

            Assert.That(
                usda,
                Does.Contain("uniform token[] xformOpOrder = [\"xformOp:translate\", \"xformOp:scale\"]"));
        }

        [Test]
        public void Color3fArray_FromDecoercedMatrix_EmitsBracketedTupleRows()
        {
            // A color3f[] materializes as a rectangular matrix; Decoerce regroups it into per-tuple
            // rows carried in an object?[]. The writer must emit "[(...)]", not "((...))".
            UsdValue decoerced = UsdValueCoercion.Decoerce(
                Variant.From(new float[,] { { 0f, 0f, 1f } }.ToMatrixOf()));

            string usda = EmitRootAttribute(
                "Mesh",
                new UsdAttribute("primvars:displayColor", "color3f[]") { Value = decoerced });

            Assert.That(usda, Does.Contain("color3f[] primvars:displayColor = [(0.0, 0.0, 1.0)]"));
            Assert.That(usda, Does.Not.Contain("((0.0, 0.0, 1.0))"));
        }

        [Test]
        public void Color3fArray_FromObjectArrayShape_MultipleRows_EmitsEachTuple()
        {
            string usda = EmitRootAttribute(
                "Mesh",
                new UsdAttribute("primvars:displayColor", "color3f[]")
                {
                    Value = UsdTestHelpers.Array(
                        UsdTestHelpers.NumberTuple(0.0, 0.0, 1.0),
                        UsdTestHelpers.NumberTuple(1.0, 1.0, 0.0)),
                });

            Assert.That(
                usda,
                Does.Contain("color3f[] primvars:displayColor = [(0.0, 0.0, 1.0), (1.0, 1.0, 0.0)]"));
        }

        [Test]
        public void AssetArray_FromDecoercedShape_EmitsAtDelimitedElements()
        {
            UsdValue decoerced = UsdValueCoercion.Decoerce(
                Variant.From((ArrayOf<string>)s_stringValues2));

            string usda = EmitRootAttribute(
                "Xform",
                new UsdAttribute("inputs:files", "asset[]") { Value = decoerced });

            Assert.That(usda, Does.Contain("asset[] inputs:files = [@./a.usda@]"));
            // The H-1 corruption emitted a single-quoted parenthesised tuple, losing the @...@ form.
            Assert.That(usda, Does.Not.Contain("('./a.usda')"));
            Assert.That(usda, Does.Not.Contain("(\"./a.usda\")"));
        }

        [Test]
        public void Double3Scalar_IsStillEmittedAsParenthesisedTuple_NotArray()
        {
            // The type-name keying must not turn a fixed-size math scalar into an array: a double3
            // (ValueRank one-dimension, three components) is a single parenthesised tuple.
            UsdValue decoerced = UsdValueCoercion.Decoerce(
                Variant.From((ArrayOf<double>)s_doubleValues1));

            string usda = EmitRootAttribute(
                "Xform",
                new UsdAttribute("xformOp:translate", "double3") { Value = decoerced });

            Assert.That(usda, Does.Contain("double3 xformOp:translate = (1.0, 2.0, 3.0)"));
            Assert.That(usda, Does.Not.Contain("[1.0, 2.0, 3.0]"));
        }

        [Test]
        public void FullEmitPath_ArraysAndConnection_RoundTripsUnderSignature()
        {
            // Composes the whole export path across every array shape plus a value co-authored with
            // a connection, then re-parses and compares signatures (the missing coverage that let
            // H-1 through, and the M-4 merge check in one).
            var stage = new UsdStage("Assets") { DefaultPrim = "World" };
            var world = new UsdPrim("World", "Xform");

            var mesh = new UsdPrim("Mesh", "Mesh");
            mesh.Attributes.Add(new UsdAttribute("xformOpOrder", "token[]")
            {
                Variability = UsdVariabilityEnum.Uniform,
                Value = UsdTestHelpers.TokenArray("xformOp:translate", "xformOp:scale"),
            });
            mesh.Attributes.Add(new UsdAttribute("primvars:displayColor", "color3f[]")
            {
                Value = UsdTestHelpers.Array(UsdTestHelpers.NumberTuple(0.0, 0.0, 1.0)),
            });
            mesh.Attributes.Add(new UsdAttribute("inputs:files", "asset[]")
            {
                Value = UsdTestHelpers.AssetArray("./a.usda", "./b.usda"),
            });

            var surface = new UsdAttribute("outputs:surface", "token") { Value = UsdValue.FromString("fallback") };
            surface.Connections.Add("/World/Shader.outputs:surface");
            mesh.Attributes.Add(surface);

            world.AddChild(mesh);
            stage.AddRootPrim(world);

            AssertRoundTripSignature(stage);
        }

        // ---- M-4: a default value co-authored with a '.connect' merges into one attribute ----

        [Test]
        public void ValueAndConnect_OnOneAttribute_ReparseMergesIntoSingleAttribute()
        {
            var stage = new UsdStage("Conn") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var attr = new UsdAttribute("inputs:surface", "token") { Value = UsdValue.FromString("fallback") };
            attr.Connections.Add("/P/Shader.outputs:surface");
            prim.Attributes.Add(attr);
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            List<UsdAttribute> matching = prim2.Attributes
                .Where(a => a.Name == "inputs:surface").ToList();

            Assert.That(matching, Has.Count.EqualTo(1),
                "a value co-authored with a .connect must re-parse as one attribute, not two");
            UsdTestHelpers.AssertText(matching[0].Value, "fallback");
            Assert.That(matching[0].Connections, Is.EqualTo(s_stringValues3));
        }

        [Test]
        public void ValueSamplesAndConnect_AllCoalesceOntoOneAttribute()
        {
            var stage = new UsdStage("Conn") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var attr = new UsdAttribute("xformOp:translate", "double3")
            {
                Value = UsdTestHelpers.NumberTuple(1.0, 2.0, 3.0),
            };
            attr.TimeSamples[0.0] = UsdTestHelpers.NumberTuple(1.0, 2.0, 3.0);
            attr.TimeSamples[24.0] = UsdTestHelpers.NumberTuple(4.0, 5.0, 6.0);
            attr.Connections.Add("/P/Rig.outputs:translate");
            prim.Attributes.Add(attr);
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            List<UsdAttribute> matching = prim2.Attributes
                .Where(a => a.Name == "xformOp:translate").ToList();

            Assert.That(matching, Has.Count.EqualTo(1),
                "value, time samples and .connect on one attribute must not split into duplicates");
            Assert.That(matching[0].Connections, Is.EqualTo(s_stringValues4));
            Assert.That(matching[0].TimeSamples, Has.Count.EqualTo(2));
            Assert.That(matching[0].Value.IsNull, Is.False);
        }

        // ---- M-1: a known string/token attribute with a structured value fails closed ----

        [Test]
        public void KnownTokenArray_WithTupleElements_FailsClosed_NeverPublishesClrTypeName()
        {
            UsdValueTypeMapping mapping = UsdValueTypeMap.Map("token[]", null);
            uint components = UsdValueTypeMap.ComponentCount("token[]");

            bool ok = UsdValueCoercion.TryCoerce(
                UsdTestHelpers.Array(
                    UsdTestHelpers.IntegerTuple(1L, 2L),
                    UsdTestHelpers.IntegerTuple(3L, 4L)),
                mapping,
                components,
                out Variant result);

            Assert.That(ok, Is.False, "a structured element cannot be a token string; fail closed");
            Assert.That(result.TryGetValue(out ArrayOf<string> _), Is.False);
        }

        [Test]
        public void KnownStringScalar_WithTupleValue_FailsClosed()
        {
            UsdValueTypeMapping mapping = UsdValueTypeMap.Map("string", null);
            uint components = UsdValueTypeMap.ComponentCount("string");

            bool ok = UsdValueCoercion.TryCoerce(
                UsdTestHelpers.IntegerTuple(1L, 2L), mapping, components, out Variant _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void KnownStringScalar_WithNumber_StillStringifiesFaithfully()
        {
            UsdValueTypeMapping mapping = UsdValueTypeMap.Map("string", null);
            uint components = UsdValueTypeMap.ComponentCount("string");

            bool ok = UsdValueCoercion.TryCoerce(UsdValue.From(42L), mapping, components, out Variant result);

            Assert.That(ok, Is.True);
            Assert.That(result.TryGetValue(out string rendered), Is.True);
            Assert.That(rendered, Is.EqualTo("42"));
            Assert.That(rendered, Does.Not.Contain("System."));
        }

        [Test]
        public void KnownTokenArray_WithStringElements_StillCoercesNormally()
        {
            UsdValueTypeMapping mapping = UsdValueTypeMap.Map("token[]", null);
            uint components = UsdValueTypeMap.ComponentCount("token[]");

            bool ok = UsdValueCoercion.TryCoerce(
                UsdTestHelpers.TokenArray("a", "b"), mapping, components, out Variant result);

            Assert.That(ok, Is.True);
            Assert.That(result.TryGetValue(out ArrayOf<string> tokens), Is.True);
            Assert.That(tokens.ToArray(), Is.EqualTo(s_stringValues5));
        }

        // ---- M-3: a ')' inside a quoted metadata string must not truncate the block ----

        [Test]
        public void CloseParenInsideQuotedMetadataString_DoesNotTruncateBlock()
        {
            string usda = string.Join("\n",
                "#usda 1.0",
                "(",
                "    defaultPrim = \"P\"",
                ")",
                string.Empty,
                "def Xform \"P\" (",
                "    comment = \"note with ) paren and ( too\"",
                "    kind = \"component\"",
                ")",
                "{",
                "}",
                string.Empty);

            UsdStage stage = UsdaReader.Parse(usda, "Meta");

            UsdPrim prim = stage.Find("/P")!;
            // The metadata after the ')'-bearing string survived (the block was not truncated).
            Assert.That(prim.Kind, Is.EqualTo(UsdPrimKindEnum.Component));
            Assert.That(prim.Metadata.ContainsKey("comment"), Is.True);
            UsdTestHelpers.AssertString(prim.Metadata["comment"], "note with ) paren and ( too");
        }

        // ---- Defect 9: §6.3 custom prim metadata round-trips through Metadata ----

        [Test]
        public void CustomScalarMetadata_RoundTripsThroughMetadataDictionary()
        {
            var stage = new UsdStage("Meta") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Metadata["displayName"] = UsdValue.FromString("Pump Assembly");
            prim.Metadata["revision"] = UsdValue.From(3L);
            prim.Metadata["approved"] = UsdValue.From(true);
            prim.Metadata["tolerance"] = UsdValue.From(0.25);
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            UsdTestHelpers.AssertString(prim2.Metadata["displayName"], "Pump Assembly");
            UsdTestHelpers.AssertInteger(prim2.Metadata["revision"], 3L);
            UsdTestHelpers.AssertBoolean(prim2.Metadata["approved"], true);
            UsdTestHelpers.AssertDouble(prim2.Metadata["tolerance"], 0.25);
        }

        [Test]
        public void CustomNestedDictionaryMetadata_RoundTripsAsNestedDictionary()
        {
            var stage = new UsdStage("Meta") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var custom = new Dictionary<string, UsdValue>(StringComparer.Ordinal)
            {
                ["author"] = UsdValue.FromString("acme"),
                ["weight"] = UsdValue.From(12.5),
                ["count"] = UsdValue.From(7L),
            };
            prim.Metadata["customData"] = UsdValue.FromDictionary(custom);
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            Assert.That(prim2.Metadata.ContainsKey("customData"), Is.True);
            Assert.That(prim2.Metadata["customData"].TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> nested),
                Is.True);
            UsdTestHelpers.AssertString(nested["author"], "acme");
            UsdTestHelpers.AssertDouble(nested["weight"], 12.5);
            UsdTestHelpers.AssertInteger(nested["count"], 7L);
        }

        [Test]
        public void CustomMetadata_CoexistsWithWellKnownMetadata_WithoutPollution()
        {
            var stage = new UsdStage("Meta") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform")
            {
                Kind = UsdPrimKindEnum.Component,
                Documentation = "A documented prim",
            };
            prim.ApiSchemas.Add(new UsdApiSchema("PhysicsRigidBodyAPI"));
            prim.Metadata["displayName"] = UsdValue.FromString("Widget");
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            // Well-known metadata still binds to typed members and is NOT duplicated as custom.
            Assert.That(prim2.Kind, Is.EqualTo(UsdPrimKindEnum.Component));
            Assert.That(prim2.Documentation, Is.EqualTo("A documented prim"));
            Assert.That(prim2.ApiSchemas.Select(a => a.SchemaName), Does.Contain("PhysicsRigidBodyAPI"));
            Assert.That(prim2.Metadata.ContainsKey("displayName"), Is.True);
            UsdTestHelpers.AssertString(prim2.Metadata["displayName"], "Widget");
            Assert.That(prim2.Metadata.ContainsKey("kind"), Is.False);
            Assert.That(prim2.Metadata.ContainsKey("doc"), Is.False);
            Assert.That(prim2.Metadata.ContainsKey("apiSchemas"), Is.False);
        }

        [Test]
        public void CustomMetadata_WithCloseParenInStringValue_RoundTrips()
        {
            var stage = new UsdStage("Meta") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform") { Kind = UsdPrimKindEnum.Component };
            prim.Metadata["comment"] = UsdValue.FromString("torque curve peaks at (n) then drops)");
            stage.AddRootPrim(prim);

            UsdStage reparsed = UsdaReader.Parse(UsdaWriter.Write(stage), stage.StageName);

            UsdPrim prim2 = reparsed.Find("/P")!;
            Assert.That(prim2.Kind, Is.EqualTo(UsdPrimKindEnum.Component));
            UsdTestHelpers.AssertString(prim2.Metadata["comment"], "torque curve peaks at (n) then drops)");
        }

        [Test]
        public void Color3fArray_FlatComponentRun_IsRegroupedIntoTupleRows()
        {
            // A tuple-group base type handed back as a flat run of components must be regrouped
            // per tuple, so a color3f[] still authors "[(r, g, b), …]" and not a flat list.
            string usda = EmitRootAttribute(
                "Mesh",
                new UsdAttribute("primvars:displayColor", "color3f[]")
                {
                    Value = UsdTestHelpers.NumberArray(1.0, 0.0, 0.0, 0.0, 1.0, 0.0),
                });

            Assert.That(
                usda,
                Does.Contain("color3f[] primvars:displayColor = [(1.0, 0.0, 0.0), (0.0, 1.0, 0.0)]"));
        }

        [Test]
        public void Color3fArray_GroupedRows_AreEmittedOnePerElement()
        {
            // Three already-grouped rows are themselves divisible by the group width, so the
            // writer must notice the elements are sequences and not regroup them a second time.
            string usda = EmitRootAttribute(
                "Mesh",
                new UsdAttribute("primvars:displayColor", "color3f[]")
                {
                    Value = UsdTestHelpers.Array(
                        UsdTestHelpers.NumberTuple(1.0, 0.0, 0.0),
                        UsdTestHelpers.NumberTuple(0.0, 1.0, 0.0),
                        UsdTestHelpers.NumberTuple(0.0, 0.0, 1.0)),
                });

            Assert.That(
                usda,
                Does.Contain(
                    "color3f[] primvars:displayColor = "
                    + "[(1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)]"));
        }

        [Test]
        public void OpaqueAbsentValue_RendersAsEmptyText()
        {
            bool rendered = UsdaWriter.TryRenderOpaqueValue(UsdValue.Null, out string text);

            Assert.That(rendered, Is.True);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public void CompositeMetadata_IsEmittedInUsdSyntax()
        {
            // A tuple/array metadata value has no scalar spelling, so it renders through the
            // structured renderer rather than being published as a CLR type name.
            var stage = new UsdStage("Meta") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Metadata["extent"] = UsdTestHelpers.NumberTuple(1.0, 2.0);
            prim.Metadata["order"] = UsdTestHelpers.IntegerArray(1L, 2L);
            prim.Metadata["source"] = UsdTestHelpers.Array(UsdValue.FromPathReference("/P/A"));
            stage.AddRootPrim(prim);

            string usda = UsdaWriter.Write(stage);

            Assert.That(usda, Does.Contain("extent = (1.0, 2.0)"));
            Assert.That(usda, Does.Contain("order = [1, 2]"));
            Assert.That(usda, Does.Contain("source = ['/P/A']"));
            Assert.That(usda, Does.Not.Contain("System."));
        }

        [Test]
        public void BoolAttribute_IsEmittedAsALowerCaseLiteral()
        {
            string usda = EmitRootAttribute(
                "Xform",
                new UsdAttribute("visible", "bool") { Value = UsdValue.From(false) });

            Assert.That(usda, Does.Contain("bool visible = false"));
        }

        private static readonly string[] s_stringValues1 = ["xformOp:translate"];
        private static readonly string[] s_stringValues2 = ["./a.usda"];
        private static readonly double[] s_doubleValues1 = [1.0, 2.0, 3.0];
        private static readonly string[] s_stringValues3 = ["/P/Shader.outputs:surface"];
        private static readonly string[] s_stringValues4 = ["/P/Rig.outputs:translate"];
        private static readonly string[] s_stringValues5 = ["a", "b"];
    }
}
