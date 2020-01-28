/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;
using Opc.Ua.Test;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Sample custom types 
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the OpcUaEncoderTests namespace (.NET code namespace is 'Opc.Ua.Client.ComplexTypes.Tests.Types.Encoders').
        /// </summary>
        public const string OpcUaEncoderTests = "http://opcfoundation.org/UA/OpcUaEncoderTests/";
    }

    /// <summary>
    /// Complex Types Common Functions for Tests.
    /// </summary>
    public class ComplexTypesCommon
    {
        protected const int RandomStart = 4840;
        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }
        protected AssemblyModule Module;
        protected ComplexTypeBuilder ComplexTypeBuilder;
        protected int NodeIdCount;


        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            NodeIdCount = 0;
            Module = new AssemblyModule();
            ComplexTypeBuilder = new ComplexTypeBuilder(
                Module,
                Namespaces.OpcUaEncoderTests,
                3,
                "Tests"
                );
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
            // ensure tests are reproducible, reset for every test
            RandomSource = new RandomSource(RandomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region DataPointSources
        [DatapointSource]
        public StructureType[] StructureTypes = (StructureType[])Enum.GetValues(typeof(StructureType));
        #endregion

        #region Public Methods
        /// <summary>
        /// Builds a complex type with all BuiltInTypes as properties.
        /// </summary>
        public Type BuildComplexTypeWithAllBuiltInTypes(StructureType structureType, string testFunc)
        {
            uint typeId = (uint)Interlocked.Add(ref NodeIdCount, 100);
            var complexTypeStructure = new StructureDefinition() {
                BaseDataType = structureType == StructureType.Union ?
                    DataTypeIds.Union : DataTypeIds.Structure,
                DefaultEncodingId = null,
                Fields = GetAllBuiltInTypesFields(),
                StructureType = structureType
            };

            var fieldBuilder = ComplexTypeBuilder.AddStructuredType(
                structureType.ToString() + "." + testFunc,
                complexTypeStructure);
            fieldBuilder.AddTypeIdAttribute(
                new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace),
                new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace),
                new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace)
                );
            int i = 1;
            foreach (var field in complexTypeStructure.Fields)
            {
                Type fieldType = TypeInfo.GetSystemType(field.DataType, null);
                fieldBuilder.AddField(field, fieldType, i++);
            }
            return fieldBuilder.CreateType();
        }

        /// <summary>
        /// Return a collection of fields with BuiltInTypes.
        /// </summary>
        public static StructureFieldCollection GetAllBuiltInTypesFields()
        {
            var collection = new StructureFieldCollection();
            foreach (var builtInType in EncoderCommon.BuiltInTypes)
            {
                if (builtInType == BuiltInType.Null)
                {
                    continue;
                }

                collection.Add(new StructureField() {
                    Name = builtInType.ToString(),
                    DataType = new NodeId((uint)builtInType),
                    ArrayDimensions = null,
                    Description = $"A BuiltInType.{builtInType} property.",
                    IsOptional = false,
                    MaxStringLength = 0,
                    ValueRank = -1
                });
            }
            return collection;
        }

        /// <summary>
        /// Create array of types.
        /// </summary>
        public Type[] CreateComplexTypes(string nameExtension)
        {
            var typeList = new List<Type>();
            foreach (var structureType in StructureTypes)
            {
                typeList.Add(BuildComplexTypeWithAllBuiltInTypes(structureType, nameof(CreateComplexTypes) + nameExtension));
            }
            return typeList.ToArray();
        }

        /// <summary>
        /// Helper to fill type with default values or random Data.
        /// </summary>
        public void FillStructWithValues(BaseComplexType structType, bool randomValues)
        {
            int index = 0;
            foreach (var property in structType.GetPropertyEnumerator())
            {
                var builtInType = TypeInfo.GetBuiltInType(TypeInfo.GetDataTypeId(property.PropertyType));
                var newObj = randomValues ? TypeInfo.GetDefaultValue(builtInType) : DataGenerator.GetRandom(builtInType);
                if (newObj == null)
                {
                    switch (builtInType)
                    {
                        case BuiltInType.XmlElement:
                            var doc = new XmlDocument();
                            newObj = doc.CreateElement("name");
                            break;
                        case BuiltInType.ByteString:
                            newObj = new byte[0];
                            break;
                        case BuiltInType.String:
                            newObj = "This is a test";
                            break;
                        case BuiltInType.ExtensionObject:
                            newObj = ExtensionObject.Null;
                            break;
                        default:
                            Assert.Fail("Unknown null default value");
                            break;
                    }
                }
                structType[property.Name] = newObj;
                Assert.AreEqual(structType[property.Name], newObj);
                Assert.AreEqual(structType[index], newObj);
                index++;
            }
        }
        #endregion

        #region Private Field
        #endregion
    }
}
