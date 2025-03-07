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
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Tests for the Json encoder class.
    /// </summary>
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypesJsonEncoderTests : ComplexTypesCommon
    {
        public ServiceMessageContext EncoderContext;
        public Dictionary<StructureType, (ExpandedNodeId, Type)> TypeDictionary;

        #region Test Setup
        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            EncoderContext = new ServiceMessageContext();
            // create private copy of factory
            EncoderContext.Factory = new EncodeableFactory(EncoderContext.Factory);
            EncoderContext.NamespaceUris.Append("urn:This:is:my:test:encoder");
            EncoderContext.NamespaceUris.Append("urn:This:is:another:namespace");
            EncoderContext.NamespaceUris.Append(Namespaces.OpcUaEncoderTests);
            TypeDictionary = new Dictionary<StructureType, (ExpandedNodeId, Type)>();
            CreateComplexTypes(EncoderContext, TypeDictionary, "");
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
        #endregion Test Setup

        #region DataSource
        /// <summary>
        /// Constants used by test data set.
        /// </summary>
        const Int64 kInt64Value = -123456789123456;
        const UInt64 kUInt64Value = 123456789123456;

        /// <summary>
        /// An array of spec compliant Json encoding test data sets which
        /// shall be followed by the JSON encoder accordingly.
        /// </summary>
        /// <remarks>
        /// Include default value is ignored by tests.
        /// Only a small subset of built in types is tested on complex types.
        /// </remarks>
        [DatapointSource]
        public static readonly JsonValidationData[] Data = new JsonValidationDataCollection() {
            {   BuiltInType.Boolean, false, "false", null, null, "false"},
            {   BuiltInType.Boolean, true,"true", null },
            {   BuiltInType.Byte, (Byte)0, "0", null, null, "0"},
            {   BuiltInType.Byte, (Byte)88, "88", null },
            {   BuiltInType.SByte, (SByte)0, "0", null, null, "0"},
            {   BuiltInType.UInt16, (UInt16)12345, "12345", null },
            {   BuiltInType.Int16, (Int16)(-12345), "-12345", null },
            {   BuiltInType.UInt32, (UInt32)1234567, "1234567", null },
            {   BuiltInType.Int32, (Int32)(-12345678), "-12345678", null },
            {   BuiltInType.Int64, kInt64Value, Quotes(kInt64Value.ToString(CultureInfo.InvariantCulture)), null },
            {   BuiltInType.UInt64, (UInt64)kUInt64Value, Quotes(kUInt64Value.ToString(CultureInfo.InvariantCulture)), null },
            {   BuiltInType.Float, (float)3.14, "3.14", "3.14" },
            // TODO: why is JToken.DeepEquals failing here?
            //{   BuiltInType.Float, float.PositiveInfinity, "Infinity", "Infinity" },
            {   BuiltInType.Double, (double)7.77, "7.77", "7.77" }
        }.ToArray();
        #endregion DataSource

        #region Test Methods
        /// <summary>
        /// Verify encoding of a Structure as body of ExtensionObject.
        /// </summary>
        [Theory]
        public void JsonEncodeStructure(
            JsonValidationData jsonValidationData,
            JsonEncodingType jsonEncoding
            )
        {
            ExpandedNodeId nodeId;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[StructureType.Structure];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            baseType[jsonValidationData.BuiltInType.ToString()] = jsonValidationData.Instance;
            ExtensionObject extensionObject = CreateExtensionObject(StructureType.Structure, nodeId, emittedType);
            EncodeJsonComplexTypeVerifyResult(
                jsonValidationData.BuiltInType,
                MemoryStreamType.ArraySegmentStream,
                extensionObject,
                jsonEncoding,
                jsonValidationData.GetExpected(jsonEncoding),
                false);
        }

        /// <summary>
        /// Verify reversible Json encoding for Structure
        /// with optional fields as body of ExtensionObject.
        /// </summary>
        [Theory]
        public void JsonEncodeOptionalFields(
            JsonValidationData jsonValidationData,
            JsonEncodingType jsonEncoding
            )
        {
            ExpandedNodeId nodeId;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[StructureType.StructureWithOptionalFields];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            baseType[jsonValidationData.BuiltInType.ToString()] = jsonValidationData.Instance;
            ExtensionObject extensionObject = CreateExtensionObject(StructureType.StructureWithOptionalFields, nodeId, emittedType);
            EncodeJsonComplexTypeVerifyResult(
                jsonValidationData.BuiltInType,
                MemoryStreamType.ArraySegmentStream,
                extensionObject,
                jsonEncoding,
                jsonValidationData.GetExpected(jsonEncoding),
                false);
        }

        /// <summary>
        /// Verify reversible Json encoding for Unions with ExtensionObject.
        /// </summary>
        [Theory]
        public void JsonEncodeUnion(
            JsonValidationData jsonValidationData,
            JsonEncodingType jsonEncoding
            )
        {
            ExpandedNodeId nodeId;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[StructureType.Union];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            baseType[jsonValidationData.BuiltInType.ToString()] = jsonValidationData.Instance;
            ExtensionObject extensionObject = CreateExtensionObject(StructureType.Union, nodeId, emittedType);
            EncodeJsonComplexTypeVerifyResult(
                jsonValidationData.BuiltInType,
                MemoryStreamType.ArraySegmentStream,
                extensionObject,
                jsonEncoding,
                jsonValidationData.GetExpected(jsonEncoding),
                false);
        }
        #endregion Test Methods

        #region Private Methods
        protected void EncodeJsonComplexTypeVerifyResult(
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            ExtensionObject data,
            JsonEncodingType jsonEncoding,
            string expected,
            bool topLevelIsArray
            )
        {
            string encodeInfo = $"Encoder: Json Type:{builtInType} Encoding: {jsonEncoding}";

            expected = BuildExpectedResponse(data, builtInType, expected, jsonEncoding);
            var formattedExpected = PrettifyAndValidateJson(expected);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (IEncoder encoder = CreateEncoder(
                    EncodingType.Json, EncoderContext, encoderStream,
                    typeof(ExtensionObject), jsonEncoding, topLevelIsArray))
                {
                    Encode(encoder, BuiltInType.ExtensionObject, builtInType.ToString(), data);
                }
                buffer = encoderStream.ToArray();
            }

            string formattedResult = null;
            string result = null;
            try
            {
                result = Encoding.UTF8.GetString(buffer);
                formattedResult = PrettifyAndValidateJson(result);
                var jsonLoadSettings = new JsonLoadSettings() {
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore
                };
                var resultParsed = JObject.Parse(result, jsonLoadSettings);
                var expectedParsed = JObject.Parse(expected, jsonLoadSettings);
                var areEqual = JToken.DeepEquals(expectedParsed, resultParsed);
                Assert.IsTrue(areEqual, encodeInfo);
            }
            catch
            {
                TestContext.Out.WriteLine(encodeInfo);
                TestContext.Out.WriteLine("Data:");
                TestContext.Out.WriteLine(data);
                TestContext.Out.WriteLine("Expected:");
                TestContext.Out.WriteLine(formattedExpected);
                TestContext.Out.WriteLine("Result:");
                if (!string.IsNullOrEmpty(formattedResult))
                {
                    TestContext.Out.WriteLine(formattedResult);
                }
                else
                {
                    TestContext.Out.WriteLine(result);
                }
            }
        }

        /// <summary>
        /// Build the response for default type and replace
        /// the builtInType parameter with the expected response
        /// depending on the structure type selected.
        /// </summary>
        private string BuildExpectedResponse(
            ExtensionObject data,
            BuiltInType builtInType,
            string expected,
            JsonEncodingType jsonEncoding)
        {
            // build expected result
            string typeId = String.Empty;
            if (!data.TypeId.IsNull)
            {
                var nodeId = ExpandedNodeId.ToNodeId(data.TypeId, EncoderContext.NamespaceUris);
                if (jsonEncoding == JsonEncodingType.NonReversible || jsonEncoding == JsonEncodingType.Reversible)
                {
                    typeId = $"\"TypeId\":{{\"Id\":{nodeId.Identifier},\"Namespace\":{nodeId.NamespaceIndex}}},";
                }
                else
                {
                    typeId = $"\"UaTypeId\":\"{nodeId.Format(EncoderContext, true)}\",";
                }
            }

            bool expectedIsEmpty = false;
            if (String.IsNullOrEmpty(expected))
            {
                expected = "{}";
                expectedIsEmpty = true;
            }

            if (!expectedIsEmpty || jsonEncoding == JsonEncodingType.Compact)
            {
                if (data.Body is UnionComplexType)
                {
                    if (jsonEncoding == JsonEncodingType.Reversible)
                    {
                        var union = data.Body as UnionComplexType;
                        var json = $"{{\"{builtInType}\" :{{";
                        if (!data.TypeId.IsNull)
                        {
                            json += typeId;
                        }
                        json += $"\"Body\":{{\"SwitchField\" : {union.SwitchField}";
                        if (!expectedIsEmpty)
                        {
                            json += ", \"Value\":" + expected;
                        }
                        json += "}}}";
                        expected = json;
                    }
                    else if (jsonEncoding == JsonEncodingType.NonReversible)
                    {
                        expected = $"{{\"{builtInType}\" :" + expected + "}";
                    }
                    else
                    {
                        var union = data.Body as UnionComplexType;
                        var json = $"{{\"{builtInType}\" :{{";

                        if (!data.TypeId.IsNull)
                        {
                            json += typeId;
                        }

                        if (jsonEncoding != JsonEncodingType.Verbose)
                        {
                            json += $"\"SwitchField\" : {union.SwitchField}";

                            if (!expectedIsEmpty)
                            {
                                json += ",";
                            }
                        }

                        if (!expectedIsEmpty)
                        {
                            json += $"\"{builtInType}\":" + expected;
                        }

                        json += "}}";
                        expected = json;
                    }
                }
                else if (data.Body is OptionalFieldsComplexType)
                {
                    if (jsonEncoding == JsonEncodingType.NonReversible)
                    {
                        var json = $"{{\"{builtInType}\" :";
                        expected = json + $"{{\"{builtInType}\" :" + expected + "}}";
                    }
                    else
                    {
                        var optional = data.Body as OptionalFieldsComplexType;
                        var json = $"{{\"{builtInType}\" :{{";

                        if (!data.TypeId.IsNull)
                        {
                            json += typeId;
                        }

                        if (jsonEncoding == JsonEncodingType.Reversible)
                        {
                            json += $"\"Body\":{{";
                        }

                        if (jsonEncoding != JsonEncodingType.Verbose)
                        {
                            json += $"\"EncodingMask\" : {optional.EncodingMask}";

                            if (!expectedIsEmpty)
                            {
                                json += ",";
                            }
                        }

                        if (!expectedIsEmpty)
                        {
                            json += $"\"{builtInType}\":" + expected;
                        }

                        json += "}}";

                        if (jsonEncoding == JsonEncodingType.Reversible)
                        {
                            json += "}";
                        }

                        expected = json;
                    }
                }
                else if (data.Body is BaseComplexType)
                {
                    var structure = data.Body as BaseComplexType;
                    var body = "";
                    bool commaNeeded = false;
                    foreach (var property in structure.GetPropertyEnumerator())
                    {
                        if (builtInType.ToString() == property.Name)
                        {
                            if (!expectedIsEmpty)
                            {
                                if (commaNeeded) body += ",";
                                commaNeeded = true;
                                body += $"\"{builtInType}\":" + expected;
                            }
                        }
                        else if (jsonEncoding != JsonEncodingType.Compact)
                        {
                            object o = property.GetValue(structure);
                            string oText = o?.ToString().ToLowerInvariant();
                            if (property.Name == "DateTime")
                            {
                                oText = "\"0001-01-01T00:00:00Z\"";
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "StatusCode")
                            {
                                if (jsonEncoding == JsonEncodingType.Reversible)
                                {
                                    oText = "0";
                                    // default statuscode is not encoded
                                    continue;
                                }
                                else
                                {
                                    oText = "{}";
                                }
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "ByteString" || property.Name == "XmlElement")
                            {
                                oText = "null";
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "LocalizedText")
                            {
                                if (jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    oText = "\"\"";
                                }
                                else
                                {
                                    oText = "{}";
                                }
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "NodeId" || property.Name == "ExpandedNodeId" || property.Name == "QualifiedName")
                            {
                                if (jsonEncoding == JsonEncodingType.Verbose)
                                {
                                    oText = "\"\"";
                                }
                                else
                                {
                                    oText = "{}";
                                }
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "Guid")
                            {
                                oText = "\"00000000-0000-0000-0000-000000000000\"";
                                if (jsonEncoding == JsonEncodingType.Reversible || jsonEncoding == JsonEncodingType.NonReversible)
                                {
                                    continue;
                                }
                            }
                            else if (property.Name == "UInt64" || property.Name == "Int64")
                            {
                                oText = "\"" + oText + "\"";
                            }

                            if (oText != null)
                            {
                                if (commaNeeded) body += ",";
                                commaNeeded = true;
                                body += $"\"{property.Name}\":" + oText;
                            }
                        }
                    }

                    if (jsonEncoding == JsonEncodingType.NonReversible)
                    {
                        var json = $"{{\"{builtInType}\" :{{";
                        expected = json + body + "}";
                    }
                    else
                    {
                        var json = $"{{\"{builtInType}\" :{{";

                        if (!data.TypeId.IsNull)
                        {
                            json += typeId;
                        }

                        if (jsonEncoding == JsonEncodingType.Reversible)
                        {
                            json += "\"Body\":{" + body + "}}}";
                        }
                        else
                        {
                            json += body + "}}";
                        }

                        expected = json;
                    }
                }
            }
            return expected;
        }
        #endregion Private Methods
    }
}
