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

using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.ContentFilter
{
    /// <summary>
    /// Measures the evaluation of typical event filter where clauses, the Like operator
    /// on typical patterns, and Variant equality and hashing on scalars and small arrays.
    /// Only public API is used, so the class can be built against older versions for
    /// paired comparisons.
    /// </summary>
    [TestFixture]
    [Category("Benchmark")]
    [MemoryDiagnoser]
    public class EventFilterEvaluationBenchmarks
    {
        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks();
            var namespaces = new NamespaceTable();
            m_context = new FilterContext(namespaces, new TypeTable(namespaces), telemetry);
            m_target = new EventTarget();

            // OfType(BaseEventType)
            m_ofType = Filter(Element(
                FilterOperator.OfType,
                new LiteralOperand(Variant.From(ObjectTypeIds.BaseEventType))));

            // Severity >= 500 And Like(Message, "%Boiler _%")
            m_severityAndLike = Filter(
                Element(FilterOperator.And, new ElementOperand(1), new ElementOperand(2)),
                Element(
                    FilterOperator.GreaterThanOrEqual,
                    Attribute(BrowseNames.Severity),
                    new LiteralOperand(Variant.From((ushort)500))),
                Element(
                    FilterOperator.Like,
                    Attribute(BrowseNames.Message),
                    new LiteralOperand(Variant.From("%Boiler _%"))));

            // Like(Message, "Temperature [a-z]% on Boiler%")
            m_likeList = Filter(Element(
                FilterOperator.Like,
                Attribute(BrowseNames.Message),
                new LiteralOperand(Variant.From("Temperature [a-z]% on Boiler%"))));

            // Like(SourceName, "Area1/%/Pump_")
            m_likePath = Filter(Element(
                FilterOperator.Like,
                Attribute(BrowseNames.SourceName),
                new LiteralOperand(Variant.From("Area1/%/Pump_"))));

            // Equals(EventType, literal) Or InList(Severity, 100, 500, 900)
            m_equalsOrInList = Filter(
                Element(FilterOperator.Or, new ElementOperand(1), new ElementOperand(2)),
                Element(
                    FilterOperator.Equals,
                    Attribute(BrowseNames.EventType),
                    new LiteralOperand(Variant.From(ObjectTypeIds.AuditEventType))),
                Element(
                    FilterOperator.InList,
                    Attribute(BrowseNames.Severity),
                    new LiteralOperand(Variant.From((ushort)100)),
                    new LiteralOperand(Variant.From((ushort)500)),
                    new LiteralOperand(Variant.From((ushort)900))));

            m_int = new Variant(42);
            m_int2 = new Variant(42);
            m_string = new Variant("Temperature high on Boiler 7");
            m_string2 = new Variant("Temperature high on Boiler 7");
            m_intArray = new Variant(s_oneToEight);
            m_intArray2 = new Variant((int[])s_oneToEight.Clone());
            m_variantArray = new Variant(new[] { new Variant(1), new Variant("a"), new Variant(2.5) });
            m_variantArray2 = new Variant(new[] { new Variant(1), new Variant("a"), new Variant(2.5) });
            m_dataValue = new Variant(new DataValue(new Variant(3.5)));
            m_dataValue2 = new Variant(new DataValue(new Variant(3.5)));
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool WhereOfType()
        {
            return m_ofType.Evaluate(m_context, m_target);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool WhereSeverityAndLike()
        {
            return m_severityAndLike.Evaluate(m_context, m_target);
        }

        [Benchmark]
        [Test(ExpectedResult = false)]
        public bool WhereEqualsOrInList()
        {
            return m_equalsOrInList.Evaluate(m_context, m_target);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool LikeCharacterList()
        {
            return m_likeList.Evaluate(m_context, m_target);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool LikePath()
        {
            return m_likePath.Evaluate(m_context, m_target);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool VariantEqualsInt32()
        {
            return m_int.Equals(m_int2);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool VariantEqualsString()
        {
            return m_string.Equals(m_string2);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool VariantEqualsInt32Array()
        {
            return m_intArray.Equals(m_intArray2);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool VariantEqualsVariantArray()
        {
            return m_variantArray.Equals(m_variantArray2);
        }

        [Benchmark]
        [Test(ExpectedResult = true)]
        public bool VariantEqualsDataValue()
        {
            return m_dataValue.Equals(m_dataValue2);
        }

        [Benchmark]
        public int VariantHashVariantArray()
        {
            return m_variantArray.GetHashCode();
        }

        private static Ua.ContentFilter Filter(params ContentFilterElement[] elements)
        {
            var filter = new Ua.ContentFilter();
            foreach (ContentFilterElement element in elements)
            {
                filter.Elements = filter.Elements.AddItem(element);
            }
            return filter;
        }

        private static ContentFilterElement Element(FilterOperator op, params FilterOperand[] operands)
        {
            var element = new ContentFilterElement { FilterOperator = op };
            foreach (FilterOperand operand in operands)
            {
                element.FilterOperands = element.FilterOperands.AddItem(new ExtensionObject(operand));
            }
            return element;
        }

        private static SimpleAttributeOperand Attribute(string browseName)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [QualifiedName.From(browseName)],
                AttributeId = Attributes.Value
            };
        }

        /// <summary>
        /// An event with fixed field values.
        /// </summary>
        private sealed class EventTarget : IFilterTarget
        {
            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                return typeDefinitionId == ObjectTypeIds.BaseEventType;
            }

            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                return relativePath.Count != 1 ? Variant.Null : relativePath[0].Name switch
                {
                    BrowseNames.Severity => m_severity,
                    BrowseNames.Message => m_message,
                    BrowseNames.SourceName => m_sourceName,
                    BrowseNames.EventType => m_eventType,
                    _ => Variant.Null
                };
            }

            private readonly Variant m_severity = Variant.From((ushort)700);
            private readonly Variant m_message =
                Variant.From(new LocalizedText("Temperature high on Boiler 7"));
            private readonly Variant m_sourceName = Variant.From("Area1/Line3/Pump7");
            private readonly Variant m_eventType = Variant.From(ObjectTypeIds.BaseEventType);
        }

        private static readonly int[] s_oneToEight = [1, 2, 3, 4, 5, 6, 7, 8];
        private FilterContext m_context = null!;
        private EventTarget m_target = null!;
        private Ua.ContentFilter m_ofType = null!;
        private Ua.ContentFilter m_severityAndLike = null!;
        private Ua.ContentFilter m_likeList = null!;
        private Ua.ContentFilter m_likePath = null!;
        private Ua.ContentFilter m_equalsOrInList = null!;
        private Variant m_int;
        private Variant m_int2;
        private Variant m_string;
        private Variant m_string2;
        private Variant m_intArray;
        private Variant m_intArray2;
        private Variant m_variantArray;
        private Variant m_variantArray2;
        private Variant m_dataValue;
        private Variant m_dataValue2;
    }
}
