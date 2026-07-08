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
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NumericRangeCoverageTests
    {
        [TestCaseSource(nameof(ApplyRangeArrayCases))]
        public void ApplyRangeArraySlicesMiddleElements(Variant input, Variant expected)
        {
            Variant value = input;
            var range = new NumericRange(1, 2);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(ApplyRangeMatrixCases))]
        public void ApplyRangeMatrixSelectsFirstRow(Variant input, Variant expected)
        {
            Variant value = input;
            NumericRange range = NumericRange.Parse("0,0:1");
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(UpdateRangeArrayCases))]
        public void UpdateRangeArrayReplacesMiddleElements(Variant input, Variant slice, Variant expected)
        {
            Variant value = input;
            var range = new NumericRange(1, 2);
            StatusCode status = range.UpdateRange(ref value, slice);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(UpdateRangeMatrixCases))]
        public void UpdateRangeMatrixReplacesFirstRow(Variant input, Variant slice, Variant expected)
        {
            Variant value = input;
            NumericRange range = NumericRange.Parse("0,0:1");
            StatusCode status = range.UpdateRange(ref value, slice);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo(expected));
        }

        private static IEnumerable<TestCaseData> ApplyRangeArrayCases()
        {
            bool[] boolSrc = [true, false, true, false];
            bool[] boolExp = [boolSrc[1], boolSrc[2]];
            yield return new TestCaseData(
                Variant.From(boolSrc.ToArrayOf()),
                Variant.From(boolExp.ToArrayOf())).SetName("ApplyRangeArrayBoolean");

            sbyte[] sbyteSrc = [10, 20, 30, 40];
            sbyte[] sbyteExp = [sbyteSrc[1], sbyteSrc[2]];
            yield return new TestCaseData(
                Variant.From(sbyteSrc.ToArrayOf()),
                Variant.From(sbyteExp.ToArrayOf())).SetName("ApplyRangeArraySByte");

            byte[] byteSrc = [10, 20, 30, 40];
            byte[] byteExp = [byteSrc[1], byteSrc[2]];
            yield return new TestCaseData(
                Variant.From(byteSrc.ToArrayOf()),
                Variant.From(byteExp.ToArrayOf())).SetName("ApplyRangeArrayByte");

            short[] int16Src = [10, 20, 30, 40];
            short[] int16Exp = [int16Src[1], int16Src[2]];
            yield return new TestCaseData(
                Variant.From(int16Src.ToArrayOf()),
                Variant.From(int16Exp.ToArrayOf())).SetName("ApplyRangeArrayInt16");

            ushort[] uint16Src = [10, 20, 30, 40];
            ushort[] uint16Exp = [uint16Src[1], uint16Src[2]];
            yield return new TestCaseData(
                Variant.From(uint16Src.ToArrayOf()),
                Variant.From(uint16Exp.ToArrayOf())).SetName("ApplyRangeArrayUInt16");

            int[] int32Src = [10, 20, 30, 40];
            int[] int32Exp = [int32Src[1], int32Src[2]];
            yield return new TestCaseData(
                Variant.From(int32Src.ToArrayOf()),
                Variant.From(int32Exp.ToArrayOf())).SetName("ApplyRangeArrayInt32");

            uint[] uint32Src = [10, 20, 30, 40];
            uint[] uint32Exp = [uint32Src[1], uint32Src[2]];
            yield return new TestCaseData(
                Variant.From(uint32Src.ToArrayOf()),
                Variant.From(uint32Exp.ToArrayOf())).SetName("ApplyRangeArrayUInt32");

            long[] int64Src = [10, 20, 30, 40];
            long[] int64Exp = [int64Src[1], int64Src[2]];
            yield return new TestCaseData(
                Variant.From(int64Src.ToArrayOf()),
                Variant.From(int64Exp.ToArrayOf())).SetName("ApplyRangeArrayInt64");

            ulong[] uint64Src = [10, 20, 30, 40];
            ulong[] uint64Exp = [uint64Src[1], uint64Src[2]];
            yield return new TestCaseData(
                Variant.From(uint64Src.ToArrayOf()),
                Variant.From(uint64Exp.ToArrayOf())).SetName("ApplyRangeArrayUInt64");

            float[] floatSrc = [1.5f, 2.5f, 3.5f, 4.5f];
            float[] floatExp = [floatSrc[1], floatSrc[2]];
            yield return new TestCaseData(
                Variant.From(floatSrc.ToArrayOf()),
                Variant.From(floatExp.ToArrayOf())).SetName("ApplyRangeArrayFloat");

            double[] doubleSrc = [1.5, 2.5, 3.5, 4.5];
            double[] doubleExp = [doubleSrc[1], doubleSrc[2]];
            yield return new TestCaseData(
                Variant.From(doubleSrc.ToArrayOf()),
                Variant.From(doubleExp.ToArrayOf())).SetName("ApplyRangeArrayDouble");

            string[] stringSrc = ["a", "b", "c", "d"];
            string[] stringExp = [stringSrc[1], stringSrc[2]];
            yield return new TestCaseData(
                Variant.From(stringSrc.ToArrayOf()),
                Variant.From(stringExp.ToArrayOf())).SetName("ApplyRangeArrayString");

            DateTimeUtc[] dateSrc = [Dt(1), Dt(2), Dt(3), Dt(4)];
            DateTimeUtc[] dateExp = [dateSrc[1], dateSrc[2]];
            yield return new TestCaseData(
                Variant.From(dateSrc.ToArrayOf()),
                Variant.From(dateExp.ToArrayOf())).SetName("ApplyRangeArrayDateTime");

            Uuid[] guidSrc = [Uid(1), Uid(2), Uid(3), Uid(4)];
            Uuid[] guidExp = [guidSrc[1], guidSrc[2]];
            yield return new TestCaseData(
                Variant.From(guidSrc.ToArrayOf()),
                Variant.From(guidExp.ToArrayOf())).SetName("ApplyRangeArrayGuid");

            ByteString[] byteStringSrc =
            [
                ByteString.From(1),
                ByteString.From(2),
                ByteString.From(3),
                ByteString.From(4)
            ];
            ByteString[] byteStringExp = [byteStringSrc[1], byteStringSrc[2]];
            yield return new TestCaseData(
                Variant.From(byteStringSrc.ToArrayOf()),
                Variant.From(byteStringExp.ToArrayOf())).SetName("ApplyRangeArrayByteString");

            XmlElement[] xmlSrc =
            [
                XmlElement.From("<a>1</a>"),
                XmlElement.From("<a>2</a>"),
                XmlElement.From("<a>3</a>"),
                XmlElement.From("<a>4</a>")
            ];
            XmlElement[] xmlExp = [xmlSrc[1], xmlSrc[2]];
            yield return new TestCaseData(
                Variant.From(xmlSrc.ToArrayOf()),
                Variant.From(xmlExp.ToArrayOf())).SetName("ApplyRangeArrayXmlElement");

            NodeId[] nodeSrc = [new NodeId(1), new NodeId(2), new NodeId(3), new NodeId(4)];
            NodeId[] nodeExp = [nodeSrc[1], nodeSrc[2]];
            yield return new TestCaseData(
                Variant.From(nodeSrc.ToArrayOf()),
                Variant.From(nodeExp.ToArrayOf())).SetName("ApplyRangeArrayNodeId");

            ExpandedNodeId[] expandedSrc = [ENId("A"), ENId("B"), ENId("C"), ENId("D")];
            ExpandedNodeId[] expandedExp = [expandedSrc[1], expandedSrc[2]];
            yield return new TestCaseData(
                Variant.From(expandedSrc.ToArrayOf()),
                Variant.From(expandedExp.ToArrayOf())).SetName("ApplyRangeArrayExpandedNodeId");

            StatusCode[] statusSrc =
            [
                new StatusCode(1u),
                new StatusCode(2u),
                new StatusCode(3u),
                new StatusCode(4u)
            ];
            StatusCode[] statusExp = [statusSrc[1], statusSrc[2]];
            yield return new TestCaseData(
                Variant.From(statusSrc.ToArrayOf()),
                Variant.From(statusExp.ToArrayOf())).SetName("ApplyRangeArrayStatusCode");

            QualifiedName[] qualifiedSrc =
            [
                new QualifiedName("a", 1),
                new QualifiedName("b", 1),
                new QualifiedName("c", 1),
                new QualifiedName("d", 1)
            ];
            QualifiedName[] qualifiedExp = [qualifiedSrc[1], qualifiedSrc[2]];
            yield return new TestCaseData(
                Variant.From(qualifiedSrc.ToArrayOf()),
                Variant.From(qualifiedExp.ToArrayOf())).SetName("ApplyRangeArrayQualifiedName");

            LocalizedText[] localizedSrc =
            [
                new LocalizedText("en", "a"),
                new LocalizedText("en", "b"),
                new LocalizedText("en", "c"),
                new LocalizedText("en", "d")
            ];
            LocalizedText[] localizedExp = [localizedSrc[1], localizedSrc[2]];
            yield return new TestCaseData(
                Variant.From(localizedSrc.ToArrayOf()),
                Variant.From(localizedExp.ToArrayOf())).SetName("ApplyRangeArrayLocalizedText");

            ExtensionObject[] extensionSrc =
            [
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            ExtensionObject[] extensionExp = [extensionSrc[1], extensionSrc[2]];
            yield return new TestCaseData(
                Variant.From(extensionSrc.ToArrayOf()),
                Variant.From(extensionExp.ToArrayOf())).SetName("ApplyRangeArrayExtensionObject");

            DataValue[] dataValueSrc =
            [
                new DataValue(1),
                new DataValue(2),
                new DataValue(3),
                new DataValue(4)
            ];
            DataValue[] dataValueExp = [dataValueSrc[1], dataValueSrc[2]];
            yield return new TestCaseData(
                Variant.From(dataValueSrc.ToArrayOf()),
                Variant.From(dataValueExp.ToArrayOf())).SetName("ApplyRangeArrayDataValue");

            EnumValue[] enumSrc =
            [
                new EnumValue(1),
                new EnumValue(2),
                new EnumValue(3),
                new EnumValue(4)
            ];
            EnumValue[] enumExp = [enumSrc[1], enumSrc[2]];
            yield return new TestCaseData(
                Variant.From(enumSrc.ToArrayOf()),
                Variant.From(enumExp.ToArrayOf())).SetName("ApplyRangeArrayEnumeration");

            Variant[] variantSrc =
            [
                new Variant(10),
                new Variant("two"),
                new Variant(30),
                new Variant("four")
            ];
            Variant[] variantExp = [variantSrc[1], variantSrc[2]];
            yield return new TestCaseData(
                Variant.From(variantSrc.ToArrayOf()),
                Variant.From(variantExp.ToArrayOf())).SetName("ApplyRangeArrayVariant");
        }

        private static IEnumerable<TestCaseData> ApplyRangeMatrixCases()
        {
            bool[,] boolSrc = new bool[,] { { true, false }, { true, true } };
            bool[,] boolExp = new bool[,] { { boolSrc[0, 0], boolSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(boolSrc.ToMatrixOf()),
                Variant.From(boolExp.ToMatrixOf())).SetName("ApplyRangeMatrixBoolean");

            sbyte[,] sbyteSrc = new sbyte[,] { { 10, 20 }, { 30, 40 } };
            sbyte[,] sbyteExp = new sbyte[,] { { sbyteSrc[0, 0], sbyteSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(sbyteSrc.ToMatrixOf()),
                Variant.From(sbyteExp.ToMatrixOf())).SetName("ApplyRangeMatrixSByte");

            byte[,] byteSrc = new byte[,] { { 10, 20 }, { 30, 40 } };
            byte[,] byteExp = new byte[,] { { byteSrc[0, 0], byteSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(byteSrc.ToMatrixOf()),
                Variant.From(byteExp.ToMatrixOf())).SetName("ApplyRangeMatrixByte");

            short[,] int16Src = new short[,] { { 10, 20 }, { 30, 40 } };
            short[,] int16Exp = new short[,] { { int16Src[0, 0], int16Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(int16Src.ToMatrixOf()),
                Variant.From(int16Exp.ToMatrixOf())).SetName("ApplyRangeMatrixInt16");

            ushort[,] uint16Src = new ushort[,] { { 10, 20 }, { 30, 40 } };
            ushort[,] uint16Exp = new ushort[,] { { uint16Src[0, 0], uint16Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(uint16Src.ToMatrixOf()),
                Variant.From(uint16Exp.ToMatrixOf())).SetName("ApplyRangeMatrixUInt16");

            int[,] int32Src = new int[,] { { 10, 20 }, { 30, 40 } };
            int[,] int32Exp = new int[,] { { int32Src[0, 0], int32Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(int32Src.ToMatrixOf()),
                Variant.From(int32Exp.ToMatrixOf())).SetName("ApplyRangeMatrixInt32");

            uint[,] uint32Src = new uint[,] { { 10, 20 }, { 30, 40 } };
            uint[,] uint32Exp = new uint[,] { { uint32Src[0, 0], uint32Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(uint32Src.ToMatrixOf()),
                Variant.From(uint32Exp.ToMatrixOf())).SetName("ApplyRangeMatrixUInt32");

            long[,] int64Src = new long[,] { { 10, 20 }, { 30, 40 } };
            long[,] int64Exp = new long[,] { { int64Src[0, 0], int64Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(int64Src.ToMatrixOf()),
                Variant.From(int64Exp.ToMatrixOf())).SetName("ApplyRangeMatrixInt64");

            ulong[,] uint64Src = new ulong[,] { { 10, 20 }, { 30, 40 } };
            ulong[,] uint64Exp = new ulong[,] { { uint64Src[0, 0], uint64Src[0, 1] } };
            yield return new TestCaseData(
                Variant.From(uint64Src.ToMatrixOf()),
                Variant.From(uint64Exp.ToMatrixOf())).SetName("ApplyRangeMatrixUInt64");

            float[,] floatSrc = new float[,] { { 1.5f, 2.5f }, { 3.5f, 4.5f } };
            float[,] floatExp = new float[,] { { floatSrc[0, 0], floatSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(floatSrc.ToMatrixOf()),
                Variant.From(floatExp.ToMatrixOf())).SetName("ApplyRangeMatrixFloat");

            double[,] doubleSrc = new double[,] { { 1.5, 2.5 }, { 3.5, 4.5 } };
            double[,] doubleExp = new double[,] { { doubleSrc[0, 0], doubleSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(doubleSrc.ToMatrixOf()),
                Variant.From(doubleExp.ToMatrixOf())).SetName("ApplyRangeMatrixDouble");

            string[,] stringSrc = new string[,] { { "a", "b" }, { "c", "d" } };
            string[,] stringExp = new string[,] { { stringSrc[0, 0], stringSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(stringSrc.ToMatrixOf()),
                Variant.From(stringExp.ToMatrixOf())).SetName("ApplyRangeMatrixString");

            DateTimeUtc[,] dateSrc = new DateTimeUtc[,] { { Dt(1), Dt(2) }, { Dt(3), Dt(4) } };
            DateTimeUtc[,] dateExp = new DateTimeUtc[,] { { dateSrc[0, 0], dateSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(dateSrc.ToMatrixOf()),
                Variant.From(dateExp.ToMatrixOf())).SetName("ApplyRangeMatrixDateTime");

            Uuid[,] guidSrc = new Uuid[,] { { Uid(1), Uid(2) }, { Uid(3), Uid(4) } };
            Uuid[,] guidExp = new Uuid[,] { { guidSrc[0, 0], guidSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(guidSrc.ToMatrixOf()),
                Variant.From(guidExp.ToMatrixOf())).SetName("ApplyRangeMatrixGuid");

            ByteString[,] byteStringSrc = new ByteString[,]
            {
                { ByteString.From(1), ByteString.From(2) },
                { ByteString.From(3), ByteString.From(4) }
            };
            ByteString[,] byteStringExp = new ByteString[,]
            {
                { byteStringSrc[0, 0], byteStringSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(byteStringSrc.ToMatrixOf()),
                Variant.From(byteStringExp.ToMatrixOf())).SetName("ApplyRangeMatrixByteString");

            XmlElement[,] xmlSrc = new XmlElement[,]
            {
                { XmlElement.From("<a>1</a>"), XmlElement.From("<a>2</a>") },
                { XmlElement.From("<a>3</a>"), XmlElement.From("<a>4</a>") }
            };
            XmlElement[,] xmlExp = new XmlElement[,] { { xmlSrc[0, 0], xmlSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(xmlSrc.ToMatrixOf()),
                Variant.From(xmlExp.ToMatrixOf())).SetName("ApplyRangeMatrixXmlElement");

            NodeId[,] nodeSrc = new NodeId[,]
            {
                { new NodeId(1), new NodeId(2) },
                { new NodeId(3), new NodeId(4) }
            };
            NodeId[,] nodeExp = new NodeId[,] { { nodeSrc[0, 0], nodeSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(nodeSrc.ToMatrixOf()),
                Variant.From(nodeExp.ToMatrixOf())).SetName("ApplyRangeMatrixNodeId");

            ExpandedNodeId[,] expandedSrc = new ExpandedNodeId[,]
            {
                { ENId("A"), ENId("B") },
                { ENId("C"), ENId("D") }
            };
            ExpandedNodeId[,] expandedExp = new ExpandedNodeId[,]
            {
                { expandedSrc[0, 0], expandedSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(expandedSrc.ToMatrixOf()),
                Variant.From(expandedExp.ToMatrixOf())).SetName("ApplyRangeMatrixExpandedNodeId");

            StatusCode[,] statusSrc = new StatusCode[,]
            {
                { new StatusCode(1u), new StatusCode(2u) },
                { new StatusCode(3u), new StatusCode(4u) }
            };
            StatusCode[,] statusExp = new StatusCode[,] { { statusSrc[0, 0], statusSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(statusSrc.ToMatrixOf()),
                Variant.From(statusExp.ToMatrixOf())).SetName("ApplyRangeMatrixStatusCode");

            QualifiedName[,] qualifiedSrc = new QualifiedName[,]
            {
                { new QualifiedName("a", 1), new QualifiedName("b", 1) },
                { new QualifiedName("c", 1), new QualifiedName("d", 1) }
            };
            QualifiedName[,] qualifiedExp = new QualifiedName[,]
            {
                { qualifiedSrc[0, 0], qualifiedSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(qualifiedSrc.ToMatrixOf()),
                Variant.From(qualifiedExp.ToMatrixOf())).SetName("ApplyRangeMatrixQualifiedName");

            LocalizedText[,] localizedSrc = new LocalizedText[,]
            {
                { new LocalizedText("en", "a"), new LocalizedText("en", "b") },
                { new LocalizedText("en", "c"), new LocalizedText("en", "d") }
            };
            LocalizedText[,] localizedExp = new LocalizedText[,]
            {
                { localizedSrc[0, 0], localizedSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(localizedSrc.ToMatrixOf()),
                Variant.From(localizedExp.ToMatrixOf())).SetName("ApplyRangeMatrixLocalizedText");

            ExtensionObject[,] extensionSrc = new ExtensionObject[,]
            {
                { new ExtensionObject(new Argument()), new ExtensionObject(new Argument()) },
                { new ExtensionObject(new Argument()), new ExtensionObject(new Argument()) }
            };
            ExtensionObject[,] extensionExp = new ExtensionObject[,]
            {
                { extensionSrc[0, 0], extensionSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(extensionSrc.ToMatrixOf()),
                Variant.From(extensionExp.ToMatrixOf())).SetName("ApplyRangeMatrixExtensionObject");

            DataValue[,] dataValueSrc = new DataValue[,]
            {
                { new DataValue(1), new DataValue(2) },
                { new DataValue(3), new DataValue(4) }
            };
            DataValue[,] dataValueExp = new DataValue[,]
            {
                { dataValueSrc[0, 0], dataValueSrc[0, 1] }
            };
            yield return new TestCaseData(
                Variant.From(dataValueSrc.ToMatrixOf()),
                Variant.From(dataValueExp.ToMatrixOf())).SetName("ApplyRangeMatrixDataValue");

            EnumValue[,] enumSrc = new EnumValue[,]
            {
                { new EnumValue(1), new EnumValue(2) },
                { new EnumValue(3), new EnumValue(4) }
            };
            EnumValue[,] enumExp = new EnumValue[,] { { enumSrc[0, 0], enumSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(enumSrc.ToMatrixOf()),
                Variant.From(enumExp.ToMatrixOf())).SetName("ApplyRangeMatrixEnumeration");

            Variant[,] variantSrc = new Variant[,]
            {
                { new Variant(10), new Variant("two") },
                { new Variant(30), new Variant("four") }
            };
            Variant[,] variantExp = new Variant[,] { { variantSrc[0, 0], variantSrc[0, 1] } };
            yield return new TestCaseData(
                Variant.From(variantSrc.ToMatrixOf()),
                Variant.From(variantExp.ToMatrixOf())).SetName("ApplyRangeMatrixVariant");
        }

        private static IEnumerable<TestCaseData> UpdateRangeArrayCases()
        {
            bool[] boolDst = [true, true, true, true];
            bool[] boolSlice = [false, false];
            yield return new TestCaseData(
                Variant.From(boolDst.ToArrayOf()),
                Variant.From(boolSlice.ToArrayOf()),
                Variant.From(((bool[])[boolDst[0], boolSlice[0], boolSlice[1], boolDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayBoolean");

            sbyte[] sbyteDst = [10, 20, 30, 40];
            sbyte[] sbyteSlice = [77, 88];
            yield return new TestCaseData(
                Variant.From(sbyteDst.ToArrayOf()),
                Variant.From(sbyteSlice.ToArrayOf()),
                Variant.From(((sbyte[])[sbyteDst[0], sbyteSlice[0], sbyteSlice[1], sbyteDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArraySByte");

            byte[] byteDst = [10, 20, 30, 40];
            byte[] byteSlice = [77, 88];
            yield return new TestCaseData(
                Variant.From(byteDst.ToArrayOf()),
                Variant.From(byteSlice.ToArrayOf()),
                Variant.From(((byte[])[byteDst[0], byteSlice[0], byteSlice[1], byteDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayByte");

            short[] int16Dst = [10, 20, 30, 40];
            short[] int16Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(int16Dst.ToArrayOf()),
                Variant.From(int16Slice.ToArrayOf()),
                Variant.From(((short[])[int16Dst[0], int16Slice[0], int16Slice[1], int16Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayInt16");

            ushort[] uint16Dst = [10, 20, 30, 40];
            ushort[] uint16Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(uint16Dst.ToArrayOf()),
                Variant.From(uint16Slice.ToArrayOf()),
                Variant.From(((ushort[])[uint16Dst[0], uint16Slice[0], uint16Slice[1], uint16Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayUInt16");

            int[] int32Dst = [10, 20, 30, 40];
            int[] int32Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(int32Dst.ToArrayOf()),
                Variant.From(int32Slice.ToArrayOf()),
                Variant.From(((int[])[int32Dst[0], int32Slice[0], int32Slice[1], int32Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayInt32");

            uint[] uint32Dst = [10, 20, 30, 40];
            uint[] uint32Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(uint32Dst.ToArrayOf()),
                Variant.From(uint32Slice.ToArrayOf()),
                Variant.From(((uint[])[uint32Dst[0], uint32Slice[0], uint32Slice[1], uint32Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayUInt32");

            long[] int64Dst = [10, 20, 30, 40];
            long[] int64Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(int64Dst.ToArrayOf()),
                Variant.From(int64Slice.ToArrayOf()),
                Variant.From(((long[])[int64Dst[0], int64Slice[0], int64Slice[1], int64Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayInt64");

            ulong[] uint64Dst = [10, 20, 30, 40];
            ulong[] uint64Slice = [77, 88];
            yield return new TestCaseData(
                Variant.From(uint64Dst.ToArrayOf()),
                Variant.From(uint64Slice.ToArrayOf()),
                Variant.From(((ulong[])[uint64Dst[0], uint64Slice[0], uint64Slice[1], uint64Dst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayUInt64");

            float[] floatDst = [1.5f, 2.5f, 3.5f, 4.5f];
            float[] floatSlice = [7.5f, 8.5f];
            yield return new TestCaseData(
                Variant.From(floatDst.ToArrayOf()),
                Variant.From(floatSlice.ToArrayOf()),
                Variant.From(((float[])[floatDst[0], floatSlice[0], floatSlice[1], floatDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayFloat");

            double[] doubleDst = [1.5, 2.5, 3.5, 4.5];
            double[] doubleSlice = [7.5, 8.5];
            yield return new TestCaseData(
                Variant.From(doubleDst.ToArrayOf()),
                Variant.From(doubleSlice.ToArrayOf()),
                Variant.From(((double[])[doubleDst[0], doubleSlice[0], doubleSlice[1], doubleDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayDouble");

            string[] stringDst = ["a", "b", "c", "d"];
            string[] stringSlice = ["X", "Y"];
            yield return new TestCaseData(
                Variant.From(stringDst.ToArrayOf()),
                Variant.From(stringSlice.ToArrayOf()),
                Variant.From(((string[])[stringDst[0], stringSlice[0], stringSlice[1], stringDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayString");

            DateTimeUtc[] dateDst = [Dt(1), Dt(2), Dt(3), Dt(4)];
            DateTimeUtc[] dateSlice = [Dt(7), Dt(8)];
            yield return new TestCaseData(
                Variant.From(dateDst.ToArrayOf()),
                Variant.From(dateSlice.ToArrayOf()),
                Variant.From(((DateTimeUtc[])[dateDst[0], dateSlice[0], dateSlice[1], dateDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayDateTime");

            Uuid[] guidDst = [Uid(1), Uid(2), Uid(3), Uid(4)];
            Uuid[] guidSlice = [Uid(7), Uid(8)];
            yield return new TestCaseData(
                Variant.From(guidDst.ToArrayOf()),
                Variant.From(guidSlice.ToArrayOf()),
                Variant.From(((Uuid[])[guidDst[0], guidSlice[0], guidSlice[1], guidDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayGuid");

            ByteString[] byteStringDst =
            [
                ByteString.From(1),
                ByteString.From(2),
                ByteString.From(3),
                ByteString.From(4)
            ];
            ByteString[] byteStringSlice = [ByteString.From(77), ByteString.From(88)];
            yield return new TestCaseData(
                Variant.From(byteStringDst.ToArrayOf()),
                Variant.From(byteStringSlice.ToArrayOf()),
                Variant.From(((ByteString[])
                    [byteStringDst[0], byteStringSlice[0], byteStringSlice[1], byteStringDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayByteString");

            NodeId[] nodeDst = [new NodeId(1), new NodeId(2), new NodeId(3), new NodeId(4)];
            NodeId[] nodeSlice = [new NodeId(77), new NodeId(88)];
            yield return new TestCaseData(
                Variant.From(nodeDst.ToArrayOf()),
                Variant.From(nodeSlice.ToArrayOf()),
                Variant.From(((NodeId[])[nodeDst[0], nodeSlice[0], nodeSlice[1], nodeDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayNodeId");

            StatusCode[] statusDst =
            [
                new StatusCode(1u),
                new StatusCode(2u),
                new StatusCode(3u),
                new StatusCode(4u)
            ];
            StatusCode[] statusSlice = [new StatusCode(77u), new StatusCode(88u)];
            yield return new TestCaseData(
                Variant.From(statusDst.ToArrayOf()),
                Variant.From(statusSlice.ToArrayOf()),
                Variant.From(((StatusCode[])
                    [statusDst[0], statusSlice[0], statusSlice[1], statusDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayStatusCode");

            LocalizedText[] localizedDst =
            [
                new LocalizedText("en", "a"),
                new LocalizedText("en", "b"),
                new LocalizedText("en", "c"),
                new LocalizedText("en", "d")
            ];
            LocalizedText[] localizedSlice =
            [
                new LocalizedText("en", "X"),
                new LocalizedText("en", "Y")
            ];
            yield return new TestCaseData(
                Variant.From(localizedDst.ToArrayOf()),
                Variant.From(localizedSlice.ToArrayOf()),
                Variant.From(((LocalizedText[])
                    [localizedDst[0], localizedSlice[0], localizedSlice[1], localizedDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayLocalizedText");

            EnumValue[] enumDst =
            [
                new EnumValue(1),
                new EnumValue(2),
                new EnumValue(3),
                new EnumValue(4)
            ];
            EnumValue[] enumSlice = [new EnumValue(77), new EnumValue(88)];
            yield return new TestCaseData(
                Variant.From(enumDst.ToArrayOf()),
                Variant.From(enumSlice.ToArrayOf()),
                Variant.From(((EnumValue[])[enumDst[0], enumSlice[0], enumSlice[1], enumDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayEnumeration");

            Variant[] variantDst =
            [
                new Variant(10),
                new Variant("two"),
                new Variant(30),
                new Variant("four")
            ];
            Variant[] variantSlice = [new Variant(77), new Variant("eighty")];
            yield return new TestCaseData(
                Variant.From(variantDst.ToArrayOf()),
                Variant.From(variantSlice.ToArrayOf()),
                Variant.From(((Variant[])
                    [variantDst[0], variantSlice[0], variantSlice[1], variantDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayVariant");

            XmlElement[] xmlDst =
            [
                XmlElement.From("<a>1</a>"),
                XmlElement.From("<a>2</a>"),
                XmlElement.From("<a>3</a>"),
                XmlElement.From("<a>4</a>")
            ];
            XmlElement[] xmlSlice = [XmlElement.From("<a>77</a>"), XmlElement.From("<a>88</a>")];
            yield return new TestCaseData(
                Variant.From(xmlDst.ToArrayOf()),
                Variant.From(xmlSlice.ToArrayOf()),
                Variant.From(((XmlElement[])[xmlDst[0], xmlSlice[0], xmlSlice[1], xmlDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayXmlElement");

            ExpandedNodeId[] expandedDst = [ENId("A"), ENId("B"), ENId("C"), ENId("D")];
            ExpandedNodeId[] expandedSlice = [ENId("X"), ENId("Y")];
            yield return new TestCaseData(
                Variant.From(expandedDst.ToArrayOf()),
                Variant.From(expandedSlice.ToArrayOf()),
                Variant.From(((ExpandedNodeId[])
                    [expandedDst[0], expandedSlice[0], expandedSlice[1], expandedDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayExpandedNodeId");

            QualifiedName[] qualifiedDst =
            [
                new QualifiedName("a", 1),
                new QualifiedName("b", 1),
                new QualifiedName("c", 1),
                new QualifiedName("d", 1)
            ];
            QualifiedName[] qualifiedSlice = [new QualifiedName("x", 1), new QualifiedName("y", 1)];
            yield return new TestCaseData(
                Variant.From(qualifiedDst.ToArrayOf()),
                Variant.From(qualifiedSlice.ToArrayOf()),
                Variant.From(((QualifiedName[])
                    [qualifiedDst[0], qualifiedSlice[0], qualifiedSlice[1], qualifiedDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayQualifiedName");

            ExtensionObject[] extensionDst =
            [
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            ExtensionObject[] extensionSlice =
            [
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            yield return new TestCaseData(
                Variant.From(extensionDst.ToArrayOf()),
                Variant.From(extensionSlice.ToArrayOf()),
                Variant.From(((ExtensionObject[])
                    [extensionDst[0], extensionSlice[0], extensionSlice[1], extensionDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayExtensionObject");

            DataValue[] dataValueDst =
            [
                new DataValue(1),
                new DataValue(2),
                new DataValue(3),
                new DataValue(4)
            ];
            DataValue[] dataValueSlice = [new DataValue(77), new DataValue(88)];
            yield return new TestCaseData(
                Variant.From(dataValueDst.ToArrayOf()),
                Variant.From(dataValueSlice.ToArrayOf()),
                Variant.From(((DataValue[])
                    [dataValueDst[0], dataValueSlice[0], dataValueSlice[1], dataValueDst[3]])
                    .ToArrayOf())).SetName("UpdateRangeArrayDataValue");
        }

        private static IEnumerable<TestCaseData> UpdateRangeMatrixCases()
        {
            int[,] int32Dst = new int[,] { { 10, 20 }, { 30, 40 } };
            int[,] int32Slice = new int[,] { { 77, 88 } };
            int[,] int32Exp = new int[,] { { 77, 88 }, { 30, 40 } };
            yield return new TestCaseData(
                Variant.From(int32Dst.ToMatrixOf()),
                Variant.From(int32Slice.ToMatrixOf()),
                Variant.From(int32Exp.ToMatrixOf())).SetName("UpdateRangeMatrixInt32");

            double[,] doubleDst = new double[,] { { 1.5, 2.5 }, { 3.5, 4.5 } };
            double[,] doubleSlice = new double[,] { { 7.5, 8.5 } };
            double[,] doubleExp = new double[,] { { 7.5, 8.5 }, { 3.5, 4.5 } };
            yield return new TestCaseData(
                Variant.From(doubleDst.ToMatrixOf()),
                Variant.From(doubleSlice.ToMatrixOf()),
                Variant.From(doubleExp.ToMatrixOf())).SetName("UpdateRangeMatrixDouble");

            string[,] stringDst = new string[,] { { "a", "b" }, { "c", "d" } };
            string[,] stringSlice = new string[,] { { "X", "Y" } };
            string[,] stringExp = new string[,] { { "X", "Y" }, { "c", "d" } };
            yield return new TestCaseData(
                Variant.From(stringDst.ToMatrixOf()),
                Variant.From(stringSlice.ToMatrixOf()),
                Variant.From(stringExp.ToMatrixOf())).SetName("UpdateRangeMatrixString");

            NodeId[,] nodeDst = new NodeId[,]
            {
                { new NodeId(10), new NodeId(20) },
                { new NodeId(30), new NodeId(40) }
            };
            NodeId[,] nodeSlice = new NodeId[,] { { new NodeId(77), new NodeId(88) } };
            NodeId[,] nodeExp = new NodeId[,]
            {
                { nodeSlice[0, 0], nodeSlice[0, 1] },
                { nodeDst[1, 0], nodeDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(nodeDst.ToMatrixOf()),
                Variant.From(nodeSlice.ToMatrixOf()),
                Variant.From(nodeExp.ToMatrixOf())).SetName("UpdateRangeMatrixNodeId");

            bool[,] boolDst = new bool[,] { { true, false }, { true, false } };
            bool[,] boolSlice = new bool[,] { { false, true } };
            bool[,] boolExp = new bool[,]
            {
                { boolSlice[0, 0], boolSlice[0, 1] },
                { boolDst[1, 0], boolDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(boolDst.ToMatrixOf()),
                Variant.From(boolSlice.ToMatrixOf()),
                Variant.From(boolExp.ToMatrixOf())).SetName("UpdateRangeMatrixBoolean");

            sbyte[,] sbyteDst = new sbyte[,] { { 10, 20 }, { 30, 40 } };
            sbyte[,] sbyteSlice = new sbyte[,] { { 77, 88 } };
            sbyte[,] sbyteExp = new sbyte[,]
            {
                { sbyteSlice[0, 0], sbyteSlice[0, 1] },
                { sbyteDst[1, 0], sbyteDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(sbyteDst.ToMatrixOf()),
                Variant.From(sbyteSlice.ToMatrixOf()),
                Variant.From(sbyteExp.ToMatrixOf())).SetName("UpdateRangeMatrixSByte");

            byte[,] byteDst = new byte[,] { { 10, 20 }, { 30, 40 } };
            byte[,] byteSlice = new byte[,] { { 77, 88 } };
            byte[,] byteExp = new byte[,]
            {
                { byteSlice[0, 0], byteSlice[0, 1] },
                { byteDst[1, 0], byteDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(byteDst.ToMatrixOf()),
                Variant.From(byteSlice.ToMatrixOf()),
                Variant.From(byteExp.ToMatrixOf())).SetName("UpdateRangeMatrixByte");

            short[,] int16Dst = new short[,] { { 10, 20 }, { 30, 40 } };
            short[,] int16Slice = new short[,] { { 77, 88 } };
            short[,] int16Exp = new short[,]
            {
                { int16Slice[0, 0], int16Slice[0, 1] },
                { int16Dst[1, 0], int16Dst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(int16Dst.ToMatrixOf()),
                Variant.From(int16Slice.ToMatrixOf()),
                Variant.From(int16Exp.ToMatrixOf())).SetName("UpdateRangeMatrixInt16");

            ushort[,] uint16Dst = new ushort[,] { { 10, 20 }, { 30, 40 } };
            ushort[,] uint16Slice = new ushort[,] { { 77, 88 } };
            ushort[,] uint16Exp = new ushort[,]
            {
                { uint16Slice[0, 0], uint16Slice[0, 1] },
                { uint16Dst[1, 0], uint16Dst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(uint16Dst.ToMatrixOf()),
                Variant.From(uint16Slice.ToMatrixOf()),
                Variant.From(uint16Exp.ToMatrixOf())).SetName("UpdateRangeMatrixUInt16");

            uint[,] uint32Dst = new uint[,] { { 10u, 20u }, { 30u, 40u } };
            uint[,] uint32Slice = new uint[,] { { 77u, 88u } };
            uint[,] uint32Exp = new uint[,]
            {
                { uint32Slice[0, 0], uint32Slice[0, 1] },
                { uint32Dst[1, 0], uint32Dst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(uint32Dst.ToMatrixOf()),
                Variant.From(uint32Slice.ToMatrixOf()),
                Variant.From(uint32Exp.ToMatrixOf())).SetName("UpdateRangeMatrixUInt32");

            long[,] int64Dst = new long[,] { { 10L, 20L }, { 30L, 40L } };
            long[,] int64Slice = new long[,] { { 77L, 88L } };
            long[,] int64Exp = new long[,]
            {
                { int64Slice[0, 0], int64Slice[0, 1] },
                { int64Dst[1, 0], int64Dst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(int64Dst.ToMatrixOf()),
                Variant.From(int64Slice.ToMatrixOf()),
                Variant.From(int64Exp.ToMatrixOf())).SetName("UpdateRangeMatrixInt64");

            ulong[,] uint64Dst = new ulong[,] { { 10ul, 20ul }, { 30ul, 40ul } };
            ulong[,] uint64Slice = new ulong[,] { { 77ul, 88ul } };
            ulong[,] uint64Exp = new ulong[,]
            {
                { uint64Slice[0, 0], uint64Slice[0, 1] },
                { uint64Dst[1, 0], uint64Dst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(uint64Dst.ToMatrixOf()),
                Variant.From(uint64Slice.ToMatrixOf()),
                Variant.From(uint64Exp.ToMatrixOf())).SetName("UpdateRangeMatrixUInt64");

            float[,] floatDst = new float[,] { { 1.5f, 2.5f }, { 3.5f, 4.5f } };
            float[,] floatSlice = new float[,] { { 7.5f, 8.5f } };
            float[,] floatExp = new float[,]
            {
                { floatSlice[0, 0], floatSlice[0, 1] },
                { floatDst[1, 0], floatDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(floatDst.ToMatrixOf()),
                Variant.From(floatSlice.ToMatrixOf()),
                Variant.From(floatExp.ToMatrixOf())).SetName("UpdateRangeMatrixFloat");

            DateTimeUtc[,] dateDst = new DateTimeUtc[,] { { Dt(1), Dt(2) }, { Dt(3), Dt(4) } };
            DateTimeUtc[,] dateSlice = new DateTimeUtc[,] { { Dt(7), Dt(8) } };
            DateTimeUtc[,] dateExp = new DateTimeUtc[,]
            {
                { dateSlice[0, 0], dateSlice[0, 1] },
                { dateDst[1, 0], dateDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(dateDst.ToMatrixOf()),
                Variant.From(dateSlice.ToMatrixOf()),
                Variant.From(dateExp.ToMatrixOf())).SetName("UpdateRangeMatrixDateTime");

            Uuid[,] guidDst = new Uuid[,] { { Uid(1), Uid(2) }, { Uid(3), Uid(4) } };
            Uuid[,] guidSlice = new Uuid[,] { { Uid(7), Uid(8) } };
            Uuid[,] guidExp = new Uuid[,]
            {
                { guidSlice[0, 0], guidSlice[0, 1] },
                { guidDst[1, 0], guidDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(guidDst.ToMatrixOf()),
                Variant.From(guidSlice.ToMatrixOf()),
                Variant.From(guidExp.ToMatrixOf())).SetName("UpdateRangeMatrixGuid");

            ByteString[,] byteStringDst = new ByteString[,]
            {
                { ByteString.From(1), ByteString.From(2) },
                { ByteString.From(3), ByteString.From(4) }
            };
            ByteString[,] byteStringSlice = new ByteString[,]
            {
                { ByteString.From(77), ByteString.From(88) }
            };
            ByteString[,] byteStringExp = new ByteString[,]
            {
                { byteStringSlice[0, 0], byteStringSlice[0, 1] },
                { byteStringDst[1, 0], byteStringDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(byteStringDst.ToMatrixOf()),
                Variant.From(byteStringSlice.ToMatrixOf()),
                Variant.From(byteStringExp.ToMatrixOf())).SetName("UpdateRangeMatrixByteString");

            XmlElement[,] xmlDst = new XmlElement[,]
            {
                { XmlElement.From("<a>1</a>"), XmlElement.From("<a>2</a>") },
                { XmlElement.From("<a>3</a>"), XmlElement.From("<a>4</a>") }
            };
            XmlElement[,] xmlSlice = new XmlElement[,]
            {
                { XmlElement.From("<a>77</a>"), XmlElement.From("<a>88</a>") }
            };
            XmlElement[,] xmlExp = new XmlElement[,]
            {
                { xmlSlice[0, 0], xmlSlice[0, 1] },
                { xmlDst[1, 0], xmlDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(xmlDst.ToMatrixOf()),
                Variant.From(xmlSlice.ToMatrixOf()),
                Variant.From(xmlExp.ToMatrixOf())).SetName("UpdateRangeMatrixXmlElement");

            ExpandedNodeId[,] expandedDst = new ExpandedNodeId[,]
            {
                { ENId("A"), ENId("B") },
                { ENId("C"), ENId("D") }
            };
            ExpandedNodeId[,] expandedSlice = new ExpandedNodeId[,] { { ENId("X"), ENId("Y") } };
            ExpandedNodeId[,] expandedExp = new ExpandedNodeId[,]
            {
                { expandedSlice[0, 0], expandedSlice[0, 1] },
                { expandedDst[1, 0], expandedDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(expandedDst.ToMatrixOf()),
                Variant.From(expandedSlice.ToMatrixOf()),
                Variant.From(expandedExp.ToMatrixOf())).SetName("UpdateRangeMatrixExpandedNodeId");

            StatusCode[,] statusDst = new StatusCode[,]
            {
                { new StatusCode(1u), new StatusCode(2u) },
                { new StatusCode(3u), new StatusCode(4u) }
            };
            StatusCode[,] statusSlice = new StatusCode[,]
            {
                { new StatusCode(77u), new StatusCode(88u) }
            };
            StatusCode[,] statusExp = new StatusCode[,]
            {
                { statusSlice[0, 0], statusSlice[0, 1] },
                { statusDst[1, 0], statusDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(statusDst.ToMatrixOf()),
                Variant.From(statusSlice.ToMatrixOf()),
                Variant.From(statusExp.ToMatrixOf())).SetName("UpdateRangeMatrixStatusCode");

            QualifiedName[,] qualifiedDst = new QualifiedName[,]
            {
                { new QualifiedName("a", 1), new QualifiedName("b", 1) },
                { new QualifiedName("c", 1), new QualifiedName("d", 1) }
            };
            QualifiedName[,] qualifiedSlice = new QualifiedName[,]
            {
                { new QualifiedName("x", 1), new QualifiedName("y", 1) }
            };
            QualifiedName[,] qualifiedExp = new QualifiedName[,]
            {
                { qualifiedSlice[0, 0], qualifiedSlice[0, 1] },
                { qualifiedDst[1, 0], qualifiedDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(qualifiedDst.ToMatrixOf()),
                Variant.From(qualifiedSlice.ToMatrixOf()),
                Variant.From(qualifiedExp.ToMatrixOf())).SetName("UpdateRangeMatrixQualifiedName");

            LocalizedText[,] localizedDst = new LocalizedText[,]
            {
                { new LocalizedText("en", "a"), new LocalizedText("en", "b") },
                { new LocalizedText("en", "c"), new LocalizedText("en", "d") }
            };
            LocalizedText[,] localizedSlice = new LocalizedText[,]
            {
                { new LocalizedText("en", "X"), new LocalizedText("en", "Y") }
            };
            LocalizedText[,] localizedExp = new LocalizedText[,]
            {
                { localizedSlice[0, 0], localizedSlice[0, 1] },
                { localizedDst[1, 0], localizedDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(localizedDst.ToMatrixOf()),
                Variant.From(localizedSlice.ToMatrixOf()),
                Variant.From(localizedExp.ToMatrixOf())).SetName("UpdateRangeMatrixLocalizedText");

            ExtensionObject[,] extensionDst = new ExtensionObject[,]
            {
                { new ExtensionObject(new Argument()), new ExtensionObject(new Argument()) },
                { new ExtensionObject(new Argument()), new ExtensionObject(new Argument()) }
            };
            ExtensionObject[,] extensionSlice = new ExtensionObject[,]
            {
                { new ExtensionObject(new Argument()), new ExtensionObject(new Argument()) }
            };
            ExtensionObject[,] extensionExp = new ExtensionObject[,]
            {
                { extensionSlice[0, 0], extensionSlice[0, 1] },
                { extensionDst[1, 0], extensionDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(extensionDst.ToMatrixOf()),
                Variant.From(extensionSlice.ToMatrixOf()),
                Variant.From(extensionExp.ToMatrixOf())).SetName("UpdateRangeMatrixExtensionObject");

            DataValue[,] dataValueDst = new DataValue[,]
            {
                { new DataValue(1), new DataValue(2) },
                { new DataValue(3), new DataValue(4) }
            };
            DataValue[,] dataValueSlice = new DataValue[,]
            {
                { new DataValue(77), new DataValue(88) }
            };
            DataValue[,] dataValueExp = new DataValue[,]
            {
                { dataValueSlice[0, 0], dataValueSlice[0, 1] },
                { dataValueDst[1, 0], dataValueDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(dataValueDst.ToMatrixOf()),
                Variant.From(dataValueSlice.ToMatrixOf()),
                Variant.From(dataValueExp.ToMatrixOf())).SetName("UpdateRangeMatrixDataValue");

            EnumValue[,] enumDst = new EnumValue[,]
            {
                { new EnumValue(1), new EnumValue(2) },
                { new EnumValue(3), new EnumValue(4) }
            };
            EnumValue[,] enumSlice = new EnumValue[,]
            {
                { new EnumValue(77), new EnumValue(88) }
            };
            EnumValue[,] enumExp = new EnumValue[,]
            {
                { enumSlice[0, 0], enumSlice[0, 1] },
                { enumDst[1, 0], enumDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(enumDst.ToMatrixOf()),
                Variant.From(enumSlice.ToMatrixOf()),
                Variant.From(enumExp.ToMatrixOf())).SetName("UpdateRangeMatrixEnumeration");

            Variant[,] variantDst = new Variant[,]
            {
                { new Variant(10), new Variant("two") },
                { new Variant(30), new Variant("four") }
            };
            Variant[,] variantSlice = new Variant[,]
            {
                { new Variant(77), new Variant("eighty") }
            };
            Variant[,] variantExp = new Variant[,]
            {
                { variantSlice[0, 0], variantSlice[0, 1] },
                { variantDst[1, 0], variantDst[1, 1] }
            };
            yield return new TestCaseData(
                Variant.From(variantDst.ToMatrixOf()),
                Variant.From(variantSlice.ToMatrixOf()),
                Variant.From(variantExp.ToMatrixOf())).SetName("UpdateRangeMatrixVariant");
        }

        [Test]
        public void ApplyRangeScalarStringSlicesSubstring()
        {
            Variant value = Variant.From("abcdef");
            var range = new NumericRange(1, 3);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.GetString(), Is.EqualTo("bcd"));
        }

        [Test]
        public void ApplyRangeScalarByteStringSlicesBytes()
        {
            Variant value = Variant.From(ByteString.From(10, 20, 30, 40, 50));
            var range = new NumericRange(1, 3);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            byte[] expected = [20, 30, 40];
            Assert.That(value.GetByteString().ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void ApplyRangeScalarNonSlicableTypeReturnsBadIndexRangeNoData()
        {
            Variant value = Variant.From(42);
            var range = new NumericRange(1, 2);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ApplyRangeStringDirectSlicesSubstring()
        {
            string value = "abcdef";
            var range = new NumericRange(2, 4);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo("cde"));
        }

        [Test]
        public void UpdateRangeStringDirectReplacesSubstring()
        {
            string value = "abcdef";
            var range = new NumericRange(1, 3);
            StatusCode status = range.UpdateRange(ref value, "XYZ");
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(value, Is.EqualTo("aXYZef"));
        }

        [Test]
        public void UpdateRangeStringDirectWithWrongSliceLengthReturnsBadIndexRangeNoData()
        {
            string value = "abcdef";
            var range = new NumericRange(1, 3);
            StatusCode status = range.UpdateRange(ref value, "XY");
            Assert.That(status, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
        }

        [Test]
        public void ApplyRangeByteStringDirectSlicesBytes()
        {
            ByteString value = ByteString.From(10, 20, 30, 40, 50);
            var range = new NumericRange(1, 3);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            byte[] expected = [20, 30, 40];
            Assert.That(value.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void UpdateRangeByteStringDirectReplacesBytes()
        {
            ByteString value = ByteString.From(10, 20, 30, 40, 50);
            var range = new NumericRange(1, 3);
            StatusCode status = range.UpdateRange(ref value, ByteString.From(1, 2, 3));
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            byte[] expected = [10, 1, 2, 3, 50];
            Assert.That(value.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void ApplyRangeMatrixWithoutSubRangesReturnsBadIndexRangeNoData()
        {
            int[,] data = new int[,] { { 1, 2 }, { 3, 4 } };
            Variant value = Variant.From(data.ToMatrixOf());
            var range = new NumericRange(0, 1);
            StatusCode status = range.ApplyRange(ref value);
            Assert.That(status, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void EnsureValidWithArraySetsOpenEndToCount()
        {
            int[] data = [1, 2, 3, 4, 5];
            var range = new NumericRange(1);
            NumericRange valid = range.EnsureValid(data.ToArrayOf());
            Assert.That(valid.IsNull, Is.False);
            Assert.That(valid.Begin, Is.EqualTo(1));
            Assert.That(valid.End, Is.EqualTo(5));
        }

        [Test]
        public void EnsureValidWithBeginBeyondCountReturnsNull()
        {
            int[] data = [1, 2, 3, 4, 5];
            var range = new NumericRange(10);
            NumericRange valid = range.EnsureValid(data.ToArrayOf());
            Assert.That(valid.IsNull, Is.True);
        }

        [Test]
        public void EnsureValidOnNullRangeReturnsFullRange()
        {
            NumericRange valid = NumericRange.Null.EnsureValid(5);
            Assert.That(valid.IsNull, Is.False);
            Assert.That(valid.Begin, Is.Zero);
            Assert.That(valid.End, Is.EqualTo(5));
        }

        [Test]
        public void EnsureValidWithNegativeCountReturnsNull()
        {
            var range = new NumericRange(0, 1);
            NumericRange valid = range.EnsureValid(-1);
            Assert.That(valid.IsNull, Is.True);
        }

        [Test]
        public void ImplicitConversionToRangeAndBackRoundTrips()
        {
            System.Range converted = new NumericRange(2, 5);
            Assert.That(converted.Start.Value, Is.EqualTo(2));
            Assert.That(converted.End.Value, Is.EqualTo(5));
            NumericRange back = converted;
            Assert.That(back.Begin, Is.EqualTo(2));
            Assert.That(back.End, Is.EqualTo(5));
        }

        [Test]
        public void ImplicitConversionOfIndexToRangeIsOpenEnded()
        {
            System.Range converted = new NumericRange(3);
            Assert.That(converted.Start.Value, Is.EqualTo(3));
            Assert.That(converted.End.IsFromEnd, Is.True);
        }

        [Test]
        public void ImplicitConversionOfNullRangeToRangeIsDefault()
        {
            System.Range converted = NumericRange.Null;
            Assert.That(converted.Start.Value, Is.Zero);
            Assert.That(converted.End.Value, Is.Zero);
            Assert.That(converted.End.IsFromEnd, Is.False);
        }

        [Test]
        public void ImplicitConversionFromEndRelativeRangeIsNull()
        {
            NumericRange converted = ^3..^1;
            Assert.That(converted.IsNull, Is.True);
        }

        [Test]
        public void CountReturnsElementCountForRange()
        {
            var range = new NumericRange(2, 5);
            Assert.That(range.Count, Is.EqualTo(4));
        }

        [Test]
        public void CountReturnsOneForIndex()
        {
            var range = new NumericRange(3);
            Assert.That(range.Count, Is.EqualTo(1));
        }

        [Test]
        public void DimensionsReturnsSubRangeCountForMultiDimensional()
        {
            NumericRange range = NumericRange.Parse("1:2,3:4,5:6");
            Assert.That(range.Dimensions, Is.EqualTo(3));
        }

        [Test]
        public void DimensionsReturnsOneForSingleRange()
        {
            var range = new NumericRange(1, 2);
            Assert.That(range.Dimensions, Is.EqualTo(1));
        }

        [Test]
        public void EqualsReturnsTrueForRangesWithSameSubRanges()
        {
            NumericRange left = NumericRange.Parse("1:2,3:4");
            NumericRange right = NumericRange.Parse("1:2,3:4");
            bool equalsResult = left.Equals(right);
            bool operatorEquals = left == right;
            bool operatorNotEquals = left != right;
            Assert.That(equalsResult, Is.True);
            Assert.That(operatorEquals, Is.True);
            Assert.That(operatorNotEquals, Is.False);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void EqualsReturnsFalseForRangesWithDifferentSubRanges()
        {
            NumericRange left = NumericRange.Parse("1:2,3:4");
            NumericRange right = NumericRange.Parse("1:2,3:5");
            bool equalsResult = left.Equals(right);
            bool operatorNotEquals = left != right;
            Assert.That(equalsResult, Is.False);
            Assert.That(operatorNotEquals, Is.True);
        }

        [Test]
        public void WithBeginKeepsEndAndUpdatesBegin()
        {
            var range = new NumericRange(2, 8);
            NumericRange updated = range.WithBegin(4);
            Assert.That(updated.Begin, Is.EqualTo(4));
            Assert.That(updated.End, Is.EqualTo(8));
        }

        [Test]
        public void WithBeginGreaterThanEndThrows()
        {
            var range = new NumericRange(2, 8);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.WithBegin(9));
        }

        [Test]
        public void WithEndKeepsBeginAndUpdatesEnd()
        {
            var range = new NumericRange(2, 8);
            NumericRange updated = range.WithEnd(5);
            Assert.That(updated.Begin, Is.EqualTo(2));
            Assert.That(updated.End, Is.EqualTo(5));
        }

        [Test]
        public void WithEndLessThanBeginThrows()
        {
            var range = new NumericRange(4, 8);
            Assert.Throws<ArgumentOutOfRangeException>(() => range.WithEnd(2));
        }

        private static DateTimeUtc Dt(int day)
        {
            return (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, day), DateTimeKind.Utc);
        }

        private static Uuid Uid(int value)
        {
            return new Uuid(new Guid($"00000000-0000-0000-0000-{value:D12}"));
        }

        private static ExpandedNodeId ENId(string identifier)
        {
            return ExpandedNodeId.Parse("nsu=Test;s=" + identifier);
        }
    }
}
