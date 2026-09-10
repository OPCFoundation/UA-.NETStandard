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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class HttpWotEventPayloadTests
    {
        [Test]
        public async Task LinkedEventSchemaKeepsItsOwnScopedTypesAndBrowseNamesAfterResolverDisposal()
        {
            const string definition = """
                {
                  "@context":{"model":"urn:linked-model","native":"http://opcfoundation.org/UA/"},
                  "@type":["tm:ThingModel","uav:eventType"],
                  "title":"Linked event","uav:id":"nsu=urn:linked-model;s=Alarm",
                  "data":{
                    "type":"object","uav:fieldOrder":["Pressure","Target"],
                    "properties":{
                      "Pressure":{
                        "type":"integer","uav:dataTypeName":"native:UInt16","uav:browseName":"model:Pressure"
                      },
                      "Target":{"type":"string","uav:dataTypeId":"i=17","uav:browseName":"model:Target"}
                    }
                  }
                }
                """;
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.Setup(value => value.ResolveThingAsync(
                    "types.json", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(definition))));
            using var harness = new EventHarness("""{"Target":"nsu=urn:source;s=Boiler","Pressure":65535}""");
            const string td = """
                {
                  "@context":{"model":"urn:wrong-referrer"},
                  "title":"Linked source",
                  "events":{"alarm":{
                    "tm:ref":"types.json",
                    "forms":[{"href":"https://events.example/alarm","op":"subscribeevent"}]
                  }}
                }
                """;
            WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                "linked", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td), resolver.Object)
                .ConfigureAwait(false);
            WotCompiledForm form = harness.Registry.Prepare(request).CompiledForms.Single();
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(form)
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => arrived.TrySetResult(value))
                .ConfigureAwait(false);

            WotNotification notification = await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(form.EventSelection, Is.Not.Null);
            Assert.That(form.EventSelection!.Clauses[0].BrowsePath, Is.EqualTo("nsu=urn:linked-model;Pressure"));
            Assert.That(form.EventSelection.Clauses[1].BrowsePath, Is.EqualTo("nsu=urn:linked-model;Target"));
            Assert.That(notification.Data.TryGetValue(["Pressure"], out DataValue pressure), Is.True);
            Assert.That(pressure.WrappedValue.TryGetValue(out ushort value), Is.True);
            Assert.That(value, Is.EqualTo(ushort.MaxValue));
            Assert.That(notification.Data.TryGetValue(["Target"], out DataValue target), Is.True);
            Assert.That(target.WrappedValue.TryGetValue(out NodeId node), Is.True);
            Assert.That(notification.Context, Is.Not.Null);
            Assert.That(NodeId.ToExpandedNodeId(node, notification.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Boiler", "urn:source")));
            Assert.That(notification.NamespaceUris, Is.EqualTo(notification.Context.NamespaceUris.ToArrayOf()));
            Assert.That(notification.EventFields["nsu=urn:linked-model;Pressure"], Is.EqualTo(pressure));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [TestCase("{")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("""{"EventId":"AQID","EventType":"i=2041"}""")]
        public async Task InvalidEventPayloadIsObservableAndUsesTheExistingRetryPolicy(string response)
        {
            var retry = new Mock<IChannelReconnectPolicy>(MockBehavior.Strict);
            retry.Setup(value => value.GetDelay(1)).Returns(TimeSpan.FromMilliseconds(-1));
            using var harness = new EventHarness(response, retry.Object);
            WotCompiledForm form = harness.DefaultForm();
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(form)
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => arrived.TrySetResult(value))
                .ConfigureAwait(false);

            WotNotification notification = await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(notification.Data.Members, Is.Empty);
            Assert.That(subscription, Is.InstanceOf<PollingWotSubscription>());
            Assert.That(((PollingWotSubscription)subscription).ConsecutiveFailures, Is.EqualTo(1));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            retry.Verify(value => value.GetDelay(1), Times.Once);
        }

        [Test]
        public async Task EventCallbackFailureIsReportedWithoutAnotherImmediateHttpRequest()
        {
            var retry = new Mock<IChannelReconnectPolicy>(MockBehavior.Strict);
            retry.Setup(value => value.GetDelay(1)).Returns(TimeSpan.FromMilliseconds(-1));
            using var harness = new EventHarness(DefaultPayload, retry.Object);
            var failed = new TaskCompletionSource<StatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(notification =>
            {
                Interlocked.Increment(ref callbacks);
                if (StatusCode.IsGood(notification.Value.StatusCode))
                {
                    throw new InvalidOperationException("Consumer rejected the notification.");
                }
                failed.TrySetResult(notification.Value.StatusCode);
            }).ConfigureAwait(false);

            StatusCode status = await failed.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            retry.Verify(value => value.GetDelay(1), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingSubscriptionOrChannelCancelsInFlightPollingAndStopsCallbacks(bool disposeChannel)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new EventHarness(DefaultPayload, responder: async (_, token) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    throw new InvalidOperationException("The poll must be cancelled.");
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
            });
            int callbacks = 0;
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(
                _ => Interlocked.Increment(ref callbacks)).ConfigureAwait(false);
            await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            if (disposeChannel)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            Assert.That(callbacks, Is.Zero);
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task EventCancellationTokenStopsTheReturnedSubscription()
        {
            var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new EventHarness(DefaultPayload);
            using var cancellation = new CancellationTokenSource();
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(
                _ => arrived.TrySetResult(true), cancellation.Token).ConfigureAwait(false);
            await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(harness.SendCount, Is.EqualTo(1));
            Assert.That(((PollingWotSubscription)subscription).ConsecutiveFailures, Is.Zero);
        }

        [Test]
        public async Task AHealthyHttpEventResetsRetryAttemptsBeforeTheNextFailure()
        {
            var attempts = new List<int>();
            var statuses = new List<StatusCode>();
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retry = new Mock<IChannelReconnectPolicy>(MockBehavior.Strict);
            retry.Setup(value => value.GetDelay(It.IsAny<int>())).Returns<int>(attempt =>
            {
                attempts.Add(attempt);
                if (attempts.Count == 2)
                {
                    stopped.TrySetResult(true);
                    return TimeSpan.FromMilliseconds(-1);
                }
                return TimeSpan.Zero;
            });
            int responses = 0;
            using var harness = new EventHarness(DefaultPayload, retry.Object,
                responder: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        Interlocked.Increment(ref responses) == 2 ? DefaultPayload : "{",
                        Encoding.UTF8, "application/json")
                }), interval: TimeSpan.FromMilliseconds(1));
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(
                notification => statuses.Add(notification.Value.StatusCode)).ConfigureAwait(false);

            await stopped.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(statuses, Is.EqualTo(new StatusCode[]
                { StatusCodes.BadDecodingError, StatusCodes.Good, StatusCodes.BadDecodingError }));
            Assert.That(attempts, Has.Count.EqualTo(2));
            Assert.That(attempts[0], Is.EqualTo(1));
            Assert.That(attempts[1], Is.EqualTo(1));
            Assert.That(((PollingWotSubscription)subscription).ConsecutiveFailures, Is.EqualTo(1));
            Assert.That(harness.SendCount, Is.EqualTo(3));
            retry.Verify(value => value.GetDelay(1), Times.Exactly(2));
            retry.Verify(value => value.GetDelay(2), Times.Never);
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        public async Task EventByteLimitIsExactIncludingTrailingWhitespace(int padding, bool accepted)
        {
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new EventHarness(DefaultPayload + new string(' ', padding),
                new ExponentialBackoffChannelReconnectPolicy { MaxAttempts = 1 },
                bounds: new WotBindingBounds { MaxPayloadBytes = Encoding.UTF8.GetByteCount(DefaultPayload) + 1 });
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => arrived.TrySetResult(value))
                .ConfigureAwait(false);

            WotNotification notification = await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode,
                Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(notification.EventFields, Has.Count.EqualTo(accepted ? 8 : 0));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            if (accepted)
            {
                Assert.That(notification.Data.TryGetValue(["Severity"], out DataValue severity), Is.True);
                Assert.That(severity.WrappedValue.TryGetValue(out ushort value), Is.True);
                Assert.That(value, Is.EqualTo((ushort)700));
            }
        }

        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public async Task EventDepthLimitAlsoBoundsUnselectedMembers(int depth, bool accepted)
        {
            string nested = "0";
            for (int index = 1; index < depth; index++)
            {
                nested = "{\"Child\":" + nested + "}";
            }
            string payload = DefaultPayload[..^1] + ",\"Unused\":" + nested + "}";
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new EventHarness(payload,
                new ExponentialBackoffChannelReconnectPolicy { MaxAttempts = 1 },
                bounds: new WotBindingBounds { MaxPayloadDepth = 3 });
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => arrived.TrySetResult(value))
                .ConfigureAwait(false);

            WotNotification notification = await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode,
                Is.EqualTo(accepted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(notification.EventFields, Has.Count.EqualTo(accepted ? 8 : 0));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task APreCancelledEventSubscriptionNeverSendsOrCallsBack()
        {
            using var harness = new EventHarness(DefaultPayload);
            using var cancellation = new CancellationTokenSource();
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            int callbacks = 0;

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await channel.SubscribeEventAsync(_ => Interlocked.Increment(ref callbacks), cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(harness.SendCount, Is.Zero);
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public async Task LegacyCodecReportsItsEventCapabilityLimitBeforeSending()
        {
            var codec = new Mock<IWotPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("legacy-event");
            codec.Setup(value => value.CanHandle("application/x-event")).Returns(true);
            using var harness = new EventHarness("legacy-event",
                codecs: new WotPayloadCodecRegistry().Register(codec.Object));
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(
                harness.DefaultForm("application/x-event")).ConfigureAwait(false);
            int callbacks = 0;

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel.SubscribeEventAsync(_ => Interlocked.Increment(ref callbacks)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(harness.SendCount, Is.Zero);
            Assert.That(callbacks, Is.Zero);
            codec.Verify(value => value.Decode(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()),
                Times.Never);
        }

        [Test]
        public async Task CustomEventCodecOwnsTheWireMeaningSelectionAndValueContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort ns = context.NamespaceUris.GetIndexOrAppend("urn:custom-event");
            var time = new DateTimeUtc(2026, 8, 1, 12, 0, 0);
            var values = new Dictionary<string, Variant>(StringComparer.Ordinal)
            {
                ["EventId"] = new Variant(new ByteString(new byte[] { 4, 5 })),
                ["EventType"] = new Variant(Ua.ObjectTypeIds.BaseEventType),
                ["SourceNode"] = new Variant(new NodeId("Source", ns)),
                ["SourceName"] = new Variant("custom-source"),
                ["Time"] = new Variant(time),
                ["ReceiveTime"] = new Variant(time),
                ["Message"] = new Variant(new LocalizedText("Custom message")),
                ["Severity"] = new Variant((ushort)800)
            };
            var fields = new Dictionary<string, DataValue>(StringComparer.Ordinal);
            var data = new WotEventDataBuilder();
            foreach (KeyValuePair<string, Variant> field in values)
            {
                var value = new DataValue(field.Value, StatusCodes.Good, time, time);
                fields.Add(field.Key, value);
                Assert.That(data.Add([field.Key], value), Is.True);
            }
            WotNotification expected = new WotNotification(
                new DataValue(new Variant("custom-projection")), fields, data.Build()).WithContext(context);
            ByteString wire = new(Encoding.UTF8.GetBytes("not-json:custom-event"));
            var bounds = new WotBindingBounds();
            var codec = new Mock<IWotInteractionPayloadCodec>(MockBehavior.Strict);
            codec.SetupGet(value => value.Id).Returns("custom-event");
            codec.Setup(value => value.CanHandle("application/x-event")).Returns(true);
            codec.Setup(value => value.DecodeEvent(
                    wire, It.IsAny<WotPayloadDescriptor>(), It.IsAny<WotEventSelection>(), context, bounds))
                .Returns(expected);
            using var harness = new EventHarness("not-json:custom-event", bounds: bounds,
                codecs: new WotPayloadCodecRegistry().Register(codec.Object), context: context);
            WotCompiledForm form = harness.DefaultForm("application/x-event");
            var arrived = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(form)
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => arrived.TrySetResult(value))
                .ConfigureAwait(false);

            WotNotification notification = await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(notification, Is.SameAs(expected));
            Assert.That(notification.Context, Is.SameAs(context));
            Assert.That(notification.Value.WrappedValue, Is.EqualTo(new Variant("custom-projection")));
            Assert.That(notification.Data.TryGetValue(["SourceNode"], out DataValue source), Is.True);
            Assert.That(source.WrappedValue.TryGetValue(out NodeId node), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(node, notification.Context!.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Source", "urn:custom-event")));
            Assert.That(harness.SendCount, Is.EqualTo(1));
            codec.Verify(value => value.DecodeEvent(wire, form.Payload, form.EventSelection!, context, bounds),
                Times.Once);
            codec.Verify(value => value.Decode(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<WotPayloadDescriptor>()),
                Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingPollingLeavesTheCallerOwnedHttpClientUsable(bool disposeChannel)
        {
            using var harness = new EventHarness(DefaultPayload);
            var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callbacks = 0;
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(_ =>
            {
                Interlocked.Increment(ref callbacks);
                arrived.TrySetResult(true);
            }).ConfigureAwait(false);
            await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            if (disposeChannel)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await channel.SubscribeEventAsync(_ => Interlocked.Increment(ref callbacks)).ConfigureAwait(false));
            }
            else
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            using HttpResponseMessage response = await harness.Client.GetAsync(new Uri("https://events.example/probe"))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(harness.SendCount, Is.EqualTo(2));
            Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public async Task ChannelDisposalFromTheFirstCallbackWaitsForThatCallback()
        {
            using var harness = new EventHarness(DefaultPayload);
            var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? disposal = null;
            bool completedInsideCallback = false;
            int callbacks = 0;
            await using IWotBindingChannel channel = await harness.Registry.OpenChannelAsync(harness.DefaultForm())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.SubscribeEventAsync(_ =>
            {
                Interlocked.Increment(ref callbacks);
                disposal = channel.DisposeAsync().AsTask();
                completedInsideCallback = disposal.IsCompleted;
                arrived.TrySetResult(true);
            }).ConfigureAwait(false);

            await arrived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(disposal, Is.Not.Null);
            await disposal!.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(completedInsideCallback, Is.False,
                "A channel must register its subscription before it can invoke the first callback.");
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(harness.SendCount, Is.EqualTo(1));
        }

        private sealed class EventHarness : IDisposable
        {
            public EventHarness(
                string response,
                IChannelReconnectPolicy? retry = null,
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null,
                WotBindingBounds? bounds = null,
                IWotCodecRegistry? codecs = null,
                TimeSpan? interval = null,
                IServiceMessageContext? context = null)
            {
                var handler = new Mock<HttpMessageHandler>();
                handler.Protected().Setup<Task<HttpResponseMessage>>(
                        "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                    .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
                    {
                        Interlocked.Increment(ref m_sendCount);
                        return responder is not null ? responder(request, token) :
                            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent(response, Encoding.UTF8, "application/json")
                            });
                    });
                m_client = new HttpClient(handler.Object);
                Registry = new WotProtocolBinderRegistry(
                    [new HttpBindingPlanner()],
                    [new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => m_client,
                        CallerClientHandlesRedirectSafety = true,
                        ObserveInterval = interval ?? TimeSpan.FromHours(1),
                        RetryPolicy = retry ?? new ExponentialBackoffChannelReconnectPolicy()
                    })], codecs: codecs, bounds: bounds)
                {
                    MessageContext = context
                };
            }

            public WotProtocolBinderRegistry Registry { get; }

            public int SendCount => Volatile.Read(ref m_sendCount);

            public HttpClient Client => m_client;

            public WotCompiledForm DefaultForm(string contentType = "application/json")
            {
                string td = $$"""
                    {
                      "title":"Default events",
                      "events":{
                        "alarm":{
                          "forms":[{
                            "href":"https://events.example/alarm",
                            "op":"subscribeevent","contentType":"{{contentType}}"
                          }]
                        }
                      }
                    }
                    """;
                return Registry.Prepare(WotBindingPlanRequest.FromDocument(
                    "default", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)))
                    .CompiledForms.Single();
            }

            public void Dispose()
            {
                m_client.Dispose();
            }

            private readonly HttpClient m_client;
            private int m_sendCount;
        }

        private const string DefaultPayload = """
            {
              "EventId":"AQID","EventType":"i=2041","SourceNode":"i=2253","SourceName":"Boiler",
              "Time":"2026-08-01T12:00:00Z","ReceiveTime":"2026-08-01T12:00:01Z","Message":"Hot","Severity":700
            }
            """;
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    }
}
