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

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for event monitoring operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class EventsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task SubscribeToServerEventsAsync()
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));

            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotServerEvents",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var eventItem = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                DisplayName = "ServerEvents",
                Filter = eventFilter
            };

            subscription.AddItem(eventItem);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();
            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CreateEventFilterAsync()
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("SourceName"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Severity"));

            await Assert.That(eventFilter.SelectClauses.Count).IsEqualTo(5);

            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotEventFilter",
                PublishingEnabled = true,
                PublishingInterval = 1000
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                DisplayName = "FilteredServerEvents",
                Filter = eventFilter
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task ReportEventDispatchesSinksWithoutCapturingContextAsync(bool ambientContext)
        {
            var context = new SystemContext(fixture.Telemetry);
            var node = new BaseObjectState(null);
            var target = new BaseEventState(null);
            var calls = new List<string>();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool argumentsMatch = true;
            bool asyncContextCleared = true;
            var expectedFailure = new InvalidOperationException("AOT event sink failure");
            InvalidOperationException reportedFailure = null;

            var dispatch = Task.Run(() =>
            {
                SynchronizationContext previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(
                        ambientContext ? new SynchronizationContext() : null);

                    node.ReportEvent(context, target);
                    node.OnReportEvent = (c, n, e) =>
                    {
                        argumentsMatch &= ReferenceEquals(c, context) &&
                            ReferenceEquals(n, node) &&
                            ReferenceEquals(e, target);
                        calls.Add("sync");
                    };
                    node.ReportEvent(context, target);

                    node.OnReportEventAsync = (c, n, e, ct) =>
                    {
                        argumentsMatch &= ReferenceEquals(c, context) &&
                            ReferenceEquals(n, node) &&
                            ReferenceEquals(e, target) &&
                            !ct.CanBeCanceled;
                        asyncContextCleared &= SynchronizationContext.Current == null;
                        calls.Add("completed");
                        return ValueTask.CompletedTask;
                    };
                    node.ReportEvent(context, target);

                    node.OnReportEventAsync = async (c, n, e, ct) =>
                    {
                        argumentsMatch &= ReferenceEquals(c, context) &&
                            ReferenceEquals(n, node) &&
                            ReferenceEquals(e, target) &&
                            !ct.CanBeCanceled;
                        asyncContextCleared &= SynchronizationContext.Current == null;
                        calls.Add("async-start");
                        started.TrySetResult();
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        calls.Add("async-end");
                    };
                    node.ReportEvent(context, target);
                    calls.Add("returned");

                    node.OnReportEvent = null;
                    node.OnReportEventAsync = (_, _, _, _) => ValueTask.FromException(expectedFailure);
                    try
                    {
                        node.ReportEvent(context, target);
                    }
                    catch (InvalidOperationException exception)
                    {
                        reportedFailure = exception;
                    }
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }, timeout.Token);

            try
            {
                await started.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await Assert.That(dispatch.IsCompleted).IsFalse();
            }
            finally
            {
                release.TrySetResult();
                await dispatch.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }

            await Assert.That(argumentsMatch).IsTrue();
            await Assert.That(asyncContextCleared).IsTrue();
            await Assert.That(reportedFailure).IsSameReferenceAs(expectedFailure);
            await Assert.That(string.Join(",", calls))
                .IsEqualTo("sync,sync,completed,sync,async-start,async-end,returned");
        }
    }
}
