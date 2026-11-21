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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A Like element in the where clause of an EventFilter has to answer in bounded
    /// time, whatever pattern the Client sent.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 7.7.3, Table 118: Like is "TRUE if operand[0] matches a pattern
    /// defined by operand[1]". Table 120: '%' will "Match any string of zero or more
    /// characters". Both operands may be literals, so the Client picks the pattern and
    /// the string it is matched against. The where clause is evaluated for every event
    /// the monitored item is offered, on the thread that reports the event and under
    /// the lock of the item.
    /// </remarks>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemLikeFilterTests
    {
        private static readonly TimeSpan s_timeLimit = TimeSpan.FromSeconds(10);

        /// <summary>
        /// "baaa...a" is matched against "%a%a...%a%b". It cannot match, because nothing
        /// follows the only 'b', so no event is queued. With two "%a" groups the item
        /// answers at once. With ten a search through all ways to split the string
        /// takes far longer than the time limit.
        /// </summary>
        [TestCase(2)]
        [TestCase(10)]
        public void LikeWithRepeatedWildcardsAnswersInBoundedTime(int wildcardGroups)
        {
            var whereClause = new ContentFilter();
            PushLike(whereClause, wildcardGroups);

            MonitoredItem monitoredItem = CreateMonitoredItem(whereClause);

            // any event will do: both operands of the Like element are literals.
            Task report = Task.Run(() => monitoredItem.QueueEvent(new BaseEventState(null)));

            Assert.That(
                report.Wait(s_timeLimit),
                Is.True,
                $"The event was still being evaluated after {s_timeLimit.TotalSeconds} s.");
            Assert.That(
                monitoredItem.ItemsInQueue,
                Is.Zero,
                "the string does not match the pattern");
        }

        /// <summary>
        /// One where clause holds 100 of the slow Like elements, joined by Or. The time
        /// allowed for Like is shared by all elements of one evaluation, so the event is
        /// answered about as fast as with a single slow element.
        /// </summary>
        [Test]
        public void LikeElementsOfOneFilterShareTheTimeLimit()
        {
            var whereClause = new ContentFilter();
            ContentFilterElement root = PushLike(whereClause, 10);
            for (int ii = 1; ii < 100; ii++)
            {
                ContentFilterElement like = PushLike(whereClause, 10);
                root = whereClause.Push(FilterOperator.Or, like, root);
            }

            MonitoredItem monitoredItem = CreateMonitoredItem(whereClause);

            // the first evaluation also pays for the start of the regular expression engine.
            Task warmUp = Task.Run(() => monitoredItem.QueueEvent(new BaseEventState(null)));
            Assert.That(
                warmUp.Wait(s_timeLimit),
                Is.True,
                $"The event was still being evaluated after {s_timeLimit.TotalSeconds} s.");

            var stopwatch = Stopwatch.StartNew();
            Task report = Task.Run(() => monitoredItem.QueueEvent(new BaseEventState(null)));
            Assert.That(
                report.Wait(s_timeLimit),
                Is.True,
                $"The event was still being evaluated after {s_timeLimit.TotalSeconds} s.");
            stopwatch.Stop();

            // 100 elements that each ran into their own limit would need 10 s.
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            Assert.That(
                monitoredItem.ItemsInQueue,
                Is.Zero,
                "no string matches its pattern");
        }

        /// <summary>
        /// A Like element that was cut off is neither true nor false. Negating it must
        /// not report the event, and neither must a Like element that was skipped
        /// because an earlier one used up the time.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LikeThatWasCutOffIsNotNegated(bool negateALaterLike)
        {
            var whereClause = new ContentFilter();
            if (negateALaterLike)
            {
                // Or(slow Like, Not("x" Like "x")): "x" matches "x", so the Not is false.
                ContentFilterElement later = whereClause.Push(
                    FilterOperator.Like,
                    new LiteralOperand { Value = new Variant("x") },
                    new LiteralOperand { Value = new Variant("x") });
                ContentFilterElement not = whereClause.Push(FilterOperator.Not, later);
                ContentFilterElement slow = PushLike(whereClause, 10);
                whereClause.Push(FilterOperator.Or, slow, not);
            }
            else
            {
                // Not(slow Like)
                ContentFilterElement slow = PushLike(whereClause, 10);
                whereClause.Push(FilterOperator.Not, slow);
            }

            MonitoredItem monitoredItem = CreateMonitoredItem(whereClause);

            Task report = Task.Run(() => monitoredItem.QueueEvent(new BaseEventState(null)));

            Assert.That(
                report.Wait(s_timeLimit),
                Is.True,
                $"The event was still being evaluated after {s_timeLimit.TotalSeconds} s.");
            Assert.That(
                monitoredItem.ItemsInQueue,
                Is.Zero,
                "the filter did not evaluate to true");
        }

        /// <summary>
        /// Adds a Like element that matches "baaa...a" against "%a%a...%a%b".
        /// </summary>
        private static ContentFilterElement PushLike(ContentFilter whereClause, int wildcardGroups)
        {
            string target = "b" + new string('a', 40);
            string pattern = string.Concat(Enumerable.Repeat("%a", wildcardGroups)) + "%b";

            return whereClause.Push(
                FilterOperator.Like,
                new LiteralOperand { Value = new Variant(target) },
                new LiteralOperand { Value = new Variant(pattern) });
        }

        private static MonitoredItem CreateMonitoredItem(ContentFilter whereClause)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var namespaceUris = new NamespaceTable();
            var typeTree = new TypeTable(namespaceUris);
            typeTree.AddSubtype(ObjectTypeIds.BaseObjectType, default);
            typeTree.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);

            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperandCollection
                {
                    new SimpleAttributeOperand
                    {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [.. new QualifiedName[] { BrowseNames.EventId }]
                    }
                },
                WhereClause = whereClause
            };

            // CreateMonitoredItems validates the filter the same way and accepts it.
            EventFilter.Result result = filter.Validate(
                new FilterContext(namespaceUris, typeTree, telemetry));
            Assert.That(ServiceResult.IsGood(result.Status), Is.True, result.Status?.ToString());

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            serverMock.Setup(s => s.TypeTree).Returns(typeTree);
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));

            var nodeMangerMock = new Mock<INodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
                null,
                1,
                2,
                new ReadValueId(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                1000.0,
                10,
                false,
                1000);
        }
    }
}
