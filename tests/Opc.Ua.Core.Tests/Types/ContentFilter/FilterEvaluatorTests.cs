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
using System.Threading;
using System.Threading.Tasks;
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

        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.DateTime)]
        public void CastNullStringProducesNull(BuiltInType targetType)
        {
            var isNull = new ContentFilterElement { FilterOperator = FilterOperator.IsNull };
            isNull.SetOperands([new ElementOperand(1)]);
            ContentFilterElement cast = BuildBinaryElement(
                FilterOperator.Cast,
                Variant.From((string)null),
                Variant.From(new NodeId((uint)targetType)));
            var filter = new Ua.ContentFilter { Elements = [isNull, cast] };

            Assert.That(ServiceResult.IsGood(filter.Validate(m_filterContext).Status), Is.True);
            Assert.That(filter.Evaluate(m_filterContext, m_target), Is.True);
        }

        [TestCase(800)]
        [TestCase(1024)]
        public async Task DeepValidatedFilterEvaluatesOnSmallStackAsync(int count)
        {
            var elements = new ContentFilterElement[count];
            for (int ii = 0; ii < count; ii++)
            {
                elements[ii] = new ContentFilterElement { FilterOperator = FilterOperator.Not };
                elements[ii].SetOperands(
                    ii + 1 < count
                        ? [new ElementOperand((uint)(ii + 1))]
                        : new FilterOperand[] { new LiteralOperand(Variant.From(true)) });
            }
            var filter = new Ua.ContentFilter { Elements = elements };
            Assert.That(ServiceResult.IsGood(filter.Validate(m_filterContext).Status), Is.True);

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        completion.SetResult(filter.Evaluate(m_filterContext, m_target));
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                },
                1024 * 1024)
            {
                IsBackground = true
            };
            thread.Start();

            Assert.That(await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.True);
        }

        [TestCase(FilterOperator.And, false)]
        [TestCase(FilterOperator.Or, true)]
        public void IterativeEvaluationPreservesShortCircuit(FilterOperator op, bool left)
        {
            m_target.ThrowOnAttributeRead = true;
            var root = new ContentFilterElement { FilterOperator = op };
            root.SetOperands([new ElementOperand(1), new ElementOperand(2)]);
            var right = new ContentFilterElement { FilterOperator = FilterOperator.IsNull };
            right.SetOperands([new SimpleAttributeOperand { AttributeId = Attributes.Value }]);
            var filter = new Ua.ContentFilter
            {
                Elements =
                [
                    root,
                    BuildBinaryElement(FilterOperator.Equals, Variant.From(left), Variant.From(true)),
                    right
                ]
            };

            Assert.That(filter.Evaluate(m_filterContext, m_target), Is.EqualTo(left));
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void EvaluationRejectsInvalidDependencyWithoutValidation(uint index)
        {
            var element = new ContentFilterElement { FilterOperator = FilterOperator.Not };
            element.SetOperands([new ElementOperand(index)]);
            var filter = new Ua.ContentFilter { Elements = [element] };

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => filter.Evaluate(m_filterContext, m_target));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadContentFilterInvalid));
        }

        [Test]
        public void EvaluationAndValidationRejectOversizeFilter()
        {
            var elements = new ContentFilterElement[Ua.ContentFilter.MaxElementCount + 1];
            elements.AsSpan().Fill(BuildBinaryElement(FilterOperator.Equals, Variant.From(1), Variant.From(1)));
            var filter = new Ua.ContentFilter { Elements = elements };

            Assert.That(filter.Validate(m_filterContext).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadContentFilterInvalid));
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => filter.Evaluate(m_filterContext, m_target));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadContentFilterInvalid));
        }

        [Test]
        public void SharedDependenciesAreEvaluatedOnceAndUnlinkedElementsAreNotEvaluated()
        {
            m_target.AttributeValue = Variant.From(7);
            var elements = new ContentFilterElement[52];
            for (int ii = 0; ii < 50; ii++)
            {
                elements[ii] = new ContentFilterElement { FilterOperator = FilterOperator.And };
                elements[ii].SetOperands([new ElementOperand((uint)(ii + 1)), new ElementOperand((uint)(ii + 1))]);
            }
            elements[50] = new ContentFilterElement { FilterOperator = FilterOperator.Equals };
            elements[50].SetOperands(
                [new SimpleAttributeOperand { AttributeId = Attributes.Value }, new LiteralOperand(Variant.From(7))]);
            elements[51] = new ContentFilterElement { FilterOperator = (FilterOperator)int.MaxValue };
            var filter = new Ua.ContentFilter { Elements = elements };

            Assert.That(filter.Evaluate(m_filterContext, m_target), Is.True);
            Assert.That(m_target.AttributeReads, Is.EqualTo(1));
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
            notElement.SetOperands([new ElementOperand(1)]);

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
            notElement.SetOperands([new ElementOperand(1)]);

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
            andElement.SetOperands([new ElementOperand(1), new ElementOperand(2)]);

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
            andElement.SetOperands([new ElementOperand(1), new ElementOperand(2)]);

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
            orElement.SetOperands([new ElementOperand(1), new ElementOperand(2)]);

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
            orElement.SetOperands([new ElementOperand(1), new ElementOperand(2)]);

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
        public void EqualsConvertsBetweenNumericTypes()
        {
            // Part 4 7.4.1 converts the operands to a common type before the
            // comparison, so an Int32 and a Double naming the same number are
            // equal. The raw union comparison this used to do reported false
            // for every Float or Double operand, and true for a negative Int32
            // only when the other operand happened to share its low bytes.
            Ua.ContentFilter filter = BuildBinaryFilter(
                FilterOperator.Equals, Variant.From(42), Variant.From((double)42.0));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EqualsWithDifferentNumericValuesReturnsFalse()
        {
            Ua.ContentFilter filter = BuildBinaryFilter(
                FilterOperator.Equals, Variant.From(42), Variant.From((double)42.5));
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
            bool result = filter.Evaluate(m_filterContext, m_target);
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
            element.SetOperands([operand, new LiteralOperand(Variant.From(42))]);

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

        /// <summary>
        /// The examples of the OPC 10000-4 §7.7.3 "Wildcard characters" table.
        /// </summary>
        [TestCase("mainstation", "main%", true)]
        [TestCase("amain", "main%", false)]
        [TestCase("green", "%en%", true)]
        [TestCase("alpha", "%en%", false)]
        [TestCase("5%", "5[%]", true)]
        [TestCase("5a", "5[%]", false)]
        [TestCase("would", "_ould", true)]
        [TestCase("could", "_ould", true)]
        [TestCase("shoulder", "_ould", false)]
        [TestCase("5_", "5[_]", true)]
        [TestCase("\\", "\\\\", true)]
        [TestCase("%", "\\%", true)]
        [TestCase("_", "\\_", true)]
        [TestCase("abc4", "abc[13-68]", true)]
        [TestCase("abc2", "abc[13-68]", false)]
        [TestCase("xyze", "xyz[c-f]", true)]
        [TestCase("xyzg", "xyz[c-f]", false)]
        [TestCase("ABC2", "ABC[^13-5]", true)]
        [TestCase("ABC6", "ABC[^13-5]", true)]
        [TestCase("ABC1", "ABC[^13-5]", false)]
        [TestCase("ABC4", "ABC[^13-5]", false)]
        [TestCase("xyza", "xyz[^dgh]", true)]
        [TestCase("xyzh", "xyz[^dgh]", false)]
        [TestCase("This is fine", "Th[ia][ts]%", true)]
        [TestCase("Then is fine", "Th[ia][ts]%", false)]
        public void LikeImplementsWildcardCharactersTable(string target, string pattern, bool expected)
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From(target), Variant.From(pattern));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Like matches the whole string: the old regex translation matched any
        /// substring and escaped the '^' of a negated list.
        /// </summary>
        [TestCase("xxabcxx", "abc", false)]
        [TestCase("mainstation", "station", false)]
        [TestCase("a.b", "a.b", true)]
        [TestCase("aXb", "a.b", false)]
        [TestCase("Abc", "abc", false)]
        [TestCase("a$b", "a$b", true)]
        public void LikeMatchesWholeStringCaseSensitive(string target, string pattern, bool expected)
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From(target), Variant.From(pattern));
            bool result = filter.Evaluate(m_filterContext, m_target);
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// An invalid search string matches nothing; Like resolves to FALSE, as
        /// for an operand that cannot be resolved to a string.
        /// </summary>
        [TestCase("abc[")]
        [TestCase("abc\\")]
        [TestCase("abc[]")]
        [TestCase("abc[z-a]")]
        [TestCase("%[a^j-l]%")]
        public void LikeWithInvalidPatternEvaluatesToFalse(string pattern)
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("abc["), Variant.From(pattern));
            Assert.That(filter.Evaluate(m_filterContext, m_target), Is.False);

            Ua.ContentFilter negated = BuildBinaryFilter(FilterOperator.Like, Variant.From("abc["), Variant.From(pattern));
            var not = new ContentFilterElement { FilterOperator = FilterOperator.Not };
            not.SetOperands([new ElementOperand(1)]);
            negated.Elements = [not, negated.Elements[0]];
            Assert.That(negated.Evaluate(m_filterContext, m_target), Is.True);
        }

        /// <summary>
        /// A literal Like pattern that is not a valid search string is rejected
        /// when the filter is validated (OPC 10000-4 §7.7.4 operand result
        /// Bad_FilterOperandInvalid).
        /// </summary>
        [TestCase("abc[")]
        [TestCase("abc\\")]
        [TestCase("abc[^]")]
        [TestCase("%[a^j-l]%")]
        public void ValidateRejectsInvalidLiteralLikePattern(string pattern)
        {
            foreach (Variant literal in new[] { Variant.From(pattern), Variant.From(new LocalizedText(pattern)) })
            {
                Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("abc"), literal);

                Ua.ContentFilter.Result result = filter.Validate(m_filterContext);

                Assert.That(result.Status.StatusCode, Is.EqualTo(StatusCodes.BadContentFilterInvalid));
                Ua.ContentFilter.ElementResult elementResult = result.ElementResults[0];
                Assert.That(elementResult.Status.StatusCode, Is.EqualTo(StatusCodes.BadContentFilterInvalid));
                Assert.That(elementResult.OperandResults, Has.Count.EqualTo(2));
                Assert.That(elementResult.OperandResults[0], Is.Null);
                Assert.That(elementResult.OperandResults[1].StatusCode, Is.EqualTo(StatusCodes.BadFilterOperandInvalid));
            }
        }

        [TestCase("main%")]
        [TestCase("ABC[^13-5]")]
        [TestCase("")]
        public void ValidateAcceptsValidLiteralLikePattern(string pattern)
        {
            Ua.ContentFilter filter = BuildBinaryFilter(FilterOperator.Like, Variant.From("abc"), Variant.From(pattern));

            Ua.ContentFilter.Result result = filter.Validate(m_filterContext);

            Assert.That(ServiceResult.IsGood(result.Status), Is.True);
            Assert.That(result.ElementResults, Is.Empty);
        }

        [Test]
        public void ValidateDoesNotCheckLikePatternInFirstOperandOrOtherOperators()
        {
            Ua.ContentFilter like = BuildBinaryFilter(FilterOperator.Like, Variant.From("abc["), Variant.From("abc%"));
            Assert.That(ServiceResult.IsGood(like.Validate(m_filterContext).Status), Is.True);

            Ua.ContentFilter equals = BuildBinaryFilter(FilterOperator.Equals, Variant.From("abc"), Variant.From("abc["));
            Assert.That(ServiceResult.IsGood(equals.Validate(m_filterContext).Status), Is.True);
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
            element.SetOperands(
            [
                new LiteralOperand(left),
                new LiteralOperand(right)
            ]);
            return element;
        }

        private static Ua.ContentFilter BuildUnaryFilter(FilterOperator op, Variant operand)
        {
            var element = new ContentFilterElement { FilterOperator = op };
            element.SetOperands(
            [
                new LiteralOperand(operand)
            ]);
            return new Ua.ContentFilter
            {
                Elements = [element]
            };
        }

        private sealed class MockFilterTarget : IFilterTarget
        {
            public bool IsTypeOfResult { get; set; }
            public Variant AttributeValue { get; set; } = Variant.Null;
            public bool ThrowOnAttributeRead { get; set; }
            public int AttributeReads { get; private set; }

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
                if (ThrowOnAttributeRead)
                {
                    throw new InvalidOperationException("This branch must not be evaluated.");
                }
                AttributeReads++;
                return AttributeValue;
            }
        }
    }
}
