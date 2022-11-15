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
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.ContentFilter
{

    public class TestFilterTarget : IFilterTarget
    {
        public object GetAttributeValue(FilterContext context, NodeId typeDefinitionId, IList<QualifiedName> relativePath, uint attributeId, Ua.NumericRange indexRange)
        {
            throw new NotImplementedException();
        }

        public bool IsTypeOf(FilterContext context, NodeId typeDefinitionId)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("ContentFilter")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class ContentFilterTests
    {
        #region Shared Properties
        public EventFilter Filter { get; }
        public FilterContext FilterContext { get; }
        public TestFilterTarget TestFilterTarget { get; }

        #endregion

        #region Constructor
        public ContentFilterTests()
        {
            // construct the context to use for the event filter.
            NamespaceTable namespaceTable = new NamespaceTable();
            TypeTable typeTable = new TypeTable(namespaceTable);
            FilterContext = new FilterContext(namespaceTable, typeTable);

            // event filter must be specified.
            Filter = new EventFilter();
            Filter.WhereClause = new Ua.ContentFilter();

            TestFilterTarget = new TestFilterTarget();
        }
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods

        [Test]
        [TestCase(5, 3, 7, true)]
        [TestCase(3, 5, 7, false)]
        [Category("ContentFilter")]
        public void Between(object operandFirst1, object operandFirst2, object operandFirst3, object expectedResult)
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            LiteralOperand loperand3 = new LiteralOperand();

            loperand1.Value = new Variant(operandFirst1);
            loperand2.Value = new Variant(operandFirst2);
            loperand3.Value = new Variant(operandFirst3);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Between;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2, loperand3 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        [Test]
        [TestCase(3, 3, 5, 7, true)]
        [TestCase(3, 1, 5, 7, false)]
        [Category("ContentFilter")]
        public void InList(object operandFirst1, object operandFirst2, object operandFirst3, object operandFirst4, object expectedResult)
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            LiteralOperand loperand3 = new LiteralOperand();
            LiteralOperand loperand4 = new LiteralOperand();

            loperand1.Value = new Variant(operandFirst1);
            loperand2.Value = new Variant(operandFirst2);
            loperand3.Value = new Variant(operandFirst3);
            loperand4.Value = new Variant(operandFirst4);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.InList;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2, loperand3, loperand4 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        [Test]
        [Category("ContentFilter")]
        [TestCase(1, FilterOperator.IsNull, false)]
        [TestCase(false, FilterOperator.Not, true)]
        [TestCase(true, FilterOperator.Not, false)]

        public void UnaryFilterOperators(object operandFirst1, FilterOperator filterOp, object expectedResult)
        {
            LiteralOperand loperand1 = new LiteralOperand();

            loperand1.Value = new Variant(operandFirst1);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = filterOp;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        [Test]
        [TestCase("mainstation", "main%", FilterOperator.Like, true)]
        [TestCase("mainstation", "name%", FilterOperator.Like, false)]
        [TestCase(3, 5, FilterOperator.LessThanOrEqual, true)]
        [TestCase(3, 3, FilterOperator.LessThanOrEqual, true)]
        [TestCase(5, 3, FilterOperator.LessThanOrEqual, false)]
        [TestCase(3, 5, FilterOperator.LessThan, true)]
        [TestCase(5, 3, FilterOperator.LessThan, false)]
        [TestCase(7, 5, FilterOperator.GreaterThan, true)]
        [TestCase(5, 7, FilterOperator.GreaterThan, false)]
        [TestCase(7, 5, FilterOperator.GreaterThanOrEqual, true)]
        [TestCase(5, 5, FilterOperator.GreaterThanOrEqual, true)]
        [TestCase(5, 7, FilterOperator.GreaterThanOrEqual, false)]
        [TestCase(5, 5, FilterOperator.Equals, true)]
        [TestCase(5, 7, FilterOperator.Equals, false)]
        [TestCase(true, false, FilterOperator.Or, true)]
        [TestCase(false, false, FilterOperator.Or, false)]
        [TestCase(true, true, FilterOperator.And, true)]
        [TestCase(true, false, FilterOperator.And, false)]

        public void BinaryFilterOperators(object operandFirst1, object operandFirst2, FilterOperator filterOp, object expectedResult)
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(operandFirst1);
            if (filterOp == FilterOperator.Cast)
            {
                NodeId uintNoid = new NodeId(operandFirst2, 0);
                loperand2.Value = new Variant(uintNoid);
            }
            else
            {
                loperand2.Value = new Variant(operandFirst2);
            }


            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = filterOp;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        [Test]
        [TestCase("invalid", (byte)3, FilterOperator.BitwiseAnd, FilterOperator.IsNull, true)]
        [TestCase((byte)2, (byte)3, FilterOperator.BitwiseAnd, FilterOperator.IsNull, false)]
        [TestCase((ushort)5, (uint)BuiltInType.String, FilterOperator.Cast, FilterOperator.IsNull, false)]
        [TestCase((ushort)5, (uint)BuiltInType.Null, FilterOperator.Cast, FilterOperator.IsNull, true)]
        public void NonBoolWithUnary(object operandFirst1, object operandFirst2, FilterOperator filterOp1, FilterOperator filterOp2, object expectedResult)
        {
            // Setup the First ContentfilterElement (the BitwiseOr or BitwiseAnd filter operation)
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            loperand1.Value = new Variant(operandFirst1);
            if (filterOp1 == FilterOperator.Cast)
            {
                NodeId uintNoid = new NodeId(operandFirst2, 0);
                loperand2.Value = new Variant(uintNoid);
            }
            else
            {
                loperand2.Value = new Variant(operandFirst2);
            }


            ContentFilterElement filterElement1 = new ContentFilterElement();
            filterElement1.FilterOperator = filterOp1;
            filterElement1.SetOperands(new List<FilterOperand>() { loperand1, loperand2 });

            // Setup the Second ContentfilterElement
            ElementOperand elementOperand = new ElementOperand();
            elementOperand.Index = 1; // link to filterElement1

            ContentFilterElement filterElement2 = new ContentFilterElement();
            filterElement2.FilterOperator = filterOp2;
            filterElement2.SetOperands(new List<FilterOperand>() { elementOperand });

            Filter.WhereClause.Elements = new[] { filterElement2, filterElement1 };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }

        [Test]
        [TestCase((byte)2, (byte)3, FilterOperator.BitwiseOr, (byte)3, FilterOperator.Equals, true)]
        [TestCase((byte)2, (byte)3, FilterOperator.BitwiseOr, (byte)2, FilterOperator.Equals, false)]
        [TestCase((byte)2, (byte)3, FilterOperator.BitwiseAnd, (byte)2, FilterOperator.Equals, true)]
        [TestCase((byte)2, (byte)3, FilterOperator.BitwiseAnd, (byte)3, FilterOperator.Equals, false)]
        [TestCase("invalid", (byte)3, FilterOperator.BitwiseOr, null, FilterOperator.Equals, true)]
        [TestCase("invalid", (byte)3, FilterOperator.BitwiseAnd, null, FilterOperator.Equals, true)]
        [TestCase((ushort)5, (uint)BuiltInType.String, FilterOperator.Cast, "5", FilterOperator.Equals, true)]
        [TestCase((ushort)5, (uint)BuiltInType.String, FilterOperator.Cast, null, FilterOperator.Equals, false)]
        [TestCase((ushort)5, (uint)BuiltInType.Null, FilterOperator.Cast, null, FilterOperator.Equals, true)]
        [TestCase((ushort)5, (uint)BuiltInType.Null, FilterOperator.Cast, "5", FilterOperator.Equals, false)]
        [Category("ContentFilter")]
        public void NonBoolWithBinary(object operandFirst1, object operandFirst2, FilterOperator filterOp1, object operandSecondFilter, FilterOperator filterOp2, object expectedResult)
        {
            // Setup the First ContentfilterElement (the BitwiseOr or BitwiseAnd filter operation)
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            loperand1.Value = new Variant(operandFirst1);
            if (filterOp1 == FilterOperator.Cast)
            {
                NodeId uintNoid = new NodeId(operandFirst2, 0);
                loperand2.Value = new Variant(uintNoid);
            }
            else
            {
                loperand2.Value = new Variant(operandFirst2);
            }

            ContentFilterElement filterElement1 = new ContentFilterElement();
            filterElement1.FilterOperator = filterOp1;
            filterElement1.SetOperands(new List<FilterOperand>() { loperand1, loperand2 });

            // Setup the Second ContentfilterElement
            LiteralOperand lFirstOperand = new LiteralOperand();
            lFirstOperand.Value = new Variant(operandSecondFilter);
            ElementOperand elementOperand = new ElementOperand();
            elementOperand.Index = 1; // link to filterElement1

            ContentFilterElement filterElement2 = new ContentFilterElement();
            filterElement2.FilterOperator = filterOp2;
            filterElement2.SetOperands(new List<FilterOperand>() { lFirstOperand, elementOperand });

            Filter.WhereClause.Elements = new[] { filterElement2, filterElement1 };

            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, TestFilterTarget);
            Assert.AreEqual(expectedResult, result);
        }


        #endregion
    }
}
