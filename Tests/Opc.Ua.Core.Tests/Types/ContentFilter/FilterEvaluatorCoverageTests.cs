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

        [Test]
        public void InListWithStringNonMemberYieldsFalse()
        {
            // For string operands InList compares against the first list entry only and
            // returns that comparison result, so a leading mismatch yields false.
            ContentFilterElement element = Element(
                FilterOperator.InList,
                new LiteralOperand(Variant.From("x")),
                new LiteralOperand(Variant.From("a")),
                new LiteralOperand(Variant.From("x")));
            Assert.That(Filter(element).Evaluate(m_context, m_target), Is.False);
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
