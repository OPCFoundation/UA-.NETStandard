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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Core.Tests.Types.Encoders;
using Opc.Ua.Test;

using ComplexStructure = Opc.Ua.Encoders.Structure;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    /// <summary>
    /// Namespace constants for the default complex type tests.
    /// </summary>
    public static class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace.
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace.
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the test encoder namespace.
        /// </summary>
        public const string OpcUaEncoderTests = "http://opcfoundation.org/UA/DefaultComplexTypeTests/";
    }

    /// <summary>
    /// Common base class for default complex type tests.
    /// Uses DefaultComplexTypeFactory/Builder instead of Reflection.Emit.
    /// </summary>
    public abstract class DefaultComplexTypesCommon : EncoderCommon
    {
        protected DefaultComplexTypeFactory m_factory;
        protected IComplexTypeBuilder m_complexTypeBuilder;
        protected int m_nodeIdCount;

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            m_nodeIdCount = 0;
            m_factory = new DefaultComplexTypeFactory();
            m_complexTypeBuilder = m_factory.Create(
                Namespaces.OpcUaEncoderTests,
                3,
                "DefaultTests");
        }

        [OneTimeTearDown]
        protected new void OneTimeTearDown()
        {
        }

        [SetUp]
        protected new void SetUp()
        {
        }

        [TearDown]
        protected new void TearDown()
        {
        }

        /// <summary>
        /// Parameter class for structure field tests.
        /// </summary>
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
            .. GetAllBuiltInTypesFields().Select(s => new StructureFieldParameter(s))
        ];

        internal IEncodeableType BuildComplexTypeWithAllBuiltInTypes(
            StructureType structureType,
            string testFunc)
        {
            return BuildComplexTypeWithAllBuiltInTypes(null, structureType, testFunc, out _);
        }

        /// <summary>
        /// Builds a complex type with all BuiltInTypes as properties
        /// using the DefaultComplexTypeBuilder.
        /// </summary>
        private IEncodeableType BuildComplexTypeWithAllBuiltInTypes(
            IServiceMessageContext context,
            StructureType structureType,
            string testFunc,
            out ExpandedNodeId nodeId)
        {
            uint typeId = (uint)Interlocked.Add(ref m_nodeIdCount, 100);
            var complexTypeStructure = new StructureDefinition
            {
                BaseDataType = structureType == StructureType.Union
                    ? DataTypeIds.Union
                    : DataTypeIds.Structure,
                DefaultEncodingId = default,
                Fields = GetAllBuiltInTypesFields(),
                StructureType = structureType
            };

            IComplexTypeFieldBuilder fieldBuilder = m_complexTypeBuilder.AddStructuredType(
                QualifiedName.From(structureType + "." + testFunc),
                complexTypeStructure);
            nodeId = new ExpandedNodeId(typeId++, m_complexTypeBuilder.TargetNamespace);
            var binaryEncodingId = new ExpandedNodeId(
                typeId++,
                m_complexTypeBuilder.TargetNamespace);
            var xmlEncodingId = new ExpandedNodeId(typeId++, m_complexTypeBuilder.TargetNamespace);
            fieldBuilder.AddTypeIdAttribute(nodeId, binaryEncodingId, xmlEncodingId);
            int i = 1;
            foreach (StructureField field in complexTypeStructure.Fields)
            {
                IType fieldType = TypeInfo.GetSystemType(field.DataType, null);
                field.IsOptional = structureType == StructureType.StructureWithOptionalFields;
                fieldBuilder.AddField(field, fieldType, i++, false);
            }
            IEncodeableType complexType = fieldBuilder.CreateType();
            if (context != null)
            {
                context.Factory.Builder.AddEncodeableType(nodeId, complexType).Commit();
                context.Factory.Builder.AddEncodeableType(binaryEncodingId, complexType).Commit();
                context.Factory.Builder.AddEncodeableType(xmlEncodingId, complexType).Commit();
            }
            return complexType;
        }

        /// <summary>
        /// Return a collection of fields with BuiltInTypes.
        /// </summary>
        private static List<StructureField> GetAllBuiltInTypesFields()
        {
            var collection = new List<StructureField>();
            foreach (BuiltInType builtInType in BuiltInTypes)
            {
                if (builtInType
                    is BuiltInType.Null
                        or BuiltInType.Variant
                        or BuiltInType.DataValue
                        or BuiltInType.ExtensionObject
                        or >= BuiltInType.Number)
                {
                    continue;
                }

                collection.Add(
                    new StructureField
                    {
                        Name = builtInType.ToString(),
                        DataType = new NodeId((uint)builtInType),
                        ArrayDimensions = default,
                        Description = LocalizedText.From($"A BuiltInType.{builtInType} property."),
                        IsOptional = false,
                        MaxStringLength = 0,
                        ValueRank = -1
                    });
            }
            return collection;
        }

        /// <summary>
        /// Create complex types for all structure types and register them.
        /// </summary>
        internal void CreateComplexTypes(
            IServiceMessageContext context,
            Dictionary<StructureType, (ExpandedNodeId, IEncodeableType)> dict,
            string nameExtension)
        {
            foreach (StructureType structureType in StructureTypes)
            {
                IEncodeableType encodeableType = BuildComplexTypeWithAllBuiltInTypes(
                    context,
                    structureType,
                    nameof(CreateComplexTypes) + nameExtension,
                    out ExpandedNodeId nodeId);
                dict[structureType] = (nodeId, encodeableType);
            }
        }

        /// <summary>
        /// Helper to fill a Structure-based type with default or random values.
        /// </summary>
        internal void FillStructWithValues(
            ComplexStructure structType,
            bool randomValues,
            NamespaceTable namespaceUris)
        {
            int index = 0;
            foreach (IStructureField property in structType.GetFields())
            {
                BuiltInType builtInType = property.TypeInfo.BuiltInType;
                Variant newObj = randomValues
                    ? DataGenerator.GetRandomVariant(builtInType, !property.TypeInfo.IsScalar)
                    : Variant.CreateDefault(TypeInfo.Create(builtInType, property.TypeInfo.ValueRank));
                if (newObj.IsNull)
                {
                    switch (builtInType)
                    {
                        case BuiltInType.XmlElement:
                            var doc = new XmlDocument();
                            newObj = Variant.From(XmlElement.From(doc.CreateElement("name")));
                            break;
                        case BuiltInType.ByteString:
                            newObj = Variant.From(ByteString.From([1, 2, 3]));
                            break;
                        case BuiltInType.String:
                            newObj = Variant.From("This is a test");
                            break;
                        case BuiltInType.ExtensionObject:
                            newObj = Variant.From(ExtensionObject.Null);
                            break;
                        default:
                            newObj = Variant.CreateDefault(property.TypeInfo);
                            break;
                    }
                }
                structType[property.Name] = newObj;
                Assert.That(newObj, Is.EqualTo(structType[property.Name]));
                Assert.That(newObj, Is.EqualTo(structType[index]));
                index++;
            }
        }

        /// <summary>
        /// Encode and decode a default complex type, verify the result.
        /// </summary>
        protected void EncodeDecodeComplexType(
            IServiceMessageContext encoderContext,
            MemoryStreamType memoryStreamType,
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            bool useXmlParser,
            StructureType structureType,
            ExpandedNodeId nodeId,
            IEncodeable data)
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{structureType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            ExtensionObject expected = CreateExtensionObject(structureType, nodeId, data);
            Assert.That(expected.IsNull, Is.False, "Expected DataValue is Null, " + encodeInfo);
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
                        jsonEncodingType))
                {
                    encoder.WriteExtensionObject("ExtensionObject", expected);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Binary:
                    TestContext.Out.WriteLine(PrettifyAndValidateBinary(buffer));
                    break;
                case EncodingType.Json:
                    TestContext.Out.WriteLine(PrettifyAndValidateJson(buffer));
                    break;
                case EncodingType.Xml:
                    TestContext.Out.WriteLine(PrettifyAndValidateXml(buffer));
                    break;
            }

            using var decoderStream = new MemoryStream(buffer);
            using IDecoder decoder = CreateDecoder(
                encoderType,
                useXmlParser,
                encoderContext,
                decoderStream,
                typeof(DataValue));
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");
            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            Assert.That(result.IsNull, Is.False, "Resulting DataValue is Null, " + encodeInfo);
            Assert.That(result.Encoding, Is.EqualTo(expected.Encoding), encodeInfo);
            Assert.That(result, Is.EqualTo(expected),
                $"Failed to compare expected and result.\r\n{encodeInfo}.\r\n{expected}\r\n!=\r\n{result}.");
        }

        /// <summary>
        /// Create an ExtensionObject for a complex type.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        protected ExtensionObject CreateExtensionObject(
            StructureType structureType,
            ExpandedNodeId nodeId,
            object data)
        {
            switch (data)
            {
                case ByteString b:
                    return new ExtensionObject(nodeId, b);
                case XmlElement x:
                    return new ExtensionObject(nodeId, x);
                case IEncodeable e:
                    return new ExtensionObject(e, true);
                default:
                    throw new ArgumentException(
                        $"Unsupported data type {data.GetType()}" +
                        $" for structure type {structureType}.");
            }
        }
    }
}
