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
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the Json encoder and decoder class.
    /// </summary>
    [TestFixture]
    [Category("JsonEncoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderTests : EncoderCommon
    {
        private static readonly TestEnumType[] s_testEnumArray =
        [
            TestEnumType.One,
            TestEnumType.Two,
            TestEnumType.Hundred
        ];

        private static readonly int[] s_testInt32Array = [2, 3, 10];
        private static readonly ExtensionObject s_testEncodeable = new(new FooBarEncodeable(999));

        /// <summary>
        /// Constants used by test data set.
        /// </summary>
        private const ushort kDemoServerIndex = 3;
        private const string kDemoServer = "http://www.opcfoundation.org/DemoServer/";
        private const string kDemoServer2 = "http://www.opcfoundation.org/DemoServer2/";
        private const string kNodeIdString = "theNode";
        private const string kQualifiedName = "theName";
        private const string kLocalizedText = "theText";
        private const string kLocale = "en-us";
        private const int kNodeIdInt = 2345;
        private const long kInt64Value = -123456789123456;
        private const ulong kUInt64Value = 123456789123456;

        private static readonly Uuid s_nodeIdGuid = new(new Guid("AABA0CFA-674F-40C7-B7FA-339D8EECB61D"));
        private static readonly ByteString s_byteString = ByteString.From([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        private static readonly string s_byteString64 = s_byteString.ToBase64();

        /// <summary>
        /// An array of spec compliant Json encoding test data sets which
        /// shall be followed by the JSON encoder accordingly.
        /// </summary>
        [DatapointSource]
        public static readonly JsonValidationData[] Data = new JsonValidationDataCollection
        {
            {
                BuiltInType.Boolean,
                true,
                "true",
                null
            },
            {
                BuiltInType.Boolean,
                false,
                null,
                "false"
            },
            {
                BuiltInType.Byte,
                (byte)0,
                null,
                "0"
            },
            {
                BuiltInType.Byte,
                (byte)88,
                "88",
                null
            },
            {
                BuiltInType.Byte,
                (byte)188,
                "188",
                null
            },
            {
                BuiltInType.Byte,
                byte.MinValue,
                null,
                byte.MinValue.ToString(CultureInfo.InvariantCulture)
            },
            {
                BuiltInType.Byte,
                byte.MaxValue,
                byte.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.SByte,
                (sbyte)0,
                null,
                "0"
            },
            {
                BuiltInType.SByte,
                (sbyte)-77,
                "-77",
                null
            },
            {
                BuiltInType.SByte,
                (sbyte)77,
                "77",
                null
            },
            {
                BuiltInType.SByte,
                sbyte.MaxValue,
                sbyte.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.SByte,
                sbyte.MinValue,
                sbyte.MinValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.UInt16,
                (ushort)0,
                null,
                "0"
            },
            {
                BuiltInType.UInt16,
                (ushort)12345,
                "12345",
                null
            },
            {
                BuiltInType.UInt16,
                (ushort)44444,
                "44444",
                null
            },
            {
                BuiltInType.UInt16,
                ushort.MinValue,
                null,
                ushort.MinValue.ToString(CultureInfo.InvariantCulture)
            },
            {
                BuiltInType.UInt16,
                ushort.MaxValue,
                ushort.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Int16,
                (short)0,
                null,
                "0"
            },
            {
                BuiltInType.Int16,
                (short)-12345,
                "-12345",
                null
            },
            {
                BuiltInType.Int16,
                (short)12345,
                "12345",
                null
            },
            {
                BuiltInType.Int16,
                short.MaxValue,
                short.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Int16,
                short.MinValue,
                short.MinValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.UInt32,
                (uint)0,
                null,
                "0"
            },
            {
                BuiltInType.UInt32,
                (uint)1234567,
                "1234567",
                null
            },
            {
                BuiltInType.UInt32,
                (uint)4444444,
                "4444444",
                null
            },
            {
                BuiltInType.UInt32,
                uint.MinValue,
                null,
                uint.MinValue.ToString(CultureInfo.InvariantCulture)
            },
            {
                BuiltInType.UInt32,
                uint.MaxValue,
                uint.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Int32,
                0,
                null,
                "0"
            },
            {
                BuiltInType.Int32,
                -12345678,
                "-12345678",
                null
            },
            {
                BuiltInType.Int32,
                12345678,
                "12345678",
                null
            },
            {
                BuiltInType.Int32,
                int.MaxValue,
                int.MaxValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Int32,
                int.MinValue,
                int.MinValue.ToString(CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Int64,
                (long)0,
                null,
                Quotes("0")
            },
            {
                BuiltInType.Int64,
                kInt64Value,
                Quotes(kInt64Value.ToString(CultureInfo.InvariantCulture)),
                null },
            {
                BuiltInType.Int64,
                (long)kUInt64Value,
                Quotes(kUInt64Value.ToString(CultureInfo.InvariantCulture)),
                null
            },
            {
                BuiltInType.Int64,
                long.MinValue,
                Quotes(long.MinValue.ToString(CultureInfo.InvariantCulture)),
                null
            },
            {
                BuiltInType.Int64,
                long.MaxValue,
                Quotes(long.MaxValue.ToString(CultureInfo.InvariantCulture)),
                null
            },
            {
                BuiltInType.UInt64,
                (ulong)0,
                null,
                Quotes("0")
            },
            {
                BuiltInType.UInt64,
                kUInt64Value,
                Quotes(kUInt64Value.ToString(CultureInfo.InvariantCulture)),
                null },
            {
                BuiltInType.UInt64,
                ulong.MinValue,
                null,
                Quotes(ulong.MinValue.ToString(CultureInfo.InvariantCulture))
            },
            {
                BuiltInType.UInt64,
                ulong.MaxValue,
                Quotes(ulong.MaxValue.ToString(CultureInfo.InvariantCulture)),
                null
            },
            {
                BuiltInType.Float,
                (float)0,
                null,
                "0"
            },
            {
                BuiltInType.Float,
                (float)-12345678.1234,
                Convert
                    .ToSingle("-12345678.1234", CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Float,
                (float)12345678.1234,
                Convert
                    .ToSingle("12345678.1234", CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Float,
                float.MaxValue,
                float.MaxValue.ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Float,
                float.MinValue,
                float.MinValue.ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Float,
                float.NegativeInfinity,
                Quotes("-Infinity"),
                null
            },
            {
                BuiltInType.Float,
                float.PositiveInfinity,
                Quotes("Infinity"),
                null
            },
            {
                BuiltInType.Float,
                float.NaN,
                Quotes("NaN"),
                null
            },
            {
                BuiltInType.Double,
                (double)0,
                null,
                "0"
            },
            {
                BuiltInType.Double,
                -12345678.1234,
                Convert
                    .ToDouble("-12345678.1234", CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Double,
                12345678.1234,
                Convert
                    .ToDouble("12345678.1234", CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Double,
                double.MaxValue,
                double.MaxValue.ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Double,
                double.MinValue,
                double.MinValue.ToString("R", CultureInfo.InvariantCulture),
                null
            },
            {
                BuiltInType.Double,
                double.NegativeInfinity,
                Quotes("-Infinity"),
                null
            },
            {
                BuiltInType.Double,
                double.PositiveInfinity,
                Quotes("Infinity"),
                null
            },
            {
                BuiltInType.Double,
                double.NaN,
                Quotes("NaN"),
                null
            },
            {
                BuiltInType.DateTime,
                DateTimeUtc.MinValue + 1,
                Quotes("1601-01-01T00:00:00.001Z"),
                null
            },
            {
                BuiltInType.DateTime,
                DateTimeUtc.MinValue,
                null,
                Quotes("0001-01-01T00:00:00Z")
            },
            {
                BuiltInType.DateTime,
                DateTimeUtc.MaxValue,
                Quotes("9999-12-31T23:59:59Z"),
                null
            },
            {
                BuiltInType.Guid,
                Uuid.Empty,
                null,
                "null" // Quotes("00000000-0000-0000-0000-000000000000")
            },
            {
                BuiltInType.Guid,
                s_nodeIdGuid,
                Quotes($"{s_nodeIdGuid}"),
                null
            },
            {
                BuiltInType.NodeId,
                NodeId.Null,
                null,
                "null"
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt),
                $"""
                "i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, 1),
                $"""
                "nsu={kApplicationUri};i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, 88),
                $"""
                "ns=88;i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                NodeId.Parse("ns=0;s=" + kNodeIdString),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                NodeId.Parse("s=" + kNodeIdString),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 0),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 1),
                $"""
                "nsu={kApplicationUri};s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 88),
                $"""
                "ns=88;s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid),
                $"""
                "g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, 1),
                $"""
                "nsu={kApplicationUri};g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, 88),
                $"""
                "ns=88;g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString),
                $"""
                "b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, 1),
                $"""
                "nsu={kApplicationUri};b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, 88),
                $"""
                "ns=88;b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                ExpandedNodeId.Null,
                null,
                "null"
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt),
                $"""
                "i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, 1),
                $"""
                "nsu={kApplicationUri};i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, kDemoServer2),
                $"""
                "nsu={kDemoServer2};i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, 88),
                $"""
                "ns=88;i={kNodeIdInt}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                ExpandedNodeId.Parse("ns=0;s=" + kNodeIdString),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                ExpandedNodeId.Parse("s=" + kNodeIdString),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 0),
                $"""
                "s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 1),
                $"""
                "nsu={kApplicationUri};s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, kDemoServer2),
                $"""
                "nsu={kDemoServer2};s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 88),
                $"""
                "ns=88;s={kNodeIdString}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid),
                $"""
                "g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, 1),
                $"""
                "nsu={kApplicationUri};g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, kDemoServer2),
                $"""
                "nsu={kDemoServer2};g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, 88),
                $"""
                "ns=88;g={s_nodeIdGuid}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString),
                $"""
                "b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, 1),
                $"""
                "nsu={kApplicationUri};b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, kDemoServer2),
                $"""
                "nsu={kDemoServer2};b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, 88),
                $"""
                "ns=88;b={s_byteString64}"
                """,
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(NodeId.Parse("ns=33;s=StringIdentifier"), null, 23),
                """
                "svr=23;ns=33;s=StringIdentifier"
                """, // compact
                null // verbose - null == same as compact
            },
            {
                BuiltInType.StatusCode,
                StatusCodes.Good,
                null,
                "{}"
            },
            {
                BuiltInType.StatusCode,
                StatusCodes.BadBoundNotFound,
                $$"""{"Code":{{StatusCodes.BadBoundNotFound.Code}}}""",
                $$"""{"Code":{{StatusCodes.BadBoundNotFound.Code}}, "Symbol":"{{nameof(StatusCodes.BadBoundNotFound)}}"}"""
            },
            {
                BuiltInType.StatusCode,
                StatusCodes.BadCertificateInvalid,
                $$"""{"Code":{{StatusCodes.BadCertificateInvalid.Code}}}""",
                $$"""{"Code":{{StatusCodes.BadCertificateInvalid.Code}}, "Symbol":"{{nameof(StatusCodes.BadCertificateInvalid)}}"}"""
            },
            {
                BuiltInType.StatusCode,
                new StatusCode(1234567),
                /*lang=json,strict*/ """{"Code":1234567}""",
                null
            },
            {
                BuiltInType.QualifiedName,
                QualifiedName.Null,
                null,
                "null"
            },
            {
                BuiltInType.QualifiedName,
                QualifiedName.From(kQualifiedName),
                $"""
                "{kQualifiedName}"
                """,
                null
            },
            {
                BuiltInType.QualifiedName,
                new QualifiedName(kQualifiedName, 1),
                $"""
                "nsu={kApplicationUri};{kQualifiedName}"
                """,
                null
            },
            {
                BuiltInType.QualifiedName,
                new QualifiedName(kQualifiedName, kDemoServerIndex),
                $"""
                "nsu={kDemoServer};{kQualifiedName}"
                """,
                null
            },
            {
                BuiltInType.LocalizedText,
                LocalizedText.Null,
                null,
                "null"
            },
            {
                BuiltInType.LocalizedText,
                new LocalizedText(kLocalizedText),
                $$"""{"Text":"{{kLocalizedText}}"}""",
                null
            },
            {
                BuiltInType.LocalizedText,
                new LocalizedText(kLocale, kLocalizedText),
                $$"""{"Text":"{{kLocalizedText}}","Locale":"{{kLocale}}"}""",
                null
            },
            {
                BuiltInType.ExtensionObject,
                ExtensionObject.Null,
                null,
                "null"
            },
            {
                BuiltInType.ExtensionObject,
                new ExtensionObject(kNodeIdInt),
                /*lang=json,strict*/ """{"UaTypeId":"i=2345"}""",
                null
            },
            {
                BuiltInType.ExtensionObject,
                new ExtensionObject(null),
                null,
                "null"
            },
            {
                BuiltInType.Variant,
                Variant.Null,
                null,
                "null"
            },
            {
                BuiltInType.Variant,
                new Variant((sbyte)123),
                $$"""{"UaType":{{BuiltInType.SByte:d}}, "Value":123}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((short)12345),
                $$"""{"UaType":{{BuiltInType.Int16:d}}, "Value":12345}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant(1234567),
                $$"""{"UaType":{{BuiltInType.Int32:d}}, "Value":1234567}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((long)123456789),
                $$"""{"UaType":{{BuiltInType.Int64:d}}, "Value":"123456789"}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((byte)123),
                $$"""{"UaType":{{BuiltInType.Byte:d}}, "Value":123}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((ushort)12345),
                $$"""{"UaType":{{BuiltInType.UInt16:d}}, "Value":12345}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((uint)1234567),
                $$"""{"UaType":{{BuiltInType.UInt32:d}}, "Value":1234567}""",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((ulong)123456789),
                $$"""{"UaType":{{BuiltInType.UInt64:d}}, "Value":"123456789"}""",
                null
            },
            {
                BuiltInType.DataValue,
                new DataValue(),
                "{}",
                null
            },
            {
                BuiltInType.DataValue,
                new DataValue(StatusCodes.Good),
                "{}",
                null
            },
            {
                BuiltInType.DataValue,
                new DataValue(StatusCodes.BadNotWritable),
                $$$"""{"StatusCode":{"Code":{{{StatusCodes.BadNotWritable.Code}}}}}""",
                $$$"""{"StatusCode":{"Code":{{{StatusCodes.BadNotWritable.Code}}}, "Symbol":"{{{nameof(StatusCodes.BadNotWritable)}}}"}}"""
            },
            {
                BuiltInType.Enumeration,
                Variant.From((TestEnumType)0),
                null, // "0",
                """
                "0"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(TestEnumType.Three),
                """
                3
                """,
                """
                "Three_3"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(TestEnumType.Ten),
                """
                10
                """,
                """
                "Ten_10"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From((TestEnumType)11),
                "11",
                """
                "11"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(new EnumValue(1)),
                "1",
                """
                "1"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(EnumValue.From((int)TestEnumType.Two)),
                TestEnumType.Two.ToString("d"),
                $"""
                "{TestEnumType.Two:d}"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(EnumValue.From((int)TestEnumType.Hundred)),
                $"{TestEnumType.Hundred:d}",
                $"""
                "{TestEnumType.Hundred:d}"
                """
            },
            {
                BuiltInType.Enumeration,
                Variant.From(EnumValue.From(22)),
                "22",
                """
                "22"
                """
            },
            // arrays
            {
                BuiltInType.Enumeration,
                Variant.From(s_testEnumArray),
                "[1,2,100]",
                """["One_1","Two_2","Hundred_100"]"""
            },
            {
                BuiltInType.Enumeration,
                Variant.From(EnumValue.From(s_testInt32Array)),
                "[2,3,10]",
                """["2","3","10"]"""
            },
            // IEncodeable
            {
                BuiltInType.ExtensionObject,
                s_testEncodeable,
                /*lang=json,strict*/
                """{"Foo":"bar_999"}""",
                null
            }
        }.ToArray();

        [DatapointSource]
        public static readonly StatusCode[] GoodAndBadStatusCodes =
        [
            StatusCodes.Good,
            StatusCodes.BadAlreadyExists
        ];

        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            ushort demoServerIndex = NameSpaceUris.GetIndexOrAppend(kDemoServer);
            Assume.That(
                demoServerIndex == kDemoServerIndex,
                $"Server Index: {demoServerIndex} != {kDemoServerIndex}");

            // for validating benchmark tests
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = Ua.ServiceMessageContext.Create(m_telemetry);
            m_memoryStream = new MemoryStream();
        }

        [OneTimeTearDown]
        protected new void OneTimeTearDown()
        {
            m_memoryStream.Dispose();
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
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = Ua.ServiceMessageContext.Create(m_telemetry);
            m_memoryStream = new MemoryStream();
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_context = null;
            m_memoryStream.Dispose();
            m_memoryStream = null;
        }

        /// <summary>
        /// Validate constructor signature.
        /// </summary>
        [Theory]
        public void ConstructorDefault(JsonEncodingType encodingType)
        {
            var context = Ua.ServiceMessageContext.Create(m_telemetry);
            using var jsonEncoder = new JsonEncoder(context,
                encodingType == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            TestEncoding(jsonEncoder);
            string result = jsonEncoder.CloseAndReturnText();

            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Is.Not.Null);
            TestContext.Out.WriteLine("Result:");
            _ = PrettifyAndValidateJson(result);
        }

        /// <summary>
        /// Use a MemoryStream constructor with external Stream,
        /// keep the stream open for more encodings.
        /// </summary>
        [Test]
        public void ConstructorMemoryStream()
        {
            using var memoryStream = new MemoryStream();
            ConstructorStream(memoryStream);
        }

        /// <summary>
        /// Use a ArraySegmentStream constructor with external Stream,
        /// keep the stream open for more encodings.
        /// </summary>
        [Test]
        public void ConstructorArraySegmentStream()
        {
            using var memoryStream = new ArraySegmentStream(BufferManager);
            ConstructorStream(memoryStream);
        }

        /// <summary>
        /// Use a RecylableMemoryStream constructor with external Stream,
        /// keep the stream open for more encodings.
        /// </summary>
        [Test]
        public void ConstructorRecyclableMemoryStream()
        {
            var recyclableMemoryStreamManager = new RecyclableMemoryStreamManager(
                new RecyclableMemoryStreamManager.Options
                {
                    BlockSize = BufferManager.MaxSuggestedBufferSize
                });
            using var memoryStream = new RecyclableMemoryStream(recyclableMemoryStreamManager);
            ConstructorStream(memoryStream);
        }

        /// <summary>
        /// Use a constructor with external Stream,
        /// keep the stream open for more encodings.
        /// </summary>
        private void ConstructorStream(MemoryStream memoryStream)
        {
            var context = Ua.ServiceMessageContext.Create(m_telemetry);
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }
            string result1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result1, Is.Not.Empty);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);

            // recycle the StreamWriter, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result2, Is.Not.Empty);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.That(result2, Is.EqualTo(result1));

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.That(result3, Is.Not.Empty);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.That(result3, Is.EqualTo(result1));
            }
        }

        /// <summary>
        /// Use a constructor with external ArraySegmentStream,
        /// keep the stream open for more encodings.
        /// Alternate use of sequence.
        /// </summary>
        [Test]
        public void ConstructorArraySegmentStreamSequence()
        {
            var context = Ua.ServiceMessageContext.Create(m_telemetry);
            using var memoryStream = new ArraySegmentStream(BufferManager);
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }

            // get the buffers and save the result
#if NET5_0_OR_GREATER
            string result1;
            using (BufferSequence sequence = memoryStream.GetSequence(nameof(ConstructorStream)))
            {
                result1 = Encoding.UTF8.GetString(sequence.Sequence);
                Assert.IsNotEmpty(result1);
                TestContext.Out.WriteLine("Result1:");
                _ = PrettifyAndValidateJson(result1);
            }
#else
            string result1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result1, Is.Not.Empty);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);
#endif

            // recycle the memory stream, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result2, Is.Not.Empty);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.That(result2, Is.EqualTo(result1));

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(memoryStream, context))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.That(result3, Is.Not.Empty);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.That(result3, Is.EqualTo(result1));
            }
        }

        /// <summary>
        /// Use a constructor with external RecyclableMemoryStream,
        /// keep the stream open for more encodings.
        /// Alternate use of sequence.
        /// </summary>
        [Test]
        public void ConstructorRecyclableMemoryStreamSequence()
        {
            var context = Ua.ServiceMessageContext.Create(m_telemetry);
            using var memoryStream = new RecyclableMemoryStream(RecyclableMemoryManager);
            using (var jsonEncoder = new JsonEncoder((Stream)memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }

            // get the buffers and save the result
#if NET5_0_OR_GREATER
            string result1;
            {
                System.Buffers.ReadOnlySequence<byte> sequence = memoryStream.GetReadOnlySequence();
                result1 = Encoding.UTF8.GetString(in sequence);
                Assert.IsNotEmpty(result1);
                TestContext.Out.WriteLine("Result1:");
                _ = PrettifyAndValidateJson(result1);
            }
#else
            string result1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result1, Is.Not.Empty);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);
