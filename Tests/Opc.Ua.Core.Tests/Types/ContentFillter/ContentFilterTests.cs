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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Test;

namespace Opc.Ua.Core.Tests.Types.ContentFillter
{

    internal class TestFilterTarget : IFilterTarget
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
    [TestFixture, Category("ContentFillter")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class ContentFilterTests
    {
        #region Shared Properties
        public EventFilter Filter { get;}
        public FilterContext FilterContext { get;}
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
            Filter.WhereClause = new ContentFilter();
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
        [Category("ContentFillter")]
        public void And()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(true);
            loperand2.Value = new Variant(false);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.And;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(false, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Or()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(true);
            loperand2.Value = new Variant(false);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Or;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Not()
        {
            LiteralOperand loperand1 = new LiteralOperand();

            loperand1.Value = new Variant(true);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Not;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(false, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Equals()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(5);
            loperand2.Value = new Variant(5);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Equals;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void GreaterThan()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(7);
            loperand2.Value = new Variant(5);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.GreaterThan;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void GreaterThanOrEqual()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(7);
            loperand2.Value = new Variant(5);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.GreaterThanOrEqual;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void LessThan()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(3);
            loperand2.Value = new Variant(5);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.LessThan;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void LessThanOrEqual()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant(3);
            loperand2.Value = new Variant(5);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.LessThanOrEqual;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Between()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            LiteralOperand loperand3 = new LiteralOperand();

            loperand1.Value = new Variant(5);
            loperand2.Value = new Variant(3);
            loperand3.Value = new Variant(7);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Between;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2, loperand3 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void InList()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();
            LiteralOperand loperand3 = new LiteralOperand();
            LiteralOperand loperand4 = new LiteralOperand();

            loperand1.Value = new Variant(3);
            loperand2.Value = new Variant(5);
            loperand3.Value = new Variant(7);
            loperand4.Value = new Variant(3);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.InList;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2, loperand3, loperand4 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Like()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant("mainstation");
            loperand2.Value = new Variant("main%");

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Like;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void IsNull()
        {
            LiteralOperand loperand1 = new LiteralOperand();

            loperand1.Value = new Variant(1);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.IsNull;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(false, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void Cast()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant((ushort)5);
            NodeId uintNoid = new NodeId((uint)BuiltInType.String, 0);
            loperand2.Value = new Variant(uintNoid);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.Cast;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void BitwiseAnd()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant((byte)2);
            loperand2.Value = new Variant((byte)3);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.BitwiseAnd;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }

        [Test]
        [Category("ContentFillter")]
        public void BitwiseOr()
        {
            LiteralOperand loperand1 = new LiteralOperand();
            LiteralOperand loperand2 = new LiteralOperand();

            loperand1.Value = new Variant((byte)2);
            loperand2.Value = new Variant((byte)3);

            ContentFilterElement filterElement = new ContentFilterElement();
            filterElement.FilterOperator = FilterOperator.BitwiseOr;
            filterElement.SetOperands(new List<LiteralOperand>() { loperand1, loperand2 });
            Filter.WhereClause.Elements = new[] { filterElement };

            TestFilterTarget filterTarget = new TestFilterTarget();
            // apply filter.
            object result = Filter.WhereClause.Evaluate(FilterContext, filterTarget);
            Assert.AreEqual(true, result);
        }
        #endregion
    }
}
