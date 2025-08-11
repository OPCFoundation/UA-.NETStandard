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
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;
using Opc.Ua.Test;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Sample custom types
    /// </summary>
    public static class Namespaces
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

        /// <summary>
        /// The mock resolver namespace.
        /// </summary>
        public const string MockResolverUrl = "http://opcfoundation.org/MockResolver";
    }

    /// <summary>
    /// Complex Types Common Functions for Tests.
    /// </summary>
    public class ComplexTypesCommon : EncoderCommon
    {
        protected AssemblyModule m_module;
        protected ComplexTypeBuilder m_complexTypeBuilder;
        protected int m_nodeIdCount;

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            m_nodeIdCount = 0;
            m_module = new AssemblyModule();
            m_complexTypeBuilder = new ComplexTypeBuilder(m_module, Namespaces.OpcUaEncoderTests, 3, "Tests");
        }

        [OneTimeTearDown]
        protected new void OneTimeTearDown() { }

        [SetUp]
        protected new void SetUp() { }

        [TearDown]
        protected new void TearDown() { }

        public class StructureFieldParameter : IFormattable
        {
            public StructureFieldParameter(StructureField structureField)
            {
                Name = structureField.Name;
                BuiltInType = TypeInfo.GetBuiltInType(structureField.DataType);
            }

            public string Name { get; set; }
            public BuiltInType BuiltInType { get; set; }

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return Name;
            }
        }

        [DatapointSource]
        public StructureType[] StructureTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        Enum.GetValues<StructureType>();
#else
        (StructureType[])Enum.GetValues(typeof(StructureType));
