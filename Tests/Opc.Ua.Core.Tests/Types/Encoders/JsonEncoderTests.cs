/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text;
using System.Xml;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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

        private static readonly Guid s_nodeIdGuid = new("AABA0CFA-674F-40C7-B7FA-339D8EECB61D");
        private static readonly byte[] s_byteString = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        private static readonly string s_byteString64 = Convert.ToBase64String(s_byteString);

        /// <summary>
        /// An array of spec compliant Json encoding test data sets which
        /// shall be followed by the JSON encoder accordingly.
        /// </summary>
        [DatapointSource]
        public static readonly JsonValidationData[] Data = new JsonValidationDataCollection
        {
            { BuiltInType.Boolean, true, "true", null },
            { BuiltInType.Boolean, false, null, null, null, "false" },
            { BuiltInType.Boolean, false, "false", null, null, "false", true },
            { BuiltInType.Byte, (byte)0, null, null, null, "0" },
            { BuiltInType.Byte, (byte)0, "0", null, null, "0", true },
            { BuiltInType.Byte, (byte)88, "88", null },
            { BuiltInType.Byte, (byte)188, "188", null },
            {
                BuiltInType.Byte,
                byte.MinValue,
                byte.MinValue.ToString(CultureInfo.InvariantCulture),
                null,
                null,
                byte.MinValue.ToString(CultureInfo.InvariantCulture),
                true
            },
            {
                BuiltInType.Byte,
                byte.MaxValue,
                byte.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.SByte, (sbyte)0, null, null, null, "0" },
            { BuiltInType.SByte, (sbyte)0, "0", null, null, "0", true },
            { BuiltInType.SByte, (sbyte)-77, "-77", null },
            { BuiltInType.SByte, (sbyte)77, "77", null },
            {
                BuiltInType.SByte,
                sbyte.MaxValue,
                sbyte.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            {
                BuiltInType.SByte,
                sbyte.MinValue,
                sbyte.MinValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.UInt16, (ushort)0, null, null, null, "0" },
            { BuiltInType.UInt16, (ushort)0, "0", null, null, "0", true },
            { BuiltInType.UInt16, (ushort)12345, "12345", null },
            { BuiltInType.UInt16, (ushort)44444, "44444", null },
            {
                BuiltInType.UInt16,
                ushort.MinValue,
                ushort.MinValue.ToString(CultureInfo.InvariantCulture),
                null,
                null,
                ushort.MinValue.ToString(CultureInfo.InvariantCulture),
                true
            },
            {
                BuiltInType.UInt16,
                ushort.MaxValue,
                ushort.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.Int16, (short)0, null, null, null, "0" },
            { BuiltInType.Int16, (short)0, "0", null, null, "0", true },
            { BuiltInType.Int16, (short)-12345, "-12345", null },
            { BuiltInType.Int16, (short)12345, "12345", null },
            {
                BuiltInType.Int16,
                short.MaxValue,
                short.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            {
                BuiltInType.Int16,
                short.MinValue,
                short.MinValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.UInt32, (uint)0, null, null, null, "0" },
            { BuiltInType.UInt32, (uint)0, "0", null, null, "0", true },
            { BuiltInType.UInt32, (uint)1234567, "1234567", null },
            { BuiltInType.UInt32, (uint)4444444, "4444444", null },
            {
                BuiltInType.UInt32,
                uint.MinValue,
                uint.MinValue.ToString(CultureInfo.InvariantCulture),
                null,
                null,
                uint.MinValue.ToString(CultureInfo.InvariantCulture),
                true
            },
            {
                BuiltInType.UInt32,
                uint.MaxValue,
                uint.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.Int32, 0, null, null, null, "0" },
            { BuiltInType.Int32, 0, "0", null, null, "0", true },
            { BuiltInType.Int32, -12345678, "-12345678", null },
            { BuiltInType.Int32, 12345678, "12345678", null },
            {
                BuiltInType.Int32,
                int.MaxValue,
                int.MaxValue.ToString(CultureInfo.InvariantCulture),
                null },
            {
                BuiltInType.Int32,
                int.MinValue,
                int.MinValue.ToString(CultureInfo.InvariantCulture),
                null },
            { BuiltInType.Int64, (long)0, null, null, null, Quotes("0") },
            { BuiltInType.Int64, (long)0, Quotes("0"), null, null, Quotes("0"), true },
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
                null },
            {
                BuiltInType.Int64,
                long.MaxValue,
                Quotes(long.MaxValue.ToString(CultureInfo.InvariantCulture)),
                null },
            { BuiltInType.UInt64, (ulong)0, null, null, null, Quotes("0") },
            { BuiltInType.UInt64, (ulong)0, Quotes("0"), null, null, Quotes("0"), true },
            {
                BuiltInType.UInt64,
                kUInt64Value,
                Quotes(kUInt64Value.ToString(CultureInfo.InvariantCulture)),
                null },
            {
                BuiltInType.UInt64,
                ulong.MinValue,
                Quotes(ulong.MinValue.ToString(CultureInfo.InvariantCulture)),
                null,
                null,
                Quotes(ulong.MinValue.ToString(CultureInfo.InvariantCulture)),
                true
            },
            {
                BuiltInType.UInt64,
                ulong.MaxValue,
                Quotes(ulong.MaxValue.ToString(CultureInfo.InvariantCulture)),
                null },
            { BuiltInType.Float, (float)0, null, null, null, "0" },
            { BuiltInType.Float, (float)0, "0", null, null, "0", true },
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
                null },
            {
                BuiltInType.Float,
                float.MinValue,
                float.MinValue.ToString("R", CultureInfo.InvariantCulture),
                null },
            { BuiltInType.Float, float.NegativeInfinity, Quotes("-Infinity"), null },
            { BuiltInType.Float, float.PositiveInfinity, Quotes("Infinity"), null },
            { BuiltInType.Float, float.NaN, Quotes("NaN"), null },
            { BuiltInType.Double, (double)0, null, null, null, "0" },
            { BuiltInType.Double, (double)0, "0", null, null, "0", true },
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
                null },
            {
                BuiltInType.Double,
                double.MinValue,
                double.MinValue.ToString("R", CultureInfo.InvariantCulture),
                null },
            { BuiltInType.Double, double.NegativeInfinity, Quotes("-Infinity"), null },
            { BuiltInType.Double, double.PositiveInfinity, Quotes("Infinity"), null },
            { BuiltInType.Double, double.NaN, Quotes("NaN"), null },
            { BuiltInType.DateTime, Utils.TimeBase, Quotes("1601-01-01T00:00:00Z"), null, true },
            {
                BuiltInType.DateTime,
                Utils.TimeBase.ToUniversalTime(),
                Quotes("1601-01-01T00:00:00Z"),
                null },
            {
                BuiltInType.DateTime,
                DateTime.MinValue,
                null,
                null,
                null,
                Quotes("0001-01-01T00:00:00Z") },
            {
                BuiltInType.DateTime,
                DateTime.MinValue,
                Quotes("0001-01-01T00:00:00Z"),
                null,
                null,
                Quotes("0001-01-01T00:00:00Z"),
                true
            },
            { BuiltInType.DateTime, DateTime.MaxValue, Quotes("9999-12-31T23:59:59Z"), null },
            {
                BuiltInType.Guid,
                Uuid.Empty,
                null,
                null,
                null,
                Quotes("00000000-0000-0000-0000-000000000000") },
            {
                BuiltInType.Guid,
                Uuid.Empty,
                Quotes("00000000-0000-0000-0000-000000000000"),
                null,
                null,
                Quotes("00000000-0000-0000-0000-000000000000"),
                true
            },
            { BuiltInType.Guid, new Uuid(s_nodeIdGuid), Quotes($"{s_nodeIdGuid}"), null },
            { BuiltInType.NodeId, NodeId.Null, null, null, null, Quotes(string.Empty) },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt),
                $"{{\"Id\":{kNodeIdInt}}}",
                null,
                $"\"i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, 1),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":1}}",
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, kDemoServerIndex),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdInt, 88),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":88}}",
                null,
                $"\"ns=88;i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId("ns=0;" + kNodeIdString),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId("s=" + kNodeIdString),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 0),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 1),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":1}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, kDemoServerIndex),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(kNodeIdString, 88),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\"}}",
                null,
                $"\"g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, 1),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":1}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, kDemoServerIndex),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_nodeIdGuid, 88),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\"}}",
                null,
                $"\"b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, 1),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":1}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, kDemoServerIndex),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.NodeId,
                new NodeId(s_byteString, 88),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;b={s_byteString64}\"",
                null
            },
            // TODO: add cases for serverIndex
            {
                BuiltInType.ExpandedNodeId,
                ExpandedNodeId.Null,
                null,
                null,
                null,
                Quotes(string.Empty) },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt),
                $"{{\"Id\":{kNodeIdInt}}}",
                null,
                $"\"i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, 1),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":1}}",
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, kDemoServerIndex),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, kDemoServer2),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer2}\"}}",
                $"\"nsu={kDemoServer2};i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdInt, 88),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":88}}",
                null,
                $"\"ns=88;i={kNodeIdInt}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId("ns=0;" + kNodeIdString),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId("s=" + kNodeIdString),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 0),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}",
                null,
                $"\"s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 1),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":1}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, kDemoServerIndex),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, kDemoServer2),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"\"nsu={kDemoServer2};s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(kNodeIdString, 88),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;s={kNodeIdString}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\"}}",
                null,
                $"\"g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, 1),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":1}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, kDemoServerIndex),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, kDemoServer2),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"\"nsu={kDemoServer2};g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_nodeIdGuid, 88),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;g={s_nodeIdGuid}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\"}}",
                null,
                $"\"b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, 1),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":1}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, kDemoServerIndex),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, kDemoServer2),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"\"nsu={kDemoServer2};b={s_byteString64}\"",
                null
            },
            {
                BuiltInType.ExpandedNodeId,
                new ExpandedNodeId(s_byteString, 88),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":88}}",
                null,
                $"\"ns=88;b={s_byteString64}\"",
                null
            },
            { BuiltInType.StatusCode, new StatusCode(StatusCodes.Good), null, null, null, "{}" },
            {
                BuiltInType.StatusCode,
                new StatusCode(StatusCodes.Good),
                $"{StatusCodes.Good}",
                "{}",
                null,
                "{}",
                true },
            {
                BuiltInType.StatusCode,
                new StatusCode(StatusCodes.BadBoundNotFound),
                $"{StatusCodes.BadBoundNotFound}",
                $"{{\"Code\":{StatusCodes.BadBoundNotFound}, \"Symbol\":\"{nameof(StatusCodes.BadBoundNotFound)}\"}}",
                $"{{\"Code\":{StatusCodes.BadBoundNotFound}}}",
                $"{{\"Code\":{StatusCodes.BadBoundNotFound}, \"Symbol\":\"{nameof(StatusCodes.BadBoundNotFound)}\"}}"
            },
            {
                BuiltInType.StatusCode,
                new StatusCode(StatusCodes.BadCertificateInvalid),
                $"{StatusCodes.BadCertificateInvalid}",
                $"{{\"Code\":{StatusCodes.BadCertificateInvalid}, \"Symbol\":\"{nameof(StatusCodes.BadCertificateInvalid)}\"}}",
                $"{{\"Code\":{StatusCodes.BadCertificateInvalid}}}",
                $"{{\"Code\":{StatusCodes.BadCertificateInvalid}, \"Symbol\":\"{nameof(StatusCodes.BadCertificateInvalid)}\"}}"
            },
            {
                BuiltInType.StatusCode,
                new StatusCode(1234567),
                "1234567", /*lang=json,strict*/
                "{\"Code\":1234567}", /*lang=json,strict*/
                "{\"Code\":1234567}",
                null
            },
            { BuiltInType.DiagnosticInfo, new DiagnosticInfo(), null, null, null, "{}" },
            {
                BuiltInType.DiagnosticInfo,
                new DiagnosticInfo(-1, -1, -1, -1, null),
                null,
                null,
                null,
                "{}" },
            {
                BuiltInType.DiagnosticInfo,
                new DiagnosticInfo(1, 2, 3, 4, "AdditionalInfo"), /*lang=json,strict*/
                "{\"SymbolicId\":1,\"NamespaceUri\":2,\"Locale\":3,\"LocalizedText\":4,\"AdditionalInfo\":\"AdditionalInfo\"}",
                null
            },
            {
                BuiltInType.QualifiedName,
                QualifiedName.Null,
                null,
                null,
                null,
                Quotes(string.Empty) },
            {
                BuiltInType.QualifiedName,
                new QualifiedName(kQualifiedName),
                $"{{\"Name\":\"{kQualifiedName}\"}}",
                null,
                $"\"{kQualifiedName}\"",
                null
            },
            {
                BuiltInType.QualifiedName,
                new QualifiedName(kQualifiedName, 1),
                $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":1}}",
                $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":\"{kApplicationUri}\"}}",
                $"\"nsu={kApplicationUri};{kQualifiedName}\"",
                null
            },
            {
                BuiltInType.QualifiedName,
                new QualifiedName(kQualifiedName, kDemoServerIndex),
                $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":{kDemoServerIndex}}}",
                $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":\"{kDemoServer}\"}}",
                $"\"nsu={kDemoServer};{kQualifiedName}\"",
                null
            },
            { BuiltInType.LocalizedText, LocalizedText.Null, null, null, null, "{}" },
            {
                BuiltInType.LocalizedText,
                new LocalizedText(kLocalizedText),
                $"{{\"Text\":\"{kLocalizedText}\"}}",
                $"\"{kLocalizedText}\"",
                $"{{\"Text\":\"{kLocalizedText}\"}}",
                null,
                true
            },
            {
                BuiltInType.LocalizedText,
                new LocalizedText(kLocale, kLocalizedText),
                $"{{\"Text\":\"{kLocalizedText}\",\"Locale\":\"{kLocale}\"}}",
                $"\"{kLocalizedText}\"",
                $"{{\"Text\":\"{kLocalizedText}\",\"Locale\":\"{kLocale}\"}}",
                null
            },
            { BuiltInType.ExtensionObject, ExtensionObject.Null, null, null, null, "{}" },
            {
                BuiltInType.ExtensionObject,
                new ExtensionObject(kNodeIdInt),
                null,
                null,
                null,
                "{}" },
            {
                BuiltInType.ExtensionObject,
                new ExtensionObject((IEncodeable)null),
                null,
                null,
                null,
                "{}" },
            { BuiltInType.Variant, Variant.Null, string.Empty, null, string.Empty, "{}" },
            {
                BuiltInType.Variant,
                new Variant((sbyte)123),
                $"{{\"Type\":{BuiltInType.SByte:d}, \"Body\":123}}",
                "123",
                $"{{\"UaType\":{BuiltInType.SByte:d}, \"Value\":123}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((short)12345),
                $"{{\"Type\":{BuiltInType.Int16:d}, \"Body\":12345}}",
                "12345",
                $"{{\"UaType\":{BuiltInType.Int16:d}, \"Value\":12345}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant(1234567),
                $"{{\"Type\":{BuiltInType.Int32:d}, \"Body\":1234567}}",
                "1234567",
                $"{{\"UaType\":{BuiltInType.Int32:d}, \"Value\":1234567}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((long)123456789),
                $"{{\"Type\":{BuiltInType.Int64:d}, \"Body\":\"123456789\"}}",
                "\"123456789\"",
                $"{{\"UaType\":{BuiltInType.Int64:d}, \"Value\":\"123456789\"}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((byte)123),
                $"{{\"Type\":{BuiltInType.Byte:d}, \"Body\":123}}",
                "123",
                $"{{\"UaType\":{BuiltInType.Byte:d}, \"Value\":123}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((ushort)12345),
                $"{{\"Type\":{BuiltInType.UInt16:d}, \"Body\":12345}}",
                "12345",
                $"{{\"UaType\":{BuiltInType.UInt16:d}, \"Value\":12345}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((uint)1234567),
                $"{{\"Type\":{BuiltInType.UInt32:d}, \"Body\":1234567}}",
                "1234567",
                $"{{\"UaType\":{BuiltInType.UInt32:d}, \"Value\":1234567}}",
                null
            },
            {
                BuiltInType.Variant,
                new Variant((ulong)123456789),
                $"{{\"Type\":{BuiltInType.UInt64:d}, \"Body\":\"123456789\"}}",
                "\"123456789\"",
                $"{{\"UaType\":{BuiltInType.UInt64:d}, \"Value\":\"123456789\"}}",
                null
            },
            { BuiltInType.DataValue, new DataValue(), "{}", null },
            { BuiltInType.DataValue, new DataValue(StatusCodes.Good), "{}", null },
            {
                BuiltInType.DataValue,
                new DataValue(StatusCodes.BadNotWritable),
                $"{{\"StatusCode\":{StatusCodes.BadNotWritable}}}",
                $"{{\"StatusCode\":{{\"Code\":{StatusCodes.BadNotWritable}, \"Symbol\":\"{nameof(StatusCodes.BadNotWritable)}\"}}}}",
                $"{{\"StatusCode\":{{\"Code\":{StatusCodes.BadNotWritable}}}}}",
                $"{{\"StatusCode\":{{\"Code\":{StatusCodes.BadNotWritable}, \"Symbol\":\"{nameof(StatusCodes.BadNotWritable)}\"}}}}"
            },
            { BuiltInType.Enumeration, (TestEnumType)0, "0", "\"0\"" },
            {
                BuiltInType.Enumeration,
                TestEnumType.Three,
                TestEnumType.Three.ToString("d"),
                $"\"{TestEnumType.Three}_{TestEnumType.Three:d}\""
            },
            {
                BuiltInType.Enumeration,
                TestEnumType.Ten,
                $"{TestEnumType.Ten:d}",
                $"\"{nameof(TestEnumType.Ten)}_{TestEnumType.Ten:d}\""
            },
            { BuiltInType.Enumeration, (TestEnumType)11, "11", "\"11\"" },
            { BuiltInType.Enumeration, 1, "1", "\"1\"" },
            {
                BuiltInType.Enumeration,
                (int)TestEnumType.Two,
                TestEnumType.Two.ToString("d"),
                $"\"{TestEnumType.Two:d}\""
            },
            {
                BuiltInType.Enumeration,
                (int)TestEnumType.Hundred,
                $"{TestEnumType.Hundred:d}",
                $"\"{TestEnumType.Hundred:d}\""
            },
            { BuiltInType.Enumeration, 22, "22", "\"22\"" },
            // arrays
            {
                BuiltInType.Enumeration,
                s_testEnumArray,
                "[1,2,100]",
                "[\"One_1\",\"Two_2\",\"Hundred_100\"]" },
            { BuiltInType.Enumeration, s_testInt32Array, "[2,3,10]", "[\"2\",\"3\",\"10\"]" },
            // IEncodeable
            {
                BuiltInType.ExtensionObject,
                s_testEncodeable,
                /*lang=json,strict*/
                "{\"Body\":{\"Foo\":\"bar_999\"}}",
                /*lang=json,strict*/
                "{\"Foo\":\"bar_999\"}",
                /*lang=json,strict*/
                "{\"Foo\":\"bar_999\"}",
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
            m_context = new ServiceMessageContext(m_telemetry);
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
            m_telemetry = null;
            m_context = new ServiceMessageContext(m_telemetry);
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

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void ForcePropertiesShouldThrow(JsonEncodingType jsonEncodingType)
        {
            using var encoder = new JsonEncoder(Context, jsonEncodingType);
            NUnit.Framework.Assert
                .Throws<NotSupportedException>(() => encoder.ForceNamespaceUri = true);
            NUnit.Framework.Assert
                .Throws<NotSupportedException>(() => encoder.ForceNamespaceUriForIndex1 = true);
            NUnit.Framework.Assert
                .Throws<NotSupportedException>(() => encoder.IncludeDefaultNumberValues = true);
            NUnit.Framework.Assert
                .Throws<NotSupportedException>(() => encoder.IncludeDefaultValues = true);
            NUnit.Framework.Assert
                .Throws<NotSupportedException>(() => encoder.EncodeNodeIdAsString = false);
        }

        /// <summary>
        /// Validate constructor signature.
        /// </summary>
        [Theory]
        public void ConstructorDefault(bool useReversible, bool topLevelIsArray)
        {
            var context = new ServiceMessageContext(m_telemetry);
            using var jsonEncoder = new JsonEncoder(context, useReversible, topLevelIsArray);
            TestEncoding(jsonEncoder, topLevelIsArray);
            string result = jsonEncoder.CloseAndReturnText();

            Assert.IsNotEmpty(result);
            Assert.NotNull(result);
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
        public void ConstructorStream(MemoryStream memoryStream)
        {
            var context = new ServiceMessageContext(m_telemetry);
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
            {
                TestEncoding(jsonEncoder);
            }
            string result1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.IsNotEmpty(result1);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);

            // recycle the StreamWriter, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.IsNotEmpty(result2);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.AreEqual(result1, result2);

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, false))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.IsNotEmpty(result3);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.AreEqual(result1, result3);
            }

            // ensure the memory stream was closed
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new StreamWriter(memoryStream));
        }

        /// <summary>
        /// Use a constructor with external ArraySegmentStream,
        /// keep the stream open for more encodings.
        /// Alternate use of sequence.
        /// </summary>
        [Test]
        public void ConstructorArraySegmentStreamSequence()
        {
            var context = new ServiceMessageContext(m_telemetry);
            using var memoryStream = new ArraySegmentStream(BufferManager);
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
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
            Assert.IsNotEmpty(result1);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);
