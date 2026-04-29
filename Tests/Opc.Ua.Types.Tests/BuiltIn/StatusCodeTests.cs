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
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="StatusCode"/> struct.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StatusCodeTests
    {
        [Test]
        public void ConstructorWithCodeSetsCode()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.Code, Is.Zero);
        }

        [Test]
        public void ConstructorWithUnknownCodeHasNullSymbolicId()
        {
            // A code that is not interned
            var sc = new StatusCode(0x12340000);
            Assert.That(sc.Code, Is.EqualTo(0x12340000));
            Assert.That(sc.SymbolicId, Is.Null);
        }

        [Test]
        public void ConstructorWithCodePreservesInfoBits()
        {
            // Code with extra flag bits
            const uint codeWithFlags = 0x80000000 | 0x0001;
            var sc = new StatusCode(codeWithFlags);
            Assert.That(sc.Code, Is.EqualTo(codeWithFlags));
        }

        [Test]
        public void ConstructorWithCodeAndNullSymbolicIdSetsCode()
        {
            var sc = new StatusCode(0x80010000, null);
            Assert.That(sc.Code, Is.EqualTo(0x80010000));
        }

        [Test]
        public void ConstructorWithInternedCodeResolvesSymbolicId()
        {
            // Ensure code is in the intern table, then verify constructor resolves it
            var customCodes = new List<StatusCode>
            {
                new(0x0AAA0000, "TestInterned")
            };
            StatusCode.Intern(customCodes);

            var sc = new StatusCode(0x0AAA0000);
            Assert.That(sc.SymbolicId, Is.EqualTo("TestInterned"));
        }

        [Test]
        public void ConstructorWithInternedCodeAndNullSymbolicIdResolvesFromInternTable()
        {
            var customCodes = new List<StatusCode>
            {
                new(0x0BBB0000, "TestInternedNull")
            };
            StatusCode.Intern(customCodes);

            var sc = new StatusCode(0x0BBB0000, null);
            Assert.That(sc.SymbolicId, Is.EqualTo("TestInternedNull"));
        }

        [Test]
        public void ConstructorWithCodeAndSymbolicIdUsesProvidedSymbolicId()
        {
            var sc = new StatusCode(0x12340000, "CustomCode");
            Assert.That(sc.SymbolicId, Is.EqualTo("CustomCode"));
            Assert.That(sc.Code, Is.EqualTo(0x12340000));
        }

        [Test]
        public void ConstructorWithCodeAndSymbolicIdPreservesInfoBits()
        {
            const uint codeWithFlags = 0x80000000 | 0x0005;
            var sc = new StatusCode(codeWithFlags, "BadCustom");
            Assert.That(sc.Code, Is.EqualTo(codeWithFlags));
            Assert.That(sc.SymbolicId, Is.EqualTo("BadCustom"));
        }

        [Test]
        public void ConstructorWithServiceResultExceptionUsesItsStatusCode()
        {
            var sre = new ServiceResultException(StatusCodes.BadUnexpectedError);
            var sc = new StatusCode(sre, 0x00000000, null);
            Assert.That(sc.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ConstructorWithNonServiceResultExceptionUsesDefaultCode()
        {
            var ex = new InvalidOperationException("test");
            var sc = new StatusCode(ex, 0x80010000, "BadUnexpectedError");
            Assert.That(sc.Code, Is.EqualTo(0x80010000));
            Assert.That(sc.SymbolicId, Is.EqualTo("BadUnexpectedError"));
        }

        [Test]
        public void ConstructorWithNonServiceResultExceptionAndNullSymbolicId()
        {
            var ex = new InvalidOperationException("test");
            var sc = new StatusCode(ex, 0x80010000, null);
            Assert.That(sc.Code, Is.EqualTo(0x80010000));
            Assert.That(sc.SymbolicId, Is.Null);
        }

        [Test]
        public void ConstructorWithStatusCodeDefaultUsesServiceResultException()
        {
            var sre = new ServiceResultException(StatusCodes.BadDecodingError);
            var sc = new StatusCode(sre, StatusCodes.Good);
            Assert.That(sc.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithStatusCodeDefaultUsesDefaultForNonSre()
        {
            var ex = new InvalidOperationException("test");
            var sc = new StatusCode(ex, StatusCodes.BadUnexpectedError);
            Assert.That(sc.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void SetCodeReturnsNewStatusCodeWithSpecifiedCode()
        {
            var sc = new StatusCode(0x00000000, "Good");
            StatusCode result = sc.SetCode(0x80000000);
            Assert.That(result.Code, Is.EqualTo(0x80000000));
        }

        [Test]
        public void CodeBitsReturnsUpperSixteenBits()
        {
            var sc = new StatusCode(0x80010005);
            Assert.That(sc.CodeBits, Is.EqualTo(0x80010000));
        }

        [Test]
        public void WithCodeBitsUintSetsUpperBitsPreservesLower()
        {
            var sc = new StatusCode(0x0000FFFF);
            StatusCode result = sc.WithCodeBits(0x80010000);
            Assert.That(result.CodeBits, Is.EqualTo(0x80010000));
            Assert.That(result.FlagBits, Is.EqualTo(0x0000FFFF));
        }

        [Test]
        public void WithCodeBitsStatusCodeSetsUpperBitsFromStatusCode()
        {
            var sc = new StatusCode(0x00000005);
            var source = new StatusCode(0x80010000);
            StatusCode result = sc.WithCodeBits(source);
            Assert.That(result.CodeBits, Is.EqualTo(0x80010000));
            Assert.That(result.FlagBits, Is.EqualTo(0x00000005));
        }

        [Test]
        public void FlagBitsReturnsLowerSixteenBits()
        {
            var sc = new StatusCode(0x8001ABCD);
            Assert.That(sc.FlagBits, Is.EqualTo(0x0000ABCD));
        }

        [Test]
        public void WithFlagBitsSetsLowerBitsPreservesUpper()
        {
            var sc = new StatusCode(0x80010000);
            StatusCode result = sc.WithFlagBits(0x00001234);
            Assert.That(result.CodeBits, Is.EqualTo(0x80010000));
            Assert.That(result.FlagBits, Is.EqualTo(0x00001234));
        }

        [Test]
        public void SubCodeReturnsCorrectBits()
        {
            var sc = new StatusCode(0x8FFF0000);
            Assert.That(sc.SubCode, Is.EqualTo(0x0FFF0000));
        }

        [Test]
        public void WithSubCodeSetsSubCodeBits()
        {
            StatusCode result = new StatusCode(0x00000000).WithSubCode(0x0ABC0000);
            Assert.That(result.SubCode, Is.EqualTo(0x0ABC0000));
        }

        [Test]
        public void StructureChangedReturnsTrueWhenBitSet()
        {
            // kStructureChangedBit = 0x8000
            var sc = new StatusCode(0x00008000);
            Assert.That(sc.StructureChanged, Is.True);
        }

        [Test]
        public void StructureChangedReturnsFalseWhenBitNotSet()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.StructureChanged, Is.False);
        }

        [Test]
        public void SetStructureChangedTrueSetsTheBit()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.SetStructureChanged(true);
            Assert.That(result.StructureChanged, Is.True);
            Assert.That(result.Code & 0x8000, Is.EqualTo(0x8000));
        }

        [Test]
        public void SetStructureChangedFalseClearsTheBit()
        {
            var sc = new StatusCode(0x00008000);
            StatusCode result = sc.SetStructureChanged(false);
            Assert.That(result.StructureChanged, Is.False);
            Assert.That(result.Code & 0x8000, Is.Zero);
        }

        [Test]
        public void SemanticsChangedReturnsTrueWhenBitSet()
        {
            // kSemanticsChangedBit = 0x4000
            var sc = new StatusCode(0x00004000);
            Assert.That(sc.SemanticsChanged, Is.True);
        }

        [Test]
        public void SemanticsChangedReturnsFalseWhenBitNotSet()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.SemanticsChanged, Is.False);
        }

        [Test]
        public void SetSemanticsChangedTrueSetsTheBit()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.SetSemanticsChanged(true);
            Assert.That(result.SemanticsChanged, Is.True);
            Assert.That(result.Code & 0x4000, Is.EqualTo(0x4000));
        }

        [Test]
        public void SetSemanticsChangedFalseClearsTheBit()
        {
            var sc = new StatusCode(0x00004000);
            StatusCode result = sc.SetSemanticsChanged(false);
            Assert.That(result.SemanticsChanged, Is.False);
            Assert.That(result.Code & 0x4000, Is.Zero);
        }

        [Test]
        public void HasDataValueInfoReturnsTrueWhenBitSet()
        {
            // kDataValueInfoType = 0x0400
            var sc = new StatusCode(0x00000400);
            Assert.That(sc.HasDataValueInfo, Is.True);
        }

        [Test]
        public void HasDataValueInfoReturnsFalseWhenBitNotSet()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.HasDataValueInfo, Is.False);
        }

        [Test]
        public void SetHasDataValueInfoTrueSetsTheBit()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.SetHasDataValueInfo(true);
            Assert.That(result.HasDataValueInfo, Is.True);
            Assert.That(result.Code & 0x0400, Is.EqualTo(0x0400));
        }

        [Test]
        public void SetHasDataValueInfoFalseClearsBitAndInfoBits()
        {
            // When setting to false, clears both the bit and all info bits (0xFFFFFC00 mask)
            var sc = new StatusCode(0x000004FF);
            StatusCode result = sc.SetHasDataValueInfo(false);
            Assert.That(result.HasDataValueInfo, Is.False);
            Assert.That(result.Code & 0x000003FF, Is.Zero);
        }

        [Test]
        public void LimitBitsReturnsCorrectValue()
        {
            // kLimitBits = 0x0300, LimitBits.Low = 0x0100
            var sc = new StatusCode(0x00000500); // 0x0400 (DataValueInfo) | 0x0100 (Low)
            Assert.That(sc.LimitBits, Is.EqualTo(LimitBits.Low));
        }

        [Test]
        public void WithLimitBitsSetsLimitBitsAndDataValueInfoBit()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.WithLimitBits(LimitBits.High);
            Assert.That(result.LimitBits, Is.EqualTo(LimitBits.High));
            Assert.That(result.HasDataValueInfo, Is.True);
        }

        [Test]
        public void WithLimitBitsConstantSetsCorrectValue()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.WithLimitBits(LimitBits.Constant);
            Assert.That(result.LimitBits, Is.EqualTo(LimitBits.Constant));
        }

        [Test]
        public void OverflowReturnsTrueWhenBothBitsSet()
        {
            // kOverflowBit = 0x0080, kDataValueInfoType = 0x0400
            // Overflow requires BOTH DataValueInfo bit AND overflow bit
            var sc = new StatusCode(0x00000480);
            Assert.That(sc.Overflow, Is.True);
        }

        [Test]
        public void OverflowReturnsFalseWhenOnlyOverflowBitSet()
        {
            // Overflow bit set but DataValueInfo bit not set
            var sc = new StatusCode(0x00000080);
            Assert.That(sc.Overflow, Is.False);
        }

        [Test]
        public void OverflowReturnsFalseWhenNeitherBitSet()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.Overflow, Is.False);
        }

        [Test]
        public void SetOverflowTrueSetsOverflowAndDataValueInfoBit()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.SetOverflow(true);
            Assert.That(result.Overflow, Is.True);
            Assert.That(result.HasDataValueInfo, Is.True);
            Assert.That(result.Code & 0x0080, Is.EqualTo(0x0080));
        }

        [Test]
        public void SetOverflowFalseClearsOverflowBitKeepsDataValueInfo()
        {
            var sc = new StatusCode(0x00000480); // Both DataValueInfo and Overflow set
            StatusCode result = sc.SetOverflow(false);
            Assert.That(result.Overflow, Is.False);
            // DataValueInfo should still be set
            Assert.That(result.HasDataValueInfo, Is.True);
            Assert.That(result.Code & 0x0080, Is.Zero);
        }

        [Test]
        public void AggregateBitsReturnsCorrectValue()
        {
            // kAggregateBits = 0x001F, Calculated = 0x01
            var sc = new StatusCode(0x00000001);
            Assert.That(sc.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
        }

        [Test]
        public void WithAggregateBitsSetsAggregateBitsAndDataValueInfo()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.WithAggregateBits(AggregateBits.Interpolated);
            Assert.That(result.AggregateBits, Is.EqualTo(AggregateBits.Interpolated));
            Assert.That(result.HasDataValueInfo, Is.True);
        }

        [Test]
        public void WithAggregateBitsPartialSetsCorrectValue()
        {
            var sc = new StatusCode(0x00000000);
            StatusCode result = sc.WithAggregateBits(AggregateBits.Partial | AggregateBits.Calculated);
            Assert.That(result.AggregateBits, Is.EqualTo(AggregateBits.Partial | AggregateBits.Calculated));
        }

        [Test]
        public void IsGoodReturnsTrueForGoodCode()
        {
            Assert.That(StatusCode.IsGood(new StatusCode(0x00000000)), Is.True);
        }

        [Test]
        public void IsGoodReturnsFalseForBadCode()
        {
            Assert.That(StatusCode.IsGood(new StatusCode(0x80000000)), Is.False);
        }

        [Test]
        public void IsGoodReturnsFalseForUncertainCode()
        {
            Assert.That(StatusCode.IsGood(new StatusCode(0x40000000)), Is.False);
        }

        [Test]
        public void IsNotGoodReturnsTrueForBadCode()
        {
            Assert.That(StatusCode.IsNotGood(new StatusCode(0x80000000)), Is.True);
        }

        [Test]
        public void IsNotGoodReturnsTrueForUncertainCode()
        {
            Assert.That(StatusCode.IsNotGood(new StatusCode(0x40000000)), Is.True);
        }

        [Test]
        public void IsNotGoodReturnsFalseForGoodCode()
        {
            Assert.That(StatusCode.IsNotGood(new StatusCode(0x00000000)), Is.False);
        }

        [Test]
        public void IsUncertainReturnsTrueForUncertainCode()
        {
            Assert.That(StatusCode.IsUncertain(new StatusCode(0x40000000)), Is.True);
        }

        [Test]
        public void IsUncertainReturnsFalseForGoodCode()
        {
            Assert.That(StatusCode.IsUncertain(new StatusCode(0x00000000)), Is.False);
        }

        [Test]
        public void IsUncertainReturnsFalseForBadCode()
        {
            Assert.That(StatusCode.IsUncertain(new StatusCode(0x80000000)), Is.False);
        }

        [Test]
        public void IsNotUncertainReturnsTrueForGoodCode()
        {
            Assert.That(StatusCode.IsNotUncertain(new StatusCode(0x00000000)), Is.True);
        }

        [Test]
        public void IsNotUncertainReturnsTrueForBadCode()
        {
            Assert.That(StatusCode.IsNotUncertain(new StatusCode(0x80000000)), Is.True);
        }

        [Test]
        public void IsNotUncertainReturnsFalseForUncertainCode()
        {
            Assert.That(StatusCode.IsNotUncertain(new StatusCode(0x40000000)), Is.False);
        }

        [Test]
        public void IsBadReturnsTrueForBadCode()
        {
            Assert.That(StatusCode.IsBad(new StatusCode(0x80000000)), Is.True);
        }

        [Test]
        public void IsBadReturnsFalseForGoodCode()
        {
            Assert.That(StatusCode.IsBad(new StatusCode(0x00000000)), Is.False);
        }

        [Test]
        public void IsNotBadReturnsTrueForGoodCode()
        {
            Assert.That(StatusCode.IsNotBad(new StatusCode(0x00000000)), Is.True);
        }

        [Test]
        public void IsNotBadReturnsTrueForUncertainCode()
        {
            Assert.That(StatusCode.IsNotBad(new StatusCode(0x40000000)), Is.True);
        }

        [Test]
        public void IsNotBadReturnsFalseForBadCode()
        {
            Assert.That(StatusCode.IsNotBad(new StatusCode(0x80000000)), Is.False);
        }

        [Test]
        public void CompareToObjectWithStatusCodeReturnsCorrectOrder()
        {
            var sc = new StatusCode(0x00000000);
            object other = new StatusCode(0x80000000);
            Assert.That(sc.CompareTo(other), Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithNullReturnsPositive()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc.CompareTo(null), Is.EqualTo(1));
        }

        [Test]
        public void CompareToObjectWithUintReturnsCorrectOrder()
        {
            var sc = new StatusCode(0x80000000);
            object other = (uint)0x00000000;
            Assert.That(sc.CompareTo(other), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToObjectWithNonComparableTypeReturnsNegative()
        {
            var sc = new StatusCode(0x00000000);
            object other = "not a status code";
            Assert.That(sc.CompareTo(other), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToObjectWithEqualStatusCodeReturnsZero()
        {
            var sc = new StatusCode(0x80010000);
            object other = new StatusCode(0x80010000);
            Assert.That(sc.CompareTo(other), Is.Zero);
        }

        [Test]
        public void CompareToStatusCodeReturnsCorrectOrder()
        {
            var smaller = new StatusCode(0x00000000);
            var larger = new StatusCode(0x80000000);
            Assert.That(smaller.CompareTo(larger), Is.LessThan(0));
            Assert.That(larger.CompareTo(smaller), Is.GreaterThan(0));
            Assert.That(smaller.CompareTo(smaller), Is.Zero);
        }

        [Test]
        public void CompareToUintReturnsCorrectOrder()
        {
            var sc = new StatusCode(0x40000000);
            Assert.That(sc.CompareTo(0x80000000), Is.LessThan(0));
            Assert.That(sc.CompareTo(0x00000000), Is.GreaterThan(0));
            Assert.That(sc.CompareTo(0x40000000), Is.Zero);
        }

        [Test]
        public void EqualsObjectWithStatusCodeComparesCodeBits()
        {
            var sc1 = new StatusCode(0x80010000);
            object sc2 = new StatusCode(0x80010000);
            Assert.That(sc1, Is.EqualTo(sc2));
        }

        [Test]
        public void EqualsObjectWithDifferentStatusCodeReturnsFalse()
        {
            var sc1 = new StatusCode(0x80010000);
            object sc2 = new StatusCode(0x80020000);
            Assert.That(sc1, Is.Not.EqualTo(sc2));
        }

        [Test]
        public void EqualsObjectWithUintComparesFullCode()
        {
            var sc = new StatusCode(0x80010005);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            object code = (uint)0x80010005;
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(sc, Is.EqualTo(code));
        }

        [Test]
        public void EqualsObjectWithUintReturnsFalseForDifferentValues()
        {
            var sc = new StatusCode(0x80010005);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            object code = (uint)0x80010006;
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(sc, Is.Not.EqualTo(code));
        }

        [Test]
        public void EqualsObjectWithNonStatusCodeOrUintReturnsFalse()
        {
            var sc = new StatusCode(0x00000000);
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(sc.Equals("string"), Is.False);
            Assert.That(sc.Equals(42), Is.False);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualsStatusCodeComparesFullCode()
        {
            // Same code bits but different flag bits must NOT be equal
            var sc1 = new StatusCode(0x80010001);
            var sc2 = new StatusCode(0x80010002);
            Assert.That(sc1, Is.Not.EqualTo(sc2));

            // Identical full codes must be equal
            var sc3 = new StatusCode(0x80010001);
            Assert.That(sc1, Is.EqualTo(sc3));
        }

        [Test]
        public void EqualsUintComparesFullCode()
        {
            var sc = new StatusCode(0x80010005);
            Assert.That(sc, Is.EqualTo(0x80010005));
            Assert.That(sc, Is.Not.EqualTo(0x80010006));
        }

        [Test]
        public void GetHashCodeReturnsConsistentValue()
        {
            var sc = new StatusCode(0x80010000);
            Assert.That(sc.GetHashCode(), Is.EqualTo(0x80010000u.GetHashCode()));
        }

        [Test]
        public void ToStringWithSymbolicIdIncludesName()
        {
            var sc = new StatusCode(0x0CCC0000, "TestSymbolic");
            string result = sc.ToString();
            Assert.That(result, Is.EqualTo("TestSymbolic"));
        }

        [Test]
        public void ToStringWithoutSymbolicIdFormatsAsHex()
        {
            var sc = new StatusCode(0x12340000);
            string result = sc.ToString();
            Assert.That(result, Is.EqualTo("12340000"));
        }

        [Test]
        public void ToStringWithSymbolicIdAndFlagBitsIncludesBoth()
        {
            var sc = new StatusCode(0x80000001, "BadTest");
            string result = sc.ToString();
            Assert.That(result, Does.Contain("BadTest"));
            Assert.That(result, Does.Contain("[Flags: 0001]"));
        }

        [Test]
        public void ToStringWithoutSymbolicIdAndWithFlagBits()
        {
            var sc = new StatusCode(0x12340005);
            string result = sc.ToString();
            Assert.That(result, Does.Contain("12340000"));
            Assert.That(result, Does.Contain("[Flags: 0005]"));
        }

        [Test]
        public void ToStringWithNoFlagBitsDoesNotIncludeFlags()
        {
            var sc = new StatusCode(0x80000000);
            string result = sc.ToString();
            Assert.That(result, Does.Not.Contain("Flags"));
        }

        [Test]
        public void ToStringFormatNullWithSymbolicIdFormatsCorrectly()
        {
            var sc = new StatusCode(0x80010000, "BadUnexpected");
            string result = sc.ToString(null, CultureInfo.InvariantCulture);
            Assert.That(result, Is.EqualTo("BadUnexpected [0x80010000]"));
        }

        [Test]
        public void ToStringFormatNullWithoutSymbolicIdFormatsAsHex()
        {
            var sc = new StatusCode(0x12340000);
            string result = sc.ToString(null, CultureInfo.InvariantCulture);
            Assert.That(result, Is.EqualTo("0x12340000"));
        }

        [Test]
        public void ToStringWithInvalidFormatThrowsFormatException()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(
                () => sc.ToString("X", CultureInfo.InvariantCulture),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ImplicitConversionFromUintCreatesStatusCode()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            StatusCode sc = (uint)0x80000000;
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(sc.Code, Is.EqualTo(0x80000000));
        }

        [Test]
        public void ExplicitConversionToUintReturnsCode()
        {
            var sc = new StatusCode(0x80010005);
            uint code = (uint)sc;
            Assert.That(code, Is.EqualTo(0x80010005));
        }

        [Test]
        public void EqualityOperatorStatusCodeComparesFullCode()
        {
            var sc1 = new StatusCode(0x80010001);
            var sc2 = new StatusCode(0x80010002);
            // Same code bits, different flag bits -> must NOT be equal after fix
            Assert.That(sc1, Is.Not.EqualTo(sc2));

            // Identical full codes must be equal
            var sc3 = new StatusCode(0x80010001);
            Assert.That(sc1, Is.EqualTo(sc3));
        }

        [Test]
        public void InequalityOperatorStatusCodeComparesByCodeBits()
        {
            var sc1 = new StatusCode(0x80010000);
            var sc2 = new StatusCode(0x80020000);
            Assert.That(sc1, Is.Not.EqualTo(sc2));
        }

        [Test]
        public void GoodIsNotEqualToGoodWithSemanticsChangedFlag()
        {
            // StatusCode.Good (0x00000000) must NOT equal Good with SemanticsChanged bit (0x00004000)
            var good = StatusCodes.Good;
            StatusCode goodWithSemanticsChanged = good.SetSemanticsChanged(true);
            Assert.That(good, Is.Not.EqualTo(goodWithSemanticsChanged));
        }

        [Test]
        public void GoodIsNotEqualToGoodWithStructureChangedFlag()
        {
            // StatusCode.Good (0x00000000) must NOT equal Good with StructureChanged bit (0x00008000)
            var good = StatusCodes.Good;
            StatusCode goodWithStructureChanged = good.SetStructureChanged(true);
            Assert.That(good, Is.Not.EqualTo(goodWithStructureChanged));
        }

        [Test]
        public void GoodIsEqualToGoodWithNoFlags()
        {
            var good1 = StatusCodes.Good;
            var good2 = new StatusCode(0x00000000);
            Assert.That(good1, Is.EqualTo(good2));
        }

        [Test]
        public void StatusCodeWithFlagsIsNotEqualToSameCodeWithoutFlags()
        {
            // A Bad code with a flag set must not equal the same Bad code without the flag
            var bad = new StatusCode(0x80010000);
            StatusCode badWithSemanticsChanged = bad.SetSemanticsChanged(true);
            Assert.That(bad, Is.Not.EqualTo(badWithSemanticsChanged));
        }

        [Test]
        public void EqualityOperatorUintComparesByFullCode()
        {
            var sc = new StatusCode(0x80010005);
            Assert.That(sc, Is.EqualTo(0x80010005));
            Assert.That(sc, Is.Not.EqualTo(0x80010006));
        }

        [Test]
        public void InequalityOperatorUintComparesByFullCode()
        {
            var sc = new StatusCode(0x80010005);
            Assert.That(sc, Is.Not.EqualTo(0x80010006));
            Assert.That(sc, Is.EqualTo(0x80010005));
        }

#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        [Test]
        public void LessThanOperatorUintWorksCorrectly()
        {
            var sc = new StatusCode(0x00000000);
            Assert.That(sc < 0x80000000, Is.True);
            Assert.That(sc < 0x00000000, Is.False);
        }

        [Test]
        public void GreaterThanOperatorUintWorksCorrectly()
        {
            var sc = new StatusCode(0x80000000);
            Assert.That(sc > 0x00000000, Is.True);
            Assert.That(sc > 0x80000000, Is.False);
        }

        [Test]
        public void LessThanOrEqualOperatorUintWorksCorrectly()
        {
            var sc = new StatusCode(0x40000000);
            Assert.That(sc <= 0x80000000, Is.True);
            Assert.That(sc <= 0x40000000, Is.True);
            Assert.That(sc > 0x00000000, Is.True);
        }

        [Test]
        public void GreaterThanOrEqualOperatorUintWorksCorrectly()
        {
            var sc = new StatusCode(0x40000000);
            Assert.That(sc >= 0x00000000, Is.True);
            Assert.That(sc >= 0x40000000, Is.True);
            Assert.That(sc >= 0x80000000, Is.False);
        }

        [Test]
        public void LessThanOperatorStatusCodeWorksCorrectly()
        {
            var sc1 = new StatusCode(0x00000000);
            var sc2 = new StatusCode(0x80000000);
            Assert.That(sc1 < sc2, Is.True);
            Assert.That(sc2 < sc1, Is.False);
        }

        [Test]
        public void GreaterThanOperatorStatusCodeWorksCorrectly()
        {
            var sc1 = new StatusCode(0x80000000);
            var sc2 = new StatusCode(0x00000000);
            Assert.That(sc1, Is.GreaterThan(sc2));
            Assert.That(sc2 > sc1, Is.False);
        }

        [Test]
        public void LessThanOrEqualOperatorStatusCodeWorksCorrectly()
        {
            var sc1 = new StatusCode(0x00000000);
            var sc2 = new StatusCode(0x80000000);
            var sc3 = new StatusCode(0x00000000);
            Assert.That(sc1 <= sc2, Is.True);
            Assert.That(sc1 <= sc3, Is.True);
            Assert.That(sc2, Is.GreaterThan(sc1));
        }

        [Test]
        public void GreaterThanOrEqualOperatorStatusCodeWorksCorrectly()
        {
            var sc1 = new StatusCode(0x80000000);
            var sc2 = new StatusCode(0x00000000);
            var sc3 = new StatusCode(0x80000000);
            Assert.That(sc1 >= sc2, Is.True);
            Assert.That(sc1 >= sc3, Is.True);
            Assert.That(sc2 >= sc1, Is.False);
        }
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void LookupSymbolicIdReturnsNameForKnownCode()
        {
            // Ensure we have a known code in the intern table
            var customCodes = new List<StatusCode>
            {
                new(0x0FFA0000, "LookupTest")
            };
            StatusCode.Intern(customCodes);

            string result = StatusCode.LookupSymbolicId(0x0FFA0000);
            Assert.That(result, Is.EqualTo("LookupTest"));
        }

        [Test]
        public void LookupSymbolicIdReturnsNullForUnknownCode()
        {
            string result = StatusCode.LookupSymbolicId(0x12340000);
            Assert.That(result, Is.Null);
        }
#pragma warning restore CS0618  // Type or member is obsolete

        [Test]
        public void LookupUtf8SymbolicIdReturnsBytesForKnownCode()
        {
            var customCodes = new List<StatusCode>
            {
                new(0x0FFB0000, "Utf8LookupTest")
            };
            StatusCode.Intern(customCodes);

            byte[] result = StatusCode.LookupUtf8SymbolicId(0x0FFB0000);
            Assert.That(result, Is.Not.Null);
            Assert.That(Encoding.UTF8.GetString(result), Is.EqualTo("Utf8LookupTest"));
        }

        [Test]
        public void LookupUtf8SymbolicIdReturnsNullForUnknownCode()
        {
            byte[] result = StatusCode.LookupUtf8SymbolicId(0x12340000);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryGetInternedStatusCodeReturnsTrueForKnownCode()
        {
            // Ensure a code is in the intern table
            var customCodes = new List<StatusCode>
            {
                new(0x0FFC0000, "InternTest")
            };
            StatusCode.Intern(customCodes);

            bool found = StatusCode.TryGetInternedStatusCode(0x0FFC0000, out StatusCode sc);
            Assert.That(found, Is.True);
            Assert.That(sc.SymbolicId, Is.EqualTo("InternTest"));
        }

        [Test]
        public void TryGetInternedStatusCodeReturnsFalseForUnknownCode()
        {
            bool found = StatusCode.TryGetInternedStatusCode(0x12340000, out _);
            Assert.That(found, Is.False);
        }

        [Test]
        public void TryGetInternedStatusCodeMasksToCodeBits()
        {
            // Ensure the code bits are in the intern table
            var customCodes = new List<StatusCode>
            {
                new(0x0FFD0000, "MaskTest")
            };
            StatusCode.Intern(customCodes);

            // Pass a code with flag bits set - should still find the interned code
            bool found = StatusCode.TryGetInternedStatusCode(0x0FFD0001, out StatusCode sc);
            Assert.That(found, Is.True);
            Assert.That(sc.SymbolicId, Is.EqualTo("MaskTest"));
        }

        [Test]
        public void InternAddsCustomStatusCodes()
        {
            var customCodes = new List<StatusCode>
            {
                new(0x0EEE0000, "CustomTestCode")
            };
            StatusCode.Intern(customCodes);

            bool found = StatusCode.TryGetInternedStatusCode(0x0EEE0000, out StatusCode sc);
            Assert.That(found, Is.True);
            Assert.That(sc.SymbolicId, Is.EqualTo("CustomTestCode"));
        }

        [Test]
        public void InternSkipsEntriesWithNullSymbolicId()
        {
            var customCodes = new List<StatusCode>
            {
                new(0x0DDD0000) // no symbolic id
            };
            StatusCode.Intern(customCodes);

            bool found = StatusCode.TryGetInternedStatusCode(0x0DDD0000, out _);
            // Should not have been added since SymbolicId is null
            Assert.That(found, Is.False);
        }

        [Test]
        public void InternedStatusCodesReturnsCollection()
        {
            ArrayOf<StatusCode> collection = StatusCode.InternedStatusCodes;
            Assert.That(collection.IsNull, Is.False);
            Assert.That(collection.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DefaultStatusCodeIsZero()
        {
            StatusCode sc = default;
            Assert.That(sc.Code, Is.Zero);
            Assert.That(sc.SymbolicId, Is.Null);
        }

        [Test]
        public void DefaultStatusCodeIsGood()
        {
            StatusCode sc = default;
            Assert.That(StatusCode.IsGood(sc), Is.True);
            Assert.That(StatusCode.IsBad(sc), Is.False);
            Assert.That(StatusCode.IsUncertain(sc), Is.False);
        }

        [Test]
        public void SerializableStatusCodeDefaultConstructorCreatesDefault()
        {
            var ssc = new SerializableStatusCode();
            Assert.That(ssc.Value.Code, Is.Zero);
        }

        [Test]
        public void SerializableStatusCodeConstructorWithValuePreservesCode()
        {
            var sc = new StatusCode(0x80010000);
            var ssc = new SerializableStatusCode(sc);
            Assert.That(ssc.Value.Code, Is.EqualTo(0x80010000));
        }

        [Test]
        public void SerializableStatusCodeCodePropertyGetReturnsCode()
        {
            var ssc = new SerializableStatusCode(new StatusCode(0x80010000));
            Assert.That(ssc.Code, Is.EqualTo(0x80010000));
        }

        [Test]
        public void SerializableStatusCodeCodePropertySetUpdatesValue()
        {
            var ssc = new SerializableStatusCode
            {
                Code = 0x80010000
            };
            Assert.That(ssc.Value.Code, Is.EqualTo(0x80010000));
        }

        [Test]
        public void SerializableStatusCodeGetValueReturnsBoxedStatusCode()
        {
            var sc = new StatusCode(0x80010000);
            var ssc = new SerializableStatusCode(sc);
            object value = ssc.GetValue();
            Assert.That(value, Is.TypeOf<StatusCode>());
            Assert.That(((StatusCode)value).Code, Is.EqualTo(0x80010000));
        }
    }
}
