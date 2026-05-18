/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void MapIntegerReturnsInt64Scalar()
        {
            var property = new WotProperty { Type = "integer" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Int64));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void MapBooleanReturnsBooleanScalar()
        {
            var property = new WotProperty { Type = "boolean" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Boolean));
        }

        [Test]
        public void MapStringReturnsStringScalar()
        {
            var property = new WotProperty { Type = "string" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out _);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.String));
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
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Double));
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
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Boolean));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void MapArrayWithoutItemsReturnsBaseDataType()
        {
            var property = new WotProperty { Type = "array" };

            bool ok = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);

            Assert.That(ok, Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
        }
    }
}
