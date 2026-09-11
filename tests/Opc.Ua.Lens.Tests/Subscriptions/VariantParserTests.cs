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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
[SetCulture("de-DE")]
public sealed class VariantParserTests
{
    [TestCaseSource(nameof(ScalarCases))]
    public void ScalarsUseInvariantSyntaxAndTheDeclaredBuiltInType(BuiltInType type, string text, Variant expected)
    {
        foreach (int rank in s_scalarRanks)
        {
            bool success = VariantParser.TryParse(new NodeId((uint)type), rank, $"  {text}  ",
                out Variant actual, out string? error);
            Assert.That(success, Is.True, error);
            Assert.That(error, Is.Null);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(type));
            Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }
    }

    [TestCaseSource(nameof(ArrayCases))]
    public void ArraysPreserveOrderTypeAndTypedEmptyValues(
        BuiltInType type, string text, Variant expected, Variant empty)
    {
        foreach (int rank in s_arrayRanks)
        {
            Assert.That(VariantParser.TryParse(new NodeId((uint)type), rank, $"[{text}]",
                out Variant actual, out string? error), Is.True, error);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(error, Is.Null);
            Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            foreach (string emptyText in s_emptyArrays)
            {
                Assert.That(VariantParser.TryParse(new NodeId((uint)type), rank, emptyText,
                    out Variant emptyResult, out string? emptyError), Is.True, emptyError);
                Assert.That(emptyResult, Is.EqualTo(empty));
                Assert.That(emptyResult.IsNull, Is.False);
                Assert.That(emptyError, Is.Null);
            }
        }
    }

    [TestCase("0x00 ff:1A-02")]
    [TestCase("00FF1a02")]
    [TestCase("AP8aAg==")]
    public void ByteStringsAcceptHexSeparatorsOrBase64WithoutChangingBytes(string text)
    {
        Assert.That(VariantParser.TryParse(DataTypeIds.ByteString, ValueRanks.Scalar, text,
            out Variant actual, out string? error), Is.True, error);
        Assert.That(actual.TryGetValue(out ByteString bytes), Is.True);
        Assert.That(bytes.Memory.ToArray(), Is.EqualTo(s_bytes));
    }

    [TestCase(BuiltInType.Boolean, "1", "Expected 'true' or 'false'.")]
    [TestCase(BuiltInType.SByte, "-129", "Cannot parse")]
    [TestCase(BuiltInType.Byte, "256", "Cannot parse")]
    [TestCase(BuiltInType.Int16, "32768", "Cannot parse")]
    [TestCase(BuiltInType.UInt16, "-1", "Cannot parse")]
    [TestCase(BuiltInType.Int32, "2147483648", "Cannot parse")]
    [TestCase(BuiltInType.UInt32, "4294967296", "Cannot parse")]
    [TestCase(BuiltInType.Int64, "-9223372036854775809", "Cannot parse")]
    [TestCase(BuiltInType.UInt64, "-1", "Cannot parse")]
    [TestCase(BuiltInType.Float, "not a number", "Cannot parse")]
    [TestCase(BuiltInType.Double, "not a number", "Cannot parse")]
    [TestCase(BuiltInType.DateTime, "not a date", "Cannot parse")]
    [TestCase(BuiltInType.Guid, "not a guid", "Cannot parse")]
    [TestCase(BuiltInType.NodeId, "not a node", "Cannot parse")]
    [TestCase(BuiltInType.ByteString, "0xGZ", "Cannot parse")]
    public void InvalidScalarReportsTheFailureWithoutPublishingAPartialValue(
        BuiltInType type, string text, string message)
    {
        Assert.That(VariantParser.TryParse(new NodeId((uint)type), ValueRanks.Scalar, text,
            out Variant actual, out string? error), Is.False);
        Assert.That(actual.IsNull, Is.True);
        Assert.That(error, Does.StartWith(message));
    }

    [TestCase(BuiltInType.Int32, "1,wrong,3")]
    [TestCase(BuiltInType.Boolean, "true,2")]
    [TestCase(BuiltInType.DateTime, "2026-01-02T03:04:05Z,broken")]
    [TestCase(BuiltInType.Guid, "00112233-4455-6677-8899-aabbccddeeff,broken")]
    [TestCase(BuiltInType.NodeId, "i=1,broken")]
    public void InvalidArrayElementRejectsTheWholeValue(BuiltInType type, string text)
    {
        Assert.That(VariantParser.TryParse(new NodeId((uint)type), ValueRanks.OneDimension, text,
            out Variant actual, out string? error), Is.False);
        Assert.That(actual.IsNull, Is.True);
        Assert.That(error, Does.StartWith("Failed to parse array:"));
    }

    [TestCase(BuiltInType.QualifiedName)]
    [TestCase(BuiltInType.LocalizedText)]
    [TestCase(BuiltInType.ByteString)]
    public void UnsupportedArraysDistinguishEmptyConstructionFromNonemptyParsing(BuiltInType type)
    {
        Assert.That(VariantParser.TryParse(new NodeId((uint)type), 1, "value",
            out Variant actual, out string? error), Is.False);
        Assert.That(actual.IsNull, Is.True);
        Assert.That(error, Is.EqualTo($"BuiltInType {type} arrays are not supported by this dialog."));
        Assert.That(VariantParser.TryParse(new NodeId((uint)type), 1, "[]",
            out actual, out error), Is.False);
        Assert.That(actual.IsNull, Is.True);
        Assert.That(error, Is.EqualTo($"Cannot construct empty array of {type}."));
    }

    [Test]
    public void UnknownTypesAndMatrixRanksAreRejectedBeforeParsing()
    {
        AssertRejected(NodeId.Null, -1, "DataType is null.");
        AssertRejected(new NodeId("Int32", 0), -1, "Unsupported DataType: s=Int32");
        AssertRejected(new NodeId(DataTypes.Int32, 2), -1, "Unsupported DataType: ns=2;i=6");
        AssertRejected(DataTypeIds.Structure, -1, $"Unsupported DataType: {DataTypeIds.Structure}");
        AssertRejected(DataTypeIds.Int32, 2, "ValueRank 2 is not supported by this dialog.");
    }

    private static void AssertRejected(NodeId type, int rank, string expectedError)
    {
        Assert.That(VariantParser.TryParse(type, rank, "1", out Variant value, out string? error), Is.False);
        Assert.That(value.IsNull, Is.True);
        Assert.That(error, Is.EqualTo(expectedError));
    }

    private static IEnumerable<TestCaseData> ScalarCases()
    {
        yield return new(BuiltInType.Boolean, "True", Variant.From(true));
        yield return new(BuiltInType.SByte, "-128", Variant.From(sbyte.MinValue));
        yield return new(BuiltInType.Byte, "255", Variant.From(byte.MaxValue));
        yield return new(BuiltInType.Int16, "-32768", Variant.From(short.MinValue));
        yield return new(BuiltInType.UInt16, "65535", Variant.From(ushort.MaxValue));
        yield return new(BuiltInType.Int32, "-2147483648", Variant.From(int.MinValue));
        yield return new(BuiltInType.UInt32, "4294967295", Variant.From(uint.MaxValue));
        yield return new(BuiltInType.Int64, "-9223372036854775808", Variant.From(long.MinValue));
        yield return new(BuiltInType.UInt64, "18446744073709551615", Variant.From(ulong.MaxValue));
        yield return new(BuiltInType.Float, "1.25", Variant.From(1.25f));
        yield return new(BuiltInType.Double, "-2.5", Variant.From(-2.5));
        yield return new(BuiltInType.String, "alpha beta", Variant.From("alpha beta"));
        yield return new(BuiltInType.DateTime, "2026-01-02T03:04:05Z", Variant.From(s_time));
        yield return new(BuiltInType.NodeId, "ns=2;s=Boiler", Variant.From(new NodeId("Boiler", 2)));
        yield return new(BuiltInType.Guid, "00112233-4455-6677-8899-aabbccddeeff", Variant.From(s_uuid));
        yield return new(BuiltInType.QualifiedName, "2:Pressure", Variant.From(new QualifiedName("Pressure", 2)));
        yield return new(BuiltInType.LocalizedText, "Message", Variant.From(new LocalizedText("Message")));
    }

    private static IEnumerable<TestCaseData> ArrayCases()
    {
        yield return new(BuiltInType.Boolean, "true,false",
            Variant.From((ArrayOf<bool>)[true, false]), Variant.From(ArrayOf<bool>.Empty));
        yield return new(BuiltInType.SByte, "-128,127",
            Variant.From((ArrayOf<sbyte>)[-128, 127]), Variant.From(ArrayOf<sbyte>.Empty));
        yield return new(BuiltInType.Byte, "0,255",
            Variant.From((ArrayOf<byte>)[0, 255]), Variant.From(ArrayOf<byte>.Empty));
        yield return new(BuiltInType.Int16, "-32768,32767",
            Variant.From((ArrayOf<short>)[-32768, 32767]), Variant.From(ArrayOf<short>.Empty));
        yield return new(BuiltInType.UInt16, "0,65535",
            Variant.From((ArrayOf<ushort>)[0, 65535]), Variant.From(ArrayOf<ushort>.Empty));
        yield return new(BuiltInType.Int32, "1,\n -2\r3",
            Variant.From((ArrayOf<int>)[1, -2, 3]), Variant.From(ArrayOf<int>.Empty));
        yield return new(BuiltInType.UInt32, "0,4294967295",
            Variant.From((ArrayOf<uint>)[0, uint.MaxValue]), Variant.From(ArrayOf<uint>.Empty));
        yield return new(BuiltInType.Int64, "-9223372036854775808,9223372036854775807",
            Variant.From((ArrayOf<long>)[long.MinValue, long.MaxValue]), Variant.From(ArrayOf<long>.Empty));
        yield return new(BuiltInType.UInt64, "0,18446744073709551615",
            Variant.From((ArrayOf<ulong>)[0, ulong.MaxValue]), Variant.From(ArrayOf<ulong>.Empty));
        yield return new(BuiltInType.Float, "1.25,-2.5",
            Variant.From((ArrayOf<float>)[1.25f, -2.5f]), Variant.From(ArrayOf<float>.Empty));
        yield return new(BuiltInType.Double, "-3.25,5.5",
            Variant.From((ArrayOf<double>)[-3.25, 5.5]), Variant.From(ArrayOf<double>.Empty));
        yield return new(BuiltInType.String, "alpha,beta",
            Variant.From((ArrayOf<string>)["alpha", "beta"]), Variant.From(ArrayOf<string>.Empty));
        yield return new(BuiltInType.DateTime, "2026-01-02T03:04:05Z",
            Variant.From((ArrayOf<DateTimeUtc>)[s_time]), Variant.From(ArrayOf<DateTimeUtc>.Empty));
        yield return new(BuiltInType.NodeId, "ns=2;s=Boiler,i=2258",
            Variant.From((ArrayOf<NodeId>)[new NodeId("Boiler", 2), new NodeId(2258u)]), Variant.From(ArrayOf<NodeId>.Empty));
        yield return new(BuiltInType.Guid, "00112233-4455-6677-8899-aabbccddeeff",
            Variant.From((ArrayOf<Uuid>)[s_uuid]), Variant.From(ArrayOf<Uuid>.Empty));
    }

    private static readonly int[] s_scalarRanks = [ValueRanks.Scalar, ValueRanks.ScalarOrOneDimension, ValueRanks.Any];
    private static readonly int[] s_arrayRanks = [ValueRanks.OneDimension, ValueRanks.OneOrMoreDimensions];
    private static readonly string[] s_emptyArrays = [string.Empty, "[]", " ,\r\n "];
    private static readonly byte[] s_bytes = [0, 255, 26, 2];
    private static readonly Uuid s_uuid = new(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
    private static readonly DateTimeUtc s_time = new(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
}
