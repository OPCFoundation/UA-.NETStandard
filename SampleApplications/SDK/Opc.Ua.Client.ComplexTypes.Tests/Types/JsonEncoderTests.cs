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
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;

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
        #endregion


        #region DataSource
        /// <summary>
        /// An array of spec compliant Json encoding test data sets which
        /// shall be followed by the JSON encoder accordingly.
        /// </summary>
        [DatapointSource]
        public JsonValidationData[] Data = new JsonValidationDataCollection() {
            {   BuiltInType.Float, (float)3.14, "3.14", "3.14" },
            {   BuiltInType.Double, (double)7.77, "7.77", "7.77" }
        }.ToArray();
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify reversible Json encoding.
        /// </summary>
        [Theory]
        public void JsonEncodeUnionRev(
            JsonValidationData jsonValidationData)
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
                extensionObject,
                true,
                jsonValidationData.ExpectedNonReversible ?? jsonValidationData.ExpectedReversible,
                false);
        }

        /// <summary>
        /// Verify non reversible Json encoding.
        /// </summary>
        [Theory]
        public void JsonEncodeUnionNonRev(
            JsonValidationData jsonValidationData)
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
                extensionObject,
                false,
                jsonValidationData.ExpectedNonReversible ?? jsonValidationData.ExpectedReversible,
                false);
        }
        #endregion

        #region Private Methods
        protected void EncodeJsonComplexTypeVerifyResult(
            BuiltInType builtInType,
            ExtensionObject data,
            bool useReversibleEncoding,
            string expected,
            bool topLevelIsArray
            )
        {
            string encodeInfo = $"Encoder: Json Type:{builtInType} Reversible: {useReversibleEncoding}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Data:");
            TestContext.Out.WriteLine(data);
            TestContext.Out.WriteLine("Expected:");
            if (data.Body is UnionComplexType)
            {
                if (!String.IsNullOrEmpty(expected))
                {
                    if (useReversibleEncoding)
                    {
                        var union = data.Body as UnionComplexType;
                        var json = $"{{\"{builtInType}\" :{{";
                        if (!data.TypeId.IsNull)
                        {
                            var nodeId = ExpandedNodeId.ToNodeId(data.TypeId, EncoderContext.NamespaceUris);
                            string typeId = $"\"TypeId\":{{\"Id\":{nodeId.Identifier},\"Namespace\":{nodeId.NamespaceIndex}}},";
                            json += typeId;
                        }
                        json += $"\"Body\":{{\"SwitchField\" : {union.SwitchField}, \"Value\":" + expected + "}}}";
                        expected = json;
                    }
                    else
                    {
                        expected = $"{{\"Value\" :" + expected + "}";
                    }
                }
                else
                {
                    expected = "{}";
                }
            }
            var formattedExpected = PrettifyAndValidateJson(expected);
            var encoderStream = new MemoryStream();
            IEncoder encoder = CreateEncoder(EncodingType.Json, EncoderContext, encoderStream, typeof(ExtensionObject), useReversibleEncoding, topLevelIsArray);
            Encode(encoder, BuiltInType.ExtensionObject, builtInType.ToString(), data);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            TestContext.Out.WriteLine("Result:");
            var result = Encoding.UTF8.GetString(buffer);
            var formattedResult = PrettifyAndValidateJson(result);
            var jsonLoadSettings = new JsonLoadSettings() {
                CommentHandling = CommentHandling.Ignore,
                LineInfoHandling = LineInfoHandling.Ignore
            };
            var resultParsed = JObject.Parse(result, jsonLoadSettings);
            var expectedParsed = JObject.Parse(expected, jsonLoadSettings);
            var areEqual = JToken.DeepEquals(expectedParsed, resultParsed);
            Assert.IsTrue(areEqual, encodeInfo);
        }
        #endregion
    }

}
