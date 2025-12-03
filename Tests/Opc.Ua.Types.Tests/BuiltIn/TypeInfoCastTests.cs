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
using System.Globalization;
using System.Numerics;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for TypeInfo.Cast method with BigInteger support.
    /// </summary>
    [TestFixture]
    [Category("TypeInfoCast")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeInfoCastTests
    {
        /// <summary>
        /// Test BigInteger to Float conversion.
        /// </summary>
        [Test]
        public void BigIntegerToFloat()
        {
            var bigInteger = BigInteger.Parse("164702320649031400000", CultureInfo.InvariantCulture);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Float);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<float>(result);
            
            // Test with the expected float value from the issue
            float expected = (float)bigInteger;
            Assert.AreEqual(expected, result);
        }

        /// <summary>
        /// Test BigInteger to Double conversion.
        /// </summary>
        [Test]
        public void BigIntegerToDouble()
        {
            var bigInteger = BigInteger.Parse("164702320649031400000", CultureInfo.InvariantCulture);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Double);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<double>(result);
            
            double expected = (double)bigInteger;
            Assert.AreEqual(expected, result);
        }

        /// <summary>
        /// Test BigInteger to UInt64 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToUInt64()
        {
            var bigInteger = new BigInteger(ulong.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.UInt64);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ulong>(result);
            Assert.AreEqual(ulong.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to Int64 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToInt64()
        {
            var bigInteger = new BigInteger(long.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Int64);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<long>(result);
            Assert.AreEqual(long.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to UInt32 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToUInt32()
        {
            var bigInteger = new BigInteger(uint.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.UInt32);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<uint>(result);
            Assert.AreEqual(uint.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to Int32 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToInt32()
        {
            var bigInteger = new BigInteger(int.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Int32);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<int>(result);
            Assert.AreEqual(int.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to UInt16 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToUInt16()
        {
            var bigInteger = new BigInteger(ushort.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.UInt16);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<ushort>(result);
            Assert.AreEqual(ushort.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to Int16 conversion.
        /// </summary>
        [Test]
        public void BigIntegerToInt16()
        {
            var bigInteger = new BigInteger(short.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Int16);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<short>(result);
            Assert.AreEqual(short.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to Byte conversion.
        /// </summary>
        [Test]
        public void BigIntegerToByte()
        {
            var bigInteger = new BigInteger(byte.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Byte);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<byte>(result);
            Assert.AreEqual(byte.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to SByte conversion.
        /// </summary>
        [Test]
        public void BigIntegerToSByte()
        {
            var bigInteger = new BigInteger(sbyte.MaxValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.SByte);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<sbyte>(result);
            Assert.AreEqual(sbyte.MaxValue, result);
        }

        /// <summary>
        /// Test BigInteger to Boolean conversion.
        /// </summary>
        [Test]
        public void BigIntegerToBoolean()
        {
            var bigIntegerZero = BigInteger.Zero;
            var resultFalse = TypeInfo.Cast(bigIntegerZero, BuiltInType.Boolean);
            Assert.IsNotNull(resultFalse);
            Assert.IsInstanceOf<bool>(resultFalse);
            Assert.AreEqual(false, resultFalse);

            var bigIntegerOne = BigInteger.One;
            var resultTrue = TypeInfo.Cast(bigIntegerOne, BuiltInType.Boolean);
            Assert.IsNotNull(resultTrue);
            Assert.IsInstanceOf<bool>(resultTrue);
            Assert.AreEqual(true, resultTrue);
        }

        /// <summary>
        /// Test BigInteger to String conversion.
        /// </summary>
        [Test]
        public void BigIntegerToString()
        {
            var bigInteger = new BigInteger(12345678901234567890);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.String);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<string>(result);
            Assert.AreEqual("12345678901234567890", result);
        }

        /// <summary>
        /// Test that precision loss occurs for large BigInteger to Float.
        /// </summary>
        [Test]
        public void BigIntegerToFloatPrecisionLoss()
        {
            // Very large BigInteger that will lose precision when converted to float
            var bigInteger = BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Float);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<float>(result);
            
            // Just verify it returns a float value - precision loss is expected
            float floatValue = (float)result;
            Assert.IsFalse(float.IsNaN(floatValue));
            Assert.IsFalse(float.IsInfinity(floatValue));
        }

        /// <summary>
        /// Test overflow behavior for BigInteger to smaller types.
        /// </summary>
        [Test]
        public void BigIntegerToSmallerTypeOverflow()
        {
            // BigInteger larger than Int32.MaxValue
            var bigInteger = new BigInteger(long.MaxValue);
            
            // This should throw OverflowException when converting to Int32
            Assert.Throws<OverflowException>(() =>
            {
                TypeInfo.Cast(bigInteger, BuiltInType.Int32);
            });
        }

        /// <summary>
        /// Test negative BigInteger conversions.
        /// </summary>
        [Test]
        public void NegativeBigIntegerToInt64()
        {
            var bigInteger = new BigInteger(long.MinValue);
            var result = TypeInfo.Cast(bigInteger, BuiltInType.Int64);
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<long>(result);
            Assert.AreEqual(long.MinValue, result);
        }
    }
}
