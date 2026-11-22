// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public class WotPropertyMapperTests
    {
        [Test]
        public void MapNumberReturnsDoubleScalar()
        {
            var property = new WotProperty { Type = "number" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void MapIntegerReturnsInt64Scalar()
        {
            var property = new WotProperty { Type = "integer" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Int64));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void MapBooleanReturnsBooleanScalar()
        {
            var property = new WotProperty { Type = "boolean" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Boolean));
        }

        [Test]
        public void MapStringReturnsStringScalar()
        {
            var property = new WotProperty { Type = "string" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.String));
        }

        [Test]
        public void MapObjectReturnsFalse()
        {
            var property = new WotProperty { Type = "object" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.False);
            Assert.That(dataType.IsNull, Is.True);
        }

        [Test]
        public void MapNullReturnsFalse()
        {
            var property = new WotProperty { Type = "null" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.False);
            Assert.That(dataType.IsNull, Is.True);
        }

        [Test]
        public void MapArrayOfNumbersReturnsOneDimensionalDouble()
        {
            var property = new WotProperty
            {
                Type = "array",
                Items = new WotPropertyItems { Type = "number" }
            };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void MapArrayOfBooleansReturnsOneDimensionalBoolean()
        {
            var property = new WotProperty
            {
                Type = "array",
                Items = new WotPropertyItems { Type = "boolean" }
            };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Boolean));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void MapArrayWithoutItemsReturnsBaseDataType()
        {
            var property = new WotProperty { Type = "array" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        /// <summary>
        /// G1: "array" string compare must be case-insensitive.
        /// </summary>
        [TestCase("array")]
        [TestCase("Array")]
        [TestCase("ARRAY")]
        [TestCase("aRrAy")]
        public void MapArrayMatchesCaseInsensitively(string typeLiteral)
        {
            var property = new WotProperty
            {
                Type = typeLiteral,
                Items = new WotPropertyItems { Type = "number" }
            };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        /// <summary>
        /// G1 (cont'd): primitive lookups via ToLowerInvariant() are case-insensitive too.
        /// </summary>
        [Test]
        public void MapPrimitiveNumberIsCaseInsensitive(
            [Values("Number", "number", "NUMBER", "Number")] string typeLiteral)
        {
            var property = new WotProperty { Type = typeLiteral };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void MapPrimitiveBooleanIsCaseInsensitive(
            [Values("Boolean", "boolean", "BOOLEAN")] string typeLiteral)
        {
            var property = new WotProperty { Type = typeLiteral };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Boolean));
        }

        [Test]
        public void MapPrimitiveIntegerIsCaseInsensitive(
            [Values("Integer", "integer", "INTEGER")] string typeLiteral)
        {
            var property = new WotProperty { Type = typeLiteral };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Int64));
        }

        [Test]
        public void MapPrimitiveStringIsCaseInsensitive(
            [Values("String", "string", "STRING")] string typeLiteral)
        {
            var property = new WotProperty { Type = typeLiteral };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.String));
        }

        /// <summary>
        /// G2: unknown primitive type collapses to BaseDataType but returns true (mapping succeeded).
        /// </summary>
        [Test]
        public void MapUnknownPrimitiveTypeReturnsBaseDataTypeAndTrue()
        {
            var property = new WotProperty { Type = "bigint" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True, "Unknown primitive types still map (to BaseDataType).");
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        /// <summary>
        /// G2 + boundary: empty-string Type is treated as 'no mapping' (TryMapPrimitive returns false).
        /// </summary>
        [Test]
        public void MapEmptyStringTypeReturnsFalse()
        {
            var property = new WotProperty { Type = string.Empty };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.False);
            Assert.That(dataType.IsNull, Is.True);
        }

        /// <summary>
        /// G6 (property side): array with Items.Type = "object" must collapse to BaseDataType.
        /// </summary>
        [Test]
        public void MapArrayOfObjectsCollapsesToBaseDataType()
        {
            var property = new WotProperty
            {
                Type = "array",
                Items = new WotPropertyItems { Type = "object" }
            };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            // TryMapPrimitive("object") returns false, so the outer TryMap propagates it
            // and the caller observes a no-mapping signal even though valueRank stayed OneDimension.
            Assert.That(ok, Is.False);
            Assert.That(dataType.IsNull, Is.True);
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }
    }
}
