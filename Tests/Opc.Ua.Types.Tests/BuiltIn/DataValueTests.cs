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
using NUnit.Framework;

#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataValueTests
    {
        [Test]
        public void DefaultConstructorSetsDefaults()
        {
            var dv = new DataValue();

            Assert.That(dv.WrappedValue.IsNull, Is.True);
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dv.SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(dv.SourcePicoseconds, Is.Zero);
            Assert.That(dv.ServerPicoseconds, Is.Zero);
        }

        [Test]
        public void CopyCopiesAllFields()
        {
            var sourceTime = new DateTimeUtc(2024, 6, 15, 10, 30, 0);
            var serverTime = new DateTimeUtc(2024, 6, 15, 10, 30, 1);
            var original = new DataValue(new Variant(42), StatusCodes.Good, sourceTime, serverTime)
            {
                SourcePicoseconds = 100,
                ServerPicoseconds = 200
            };

            var copy = original.Copy();

            Assert.That(copy.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(copy.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(copy.SourceTimestamp, Is.EqualTo(sourceTime));
            Assert.That(copy.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(copy.SourcePicoseconds, Is.EqualTo((ushort)100));
            Assert.That(copy.ServerPicoseconds, Is.EqualTo((ushort)200));
        }

        [Test]
        public void ConstructorWithStatusCodeAndServerTimestamp()
        {
            var serverTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var dv = DataValue.FromStatusCode(StatusCodes.Bad, serverTime);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public void ConstructorWithVariantStatusCodeAndSourceTimestamp()
        {
            var sourceTime = new DateTimeUtc(2024, 3, 20, 12, 0, 0);
            var dv = new DataValue(new Variant("hello"), StatusCodes.Uncertain, sourceTime);

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("hello")));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(dv.SourceTimestamp, Is.EqualTo(sourceTime));
        }

        [Test]
        public void ConstructorWithAllFourParameters()
        {
            var sourceTime = new DateTimeUtc(2024, 5, 1, 8, 0, 0);
            var serverTime = new DateTimeUtc(2024, 5, 1, 8, 0, 1);
            var dv = new DataValue(new Variant(3.14), StatusCodes.Good, sourceTime, serverTime);

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant(3.14)));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dv.SourceTimestamp, Is.EqualTo(sourceTime));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(serverTime));
        }

        [Test]
        public void ConstructorWithStatusCodeOnly()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.BadUnexpectedError);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public void ConstructorWithIntLiteralWrapsInVariant()
        {
            var dv = new DataValue(42);

            Assert.That(dv.WrappedValue.IsNull, Is.False);
            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void FromStatusCodeSetsStatusCodeOnly()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
            Assert.That(dv.SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public void FromStatusCodeWithTimestampSetsBothFields()
        {
            var serverTime = new DateTimeUtc(2024, 7, 1, 12, 0, 0);
            var dv = DataValue.FromStatusCode(StatusCodes.Bad, serverTime);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
            Assert.That(dv.SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public void EqualsObjectWithNull()
        {
            var dv = new DataValue();

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(dv, Is.Not.EqualTo((object)null));
#pragma warning restore NUnit4002 // Use Specific constraint
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsObjectWithDataValue()
        {
            var dv1 = new DataValue(new Variant(42));
            var dv2 = new DataValue(new Variant(42));

            Assert.That(dv1, Is.EqualTo((object)dv2));
        }

        [Test]
        public void EqualsObjectWithNonDataValue()
        {
            var dv = new DataValue();

            Assert.That(dv.Equals("not a DataValue"), Is.False);
        }

        [Test]
        public void EqualsDataValueSameReference()
        {
            var dv = new DataValue(new Variant(42));

            Assert.That(dv.Equals(dv), Is.True);
        }

        [Test]
        public void EqualsDataValueWithNullOther()
        {
            var dv = new DataValue(new Variant(42));

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(dv, Is.Not.EqualTo((DataValue)null));
#pragma warning restore NUnit4002 // Use Specific constraint
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsReturnsFalseForDifferentStatusCodes()
        {
            var dv1 = new DataValue(new Variant(42), StatusCodes.Good);
            var dv2 = new DataValue(new Variant(42), StatusCodes.Bad);

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentServerTimestamps()
        {
            var dv1 = new DataValue(new Variant(42)) { ServerTimestamp = new DateTimeUtc(2024, 1, 1) };
            var dv2 = new DataValue(new Variant(42)) { ServerTimestamp = new DateTimeUtc(2025, 1, 1) };

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentSourceTimestamps()
        {
            var dv1 = new DataValue(new Variant(42)) { SourceTimestamp = new DateTimeUtc(2024, 1, 1) };
            var dv2 = new DataValue(new Variant(42)) { SourceTimestamp = new DateTimeUtc(2025, 1, 1) };

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentServerPicoseconds()
        {
            var dv1 = new DataValue(new Variant(42)) { ServerPicoseconds = 100 };
            var dv2 = new DataValue(new Variant(42)) { ServerPicoseconds = 200 };

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentSourcePicoseconds()
        {
            var dv1 = new DataValue(new Variant(42)) { SourcePicoseconds = 100 };
            var dv2 = new DataValue(new Variant(42)) { SourcePicoseconds = 200 };

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentValues()
        {
            var dv1 = new DataValue(new Variant(42));
            var dv2 = new DataValue(new Variant(99));

            Assert.That(dv1, Is.Not.EqualTo(dv2));
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalDataValues()
        {
            var time = new DateTimeUtc(2024, 6, 1, 12, 0, 0);
            var dv1 = new DataValue(new Variant("abc"), StatusCodes.Good, time, time)
            {
                SourcePicoseconds = 50,
                ServerPicoseconds = 60
            };
            var dv2 = new DataValue(new Variant("abc"), StatusCodes.Good, time, time)
            {
                SourcePicoseconds = 50,
                ServerPicoseconds = 60
            };

            Assert.That(dv1, Is.EqualTo(dv2));
        }

        [Test]
        public void EqualityOperatorBothNull()
        {
            DataValue a = null;
            DataValue b = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a, Is.EqualTo(b));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorLeftNullRightNotNull()
        {
            DataValue a = null;
            var b = new DataValue();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a, Is.Not.EqualTo(b));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorLeftNotNullRightNull()
        {
            var a = new DataValue();
            DataValue b = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a, Is.Not.EqualTo(b));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorEqualValues()
        {
            var a = new DataValue(new Variant(42));
            var b = new DataValue(new Variant(42));

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void InequalityOperatorDifferentValues()
        {
            var a = new DataValue(new Variant(1));
            var b = new DataValue(new Variant(2));

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void InequalityOperatorEqualValues()
        {
            var a = new DataValue(new Variant(42));
            var b = new DataValue(new Variant(42));

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void GetHashCodeWithNonNullValue()
        {
            var dv = new DataValue(new Variant(42));

            int hash = dv.GetHashCode();

            Assert.That(hash, Is.EqualTo(new Variant(42).GetHashCode()));
        }

        [Test]
        public void GetHashCodeWithNullValue()
        {
            var dv = new DataValue
            {
                StatusCode = StatusCodes.BadUnexpectedError
            };

            int hash = dv.GetHashCode();

            Assert.That(hash, Is.EqualTo(StatusCodes.BadUnexpectedError.GetHashCode()));
        }

        [Test]
        public void ToStringReturnsValueString()
        {
            var dv = new DataValue(new Variant(42));

            string result = dv.ToString();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void ToStringWithNullFormatReturnsValue()
        {
            var dv = new DataValue(new Variant("test"));

            string result = dv.ToString(null, null);

            Assert.That(result, Does.Contain("test"));
        }

        [Test]
        public void ToStringWithFormatThrowsFormatException()
        {
            var dv = new DataValue(new Variant(42));

            Assert.That(() => dv.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void CloneReturnsDeepCopy()
        {
            var sourceTime = new DateTimeUtc(2024, 6, 15, 10, 0, 0);
            var original = new DataValue(new Variant("hello"), StatusCodes.Good, sourceTime)
            {
                SourcePicoseconds = 42,
                ServerPicoseconds = 99
            };

            var clone = (DataValue)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(clone.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(clone.SourceTimestamp, Is.EqualTo(original.SourceTimestamp));
            Assert.That(clone.SourcePicoseconds, Is.EqualTo(original.SourcePicoseconds));
            Assert.That(clone.ServerPicoseconds, Is.EqualTo(original.ServerPicoseconds));
        }

        [Test]
        public void ValueGetterReturnsBoxedObject()
        {
            var dv = new DataValue(new Variant(42));

#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(dv.Value, Is.EqualTo(42));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void ValueSetterSetsVariant()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var dv = new DataValue
            {
                Value = "hello"
            };
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("hello")));
        }

        [Test]
        public void WrappedValueGetterAndSetter()
        {
            var dv = new DataValue();
            var variant = new Variant(true);

            dv.WrappedValue = variant;

            Assert.That(dv.WrappedValue, Is.EqualTo(variant));
        }

        [Test]
        public void IsGoodWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsGood(dv), Is.True);
        }

        [Test]
        public void IsGoodWithBadDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Bad);

            Assert.That(DataValue.IsGood(dv), Is.False);
        }

        [Test]
        public void IsGoodWithNullReturnsFalse()
        {
            Assert.That(DataValue.IsGood(null), Is.False);
        }

        [Test]
        public void IsNotGoodWithBadDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Bad);

            Assert.That(DataValue.IsNotGood(dv), Is.True);
        }

        [Test]
        public void IsNotGoodWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsNotGood(dv), Is.False);
        }

        [Test]
        public void IsNotGoodWithNullReturnsTrue()
        {
            Assert.That(DataValue.IsNotGood(null), Is.True);
        }

        [Test]
        public void IsUncertainWithUncertainDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Uncertain);

            Assert.That(DataValue.IsUncertain(dv), Is.True);
        }

        [Test]
        public void IsUncertainWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsUncertain(dv), Is.False);
        }

        [Test]
        public void IsUncertainWithNullReturnsFalse()
        {
            Assert.That(DataValue.IsUncertain(null), Is.False);
        }

        [Test]
        public void IsNotUncertainWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsNotUncertain(dv), Is.True);
        }

        [Test]
        public void IsNotUncertainWithUncertainDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Uncertain);

            Assert.That(DataValue.IsNotUncertain(dv), Is.False);
        }

        [Test]
        public void IsNotUncertainWithNullReturnsFalse()
        {
            Assert.That(DataValue.IsNotUncertain(null), Is.False);
        }

        [Test]
        public void IsBadWithBadDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Bad);

            Assert.That(DataValue.IsBad(dv), Is.True);
        }

        [Test]
        public void IsBadWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsBad(dv), Is.False);
        }

        [Test]
        public void IsBadWithNullReturnsTrue()
        {
            Assert.That(DataValue.IsBad(null), Is.True);
        }

        [Test]
        public void IsNotBadWithGoodDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Good);

            Assert.That(DataValue.IsNotBad(dv), Is.True);
        }

        [Test]
        public void IsNotBadWithBadDataValue()
        {
            var dv = DataValue.FromStatusCode(StatusCodes.Bad);

            Assert.That(DataValue.IsNotBad(dv), Is.False);
        }

        [Test]
        public void IsNotBadWithNullReturnsFalse()
        {
            Assert.That(DataValue.IsNotBad(null), Is.False);
        }

        [Test]
        public void GetValueOrDefaultReturnsValueForGoodStatus()
        {
            var dv = new DataValue(new Variant(42));

#pragma warning disable CS0618 // Type or member is obsolete
            int result = dv.GetValueOrDefault<int>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueOrDefaultReturnsDefaultForBadStatus()
        {
            var dv = new DataValue(new Variant(42), StatusCodes.Bad);

#pragma warning disable CS0618 // Type or member is obsolete
            int result = dv.GetValueOrDefault<int>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.Zero);
        }

        [Test]
        public void GetValueOrDefaultReturnsDefaultStringForBadStatus()
        {
            var dv = new DataValue(new Variant("hello"), StatusCodes.Bad);

#pragma warning disable CS0618 // Type or member is obsolete
            string result = dv.GetValueOrDefault<string>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueOrDefaultThrowsOnTypeMismatch()
        {
            // Use Argument (IEncodeable) as target type which cannot be cast from an int variant
            var dv = new DataValue(new Variant(42));

#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(dv.GetValueOrDefault<Argument>,
                Throws.TypeOf<ServiceResultException>());
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void GetValueOrDefaultNullValueForValueTypeThrows()
        {
            var dv = new DataValue();

#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(dv.GetValueOrDefault<int>,
                Throws.TypeOf<ServiceResultException>());
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void GetValueOrDefaultNullValueForReferenceTypeReturnsDefault()
        {
            var dv = new DataValue();

#pragma warning disable CS0618 // Type or member is obsolete
            string result = dv.GetValueOrDefault<string>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueOrDefaultWithExtensionObjectExtractsEncodeable()
        {
            var arg = new Argument("test", new NodeId(1), -1, "desc");
            var ext = new ExtensionObject(arg);
            var dv = new DataValue(new Variant(ext));

#pragma warning disable CS0618 // Type or member is obsolete
            Argument result = dv.GetValueOrDefault<Argument>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("test"));
        }

        [Test]
        public void GetValueGenericReturnsValueForGoodStatus()
        {
            var dv = new DataValue(new Variant(42));

            int result = dv.GetValue(0);

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueGenericReturnsDefaultForNotGoodStatus()
        {
            var dv = new DataValue(new Variant(42), StatusCodes.Bad);

            int result = dv.GetValue(-1);

            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void GetValueGenericReturnsDefaultForUncertainStatus()
        {
            // Uncertain is considered not good
            var dv = new DataValue(new Variant(42), StatusCodes.Uncertain);

            int result = dv.GetValue(-1);

            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void GetValueGenericReturnsDefaultWhenTypeMismatch()
        {
            // Use Argument as target type which cannot be cast from an int variant
            var dv = new DataValue(new Variant(42));
            var defaultArg = new Argument("default", new NodeId(0), -1, "default");

            Argument result = dv.GetValue(defaultArg);

            Assert.That(result, Is.SameAs(defaultArg));
        }

        [Test]
        public void GetValueGenericWithExtensionObjectExtractsEncodeable()
        {
            var arg = new Argument("test", new NodeId(1), -1, "desc");
            var ext = new ExtensionObject(arg);
            var dv = new DataValue(new Variant(ext));

            Argument result = dv.GetValue<Argument>(null);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("test"));
        }

        [Test]
        public void GetValueGenericWithStringType()
        {
            var dv = new DataValue(new Variant("hello"));

            string result = dv.GetValue("default");

            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void EqualDataValuesHaveSameHashCode()
        {
            var dv1 = new DataValue(new Variant(42));
            var dv2 = new DataValue(new Variant(42));

            Assert.That(dv1.GetHashCode(), Is.EqualTo(dv2.GetHashCode()));
        }

        [Test]
        public void DefaultDataValuesAreEqual()
        {
            var dv1 = new DataValue();
            var dv2 = new DataValue();

            Assert.That(dv1, Is.EqualTo(dv2));
            Assert.That(dv1, Is.EqualTo(dv2));
        }

        [Test]
        public void ToStringWithNullVariantReturnsEmptyOrNull()
        {
            var dv = new DataValue();

            string result = dv.ToString();

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithVariantSetsValue()
        {
            var dv = new DataValue(new Variant(true));

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant(true)));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ConstructorWithVariantAndStatusCode()
        {
            var dv = new DataValue(new Variant("test"), StatusCodes.Uncertain);

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("test")));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
        }

        [Test]
        public void PicoSecondProperties()
        {
            var dv = new DataValue
            {
                SourcePicoseconds = 12345,
                ServerPicoseconds = 54321
            };

            Assert.That(dv.SourcePicoseconds, Is.EqualTo((ushort)12345));
            Assert.That(dv.ServerPicoseconds, Is.EqualTo((ushort)54321));
        }

        [Test]
        public void ClonePreservesAllProperties()
        {
            var sourceTime = new DateTimeUtc(2024, 7, 1, 0, 0, 0);
            var serverTime = new DateTimeUtc(2024, 7, 1, 0, 0, 1);

            var original = new DataValue(new Variant(99), StatusCodes.Uncertain, sourceTime, serverTime)
            {
                SourcePicoseconds = 111,
                ServerPicoseconds = 222
            };

            var clone = (DataValue)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone, Is.EqualTo(original));
        }

        [Test]
        public void GetValueOrDefaultWithBoolType()
        {
            var dv = new DataValue(new Variant(true));

#pragma warning disable CS0618 // Type or member is obsolete
            bool result = dv.GetValueOrDefault<bool>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueOrDefaultWithDoubleType()
        {
            var dv = new DataValue(new Variant(3.14));

#pragma warning disable CS0618 // Type or member is obsolete
            double result = dv.GetValueOrDefault<double>();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.That(result, Is.EqualTo(3.14));
        }
    }
}
