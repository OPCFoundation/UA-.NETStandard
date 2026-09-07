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
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Unit tests for the §6.3 metadata coercion rules of the materializer: every USD scalar
    /// kind keeps its own OPC UA DataType, a homogeneous sequence keeps its element type, and
    /// mixed sequences fall back to their textual form rather than being dropped or guessed at.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class MetadataCoercionTests
    {
        [Test]
        public void AnEntryWithAnEmptyKeyIsSkipped()
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata[string.Empty] = UsdValue.FromString("dropped");
            prim.Metadata["kept"] = UsdValue.FromString("value");

            List<PropertyState> properties = MetadataProperties(prim);

            Assert.That(properties, Has.Count.EqualTo(1));
            Assert.That(properties[0].BrowseName.Name, Is.EqualTo("kept"));
        }

        [Test]
        public void AnEntryWithANullValueBecomesAValuelessProperty()
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata["missing"] = UsdValue.Null;

            PropertyState property = SingleMetadataProperty(prim);

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.BaseDataType));
            Assert.That(property.Value.IsNull, Is.True);
        }

        [TestCaseSource(nameof(ScalarCases))]
        public void AScalarKeepsItsOwnDataType(UsdValue value, uint expectedDataType)
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata["scalar"] = value;

            PropertyState property = SingleMetadataProperty(prim);

            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(
                property.DataType,
                Is.EqualTo(new NodeId(expectedDataType)),
                "The materialized DataType must be the one the CLR value maps to.");
        }

        [Test]
        public void ATextScalarIsCarriedAsText()
        {
            var prim = new UsdPrim("Cube", "Cube");
            var stamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            prim.Metadata["stamp"] = UsdValue.FromString(stamp.ToString("O"));

            PropertyState property = SingleMetadataProperty(prim);

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo(stamp.ToString("O")));
        }

        [Test]
        public void ANestedDictionaryBecomesASubFolder()
        {
            var prim = new UsdPrim("Cube", "Cube");
            var nested = new Dictionary<string, UsdValue>(StringComparer.Ordinal)
            {
                ["vendor"] = UsdValue.FromString("Contoso")
            };
            prim.Metadata["customData"] = UsdValue.FromDictionary(nested);

            MaterializedScene scene = Materialize(prim);
            FolderState metadata = MetadataFolder(scene);
            List<FolderState> folders =
                MaterializationHarness.ChildrenOfType<FolderState>(scene.Context, metadata);

            Assert.That(folders, Has.Count.EqualTo(1));
            Assert.That(folders[0].BrowseName.Name, Is.EqualTo("customData"));
            List<PropertyState> inner =
                MaterializationHarness.ChildrenOfType<PropertyState>(scene.Context, folders[0]);
            Assert.That(inner, Has.Count.EqualTo(1));
            Assert.That(inner[0].BrowseName.Name, Is.EqualTo("vendor"));
        }

        [Test]
        public void ABooleanSequenceKeepsItsElementType()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata(
                    "flags",
                    UsdTestHelpers.Array(
                        UsdValue.From(true),
                        UsdValue.From(false),
                        UsdValue.From(true))));

            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Boolean));
            Assert.That(property.Value.TryGetValue(out ArrayOf<bool> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(3));
            Assert.That(values[1], Is.False);
        }

        [Test]
        public void ALongSequenceKeepsItsElementType()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("ticks", UsdTestHelpers.IntegerArray(9_000_000_000L, 2L)));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
            Assert.That(property.Value.TryGetValue(out ArrayOf<long> values), Is.True);
            Assert.That(values[0], Is.EqualTo(9_000_000_000L));
        }

        [Test]
        public void AnUnsignedSequenceWidensToInt64()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("ids", UsdTestHelpers.IntegerArray(1L, 2L)));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
            Assert.That(property.Value.TryGetValue(out ArrayOf<long> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(2));
        }

        [Test]
        public void AFloatingPointSequenceKeepsItsElementType()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("scales", UsdTestHelpers.NumberArray(1.5, 2.5)));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            Assert.That(property.Value.TryGetValue(out ArrayOf<double> values), Is.True);
            Assert.That(values[0], Is.EqualTo(1.5).Within(1e-6));
        }

        [Test]
        public void AnEmptySequenceFallsBackToAStringSequence()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("tags", UsdValue.FromArray(ArrayOf<UsdValue>.Empty)));

            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values.Count, Is.Zero);
        }

        [Test]
        public void ASequenceHoldingANullElementFallsBackToText()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("order", UsdTestHelpers.Array(UsdValue.From(3L), UsdValue.Null)));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo("3"));
            Assert.That(values[1], Is.Empty);
        }

        [Test]
        public void ASequenceWithAnUnconvertibleElementFallsBackToText()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata(
                    "flags",
                    UsdTestHelpers.Array(UsdValue.From(true), UsdValue.FromString("not-a-boolean"))));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values[1], Is.EqualTo("not-a-boolean"));
        }

        private static IEnumerable<TestCaseData> ScalarCases()
        {
            yield return new TestCaseData(UsdValue.From(false), Opc.Ua.DataTypes.Boolean);
            yield return new TestCaseData(UsdValue.From(32L), Opc.Ua.DataTypes.Int64);
            yield return new TestCaseData(UsdValue.From(1.5), Opc.Ua.DataTypes.Double);
            yield return new TestCaseData(UsdValue.FromString("text"), Opc.Ua.DataTypes.String);
        }

        private static UsdPrim WithMetadata(string key, UsdValue value)
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata[key] = value;
            return prim;
        }

        private static MaterializedScene Materialize(UsdPrim prim)
        {
            var stage = new UsdStage("Test");
            stage.AddRootPrim(prim);
            return MaterializationHarness.Materialize(stage);
        }

        private static FolderState MetadataFolder(MaterializedScene scene)
        {
            List<UsdPrimState> prims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(scene.Context, scene.Stage);
            Assert.That(prims, Has.Count.EqualTo(1));
            FolderState? metadata = prims[0].Metadata;
            Assert.That(metadata, Is.Not.Null);
            return metadata!;
        }

        private static List<PropertyState> MetadataProperties(UsdPrim prim)
        {
            MaterializedScene scene = Materialize(prim);
            return MaterializationHarness.ChildrenOfType<PropertyState>(
                scene.Context, MetadataFolder(scene));
        }

        private static PropertyState SingleMetadataProperty(UsdPrim prim)
        {
            List<PropertyState> properties = MetadataProperties(prim);
            Assert.That(properties, Has.Count.EqualTo(1));
            return properties[0];
        }

    }
}
