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
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using NUnit.Framework;
using System.Threading;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the Json encoder and decoder class.
    /// </summary>
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class JsonEncoderTests : EncoderCommon
    {
        #region DataSource
        static TestEnumType[] TestEnumArray = new TestEnumType[]
            { TestEnumType.One, TestEnumType.Two, TestEnumType.Hundred };
        static Int32[] TestInt32Array = new Int32[]
            { 2, 3, 10 };
        static ExtensionObject TestEncodeable = new ExtensionObject(new FooBarEncodeable(999));

        /// <summary>
        /// Constants used by test data set.
        /// </summary>
        const ushort kDemoServerIndex = 3;
        const string kDemoServer = "http://www.opcfoundation.org/DemoServer/";
        const string kDemoServer2 = "http://www.opcfoundation.org/DemoServer2/";
        const ushort kServerUriIndex = 2;
        const string kServerUri = "opc.tcp://localhost:55555";
        const string kNodeIdString = "theNode";
        const string kQualifiedName = "theName";
        const string kLocalizedText = "theText";
        const string kLocale = "en-us";
        const int kNodeIdInt = 2345;
        const Int64 kInt64Value = -123456789123456;
        const UInt64 kUInt64Value = 123456789123456;
        static Guid s_nodeIdGuid = new Guid("AABA0CFA-674F-40C7-B7FA-339D8EECB61D");
        static byte[] s_byteString = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        static string s_byteString64 = Convert.ToBase64String((byte[])s_byteString);

        /// <summary>
        /// An array of spec compliant Json encoding test data sets which
        /// shall be followed by the JSON encoder accordingly.
        /// </summary>
        [DatapointSource]
        public JsonValidationData[] Data = new JsonValidationDataCollection() {
            {   BuiltInType.Boolean, true,"true", null },
            {   BuiltInType.Boolean, false, null, null },
            {   BuiltInType.Boolean, false, "false", null, true },

            {   BuiltInType.Byte, (Byte)0, null, null},
            {   BuiltInType.Byte, (Byte)0, "0", null, true },
            {   BuiltInType.Byte, (Byte)88, "88", null },
            {   BuiltInType.Byte, (Byte)188, "188", null },
            {   BuiltInType.Byte, Byte.MinValue, Byte.MinValue.ToString(), null, true},
            {   BuiltInType.Byte, Byte.MaxValue, Byte.MaxValue.ToString(), null },

            {   BuiltInType.SByte, (SByte)0, null, null },
            {   BuiltInType.SByte, (SByte)0, "0", null, true },
            {   BuiltInType.SByte, (SByte)(-77), "-77", null },
            {   BuiltInType.SByte, (SByte)(77), "77", null },
            {   BuiltInType.SByte, SByte.MaxValue, SByte.MaxValue.ToString(), null },
            {   BuiltInType.SByte, SByte.MinValue, SByte.MinValue.ToString(), null },

            {   BuiltInType.UInt16, (UInt16)0, null, null},
            {   BuiltInType.UInt16, (UInt16)0, "0", null, true },
            {   BuiltInType.UInt16, (UInt16)12345, "12345", null },
            {   BuiltInType.UInt16, (UInt16)44444, "44444", null },
            {   BuiltInType.UInt16, UInt16.MinValue, UInt16.MinValue.ToString(), null, true },
            {   BuiltInType.UInt16, UInt16.MaxValue, UInt16.MaxValue.ToString(), null },

            {   BuiltInType.Int16, (Int16)0, null, null },
            {   BuiltInType.Int16, (Int16)0, "0", null, true },
            {   BuiltInType.Int16, (Int16)(-12345), "-12345", null },
            {   BuiltInType.Int16, (Int16)12345, "12345", null },
            {   BuiltInType.Int16, Int16.MaxValue, Int16.MaxValue.ToString(), null },
            {   BuiltInType.Int16, Int16.MinValue, Int16.MinValue.ToString(), null },

            {   BuiltInType.UInt32, (UInt32)0, null, null },
            {   BuiltInType.UInt32, (UInt32)0, "0", null, true },
            {   BuiltInType.UInt32, (UInt32)1234567, "1234567", null },
            {   BuiltInType.UInt32, (UInt32)4444444, "4444444", null },
            {   BuiltInType.UInt32, UInt32.MinValue, UInt32.MinValue.ToString(), null, true },
            {   BuiltInType.UInt32, UInt32.MaxValue, UInt32.MaxValue.ToString(), null },

            {   BuiltInType.Int32, (Int32)0, null, null },
            {   BuiltInType.Int32, (Int32)0, "0", null, true },
            {   BuiltInType.Int32, (Int32)(-12345678), "-12345678", null },
            {   BuiltInType.Int32, (Int32)12345678, "12345678", null },
            {   BuiltInType.Int32, Int32.MaxValue, Int32.MaxValue.ToString(), null },
            {   BuiltInType.Int32, Int32.MinValue, Int32.MinValue.ToString(), null },

            {   BuiltInType.Int64, (Int64)0, null, null },
            {   BuiltInType.Int64, (Int64)0, Quotes("0"), null, true },
            {   BuiltInType.Int64, kInt64Value, Quotes(kInt64Value.ToString()), null },
            {   BuiltInType.Int64, (Int64)kUInt64Value, Quotes(kUInt64Value.ToString()), null },
            {   BuiltInType.Int64, Int64.MinValue, Quotes(Int64.MinValue.ToString()), null },
            {   BuiltInType.Int64, Int64.MaxValue, Quotes(Int64.MaxValue.ToString()), null },

            {   BuiltInType.UInt64, (UInt64)0, null, null },
            {   BuiltInType.UInt64, (UInt64)0, Quotes("0"), null, true },
            {   BuiltInType.UInt64, (UInt64)kUInt64Value, Quotes(kUInt64Value.ToString()), null },
            {   BuiltInType.UInt64, UInt64.MinValue, Quotes(UInt64.MinValue.ToString()), null, true },
            {   BuiltInType.UInt64, UInt64.MaxValue, Quotes(UInt64.MaxValue.ToString()), null },

            {   BuiltInType.Float, (Single)0, null, null},
            {   BuiltInType.Float, (Single)0, "0", null, true},
            {   BuiltInType.Float, (Single)(-12345678.1234), Convert.ToSingle("-12345678.1234", CultureInfo.InvariantCulture).ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Float, (Single)12345678.1234, Convert.ToSingle("12345678.1234", CultureInfo.InvariantCulture).ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Float, Single.MaxValue, Single.MaxValue.ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Float, Single.MinValue, Single.MinValue.ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Float, Single.NegativeInfinity, Quotes("-Infinity"), null },
            {   BuiltInType.Float, Single.PositiveInfinity, Quotes("Infinity"), null },
            {   BuiltInType.Float, Single.NaN, Quotes("NaN"), null },

            {   BuiltInType.Double, (Double)0, null, null},
            {   BuiltInType.Double, (Double)0, "0", null, true},
            {   BuiltInType.Double, (Double)(-12345678.1234), Convert.ToDouble("-12345678.1234", CultureInfo.InvariantCulture).ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Double, (Double)12345678.1234, Convert.ToDouble("12345678.1234", CultureInfo.InvariantCulture).ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Double, Double.MaxValue, Double.MaxValue.ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Double, Double.MinValue, Double.MinValue.ToString("R",CultureInfo.InvariantCulture), null },
            {   BuiltInType.Double, Double.NegativeInfinity, Quotes("-Infinity"), null },
            {   BuiltInType.Double, Double.PositiveInfinity, Quotes("Infinity"), null },
            {   BuiltInType.Double, Double.NaN, Quotes("NaN"), null },

            {   BuiltInType.DateTime, Utils.TimeBase,  Quotes("1601-01-01T00:00:00Z"), null , true},
            {   BuiltInType.DateTime, Utils.TimeBase.ToUniversalTime(),  Quotes("1601-01-01T00:00:00Z"), null },
            {   BuiltInType.DateTime, DateTime.MinValue,  null, null },
            {   BuiltInType.DateTime, DateTime.MinValue,  Quotes("0001-01-01T00:00:00Z"), null, true },
            {   BuiltInType.DateTime, DateTime.MaxValue,  Quotes("9999-12-31T23:59:59Z"), null },

            {   BuiltInType.Guid, Uuid.Empty,  null, null },
            {   BuiltInType.Guid, Uuid.Empty,  Quotes("00000000-0000-0000-0000-000000000000"), null, true },
            {   BuiltInType.Guid, new Uuid(s_nodeIdGuid),  Quotes($"{s_nodeIdGuid}"), null },

            {   BuiltInType.NodeId, NodeId.Null, null, null },
            {   BuiltInType.NodeId, new NodeId(kNodeIdInt), $"{{\"Id\":{kNodeIdInt}}}", null },
            {   BuiltInType.NodeId, new NodeId(kNodeIdInt,1), $"{{\"Id\":{kNodeIdInt},\"Namespace\":1}}", null },
            {   BuiltInType.NodeId, new NodeId(kNodeIdInt,kDemoServerIndex),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":{kDemoServerIndex}}}", $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.NodeId, new NodeId(kNodeIdInt,88), $"{{\"Id\":{kNodeIdInt},\"Namespace\":88}}", null},
            {   BuiltInType.NodeId, new NodeId(kNodeIdString), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}", null },
            {   BuiltInType.NodeId, new NodeId(kNodeIdString,1), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":1}}", null },
            {   BuiltInType.NodeId, new NodeId(kNodeIdString,kDemoServerIndex),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.NodeId, new NodeId(kNodeIdString,88), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":88}}", null},
            {   BuiltInType.NodeId, new NodeId(s_nodeIdGuid), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\"}}", null },
            {   BuiltInType.NodeId, new NodeId(s_nodeIdGuid,1), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":1}}", null },
            {   BuiltInType.NodeId, new NodeId(s_nodeIdGuid,kDemoServerIndex),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.NodeId, new NodeId(s_nodeIdGuid,88), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":88}}", null},
            {   BuiltInType.NodeId, new NodeId(s_byteString), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\"}}", null },
            {   BuiltInType.NodeId, new NodeId(s_byteString,1), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":1}}", null },
            {   BuiltInType.NodeId, new NodeId(s_byteString,kDemoServerIndex),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.NodeId, new NodeId(s_byteString,88), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":88}}", null},
            // TODO: add cases for serverIndex
            {   BuiltInType.ExpandedNodeId, ExpandedNodeId.Null, null, null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdInt), $"{{\"Id\":{kNodeIdInt}}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdInt,1), $"{{\"Id\":{kNodeIdInt},\"Namespace\":1}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdInt,kDemoServerIndex),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":{kDemoServerIndex}}}", $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdInt,kDemoServer2),
                $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer2}\"}}", $"{{\"Id\":{kNodeIdInt},\"Namespace\":\"{kDemoServer2}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdInt,88), $"{{\"Id\":{kNodeIdInt},\"Namespace\":88}}", null},
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdString), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\"}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdString,1), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":1}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdString,kDemoServerIndex),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdString,kDemoServer2),
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":\"{kDemoServer2}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(kNodeIdString,88), $"{{\"IdType\":1,\"Id\":\"{kNodeIdString}\",\"Namespace\":88}}", null},
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_nodeIdGuid), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\"}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_nodeIdGuid, 1), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":1}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_nodeIdGuid, kDemoServerIndex),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_nodeIdGuid, kDemoServer2),
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":\"{kDemoServer2}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_nodeIdGuid,88), $"{{\"IdType\":2,\"Id\":\"{s_nodeIdGuid}\",\"Namespace\":88}}", null},
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_byteString), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\"}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_byteString,1), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":1}}", null },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_byteString,kDemoServerIndex),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":{kDemoServerIndex}}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_byteString,kDemoServer2),
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer2}\"}}",
                $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":\"{kDemoServer2}\"}}" },
            {   BuiltInType.ExpandedNodeId, new ExpandedNodeId(s_byteString,88), $"{{\"IdType\":3,\"Id\":\"{s_byteString64}\",\"Namespace\":88}}", null},

            {   BuiltInType.StatusCode, new StatusCode(StatusCodes.Good), null, null},
            {   BuiltInType.StatusCode, new StatusCode(StatusCodes.Good), $"{StatusCodes.Good}", "", true},
            {   BuiltInType.StatusCode, new StatusCode(StatusCodes.BadBoundNotFound), $"{StatusCodes.BadBoundNotFound}",
                $"{{\"Code\":{StatusCodes.BadBoundNotFound}, \"Symbol\":\"{nameof(StatusCodes.BadBoundNotFound)}\"}}"},
            {   BuiltInType.StatusCode, new StatusCode(StatusCodes.BadCertificateInvalid),
                $"{StatusCodes.BadCertificateInvalid}", $"{{\"Code\":{StatusCodes.BadCertificateInvalid}, \"Symbol\":\"{nameof(StatusCodes.BadCertificateInvalid)}\"}}"},

            {   BuiltInType.DiagnosticInfo, new DiagnosticInfo(), "{}", null},

            {   BuiltInType.QualifiedName, QualifiedName.Null, null, null},
            {   BuiltInType.QualifiedName, new QualifiedName(kQualifiedName), $"{{\"Name\":\"{kQualifiedName}\"}}", null},
            {   BuiltInType.QualifiedName, new QualifiedName(kQualifiedName, 1), $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":1}}", $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":1}}"},
            {   BuiltInType.QualifiedName, new QualifiedName(kQualifiedName, kDemoServerIndex),
                $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":{kDemoServerIndex}}}", $"{{\"Name\":\"{kQualifiedName}\",\"Uri\":\"{kDemoServer}\"}}"},

            {   BuiltInType.LocalizedText, LocalizedText.Null, null, null},
            {   BuiltInType.LocalizedText, new LocalizedText(kLocalizedText), $"{{\"Text\":\"{kLocalizedText}\"}}", $"\"{kLocalizedText}\"", true},
            {   BuiltInType.LocalizedText, new LocalizedText(kLocale, kLocalizedText), $"{{\"Text\":\"{kLocalizedText}\",\"Locale\":\"{kLocale}\"}}", $"\"{kLocalizedText}\""},

            {   BuiltInType.ExtensionObject, ExtensionObject.Null, null, null},
            {   BuiltInType.ExtensionObject, new ExtensionObject(kNodeIdInt), null, null},
            {   BuiltInType.ExtensionObject, new ExtensionObject((IEncodeable) null), null, null},

            {   BuiltInType.Variant, Variant.Null, "", null},
            {   BuiltInType.Variant, new Variant((SByte)123), $"{{\"Type\":{BuiltInType.SByte.ToString("d")}, \"Body\":123}}", "123"},
            {   BuiltInType.Variant, new Variant((Int16)12345), $"{{\"Type\":{BuiltInType.Int16.ToString("d")}, \"Body\":12345}}", "12345"},
            {   BuiltInType.Variant, new Variant((Int32)1234567), $"{{\"Type\":{BuiltInType.Int32.ToString("d")}, \"Body\":1234567}}", "1234567"},
            {   BuiltInType.Variant, new Variant((Int64)123456789), $"{{\"Type\":{BuiltInType.Int64.ToString("d")}, \"Body\":\"123456789\"}}", "\"123456789\""},
            {   BuiltInType.Variant, new Variant((Byte)123), $"{{\"Type\":{BuiltInType.Byte.ToString("d")}, \"Body\":123}}", "123"},
            {   BuiltInType.Variant, new Variant((UInt16)12345), $"{{\"Type\":{BuiltInType.UInt16.ToString("d")}, \"Body\":12345}}", "12345"},
            {   BuiltInType.Variant, new Variant((UInt32)1234567), $"{{\"Type\":{BuiltInType.UInt32.ToString("d")}, \"Body\":1234567}}", "1234567"},
            {   BuiltInType.Variant, new Variant((UInt64)123456789), $"{{\"Type\":{BuiltInType.UInt64.ToString("d")}, \"Body\":\"123456789\"}}", "\"123456789\""},

            {   BuiltInType.DataValue, new DataValue(), "{}", null},
            {   BuiltInType.DataValue, new DataValue(StatusCodes.Good), "{}", null},

            {   BuiltInType.Enumeration, (TestEnumType) 0, "0", "\"0\""},
            {   BuiltInType.Enumeration, TestEnumType.Three, TestEnumType.Three.ToString("d"), $"\"{TestEnumType.Three}_{TestEnumType.Three.ToString("d")}\""},
            {   BuiltInType.Enumeration, TestEnumType.Ten, $"{TestEnumType.Ten.ToString("d")}", $"\"{TestEnumType.Ten.ToString()}_{TestEnumType.Ten.ToString("d")}\""},
            {   BuiltInType.Enumeration, (TestEnumType) 11, "11", "\"11\""},

            {   BuiltInType.Enumeration, (Int32) 1, "1", "\"1\""},
            {   BuiltInType.Enumeration, (Int32)TestEnumType.Two, TestEnumType.Two.ToString("d"), $"\"{TestEnumType.Two.ToString("d")}\""},
            {   BuiltInType.Enumeration, (Int32)TestEnumType.Hundred, $"{TestEnumType.Hundred.ToString("d")}", $"\"{TestEnumType.Hundred.ToString("d")}\""},
            {   BuiltInType.Enumeration, (Int32) 22, "22", "\"22\""},

            // arrays
            {   BuiltInType.Enumeration, TestEnumArray, "[1,2,100]", "[\"One_1\",\"Two_2\",\"Hundred_100\"]"},
            {   BuiltInType.Enumeration, TestInt32Array, "[2,3,10]", "[\"2\",\"3\",\"10\"]"},

            // IEncodeable
            {   BuiltInType.ExtensionObject, TestEncodeable, "{\"Body\":{\"Foo\":\"bar_999\"}}", "{\"Foo\":\"bar_999\"}"}

        }.ToArray();
        #endregion

        #region Setup
        [OneTimeSetUp]
        protected new void OneTimeSetUp()
        {
            ushort demoServerIndex = NameSpaceUris.GetIndexOrAppend(kDemoServer);
            Assume.That(demoServerIndex == kDemoServerIndex, $"Server Index: {demoServerIndex} != {kDemoServerIndex}");
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

        #region Test Methods
        /// <summary>
        /// Verify reversible Json encoding.
        /// </summary>
        [Theory]
        public void JsonEncodeRev(JsonValidationData jsonValidationData)
        {
            EncodeJsonVerifyResult(
                jsonValidationData.BuiltInType,
                jsonValidationData.Instance,
                true,
                jsonValidationData.ExpectedReversible,
                false,
                jsonValidationData.IncludeDefaultValue);
        }

        /// <summary>
        /// Verify non reversible Json encoding.
        /// </summary>
        [Theory]
        public void JsonEncodeNonRev(JsonValidationData jsonValidationData)
        {
            EncodeJsonVerifyResult(
                jsonValidationData.BuiltInType,
                jsonValidationData.Instance,
                false,
                jsonValidationData.ExpectedNonReversible ?? jsonValidationData.ExpectedReversible,
                false,
                jsonValidationData.IncludeDefaultValue);
        }

        /// <summary>
        /// Within a object JSON don't allow another object without fieldname.
        /// </summary>
        [TestCase(false, "{\"Foo\":\"bar_1\"}")]
        [TestCase(true, "[{\"Foo\":\"bar_1\"}]")]
        public void Test_WriteSingleEncodeableWithoutName(bool topLevelIsArray, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using (var encodeable = new FooBarEncodeable())
            {
                var encoder = new JsonEncoder(Context, true, null, topLevelIsArray);

                encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));

                var encoded = encoder.CloseAndReturnText();

                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void Test_WriteSingleEncodeableWithName()
        {
            var expected = "{\"bar_1\":{\"Foo\":\"bar_1\"}}";
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using (var encodeable = new FooBarEncodeable())
            {
                var encoder = new JsonEncoder(Context, true, null, false);

                encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable));

                var encoded = encoder.CloseAndReturnText();

                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void Test_WriteSingleEncodeableWithNameAndArrayAsTopLevel_Expect_Exception()
        {
            using (var encodeable = new FooBarEncodeable())
            {
                var encoder = new JsonEncoder(Context, true, null, true);

                Assert.Throws<ServiceResultException>(() => encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable)));
            }
        }

        /// <summary>
        /// A single encodeable in an array cannot have a fieldname.
        /// </summary>
        [Test]
        public void Test_WriteMultipleEncodeableWithoutName_Expect_Exception()
        {
            // invalid JSON
            // "{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}"
            // "{{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}}"
            using (var encodeable = new FooBarEncodeable())
            {
                var encoder = new JsonEncoder(Context, true, null, false);

                Assert.Throws<ServiceResultException>(() => {
                    encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                    encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                    encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));

                }
                );
            }
        }

        /// <summary>
        /// It is not valid to have a JSON object within another object without fieldname
        /// </summary>
        [TestCase(true, "[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]")]
        public void Test_WriteMultipleEncodeablesWithoutFieldNames(bool topLevelIsArray, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);


            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };
            try
            {
                var encoder = new JsonEncoder(Context, true, null, topLevelIsArray);

                foreach (var encodeable in encodeables)
                {
                    encoder.WriteEncodeable(null, encodeable, typeof(FooBarEncodeable));
                }

                var encoded = encoder.CloseAndReturnText();
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
        /// Write multiple encodeables with fieldnames.
        /// </summary>
        [Test]
        public void Test_WriteMultipleEncodeablesWithFieldNames()
        {
            var expected = "{\"bar_1\":{\"Foo\":\"bar_1\"},\"bar_2\":{\"Foo\":\"bar_2\"},\"bar_3\":{\"Foo\":\"bar_3\"}}";

            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };
            try
            {
                var encoder = new JsonEncoder(Context, true, null, false);

                foreach (var encodeable in encodeables)
                {
                    encoder.WriteEncodeable(encodeable.Foo, encodeable, typeof(FooBarEncodeable));
                }

                var encoded = encoder.CloseAndReturnText();
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
        public void Test_WriteEncodeableArrayWithFieldName()
        {
            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };

            RunWriteEncodeableArrayTest(
                "array",
                encodeables,
                "{\"array\":[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]}",
                false);
        }

        /// <summary>
        /// It is not valid to have a JSON array within an object without fieldname.
        /// </summary>
        [TestCase(false, "[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]")]
        [TestCase(true, "[[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]]")]
        public void Test_WriteEncodeableArrayWithoutFieldName(bool topLevelIsArray, string expected)
        {
            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };

            RunWriteEncodeableArrayTest(
                null,
                encodeables,
                expected,
                topLevelIsArray);
        }

        /// <summary>
        /// Write encodeable top level array with fieldname.
        /// </summary>
        [Test]
        public void Test_WriteEncodeableArrayWithFieldNameAndArrayAsTopLevel_Expect_Exception()
        {
            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };

            Assert.Throws<ServiceResultException>(() => RunWriteEncodeableArrayTest(
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
        public void Test_WriteEncodeableArrayWithoutFieldNameAndArrayAsTopLevel()
        {
            var encodeables = new List<FooBarEncodeable> { new FooBarEncodeable(), new FooBarEncodeable(), new FooBarEncodeable() };

            RunWriteEncodeableArrayTest(
                null,
                encodeables,
                "[[{\"Foo\":\"bar_1\"},{\"Foo\":\"bar_2\"},{\"Foo\":\"bar_3\"}]]",
                true);
        }

        /// <summary>
        /// Test if field names and values are properly escaped.
        /// </summary>
        [TestCase("\"Hello\".\"World\"", "\"Test\".\"Output\"",
            "{\"\\\"Hello\\\".\\\"World\\\"\":{\"\\\"Hello\\\".\\\"World\\\"\":\"\\\"Test\\\".\\\"Output\\\"\"}}")]
        [TestCase("\"Hello\".\"World\"\b\f\n\r\t\\", "\"Test\b\f\n\r\t\\\".\"Output\"",
            "{\"\\\"Hello\\\".\\\"World\\\"\\b\\f\\n\\r\\t\\\\\":{\"\\\"Hello\\\".\\\"World\\\"\\b\\f\\n\\r\\t\\\\\":\"\\\"Test\\b\\f\\n\\r\\t\\\\\\\".\\\"Output\\\"\"}}")]
        public void TestFieldValueEscapedEncodeable(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using (var encodeable = new FooBarEncodeable(fieldname, foo))
            {
                var encoder = new JsonEncoder(Context, true);
                encoder.WriteEncodeable(encodeable.FieldName, encodeable, typeof(FooBarEncodeable));

                var encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Test if field names and values are properly escaped when used in an array.
        /// </summary>
        [TestCase("\"Hello\".\"World\"", "\"Test\".\"Output\"",
            "{\"\\\"Hello\\\".\\\"World\\\"\":[" +
            "{\"\\\"Hello\\\".\\\"World\\\"\":\"\\\"Test\\\".\\\"Output\\\"\"}," +
            "{\"\\\"Hello\\\".\\\"World\\\"\":\"\\\"Test\\\".\\\"Output\\\"\"}" +
            "]}")]
        public void TestFieldValueEscapedArray(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using (var encodeable = new FooBarEncodeable(fieldname, foo))
            {
                var list = new List<IEncodeable>() { encodeable, encodeable };
                var encoder = new JsonEncoder(Context, true);
                encoder.WriteEncodeableArray(encodeable.FieldName, list, typeof(FooBarEncodeable));

                var encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Test if field names and values are properly escaped when used in a variant.
        /// </summary>
        [TestCase("\"Hello\".\"World\"", "\"Test\".\"Output\"",
            "{\"\\\"Hello\\\".\\\"World\\\"\":" +
            "{\"\\\"Hello\\\".\\\"World\\\"\":\"\\\"Test\\\".\\\"Output\\\"\"}" +
            "}")]
        public void TestFieldValueEscapedVariant(string fieldname, string foo, string expected)
        {
            TestContext.Out.WriteLine("Expected:");
            _ = PrettifyAndValidateJson(expected);

            using (var encodeable = new FooBarEncodeable(fieldname, foo))
            {
                var variant = new Variant(new ExtensionObject(encodeable));
                // non reversible to save some space
                var encoder = new JsonEncoder(Context, false);
                encoder.WriteVariant(encodeable.FieldName, variant);

                var encoded = encoder.CloseAndReturnText();
                TestContext.Out.WriteLine("Encoded:");
                TestContext.Out.WriteLine(encoded);

                TestContext.Out.WriteLine("Formatted Encoded:");
                _ = PrettifyAndValidateJson(encoded);

                Assert.That(encoded, Is.EqualTo(expected));
            }
        }

        #endregion

        #region Private Methods

        private void RunWriteEncodeableArrayTest(string fieldName, List<FooBarEncodeable> encodeables, string expected, bool topLevelIsArray, bool noExpectedValidation = false)
        {
            try
            {
                if (!noExpectedValidation)
                {
                    TestContext.Out.WriteLine("Expected:");
                    _ = PrettifyAndValidateJson(expected);
                }

                var encoder = new JsonEncoder(Context, true, null, topLevelIsArray);

                encoder.WriteEncodeableArray(
                    fieldName,
                    encodeables.Cast<IEncodeable>().ToList(),
                    typeof(FooBarEncodeable));

                var encoded = encoder.CloseAndReturnText();
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

        #endregion

        #region Private Fields
        #endregion
    }

}
