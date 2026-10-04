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

using System;
using System.Runtime.Serialization;
using NUnit.Framework;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Main purpose of this test is to verify the
    /// system.emit functionality on a target platform.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexSampleTypesBuilder : ComplexTypesCommon
    {
        /// <summary>
        /// Concrete nested fields retain their values in both accessor directions.
        /// </summary>
        [Test]
        [Combinatorial]
        public void ConcreteStructurePropertyPreservesValue(
            [Values(ValueRanks.Scalar, ValueRanks.OneDimension, ValueRanks.TwoDimensions)] int valueRank,
            [Values] bool write,
            [Values(BuiltInType.Null, BuiltInType.ExtensionObject)] BuiltInType builtInType)
        {
            var child = new Argument { Name = "Nested", DataType = DataTypeIds.Int32 };
            var holder = new StructurePropertyHolder();
            string propertyName = valueRank switch
            {
                ValueRanks.Scalar => nameof(StructurePropertyHolder.Scalar),
                ValueRanks.OneDimension => nameof(StructurePropertyHolder.Array),
                _ => nameof(StructurePropertyHolder.Matrix)
            };
            var reflectionProperty = typeof(StructurePropertyHolder).GetProperty(propertyName);
            var property = new ComplexTypePropertyInfo(
                reflectionProperty,
                new StructureFieldAttribute { BuiltInType = (int)builtInType, ValueRank = valueRank },
                new DataMemberAttribute { Name = propertyName });
            Variant expected = valueRank switch
            {
                ValueRanks.Scalar => Variant.FromStructure(child),
                ValueRanks.OneDimension => Variant.FromStructure(new Argument[] { child }.ToArrayOf()),
                _ => Variant.FromStructure(MatrixOf.From<Argument>(new Argument[,] { { child } }))
            };

            if (write)
            {
                property.SetValue(holder, expected);
                switch (valueRank)
                {
                    case ValueRanks.Scalar:
                        Assert.That(holder.Scalar, Is.SameAs(child));
                        break;
                    case ValueRanks.OneDimension:
                        Assert.That(holder.Array, Is.EqualTo(new Argument[] { child }));
                        break;
                    default:
                        Assert.That(holder.Matrix, Is.EqualTo(new Argument[,] { { child } }));
                        break;
                }
            }
            else
            {
                holder.Scalar = child;
                holder.Array = [child];
                holder.Matrix = new Argument[,] { { child } };
                Variant actual = property.GetValue(holder);
                Assert.That(actual.IsNull, Is.False);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        [Combinatorial]
        public void ConcreteStructureCollectionPreservesNullAndEmpty(
            [Values(ValueRanks.OneDimension, ValueRanks.TwoDimensions)] int valueRank,
            [Values] bool isNull,
            [Values(BuiltInType.Null, BuiltInType.ExtensionObject)] BuiltInType builtInType)
        {
            var holder = new StructurePropertyHolder
            {
                Array = [new Argument()],
                Matrix = new Argument[,] { { new Argument() } }
            };
            string propertyName = valueRank == ValueRanks.OneDimension
                ? nameof(StructurePropertyHolder.Array)
                : nameof(StructurePropertyHolder.Matrix);
            var reflectionProperty = typeof(StructurePropertyHolder).GetProperty(propertyName);
            var property = new ComplexTypePropertyInfo(
                reflectionProperty,
                new StructureFieldAttribute { BuiltInType = (int)builtInType, ValueRank = valueRank },
                new DataMemberAttribute { Name = propertyName });
            Variant value = isNull ? Variant.Null : valueRank == ValueRanks.OneDimension
                ? Variant.FromStructure(ArrayOf<IEncodeable>.Empty)
                : Variant.FromStructure(MatrixOf.From<IEncodeable>(new IEncodeable[0, 2]));

            property.SetValue(holder, value);

            if (isNull)
            {
                Assert.That(reflectionProperty.GetValue(holder), Is.Null);
                Variant actual = property.GetValue(holder);
                Assert.That(valueRank == ValueRanks.OneDimension
                    ? actual.GetStructureArray<IEncodeable>().IsNull
                    : actual.GetStructureMatrix<IEncodeable>().IsNull, Is.True);
            }
            else
            {
                var actual = (Array)reflectionProperty.GetValue(holder);
                Assert.That(actual, Has.Length.Zero);
                Assert.That(actual.Rank, Is.EqualTo(valueRank));
                Assert.That(property.GetValue(holder).IsNull, Is.False);
                if (valueRank == ValueRanks.TwoDimensions)
                {
                    Assert.That(actual.GetLength(1), Is.EqualTo(2));
                }
            }
        }

        /// <summary>
        /// ExtensionObject collection properties retain their wrapper representation.
        /// </summary>
        [Test]
        [Combinatorial]
        public void ExtensionObjectCollectionPreservesValue(
            [Values(ValueRanks.OneDimension, ValueRanks.TwoDimensions)] int valueRank,
            [Values(0, 1, 2)] int elementCount,
            [Values] bool isNull)
        {
            var holder = new StructurePropertyHolder();
            string propertyName = valueRank == ValueRanks.OneDimension
                ? nameof(StructurePropertyHolder.ExtensionArray)
                : nameof(StructurePropertyHolder.ExtensionMatrix);
            var reflectionProperty = typeof(StructurePropertyHolder).GetProperty(propertyName);
            var property = new ComplexTypePropertyInfo(
                reflectionProperty,
                new StructureFieldAttribute { BuiltInType = (int)BuiltInType.ExtensionObject, ValueRank = valueRank },
                new DataMemberAttribute { Name = propertyName });
            var elements = new ExtensionObject[elementCount];
            var matrix = new ExtensionObject[1, elementCount];
            for (int index = 0; index < elementCount; index++)
            {
                elements[index] = new ExtensionObject(new Argument { Name = "Nested" });
                matrix[0, index] = elements[index];
            }
            Variant expected = isNull ? Variant.Null : valueRank == ValueRanks.OneDimension
                ? Variant.From(elements.ToArrayOf())
                : Variant.From(MatrixOf.From<ExtensionObject>(matrix));

            property.SetValue(holder, expected);

            if (isNull)
            {
                Assert.That(reflectionProperty.GetValue(holder), Is.Null);
            }
            else
            {
                Assert.That(property.GetValue(holder), Is.EqualTo(expected));
                Assert.That(reflectionProperty.GetValue(holder), Is.TypeOf(reflectionProperty.PropertyType));
            }
        }

        /// <summary>
        /// Incompatible structure elements cannot replace a concrete collection property.
        /// </summary>
        [TestCase(ValueRanks.OneDimension)]
        [TestCase(ValueRanks.TwoDimensions)]
        public void SubtypedStructureCollectionRejectsIncompatibleElements(int valueRank)
        {
            var holder = new StructurePropertyHolder
            {
                Array = [new Argument()],
                Matrix = new Argument[,] { { new Argument() } }
            };
            string propertyName = valueRank == ValueRanks.OneDimension
                ? nameof(StructurePropertyHolder.Array)
                : nameof(StructurePropertyHolder.Matrix);
            var reflectionProperty = typeof(StructurePropertyHolder).GetProperty(propertyName);
            var previous = (Array)reflectionProperty.GetValue(holder);
            var property = new ComplexTypePropertyInfo(
                reflectionProperty,
                new StructureFieldAttribute { BuiltInType = (int)BuiltInType.ExtensionObject, ValueRank = valueRank },
                new DataMemberAttribute { Name = propertyName });
            Variant incompatible = valueRank == ValueRanks.OneDimension
                ? Variant.FromStructure(new IEncodeable[] { new StructureDefinition() }.ToArrayOf())
                : Variant.FromStructure(MatrixOf.From<IEncodeable>(new IEncodeable[,] { { new StructureDefinition() } }));

            Assert.Throws<InvalidCastException>(() => property.SetValue(holder, incompatible));
            Assert.That(reflectionProperty.GetValue(holder), Is.SameAs(previous));
        }

        /// <summary>
        /// Create a structure type from a DataTypeDefinition.
        /// Activate an object and verify it is the expected type
        /// with expected properties.
        /// </summary>
        [Theory]
        public void CreateComplexType(StructureType structureType)
        {
            // EncoderCommon.BuiltInTypes subtracted by the number of unused types.
            int propertyBuiltInTypes = BuiltInTypes.Length - 3;
            Type complexType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(CreateComplexType));
            Assert.That(complexType, Is.Not.Null);
            object emittedType = Activator.CreateInstance(complexType);
            var structType = emittedType as BaseComplexType;
            switch (structureType)
            {
                case StructureType.Structure:
                    Assert.That(structType, Is.Not.Null);
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(structType.GetPropertyTypes().Count));
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(structType.GetPropertyCount()));
                    break;
                case StructureType.StructureWithOptionalFields:
                    var structWithOptionalFieldsType = emittedType as OptionalFieldsComplexType;
                    Assert.That(structWithOptionalFieldsType, Is.Not.Null);
                    Assert.That(structWithOptionalFieldsType.EncodingMask, Is.Zero);
                    Assert.That(
                        propertyBuiltInTypes,
                        Is.EqualTo(structWithOptionalFieldsType.GetPropertyTypes().Count));
                    Assert.That(
                        propertyBuiltInTypes,
                        Is.EqualTo(structWithOptionalFieldsType.GetPropertyCount()));
                    break;
                case StructureType.Union:
                    var unionType = emittedType as UnionComplexType;
                    Assert.That(unionType, Is.Not.Null);
                    Assert.That(unionType.SwitchField, Is.Zero);
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(unionType.GetPropertyTypes().Count));
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(unionType.GetPropertyCount()));
                    Assert.That(unionType.Value.IsNull, Is.True);
                    break;
            }
            var encodeable = emittedType as IEncodeable;
            Assert.That(encodeable, Is.Not.Null);
            // try the accessor by name
            foreach (string accessorname in structType.GetPropertyNames())
            {
                object obj = structType[accessorname];
            }
            // try the accessor by index
            for (int i = 0; i < structType.GetPropertyCount(); i++)
            {
                object obj = structType[i];
            }
        }

        /// <summary>
        /// Create a complex type with one data field set with default or random value.
        /// </summary>
        [Theory]
        public void CreateComplexTypeWithData(StructureType structureType, bool randomValue)
        {
            // BuiltInTypes - Null type.
            int propertyBuiltInTypes = BuiltInTypes.Length - 1;
            Type complexType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(CreateComplexTypeWithData) + "." + randomValue.ToString());
            Assert.That(complexType, Is.Not.Null);
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;

            // fill struct with default values
            FillStructWithValues(baseType, randomValue, NameSpaceUris);

            for (int i = 0; i < baseType.GetPropertyCount(); i++)
            {
                Variant obj = baseType[i];
                if (structureType is StructureType.Union or StructureType.UnionWithSubtypedValues)
                {
                    if (((UnionComplexType)baseType).SwitchField == i + 1)
                    {
                        Assert.That(obj.IsNull, Is.False);
                    }
                    else
                    {
                        Assert.That(obj.IsNull, Is.True);
                    }
                }
                else
                {
                    Assert.That(obj.IsNull, Is.False);
                }
            }
        }
        private sealed class StructurePropertyHolder
        {
            public Argument Scalar { get; set; }

            public Argument[] Array { get; set; }

            public Argument[,] Matrix { get; set; }

            public ExtensionObject[] ExtensionArray { get; set; }

            public ExtensionObject[,] ExtensionMatrix { get; set; }
        }
    }
}
