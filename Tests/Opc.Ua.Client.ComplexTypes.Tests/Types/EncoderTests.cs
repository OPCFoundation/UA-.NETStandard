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
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Opc.Ua.Core.Tests.Types.Encoders;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Tests for the IEncoder and IDecoder class
    /// on complex data types as defined in the
    /// Client.ComplexTypes assembly.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypesEncoderTests : ComplexTypesCommon
    {
        private static readonly JsonSerializerOptions s_ignoreCyclesOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        public IServiceMessageContext EncoderContext;
        public new ITelemetryContext Telemetry;
        public Dictionary<StructureType, (ExpandedNodeId, Type)> TypeDictionary;

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            Telemetry = NUnitTelemetryContext.Create();
            EncoderContext = ServiceMessageContext.Create(Telemetry);
            // add a few random namespaces
            EncoderContext.NamespaceUris.Append("urn:This:is:my:test:encoder");
            EncoderContext.NamespaceUris.Append("urn:This:is:another:namespace");
            EncoderContext.NamespaceUris.Append(Namespaces.OpcUaEncoderTests);
            // create only a single type per structure type, tests can activate
            TypeDictionary = [];
            CreateComplexTypes(EncoderContext, TypeDictionary, string.Empty);
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
        /// Verify encode and decode of a structured type.
        /// </summary>
        [Theory]
        [Category("ComplexTypes")]
        public void ReEncodeComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureType structureType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            ExpandedNodeId nodeId;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[structureType];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            FillStructWithValues(baseType, true, NameSpaceUris);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                structureType,
                nodeId,
                emittedType);
        }

        /// <summary>
        /// Verify encode and decode of a Structure type with optional fields.
        /// Test accessors for all defined properties.
        /// </summary>
        [Theory]
        [Category("ComplexTypes")]
        public void ReEncodeStructureWithOptionalFieldsComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureFieldParameter structureFieldParameter)
        {
            ExpandedNodeId nodeId;
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[StructureType.StructureWithOptionalFields];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            BuiltInType builtInType = structureFieldParameter.BuiltInType;
            TestContext.Out.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} is the only value.");
            baseType[structureFieldParameter.Name] = DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                emittedType);
            TestContext.Out
                .WriteLine($"Optional Field: {structureFieldParameter.BuiltInType} is null.");
            baseType[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                emittedType);
            TestContext.Out.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} is null, all other fields have random values.");
            FillStructWithValues(baseType, true, NameSpaceUris);
            baseType[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                emittedType);
            TestContext.Out.WriteLine(
                $"Optional Field: {structureFieldParameter.BuiltInType} has random value.");
            baseType[structureFieldParameter.Name] = DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.StructureWithOptionalFields,
                nodeId,
                emittedType);
        }

        /// <summary>
        /// Verify encode and decode of a Union type.
        /// Test accessors for all defined properties.
        /// </summary>
        [Theory]
        [Category("ComplexTypes")]
        public void ReEncodeUnionComplexType(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            StructureFieldParameter structureFieldParameter)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            ExpandedNodeId nodeId;
            Type complexType;
            (nodeId, complexType) = TypeDictionary[StructureType.Union];
            object emittedType = Activator.CreateInstance(complexType);
            var baseType = emittedType as BaseComplexType;
            BuiltInType builtInType = structureFieldParameter.BuiltInType;
            TestContext.Out
                .WriteLine($"Union Field: {structureFieldParameter.BuiltInType} is random.");
            baseType[structureFieldParameter.Name] = DataGenerator.GetRandomVariant(structureFieldParameter.BuiltInType, false);
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.Union,
                nodeId,
                emittedType);
            TestContext.Out
                .WriteLine($"Union Field: {structureFieldParameter.BuiltInType} is null.");
            baseType[structureFieldParameter.Name] = default;
            EncodeDecodeComplexType(
                EncoderContext,
                memoryStreamType,
                encoderType,
                jsonEncodingType,
                useXmlParser,
                StructureType.Union,
                nodeId,
                emittedType);
        }

        /// <summary>
        /// Verify serialize/encode of a structured type initiated from outside of an IEncoder instance.
        /// </summary>
        [Theory]
        [Category("ComplexTypes")]
        public void ReEncodeComplexTypeScopedContext(
            MemoryStreamType memoryStreamType,
            StructureType structureType)
        {
            (_, Type complexType) = TypeDictionary[structureType];
            var emittedType = Activator.CreateInstance(complexType) as IEncodeable;
            var baseType = emittedType as BaseComplexType;
            FillStructWithValues(baseType, true, NameSpaceUris);

            var extensionObject = new ExtensionObject(emittedType);

            var keyValuePair = new KeyValuePair
            {
                Key = QualifiedName.From("AKEY"),
                Value = extensionObject
            };

            var localCtxt = (ServiceMessageContext)EncoderContext;

            // Serialize/Encode an ExtensionObject succeeds with a context available
            using (AmbientMessageContext.SetScopedContext(localCtxt))
            {
                _ = JsonSerializer.Serialize(extensionObject, s_ignoreCyclesOptions);
            }

            // Serialize/Encode a Variant succeeds with a context available
            using (AmbientMessageContext.SetScopedContext(localCtxt))
            {
                _ = JsonSerializer.Serialize(keyValuePair, s_ignoreCyclesOptions);
            }
        }
    }
}
