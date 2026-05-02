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
    public class FilterEvaluatorTests
    {
        private IFilterContext m_filterContext;
        private MockFilterTarget m_target;

        [SetUp]
        public void SetUp()
        {
            var namespaceTable = new NamespaceTable();
            var typeTable = new TypeTable(namespaceTable);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_filterContext = new FilterContext(namespaceTable, typeTable, telemetry);
            m_target = new MockFilterTarget();
        }

        [Test]
        public void EmptyFilterReturnsTrue()
        {
            var filter = new Ua.ContentFilter();
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EmptyElementsArrayReturnsTrue()
        {
            var filter = new Ua.ContentFilter
            {
                Elements = []
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithMatchingIntegers()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(42), Variant.From(42));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithNonMatchingIntegers()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(42), Variant.From(99));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithMatchingStrings()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From("hello"), Variant.From("hello"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithNonMatchingStrings()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From("hello"), Variant.From("world"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void GreaterThanTrue()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.GreaterThan, Variant.From(10), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.GreaterThan, Variant.From(3), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void GreaterThanOrEqualTrue()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.GreaterThanOrEqual, Variant.From(5), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanOrEqualFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.GreaterThanOrEqual, Variant.From(3), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void LessThanTrue()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.LessThan, Variant.From(3), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.LessThan, Variant.From(10), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void LessThanOrEqualTrue()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.LessThanOrEqual, Variant.From(5), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanOrEqualFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.LessThanOrEqual, Variant.From(10), Variant.From(5));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void IsNullWithNullVariantReturnsTrue()
        {
            Ua.ContentFilter filter = BuildUnaryFilter(FilterOperator.IsNull, Variant.Null);
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void IsNullWithNonNullVariantReturnsFalse()
        {
            Ua.ContentFilter filter = BuildUnaryFilter(FilterOperator.IsNull, Variant.From(42));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void NotTrueReturnsFalse()
        {
            ContentFilterElement inner = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(1));
            var notElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.Not
            };
            notElement.SetOperands(new FilterOperand[] { new ElementOperand(1) });

            var filter = new Ua.ContentFilter
            {
                Elements = [notElement, inner]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void NotFalseReturnsTrue()
        {
            ContentFilterElement inner = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(2));
            var notElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.Not
            };
            notElement.SetOperands(new FilterOperand[] { new ElementOperand(1) });

            var filter = new Ua.ContentFilter
            {
                Elements = [notElement, inner]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void AndBothTrueReturnsTrue()
        {
            ContentFilterElement left = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(1));
            ContentFilterElement right = BuildBinaryElement(FilterOperator.Equals, Variant.From(2), Variant.From(2));
            var andElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.And
            };
            andElement.SetOperands(new FilterOperand[] { new ElementOperand(1), new ElementOperand(2) });

            var filter = new Ua.ContentFilter
            {
                Elements = [andElement, left, right]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void AndOneFalseReturnsFalse()
        {
            ContentFilterElement left = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(1));
            ContentFilterElement right = BuildBinaryElement(FilterOperator.Equals, Variant.From(2), Variant.From(3));
            var andElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.And
            };
            andElement.SetOperands(new FilterOperand[] { new ElementOperand(1), new ElementOperand(2) });

            var filter = new Ua.ContentFilter
            {
                Elements = [andElement, left, right]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void OrOneTrueReturnsTrue()
        {
            ContentFilterElement left = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(2));
            ContentFilterElement right = BuildBinaryElement(FilterOperator.Equals, Variant.From(2), Variant.From(2));
            var orElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.Or
            };
            orElement.SetOperands(new FilterOperand[] { new ElementOperand(1), new ElementOperand(2) });

            var filter = new Ua.ContentFilter
            {
                Elements = [orElement, left, right]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void OrBothFalseReturnsFalse()
        {
            ContentFilterElement left = BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(2));
            ContentFilterElement right = BuildBinaryElement(FilterOperator.Equals, Variant.From(3), Variant.From(4));
            var orElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.Or
            };
            orElement.SetOperands(new FilterOperand[] { new ElementOperand(1), new ElementOperand(2) });

            var filter = new Ua.ContentFilter
            {
                Elements = [orElement, left, right]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void LikeWithMatchingPattern()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("Hello World"), Variant.From("Hello%"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LikeWithNonMatchingPattern()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("Hello World"), Variant.From("Goodbye%"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void LikeWithUnderscoreWildcard()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("cat"), Variant.From("c_t"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LikeWithExactMatch()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("test"), Variant.From("test"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void CastIntToDouble()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Cast, Variant.From(42), Variant.From(NodeId.Parse("i=11")));
            var evaluator = new FilterEvaluator(filter, m_filterContext, m_target);
            Assert.That(evaluator.Result, Is.True.Or.False);
        }

        [Test]
        public void BitwiseAndOperation()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.BitwiseAnd, Variant.From(0xFF), Variant.From(0x0F));
            var evaluator = new FilterEvaluator(filter, m_filterContext, m_target);
            Assert.That(evaluator, Is.Not.Null);
        }

        [Test]
        public void BitwiseOrOperation()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.BitwiseOr, Variant.From(0xF0), Variant.From(0x0F));
            var evaluator = new FilterEvaluator(filter, m_filterContext, m_target);
            Assert.That(evaluator, Is.Not.Null);
        }

        [Test]
        public void InListWithValuePresent()
        {
            var operands = new List<FilterOperand>
            {
                new LiteralOperand(Variant.From(3)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(2)),
                new LiteralOperand(Variant.From(3)),
                new LiteralOperand(Variant.From(4))
            };

            var element = new ContentFilterElement { FilterOperator = FilterOperator.InList };
            element.SetOperands(operands);

            var filter = new Ua.ContentFilter
            {
                Elements = [element]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void InListWithValueNotPresent()
        {
            var operands = new List<FilterOperand>
            {
                new LiteralOperand(Variant.From(99)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(2)),
                new LiteralOperand(Variant.From(3))
            };

            var element = new ContentFilterElement { FilterOperator = FilterOperator.InList };
            element.SetOperands(operands);

            var filter = new Ua.ContentFilter
            {
                Elements = [element]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithDifferentNumericTypesReturnsFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From((int)42), Variant.From((double)42.0));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void EqualsWithNullOperands()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.Null, Variant.Null);
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void GreaterThanWithDoubles()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.GreaterThan, Variant.From(3.14), Variant.From(2.71));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LessThanWithDoubles()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.LessThan, Variant.From(2.71), Variant.From(3.14));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LikeWithBracketCharacterClass()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("cat"), Variant.From("[abc]at"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void LikeWithNegatedCharacterClass()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("cat"), Variant.From("[!d]at"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithBooleans()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(true), Variant.From(true));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDateTimes()
        {
            DateTime now = DateTime.UtcNow;
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(now), Variant.From(now));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ContentFilterExtensionEvaluate()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(1), Variant.From(1));
            bool result = ContentFilterExtensions.Evaluate(filter, m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void FilterEvaluatorConstructorAndResult()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From(5), Variant.From(5));
            var evaluator = new FilterEvaluator(filter, m_filterContext, m_target);
            Assert.That(evaluator.Result, Is.True);
        }

        [Test]
        public void OfTypeWithTargetReturningFalse()
        {
            m_target.IsTypeOfResult = false;
            Ua.ContentFilter filter = BuildUnaryFilter(FilterOperator.OfType, Variant.From(new NodeId(1)));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void OfTypeWithTargetReturningTrue()
        {
            m_target.IsTypeOfResult = true;
            Ua.ContentFilter filter = BuildUnaryFilter(FilterOperator.OfType, Variant.From(new NodeId(1)));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void SimpleAttributeOperandResolution()
        {
            m_target.AttributeValue = Variant.From(42);
            var operand = new SimpleAttributeOperand(ObjectTypeIds.BaseEventType, new QualifiedName("Severity"));

            var element = new ContentFilterElement { FilterOperator = FilterOperator.Equals };
            element.SetOperands(new FilterOperand[] { operand, new LiteralOperand(Variant.From(42)) });

            var filter = new Ua.ContentFilter
            {
                Elements = [element]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void BetweenInRange()
        {
            var operands = new List<FilterOperand>
            {
                new LiteralOperand(Variant.From(5)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10))
            };
            var element = new ContentFilterElement { FilterOperator = FilterOperator.Between };
            element.SetOperands(operands);

            var filter = new Ua.ContentFilter
            {
                Elements = [element]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void BetweenOutOfRange()
        {
            var operands = new List<FilterOperand>
            {
                new LiteralOperand(Variant.From(15)),
                new LiteralOperand(Variant.From(1)),
                new LiteralOperand(Variant.From(10))
            };
            var element = new ContentFilterElement { FilterOperator = FilterOperator.Between };
            element.SetOperands(operands);

            var filter = new Ua.ContentFilter
            {
                Elements = [element]
            };
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.False);
        }

        [Test]
        public void PushMethodBuildsFilter()
        {
            var filter = new Ua.ContentFilter();
            filter.Push(FilterOperator.Equals, Variant.From(10), Variant.From(10));
            Assert.That(filter.Elements, Has.Count.EqualTo(1));
            Assert.That(filter.Elements[0].FilterOperator, Is.EqualTo(FilterOperator.Equals));
        }

        [Test]
        public void PushMultipleElementsBuildsCompoundFilter()
        {
            var filter = new Ua.ContentFilter();
            filter.Push(FilterOperator.Equals, Variant.From(1), Variant.From(1));
            filter.Push(FilterOperator.Equals, Variant.From(2), Variant.From(2));
            Assert.That(filter.Elements.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void LikeWithPercentWildcard()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("foobar"), Variant.From("%bar"));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithByteValues()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Equals, Variant.From((byte)0xFF), Variant.From((byte)0xFF));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        private static Ua.ContentFilter BuildBinaryFilter(FilterOperator op, Variant left, Variant right)
        {
            ContentFilterElement element = BuildBinaryElement(op, left, right);
            return new Ua.ContentFilter
            {
                Elements = [element]
            };
        }

        private static ContentFilterElement BuildBinaryElement(FilterOperator op, Variant left, Variant right)
        {
            var element = new ContentFilterElement { FilterOperator = op };
            element.SetOperands(new FilterOperand[]
            {
                new LiteralOperand(left),
                new LiteralOperand(right)
            });
            return element;
        }

        private static Ua.ContentFilter BuildUnaryFilter(FilterOperator op, Variant operand)
        {
            var element = new ContentFilterElement { FilterOperator = op };
            element.SetOperands(new FilterOperand[]
            {
                new LiteralOperand(operand)
            });
            return new Ua.ContentFilter
            {
                Elements = [element]
            };
        }

        private sealed class MockFilterTarget : IFilterTarget
        {
            public bool IsTypeOfResult { get; set; }
            public Variant AttributeValue { get; set; } = Variant.Null;

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
                return AttributeValue;
            }
        }
    }
}
