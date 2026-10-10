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
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.ContentFilter
{
    [TestFixture]
    [Category("ContentFilter")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class FilterEvaluatorCoverageTests
    {
        private static readonly int[] s_twoIntegers = [1, 2];
        private IFilterContext m_context;
        private CoverageFilterTarget m_target;

        [SetUp]
        public void SetUp()
        {
            var namespaceTable = new NamespaceTable();
            var typeTable = new TypeTable(namespaceTable);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = new FilterContext(namespaceTable, typeTable, telemetry);
            m_target = new CoverageFilterTarget();
        }

        [Test]
        public void AndFalseLeftShortCircuitsToFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.And,
                new LiteralOperand(Variant.From(false)),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void AndNullLeftWithTrueRightIsNullAndYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.And,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(true)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void AndNullLeftWithFalseRightYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.And,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(false)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void AndTrueLeftWithNullRightIsNullAndYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.And,
                new LiteralOperand(Variant.From(true)),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void OrTrueLeftShortCircuitsToTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.Or,
                new LiteralOperand(Variant.From(true)),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void OrNullLeftWithFalseRightIsNullAndYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Or,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(false)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void OrNullLeftWithTrueRightYieldsTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.Or,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(true)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void OrFalseLeftWithNullRightIsNullAndYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Or,
                new LiteralOperand(Variant.From(false)),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void NotNullOperandIsNullAndYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Not,
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void GreaterThanWithIncomparableTypesYieldsFalse()
        {
            Assert.That(
                BinaryFilter(FilterOperator.GreaterThan, Variant.From("abc"), Variant.From(42))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void GreaterThanOrEqualWithIncomparableTypesYieldsFalse()
        {
            Assert.That(
                BinaryFilter(FilterOperator.GreaterThanOrEqual, Variant.From("abc"), Variant.From(42))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void LessThanWithIncomparableTypesYieldsFalse()
        {
            Assert.That(
                BinaryFilter(FilterOperator.LessThan, Variant.From("abc"), Variant.From(42))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void LessThanOrEqualWithIncomparableTypesYieldsFalse()
        {
            Assert.That(
                BinaryFilter(FilterOperator.LessThanOrEqual, Variant.From("abc"), Variant.From(42))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void BetweenValueBelowMinYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Between,
                new LiteralOperand(Variant.From(0)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void BetweenValueAtLowerBoundaryYieldsTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.Between,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void BetweenValueAtUpperBoundaryYieldsTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.Between,
                new LiteralOperand(Variant.From(10)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void BetweenWithIncomparableMinYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Between,
                new LiteralOperand(Variant.From("abc")),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void BetweenWithIncomparableMaxYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Between,
                new LiteralOperand(Variant.From("m")),
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From(10)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void InListWithStringMemberYieldsTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From("b")),
                new LiteralOperand(Variant.From("b")),
                new LiteralOperand(Variant.From("c")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        /// <summary>
        /// Verifies a leading string mismatch does not prevent InList from finding a later matching operand.
        /// </summary>
        [Test]
        public void InListWithStringMemberAfterLeadingMismatchYieldsTrue()
        {
            // A leading mismatch only rules out that operand; the remaining
            // list entries are still compared.
            ContentFilterElement element = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From("x")),
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From("x")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void InListWithStringNonMemberYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From("x")),
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From("b")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        /// <summary>
        /// Verifies InList evaluates every string operand position.
        /// </summary>
        [TestCase("a")]
        [TestCase("b")]
        [TestCase("c")]
        public void InListMatchesEveryStringOperandPosition(string value)
        {
            ContentFilterElement element = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From(value)),
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From("b")),
                new LiteralOperand(Variant.From("c")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void LikeWithLocalizedTextOperandsMatches()
        {
            ContentFilterElement element = Element(
                FilterOperator.Like,
                new LiteralOperand(Variant.From(new LocalizedText("Hello World"))),
                new LiteralOperand(Variant.From(new LocalizedText("Hello%"))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void LikeWithNullOperandYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Like,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From("abc")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void CastWithNullValueYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(NodeId.Parse("i=1"))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void CastWithNonNodeIdDataTypeYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(42)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void CastToUnknownDataTypeYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(NodeId.Parse("i=9999"))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void CastIntegerToBooleanYieldsTrue()
        {
            ContentFilterElement element = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(NodeId.Parse("i=1"))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void BitwiseAndProducesMaskedValue()
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new ElementOperand(1),
                new LiteralOperand(Variant.From(0x0F)));
            ContentFilterElement bitwise = Element(
                FilterOperator.BitwiseAnd,
                new LiteralOperand(Variant.From(0xFF)),
                new LiteralOperand(Variant.From(0x0F)));
            Assert.That(Filter(equals, bitwise).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void BitwiseOrProducesCombinedValue()
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new ElementOperand(1),
                new LiteralOperand(Variant.From(0xFF)));
            ContentFilterElement bitwise = Element(
                FilterOperator.BitwiseOr,
                new LiteralOperand(Variant.From(0xF0)),
                new LiteralOperand(Variant.From(0x0F)));
            Assert.That(Filter(equals, bitwise).Evaluate(m_context, m_target), Is.True);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3: an element with a NULL operand evaluates to NULL
        /// (not to an ordered comparison with NULL sorted first).
        /// </summary>
        [TestCase(FilterOperator.Equals, true, true)]
        [TestCase(FilterOperator.Equals, true, false)]
        [TestCase(FilterOperator.GreaterThan, true, false)]
        [TestCase(FilterOperator.GreaterThan, false, true)]
        [TestCase(FilterOperator.GreaterThanOrEqual, true, false)]
        [TestCase(FilterOperator.GreaterThanOrEqual, false, true)]
        [TestCase(FilterOperator.LessThan, true, false)]
        [TestCase(FilterOperator.LessThan, false, true)]
        [TestCase(FilterOperator.LessThanOrEqual, true, false)]
        [TestCase(FilterOperator.LessThanOrEqual, false, true)]
        public void RelationalOperatorWithNullOperandIsNull(
            FilterOperator op,
            bool lhsNull,
            bool rhsNull)
        {
            Variant lhs = lhsNull ? Variant.Null : Variant.From(100.0);
            Variant rhs = rhsNull ? Variant.Null : Variant.From(100.0);
            ContentFilterElement compare = Element(
                op,
                new LiteralOperand(lhs),
                new LiteralOperand(rhs));

            // the element result itself is NULL, so it neither passes nor
            // turns into TRUE when negated.
            Assert.That(BinaryFilter(op, lhs, rhs).Evaluate(m_context, m_target), Is.False);
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), compare)
                    .Evaluate(m_context, m_target),
                Is.True);
            Assert.That(
                Filter(Element(FilterOperator.Not, new ElementOperand(1)), compare)
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void LessThanMissingFieldDoesNotPassFilter()
        {
            // A field missing from the event resolves to NULL; NULL < 100 must
            // not be TRUE.
            Assert.That(
                BinaryFilter(FilterOperator.LessThan, Variant.Null, Variant.From(100.0))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void BetweenWithNullOperandIsNull(bool valueNull, bool minNull, bool maxNull)
        {
            ContentFilterElement between = Element(
                FilterOperator.Between,
                new LiteralOperand(valueNull ? Variant.Null : Variant.From(5)),
                new LiteralOperand(minNull ? Variant.Null : Variant.From(1)),
                new LiteralOperand(maxNull ? Variant.Null : Variant.From(10)));
            Assert.That(Filter(between).Evaluate(m_context, m_target), Is.False);
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), between)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void InListWithNullValueIsNull()
        {
            ContentFilterElement inList = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(1)));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), inList)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void InListWithNullEntryAndNoMatchIsNull()
        {
            ContentFilterElement inList = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(2)));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), inList)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void InListWithNullEntryAndMatchYieldsTrue()
        {
            ContentFilterElement inList = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(1)));
            Assert.That(Filter(inList).Evaluate(m_context, m_target), Is.True);
        }

        /// <summary>
        /// A String-typed operand holding a null string (e.g. a decoded
        /// string of length -1) is NULL and must not throw.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void EqualsWithNullStringOperandIsNull(bool lhsNull, bool rhsNull)
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new LiteralOperand(lhsNull ? Variant.From((string)null!) : Variant.From("a")),
                new LiteralOperand(rhsNull ? Variant.From((string)null!) : Variant.From("a")));
            Assert.That(Filter(equals).Evaluate(m_context, m_target), Is.False);
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), equals)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void InListWithNullStringValueIsNull()
        {
            ContentFilterElement inList = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From((string)null!)),
                new LiteralOperand(Variant.From("a")));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), inList)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void InListWithNullStringEntryAndNoMatchIsNull()
        {
            ContentFilterElement inList = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From((string)null!)),
                new LiteralOperand(Variant.From("b")));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), inList)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3 IsNull: a String-typed Variant holding a null
        /// string is a null value, like it is for every other operator.
        /// </summary>
        [Test]
        public void IsNullWithNullStringOperandYieldsTrue()
        {
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new LiteralOperand(Variant.From((string)null!))))
                    .Evaluate(m_context, m_target),
                Is.True);
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new LiteralOperand(Variant.From(string.Empty))))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3: a NULL Like operand makes the element NULL, so
        /// it does not turn into TRUE when negated.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void LikeWithNullStringOperandIsNull(bool lhsNull, bool rhsNull)
        {
            ContentFilterElement like = Element(
                FilterOperator.Like,
                new LiteralOperand(lhsNull ? Variant.From((string)null!) : Variant.From("abc")),
                new LiteralOperand(rhsNull ? Variant.From((string)null!) : Variant.From("a%")));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), like)
                    .Evaluate(m_context, m_target),
                Is.True);
            Assert.That(
                Filter(Element(FilterOperator.Not, new ElementOperand(1)), like)
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3 Table 121 marks these conversions X, so Cast is
        /// NULL even though the general-purpose Variant converters support them.
        /// </summary>
        [TestCase(BuiltInType.StatusCode, BuiltInType.String)]
        [TestCase(BuiltInType.String, BuiltInType.StatusCode)]
        [TestCase(BuiltInType.String, BuiltInType.XmlElement)]
        [TestCase(BuiltInType.String, BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement, BuiltInType.String)]
        [TestCase(BuiltInType.ExtensionObject, BuiltInType.String)]
        public void CastNotInTable121IsNull(BuiltInType sourceType, BuiltInType targetType)
        {
            Variant value = sourceType switch
            {
                BuiltInType.StatusCode => new Variant(new StatusCode(0x80000000u)),
                BuiltInType.String => Variant.From("0A0B"),
                BuiltInType.XmlElement => new Variant(XmlElement.From("<a>1</a>")),
                _ => new Variant(new ExtensionObject(new Argument { Name = "x" }))
            };
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(sourceType));
            ContentFilterElement cast = Element(
                FilterOperator.Cast,
                new LiteralOperand(value),
                new LiteralOperand(Variant.From(new NodeId((uint)targetType))));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), cast)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3 Table 121: QualifiedName converts implicitly to
        /// LocalizedText.
        /// </summary>
        [Test]
        public void CastQualifiedNameToLocalizedTextUsesName()
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new ElementOperand(1),
                new LiteralOperand(Variant.From(new LocalizedText("Name"))));
            ContentFilterElement cast = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.From(new QualifiedName("Name", 2))),
                new LiteralOperand(Variant.From(DataTypeIds.LocalizedText)));
            Assert.That(Filter(equals, cast).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void CastWithNullStringOperandIsNull()
        {
            ContentFilterElement cast = Element(
                FilterOperator.Cast,
                new LiteralOperand(Variant.From((string)null!)),
                new LiteralOperand(Variant.From(DataTypeIds.String)));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), cast)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void OrNullStringCompareWithTrueRightYieldsTrue()
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new LiteralOperand(Variant.From((string)null!)),
                new LiteralOperand(Variant.From("a")));
            Assert.That(
                Filter(
                    Element(
                        FilterOperator.Or,
                        new ElementOperand(1),
                        new LiteralOperand(Variant.From(true))),
                    equals)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3: the bitwise result matches the size of the largest
        /// operand.
        /// </summary>
        [TestCase(FilterOperator.BitwiseOr, 0x101u)]
        [TestCase(FilterOperator.BitwiseAnd, 0x0u)]
        public void BitwiseResultHasSizeOfLargestOperand(FilterOperator op, uint expected)
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new ElementOperand(1),
                new LiteralOperand(Variant.From(expected)));
            ContentFilterElement bitwise = Element(
                op,
                new LiteralOperand(Variant.From((byte)0x01)),
                new LiteralOperand(Variant.From(0x100u)));
            Assert.That(Filter(equals, bitwise).Evaluate(m_context, m_target), Is.True);
        }

        /// <summary>
        /// OPC 10000-4 7.7.3 Table 121: Double, Float and StatusCode only have an
        /// explicit conversion to an integer, so a bitwise element is NULL.
        /// </summary>
        [TestCase(FilterOperator.BitwiseAnd)]
        [TestCase(FilterOperator.BitwiseOr)]
        public void BitwiseWithNonIntegerOperandsIsNull(FilterOperator op)
        {
            Variant[] nonIntegers =
            [
                Variant.From(1.0),
                Variant.From(1.0f),
                new Variant(new StatusCode(1u)),
                Variant.From(Uuid.Empty),
                Variant.From("abc"),
                Variant.From(s_twoIntegers),
                Variant.Null
            ];
            foreach (Variant nonInteger in nonIntegers)
            {
                ContentFilterElement bitwise = Element(
                    op,
                    new LiteralOperand(nonInteger),
                    new LiteralOperand(Variant.From(1)));
                Assert.That(
                    Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), bitwise)
                        .Evaluate(m_context, m_target),
                    Is.True,
                    nonInteger.ToString());
            }
        }

        /// <summary>
        /// OPC 10000-4 7.7.3: Boolean and String operands have an implicit
        /// conversion to integers (Table 121) and a one element array converts
        /// implicitly to a scalar, so the bitwise operators accept them.
        /// </summary>
        [TestCase(FilterOperator.BitwiseAnd, true, 0x0F, 0x01)]
        [TestCase(FilterOperator.BitwiseOr, true, 0xF0, 0xF1)]
        [TestCase(FilterOperator.BitwiseAnd, "12", 0x0F, 0x0C)]
        [TestCase(FilterOperator.BitwiseOr, "12", 0x03, 0x0F)]
        public void BitwiseImplicitlyConvertsOperandsToIntegers(
            FilterOperator op,
            object lhs,
            int rhs,
            int expected)
        {
            Variant lhsValue = lhs is bool b ? Variant.From(b) : Variant.From((string)lhs);
            Variant[] lhsForms = [lhsValue, lhs is bool b2
                ? Variant.From(new[] { b2 })
                : Variant.From(new[] { (string)lhs })];
            Variant[] rhsForms = [Variant.From(rhs), Variant.From(new[] { rhs })];
            foreach (Variant lhsForm in lhsForms)
            {
                foreach (Variant rhsForm in rhsForms)
                {
                    ContentFilterElement equals = Element(
                        FilterOperator.Equals,
                        new ElementOperand(1),
                        new LiteralOperand(Variant.From(expected)));
                    ContentFilterElement bitwise = Element(
                        op,
                        new LiteralOperand(lhsForm),
                        new LiteralOperand(rhsForm));
                    Assert.That(
                        Filter(equals, bitwise).Evaluate(m_context, m_target),
                        Is.True,
                        $"{lhsForm} {op} {rhsForm}");
                }
            }
        }

        [Test]
        public void BitwiseWithBooleanOperandsUsesByte()
        {
            ContentFilterElement equals = Element(
                FilterOperator.Equals,
                new ElementOperand(1),
                new LiteralOperand(Variant.From((byte)1)));
            ContentFilterElement bitwise = Element(
                FilterOperator.BitwiseAnd,
                new LiteralOperand(Variant.From(true)),
                new LiteralOperand(Variant.From(true)));
            Assert.That(Filter(equals, bitwise).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void BitwiseWithUnparsableStringIsNull()
        {
            ContentFilterElement bitwise = Element(
                FilterOperator.BitwiseOr,
                new LiteralOperand(Variant.From("0x1G")),
                new LiteralOperand(Variant.From((byte)1)));
            Assert.That(
                Filter(Element(FilterOperator.IsNull, new ElementOperand(1)), bitwise)
                    .Evaluate(m_context, m_target),
                Is.True);
        }

        [Test]
        public void GreaterThanOrdersStringsOrdinally()
        {
            // 'a' (0x61) sorts after 'B' (0x42) ordinally; a culture aware
            // comparison puts "a" first.
            Assert.That(
                BinaryFilter(FilterOperator.GreaterThan, Variant.From("a"), Variant.From("B"))
                    .Evaluate(m_context, m_target),
                Is.True);
            Assert.That(
                BinaryFilter(FilterOperator.LessThan, Variant.From("a"), Variant.From("B"))
                    .Evaluate(m_context, m_target),
                Is.False);
        }

        [Test]
        public void OfTypeWithNonNodeIdOperandYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.OfType,
                new LiteralOperand(Variant.From(42)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void OfTypeWhenTargetThrowsYieldsFalse()
        {
            m_target.ThrowOnIsTypeOf = true;
            ContentFilterElement element = Element(
                FilterOperator.OfType,
                new LiteralOperand(Variant.From(new NodeId(1))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void InViewWithBasicTargetYieldsFalse()
        {
            ContentFilterElement element = Element(
                FilterOperator.InView,
                new LiteralOperand(Variant.From(new NodeId(1))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void InViewWithAdvancedTargetYieldsTrue()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsInViewResult = true };
            ContentFilterElement element = Element(
                FilterOperator.InView,
                new LiteralOperand(Variant.From(new NodeId(1))));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
        }

        [Test]
        public void InViewWithAdvancedTargetNonNodeIdYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsInViewResult = true };
            ContentFilterElement element = Element(
                FilterOperator.InView,
                new LiteralOperand(Variant.From(42)));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void InViewWhenAdvancedTargetThrowsYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { ThrowOnIsInView = true };
            ContentFilterElement element = Element(
                FilterOperator.InView,
                new LiteralOperand(Variant.From(new NodeId(1))));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToWithBasicTargetYieldsFalse()
        {
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
        }

        [Test]
        public void RelatedToWithAdvancedTargetYieldsTrue()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
        }

        [Test]
        public void RelatedToWithExplicitParametersYieldsTrue()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = Element(
                FilterOperator.RelatedTo,
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))),
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.From(2)),
                new LiteralOperand(Variant.From(true)),
                new LiteralOperand(Variant.From(false)));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void RelatedToNullStringParameterDoesNotMatch(int parameter)
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            FilterOperand[] operands =
            [
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))),
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(false)),
                new LiteralOperand(Variant.From(false))
            ];
            operands[parameter] = new LiteralOperand(Variant.From((string)null!));
            Assert.That(Filter(Element(FilterOperator.RelatedTo, operands)).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToWithOmittedSubtypeOperandsIncludesSubtypes()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))));

            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
            Assert.Multiple(() =>
            {
                // Part 4 7.7.4: both optional subtype operands default to true.
                Assert.That(advanced.LastIncludeTypeDefinitionSubtypes, Is.True);
                Assert.That(advanced.LastIncludeReferenceSubtypes, Is.True);
            });
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RelatedToWithSuppliedSubtypeOperandsForwardsThem(
            bool includeTypeSubtypes,
            bool includeReferenceSubtypes)
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = Element(
                FilterOperator.RelatedTo,
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))),
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.From(includeTypeSubtypes)),
                new LiteralOperand(Variant.From(includeReferenceSubtypes)));

            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(advanced.LastIncludeTypeDefinitionSubtypes, Is.EqualTo(includeTypeSubtypes));
                Assert.That(advanced.LastIncludeReferenceSubtypes, Is.EqualTo(includeReferenceSubtypes));
            });
        }

        [Test]
        public void RelatedToWithNonNodeIdSourceYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(42)),
                new LiteralOperand(Variant.From(new NodeId(2))));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToWithNonNodeIdReferenceYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = Element(
                FilterOperator.RelatedTo,
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))),
                new LiteralOperand(Variant.From(42)),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToWithNullTargetTypeYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToWhenAdvancedTargetThrowsYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { ThrowOnIsRelatedTo = true };
            ContentFilterElement element = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(1))),
                new LiteralOperand(Variant.From(new NodeId(2))));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void RelatedToChainedYieldsTrue()
        {
            var advanced = new AdvancedCoverageFilterTarget
            {
                IsRelatedToResult = true,
                RelatedNodes = [new NodeId(100)]
            };
            ContentFilterElement root = Element(
                FilterOperator.RelatedTo,
                new LiteralOperand(Variant.From(new NodeId(1))),
                new ElementOperand(1),
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null));
            ContentFilterElement chained = RelatedToElement(
                new LiteralOperand(Variant.From(new NodeId(4))),
                new LiteralOperand(Variant.From(new NodeId(2))));
            Assert.That(Filter(root, chained).Evaluate(m_context, advanced), Is.True);
        }

        [Test]
        public void DeepRelatedToChainEvaluatesEachLinkOnce()
        {
            var advanced = new AdvancedCoverageFilterTarget
            {
                IsRelatedToResult = true,
                RelatedNodes = [new NodeId(100)]
            };
            var elements = new ContentFilterElement[1024];
            for (int ii = 0; ii < elements.Length; ii++)
            {
                elements[ii] = RelatedToElement(
                    new LiteralOperand(Variant.From(new NodeId(1))),
                    ii + 1 < elements.Length
                        ? new ElementOperand((uint)(ii + 1))
                        : new LiteralOperand(Variant.From(new NodeId(2))));
            }

            Assert.That(Filter(elements).Evaluate(m_context, advanced), Is.True);
            Assert.That(advanced.RelatedNodeReads, Is.EqualTo(1023));
        }

        [Test]
        public void RelatedToChainedWithOutOfRangeIndexYieldsFalse()
        {
            var advanced = new AdvancedCoverageFilterTarget { IsRelatedToResult = true };
            ContentFilterElement root = Element(
                FilterOperator.RelatedTo,
                new LiteralOperand(Variant.From(new NodeId(1))),
                new ElementOperand(9),
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null));
            Assert.That(Filter(root).Evaluate(m_context, advanced), Is.False);
        }

        [Test]
        public void AttributeOperandWithBasicTargetResolvesToFalseValue()
        {
            var attribute = new AttributeOperand(new NodeId(1), new QualifiedName("Value"));
            ContentFilterElement element = Element(
                FilterOperator.Equals,
                attribute,
                new LiteralOperand(Variant.From(false)));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.True);
        }

        [Test]
        public void AttributeOperandWithAdvancedTargetResolvesRelatedValue()
        {
            var advanced = new AdvancedCoverageFilterTarget { RelatedAttributeValue = Variant.From(7) };
            var attribute = new AttributeOperand(new NodeId(1), new QualifiedName("Value"));
            ContentFilterElement element = Element(
                FilterOperator.Equals,
                attribute,
                new LiteralOperand(Variant.From(7)));
            Assert.That(Filter(element).Evaluate(m_context, advanced), Is.True);
        }

        [Test]
        public void EvaluateWithWrongOperandCountThrowsServiceResultException()
        {
            ContentFilterElement element = Element(
                FilterOperator.IsNull,
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(2)));
            Assert.That(
                () => Filter(element).Evaluate(m_context, m_target),
                Throws.InstanceOf<ServiceResultException>());
        }

        [Test]
        public void EvaluateWithUnknownOperatorThrowsServiceResultException()
        {
            var element = new ContentFilterElement { FilterOperator = (FilterOperator)12345 };
            Assert.That(
                () => Filter(element).Evaluate(m_context, m_target),
                Throws.InstanceOf<ServiceResultException>());
        }

        private static Ua.ContentFilter Filter(params ContentFilterElement[] elements)
        {
            return new Ua.ContentFilter { Elements = elements };
        }

        private static Ua.ContentFilter BinaryFilter(FilterOperator op, Variant left, Variant right)
        {
            return Filter(Element(op, new LiteralOperand(left), new LiteralOperand(right)));
        }

        private static ContentFilterElement Element(FilterOperator op, params FilterOperand[] operands)
        {
            var element = new ContentFilterElement { FilterOperator = op };
            element.SetOperands(operands);
            return element;
        }

        private static ContentFilterElement RelatedToElement(
            FilterOperand sourceType,
            FilterOperand targetType)
        {
            return Element(
                FilterOperator.RelatedTo,
                sourceType,
                targetType,
                new LiteralOperand(Variant.From(new NodeId(3))),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null),
                new LiteralOperand(Variant.Null));
        }

        private sealed class CoverageFilterTarget : IFilterTarget
        {
            public bool IsTypeOfResult { get; set; }
            public bool ThrowOnIsTypeOf { get; set; }
            public Variant AttributeValue { get; set; } = Variant.Null;

            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                if (ThrowOnIsTypeOf)
                {
                    throw new InvalidOperationException("IsTypeOf failed.");
                }
                return IsTypeOfResult;
            }

            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                return AttributeValue;
            }
        }

        private sealed class AdvancedCoverageFilterTarget : IAdvancedFilterTarget
        {
            public bool IsTypeOfResult { get; set; }
            public bool IsInViewResult { get; set; }
            public bool IsRelatedToResult { get; set; }
            public bool ThrowOnIsInView { get; set; }
            public bool ThrowOnIsRelatedTo { get; set; }
            public Variant RelatedAttributeValue { get; set; } = Variant.Null;
            public IList<NodeId> RelatedNodes { get; set; } = [];
            public int RelatedNodeReads { get; private set; }
            public bool? LastIncludeTypeDefinitionSubtypes { get; private set; }
            public bool? LastIncludeReferenceSubtypes { get; private set; }

            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                return IsTypeOfResult;
            }

            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                return Variant.Null;
            }

            public bool IsInView(IFilterContext context, NodeId viewId)
            {
                if (ThrowOnIsInView)
                {
                    throw new InvalidOperationException("IsInView failed.");
                }
                return IsInViewResult;
            }

            public bool IsRelatedTo(
                IFilterContext context,
                NodeId intermediateNodeId,
                NodeId sourceTypeId,
                NodeId targetTypeId,
                NodeId referenceTypeId,
                int hops,
                bool includeTypeDefintionSubtypes,
                bool includeReferenceSubtypes)
            {
                if (ThrowOnIsRelatedTo)
                {
                    throw new InvalidOperationException("IsRelatedTo failed.");
                }
                LastIncludeTypeDefinitionSubtypes = includeTypeDefintionSubtypes;
                LastIncludeReferenceSubtypes = includeReferenceSubtypes;
                return IsRelatedToResult;
            }

            public IList<NodeId> GetRelatedNodes(
                IFilterContext context,
                NodeId intermediateNodeId,
                NodeId sourceTypeId,
                NodeId targetTypeId,
                NodeId referenceTypeId,
                int hops,
                bool includeTypeDefintionSubtypes,
                bool includeReferenceSubtypes)
            {
                RelatedNodeReads++;
                return RelatedNodes;
            }

            public Variant GetRelatedAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                RelativePath relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                return RelatedAttributeValue;
            }
        }
    }
}
