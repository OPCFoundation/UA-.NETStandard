/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET10_0
using System;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class OpcUaJsonHelperTests
    {
        [Test]
        public void SerializeUsesCamelCaseAndOmitsNulls()
        {
            string json = OpcUaJsonHelper.Serialize(new
            {
                FirstValue = "abc",
                SecondValue = (string?)null
            });

            Assert.That(json, Does.Contain("firstValue"));
            Assert.That(json, Does.Not.Contain("secondValue"));
        }

        [TestCase("i=85")]
        [TestCase("ns=2;s=MyVariable")]
        [TestCase("ns=1;g=72962b91-fa75-4ae6-8d28-b404dc7daf63")]
        public void ParseNodeIdAcceptsValidStrings(string nodeIdString)
        {
            NodeId nodeId = OpcUaJsonHelper.ParseNodeId(nodeIdString);

            Assert.That(nodeId.IsNull, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ParseNodeIdRejectsNullOrWhitespace(string? nodeIdString)
        {
            Assert.That(
                () => OpcUaJsonHelper.ParseNodeId(nodeIdString!),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ParseExpandedNodeIdAcceptsValidString()
        {
            ExpandedNodeId nodeId = OpcUaJsonHelper.ParseExpandedNodeId("nsu=http://test/;s=MyVariable");

            Assert.That(nodeId.IsNull, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void ParseExpandedNodeIdRejectsNullOrEmpty(string? nodeIdString)
        {
            Assert.That(
                () => OpcUaJsonHelper.ParseExpandedNodeId(nodeIdString!),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ParseQualifiedNameAcceptsNamespaceQualifiedString()
        {
            QualifiedName name = OpcUaJsonHelper.ParseQualifiedName("2:MyName");

            Assert.That(name.Name, Is.EqualTo("MyName"));
            Assert.That(name.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ParseQualifiedNameAcceptsUnqualifiedString()
        {
            QualifiedName name = OpcUaJsonHelper.ParseQualifiedName("MyName");

            Assert.That(name.Name, Is.EqualTo("MyName"));
            Assert.That(name.NamespaceIndex, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public void ParseQualifiedNameRejectsNullOrEmpty(string? qualifiedNameString)
        {
            Assert.That(
                () => OpcUaJsonHelper.ParseQualifiedName(qualifiedNameString!),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void DataValueToDictIncludesValueAndStatus()
        {
            var timestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var dataValue = new DataValue(new Variant(42), StatusCodes.Good, timestamp, timestamp);

            System.Collections.Generic.Dictionary<string, object?> dict =
                OpcUaJsonHelper.DataValueToDict(dataValue);

            Assert.That(dict["value"], Is.EqualTo(42));
            Assert.That(
                dict["sourceTimestamp"],
                Is.EqualTo(timestamp.ToString("o", CultureInfo.InvariantCulture)));
            Assert.That(
                dict["serverTimestamp"],
                Is.EqualTo(timestamp.ToString("o", CultureInfo.InvariantCulture)));
        }

        [Test]
        public void DataValueToDictReturnsNullTimestampsWhenUnset()
        {
            var dataValue = new DataValue(new Variant(1));

            System.Collections.Generic.Dictionary<string, object?> dict =
                OpcUaJsonHelper.DataValueToDict(dataValue);

            Assert.That(dict["sourceTimestamp"], Is.Null);
            Assert.That(dict["serverTimestamp"], Is.Null);
        }

        [Test]
        public void VariantToObjectReturnsNullForNullVariant()
        {
            Assert.That(OpcUaJsonHelper.VariantToObject(Variant.Null), Is.Null);
        }

        [Test]
        public void VariantToObjectReturnsBooleanAsIs()
        {
            Assert.That(OpcUaJsonHelper.VariantToObject(new Variant(true)), Is.True);
        }

        [Test]
        public void VariantToObjectReturnsIntegerAsIs()
        {
            Assert.That(OpcUaJsonHelper.VariantToObject(new Variant(123)), Is.EqualTo(123));
        }

        [Test]
        public void VariantToObjectFormatsScalarDateTimeUsingDateTimeUtcToString()
        {
            // Scalar DateTime values are boxed by Variant.AsBoxedObject() as
            // DateTimeUtc (not System.DateTime), so VariantToObject falls back
            // to the default ToString() branch rather than the ISO-8601 branch.
            var dt = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

            object? result = OpcUaJsonHelper.VariantToObject(new Variant(dt));

            Assert.That(
                result,
                Is.EqualTo(new DateTimeUtc(dt).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void VariantToObjectFormatsByteArrayUsingArrayOfToString()
        {
            // byte[] is implicitly converted to ArrayOf<byte> by the Variant
            // constructor, so AsBoxedObject() returns the ArrayOf<byte> wrapper
            // rather than a raw byte[], and VariantToObject falls back to
            // ArrayOf<T>.ToString() rather than Base64-encoding the bytes.
            byte[] bytes = [1, 2, 3, 4];

            object? result = OpcUaJsonHelper.VariantToObject(new Variant(bytes));

            Assert.That(result, Is.EqualTo(new ArrayOf<byte>(bytes).ToString()));
        }

        [Test]
        public void VariantToObjectConvertsNodeIdToString()
        {
            var nodeId = new NodeId(42, 2);

            object? result = OpcUaJsonHelper.VariantToObject(new Variant(nodeId));

            Assert.That(result, Is.EqualTo(nodeId.ToString()));
        }

        [Test]
        public void VariantToObjectConvertsLocalizedTextToText()
        {
            var text = new LocalizedText("en", "Hello");

            object? result = OpcUaJsonHelper.VariantToObject(new Variant(text));

            Assert.That(result, Is.EqualTo("Hello"));
        }

        [Test]
        public void VariantToObjectConvertsStatusCodeToSymbolicString()
        {
            object? result = OpcUaJsonHelper.VariantToObject(new Variant(StatusCodes.BadNotFound));

            Assert.That(result, Is.EqualTo("BadNotFound"));
        }

        [Test]
        public void VariantToObjectConvertsAdditionalScalarTypes()
        {
            var uuid = new Uuid(Guid.NewGuid());
            var expandedNodeId = new ExpandedNodeId(new NodeId(42, 2));
            var qualifiedName = new QualifiedName("Name", 2);
            var extensionObject = new ExtensionObject(new ReadRawModifiedDetails());

            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant(1.25f)),
                Is.EqualTo(1.25f));
            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant(2.5d)),
                Is.EqualTo(2.5d));
            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant("text")),
                Is.EqualTo("text"));
            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant(uuid)),
                Is.EqualTo(uuid.ToString()));
            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant(expandedNodeId)),
                Is.EqualTo(expandedNodeId.ToString()));
            Assert.That(
                OpcUaJsonHelper.VariantToObject(new Variant(qualifiedName)),
                Is.EqualTo(qualifiedName.ToString()));

            object? extensionResult = OpcUaJsonHelper.VariantToObject(
                new Variant(extensionObject));
            Assert.That(
                extensionResult,
                Is.InstanceOf<System.Collections.Generic.Dictionary<string, object?>>());
        }

        [Test]
        public void VariantToObjectFormatsPrimitiveArrayUsingArrayOfToString()
        {
            // int[] is implicitly converted to ArrayOf<int>, so VariantToObject's
            // "Array array => ArrayToList(array)" branch is not reached; the
            // result falls back to ArrayOf<T>.ToString().
            var values = new int[] { 1, 2, 3 };

            object? result = OpcUaJsonHelper.VariantToObject(new Variant(values));

            Assert.That(result, Is.EqualTo(new ArrayOf<int>(values).ToString()));
        }

        [Test]
        public void StatusCodeToStringReturnsSymbolicId()
        {
            Assert.That(
                OpcUaJsonHelper.StatusCodeToString(StatusCodes.BadNodeIdUnknown),
                Is.EqualTo("BadNodeIdUnknown"));
        }

        [Test]
        public void StatusCodeToStringReturnsGoodForGood()
        {
            // StatusCodeToString returns the SymbolicId, which for Good is
            // the literal string "Good" (not empty) in this codebase.
            Assert.That(OpcUaJsonHelper.StatusCodeToString(StatusCodes.Good), Is.EqualTo("Good"));
        }

        [Test]
        public void ReferenceDescriptionToDictMapsAllFields()
        {
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(new NodeId(1, 0)),
                BrowseName = new QualifiedName("Foo"),
                DisplayName = new LocalizedText("en", "Foo Display"),
                NodeClass = NodeClass.Variable,
                TypeDefinition = new ExpandedNodeId(new NodeId(63, 0)),
                IsForward = true,
                ReferenceTypeId = new NodeId(40, 0)
            };

            System.Collections.Generic.Dictionary<string, object?> dict =
                OpcUaJsonHelper.ReferenceDescriptionToDict(reference);

            Assert.That(dict["displayName"], Is.EqualTo("Foo Display"));
            Assert.That(dict["nodeClass"], Is.EqualTo("Variable"));
            Assert.That(dict["isForward"], Is.True);
            Assert.That(dict["typeDefinition"], Is.Not.Null);
        }

        [Test]
        public void ReferenceDescriptionToDictReturnsNullTypeDefinitionWhenNull()
        {
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(new NodeId(1, 0)),
                BrowseName = new QualifiedName("Foo"),
                DisplayName = new LocalizedText("en", "Foo"),
                NodeClass = NodeClass.Object,
                TypeDefinition = ExpandedNodeId.Null,
                IsForward = false,
                ReferenceTypeId = new NodeId(40, 0)
            };

            System.Collections.Generic.Dictionary<string, object?> dict =
                OpcUaJsonHelper.ReferenceDescriptionToDict(reference);

            Assert.That(dict["typeDefinition"], Is.Null);
        }

        [Test]
        public void ResponseHeaderToDictMapsFields()
        {
            var header = new ResponseHeader
            {
                Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RequestHandle = 7,
                ServiceResult = StatusCodes.Good
            };

            System.Collections.Generic.Dictionary<string, object?> dict =
                OpcUaJsonHelper.ResponseHeaderToDict(header);

            Assert.That(dict["requestHandle"], Is.EqualTo(7u));
            Assert.That(dict["serviceResult"], Is.EqualTo("Good"));
        }

        [Test]
        public void DiagnosticInfoToDictReturnsNullForNullInput()
        {
            Assert.That(OpcUaJsonHelper.DiagnosticInfoToDict(null), Is.Null);
        }

        [Test]
        public void DiagnosticInfoToDictMapsFields()
        {
            var diagnosticInfo = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4,
                AdditionalInfo = "extra",
                InnerStatusCode = StatusCodes.BadTimeout
            };

            System.Collections.Generic.Dictionary<string, object?>? dict =
                OpcUaJsonHelper.DiagnosticInfoToDict(diagnosticInfo);

            Assert.That(dict, Is.Not.Null);
            Assert.That(dict!["additionalInfo"], Is.EqualTo("extra"));
            Assert.That(dict["innerStatusCode"], Is.EqualTo("BadTimeout"));
        }

        [Test]
        public void StatusCodesToStringsReturnsEmptyListForNullArray()
        {
            Assert.That(OpcUaJsonHelper.StatusCodesToStrings(default), Is.Empty);
        }

        [Test]
        public void StatusCodesToStringsConvertsEachEntry()
        {
            ArrayOf<StatusCode> codes =
                new StatusCode[] { StatusCodes.Good, StatusCodes.BadNotFound }.ToArrayOf();

            System.Collections.Generic.List<string> strings = OpcUaJsonHelper.StatusCodesToStrings(codes);

            string[] expected = ["Good", "BadNotFound"];
            Assert.That(strings, Is.EqualTo(expected));
        }

        [Test]
        public void JsonElementToVariantParsesBooleans()
        {
            using JsonDocument doc = JsonDocument.Parse("true");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.True);
        }

        [Test]
        public void JsonElementToVariantParsesTypedInt32()
        {
            using JsonDocument doc = JsonDocument.Parse("42");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement, "Int32");

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(42));
        }

        [Test]
        public void JsonElementToVariantParsesTypedDouble()
        {
            using JsonDocument doc = JsonDocument.Parse("1.5");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement, "Double");

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(1.5));
        }

        [Test]
        public void JsonElementToVariantParsesAllTypedNumericValues()
        {
            Variant uint32Variant = ParseVariant("42", "UInt32");
            Variant int16Variant = ParseVariant("-12", "Int16");
            Variant uint16Variant = ParseVariant("12", "UInt16");
            Variant int64Variant = ParseVariant("-5000000000", "Int64");
            Variant uint64Variant = ParseVariant("5000000000", "UInt64");
            Variant floatVariant = ParseVariant("1.25", "Float");
            Variant byteVariant = ParseVariant("200", "Byte");
            Variant sbyteVariant = ParseVariant("-100", "SByte");
            Variant falseVariant = ParseVariant("false");

            Assert.That(uint32Variant.TryGetValue(out uint uint32Value), Is.True);
            Assert.That(uint32Value, Is.EqualTo(42));
            Assert.That(int16Variant.TryGetValue(out short int16Value), Is.True);
            Assert.That(int16Value, Is.EqualTo(-12));
            Assert.That(uint16Variant.TryGetValue(out ushort uint16Value), Is.True);
            Assert.That(uint16Value, Is.EqualTo(12));
            Assert.That(int64Variant.TryGetValue(out long int64Value), Is.True);
            Assert.That(int64Value, Is.EqualTo(-5000000000));
            Assert.That(uint64Variant.TryGetValue(out ulong uint64Value), Is.True);
            Assert.That(uint64Value, Is.EqualTo(5000000000));
            Assert.That(floatVariant.TryGetValue(out float floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(1.25f));
            Assert.That(byteVariant.TryGetValue(out byte byteValue), Is.True);
            Assert.That(byteValue, Is.EqualTo(200));
            Assert.That(sbyteVariant.TryGetValue(out sbyte sbyteValue), Is.True);
            Assert.That(sbyteValue, Is.EqualTo(-100));
            Assert.That(falseVariant.TryGetValue(out bool falseValue), Is.True);
            Assert.That(falseValue, Is.False);
        }

        [Test]
        public void JsonElementToVariantParsesUntypedNumberAsInt32()
        {
            using JsonDocument doc = JsonDocument.Parse("7");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(7));
        }

        [Test]
        public void JsonElementToVariantParsesLargeNumberAsInt64()
        {
            using JsonDocument doc = JsonDocument.Parse(
                long.MaxValue.ToString(CultureInfo.InvariantCulture));

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void JsonElementToVariantParsesFractionalNumberAsDouble()
        {
            using JsonDocument doc = JsonDocument.Parse("3.14");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(3.14));
        }

        [Test]
        public void JsonElementToVariantParsesTypedDateTime()
        {
            using JsonDocument doc = JsonDocument.Parse("\"2024-01-01T00:00:00Z\"");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement, "DateTime");

            Assert.That(variant.TryGetValue(out DateTimeUtc value), Is.True);
            Assert.That(
                value,
                Is.EqualTo(new DateTimeUtc(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        }

        [Test]
        public void JsonElementToVariantParsesString()
        {
            using JsonDocument doc = JsonDocument.Parse("\"hello\"");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo("hello"));
        }

        [Test]
        public void JsonElementToVariantReturnsNullVariantForJsonNull()
        {
            using JsonDocument doc = JsonDocument.Parse("null");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void JsonElementToVariantFallsBackToRawTextForArrays()
        {
            using JsonDocument doc = JsonDocument.Parse("[1,2,3]");

            Variant variant = OpcUaJsonHelper.JsonElementToVariant(doc.RootElement);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo("[1,2,3]"));
        }

        [Test]
        public void ParseAttributeIdDefaultsToValueWhenNullOrEmpty()
        {
            Assert.That(OpcUaJsonHelper.ParseAttributeId(null), Is.EqualTo(Attributes.Value));
            Assert.That(OpcUaJsonHelper.ParseAttributeId(string.Empty), Is.EqualTo(Attributes.Value));
            Assert.That(OpcUaJsonHelper.ParseAttributeId("   "), Is.EqualTo(Attributes.Value));
        }

        [Test]
        public void ParseAttributeIdAcceptsNumericString()
        {
            Assert.That(OpcUaJsonHelper.ParseAttributeId("13"), Is.EqualTo(13u));
        }

        [TestCase("DisplayName", ExpectedResult = (uint)Attributes.DisplayName)]
        [TestCase("displayname", ExpectedResult = (uint)Attributes.DisplayName)]
        [TestCase("NodeId", ExpectedResult = (uint)Attributes.NodeId)]
        [TestCase("BrowseName", ExpectedResult = (uint)Attributes.BrowseName)]
        [TestCase("NodeClass", ExpectedResult = (uint)Attributes.NodeClass)]
        [TestCase("Description", ExpectedResult = (uint)Attributes.Description)]
        [TestCase("WriteMask", ExpectedResult = (uint)Attributes.WriteMask)]
        [TestCase("UserWriteMask", ExpectedResult = (uint)Attributes.UserWriteMask)]
        [TestCase("IsAbstract", ExpectedResult = (uint)Attributes.IsAbstract)]
        [TestCase("Symmetric", ExpectedResult = (uint)Attributes.Symmetric)]
        [TestCase("InverseName", ExpectedResult = (uint)Attributes.InverseName)]
        [TestCase("ContainsNoLoops", ExpectedResult = (uint)Attributes.ContainsNoLoops)]
        [TestCase("EventNotifier", ExpectedResult = (uint)Attributes.EventNotifier)]
        [TestCase("Value", ExpectedResult = (uint)Attributes.Value)]
        [TestCase("DataType", ExpectedResult = (uint)Attributes.DataType)]
        [TestCase("ValueRank", ExpectedResult = (uint)Attributes.ValueRank)]
        [TestCase("ArrayDimensions", ExpectedResult = (uint)Attributes.ArrayDimensions)]
        [TestCase("AccessLevel", ExpectedResult = (uint)Attributes.AccessLevel)]
        [TestCase("UserAccessLevel", ExpectedResult = (uint)Attributes.UserAccessLevel)]
        [TestCase(
            "MinimumSamplingInterval",
            ExpectedResult = (uint)Attributes.MinimumSamplingInterval)]
        [TestCase("Historizing", ExpectedResult = (uint)Attributes.Historizing)]
        [TestCase("Executable", ExpectedResult = (uint)Attributes.Executable)]
        [TestCase("UserExecutable", ExpectedResult = (uint)Attributes.UserExecutable)]
        [TestCase(
            "DataTypeDefinition",
            ExpectedResult = (uint)Attributes.DataTypeDefinition)]
        [TestCase("RolePermissions", ExpectedResult = (uint)Attributes.RolePermissions)]
        [TestCase(
            "UserRolePermissions",
            ExpectedResult = (uint)Attributes.UserRolePermissions)]
        [TestCase("AccessRestrictions", ExpectedResult = (uint)Attributes.AccessRestrictions)]
        [TestCase("AccessLevelEx", ExpectedResult = (uint)Attributes.AccessLevelEx)]
        public uint ParseAttributeIdAcceptsKnownNames(string name)
        {
            return OpcUaJsonHelper.ParseAttributeId(name);
        }

        [Test]
        public void ParseAttributeIdRejectsUnknownName()
        {
            Assert.That(
                () => OpcUaJsonHelper.ParseAttributeId("NotARealAttribute"),
                Throws.TypeOf<ArgumentException>());
        }

        private static Variant ParseVariant(string json, string? dataType = null)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return OpcUaJsonHelper.JsonElementToVariant(
                document.RootElement,
                dataType);
        }
    }
}
#endif
