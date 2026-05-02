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

#nullable enable

using System;
using NUnit.Framework;

#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable CA2263 // Prefer generic overload when type is known

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="EnumHelper"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EnumHelperTests
    {
        [Test]
        public void Int32ToEnumGenericConvertsIntBasedEnum()
        {
            NodeClass result = EnumHelper.Int32ToEnum<NodeClass>(1);
            Assert.That(result, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void Int32ToEnumGenericConvertsZero()
        {
            NodeClass result = EnumHelper.Int32ToEnum<NodeClass>(0);
            Assert.That(result, Is.EqualTo(NodeClass.Unspecified));
        }

        [Test]
        public void Int32ToEnumGenericConvertsByteEnum()
        {
            ByteTestEnum result = EnumHelper.Int32ToEnum<ByteTestEnum>(1);
            Assert.That(result, Is.EqualTo(ByteTestEnum.One));
        }

        [Test]
        public void Int32ToEnumGenericConvertsShortEnum()
        {
            ShortTestEnum result = EnumHelper.Int32ToEnum<ShortTestEnum>(1);
            Assert.That(result, Is.EqualTo(ShortTestEnum.One));
        }

        [Test]
        public void Int32ToEnumGenericConvertsUShortEnum()
        {
            UShortTestEnum result = EnumHelper.Int32ToEnum<UShortTestEnum>(1);
            Assert.That(result, Is.EqualTo(UShortTestEnum.One));
        }

        [Test]
        public void EnumToInt32GenericConvertsNodeClass()
        {
            int result = EnumHelper.EnumToInt32(NodeClass.Variable);
            Assert.That(result, Is.EqualTo(2));
        }

        [Test]
        public void EnumToInt32GenericConvertsZero()
        {
            int result = EnumHelper.EnumToInt32(NodeClass.Unspecified);
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void EnumToInt32GenericConvertsBrowseDirection()
        {
            int result = EnumHelper.EnumToInt32(BrowseDirection.Inverse);
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithByteEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)ByteTestEnum.One, typeof(ByteTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithSByteEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)SByteTestEnum.One, typeof(SByteTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithShortEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)ShortTestEnum.One, typeof(ShortTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithUShortEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)UShortTestEnum.One, typeof(UShortTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithIntEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)NodeClass.Object, typeof(NodeClass));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithUIntEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)UIntTestEnum.One, typeof(UIntTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithLongEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)LongTestEnum.One, typeof(LongTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithULongEnum()
        {
            int result = EnumHelper.EnumToInt32(
                (object)ULongTestEnum.One, typeof(ULongTestEnum));
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void EnumToInt32ObjectWithIntType()
        {
            int result = EnumHelper.EnumToInt32((object)42, typeof(int));
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void EnumToInt64ConvertsNodeClass()
        {
            long result = EnumHelper.EnumToInt64(NodeClass.Variable);
            Assert.That(result, Is.EqualTo(2L));
        }

        [Test]
        public void EnumToInt64ConvertsByteEnum()
        {
            long result = EnumHelper.EnumToInt64(ByteTestEnum.One);
            Assert.That(result, Is.EqualTo(1L));
        }

        [Test]
        public void EnumToInt64ConvertsShortEnum()
        {
            long result = EnumHelper.EnumToInt64(ShortTestEnum.One);
            Assert.That(result, Is.EqualTo(1L));
        }

        [Test]
        public void EnumArrayToInt32ArrayWithNullReturnsDefault()
        {
            ArrayOf<int> result = EnumHelper.EnumArrayToInt32Array(null!);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void EnumArrayToInt32ArrayWithIntArrayFastPath()
        {
            int[] source = [1, 2, 4];
            ArrayOf<int> result = EnumHelper.EnumArrayToInt32Array(source);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(2));
            Assert.That(result[2], Is.EqualTo(4));
        }

        [Test]
        public void EnumArrayToInt32ArrayWithEnumArrayConverts()
        {
            NodeClass[] source =
            [
                NodeClass.Object, NodeClass.Variable, NodeClass.Method
            ];
            ArrayOf<int> result = EnumHelper.EnumArrayToInt32Array(source);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(2));
            Assert.That(result[2], Is.EqualTo(4));
        }

        [Test]
        public void EnumArrayToInt32MatrixWithNullReturnsDefault()
        {
            MatrixOf<int> result = EnumHelper.EnumArrayToInt32Matrix(null!);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void EnumArrayToInt32MatrixWithIntArrayFastPath()
        {
            int[,] source = new int[,] { { 1, 2 }, { 4, 8 } };
            MatrixOf<int> result = EnumHelper.EnumArrayToInt32Matrix(source);
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void EnumArrayToInt32MatrixWithEnumMultiDimArray()
        {
            var source = new NodeClass[,]
            {
                { NodeClass.Object, NodeClass.Variable },
                { NodeClass.Method, NodeClass.ObjectType }
            };
            MatrixOf<int> result = EnumHelper.EnumArrayToInt32Matrix(source);
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void Int32ToEnumWithIntTypeReturnsInt()
        {
            object? result = EnumHelper.Int32ToEnum(42, typeof(int));
            Assert.That(result, Is.TypeOf<int>());
            Assert.That((int)result!, Is.EqualTo(42));
        }

        [Test]
        public void Int32ToEnumWithNonEnumTypeReturnsNull()
        {
            object? result = EnumHelper.Int32ToEnum(42, typeof(string));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Int32ToEnumWithEnumTypeReturnsEnumValue()
        {
            object? result = EnumHelper.Int32ToEnum(1, typeof(NodeClass));
            Assert.That(result, Is.TypeOf<NodeClass>());
            Assert.That((NodeClass)result!, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        public void Int32ToEnumWithBrowseDirectionReturnsCorrectValue()
        {
            object? result = EnumHelper.Int32ToEnum(2, typeof(BrowseDirection));
            Assert.That(result, Is.TypeOf<BrowseDirection>());
            Assert.That((BrowseDirection)result!, Is.EqualTo(BrowseDirection.Both));
        }

        [Test]
        public void Int32ArrayToEnumArrayWithNullReturnsNull()
        {
            ArrayOf<int> source = default;
            Array? result = EnumHelper.Int32ArrayToEnumArray(source, typeof(NodeClass));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Int32ArrayToEnumArrayWithIntTypeFastPath()
        {
            ArrayOf<int> source = new int[] { 1, 2, 4 };
            Array? result = EnumHelper.Int32ArrayToEnumArray(source, typeof(int));
            Assert.That(result, Is.TypeOf<int[]>());
            Assert.That(result, Has.Length.EqualTo(3));
        }

        [Test]
        public void Int32ArrayToEnumArrayWithEnumType()
        {
            ArrayOf<int> source = new int[] { 1, 2 };
            Array? result = EnumHelper.Int32ArrayToEnumArray(source, typeof(NodeClass));
            Assert.That(result, Is.TypeOf<NodeClass[]>());
            var enumArray = (NodeClass[])result!;
            Assert.That(enumArray[0], Is.EqualTo(NodeClass.Object));
            Assert.That(enumArray[1], Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void Int32MatrixToEnumArrayWithNullReturnsNull()
        {
            MatrixOf<int> source = default;
            Array? result = EnumHelper.Int32MatrixToEnumArray(source, typeof(NodeClass));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Int32MatrixToEnumArrayWithIntTypeFastPath()
        {
            MatrixOf<int> source = s_testIntValues.ToMatrixOf(2, 2);
            Array? result = EnumHelper.Int32MatrixToEnumArray(source, typeof(int));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!, Has.Length.EqualTo(4));
        }

        [Test]
        public void Int32MatrixToEnumArrayWithEnumType()
        {
            MatrixOf<int> source = s_testIntValues.ToMatrixOf(2, 2);
            Array? result = EnumHelper.Int32MatrixToEnumArray(
                source, typeof(NodeClass));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Rank, Is.EqualTo(2));
            Assert.That(result.GetLength(0), Is.EqualTo(2));
            Assert.That(result.GetLength(1), Is.EqualTo(2));
        }

        [Test]
        public void ArrayOfIntExtensionInt32ToEnumConverts()
        {
            ArrayOf<int> source = [0, 1, 2];
            ArrayOf<NodeClass> result = source.Int32ToEnum<NodeClass>();
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(NodeClass.Unspecified));
            Assert.That(result[1], Is.EqualTo(NodeClass.Object));
            Assert.That(result[2], Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void ArrayOfEnumExtensionEnumToInt32Converts()
        {
            ArrayOf<NodeClass> source = new NodeClass[]
            {
                NodeClass.Object, NodeClass.Variable
            };
            ArrayOf<int> result = source.EnumToInt32();
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(2));
        }

        [Test]
        public void MatrixOfIntExtensionInt32ToEnumConverts()
        {
            MatrixOf<int> source = s_testIntValues
                .ToMatrixOf(2, 2);
            MatrixOf<NodeClass> result = source.Int32ToEnum<NodeClass>();
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void MatrixOfEnumExtensionEnumToInt32Converts()
        {
            MatrixOf<NodeClass> source = new NodeClass[]
            {
                NodeClass.Object, NodeClass.Variable,
                NodeClass.Method, NodeClass.ObjectType
            }.ToMatrixOf(2, 2);
            MatrixOf<int> result = source.EnumToInt32();
            Assert.That(result.Count, Is.EqualTo(4));
        }

        private static readonly int[] s_testIntValues = [1, 2, 4, 8];

        private enum ByteTestEnum : byte
        {
            Zero = 0,
            One = 1
        }

        private enum SByteTestEnum : sbyte
        {
            Zero = 0,
            One = 1
        }

        private enum ShortTestEnum : short
        {
            Zero = 0,
            One = 1
        }

        private enum UShortTestEnum : ushort
        {
            Zero = 0,
            One = 1
        }

        private enum UIntTestEnum : uint
        {
            Zero = 0,
            One = 1
        }

        private enum LongTestEnum : long
        {
            Zero = 0,
            One = 1
        }

        private enum ULongTestEnum : ulong
        {
            Zero = 0,
            One = 1
        }
    }
}
