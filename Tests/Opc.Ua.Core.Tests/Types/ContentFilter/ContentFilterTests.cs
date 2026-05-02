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
    public class TestFilterTarget : IFilterTarget
    {
        public Variant GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            ArrayOf<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            throw new NotImplementedException();
        }

        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("ContentFilter")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ContentFilterTests
    {
        public ITelemetryContext Telemetry { get; }
        public EventFilter Filter { get; }
        public IFilterContext FilterContext { get; }
        public TestFilterTarget TestFilterTarget { get; }

        public ContentFilterTests()
        {
            // construct the context to use for the event filter.
            var namespaceTable = new NamespaceTable();
            var typeTable = new TypeTable(namespaceTable);
            Telemetry = NUnitTelemetryContext.Create();
            FilterContext = new FilterContext(namespaceTable, typeTable, Telemetry);

            // event filter must be specified.
            Filter = new EventFilter { WhereClause = new Ua.ContentFilter() };

            TestFilterTarget = new TestFilterTarget();
        }

        private static IEnumerable<TestCaseData> BetweenTestCases()
        {
            yield return new TestCaseData(Variant.From(5), Variant.From(3), Variant.From(7), Variant.From(true));
            yield return new TestCaseData(Variant.From(3), Variant.From(5), Variant.From(7), Variant.From(false));
        }

        [Test]
        [Category("ContentFilter")]
        public void InvalidWhereClauseElementWithoutOperandResultsConvertsToEventFilterResult()
        {
            var filter = new EventFilter { WhereClause = new Ua.ContentFilter() };
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, new QualifiedName(BrowseNames.EventId));
            filter.WhereClause.Elements = new[]
            {
                new ContentFilterElement { FilterOperator = FilterOperator.IsNull }
            };

            EventFilter.Result validationResult = filter.Validate(FilterContext);

            Assert.That(
                validationResult.WhereClauseResult.ElementResults[0].Status.StatusCode,
                Is.EqualTo(StatusCodes.BadEventFilterInvalid));
            Assert.That(
                () => validationResult.ToEventFilterResult(
                    DiagnosticsMasks.None,
                    new StringTable(),
                    Telemetry.CreateLogger<ContentFilterTests>()),
                Throws.Nothing);
        }

        [Test]
        [TestCaseSource(nameof(BetweenTestCases))]
        [Category("ContentFilter")]
        public void Between(
            Variant operandFirst1,
            Variant operandFirst2,
            Variant operandFirst3,
            Variant expectedResult)
        {
            var loperand1 = new LiteralOperand();
            var loperand2 = new LiteralOperand();
            var loperand3 = new LiteralOperand();

            loperand1.Value = operandFirst1;
            loperand2.Value = operandFirst2;
            loperand3.Value = operandFirst3;

            var filterElement = new ContentFilterElement
            {
                FilterOperator = FilterOperator.Between
            };
            filterElement.SetOperands(new List<LiteralOperand> { loperand1, loperand2, loperand3 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> InListTestCases()
        {
            yield return new TestCaseData(
                Variant.From(3), Variant.From(3),
                Variant.From(5), Variant.From(7),
                Variant.From(true));
            yield return new TestCaseData(
                Variant.From(3), Variant.From(1),
                Variant.From(5), Variant.From(7),
                Variant.From(false));
        }

        [Test]
        [TestCaseSource(nameof(InListTestCases))]
        [Category("ContentFilter")]
        public void InList(
            Variant operandFirst1,
            Variant operandFirst2,
            Variant operandFirst3,
            Variant operandFirst4,
            Variant expectedResult)
        {
            var loperand1 = new LiteralOperand();
            var loperand2 = new LiteralOperand();
            var loperand3 = new LiteralOperand();
            var loperand4 = new LiteralOperand();

            loperand1.Value = operandFirst1;
            loperand2.Value = operandFirst2;
            loperand3.Value = operandFirst3;
            loperand4.Value = operandFirst4;

            var filterElement = new ContentFilterElement { FilterOperator = FilterOperator.InList };
            filterElement.SetOperands(
                new List<LiteralOperand> { loperand1, loperand2, loperand3, loperand4 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> UnaryFilterOperatorsTestCases()
        {
            yield return new TestCaseData(Variant.From(1), FilterOperator.IsNull, Variant.From(false));
            yield return new TestCaseData(Variant.From(false), FilterOperator.Not, Variant.From(true));
            yield return new TestCaseData(Variant.From(true), FilterOperator.Not, Variant.From(false));
        }

        [Test]
        [Category("ContentFilter")]
        [TestCaseSource(nameof(UnaryFilterOperatorsTestCases))]
        public void UnaryFilterOperators(
            Variant operandFirst1,
            FilterOperator filterOp,
            Variant expectedResult)
        {
            var loperand1 = new LiteralOperand { Value = operandFirst1 };

            var filterElement = new ContentFilterElement { FilterOperator = filterOp };
            filterElement.SetOperands(new List<LiteralOperand> { loperand1 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> BinaryFilterOperatorsTestCases()
        {
            yield return new TestCaseData(
                Variant.From("mainstation"), Variant.From("main%"),
                FilterOperator.Like, Variant.From(true));
            yield return new TestCaseData(
                Variant.From("mainstation"), Variant.From("name%"),
                FilterOperator.Like, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(3), Variant.From(5),
                FilterOperator.LessThanOrEqual, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(3), Variant.From(3),
                FilterOperator.LessThanOrEqual, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(3),
                FilterOperator.LessThanOrEqual, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(3), Variant.From(5),
                FilterOperator.LessThan, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(3),
                FilterOperator.LessThan, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(7), Variant.From(5),
                FilterOperator.GreaterThan, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(7),
                FilterOperator.GreaterThan, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(7), Variant.From(5),
                FilterOperator.GreaterThanOrEqual, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(5),
                FilterOperator.GreaterThanOrEqual, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(7),
                FilterOperator.GreaterThanOrEqual, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(5),
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(5), Variant.From(7),
                FilterOperator.Equals, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(true), Variant.From(false),
                FilterOperator.Or, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(false), Variant.From(false),
                FilterOperator.Or, Variant.From(false));
            yield return new TestCaseData(
                Variant.From(true), Variant.From(true),
                FilterOperator.And, Variant.From(true));
            yield return new TestCaseData(
                Variant.From(true), Variant.From(false),
                FilterOperator.And, Variant.From(false));
        }

        [Test]
        [TestCaseSource(nameof(BinaryFilterOperatorsTestCases))]
        public void BinaryFilterOperators(
            Variant operandFirst1,
            Variant operandFirst2,
            FilterOperator filterOp,
            Variant expectedResult)
        {
            var loperand1 = new LiteralOperand();
            var loperand2 = new LiteralOperand();

            loperand1.Value = operandFirst1;
            if (filterOp == FilterOperator.Cast)
            {
                loperand2.Value = new NodeId((uint)operandFirst2, 0);
            }
            else
            {
                loperand2.Value = operandFirst2;
            }

            var filterElement = new ContentFilterElement { FilterOperator = filterOp };
            filterElement.SetOperands(new List<LiteralOperand> { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> NonBoolWithUnaryTestCases()
        {
            yield return new TestCaseData(
                Variant.From("invalid"),
                Variant.From((byte)3),
                FilterOperator.BitwiseAnd,
                FilterOperator.IsNull,
                Variant.From(true));
            yield return new TestCaseData(
                Variant.From((byte)2),
                Variant.From((byte)3),
                FilterOperator.BitwiseAnd,
                FilterOperator.IsNull,
                Variant.From(false));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.String),
                FilterOperator.Cast,
                FilterOperator.IsNull,
                Variant.From(false));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.Null),
                FilterOperator.Cast,
                FilterOperator.IsNull,
                Variant.From(true));
        }

        [Test]
        [TestCaseSource(nameof(NonBoolWithUnaryTestCases))]
        public void NonBoolWithUnary(
            Variant operandFirst1,
            Variant operandFirst2,
            FilterOperator filterOp1,
            FilterOperator filterOp2,
            Variant expectedResult)
        {
            // Setup the First ContentfilterElement (the BitwiseOr or BitwiseAnd filter operation)
            var loperand1 = new LiteralOperand();
            var loperand2 = new LiteralOperand();
            loperand1.Value = operandFirst1;
            if (filterOp1 == FilterOperator.Cast)
            {
                loperand2.Value = new NodeId((uint)operandFirst2, 0);
            }
            else
            {
                loperand2.Value = operandFirst2;
            }

            var filterElement1 = new ContentFilterElement { FilterOperator = filterOp1 };
            filterElement1.SetOperands([loperand1, loperand2]);

            // Setup the Second ContentfilterElement
            var elementOperand = new ElementOperand
            {
                Index = 1 // link to filterElement1
            };

            var filterElement2 = new ContentFilterElement { FilterOperator = filterOp2 };
            filterElement2.SetOperands([elementOperand]);

            Filter.WhereClause.Elements = new[] { filterElement2, filterElement1 };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> NonBoolWithBinaryTestCases()
        {
            yield return new TestCaseData(
                Variant.From((byte)2), Variant.From((byte)3),
                FilterOperator.BitwiseOr, Variant.From((byte)3),
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From((byte)2), Variant.From((byte)3),
                FilterOperator.BitwiseOr, Variant.From((byte)2),
                FilterOperator.Equals, Variant.From(false));
            yield return new TestCaseData(
                Variant.From((byte)2), Variant.From((byte)3),
                FilterOperator.BitwiseAnd, Variant.From((byte)2),
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From((byte)2), Variant.From((byte)3),
                FilterOperator.BitwiseAnd, Variant.From((byte)3),
                FilterOperator.Equals, Variant.From(false));
            yield return new TestCaseData(
                Variant.From("invalid"), Variant.From((byte)3),
                FilterOperator.BitwiseOr, Variant.Null,
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From("invalid"), Variant.From((byte)3),
                FilterOperator.BitwiseAnd, Variant.Null,
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.String),
                FilterOperator.Cast, Variant.From("5"),
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.String),
                FilterOperator.Cast, Variant.Null,
                FilterOperator.Equals, Variant.From(false));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.Null),
                FilterOperator.Cast, Variant.Null,
                FilterOperator.Equals, Variant.From(true));
            yield return new TestCaseData(
                Variant.From((ushort)5),
                Variant.From((uint)BuiltInType.Null),
                FilterOperator.Cast, Variant.From("5"),
                FilterOperator.Equals, Variant.From(false));
        }

        [Test]
        [TestCaseSource(nameof(NonBoolWithBinaryTestCases))]
        [Category("ContentFilter")]
        public void NonBoolWithBinary(
            Variant operandFirst1,
            Variant operandFirst2,
            FilterOperator filterOp1,
            Variant operandSecondFilter,
            FilterOperator filterOp2,
            Variant expectedResult)
        {
            // Setup the First ContentfilterElement (the BitwiseOr or BitwiseAnd filter operation)
            var loperand1 = new LiteralOperand();
            var loperand2 = new LiteralOperand();
            loperand1.Value = operandFirst1;
            if (filterOp1 == FilterOperator.Cast)
            {
                loperand2.Value = new NodeId((uint)operandFirst2, 0);
            }
            else
            {
                loperand2.Value = operandFirst2;
            }

            var filterElement1 = new ContentFilterElement { FilterOperator = filterOp1 };
            filterElement1.SetOperands([loperand1, loperand2]);

            // Setup the Second ContentfilterElement
            var lFirstOperand = new LiteralOperand { Value = operandSecondFilter };
            var elementOperand = new ElementOperand
            {
                Index = 1 // link to filterElement1
            };

            var filterElement2 = new ContentFilterElement { FilterOperator = filterOp2 };
            filterElement2.SetOperands([lFirstOperand, elementOperand]);

            Filter.WhereClause.Elements = new[] { filterElement2, filterElement1 };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        private static IEnumerable<TestCaseData> EqualsStringComparsionTestCases()
        {
            yield return new TestCaseData(
                "mainstation", "mainstation",
                StringComparison.Ordinal, Variant.From(true));
            yield return new TestCaseData(
                "mainstation", "Mainstation",
                StringComparison.Ordinal, Variant.From(false));
            yield return new TestCaseData(
                "mainstation", "Mainstation",
                StringComparison.OrdinalIgnoreCase,
                Variant.From(true));
            yield return new TestCaseData(
                "mainstation", "Mainstation",
                StringComparison.InvariantCultureIgnoreCase,
                Variant.From(true));
            yield return new TestCaseData(
                "mainstation", "Mainstation",
                StringComparison.InvariantCulture,
                Variant.From(false));
            yield return new TestCaseData(
                "mainstation", "mainstation",
                StringComparison.InvariantCulture,
                Variant.From(true));
        }

        [Test]
        [TestCaseSource(nameof(EqualsStringComparsionTestCases))]
        public void EqualsStringComparsion(
            string operandFirst1,
            string operandFirst2,
            StringComparison stringComparison,
            Variant expectedResult)
        {
            Ua.ContentFilter.EqualsOperatorDefaultStringComparison = stringComparison;
            var loperand1 = new LiteralOperand { Value = Variant.From(operandFirst1) };
            var loperand2 = new LiteralOperand { Value = Variant.From(operandFirst2) };

            var filterElement = new ContentFilterElement { FilterOperator = FilterOperator.Equals };
            filterElement.SetOperands(new List<LiteralOperand> { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            Variant result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }
    }
}
