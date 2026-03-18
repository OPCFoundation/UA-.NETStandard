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
                    Assert.That(structWithOptionalFieldsType.EncodingMask, Is.EqualTo(0));
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
                    Assert.That(unionType.SwitchField, Is.EqualTo(0));
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
                        Assert.IsFalse(obj.IsNull);
                    }
                    else
                    {
                        Assert.That(obj.IsNull, Is.True);
                    }
                }
                else
                {
                    Assert.IsFalse(obj.IsNull);
                }
            }
        }
    }
}
