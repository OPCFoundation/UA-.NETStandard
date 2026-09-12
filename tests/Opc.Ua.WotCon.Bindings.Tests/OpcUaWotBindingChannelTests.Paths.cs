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
 *
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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotBindingChannelTests
    {
        [TestCase("/Objects/Server/ServerStatus/CurrentTime", "i=85")]
        [TestCase("ServerStatus/CurrentTime", "i=2253")]
        [TestCase("<HasComponent>ServerStatus<HasComponent>CurrentTime", "i=2253")]
        public async Task LivePathReadResolvesAbsoluteRelativeAndNamedReferencePaths(string path, string anchor)
        {
            WotBindingPlan plan = PathPlan(m_registry, "properties", "readproperty", path, anchor);
            await using IWotBindingChannel channel = await m_registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Value.WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
            Assert.That(time, Is.GreaterThan(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task LivePathObserveCreatesAndDisposesANativeMonitoredItem()
        {
            WotBindingPlan plan = PathPlan(m_registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/CurrentTime", "i=85");
            await using IWotBindingChannel channel = await m_registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            var received = new TaskCompletionSource<WotNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int before = m_session.Subscriptions.Count();
            IWotSubscription subscription = await channel.ObserveAsync(value => received.TrySetResult(value))
                .ConfigureAwait(false);
            await using (subscription.ConfigureAwait(false))
            {
                WotNotification notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
                Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notification.Value.WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.GreaterThan(DateTimeUtc.MinValue));
                Assert.That(m_session.Subscriptions.Count(), Is.EqualTo(before + 1));
            }
            Assert.That(m_session.Subscriptions.Count(), Is.EqualTo(before));
        }

        [Test]
        public async Task LivePathEventSubscriptionDeliversTheNativeSelectedFields()
        {
            WotBindingPlan plan = PathPlan(m_registry, "events", "subscribeevent", "/Objects/Server", "i=85");
            await using IWotBindingChannel channel = await m_registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            var received = new TaskCompletionSource<WotNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotSubscription subscription = await channel
                .SubscribeEventAsync(value => received.TrySetResult(value))
                .ConfigureAwait(false);
            WriteResponse response = await m_session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = ResolvePortableNodeId(TriggerNode01Id),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(901))
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));

            WotNotification notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);

            Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(notification.EventFields.Keys,
                Does.Contain("EventId").And.Contain("SourceNode").And.Contain("Time"));
            Assert.That(notification.EventFields["EventId"].WrappedValue.TryGetValue(out ByteString eventId), Is.True);
            Assert.That(eventId.IsEmpty, Is.False);
        }

        [Test]
        public async Task LivePathCallUsesTheResolvedMethodAndItsExplicitSourceReceiver()
        {
            WotBindingPlan plan = PathPlan(m_registry, "actions", "invokeaction",
                "t:Methods_Add", MethodsObjectNodeId, PathAddInput(),
                JsonNode.Parse("""{"type":"number","uav:dataTypeId":"i=10"}"""));
            await using IWotBindingChannel channel = await m_registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(2.5f), new Variant(3u)])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(result.Outputs, Has.Count.EqualTo(1));
            Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out float sum), Is.True);
            Assert.That(sum, Is.EqualTo(5.5f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LivePathCallRejectsAnUnannotatedUnsignedInputOrUndeclaredOutput(bool missingOutput)
        {
            WotBindingPlan plan = PathPlan(m_registry, "actions", "invokeaction",
                "t:Methods_Add", MethodsObjectNodeId, PathAddInput(annotateUnsigned: missingOutput),
                missingOutput ? null : JsonNode.Parse("""{"type":"number","uav:dataTypeId":"i=10"}"""));
            await using IWotBindingChannel channel = await m_registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(2.5f), new Variant(3u)])
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(missingOutput
                ? StatusCodes.BadDecodingError : StatusCodes.BadTypeMismatch));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LivePathMaintenanceRebindsANativeItemAfterModelOrSessionChange(bool sessionChange)
        {
            using var clock = new PathClock();
            int phase = 0;
            int translations = 0;
            Mock<ISession> proxy = PathSessionProxy((requests, token) =>
            {
                Interlocked.Increment(ref translations);
                if (Volatile.Read(ref phase) == 0)
                {
                    return m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token);
                }
                return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(PathTranslation(new NodeId(2257)));
            });
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/CurrentTime", "i=85");
            DataValue start = await m_session.ReadValueAsync(new NodeId(2257)).ConfigureAwait(false);
            Assert.That(start.WrappedValue.TryGetValue(out DateTimeUtc expected), Is.True);
            var initial = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rebound = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            IWotSubscription subscription = await channel.ObserveAsync(value =>
            {
                initial.TrySetResult(value);
                if (Volatile.Read(ref phase) == 1 &&
                    value.Value.WrappedValue.TryGetValue(out DateTimeUtc timestamp) &&
                    timestamp == expected)
                {
                    rebound.TrySetResult(value);
                }
            }).ConfigureAwait(false);
            await using (subscription.ConfigureAwait(false))
            {
                WotNotification first = await initial.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                Assert.That(first.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Volatile.Write(ref phase, 1);
                if (sessionChange)
                {
                    proxy.Raise(value => value.SessionConfigurationChanged += null, EventArgs.Empty);
                }
                else
                {
                    clock.Tick();
                }

                WotNotification changed = await rebound.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                Assert.That(changed.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(changed.Value.WrappedValue.TryGetValue(out DateTimeUtc actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(translations, Is.GreaterThanOrEqualTo(2));
            }
            int stoppedCount = Volatile.Read(ref translations);
            clock.Tick();
            Assert.That(clock.Disposed, Is.True);
            Assert.That(Volatile.Read(ref translations), Is.EqualTo(stoppedCount));
        }

        [TestCase("CurrentTime")]
        [TestCase("StartTime")]
        public async Task LivePathMaintenanceReportsResolutionFailureAndRecoversTheNativeRoute(string targetName)
        {
            using var clock = new PathClock();
            int phase = 0;
            Mock<ISession> proxy = PathSessionProxy((requests, token) => Volatile.Read(ref phase) == 1
                ? new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
                })
                : m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token));
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/" + targetName, "i=85");
            var initial = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource<WotNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovered = new TaskCompletionSource<WotNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            await using IWotSubscription subscription = await channel.ObserveAsync(value =>
            {
                if (value.Value.StatusCode == StatusCodes.BadNoMatch)
                {
                    failed.TrySetResult(value);
                }
                else if (StatusCode.IsGood(value.Value.StatusCode))
                {
                    initial.TrySetResult(value);
                    if (Volatile.Read(ref phase) == 2)
                    {
                        recovered.TrySetResult(value);
                    }
                }
            }).ConfigureAwait(false);
            await initial.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Volatile.Write(ref phase, 1);
            clock.Tick();

            WotNotification error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Assert.That(error.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Volatile.Write(ref phase, 2);
            clock.Tick();
            WotNotification value = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

            Assert.That(value.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.Value.WrappedValue.TryGetValue(out DateTimeUtc timestamp), Is.True);
            Assert.That(timestamp, Is.GreaterThan(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task DisposingALivePathSubscriptionCancelsItsInFlightMaintenance()
        {
            using var clock = new PathClock();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int phase = 0;
            CancellationToken refreshToken = default;
            Mock<ISession> proxy = PathSessionProxy((requests, token) =>
            {
                if (Volatile.Read(ref phase) == 0)
                {
                    return m_session.TranslateBrowsePathsToNodeIdsAsync(null, requests, token);
                }
                refreshToken = token;
                started.TrySetResult(true);
                return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(blocked.Task.WaitAsync(token));
            });
            WotProtocolBinderRegistry registry = PathRegistry(proxy.Object, clock);
            WotBindingPlan plan = PathPlan(registry, "properties", "observeproperty",
                "/Objects/Server/ServerStatus/CurrentTime", "i=85");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms.Single())
                .ConfigureAwait(false);
            int before = m_session.Subscriptions.Count();
            IWotSubscription subscription = await channel.ObserveAsync(_ => { }).ConfigureAwait(false);
            try
            {
                Volatile.Write(ref phase, 1);
                clock.Tick();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                await subscription.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                Assert.That(refreshToken.IsCancellationRequested, Is.True);
                Assert.That(clock.Disposed, Is.True);
                Assert.That(m_session.Subscriptions.Count(), Is.EqualTo(before));
            }
            finally
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        private WotBindingPlan PathPlan(
            WotProtocolBinderRegistry registry, string collection, string operation,
            string path, string anchor, JsonNode? input = null, JsonNode? output = null)
        {
            var form = new JsonObject
            {
                ["href"] = new UriBuilder("opc.tcp", "localhost", m_serverFixture.Port).Uri.AbsoluteUri,
                ["op"] = operation,
                ["uav:browsePath"] = path
            };
            if (collection == "actions")
            {
                form["uav:callObjectId"] = anchor;
            }
            var affordance = new JsonObject { ["forms"] = new JsonArray(form) };
            if (input is not null)
            {
                affordance["input"] = input;
            }
            if (output is not null)
            {
                affordance["output"] = output;
            }
            var root = new JsonObject
            {
                ["@context"] = new JsonObject { ["t"] = ReferenceServerNamespace },
                ["uav:browsePathAnchor"] = anchor,
                [collection] = new JsonObject { ["path"] = affordance }
            };
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "live-path", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(root.ToJsonString())));
            Assert.That(plan.Diagnostics.Where(value => value.IsError), Is.Empty);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1));
            return plan;
        }

        private static JsonNode PathAddInput(bool annotateUnsigned = true)
        {
            JsonNode input = JsonNode.Parse("""
                {
                  "type":"object", "uav:argumentLayout":"named", "uav:fieldOrder":["x","y"],
                  "properties":{"x":{"type":"number","uav:dataTypeId":"i=10"},"y":{"type":"integer"}},
                  "required":["x","y"]
                }
                """)!;
            if (annotateUnsigned)
            {
                input["properties"]!["y"]!["uav:dataTypeId"] = "i=7";
            }
            return input;
        }

        private Mock<ISession> PathSessionProxy(
            Func<ArrayOf<BrowsePath>, CancellationToken, ValueTask<TranslateBrowsePathsToNodeIdsResponse>> translate)
        {
            var proxy = new Mock<ISession>();
            proxy.SetupGet(value => value.NamespaceUris).Returns(m_session.NamespaceUris);
            proxy.SetupGet(value => value.ServerUris).Returns(m_session.ServerUris);
            proxy.SetupGet(value => value.Factory).Returns(m_session.Factory);
            proxy.SetupGet(value => value.TypeTree).Returns(m_session.TypeTree);
            proxy.SetupGet(value => value.SessionId).Returns(m_session.SessionId);
            proxy.SetupGet(value => value.DefaultSubscription).Returns(m_session.DefaultSubscription);
            proxy.Setup(value => value.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) => m_session.AddSubscription(subscription));
            proxy.Setup(value => value.RemoveSubscriptionAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .Returns((Subscription subscription, CancellationToken token) =>
                    m_session.RemoveSubscriptionAsync(subscription, token));
            proxy.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader header, double maxAge, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> requests, CancellationToken token) =>
                    m_session.ReadAsync(header, maxAge, timestamps, requests, token));
            proxy.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> requests, CancellationToken token) =>
                    translate(requests, token));
            return proxy;
        }

        private static WotProtocolBinderRegistry PathRegistry(ISession session, TimeProvider clock)
        {
            return new WotProtocolBinderRegistry(
                [new OpcUaBindingPlanner()],
                [new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) => new ValueTask<ISession>(session),
                    DisposeSession = false,
                    ObserveInterval = TimeSpan.FromMilliseconds(100),
                    TimeProvider = clock
                })],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static TranslateBrowsePathsToNodeIdsResponse PathTranslation(NodeId target)
        {
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
                    }
                ]
            };
        }

        private sealed class PathClock : TimeProvider, IDisposable
        {
            public bool Disposed => m_timer?.Disposed == true;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                m_timer = new PathTimer(callback, state);
                return m_timer;
            }

            public void Tick()
            {
                m_timer?.Tick();
            }

            public void Dispose()
            {
                m_timer?.Dispose();
            }

            private sealed class PathTimer(TimerCallback callback, object? state) : ITimer
            {
                public bool Disposed => Volatile.Read(ref m_disposed) != 0;

                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    return !Disposed;
                }

                public void Tick()
                {
                    if (!Disposed)
                    {
                        callback(state);
                    }
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref m_disposed, 1);
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                private int m_disposed;
            }

            private PathTimer? m_timer;
        }
    }
}
