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

using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    [TestFixture]
    public class UsdSceneSignatureTests
    {
        [Test]
        public void Signature_ExcludesNonComposedState()
        {
            string bare = UsdSceneSignature.Compute(BuildStage(withNonComposedState: false));
            string decorated = UsdSceneSignature.Compute(BuildStage(withNonComposedState: true));

            Assert.That(decorated, Is.EqualTo(bare));
            Assert.That(UsdSceneSignature.FirstDifference(
                BuildStage(withNonComposedState: false),
                BuildStage(withNonComposedState: true)), Is.Null);
        }

        [Test]
        public void Signature_ExcludesStageLevelMetadata()
        {
            var a = new UsdStage("A") { DefaultPrim = "X", UpAxis = "Y", MetersPerUnit = 1.0 };
            a.AddRootPrim(new UsdPrim("X", "Xform"));

            var b = new UsdStage("B") { DefaultPrim = "Other", UpAxis = "Z", MetersPerUnit = 0.01 };
            b.AddRootPrim(new UsdPrim("X", "Xform"));

            Assert.That(UsdSceneSignature.Compute(b), Is.EqualTo(UsdSceneSignature.Compute(a)));
        }

        [Test]
        public void Signature_DetectsChangedAttributeValue()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Attributes.Add(new UsdAttribute("v", "int") { Value = UsdValue.From(1L) });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Attributes.Add(new UsdAttribute("v", "int") { Value = UsdValue.From(2L) });

            Assert.That(UsdSceneSignature.Compute(b), Is.Not.EqualTo(UsdSceneSignature.Compute(a)));
            Assert.That(UsdSceneSignature.FirstDifference(a, b), Is.Not.Null);
        }

        [Test]
        public void Signature_IsIndependentOfAttributeOrder()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Attributes.Add(new UsdAttribute("a", "int") { Value = UsdValue.From(1L) });
            pa.Attributes.Add(new UsdAttribute("b", "int") { Value = UsdValue.From(2L) });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Attributes.Add(new UsdAttribute("b", "int") { Value = UsdValue.From(2L) });
            pb.Attributes.Add(new UsdAttribute("a", "int") { Value = UsdValue.From(1L) });

            Assert.That(UsdSceneSignature.Compute(b), Is.EqualTo(UsdSceneSignature.Compute(a)));
        }

        [Test]
        public void Signature_IsSensitiveToCompositionArcOrder()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Reference) { AssetPath = "r.usda", PrimPath = "/R" });
            pa.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Instance) { AssetPath = "r.usda", PrimPath = "/R" });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Instance) { AssetPath = "r.usda", PrimPath = "/R" });
            pb.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Reference) { AssetPath = "r.usda", PrimPath = "/R" });

            Assert.That(UsdSceneSignature.Compute(b), Is.Not.EqualTo(UsdSceneSignature.Compute(a)));
        }

        [Test]
        public void Signature_NormalizesEveryValueKind()
        {
            // Every kind must reach the normalizer: an unnormalized kind would make two
            // different scenes sign identically.
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Attributes.Add(new UsdAttribute("flag", "bool") { Value = UsdValue.From(true) });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Attributes.Add(new UsdAttribute("flag", "bool") { Value = UsdValue.From(false) });

            Assert.That(UsdSceneSignature.Compute(b), Is.Not.EqualTo(UsdSceneSignature.Compute(a)));
            Assert.That(UsdSceneSignature.FirstDifference(a, b), Is.Not.Null);
        }

        [Test]
        public void Signature_OfADictionaryValueIsIndependentOfEntryOrder()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Attributes.Add(new UsdAttribute("d", "dictionary")
            {
                Value = Dictionary(("author", UsdValue.FromString("acme")), ("order", UsdValue.From(3L)))
            });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Attributes.Add(new UsdAttribute("d", "dictionary")
            {
                Value = Dictionary(("order", UsdValue.From(3L)), ("author", UsdValue.FromString("acme")))
            });

            Assert.That(UsdSceneSignature.Compute(b), Is.EqualTo(UsdSceneSignature.Compute(a)));
        }

        [Test]
        public void Signature_DetectsAChangedDictionaryEntry()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            pa.Attributes.Add(new UsdAttribute("d", "dictionary")
            {
                Value = Dictionary(("nested", Dictionary(("depth", UsdValue.From(1L)))))
            });

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            pb.Attributes.Add(new UsdAttribute("d", "dictionary")
            {
                Value = Dictionary(("nested", Dictionary(("depth", UsdValue.From(2L)))))
            });

            Assert.That(UsdSceneSignature.Compute(b), Is.Not.EqualTo(UsdSceneSignature.Compute(a)));
        }

        private static UsdValue Dictionary(params (string Key, UsdValue Value)[] entries)
        {
            var map = new System.Collections.Generic.Dictionary<string, UsdValue>(
                System.StringComparer.Ordinal);
            foreach ((string key, UsdValue value) in entries)
            {
                map[key] = value;
            }
            return UsdValue.FromDictionary(map);
        }

        private static UsdStage BuildStage(bool withNonComposedState)
        {
            var stage = new UsdStage("S")
            {
                Documentation = "stage doc is ignored",
            };
            var prim = new UsdPrim("X", "Xform")
            {
                Kind = UsdPrimKindEnum.Component,
                Active = withNonComposedState,
                Instanceable = withNonComposedState,
            };
            var attr = new UsdAttribute("v", "int")
            {
                Value = UsdValue.From(1L),
                Live = withNonComposedState,
                Interpolation = withNonComposedState ? "vertex" : null,
            };
            prim.Attributes.Add(attr);
            if (withNonComposedState)
            {
                prim.ApiSchemas.Add(new UsdApiSchema("MaterialBindingAPI"));
                prim.Metadata["hidden"] = UsdValue.From(true);
            }
            stage.AddRootPrim(prim);
            return stage;
        }
    }
}
