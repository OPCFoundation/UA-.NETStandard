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

using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.ContentFilter
{
    /// <summary>
    /// Validation of EventFilter and DataChangeFilter (Part 4 §7.22).
    /// </summary>
    [TestFixture]
    [Category("ContentFilter")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MonitoringFilterValidationTests
    {
        private ITelemetryContext m_telemetry;
        private IFilterContext m_filterContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var namespaceTable = new NamespaceTable();
            m_telemetry = NUnitTelemetryContext.Create();
            m_filterContext = new FilterContext(namespaceTable, new TypeTable(namespaceTable), m_telemetry);
        }

        /// <summary>
        /// M7-3: one invalid select clause next to valid ones leaves the filter valid and is
        /// reported per clause.
        /// </summary>
        [Test]
        public void EventFilterWithOneInvalidSelectClauseIsValidWithClauseResult()
        {
            EventFilter filter = CreateFilter(validClauses: 2, invalidClauses: 1);

            EventFilter.Result result = filter.Validate(m_filterContext);

            Assert.That(ServiceResult.IsGood(result.Status), Is.True);
            Assert.That(result.HasSelectClauseErrors, Is.True);
            EventFilterResult filterResult = result.ToEventFilterResult(
                DiagnosticsMasks.None, new StringTable(), m_telemetry.CreateLogger<MonitoringFilterValidationTests>());
            Assert.That(filterResult.SelectClauseResults.Count, Is.EqualTo(3));
            Assert.That(filterResult.SelectClauseResults[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(filterResult.SelectClauseResults[1], Is.EqualTo(StatusCodes.Good));
            Assert.That(filterResult.SelectClauseResults[2], Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
        }

        /// <summary>
        /// M7-3: a filter without any valid select clause is Bad_EventFilterInvalid.
        /// </summary>
        [Test]
        public void EventFilterWithOnlyInvalidSelectClausesIsInvalid()
        {
            EventFilter filter = CreateFilter(validClauses: 0, invalidClauses: 2);

            EventFilter.Result result = filter.Validate(m_filterContext);

            Assert.That(result.Status.StatusCode, Is.EqualTo(StatusCodes.BadEventFilterInvalid));
            Assert.That(result.SelectClauseResults, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// M7-3: a fully valid filter has no per-clause results.
        /// </summary>
        [Test]
        public void EventFilterWithValidSelectClausesHasNoClauseResults()
        {
            EventFilter filter = CreateFilter(validClauses: 2, invalidClauses: 0);

            EventFilter.Result result = filter.Validate(m_filterContext);

            Assert.That(ServiceResult.IsGood(result.Status), Is.True);
            Assert.That(result.HasSelectClauseErrors, Is.False);
            Assert.That(result.SelectClauseResults, Is.Empty);
        }

        /// <summary>
        /// M7-5: an empty select clause list is Bad_EventFilterInvalid.
        /// </summary>
        [Test]
        public void EventFilterWithoutSelectClauseIsBadEventFilterInvalid()
        {
            var noSelect = new EventFilter { WhereClause = new Ua.ContentFilter() };
            Assert.That(
                noSelect.Validate(m_filterContext).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadEventFilterInvalid));
        }

        /// <summary>
        /// M7-4: selectClauseDiagnosticInfos stays empty when diagnostics were not requested
        /// and has one entry per clause when they were (Part 4 §7.22.3 Table 144).
        /// </summary>
        [Test]
        public void EventFilterResultReturnsSelectClauseDiagnosticsOnlyWhenRequested()
        {
            EventFilter filter = CreateFilter(validClauses: 2, invalidClauses: 1);
            EventFilter.Result result = filter.Validate(m_filterContext);
            ILogger logger = m_telemetry.CreateLogger<MonitoringFilterValidationTests>();

            EventFilterResult withoutDiagnostics = result.ToEventFilterResult(
                DiagnosticsMasks.None, new StringTable(), logger);
            Assert.That(withoutDiagnostics.SelectClauseDiagnosticInfos.Count, Is.Zero);

            EventFilterResult withDiagnostics = result.ToEventFilterResult(
                DiagnosticsMasks.OperationAll, new StringTable(), logger);
            Assert.That(withDiagnostics.SelectClauseDiagnosticInfos.Count, Is.EqualTo(3));
            Assert.That(withDiagnostics.SelectClauseDiagnosticInfos[2], Is.Not.Null);
        }

        /// <summary>
        /// M7-4: an invalid where clause with only valid select clauses returns no select
        /// clause results and no select clause diagnostics.
        /// </summary>
        [Test]
        public void EventFilterResultWithInvalidWhereClauseHasNoSelectClauseDiagnostics()
        {
            EventFilter filter = CreateFilter(validClauses: 3, invalidClauses: 0);
            filter.WhereClause.Elements =
            [
                new ContentFilterElement { FilterOperator = FilterOperator.IsNull }
            ];

            EventFilter.Result result = filter.Validate(m_filterContext);
            Assert.That(result.Status.StatusCode, Is.EqualTo(StatusCodes.BadEventFilterInvalid));

            EventFilterResult filterResult = result.ToEventFilterResult(
                DiagnosticsMasks.None,
                new StringTable(),
                m_telemetry.CreateLogger<MonitoringFilterValidationTests>());
            Assert.That(filterResult.SelectClauseResults.Count, Is.Zero);
            Assert.That(filterResult.SelectClauseDiagnosticInfos.Count, Is.Zero);
        }

        /// <summary>
        /// M7-5: an unknown DataChangeTrigger is Bad_MonitoredItemFilterInvalid, not a deadband
        /// error.
        /// </summary>
        [Test]
        public void DataChangeFilterWithUnknownTriggerIsBadMonitoredItemFilterInvalid()
        {
            var filter = new DataChangeFilter
            {
                Trigger = (DataChangeTrigger)5,
                DeadbandType = (uint)DeadbandType.None
            };

            Assert.That(filter.Validate().StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemFilterInvalid));
        }

        /// <summary>
        /// M7-5: an unknown deadband type is still Bad_DeadbandFilterInvalid.
        /// </summary>
        [Test]
        public void DataChangeFilterWithUnknownDeadbandTypeIsBadDeadbandFilterInvalid()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = 7
            };

            Assert.That(filter.Validate().StatusCode, Is.EqualTo(StatusCodes.BadDeadbandFilterInvalid));
        }

        private static EventFilter CreateFilter(int validClauses, int invalidClauses)
        {
            var filter = new EventFilter { WhereClause = new Ua.ContentFilter() };
            for (int ii = 0; ii < validClauses; ii++)
            {
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
            }

            for (int ii = 0; ii < invalidClauses; ii++)
            {
                filter.SelectClauses = filter.SelectClauses.AddItem(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    BrowsePath = [QualifiedName.From(BrowseNames.Message)],
                    AttributeId = 999
                });
            }

            return filter;
        }
    }
}
