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

#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Coverage tests for DataValue: constructors, Equals, GetHashCode,
    /// ToString, Clone, Value property, static quality methods,
    /// GetValue, GetValueOrDefault, and GetValue&lt;T&gt;.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataValueTests
    {
        #region Constructors

        [Test]
        public void DefaultConstructorSetsDefaults()
        {
            var dv = new DataValue();

            Assert.That(dv.WrappedValue.IsNull, Is.True);
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dv.SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(dv.SourcePicoseconds, Is.EqualTo((ushort)0));
            Assert.That(dv.ServerPicoseconds, Is.EqualTo((ushort)0));
        }

        [Test]
        public void CopyConstructorCopiesAllFields()
        {
            // Covers lines 103-108 (copy constructor body)
            var sourceTime = new DateTimeUtc(2024, 6, 15, 10, 30, 0);
            var serverTime = new DateTimeUtc(2024, 6, 15, 10, 30, 1);
            var original = new DataValue(new Variant(42), StatusCodes.Good, sourceTime, serverTime)
            {
                SourcePicoseconds = 100,
                ServerPicoseconds = 200
            };

            var copy = new DataValue(original);

            Assert.That(copy.WrappedValue, Is.EqualTo(original.WrappedValue));
            Assert.That(copy.StatusCode, Is.EqualTo(original.StatusCode));
            Assert.That(copy.SourceTimestamp, Is.EqualTo(sourceTime));
            Assert.That(copy.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(copy.SourcePicoseconds, Is.EqualTo((ushort)100));
            Assert.That(copy.ServerPicoseconds, Is.EqualTo((ushort)200));
        }

        [Test]
        public void CopyConstructorThrowsOnNull()
        {
            // Covers lines 99-100 (null check + throw)
            Assert.That(() => new DataValue((DataValue)null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithStatusCodeAndServerTimestamp()
        {
            // Covers lines 140-145
            var serverTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            var dv = new DataValue(StatusCodes.Bad, serverTime);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(dv.ServerTimestamp, Is.EqualTo(serverTime));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public void ConstructorWithVariantStatusCodeAndSourceTimestamp()
        {
            // Covers lines 166-173
            var sourceTime = new DateTimeUtc(2024, 3, 20, 12, 0, 0);
            var dv = new DataValue(new Variant("hello"), StatusCodes.Uncertain, sourceTime);

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("hello")));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(dv.SourceTimestamp, Is.EqualTo(sourceTime));
        }

        [Test]
        public void ConstructorWithAllFourParameters()
        {
            // Covers lines 182-194
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
            // Covers lines 129-133
            var dv = new DataValue(StatusCodes.BadUnexpectedError);

            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(dv.WrappedValue.IsNull, Is.True);
        }

        #endregion

        #region Equals

        [Test]
        public void EqualsObjectWithNull()
        {
            // Covers line 212 (null => false)
            var dv = new DataValue();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(dv.Equals((object)null), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsObjectWithDataValue()
        {
            // Covers line 213 (DataValue value => Equals(value))
            var dv1 = new DataValue(new Variant(42));
            var dv2 = new DataValue(new Variant(42));

            Assert.That(dv1.Equals((object)dv2), Is.True);
        }

        [Test]
        public void EqualsObjectWithNonDataValue()
        {
            // Covers line 214 (_ => base.Equals(obj))
            var dv = new DataValue();

            Assert.That(dv.Equals("not a DataValue"), Is.False);
        }

        [Test]
        public void EqualsDataValueSameReference()
        {
            // Covers lines 221-224 (ReferenceEquals)
            var dv = new DataValue(new Variant(42));

            Assert.That(dv.Equals(dv), Is.True);
        }

        [Test]
        public void EqualsDataValueWithNullOther()
        {
            // Covers lines 226-229 (other is null)
            var dv = new DataValue(new Variant(42));

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(dv.Equals((DataValue)null), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsReturnsFalseForDifferentStatusCodes()
        {
            // Covers lines 231-234
            var dv1 = new DataValue(new Variant(42), StatusCodes.Good);
            var dv2 = new DataValue(new Variant(42), StatusCodes.Bad);

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentServerTimestamps()
        {
            // Covers lines 236-239
            var dv1 = new DataValue(new Variant(42)) { ServerTimestamp = new DateTimeUtc(2024, 1, 1) };
            var dv2 = new DataValue(new Variant(42)) { ServerTimestamp = new DateTimeUtc(2025, 1, 1) };

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentSourceTimestamps()
        {
            // Covers lines 241-244
            var dv1 = new DataValue(new Variant(42)) { SourceTimestamp = new DateTimeUtc(2024, 1, 1) };
            var dv2 = new DataValue(new Variant(42)) { SourceTimestamp = new DateTimeUtc(2025, 1, 1) };

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentServerPicoseconds()
        {
            // Covers lines 246-249
            var dv1 = new DataValue(new Variant(42)) { ServerPicoseconds = 100 };
            var dv2 = new DataValue(new Variant(42)) { ServerPicoseconds = 200 };

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentSourcePicoseconds()
        {
            // Covers lines 251-254
            var dv1 = new DataValue(new Variant(42)) { SourcePicoseconds = 100 };
            var dv2 = new DataValue(new Variant(42)) { SourcePicoseconds = 200 };

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentValues()
        {
            // Covers lines 256-259
            var dv1 = new DataValue(new Variant(42));
            var dv2 = new DataValue(new Variant(99));

            Assert.That(dv1.Equals(dv2), Is.False);
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalDataValues()
        {
            // Covers line 261 (return true)
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

            Assert.That(dv1.Equals(dv2), Is.True);
        }

        #endregion

        #region Operators == and !=

        [Test]
        public void EqualityOperatorBothNull()
        {
            // Covers line 267 (a is null ? b is null : ...)
            DataValue a = null;
            DataValue b = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a == b, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorLeftNullRightNotNull()
        {
            DataValue a = null;
            var b = new DataValue();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a == b, Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorLeftNotNullRightNull()
        {
            var a = new DataValue();
            DataValue b = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a == b, Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorEqualValues()
        {
            var a = new DataValue(new Variant(42));
            var b = new DataValue(new Variant(42));

            Assert.That(a == b, Is.True);
        }

        [Test]
        public void InequalityOperatorDifferentValues()
        {
            // Covers lines 272-274
            var a = new DataValue(new Variant(1));
            var b = new DataValue(new Variant(2));

            Assert.That(a != b, Is.True);
        }

        [Test]
        public void InequalityOperatorEqualValues()
        {
            var a = new DataValue(new Variant(42));
            var b = new DataValue(new Variant(42));

            Assert.That(a != b, Is.False);
        }

        #endregion

        #region GetHashCode

        [Test]
        public void GetHashCodeWithNonNullValue()
        {
            // Covers lines 281-283 (value is not null path)
            var dv = new DataValue(new Variant(42));

            var hash = dv.GetHashCode();

            Assert.That(hash, Is.EqualTo(new Variant(42).GetHashCode()));
        }

        [Test]
        public void GetHashCodeWithNullValue()
        {
            // Covers line 286 (return StatusCode.GetHashCode)
            var dv = new DataValue()
            {
                StatusCode = StatusCodes.BadUnexpectedError
            };

            var hash = dv.GetHashCode();

            Assert.That(hash, Is.EqualTo(StatusCodes.BadUnexpectedError.GetHashCode()));
        }

        #endregion

        #region ToString

        [Test]
        public void ToStringReturnsValueString()
        {
            // Covers lines 293-294 (ToString() calls ToString(null, null))
            var dv = new DataValue(new Variant(42));

            var result = dv.ToString();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void ToStringWithNullFormatReturnsValue()
        {
            // Covers lines 305-308
            var dv = new DataValue(new Variant("test"));

            var result = dv.ToString(null, null);

            Assert.That(result, Does.Contain("test"));
        }

        [Test]
        public void ToStringWithFormatThrowsFormatException()
        {
            // Covers line 310
            var dv = new DataValue(new Variant(42));

            Assert.That(() => dv.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        #endregion

        #region Clone

        [Test]
        public void CloneReturnsDeepCopy()
        {
            // Covers lines 314-317 (Clone) and 323-325 (MemberwiseClone)
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

        #endregion

        #region Value property

        [Test]
        public void ValueGetterReturnsBoxedObject()
        {
            // Covers line 333
            var dv = new DataValue(new Variant(42));

            Assert.That(dv.Value, Is.EqualTo(42));
        }

        [Test]
        public void ValueSetterSetsVariant()
        {
            // Covers line 334
            var dv = new DataValue
            {
                Value = "hello"
            };

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("hello")));
        }

        [Test]
        public void WrappedValueGetterAndSetter()
        {
            // Covers lines 345-347
            var dv = new DataValue();
            var variant = new Variant(true);

            dv.WrappedValue = variant;

            Assert.That(dv.WrappedValue, Is.EqualTo(variant));
        }

        #endregion

        #region Static quality methods: IsGood, IsNotGood, IsUncertain, IsNotUncertain, IsBad, IsNotBad

        [Test]
        public void IsGoodWithGoodDataValue()
        {
            // Covers lines 385-387
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsGood(dv), Is.True);
        }

        [Test]
        public void IsGoodWithBadDataValue()
        {
            var dv = new DataValue(StatusCodes.Bad);

            Assert.That(DataValue.IsGood(dv), Is.False);
        }

        [Test]
        public void IsGoodWithNullReturnsFalse()
        {
            // Covers line 390
            Assert.That(DataValue.IsGood(null), Is.False);
        }

        [Test]
        public void IsNotGoodWithBadDataValue()
        {
            // Covers lines 399-401
            var dv = new DataValue(StatusCodes.Bad);

            Assert.That(DataValue.IsNotGood(dv), Is.True);
        }

        [Test]
        public void IsNotGoodWithGoodDataValue()
        {
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsNotGood(dv), Is.False);
        }

        [Test]
        public void IsNotGoodWithNullReturnsTrue()
        {
            // Covers line 404
            Assert.That(DataValue.IsNotGood(null), Is.True);
        }

        [Test]
        public void IsUncertainWithUncertainDataValue()
        {
            // Covers lines 413-415
            var dv = new DataValue(StatusCodes.Uncertain);

            Assert.That(DataValue.IsUncertain(dv), Is.True);
        }

        [Test]
        public void IsUncertainWithGoodDataValue()
        {
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsUncertain(dv), Is.False);
        }

        [Test]
        public void IsUncertainWithNullReturnsFalse()
        {
            // Covers line 418
            Assert.That(DataValue.IsUncertain(null), Is.False);
        }

        [Test]
        public void IsNotUncertainWithGoodDataValue()
        {
            // Covers lines 427-429
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsNotUncertain(dv), Is.True);
        }

        [Test]
        public void IsNotUncertainWithUncertainDataValue()
        {
            var dv = new DataValue(StatusCodes.Uncertain);

            Assert.That(DataValue.IsNotUncertain(dv), Is.False);
        }

        [Test]
        public void IsNotUncertainWithNullReturnsFalse()
        {
            // Covers line 432
            Assert.That(DataValue.IsNotUncertain(null), Is.False);
        }

        [Test]
        public void IsBadWithBadDataValue()
        {
            // Covers lines 441-443
            var dv = new DataValue(StatusCodes.Bad);

            Assert.That(DataValue.IsBad(dv), Is.True);
        }

        [Test]
        public void IsBadWithGoodDataValue()
        {
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsBad(dv), Is.False);
        }

        [Test]
        public void IsBadWithNullReturnsTrue()
        {
            // Covers line 446
            Assert.That(DataValue.IsBad(null), Is.True);
        }

        [Test]
        public void IsNotBadWithGoodDataValue()
        {
            // Covers lines 455-457
            var dv = new DataValue(StatusCodes.Good);

            Assert.That(DataValue.IsNotBad(dv), Is.True);
        }

        [Test]
        public void IsNotBadWithBadDataValue()
        {
            var dv = new DataValue(StatusCodes.Bad);

            Assert.That(DataValue.IsNotBad(dv), Is.False);
        }

        [Test]
        public void IsNotBadWithNullReturnsFalse()
        {
            // Covers line 460
            Assert.That(DataValue.IsNotBad(null), Is.False);
        }

        #endregion

        #region GetValue(Type expectedType)

        [Test]
        public void GetValueReturnsValueWhenTypeMatches()
        {
            // Covers lines 469, 471, 485 (type matches), 494
            var dv = new DataValue(new Variant(42));

            var result = dv.GetValue(typeof(int));

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueReturnsNullForBadStatusCode()
        {
            // Covers lines 474-477
            var dv = new DataValue(new Variant(42), StatusCodes.Bad);

            var result = dv.GetValue(typeof(int));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueThrowsOnTypeMismatch()
        {
            // Covers lines 487-491
            var dv = new DataValue(new Variant(42));

            Assert.That(() => dv.GetValue(typeof(string)),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void GetValueReturnsValueWhenExpectedTypeIsNull()
        {
            // Covers line 471 (expectedType == null) and 494
            var dv = new DataValue(new Variant(42));

            var result = dv.GetValue(null);

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueReturnsNullWhenValueIsNull()
        {
            // Covers line 471 (value == null) and 494
            var dv = new DataValue();

            var result = dv.GetValue(typeof(int));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueWithExtensionObjectExtractsEncodeable()
        {
            // Covers lines 479-483 (ExtensionObject extraction)
            var arg = new Argument("test", new NodeId(1), -1, "desc");
            var ext = new ExtensionObject(arg);
            var dv = new DataValue(new Variant(ext));

            var result = dv.GetValue(typeof(Argument));

            Assert.That(result, Is.InstanceOf<Argument>());
            Assert.That(((Argument)result).Name, Is.EqualTo("test"));
        }

        [Test]
        public void GetValueWithExtensionObjectThrowsOnTypeMismatch()
        {
            // Covers lines 479-491 (ExtensionObject with wrong target type)
            var arg = new Argument("test", new NodeId(1), -1, "desc");
            var ext = new ExtensionObject(arg);
            var dv = new DataValue(new Variant(ext));

            // Requesting int, but value is an Argument wrapped in ExtensionObject
            Assert.That(() => dv.GetValue(typeof(int)),
                Throws.TypeOf<ServiceResultException>());
        }

        #endregion

        #region GetValueOrDefault<T>()

        [Test]
        public void GetValueOrDefaultReturnsValueForGoodStatus()
        {
            // Covers lines 517, 525-528
            var dv = new DataValue(new Variant(42));

            var result = dv.GetValueOrDefault<int>();

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueOrDefaultReturnsDefaultForBadStatus()
        {
            // Covers lines 512-514
            var dv = new DataValue(new Variant(42), StatusCodes.Bad);

            var result = dv.GetValueOrDefault<int>();

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetValueOrDefaultReturnsDefaultStringForBadStatus()
        {
            var dv = new DataValue(new Variant("hello"), StatusCodes.Bad);

            var result = dv.GetValueOrDefault<string>();

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueOrDefaultThrowsOnTypeMismatch()
        {
            // Covers lines 530-533
            // Use Argument (IEncodeable) as target type which cannot be cast from an int variant
            var dv = new DataValue(new Variant(42));

            Assert.That(() => dv.GetValueOrDefault<Argument>(),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void GetValueOrDefaultNullValueForValueTypeThrows()
        {
            // Covers lines 537-543 (null value + value type)
            var dv = new DataValue();

            Assert.That(() => dv.GetValueOrDefault<int>(),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void GetValueOrDefaultNullValueForReferenceTypeReturnsDefault()
        {
            // Covers line 545 (null value + reference type returns default)
            var dv = new DataValue();

            var result = dv.GetValueOrDefault<string>();

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueOrDefaultWithExtensionObjectExtractsEncodeable()
        {
            // Covers lines 519-524 (ExtensionObject extraction path)
            var arg = new Argument("test", new NodeId(1), -1, "desc");
            var ext = new ExtensionObject(arg);
            var dv = new DataValue(new Variant(ext));

            Argument result = dv.GetValueOrDefault<Argument>();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("test"));
        }

        #endregion

        #region GetValue<T>(T defaultValue)

        [Test]
        public void GetValueGenericReturnsValueForGoodStatus()
        {
            // Covers lines 572-575
            var dv = new DataValue(new Variant(42));

            var result = dv.GetValue(0);

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueGenericReturnsDefaultForNotGoodStatus()
        {
            // Covers lines 561-563
            var dv = new DataValue(new Variant(42), StatusCodes.Bad);

            var result = dv.GetValue(-1);

            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void GetValueGenericReturnsDefaultForUncertainStatus()
        {
            // Uncertain is considered not good
            var dv = new DataValue(new Variant(42), StatusCodes.Uncertain);

            var result = dv.GetValue(-1);

            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void GetValueGenericReturnsDefaultWhenTypeMismatch()
        {
            // Covers line 577 (fallback to defaultValue when cast fails)
            // Use Argument as target type which cannot be cast from an int variant
            var dv = new DataValue(new Variant(42));
            var defaultArg = new Argument("default", new NodeId(0), -1, "default");

            Argument result = dv.GetValue(defaultArg);

            Assert.That(result, Is.SameAs(defaultArg));
        }

        [Test]
        public void GetValueGenericWithExtensionObjectExtractsEncodeable()
        {
            // Covers lines 566-571 (ExtensionObject extraction)
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

            var result = dv.GetValue("default");

            Assert.That(result, Is.EqualTo("hello"));
        }

        #endregion

        #region Edge cases and combined scenarios

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

            Assert.That(dv1.Equals(dv2), Is.True);
            Assert.That(dv1 == dv2, Is.True);
        }

        [Test]
        public void ToStringWithNullVariantReturnsEmptyOrNull()
        {
            var dv = new DataValue();

            var result = dv.ToString();

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithVariantSetsValue()
        {
            // Covers lines 119-123
            var dv = new DataValue(new Variant(true));

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant(true)));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ConstructorWithVariantAndStatusCode()
        {
            // Covers lines 153-158
            var dv = new DataValue(new Variant("test"), StatusCodes.Uncertain);

            Assert.That(dv.WrappedValue, Is.EqualTo(new Variant("test")));
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
        }

        [Test]
        public void PicoSecondProperties()
        {
            // Covers lines 365-366, 376-377
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

            var clone = (DataValue)((ICloneable)original).Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Equals(original), Is.True);
        }

        [Test]
        public void GetValueWithBadStatusAndExpectedTypeReturnsNull()
        {
            var dv = new DataValue(new Variant("text"), StatusCodes.BadUnexpectedError);

            var result = dv.GetValue(typeof(string));

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetValueOrDefaultWithBoolType()
        {
            var dv = new DataValue(new Variant(true));

            var result = dv.GetValueOrDefault<bool>();

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueOrDefaultWithDoubleType()
        {
            var dv = new DataValue(new Variant(3.14));

            var result = dv.GetValueOrDefault<double>();

            Assert.That(result, Is.EqualTo(3.14));
        }

        #endregion
    }
}
