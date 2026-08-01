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
using Opc.Ua.OpenUsdScene.Scene;

namespace Opc.Ua.OpenUsdScene.Tests
{
    /// <summary>
    /// Unit tests for the §6.3 metadata coercion rules of the materializer: every CLR scalar
    /// kind keeps its own OPC UA DataType, a homogeneous sequence keeps its element type, and
    /// anything the mapping cannot represent falls back to its invariant textual form rather
    /// than being dropped or guessed at.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class MetadataCoercionTests
    {
        [Test]
        public void AnEntryWithAnEmptyKeyIsSkipped()
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata[string.Empty] = "dropped";
            prim.Metadata["kept"] = "value";

            List<PropertyState> properties = MetadataProperties(prim);

            Assert.That(properties, Has.Count.EqualTo(1));
            Assert.That(properties[0].BrowseName.Name, Is.EqualTo("kept"));
        }

        [Test]
        public void AnEntryWithANullValueBecomesAValuelessProperty()
        {
            var prim = new UsdPrim("Cube", "Cube");
            prim.Metadata["missing"] = null;

            PropertyState property = SingleMetadataProperty(prim);

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.BaseDataType));
            Assert.That(property.Value.IsNull, Is.True);
        }

        [TestCase((sbyte)-8, Opc.Ua.DataTypes.SByte)]
        [TestCase((byte)8, Opc.Ua.DataTypes.Byte)]
        [TestCase((short)-16, Opc.Ua.DataTypes.Int16)]
        [TestCase((ushort)16, Opc.Ua.DataTypes.UInt16)]
        [TestCase(32u, Opc.Ua.DataTypes.UInt32)]
        [TestCase(64UL, Opc.Ua.DataTypes.UInt64)]
        [TestCase(1.5f, Opc.Ua.DataTypes.Float)]
        public void AScalarKeepsItsOwnDataType(object value, uint expectedDataType)
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
        public void AnUnrepresentableScalarIsCarriedAsInvariantText()
        {
            var prim = new UsdPrim("Cube", "Cube");
            var stamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            prim.Metadata["stamp"] = new UnprintableMetadata(stamp);

            PropertyState property = SingleMetadataProperty(prim);

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo(stamp.ToString("O")));
        }

        [Test]
        public void ANestedWriteOnlyDictionaryBecomesASubFolder()
        {
            var prim = new UsdPrim("Cube", "Cube");
            var nested = new WriteOnlyMetadataDictionary { ["vendor"] = "Contoso" };
            prim.Metadata["customData"] = nested;

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
                WithMetadata("flags", new[] { true, false, true }));

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
                WithMetadata("ticks", new[] { 9_000_000_000L, 2L }));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
            Assert.That(property.Value.TryGetValue(out ArrayOf<long> values), Is.True);
            Assert.That(values[0], Is.EqualTo(9_000_000_000L));
        }

        [Test]
        public void AnUnsignedSequenceWidensToInt64()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("ids", new[] { 1u, 2u }));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Int64));
            Assert.That(property.Value.TryGetValue(out ArrayOf<long> values), Is.True);
            Assert.That(values.Count, Is.EqualTo(2));
        }

        [Test]
        public void AFloatingPointSequenceKeepsItsElementType()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("scales", new[] { 1.5f, 2.5f }));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            Assert.That(property.Value.TryGetValue(out ArrayOf<double> values), Is.True);
            Assert.That(values[0], Is.EqualTo(1.5).Within(1e-6));
        }

        [Test]
        public void AnEmptySequenceFallsBackToAStringSequence()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("tags", Array.Empty<object>()));

            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values.Count, Is.Zero);
        }

        [Test]
        public void ASequenceHoldingANullElementFallsBackToText()
        {
            PropertyState property = SingleMetadataProperty(
                WithMetadata("order", new object?[] { 3, null }));

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
                WithMetadata("flags", new object[] { true, "not-a-boolean" }));

            Assert.That(property.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.String));
            Assert.That(property.Value.TryGetValue(out ArrayOf<string> values), Is.True);
            Assert.That(values[1], Is.EqualTo("not-a-boolean"));
        }

        private static UsdPrim WithMetadata(string key, object value)
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

        /// <summary>
        /// A metadata value of a kind the §6.3 mapping does not represent, so the materializer
        /// must fall back to its invariant textual form.
        /// </summary>
        private sealed class UnprintableMetadata
        {
            private readonly DateTime m_stamp;

            public UnprintableMetadata(DateTime stamp)
            {
                m_stamp = stamp;
            }

            public override string ToString()
            {
                return m_stamp.ToString("O");
            }
        }

        /// <summary>
        /// A dictionary that implements only the mutable dictionary contract, so the nested
        /// customData detection has to fall through to its second, read-write case.
        /// </summary>
        private sealed class WriteOnlyMetadataDictionary : IDictionary<string, object?>
        {
            private readonly Dictionary<string, object?> m_inner =
                new Dictionary<string, object?>(StringComparer.Ordinal);

            public object? this[string key]
            {
                get => m_inner[key];
                set => m_inner[key] = value;
            }

            public ICollection<string> Keys => m_inner.Keys;

            public ICollection<object?> Values => m_inner.Values;

            public int Count => m_inner.Count;

            public bool IsReadOnly => false;

            public void Add(string key, object? value)
            {
                m_inner.Add(key, value);
            }

            public void Add(KeyValuePair<string, object?> item)
            {
                m_inner.Add(item.Key, item.Value);
            }

            public void Clear()
            {
                m_inner.Clear();
            }

            public bool Contains(KeyValuePair<string, object?> item)
            {
                return m_inner.TryGetValue(item.Key, out object? found) && Equals(found, item.Value);
            }

            public bool ContainsKey(string key)
            {
                return m_inner.ContainsKey(key);
            }

            public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
            {
                ((ICollection<KeyValuePair<string, object?>>)m_inner).CopyTo(array, arrayIndex);
            }

            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                return m_inner.GetEnumerator();
            }

            public bool Remove(string key)
            {
                return m_inner.Remove(key);
            }

            public bool Remove(KeyValuePair<string, object?> item)
            {
                return m_inner.Remove(item.Key);
            }

            public bool TryGetValue(string key, out object? value)
            {
                return m_inner.TryGetValue(key, out value);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return m_inner.GetEnumerator();
            }
        }
    }
}
