/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using NUnit.Framework;
using Opc.Ua.Client.Alarms;

namespace Opc.Ua.Client.Tests.Alarms
{
    [TestFixture, Category("Alarms"), Parallelizable]
    public class AlarmEventFilterBuilderTests
    {
        [Test]
        public void BuildIncludesAllStandardSelectClauses()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForAlarms()
                .Build();

            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.SelectClauses.Count, Is.EqualTo(AlarmEventDecoder.StandardFields.Length));
        }

        [Test]
        public void BuildWithoutOfTypeHasEmptyWhereClause()
        {
            EventFilter filter = new AlarmEventFilterBuilder().Build();

            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.WhereClause, Is.Not.Null);
            Assert.That(filter.WhereClause.Elements.Count, Is.Zero);
        }

        [Test]
        public void ForAlarmsSetsAlarmConditionTypeFilter()
        {
            EventFilter filter = new AlarmEventFilterBuilder()
                .ForAlarms()
                .Build();

            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            Assert.That(filter.WhereClause.Elements[0].FilterOperator,
                Is.EqualTo(FilterOperator.OfType));
        }
    }
}