#endif

        [DatapointSource]
        public StructureFieldParameter[] StructureField =
        [
            .. GetAllBuiltInTypesFields().Select(s => new StructureFieldParameter(s)),
        ];

        public Type BuildComplexTypeWithAllBuiltInTypes(StructureType structureType, string testFunc)
        {
            return BuildComplexTypeWithAllBuiltInTypes(null, structureType, testFunc, out _);
        }

        /// <summary>
        /// Builds a complex type with all BuiltInTypes as properties.
        /// </summary>
        public Type BuildComplexTypeWithAllBuiltInTypes(
            IServiceMessageContext context,
            StructureType structureType,
            string testFunc,
            out ExpandedNodeId nodeId
        )
        {
            uint typeId = (uint)Interlocked.Add(ref m_nodeIdCount, 100);
            var complexTypeStructure = new StructureDefinition()
            {
                BaseDataType = structureType == StructureType.Union ? DataTypeIds.Union : DataTypeIds.Structure,
                DefaultEncodingId = null,
                Fields = GetAllBuiltInTypesFields(),
                StructureType = structureType,
            };

            IComplexTypeFieldBuilder fieldBuilder = m_complexTypeBuilder.AddStructuredType(
                structureType.ToString() + "." + testFunc,
                complexTypeStructure
            );
            nodeId = new ExpandedNodeId(typeId++, m_complexTypeBuilder.TargetNamespace);
            var binaryEncodingId = new ExpandedNodeId(typeId++, m_complexTypeBuilder.TargetNamespace);
            var xmlEncodingId = new ExpandedNodeId(typeId++, m_complexTypeBuilder.TargetNamespace);
            fieldBuilder.AddTypeIdAttribute(nodeId, binaryEncodingId, xmlEncodingId);
            int i = 1;
            foreach (StructureField field in complexTypeStructure.Fields)
            {
                Type fieldType = TypeInfo.GetSystemType(field.DataType, null);
                field.IsOptional = structureType == StructureType.StructureWithOptionalFields;
                fieldBuilder.AddField(field, fieldType, i++);
            }
            Type complexType = fieldBuilder.CreateType();
            if (context != null)
            {
                context.Factory.AddEncodeableType(nodeId, complexType);
                context.Factory.AddEncodeableType(binaryEncodingId, complexType);
                context.Factory.AddEncodeableType(xmlEncodingId, complexType);
            }
            return complexType;
        }

        /// <summary>
        /// Return a collection of fields with BuiltInTypes.
        /// </summary>
        public static StructureFieldCollection GetAllBuiltInTypesFields()
        {
            var collection = new StructureFieldCollection();
            foreach (BuiltInType builtInType in BuiltInTypes)
            {
                if (
                    builtInType
                    is BuiltInType.Null
                        or BuiltInType.Variant
                        or BuiltInType.DataValue
                        or BuiltInType.ExtensionObject
                        or >= BuiltInType.Number
                )
                {
                    continue;
                }

                collection.Add(
                    new StructureField()
                    {
                        Name = builtInType.ToString(),
                        DataType = new NodeId((uint)builtInType),
                        ArrayDimensions = null,
                        Description = $"A BuiltInType.{builtInType} property.",
                        IsOptional = false,
                        MaxStringLength = 0,
                        ValueRank = -1,
                    }
                );
            }
            return collection;
        }

        /// <summary>
        /// Create array of types for tests.
        /// </summary>
        public void CreateComplexTypes(
            IServiceMessageContext context,
            Dictionary<StructureType, (ExpandedNodeId, Type)> dict,
            string nameExtension
        )
        {
            foreach (StructureType structureType in StructureTypes)
            {
                Type type = BuildComplexTypeWithAllBuiltInTypes(
                    context,
                    structureType,
                    nameof(CreateComplexTypes) + nameExtension,
                    out ExpandedNodeId nodeId
                );
                dict[structureType] = (nodeId, type);
            }
        }

        /// <summary>
        /// Helper to fill type with default values or random Data.
        /// </summary>
        public void FillStructWithValues(BaseComplexType structType, bool randomValues)
        {
            int index = 0;
            foreach (ComplexTypePropertyInfo property in structType.GetPropertyEnumerator())
            {
                BuiltInType builtInType = TypeInfo.GetBuiltInType(TypeInfo.GetDataTypeId(property.PropertyType));
                object newObj = randomValues
                    ? DataGenerator.GetRandom(builtInType)
                    : TypeInfo.GetDefaultValue(builtInType);
                if (newObj == null)
                {
                    // fill known missing default values (by design)
                    switch (builtInType)
                    {
                        case BuiltInType.XmlElement:
                            var doc = new XmlDocument();
                            newObj = doc.CreateElement("name");
                            break;
                        case BuiltInType.ByteString:
                            newObj = Array.Empty<byte>();
                            break;
                        case BuiltInType.String:
                            newObj = "This is a test";
                            break;
                        case BuiltInType.ExtensionObject:
                            newObj = ExtensionObject.Null;
                            break;
                        default:
                            NUnit.Framework.Assert.Fail("Unknown null default value");
                            break;
                    }
                }
                structType[property.Name] = newObj;
                Assert.AreEqual(structType[property.Name], newObj);
                Assert.AreEqual(structType[index], newObj);
                index++;
            }
        }

        /// <summary>
        /// Encode and decode a complex type, verify the result against expected data.
        /// </summary>
        protected void EncodeDecodeComplexType(
            IServiceMessageContext encoderContext,
            MemoryStreamType memoryStreamType,
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            StructureType structureType,
            ExpandedNodeId nodeId,
            object data
        )
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{structureType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            ExtensionObject expected = CreateExtensionObject(structureType, nodeId, data);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (
                    IEncoder encoder = CreateEncoder(
                        encoderType,
                        encoderContext,
                        encoderStream,
                        typeof(DataValue),
                        jsonEncodingType
                    )
                )
                {
                    encoder.WriteExtensionObject("ExtensionObject", expected);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    _ = PrettifyAndValidateJson(buffer);
                    break;
                case EncodingType.Xml:
                    _ = PrettifyAndValidateXml(buffer);
                    break;
            }

            using var decoderStream = new MemoryStream(buffer);
            using IDecoder decoder = CreateDecoder(encoderType, encoderContext, decoderStream, typeof(DataValue));
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");
            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            Assert.IsNotNull(result, "Resulting DataValue is Null, " + encodeInfo);
            Assert.AreEqual(expected.Encoding, result.Encoding, encodeInfo);
            //TODO: investigate why AreEqual cannot compare ExtensionObject and Body
            //Assert.AreEqual(expected.Body, result.Body, encodeInfo);
            Assert.IsTrue(
                Utils.IsEqual(expected.Body, result.Body),
                $"Opc.Ua.Utils.IsEqual failed to compare expected and result.\r\n{encodeInfo}.\r\n{expected.Body}!={result.Body}."
            );
        }

        /// <summary>
        /// Create an ExtensionObject for a complex type.
        /// The complex type is the Body.
        /// </summary>
        protected ExtensionObject CreateExtensionObject(StructureType structureType, ExpandedNodeId nodeId, object data)
        {
            return new ExtensionObject(nodeId, data);
        }
    }
}