#endif

            // recycle the memory stream, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder((Stream)memoryStream, context))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.That(result2, Is.Not.Empty);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.That(result2, Is.EqualTo(result1));

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder((Stream)memoryStream, context))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.That(result3, Is.Not.Empty);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.That(result3, Is.EqualTo(result1));
            }
        }

        /// <summary>
        /// Verify any Json encoding.
        /// </summary>
        [Theory]
        public void JsonEncode(
            JsonEncodingType jsonEncodingType,
            JsonValidationData jsonValidationData,
            MemoryStreamType memoryStreamType)
        {
            EncodeJsonVerifyResult(
                jsonValidationData.BuiltInType,
                memoryStreamType,
                jsonValidationData.Instance,
                jsonEncodingType,
                jsonValidationData.GetExpected(jsonEncodingType));
        }

        /// <summary>
        /// Within an object JSON don't allow another object without fieldname.
        /// </summary>
        [Test]
        public void TestWriteSingleEncodeableWithoutName()
        {
            string expected = /*lang=json,strict*/ """{"Test":{"Foo":"bar_1"}}""";
            TestContext.Out.WriteLine("Expected:");
            expected = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context);
            encoder.WriteEncodeable("Test", encodeable);

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            encoded = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void TestWriteSingleEncodeableWithName()
        {
            const string expected = /*lang=json,strict*/
                """{"bar_1":{"Foo":"bar_1"}}""";
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context);
            encoder.WriteEncodeable(encodeable.Foo, encodeable);

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// A single dynamic encodeable
        /// </summary>
        [Test]
        public void TestWriteSingleDynamicEncodeableWithName()
        {
            const string expected = /*lang=json,strict*/
                """{"bar_1":{"Foo":"bar_1"}}""";
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new DynamicEncodeable(
                "FooXml",
                "urn:dynamic_encoder_test",
                ExpandedNodeId.Parse("ns=2;s=test_dyn_typeid"),
                ExpandedNodeId.Parse("s=test_dyn_binaryencodingid"),
                ExpandedNodeId.Parse("s=test_dyn_xmlencodingid"),
                new Dictionary<string, (int, string)>
                {
                    { "Foo", (1, "bar_1") }
                });
            using var encoder = new JsonEncoder(Context);
            encoder.WriteEncodeable("bar_1", encodeable);

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Extension object with dynamic encodeable encoded to and from Json and xml
        /// </summary>
        [Test]
        public void TestExtensionObjectWithDynamicEncodeable()
        {
            const string expectedJson = /*lang=json,strict*/
                """{"Test":{"UaTypeId":"s=test_dyn2_typeid","Foo":"bar_1","Foo2":"bar_2"}}""";
            string expectedXml =
                """
                <uax:ExtensionObject xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                    <uax:TypeId><uax:Identifier>s=test_dyn2_xmlencodingid</uax:Identifier></uax:TypeId>
                    <uax:Body>
                        <FooXml xmlns="urn:dynamic_encoder_test">
                            <Foo>bar_1</Foo>
                            <Foo2>bar_2</Foo2>
                        </FooXml>
                    </uax:Body>
                </uax:ExtensionObject>
                """;
            TestContext.Out.WriteLine("Expected XML:");
            expectedXml = PrettifyAndValidateXml(Encoding.UTF8.GetBytes(expectedXml));

            var encodeable = new DynamicEncodeable(
                "FooXml",
                "urn:dynamic_encoder_test",
                ExpandedNodeId.Parse("s=test_dyn2_typeid"),
                ExpandedNodeId.Parse("s=test_dyn2_binaryencodingid"),
                ExpandedNodeId.Parse("s=test_dyn2_xmlencodingid"),
                new Dictionary<string, (int, string)>
                {
                    { "Foo", (1, "bar_1") },
                    { "Foo2", (2, "bar_2") }
                });

            // Register in the context's Factory, make it a custom factory so the dynamic type can
            // look up its type information when instantiated during encoding/decoding
            var dynamicContext = new ServiceMessageContext(
                m_telemetry,
                new DynamicEncodeableFactory(Context.Factory))
            {
                NamespaceUris = Context.NamespaceUris
            };
            (dynamicContext.Factory as DynamicEncodeableFactory)?.AddDynamicEncodeable(encodeable);

            // Encode to XML: invokes IDynamicComplexTypeInstance.GetXmlName
            string encodedXml;
            using (var ms = new MemoryStream())
            using (var xmlWriter = new XmlTextWriter(ms, Encoding.UTF8))
            {
                using (
                    var encoder = new XmlEncoder(
                        new XmlQualifiedName("uax:ExtensionObject", null),
                        xmlWriter,
                        Context))
                {
                    var extensionObject = new ExtensionObject(encodeable);
                    encoder.WriteExtensionObject(null, extensionObject);
                    xmlWriter.Flush();
                }
                TestContext.Out.WriteLine("Formatted Encoded:");
                encodedXml = PrettifyAndValidateXml(ms.ToArray());
            }
            Assert.That(encodedXml, Is.EqualTo(expectedXml));

            // Decode from XML
            ExtensionObject extensionObjectFromXml;
            using (var ms2 = new MemoryStream(Encoding.UTF8.GetBytes(expectedXml)))
            {
                var xmlDoc = new XmlDocument();
                var r = XmlReader.Create(
                    ms2,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                xmlDoc.Load(r);

                using var decoder = new XmlDecoder(xmlDoc.FirstChild as System.Xml.XmlElement, dynamicContext);
                decoder.PushNamespace(Namespaces.OpcUaXsd);
                extensionObjectFromXml = decoder.ReadExtensionObject("ExtensionObject");
                decoder.PopNamespace();
            }
            Assert.That(
                extensionObjectFromXml.TryGetEncodeable(out IEncodeable resultEncodeable), Is.True);
            Assert.That(encodeable.IsEqual(resultEncodeable), Is.True);

            // Encode to JSON
            string encodedJson;
            using (var encoder = new JsonEncoder(Context))
            {
                encoder.WriteExtensionObject("Test", extensionObjectFromXml);

                encodedJson = encoder.CloseAndReturnText();

                TestContext.Out.WriteLine("Expected Json:");
                _ = PrettifyAndValidateJson(expectedJson);

                TestContext.Out.WriteLine("Encoded Json:");
                TestContext.Out.WriteLine(encodedJson);

                TestContext.Out.WriteLine("Formatted Encoded Json:");
                _ = PrettifyAndValidateJson(encodedJson);
            }
            Assert.That(encodedJson, Is.EqualTo(expectedJson));

            // Decode from JSON: requires custom context
            ExtensionObject extensionObjectFromJson;
            using (var decoder = new JsonDecoder(encodedJson, dynamicContext))
            {
                extensionObjectFromJson = decoder.ReadExtensionObject("Test");
            }
            Assert.That(
                extensionObjectFromJson.TryGetEncodeable(out resultEncodeable), Is.True);
            Assert.That(encodeable.IsEqual(resultEncodeable), Is.True);
        }

        /// <summary>
        /// Write multiple encodeables with fieldnames.
        /// </summary>
        [Test]
        public void TestWriteMultipleEncodeablesWithFieldNames()
        {
            const string expected = /*lang=json,strict*/
                """{"bar_1":{"Foo":"bar_1"},"bar_2":{"Foo":"bar_2"},"bar_3":{"Foo":"bar_3"}}""";

            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };
            try
            {
                using var encoder = new JsonEncoder(Context);
                foreach (FooBarEncodeable encodeable in encodeables)
                {
                    encoder.WriteEncodeable(encodeable.Foo, encodeable);
                }

                string encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
            finally
            {
                encodeables.ForEach(e => e.Dispose());
            }
        }

        /// <summary>
        /// Write encodeable array with fieldname.
        /// </summary>
        [Test]
        public void TestWriteEncodeableArrayWithFieldName()
        {
            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };

            RunWriteEncodeableArrayTest(
                "array",
                encodeables,
                /*lang=json,strict*/
                """{"array":[{"Foo":"bar_1"},{"Foo":"bar_2"},{"Foo":"bar_3"}]}""",
                false);
        }

        /// <summary>
        /// Test if field names and values are properly escaped.
        /// </summary>
        [TestCase(
            """
            "Hello"."World"
            """,
            """
            "Test"."Output"
            """,
            /*lang=json,strict*/
            """{"\"Hello\".\"World\"":{"\"Hello\".\"World\"":"\"Test\".\"Output\""}}"""
        )]
        [TestCase(
            "\"Hello\".\"World\"\b\f\n\r\t\\",
            "\"Test\b\f\n\r\t\\\".\"Output\"",
            /*lang=json,strict*/
            """{"\"Hello\".\"World\"\b\f\n\r\t\\":{"\"Hello\".\"World\"\b\f\n\r\t\\":"\"Test\b\f\n\r\t\\\".\"Output\""}}"""
        )]
        public void TestFieldValueEscapedEncodeable(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            expected = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            using var encoder = new JsonEncoder(Context, JsonEncoderOptions.Verbose);
            encoder.WriteEncodeable(encodeable.FieldName, encodeable);

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            encoded = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Test if field names and values are properly escaped when used in an array.
        /// </summary>
        [TestCase(
            """
                "Hello"."World"
                """,
            """
                "Test"."Output"
                """,
            /*lang=json,strict*/"""{"\"Hello\".\"World\"":[{"\"Hello\".\"World\"":"\"Test\".\"Output\""},{"\"Hello\".\"World\"":"\"Test\".\"Output\""}]}"""
        )]
        public void TestFieldValueEscapedArray(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            expected = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            ArrayOf<FooBarEncodeable> list = [encodeable, encodeable];
            using var encoder = new JsonEncoder(Context, JsonEncoderOptions.Verbose);
            encoder.WriteEncodeableArray(encodeable.FieldName, list);

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            encoded = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Test if field names and values are properly escaped when used in a variant.
        /// </summary>
        [TestCase(
            """
                "Hello"."World"
                """,
            """
                "Test"."Output"
                """,
            /*lang=json,strict*/"""{"\"Hello\".\"World\"":{"UaType":22,"Value":{"\"Hello\".\"World\"":"\"Test\".\"Output\""}}}"""
        )]
        public void TestFieldValueEscapedVariant(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            expected = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            var variant = new Variant(new ExtensionObject(encodeable));
            using var encoder = new JsonEncoder(Context, JsonEncoderOptions.Verbose);
            encoder.WriteVariant(encodeable.FieldName, variant);

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            encoded = PrettifyAndValidateJson(encoded);

            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Validate that the DateTime format strings return an equal result.
        /// </summary>
        [Test]
        [Repeat(kRandomRepeats)]
        public void DateTimeEncodeRandomStringTest()
        {
            SetRepeatedRandomSeed();
            DateTimeUtc randomDateTime = DataGenerator.GetRandomDateTime();
            DateTimeEncodeStringTest(randomDateTime);
        }

        /// <summary>
        /// Validate that the DateTime format strings return an equal result.
        /// </summary>
        [Test]
        [TestCase("1952-12-14T17:48:51.3559888Z")]
        [TestCase("1952-12-14T17:48:51.3559880Z")]
        [TestCase("1952-12-14T17:48:51.3559800Z")]
        [TestCase("1952-12-14T17:48:51.3559000Z")]
        [TestCase("1952-12-14T17:48:51.3550000Z")]
        [TestCase("1952-12-14T17:48:51.3500000Z")]
        [TestCase("1952-12-14T17:48:51.3000000Z")]
        [TestCase("1952-12-14T17:48:51.0000000Z")]
        [TestCase("1952-12-14T17:48:51Z")]
        public void DateTimeEncodeStringTestCase(string dateTimeString)
        {
            bool success = DateTime.TryParse(
                dateTimeString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out DateTime dateTime);
            Assert.That(success, Is.True);
            DateTimeEncodeStringTest(dateTime);
        }

        /// <summary>
        /// Validate that a ExpandedNodeId returns the expected
        /// result for a not well formed Uri.
        /// </summary>
        [Test]
        public void NotWellFormedUriInExpandedNodeId2String()
        {
            const string namespaceUri = "KEPServerEX";
            const string nodeName = "Data Type Examples.16 Bit Device.K Registers.Double3";
            string expectedNodeIdString = $"nsu={namespaceUri};s={nodeName}";
            var expandedNodeId = ExpandedNodeId.Parse(expectedNodeIdString);

            string stringifiedExpandedNodeId = expandedNodeId.ToString();
            TestContext.Out.WriteLine(stringifiedExpandedNodeId);
            Assert.That(stringifiedExpandedNodeId, Is.EqualTo(expectedNodeIdString));
        }

        /// <summary>
        /// Validate that a statuscode in a DataValue produces valid JSON.
        /// </summary>
        [Theory]
        public void DataValueWithStatusCodes(
            JsonEncodingType jsonEncodingType,
            [ValueSource(nameof(GoodAndBadStatusCodes))] StatusCode statusCodeVariant,
            [ValueSource(nameof(GoodAndBadStatusCodes))] StatusCode statusCode)
        {
            var dataValue = new DataValue
            {
                WrappedValue = new Variant(statusCodeVariant),
                ServerTimestamp = DateTime.UtcNow,
                StatusCode = statusCode
            };
            using var jsonEncoder = new JsonEncoder(
                m_context,
                jsonEncodingType == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            jsonEncoder.WriteDataValue("Data", dataValue);
            string result = jsonEncoder.CloseAndReturnText();
            PrettifyAndValidateJson(result, true);
        }

        /// <summary>
        /// Validate that the DateTime format strings return an equal result.
        /// </summary>
        private void DateTimeEncodeStringTest(DateTimeUtc testDateTime)
        {
            string resultString = testDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                CultureInfo.InvariantCulture);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            Span<char> valueString = stackalloc char[DateTimeHelper.DateTimeRoundTripKindLength];
            DateTimeHelper.ConvertUniversalTimeToString(
                (DateTime)testDateTime,
                valueString,
                out int charsWritten);
            string resultO = valueString[..charsWritten].ToString();
#else
            string resultO = DateTimeHelper.ConvertUniversalTimeToString((DateTime)testDateTime);
#endif
            Assert.That(resultString, Is.Not.Null);
            Assert.That(resultO, Is.Not.Null);

            TestContext.Out.WriteLine(
                """Encoded: "o": {0} "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK": {1}""",
                resultO,
                resultString);

            // last char is always 'Z', Utc time
            Assert.That(resultString[^1], Is.EqualTo('Z'));
            Assert.That(resultO[^1], Is.EqualTo('Z'));

            Assert.That(resultO, Is.EqualTo(resultString));

            var decodedXmlString = XmlConvert.ToDateTime(
                resultString,
                XmlDateTimeSerializationMode.Utc);
            var decodedXmlO = XmlConvert.ToDateTime(resultO, XmlDateTimeSerializationMode.Utc);

            Assert.That(decodedXmlString, Is.Not.EqualTo(DateTime.MinValue));
            Assert.That(decodedXmlO, Is.Not.EqualTo(DateTime.MinValue));

            TestContext.Out.WriteLine(
                "Decoded Xml: {0} {1}",
                decodedXmlO.ToString("o"),
                decodedXmlString.ToString("o"));
            Assert.That(Utils.IsEqual(decodedXmlString, decodedXmlO), Is.True);

            // ensure decoded values are identical
            bool successString = DateTime.TryParse(
                resultString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out DateTime decodedString);
            bool successO = DateTime.TryParse(
                resultO,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out DateTime decodedO);
            Assert.That(successString, Is.True);
            Assert.That(successO, Is.True);

            TestContext.Out
                .WriteLine("Decoded: {0} {1}", decodedO.ToString("o"), decodedString.ToString("o"));
            Assert.That(decodedString, Is.EqualTo(decodedO));
            Assert.That(decodedO, Is.EqualTo(testDateTime));
        }

        /// <summary>
        /// Benchmark overhead to create ServiceMessageContext.
        /// </summary>
        [Benchmark]
        [Test]
        public void ServiceMessageContext()
        {
            _ = Ua.ServiceMessageContext.Create(m_telemetry);
        }

        /// <summary>
        /// Benchmark overhead to create MemoryStream.
        /// </summary>
        [Benchmark]
        [Test]
        public void MemoryStream()
        {
            using var test = new MemoryStream();
            _ = test.Length;
        }

        /// <summary>
        /// Benchmark encoding with internal memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderConstructor()
        {
            using var jsonEncoder = new JsonEncoder(m_context, JsonEncoderOptions.Compact);
            TestEncoding(jsonEncoder);
            _ = jsonEncoder.CloseAndReturnText();
        }

        protected void TestEncoding(IEncoder encoder)
        {
            encoder.WriteBoolean("Boolean", true);
            encoder.WriteUInt64("UInt64", 1234566890);
            encoder.WriteString("String", "The quick brown fox...");
            encoder.WriteNodeId("NodeId", new NodeId(1234, 3));
            encoder.WriteInt32Array("Array", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        }

        private void RunWriteEncodeableArrayTest(
            string fieldName,
            List<FooBarEncodeable> encodeables,
            string expected,
            bool noExpectedValidation = false)
        {
            try
            {
                if (!noExpectedValidation)
                {
                    TestContext.Out.WriteLine("Expected:");
                    _ = PrettifyAndValidateJson(expected);
                }

                using var encoder = new JsonEncoder(Context, JsonEncoderOptions.Verbose);
                encoder.WriteEncodeableArray(
                    fieldName,
                    encodeables.ToArrayOf());

                string encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
            finally
            {
                encodeables.ForEach(e => e.Dispose());
            }
        }

        private IServiceMessageContext m_context;
        private MemoryStream m_memoryStream;
        private ITelemetryContext m_telemetry;
    }
}
