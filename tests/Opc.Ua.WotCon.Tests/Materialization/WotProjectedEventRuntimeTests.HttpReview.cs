/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedEventRuntimeTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task SharedHttpSourceSurvivesEitherListenerCancellationAndDrainsOnStop(
            bool cancelFirst, bool disposeRuntime)
        {
            TaskCompletionSource<bool> firstResponse = NewSignal();
            TaskCompletionSource<bool> secondResponse = NewSignal();
            TaskCompletionSource<bool> secondEntered = NewSignal();
            TaskCompletionSource<bool> secondCancelled = NewSignal();
            TaskCompletionSource<bool> finalEntered = NewSignal();
            TaskCompletionSource<bool> finalCancelled = NewSignal();
            TaskCompletionSource<bool> finishPollCleanup = NewSignal();
            TaskCompletionSource<bool> finalDrained = NewSignal();
            int sends = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
                {
                    int sequence = Interlocked.Increment(ref sends);
                    if (sequence == 1)
                    {
                        await firstResponse.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    else if (sequence == 2)
                    {
                        secondEntered.TrySetResult(true);
                        try
                        {
                            await secondResponse.Task.WaitAsync(token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            secondCancelled.TrySetResult(true);
                            throw;
                        }
                    }
                    else
                    {
                        finalEntered.TrySetResult(true);
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            finalCancelled.TrySetResult(token.IsCancellationRequested);
                            await finishPollCleanup.Task.ConfigureAwait(false);
                            finalDrained.TrySetResult(true);
                        }
                    }
                    string id = sequence == 1 ? "AQ==" : "Ag==";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($$"""
                            {"EventId":"{{id}}","EventType":"i=2041","SourceNode":"i=2253","SourceName":"Shared",
                             "Time":"2026-08-01T12:00:00Z","ReceiveTime":"2026-08-01T12:00:01Z",
                             "Message":"Shared HTTP event {{sequence}}","Severity":700}
                            """, Encoding.UTF8, "application/json")
                    };
                });
            using var client = new HttpClient(handler.Object);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromMilliseconds(25)
                })]);
            var nodes = new WotProjectionBindingRuntimeTestHarness();
            BaseObjectState firstNotifier = nodes.AddDetachedObject("First");
            BaseObjectState secondNotifier = nodes.AddDetachedObject("Second");
            BaseObjectTypeState type = nodes.AddEventType("SharedHttpEventType", Ua.ObjectTypeIds.BaseEventType);
            const string document = /*lang=json,strict*/ """
                {"title":"Shared HTTP events","events":{
                  "first":{"forms":[{"href":"https://payloads.example/events","op":"subscribeevent"}]},
                  "second":{"forms":[{"href":"https://payloads.example/events","op":"subscribeevent"}]}
                }}
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "shared-http", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)))
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(WotAffordanceKind.Event, "first", "/events/first",
                        type.NodeId.ToString(), firstNotifier.NodeId.ToString()),
                    new WotProjectedAffordance(WotAffordanceKind.Event, "second", "/events/second",
                        type.NodeId.ToString(), secondNotifier.NodeId.ToString())
                ]);
            var publisher = new RecordingPublisher();
            var factory = new WotProjectionBindingRuntimeFactory(
                registry, null, publisher, Mock.Of<IWotProjectionConditionFactory>(MockBehavior.Strict),
                new WotProjectionBindingRuntimeOptions());
            await using IAsyncDisposable runtime = await factory.CreateAsync(nodes.Builder, [plan])
                .ConfigureAwait(false) ??
                throw new InvalidOperationException("The HTTP runtime must be present.");
            using var firstCancellation = new CancellationTokenSource();
            using var secondCancellation = new CancellationTokenSource();
            IAsyncEnumerable<BaseEventState> firstStream = publisher.OpenStream(firstNotifier.NodeId);
            IAsyncEnumerable<BaseEventState> secondStream = publisher.OpenStream(secondNotifier.NodeId);
            await using IAsyncEnumerator<BaseEventState> first =
                firstStream.GetAsyncEnumerator(firstCancellation.Token);
            await using IAsyncEnumerator<BaseEventState> second =
                secondStream.GetAsyncEnumerator(secondCancellation.Token);
            Task<bool>? firstRead = null;
            Task<bool>? secondRead = null;
            Task? stopping = null;
            try
            {
                firstRead = first.MoveNextAsync().AsTask();
                await ((IEventSourceReadiness)firstStream).WaitUntilReadyAsync().AsTask()
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                secondRead = second.MoveNextAsync().AsTask();
                await ((IEventSourceReadiness)secondStream).WaitUntilReadyAsync().AsTask()
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(sends, Is.LessThanOrEqualTo(1));
                firstResponse.TrySetResult(true);
                Assert.That(await firstRead.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(await secondRead.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(first.Current.SourceNode!.Value, Is.EqualTo(firstNotifier.NodeId));
                Assert.That(second.Current.SourceNode!.Value, Is.EqualTo(secondNotifier.NodeId));
                await secondEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                firstRead = first.MoveNextAsync().AsTask();
                secondRead = second.MoveNextAsync().AsTask();
                CancellationTokenSource victim = cancelFirst ? firstCancellation : secondCancellation;
                CancellationTokenSource survivorCancellation = cancelFirst ? secondCancellation : firstCancellation;
                IAsyncEnumerator<BaseEventState> survivor = cancelFirst ? second : first;
                Task<bool> survivorRead = cancelFirst ? secondRead : firstRead;
                Task<bool> victimRead = cancelFirst ? firstRead : secondRead;
                await victim.CancelAsync().ConfigureAwait(false);
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await victimRead.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(secondCancelled.Task.IsCompleted, Is.False,
                    "Cancelling either local listener must not cancel the shared HTTP acquisition.");
                Assert.That(survivorCancellation.IsCancellationRequested, Is.False);
                secondResponse.TrySetResult(true);
                Assert.That(await survivorRead.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(survivor.Current.Message!.Value.Text, Is.EqualTo("Shared HTTP event 2"));
                Assert.That(survivor.Current.Severity!.Value, Is.EqualTo((ushort)700));
                Assert.That(survivor.Current.EventType!.Value, Is.EqualTo(type.NodeId));
                await finalEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                stopping = disposeRuntime ? runtime.DisposeAsync().AsTask() : survivor.DisposeAsync().AsTask();
                Assert.That(await finalCancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(stopping.IsCompleted, Is.False, "Disposal must await the poll's asynchronous cleanup.");
                finishPollCleanup.TrySetResult(true);
                await stopping.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(finalDrained.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(survivorCancellation.IsCancellationRequested, Is.False);
                Assert.That(secondCancelled.Task.IsCompleted, Is.False);
                Assert.That(sends, Is.EqualTo(3));
            }
            finally
            {
                finishPollCleanup.TrySetResult(true);
                await firstCancellation.CancelAsync().ConfigureAwait(false);
                await secondCancellation.CancelAsync().ConfigureAwait(false);
                await DrainCancelledReadAsync(firstRead).ConfigureAwait(false);
                await DrainCancelledReadAsync(secondRead).ConfigureAwait(false);
                if (stopping is not null)
                {
                    await stopping.WaitAsync(s_timeout).ConfigureAwait(false);
                }
            }
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task DrainCancelledReadAsync(Task<bool>? pending)
        {
            if (pending is not null)
            {
                try
                {
                    await pending.WaitAsync(s_timeout).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Finish an outstanding read before disposing its async iterator.
                }
            }
        }
    }
}
#endif
