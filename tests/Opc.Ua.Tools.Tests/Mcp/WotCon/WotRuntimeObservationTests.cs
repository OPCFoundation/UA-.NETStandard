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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises actual callback subscription lifetimes with deterministic count, byte and time boundaries.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeObservationTests
    {
        [Test]
        public async Task CountLimitBoundsNotificationsAndDisposesSubscriptionAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Notifications =
            [
                new WotNotification(new DataValue(Variant.From(10))),
                new WotNotification(new DataValue(Variant.From(20))),
                new WotNotification(new DataValue(Variant.From(30)))
            ];
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.ObservePropertyAsync(
                handle, maxNotifications: 2).ConfigureAwait(false));

            Assert.That(result["items"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(result["items"]![0]!["dataValue"]!["Value"]!.GetValue<int>(), Is.EqualTo(10));
            Assert.That(result["items"]![1]!["dataValue"]!["Value"]!.GetValue<int>(), Is.EqualTo(20));
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("itemLimit"));
            Assert.That(result["discardedNotifications"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(result["subscriptionRetained"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ByteLimitRejectsOversizedNotificationWithoutRetainingItsValueAsync()
        {
            await using var fixture = new WotRuntimeTestContext(
                options: new WotRuntimeOptions { MaxResultBytes = 1024 });
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Notifications =
                [new WotNotification(new DataValue(Variant.From(new string('x', 2048))))];
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.ObservePropertyAsync(handle)
                .ConfigureAwait(false), error: true);

            Assert.That(result["items"]!.AsArray(), Is.Empty);
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("byteLimit"));
            Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded.Code));
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ObservationDeadlineDisposesSubscriptionAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> pending = fixture.Tools.ObservePropertyAsync(handle, durationMilliseconds: 100);
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Subscribed.Task).ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
            JsonObject result = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(pending).ConfigureAwait(false));

            Assert.That(result["items"]!.AsArray(), Is.Empty);
            Assert.That(result["stoppedBy"]!.GetValue<string>(), Is.EqualTo("durationLimit"));
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task CallerCancellationDisposesSubscriptionAndAllowsExplicitCloseAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> pending = fixture.Tools.ObservePropertyAsync(handle, ct: cancellation.Token);
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Subscribed.Task).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(pending).ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.CloseAsync(handle).ConfigureAwait(false));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
        }

        [TestCase(0, 1)]
        [TestCase(10_001, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 65)]
        public async Task InvalidObservationBoundsNeverActivateAsync(int duration, int count)
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ObservePropertyAsync(handle, duration, count).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.Zero);
        }

        [Test]
        public async Task CleanupFailureRemainsOwnedAndDoesNotClaimSubscriptionReleasedAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            fixture.Factory.Notifications = [new WotNotification(new DataValue(Variant.From(5)))];
            int attempts = 0;
            fixture.Factory.DisposingSubscription = () => ++attempts == 1
                ? ValueTask.FromException(new IOException("synthetic cleanup failure")) : ValueTask.CompletedTask;
            string handle = await fixture.OpenAsync(operations: ["observeproperty"]).ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.ObservePropertyAsync(
                handle, maxNotifications: 1).ConfigureAwait(false));

            Assert.That(result["cleanupFailed"]!.GetValue<bool>(), Is.True);
            Assert.That(result["subscriptionRetained"]!.GetValue<bool>(), Is.True);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ObservePropertyAsync(
                handle, maxNotifications: 1).ConfigureAwait(false), error: true);
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(1));
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.CloseAsync(handle).ConfigureAwait(false));
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(2));
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public async Task EventProjectionRetainsTypedNestedDataAndExactFieldNamesAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            var field = new DataValue(Variant.From(false));
            fixture.Factory.Notifications =
            [
                new WotNotification(new DataValue(Variant.From(7)),
                    new Dictionary<string, DataValue> { ["EnabledState/Id"] = field },
                    new WotEventData(new Dictionary<string, WotEventData>
                    {
                        ["EnabledState"] = new(new Dictionary<string, WotEventData> { ["Id"] = new(field) })
                    }))
            ];
            const string document = /*lang=json,strict*/ """
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Event runtime",
                  "events": {
                    "temperature": {
                      "forms": [{"href": "http://device.example/value", "op": "subscribeevent"}]
                    }
                  }
                }
                """;
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("event",
                new WotDocumentSource { Text = document },
                WotRuntimeTestContext.Selection(document) with { Kind = WotAffordanceKind.Event },
                ["subscribeevent"]).ConfigureAwait(false));
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.SubscribeEventsAsync(
                opened["runtime"]!["handle"]!.GetValue<string>(), maxNotifications: 1).ConfigureAwait(false));
            JsonNode item = result["items"]![0]!;

            Assert.That(item["eventFields"]!["EnabledState/Id"]!["Value"]!.GetValue<bool>(), Is.False);
            Assert.That(item["data"]!["EnabledState"]!["Id"]!["Value"]!.GetValue<bool>(), Is.False);
            Assert.That(item["data"]!.AsObject().ContainsKey("EnabledState/Id"), Is.False);
            Assert.That(item["dataValue"]!["Value"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
        }
    }
}
#endif
