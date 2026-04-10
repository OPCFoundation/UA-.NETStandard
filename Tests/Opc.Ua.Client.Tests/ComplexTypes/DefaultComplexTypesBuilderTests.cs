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
using Opc.Ua.Client.ComplexTypes;

using ComplexStructure = Opc.Ua.Client.ComplexTypes.Structure;
using ComplexUnion = Opc.Ua.Client.ComplexTypes.Union;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    /// <summary>
    /// Tests for creating default complex types using DefaultComplexTypeBuilder.
    /// Mirrors ComplexSampleTypesBuilder from Opc.Ua.Client.ComplexTypes.Tests.
    /// </summary>
    [TestFixture]
    [Category("DefaultComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultComplexTypesBuilderTests : DefaultComplexTypesCommon
    {
        /// <summary>
        /// Create a structure type from a DataTypeDefinition using DefaultComplexTypeBuilder.
        /// Verify the created type implements IStructure and has expected properties.
        /// </summary>
        [Theory]
        public void CreateComplexType(StructureType structureType)
        {
            int propertyBuiltInTypes = BuiltInTypes.Length - 3;
            IEncodeableType encodeableType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(CreateComplexType));
            Assert.That(encodeableType, Is.Not.Null);

            IEncodeable instance = encodeableType.CreateInstance();
            Assert.That(instance, Is.Not.Null);

            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);

            switch (structureType)
            {
                case StructureType.Structure:
                case StructureType.StructureWithSubtypedValues:
                    Assert.That(structure, Is.Not.Null);
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(structure.GetFields().Count));
                    break;
                case StructureType.StructureWithOptionalFields:
                    var optionalFields = instance as StructureWithOptionalFields;
                    Assert.That(optionalFields, Is.Not.Null);
                    Assert.That(optionalFields.EncodingMask, Is.Zero);
                    Assert.That(
                        propertyBuiltInTypes,
                        Is.EqualTo(optionalFields.GetFields().Count));
                    break;
                case StructureType.Union:
                case StructureType.UnionWithSubtypedValues:
                    var union = instance as ComplexUnion;
                    Assert.That(union, Is.Not.Null);
                    Assert.That(union.SwitchField, Is.Zero);
                    Assert.That(propertyBuiltInTypes, Is.EqualTo(union.GetFields().Count));
                    Assert.That(union.Value.IsNull, Is.True);
                    break;
            }

            IEncodeable encodeable = instance;
            Assert.That(encodeable, Is.Not.Null);

            foreach (IStructureField accessorName in structure.GetFields())
            {
                _ = structure[accessorName.Name];
            }

            for (int i = 0; i < structure.GetFields().Count; i++)
            {
                _ = structure[i];
            }
        }

        /// <summary>
        /// Create a complex type with one data field set with default or random value.
        /// </summary>
        [Theory]
        public void CreateComplexTypeWithData(StructureType structureType, bool randomValue)
        {
            int propertyBuiltInTypes = BuiltInTypes.Length - 1;
            IEncodeableType encodeableType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(CreateComplexTypeWithData) + "." + randomValue.ToString());
            Assert.That(encodeableType, Is.Not.Null);

            IEncodeable instance = encodeableType.CreateInstance();
            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);

            FillStructWithValues(structure, randomValue, NameSpaceUris);

            var union = structure as ComplexUnion;
            for (int i = 0; i < structure.GetFields().Count; i++)
            {
                Variant obj = structure[i];
                if (structureType is StructureType.Union or StructureType.UnionWithSubtypedValues)
                {
                    Assert.That(union, Is.Not.Null);
                    if (union.SwitchField == i + 1)
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

        /// <summary>
        /// Verify that the factory creates builders with correct namespace info.
        /// </summary>
        [Test]
        public void CreateFactoryAndBuilder()
        {
            var factory = new DefaultComplexTypeFactory();
            Assert.That(factory, Is.Not.Null);

            IComplexTypeBuilder builder = factory.Create(
                Namespaces.OpcUaEncoderTests, 5, "TestModule");
            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.TargetNamespace, Is.EqualTo(Namespaces.OpcUaEncoderTests));
            Assert.That(builder.TargetNamespaceIndex, Is.EqualTo(5));
        }

        /// <summary>
        /// Verify that Clone/CreateInstance produces an equivalent but separate instance.
        /// </summary>
        [Theory]
        public void CloneComplexType(StructureType structureType)
        {
            IEncodeableType encodeableType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(CloneComplexType));
            Assert.That(encodeableType, Is.Not.Null);

            IEncodeable instance = encodeableType.CreateInstance();
            var structure = instance as ComplexStructure;
            Assert.That(structure, Is.Not.Null);

            FillStructWithValues(structure, true, NameSpaceUris);

            var cloned = structure.Clone() as ComplexStructure;
            Assert.That(cloned, Is.Not.Null);
            Assert.That(structure.IsEqual(cloned), Is.True);
            Assert.That(ReferenceEquals(structure, cloned), Is.False);
        }

        /// <summary>
        /// Verify the StructureType property of each type variant.
        /// </summary>
        [Theory]
        public void VerifyStructureType(StructureType structureType)
        {
            IEncodeableType encodeableType = BuildComplexTypeWithAllBuiltInTypes(
                structureType,
                nameof(VerifyStructureType));
            Assert.That(encodeableType, Is.Not.Null);

            IEncodeable instance = encodeableType.CreateInstance();
            var structureTypeInfo = instance as IStructureTypeInfo;
            Assert.That(structureTypeInfo, Is.Not.Null);

            switch (structureType)
            {
                case StructureType.Structure:
                case StructureType.StructureWithSubtypedValues:
                    Assert.That(
                        structureTypeInfo.StructureType,
                        Is.EqualTo(StructureType.Structure));
                    break;
                case StructureType.StructureWithOptionalFields:
                    Assert.That(
                        structureTypeInfo.StructureType,
                        Is.EqualTo(StructureType.StructureWithOptionalFields));
                    break;
                case StructureType.Union:
                case StructureType.UnionWithSubtypedValues:
                    Assert.That(
                        structureTypeInfo.StructureType,
                        Is.EqualTo(StructureType.Union));
                    break;
            }
        }
    }
}
