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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Test;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Supported memory stream types.
    /// </summary>
    public enum MemoryStreamType
    {
        MemoryStream,
        ArraySegmentStream,
        RecyclableMemoryStream
    }

    /// <summary>
    /// Base class for the encoder tests.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    public abstract class EncoderCommon
    {
        protected const int kArrayRepeats = 3;
        protected const int kRandomStart = 4840;
        protected const int kRandomRepeats = 100;
        protected const int kMaxArrayLength = 1024 * 64;
        protected const int kTestBlockSize = 0x1000;
        protected const string kApplicationUri = "uri:localhost:opcfoundation.org:EncoderCommon";

        private static readonly JsonSerializerOptions s_prettifyOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true
        };

        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }
        protected IServiceMessageContext Context { get; private set; }
        protected NamespaceTable NameSpaceUris { get; private set; }
        protected StringTable ServerUris { get; private set; }
        protected BufferManager BufferManager { get; private set; }
        protected RecyclableMemoryStreamManager RecyclableMemoryManager { get; private set; }
        protected ITelemetryContext Telemetry { get; private set; }

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            Telemetry = NUnitTelemetryContext.Create();
            Context = new ServiceMessageContext(Telemetry, EncodeableFactory.Create())
            {
                MaxArrayLength = kMaxArrayLength
            };
            NameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            NameSpaceUris.GetIndexOrAppend(kApplicationUri);
            NameSpaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
            ServerUris = new StringTable();
            BufferManager = new BufferManager(nameof(EncoderCommon), kTestBlockSize, Telemetry);
            RecyclableMemoryManager = new RecyclableMemoryStreamManager(
                new RecyclableMemoryStreamManager.Options { BlockSize = kTestBlockSize });
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
            // ensure tests are reproducible, reset for every test
            RandomSource = new RandomSource(kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [TearDown]
        protected void TearDown()
        {
            // ensure after every test that the Null NodeId was not modified
            Assert.That(NodeId.Null.IsNull, Is.True);
        }

        /// <summary>
        /// Ensure repeated tests get different seed.
        /// </summary>
        protected void SetRepeatedRandomSeed()
        {
            int randomSeed = TestContext.CurrentContext.CurrentRepeatCount + kRandomStart;
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            RandomSource = new RandomSource(randomSeed + kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [DatapointSource]
        public static readonly BuiltInType[] BuiltInTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        [
            .. Enum.GetValues<BuiltInType>()
#else
        [
            .. Enum.GetValues(typeof(BuiltInType))
                .Cast<BuiltInType>()
#endif
                .Where(b =>
                    b
                        is not BuiltInType.Variant
                            and not BuiltInType.DiagnosticInfo
                            and not BuiltInType.DataValue
                            and (< BuiltInType.Number or > BuiltInType.UInteger))
        ];

        [DatapointSource]
        public static readonly EncodingType[] EncoderTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
            Enum.GetValues<EncodingType>();
#else
            (EncodingType[])Enum.GetValues(typeof(EncodingType));
#endif

        public static readonly EncodingTypeGroup[] EncodingTypesJson =
        [
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesJsonVerbose =
        [
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesJsonBinaryXmlAndJsonCompact =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml, useXmlParser: false),
            new EncodingTypeGroup(EncodingType.Xml, useXmlParser: true),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesAll =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml, useXmlParser: false),
            new EncodingTypeGroup(EncodingType.Xml, useXmlParser: true),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        /// <summary>
        /// Encode data value and return encoded string.
        /// </summary>
        protected string EncodeDataValue(
            EncodingType encoderType,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            Variant data,
            JsonEncodingType encoding)
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType} Encoding:{encoding}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(data);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);
            Assert.That(expected, Is.Not.Null, "Expected DataValue is Null, " + encodeInfo);
            using MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType);
            using (IEncoder encoder = CreateEncoder(
                encoderType,
                Context,
                encoderStream,
                typeof(DataValue),
                encoding))
            {
                encoder.WriteDataValue("DataValue", expected);
            }
            byte[] buffer = encoderStream.ToArray();
            return Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Encode and decode a DataValue,
        /// validate the result against the input data.
        /// </summary>
        protected void EncodeDecodeDataValue(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            bool useXmlParser,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            Variant data)
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(data);
            Assert.That(expected, Is.Not.Null, "Expected DataValue is Null, " + encodeInfo);

            string formatted = null;
            DataValue result = null;
            try
            {
                byte[] buffer;
                using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
                {
                    using (
                        IEncoder encoder = CreateEncoder(
                            encoderType,
                            Context,
                            encoderStream,
                            typeof(DataValue),
                            jsonEncodingType))
                    {
                        encoder.WriteDataValue("DataValue", expected);
                    }
                    buffer = encoderStream.ToArray();
                }

                switch (encoderType)
                {
                    case EncodingType.Json:
                        formatted = PrettifyAndValidateJson(buffer);
                        break;
                    case EncodingType.Xml:
                        formatted = PrettifyAndValidateXml(buffer);
                        break;
                }

                using (var decoderStream = new MemoryStream(buffer))
                using (IDecoder decoder = CreateDecoder(
                    encoderType,
                    useXmlParser,
                    Context,
                    decoderStream,
                    typeof(DataValue)))
                {
                    result = decoder.ReadDataValue("DataValue");
                }

                Assert.That(result, Is.Not.Null, "Resulting DataValue is Null, " + encodeInfo);
                Assert.That(result, Is.EqualTo(expected), encodeInfo);
                Assert.That(
                    Utils.IsEqual(expected, result),
                    Is.True,
                    "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
            }
            catch
            {
                TestContext.Out.WriteLine("Expected:");
                TestContext.Out.WriteLine(expected);
                if (formatted != null)
                {
                    TestContext.Out.WriteLine("Encoded:");
                    TestContext.Out.WriteLine(formatted);
                }

                TestContext.Out.WriteLine("Result:");
                if (result != null)
                {
                    TestContext.Out.WriteLine(result);
                }
            }
        }

        /// <summary>
        /// Encode and decode Variant, validate result.
        /// </summary>
        protected void EncodeDecode(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            bool useXmlParser,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            Variant expected)
        {
            string formatted = null;
            Variant result = default;
            try
            {
                string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
                IBuiltInType type = TypeInfo.GetSystemType(builtInType);
                TestContext.Out.WriteLine(encodeInfo);

                byte[] buffer;
                using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
                {
                    using (
                        IEncoder encoder = CreateEncoder(
                            encoderType,
                            Context,
                            encoderStream,
                            type?.Type,
                            jsonEncodingType))
                    {
                        encoder.WriteVariantValue(builtInType.ToString(), expected);
                    }
                    buffer = encoderStream.ToArray();
                }

                switch (encoderType)
                {
                    case EncodingType.Json:
                        formatted = PrettifyAndValidateJson(buffer);
                        break;
                    case EncodingType.Xml:
                        formatted = PrettifyAndValidateXml(buffer);
                        break;
                    default:
                        formatted = Encoding.UTF8.GetString(buffer);
                        break;
                }

                using (var decoderStream = new MemoryStream(buffer))
                using (IDecoder decoder = CreateDecoder(
                    encoderType,
                    useXmlParser,
                    Context,
                    decoderStream,
                    type?.Type))
                {
                    result = decoder.ReadVariantValue(
                        builtInType.ToString(),
                        expected.TypeInfo);
                }

                Assert.That(result, Is.EqualTo(expected), encodeInfo);
            }
            catch
            {
                // only print infos if test fails, to reduce log output
                TestContext.Out.WriteLine("Expected:");
                TestContext.Out.WriteLine(expected);
                TestContext.Out.WriteLine("Result:");
                TestContext.Out.WriteLine(result);
                if (formatted != null)
                {
                    TestContext.Out.WriteLine("Encoded:");
                    TestContext.Out.WriteLine(formatted);
                }
                throw;
            }
        }

        /// <summary>
        /// Encode Variant as JSON and validate against expected JSON string.
        /// </summary>
        protected void EncodeJsonVerifyResult(
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            Variant data,
            JsonEncodingType jsonEncoding,
            string expected)
        {
            string result = null;
            string formattedResult = null;
            try
            {
                string encodeInfo = $"Encoder: Json Type:{builtInType} Encoding: {jsonEncoding}";
                TestContext.Out.WriteLine(encodeInfo);
                if (!string.IsNullOrEmpty(expected))
                {
                    expected = $"{{\"{builtInType}\":" + expected + "}";
                }
                else
                {
                    expected = "{}";
                }

                byte[] buffer;
                using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
                {
                    using (
                        IEncoder encoder = CreateEncoder(
                            EncodingType.Json,
                            Context,
                            encoderStream,
                            typeof(DataValue),
                            jsonEncoding))
                    {
                        if (builtInType == BuiltInType.Variant)
                        {
                            encoder.WriteVariant(builtInType.ToString(), data);
                        }
                        else
                        {
                            encoder.WriteVariantValue(builtInType.ToString(), data);
                        }
                    }
                    buffer = encoderStream.ToArray();
                }

                TestContext.Out.WriteLine("Result:");
                result = Encoding.UTF8.GetString(buffer);
                formattedResult = PrettifyAndValidateJson(result);
                var resultParsed = JsonNode.Parse(result,
                    documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
                var expectedParsed = JsonNode.Parse(expected,
                    documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
                bool areEqual = JsonNode.DeepEquals(expectedParsed, resultParsed);
                Assert.That(areEqual, Is.True, encodeInfo);
            }
            catch
            {
                TestContext.Out.WriteLine("Data:");
                TestContext.Out.WriteLine(data);
                TestContext.Out.WriteLine("Expected:");
                string formattedExpected = PrettifyAndValidateJson(expected);
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
                throw;
            }
        }

        /// <summary>
        /// Format and validate a XML document string.
        /// </summary>
        protected string PrettifyAndValidateXml(byte[] xml, bool outputFormatted = false)
        {
            try
            {
                using var reader = new MemoryStream(xml);
                using var xmlReader = XmlReader.Create(reader, Utils.DefaultXmlReaderSettings());
                var document = new XmlDocument();
                document.Load(xmlReader);

                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineOnAttributes = true
                };

                var stringBuilder = new StringBuilder();
                using (var xmlWriter = XmlWriter.Create(stringBuilder, settings))
                {
                    document.Save(xmlWriter);
                }
                string formattedXml = stringBuilder.ToString();
                if (outputFormatted)
                {
                    TestContext.Out.WriteLine(formattedXml);
                }
                return formattedXml;
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(xml);
                Assert.Fail("Invalid xml data: " + ex.Message);
            }
            return Encoding.UTF8.GetString(xml);
        }

        /// <summary>
        /// Format binary data
        /// </summary>
        public static string PrettifyAndValidateBinary(byte[] buffer, bool outputFormatted = false)
        {
            return CoreUtils.ToHexString(buffer);
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        public static string PrettifyAndValidateJson(byte[] json, bool outputFormatted = false)
        {
            return PrettifyAndValidateJson(Encoding.UTF8.GetString(json), outputFormatted);
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        public static string PrettifyAndValidateJson(string json, bool outputFormatted = false)
        {
            try
            {
                var jsonDocument = JsonDocument.Parse(json,
                    new JsonDocumentOptions { AllowTrailingCommas = true });
                string formattedJson = JsonSerializer.Serialize(jsonDocument, s_prettifyOptions);
                if (outputFormatted)
                {
                    TestContext.Out.WriteLine(formattedJson);
                }
                return formattedJson;
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(json);
                Assert.Fail("Invalid json data: " + ex.Message);
            }
            return json;
        }

        /// <summary>
        /// Returns various implementations of a memory stream.
        /// </summary>
        /// <returns>A MemoryStream</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected MemoryStream CreateEncoderMemoryStream(MemoryStreamType memoryStreamType)
        {
            switch (memoryStreamType)
            {
                case MemoryStreamType.MemoryStream:
                    return new MemoryStream(kTestBlockSize);
                case MemoryStreamType.ArraySegmentStream:
                    return new ArraySegmentStream(BufferManager);
                case MemoryStreamType.RecyclableMemoryStream:
                    return new RecyclableMemoryStream(RecyclableMemoryManager);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(memoryStreamType),
                        memoryStreamType,
                        "Invalid MemoryStreamType specified.");
            }
        }

        /// <summary>
        /// Encoder factory for all encoding types.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected IEncoder CreateEncoder(
            EncodingType encoderType,
            IServiceMessageContext context,
            Stream stream,
            Type systemType,
            JsonEncodingType jsonEncoding = JsonEncodingType.Verbose)
        {
            switch (encoderType)
            {
                case EncodingType.Binary:
                    return new BinaryEncoder(stream, context, true);
                case EncodingType.Xml:
                    var xmlWriter = XmlWriter.Create(stream, Utils.DefaultXmlWriterSettings());
                    return new XmlEncoder(systemType, xmlWriter, context);
                case EncodingType.Json:
                    return new JsonEncoder(
                        stream,
                        context,
                        jsonEncoding == JsonEncodingType.Verbose ? JsonEncoderOptions.Verbose : JsonEncoderOptions.Compact);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(encoderType),
                        encoderType,
                        "Invalid EncoderType specified.");
            }
        }

        /// <summary>
        /// Decoder factory for all decoding types.
        /// </summary>
        protected IDecoder CreateDecoder(
            EncodingType decoderType,
            bool useXmlParser,
            IServiceMessageContext context,
            Stream stream,
            Type systemType)
        {
            switch (decoderType)
            {
                case EncodingType.Binary:
                    return new BinaryDecoder(stream, context);
                case EncodingType.Xml when useXmlParser:
                    return new XmlParser(systemType, stream, context);
                case EncodingType.Xml:
                    var xmlReader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                    return new XmlDecoder(systemType, xmlReader, context);
                case EncodingType.Json:
                    return new JsonDecoder(stream, context);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Wrap Variant in a DataValue.
        /// </summary>
        protected DataValue CreateDataValue(Variant variant)
        {
            StatusCode statusCode = DataGenerator.GetRandomStatusCode();
            DateTimeUtc sourceTimeStamp = DataGenerator.GetRandomDateTime();
            return new DataValue(variant, statusCode, sourceTimeStamp, DateTime.UtcNow);
        }

        /// <summary>
        /// Helper to add escaped quotes to a string.
        /// </summary>
        protected static string Quotes(string json)
        {
            return "\"" + json + "\"";
        }

        /// <summary>
        /// Return true if system Type is IEncodeable.
        /// </summary>
        protected static bool IsEncodeableType(Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            System.Reflection.TypeInfo systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                !typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemTypeInfo) ||
                typeof(Ua.Encoders.Structure).IsAssignableFrom(systemType))
            {
                return false;
            }

            return Activator.CreateInstance(systemType) is IEncodeable;
        }

        /// <summary>
        /// Calculates the number of elements from a dimension array.
        /// </summary>
        protected static int ElementsFromDimension(int[] dimensions)
        {
            int elements = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                if (dimensions[i] != 0)
                {
                    elements *= dimensions[i];
                }
            }
            return elements;
        }

        /// <summary>
        /// Sets random array dimensions between 2 and 10.
        /// Number of total elements is limited by <see cref="kMaxArrayLength"/>
        /// </summary>
        protected void SetMatrixDimensions(int[] dimensions)
        {
            int totalElements = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = RandomSource.NextInt32(8) + 2;
                totalElements *= dimensions[i];
            }
            while (totalElements > kMaxArrayLength)
            {
                int random = RandomSource.NextInt32(dimensions.Length - 1);
                if (dimensions[random] > 1)
                {
                    dimensions[random]--;
                }
                totalElements = 1;
                for (int i = 0; i < dimensions.Length; i++)
                {
                    totalElements *= dimensions[i];
                }
            }
        }

        protected enum TestEnumType
        {
            [EnumMember(Value = "One_1")]
            One = 1,

            [EnumMember(Value = "Two_2")]
            Two = 2,

            [EnumMember(Value = "Three_3")]
            Three = 3,

            [EnumMember(Value = "Ten_10")]
            Ten = 10,

            [EnumMember(Value = "Hundred_100")]
            Hundred = 100
        }

        protected class FooBarEncodeable : IEncodeable, IDisposable
        {
            private static int s_count;

            public FooBarEncodeable()
            {
                m_resetCounter = true;
                Count = Interlocked.Increment(ref s_count);
                Foo = $"bar_{Count}";
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(int count)
            {
                Count = count;
                Foo = $"bar_{Count}";
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(string foo)
            {
                Foo = foo;
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(string fieldname, string foo)
            {
                Foo = foo;
                FieldName = fieldname;
            }

            public string Foo { get; set; }
            public string FieldName { get; set; }
            public int Count { get; set; }

            public ExpandedNodeId TypeId { get; }
            public ExpandedNodeId BinaryEncodingId { get; }
            public ExpandedNodeId XmlEncodingId { get; }

            public void Encode(IEncoder encoder)
            {
                encoder.PushNamespace(kApplicationUri);
                encoder.WriteString(FieldName, Foo);
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                decoder.PushNamespace(kApplicationUri);
                Foo = decoder.ReadString(FieldName);
                decoder.PopNamespace();
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                if (encodeable is FooBarEncodeable de)
                {
                    return Foo == de.Foo;
                }

                return false;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing && m_resetCounter)
                {
                    s_count = 0;
                }
                // free unmanaged resources
            }

            public virtual object Clone()
            {
                return MemberwiseClone();
            }

            public new object MemberwiseClone()
            {
                return new FooBarEncodeable(FieldName, Foo) { Count = Count };
            }

            private readonly bool m_resetCounter;
        }

        /// <summary>
        /// A simple dynamic encodeable that can handle arbitrary fields of type string
        /// </summary>
        protected class DynamicEncodeable :
            IEncodeable,
            IDisposable,
            IDynamicComplexTypeInstance
        {
            private static int s_count;

            public DynamicEncodeable()
            {
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    (Dictionary<string, (int, string)>)null)
            {
                m_resetCounter = true;
                Count = Interlocked.Increment(ref s_count);

                m_fields = new Dictionary<string, (int, string)> { { "Foo", (1, $"bar_{Count}") } };
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                int count)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    new Dictionary<string, (int, string)> { { "Foo", (1, $"bar_{count}") } })
            {
                Count = count;
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                string foo)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    new Dictionary<string, (int, string)> { { "Foo", (1, foo) } })
            {
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                Dictionary<string, (int, string)> fields)
            {
                m_xmlName = xmlName;
                m_xmlNamespace = xmlNamespace;
                TypeId = typeId;
                BinaryEncodingId = binaryEncodingId;
                XmlEncodingId = xmlEncodingId;

                m_fields = fields;
            }

            public int Count { get; set; }

            public ExpandedNodeId TypeId { get; set; }
            public ExpandedNodeId BinaryEncodingId { get; set; }
            public ExpandedNodeId XmlEncodingId { get; set; }

            public void Encode(IEncoder encoder)
            {
                InitializeFromFactory(encoder.Context?.Factory);
                encoder.PushNamespace(m_xmlNamespace);
                foreach (
                    KeyValuePair<string, (int FieldOrder, string Value)> field in m_fields
                        .OrderBy(kv => kv.Value.FieldOrder)
                        .ToList())
                {
                    encoder.WriteString(field.Key, field.Value.Value);
                }
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                InitializeFromFactory(decoder.Context?.Factory);
                decoder.PushNamespace(m_xmlNamespace);
                foreach (
                    KeyValuePair<string, (int FieldOrder, string Value)> fieldKV in m_fields
                        .OrderBy(kv => kv.Value.FieldOrder)
                        .ToList())
                {
                    m_fields[fieldKV.Key] = (fieldKV.Value.FieldOrder, decoder.ReadString(
                        fieldKV.Key));
                }
                decoder.PopNamespace();
            }

            private void InitializeFromFactory(IEncodeableFactory factory)
            {
                if (m_fields == null)
                {
                    // When the dynamic encodeable is instantiated by a encoder/decoder,
                    // it needs to find it's type information

                    // Obtain a previously registered instance from the Factory
                    // Other systems will want to put just type information into the factory,
                    // or have other means of finding type information given an encoding id
                    DynamicEncodeable encodeable = factory is DynamicEncodeableFactory df
                        ? df.GetDynamicEncodeableForEncoding(TypeId)
                        : null;
                    // Read the type information
                    TypeId = encodeable?.TypeId ?? default;
                    XmlEncodingId = encodeable?.XmlEncodingId ?? default;
                    BinaryEncodingId = encodeable?.BinaryEncodingId ?? default;
                    Count = encodeable?.Count ?? 0;
                    m_fields = encodeable?.m_fields.ToDictionary(
                        kv => kv.Key,
                        kv => (kv.Value.FieldOrder, (string)null));
                    m_xmlName = encodeable?.m_xmlName;
                    m_xmlNamespace = encodeable?.m_xmlNamespace;
                }
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                if (encodeable is DynamicEncodeable de)
                {
                    return m_fields.OrderBy(kv => kv.Key)
                        .SequenceEqual(de.m_fields.OrderBy(kv => kv.Key));
                }

                return false;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing && m_resetCounter)
                {
                    s_count = 0;
                }
            }

            public virtual object Clone()
            {
                return MemberwiseClone();
            }

            public new object MemberwiseClone()
            {
                return new DynamicEncodeable(
                    m_xmlName,
                    m_xmlNamespace,
                    TypeId,
                    BinaryEncodingId,
                    XmlEncodingId,
                    m_fields.ToDictionary(kv => kv.Key, kv => kv.Value))
                {
                    Count = Count
                };
            }

            public XmlQualifiedName GetXmlName(IServiceMessageContext context)
            {
                InitializeFromFactory(context?.Factory);
                return new XmlQualifiedName(m_xmlName, m_xmlNamespace);
            }

            private Dictionary<string, (int FieldOrder, string Value)> m_fields;
            private string m_xmlName;
            private string m_xmlNamespace;
            private readonly bool m_resetCounter;
        }

        protected class DynamicEncodeableFactory : IEncodeableFactory
        {
            public DynamicEncodeableFactory(IEncodeableFactory factory)
            {
                m_inner = factory;
            }

            public IEnumerable<ExpandedNodeId> KnownTypeIds => m_inner.KnownTypeIds;

            public IEncodeableFactoryBuilder Builder => m_inner.Builder;

            public DynamicEncodeable GetDynamicEncodeableForEncoding(ExpandedNodeId typeId)
            {
                if (!typeId.IsNull &&
                    m_dynamicEncodeables.TryGetValue(
                        typeId,
                        out DynamicEncodeable dynamicEncodeable))
                {
                    return dynamicEncodeable;
                }
                return null;
            }

            public void AddDynamicEncodeable(DynamicEncodeable encodeable)
            {
                m_dynamicEncodeables[encodeable.XmlEncodingId] = encodeable;
                m_dynamicEncodeables[encodeable.BinaryEncodingId] = encodeable;
                m_dynamicEncodeables[encodeable.TypeId] = encodeable;
                m_inner.AddEncodeableType(encodeable.XmlEncodingId, typeof(DynamicEncodeable));
                m_inner.AddEncodeableType(encodeable.BinaryEncodingId, typeof(DynamicEncodeable));
                m_inner.AddEncodeableType(encodeable.TypeId, typeof(DynamicEncodeable));
            }

            public bool TryGetEncodeableType(ExpandedNodeId typeId, [NotNullWhen(true)] out IEncodeableType encodeableType)
            {
                return m_inner.TryGetEncodeableType(typeId, out encodeableType);
            }

            public bool TryGetEnumeratedType(ExpandedNodeId typeId, [NotNullWhen(true)] out IEnumeratedType enumeratedType)
            {
                return m_inner.TryGetEnumeratedType(typeId, out enumeratedType);
            }

            public bool TryGetType(XmlQualifiedName xmlName, [NotNullWhen(true)] out IType type)
            {
                return m_inner.TryGetType(xmlName, out type);
            }

            private readonly IEncodeableFactory m_inner;
            private readonly Dictionary<ExpandedNodeId, DynamicEncodeable> m_dynamicEncodeables = [];
        }
    }
}
