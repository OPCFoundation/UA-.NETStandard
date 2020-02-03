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
using System.Text;
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
    public class ComplexTypesCommon : EncoderCommon
    {
        protected AssemblyModule Module;
        protected ComplexTypeBuilder ComplexTypeBuilder;
        protected int NodeIdCount;


        #region Test Setup
        [OneTimeSetUp]
        protected new void OneTimeSetUp()
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
        #endregion

        #region DataPointSources
        public class StructureFieldParameter : IFormattable
        {
            public StructureFieldParameter(StructureField structureField)
            {
                Name = structureField.Name;
                BuiltInType = TypeInfo.GetBuiltInType(structureField.DataType);
            }

            public string Name;
            public BuiltInType BuiltInType;

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return Name;
            }
        }

        [DatapointSource]
        public StructureType[] StructureTypes = (StructureType[])Enum.GetValues(typeof(StructureType));

        [DatapointSource]
        public StructureFieldParameter[] StructureField = GetAllBuiltInTypesFields().Select(s => new StructureFieldParameter(s)).ToArray();
        #endregion

        #region Public Methods
        public Type BuildComplexTypeWithAllBuiltInTypes(
            StructureType structureType, string testFunc)
        {
            return BuildComplexTypeWithAllBuiltInTypes(null, structureType, testFunc, out ExpandedNodeId nodeId);
        }

        /// <summary>
        /// Builds a complex type with all BuiltInTypes as properties.
        /// </summary>
        public Type BuildComplexTypeWithAllBuiltInTypes(
            ServiceMessageContext context,
            StructureType structureType,
            string testFunc,
            out ExpandedNodeId nodeId)
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
            nodeId = new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace);
            var binaryEncodingId = new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace);
            var xmlEncodingId = new ExpandedNodeId(typeId++, ComplexTypeBuilder.TargetNamespace);
            fieldBuilder.AddTypeIdAttribute(
                nodeId, binaryEncodingId, xmlEncodingId
                );
            int i = 1;
            foreach (var field in complexTypeStructure.Fields)
            {
                Type fieldType = TypeInfo.GetSystemType(field.DataType, null);
                field.IsOptional = structureType == StructureType.StructureWithOptionalFields;
                fieldBuilder.AddField(field, fieldType, i++);
            }
            var complexType = fieldBuilder.CreateType();
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
            foreach (var builtInType in EncoderCommon.BuiltInTypes)
            {
                if (builtInType == BuiltInType.Null ||
                    builtInType == BuiltInType.Variant ||
                    builtInType == BuiltInType.DataValue ||
                    builtInType == BuiltInType.ExtensionObject ||
                    builtInType >= BuiltInType.Number
                    )
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
        /// Create array of types for tests.
        /// </summary>
        public void CreateComplexTypes(
            ServiceMessageContext context,
            Dictionary<StructureType, (ExpandedNodeId, Type)> dict,
            string nameExtension)
        {
            foreach (var structureType in StructureTypes)
            {
                var type = BuildComplexTypeWithAllBuiltInTypes(
                    context,
                    structureType,
                    nameof(CreateComplexTypes) + nameExtension,
                    out ExpandedNodeId nodeId);
                dict[structureType] = (nodeId, type);
            }
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
                var newObj = randomValues ? DataGenerator.GetRandom(builtInType) : TypeInfo.GetDefaultValue(builtInType);
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

        /// <summary>
        /// Encode and decode a complex type, verify the result against expected data.
        /// </summary>
        protected void EncodeDecodeComplexType(
            ServiceMessageContext encoderContext,
            EncodingType encoderType,
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
            var encoderStream = new MemoryStream();
            IEncoder encoder = CreateEncoder(encoderType, encoderContext, encoderStream, typeof(DataValue));
            encoder.WriteExtensionObject("ExtensionObject", expected);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            string jsonFormatted;
            switch (encoderType)
            {
                case EncodingType.Json:
                    jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }
            var decoderStream = new MemoryStream(buffer);
            IDecoder decoder = CreateDecoder(encoderType, encoderContext, decoderStream, typeof(DataValue));
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");
            Dispose(decoder);
            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            Assert.IsNotNull(result, "Resulting DataValue is Null, " + encodeInfo);
            Assert.AreEqual(expected.Encoding, result.Encoding, encodeInfo);
            //TODO: investigate why AreEqual cannot compare ExtensionObject and Body
            //Assert.AreEqual(expected.Body, result.Body, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected.Body, result.Body), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Create an ExtensionObject for a complex type.
        /// The complex type is the Body.
        /// </summary>
        protected ExtensionObject CreateExtensionObject(StructureType structureType, ExpandedNodeId nodeId, object data)
        {
            return new ExtensionObject(nodeId, data);
        }
        #endregion

        #region Private Field
        #endregion
    }
}