#endif

            // recycle the memory stream, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.IsNotEmpty(result2);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.AreEqual(result1, result2);

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, false))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.IsNotEmpty(result3);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.AreEqual(result1, result3);
            }

            // ensure the memory stream was closed
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new StreamWriter(memoryStream));
        }

        /// <summary>
        /// Use a constructor with external RecyclableMemoryStream,
        /// keep the stream open for more encodings.
        /// Alternate use of sequence.
        /// </summary>
        [Test]
        public void ConstructorRecyclableMemoryStreamSequence()
        {
            var context = new ServiceMessageContext(m_telemetry);
            using var memoryStream = new RecyclableMemoryStream(RecyclableMemoryManager);
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
            {
                TestEncoding(jsonEncoder);
            }

            // get the buffers and save the result
#if NET5_0_OR_GREATER
            string result1;
            {
                System.Buffers.ReadOnlySequence<byte> sequence = memoryStream.GetReadOnlySequence();
                result1 = Encoding.UTF8.GetString(sequence);
                Assert.IsNotEmpty(result1);
                TestContext.Out.WriteLine("Result1:");
                _ = PrettifyAndValidateJson(result1);
            }
#else
            string result1 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.IsNotEmpty(result1);
            TestContext.Out.WriteLine("Result1:");
            _ = PrettifyAndValidateJson(result1);
#endif

            // recycle the memory stream, ensure the result is equal
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, true))
            {
                TestEncoding(jsonEncoder);
            }
            string result2 = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.IsNotEmpty(result2);
            TestContext.Out.WriteLine("Result2:");
            _ = PrettifyAndValidateJson(result2);
            Assert.AreEqual(result1, result2);

            // recycle the StreamWriter, ensure the result is equal,
            // use reflection to return result in external stream
            memoryStream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(context, true, false, memoryStream, false))
            {
                TestEncoding(jsonEncoder);
                string result3 = jsonEncoder.CloseAndReturnText();
                Assert.IsNotEmpty(result3);
                TestContext.Out.WriteLine("Result3:");
                _ = PrettifyAndValidateJson(result3);
                Assert.AreEqual(result1, result3);
            }

            // ensure the memory stream was closed
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new StreamWriter(memoryStream));
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
                jsonValidationData.GetExpected(jsonEncodingType),
                false,
                jsonValidationData.IncludeDefaultValue);
        }

        /// <summary>
        /// Within an object JSON don't allow another object without fieldname.
        /// </summary>
        [TestCase(
            false, /*lang=json,strict*/
            "{\"Foo\":\"bar_1\"}"
        )]
        [TestCase(
            true, /*lang=json,strict*/
            "[{\"Foo\":\"bar_1\"}]"
        )]
        public void TestWriteSingleEncodeableWithoutName(bool topLevelIsArray, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context, true, topLevelIsArray);
            encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void TestWriteSingleEncodeableWithName()
        {
            const string expected = /*lang=json,strict*/
                "{\"bar_1\":{\"Foo\":\"bar_1\"}}";
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context, true, false);
            encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable));

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// A single dynamic encodeable
        /// </summary>
        [Test]
        public void TestWriteSingleDynamicEncodeableWithName()
        {
            const string expected = /*lang=json,strict*/
                "{\"bar_1\":{\"Foo\":\"bar_1\"}}";
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new DynamicEncodeable(
                "FooXml",
                "urn:dynamic_encoder_test",
                "ns=2;test_dyn_typeid",
                "s=test_dyn_binaryencodingid",
                "s=test_dyn_xmlencodingid",
                "s=test_dyn_jsonencodingid",
                new Dictionary<string, (int, string)> { { "Foo", (1, "bar_1") } });
            using var encoder = new JsonEncoder(Context, true, false);
            encoder.WriteEncodeable("bar_1", encodeable, typeof(DynamicEncodeable));

            string encoded = encoder.CloseAndReturnText();

            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Extension object with dynamic encodeable encoded to and from Json and xml
        /// </summary>
        [Test]
        public void TestExtensionObjectWithDynamicEncodeable()
        {
            const string expectedJson = /*lang=json,strict*/
                "{\"TypeId\":{\"IdType\":1,\"Id\":\"test_dyn2_typeid\"},\"Body\":{\"Foo\":\"bar_1\",\"Foo2\":\"bar_2\"}}";
            string expectedXml =
                "<uax:ExtensionObject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                "  <uax:TypeId><uax:Identifier>s=test_dyn2_xmlencodingid</uax:Identifier></uax:TypeId>" +
                "  <uax:Body><FooXml  xmlns=\"urn:dynamic_encoder_test\"><Foo>bar_1</Foo><Foo2>bar_2</Foo2></FooXml></uax:Body></uax:ExtensionObject>";
            TestContext.Out.WriteLine("Expected XML:");
            expectedXml = PrettifyAndValidateXml(Encoding.UTF8.GetBytes(expectedXml));

            var encodeable = new DynamicEncodeable(
                "FooXml",
                "urn:dynamic_encoder_test",
                "s=test_dyn2_typeid",
                "s=test_dyn2_binaryencodingid",
                "s=test_dyn2_xmlencodingid",
                "ns=1;test_dyn2_jsonencodingid",
                new Dictionary<string, (int, string)> {
                    { "Foo", (1, "bar_1") },
                    { "Foo2", (2, "bar_2") } });

            // Register in the context's Factory, make it a custom factory so the dynamic type can look up its type information when instantiated during encoding/decoding
            var dynamicContext = new ServiceMessageContext(m_telemetry)
            {
                Factory = new DynamicEncodeableFactory(Context.Factory, m_telemetry),
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
            NUnit.Framework.Assert.That(encodedXml, Is.EqualTo(expectedXml));

            // Decode from XML
            ExtensionObject extensionObjectFromXml;
            using (var ms2 = new MemoryStream(Encoding.UTF8.GetBytes(expectedXml)))
            {
                var xmlDoc = new XmlDocument();
                var r = XmlReader.Create(
                    ms2,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                xmlDoc.Load(r);

                using var decoder = new XmlDecoder(xmlDoc.FirstChild as XmlElement, dynamicContext);
                decoder.PushNamespace(Namespaces.OpcUaXsd);
                extensionObjectFromXml = decoder.ReadExtensionObject("ExtensionObject");
                decoder.PopNamespace();
            }
            NUnit.Framework.Assert
                .That(encodeable.IsEqual(extensionObjectFromXml.Body as IEncodeable), Is.True);

            // Encode to JSON
            string encodedJson;
            using (var encoder = new JsonEncoder(Context, true, false))
            {
                encoder.WriteExtensionObject(null, extensionObjectFromXml);

                encodedJson = encoder.CloseAndReturnText();

                TestContext.Out.WriteLine("Expected Json:");
                _ = PrettifyAndValidateJson(expectedJson);

                TestContext.Out.WriteLine("Encoded Json:");
                TestContext.Out.WriteLine(encodedJson);

                TestContext.Out.WriteLine("Formatted Encoded Json:");
                _ = PrettifyAndValidateJson(encodedJson);
            }
            NUnit.Framework.Assert.That(encodedJson, Is.EqualTo(expectedJson));

            // Decode from JSON: requires custom context
            ExtensionObject extensionObjectFromJson;
            using (var decoder = new JsonDecoder(encodedJson, dynamicContext))
            {
                extensionObjectFromJson = decoder.ReadExtensionObject(null);
            }
            NUnit.Framework.Assert
                .That(encodeable.IsEqual(extensionObjectFromJson.Body as IEncodeable), Is.True);
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void TestWriteSingleEncodeableWithNameAndArrayAsTopLevelExpectException()
        {
            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context, true, true);
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable)));
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void TestWriteMultipleEncodeableWithoutNameExpectException()
        {
            // invalid JSON
            // "{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}"
            // "{{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}}"
            using var encodeable = new FooBarEncodeable();
            using var encoder = new JsonEncoder(Context, true, false);
            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
            {
                encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
            });
        }

        /// <summary>
        /// It is not valid to have a JSON object within another object without fieldname
        /// </summary>
        [TestCase(
            true, /*lang=json,strict*/
            "[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]"
        )]
        public void TestWriteMultipleEncodeablesWithoutFieldNames(
            bool topLevelIsArray,
            string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };
            try
            {
                using var encoder = new JsonEncoder(Context, true, topLevelIsArray);
                foreach (FooBarEncodeable encodeable in encodeables)
                {
                    encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                }

                string encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
            }
            finally
            {
                encodeables.ForEach(e => e.Dispose());
            }
        }

        /// <summary>
        /// Write multiple encodeables with fieldnames.
        /// </summary>
        [Test]
        public void TestWriteMultipleEncodeablesWithFieldNames()
        {
            const string expected = /*lang=json,strict*/
                "{\"bar_1\":{\"Foo\":\"bar_1\"},\"bar_2\":{\"Foo\":\"bar_2\"},\"bar_3\":{\"Foo\":\"bar_3\"}}";

            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };
            try
            {
                using var encoder = new JsonEncoder(Context, true, false);
                foreach (FooBarEncodeable encodeable in encodeables)
                {
                    encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable));
                }

                string encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
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
                "{\"array\":[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]}",
                false);
        }

        /// <summary>
        /// It is not valid to have a JSON array within an object without fieldname.
        /// </summary>
        [TestCase(
            false, /*lang=json,strict*/
            "[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]"
        )]
        [TestCase(
            true, /*lang=json,strict*/
            "[[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]]"
        )]
        public void TestWriteEncodeableArrayWithoutFieldName(bool topLevelIsArray, string expected)
        {
            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };

            RunWriteEncodeableArrayTest(null, encodeables, expected, topLevelIsArray);
        }

        /// <summary>
        /// Write encodeable top level array with fieldname.
        /// </summary>
        [Test]
        public void TestWriteEncodeableArrayWithFieldNameAndArrayAsTopLevelExpectException()
        {
            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };

            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                RunWriteEncodeableArrayTest(
                    "array",
                    encodeables,
                    "[\"array\":[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]]",
                    true,
                    true));
        }

        /// <summary>
        /// Write encodeable top level array without fieldname.
        /// </summary>
        [Test]
        public void TestWriteEncodeableArrayWithoutFieldNameAndArrayAsTopLevel()
        {
            var encodeables = new List<FooBarEncodeable> { new(), new(), new() };

            RunWriteEncodeableArrayTest(
                null,
                encodeables,
                /*lang=json,strict*/
                "[[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]]",
                true);
        }

        /// <summary>
        /// Test if field names and values are properly escaped.
        /// </summary>
        [TestCase(
            "\"Hello\".\"World\"",
            "\"Test\".\"Output\"",
            /*lang=json,strict*/
            "{\"\\\"Hello\\\".\\\"World\\\"\":{\"\\\"Hello\\\".\\\"World\\\"\":\"\\\"Test\\\".\\\"Output\\\"\"}}"
        )]
        [TestCase(
            "\"Hello\".\"World\"\b\f\n\r\t\\",
            "\"Test\b\f\n\r\t\\\".\"Output\"",
            /*lang=json,strict*/
            "{\"\\\"Hello\\\".\\\"World\\\"\\b\\f\\n\\r\\t\\\\\":{\"\\\"Hello\\\".\\\"World\\\"\\b\\f\\n\\r\\t\\\\\":\"\\\"Test\\b\\f\\n\\r\\t\\\\\\\".\\\"Output\\\"\"}}"
        )]
        public void TestFieldValueEscapedEncodeable(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            using var encoder = new JsonEncoder(Context, true);
            encoder.WriteEncodeable(encodeable.FieldName, encodeable, typeof(FooBarEncodeable));

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
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
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            var list = new List<IEncodeable> { encodeable, encodeable };
            using var encoder = new JsonEncoder(Context, true);
            encoder.WriteEncodeableArray(encodeable.FieldName, list, typeof(FooBarEncodeable));

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
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
            /*lang=json,strict*/"""{"\"Hello\".\"World\"":{"\"Hello\".\"World\"":"\"Test\".\"Output\""}}"""
        )]
        public void TestFieldValueEscapedVariant(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using var encodeable = new FooBarEncodeable(fieldname, foo);
            var variant = new Variant(new ExtensionObject(encodeable));
            // non reversible to save some space
            using var encoder = new JsonEncoder(Context, false);
            encoder.WriteVariant(encodeable.FieldName, variant);

            string encoded = encoder.CloseAndReturnText();
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(encoded);

            TestContext.Out.WriteLine("Formatted Encoded:");
            _ = PrettifyAndValidateJson(encoded);

            NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// Validate that the DateTime format strings return an equal result.
        /// </summary>
        [Test]
        [Repeat(kRandomRepeats)]
        public void DateTimeEncodeRandomStringTest()
        {
            SetRepeatedRandomSeed();
            DateTime randomDateTime = DataGenerator.GetRandomDateTime().ToUniversalTime();
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
            Assert.True(success);
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
            var expandedNodeId = new ExpandedNodeId(expectedNodeIdString);

            string stringifiedExpandedNodeId = expandedNodeId.ToString();
            TestContext.Out.WriteLine(stringifiedExpandedNodeId);
            Assert.AreEqual(expectedNodeIdString, stringifiedExpandedNodeId);
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
            using var jsonEncoder = new JsonEncoder(m_context, jsonEncodingType);
            jsonEncoder.WriteDataValue("Data", dataValue);
            string result = jsonEncoder.CloseAndReturnText();
            PrettifyAndValidateJson(result, true);
        }

        /// <summary>
        /// Validate that the DateTime format strings return an equal result.
        /// </summary>
        public void DateTimeEncodeStringTest(DateTime testDateTime)
        {
            string resultString = testDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                CultureInfo.InvariantCulture);
#if ECC_SUPPORT && (NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER)
            Span<char> valueString = stackalloc char[JsonEncoder.DateTimeRoundTripKindLength];
            JsonEncoder.ConvertUniversalTimeToString(
                testDateTime,
                valueString,
                out int charsWritten);
            string resultO = valueString[..charsWritten].ToString();
#else
            string resultO = JsonEncoder.ConvertUniversalTimeToString(testDateTime);
#endif
            Assert.NotNull(resultString);
            Assert.NotNull(resultO);

            TestContext.Out.WriteLine(
                "Encoded: \"o\": {0} \"yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK\": {1}",
                resultO,
                resultString);

            // last char is always 'Z', Utc time
            Assert.AreEqual('Z', resultString[^1]);
            Assert.AreEqual('Z', resultO[^1]);

            Assert.AreEqual(resultString, resultO);

            var decodedXmlString = XmlConvert.ToDateTime(
                resultString,
                XmlDateTimeSerializationMode.Utc);
            var decodedXmlO = XmlConvert.ToDateTime(resultO, XmlDateTimeSerializationMode.Utc);

            Assert.NotNull(decodedXmlString);
            Assert.NotNull(decodedXmlO);

            TestContext.Out.WriteLine(
                "Decoded Xml: {0} {1}",
                decodedXmlO.ToString("o"),
                decodedXmlString.ToString("o"));
            Assert.True(Utils.IsEqual(decodedXmlString, decodedXmlO));

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
            Assert.True(successString);
            Assert.True(successO);

            TestContext.Out
                .WriteLine("Decoded: {0} {1}", decodedO.ToString("o"), decodedString.ToString("o"));
            Assert.AreEqual(decodedO, decodedString);
            Assert.AreEqual(testDateTime, decodedO);
        }

        /// <summary>
        /// Benchmark overhead to create ServiceMessageContext.
        /// </summary>
        [Benchmark]
        [Test]
        public void ServiceMessageContext()
        {
            _ = new ServiceMessageContext(null);
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
            using var jsonEncoder = new JsonEncoder(m_context, false);
            TestEncoding(jsonEncoder);
            _ = jsonEncoder.CloseAndReturnText();
        }

        protected void TestEncoding(IEncoder encoder, bool topLevelIsArray = false)
        {
            if (topLevelIsArray)
            {
                encoder.WriteNodeId(null, new NodeId(10000, 0));
                encoder.WriteNodeId(null, new NodeId(20000, 1));
                encoder.WriteNodeId(null, new NodeId(30000, 2));
                encoder.WriteNodeId(null, new NodeId(40000, 3));
            }
            else
            {
                encoder.WriteBoolean("Boolean", true);
                encoder.WriteUInt64("UInt64", 1234566890);
                encoder.WriteString("String", "The quick brown fox...");
                encoder.WriteNodeId("NodeId", new NodeId(1234, 3));
                encoder.WriteInt32Array("Array", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
            }
        }

        private void RunWriteEncodeableArrayTest(
            string fieldName,
            List<FooBarEncodeable> encodeables,
            string expected,
            bool topLevelIsArray,
            bool noExpectedValidation = false)
        {
            try
            {
                if (!noExpectedValidation)
                {
                    TestContext.Out.WriteLine("Expected:");
                    _ = PrettifyAndValidateJson(expected);
                }

                using var encoder = new JsonEncoder(Context, true, topLevelIsArray);
                encoder.WriteEncodeableArray(
                    fieldName,
                    [.. encodeables.Cast<IEncodeable>()],
                    typeof(FooBarEncodeable));

                string encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                NUnit.Framework.Assert.That(encoded, Is.EqualTo(expected));
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
