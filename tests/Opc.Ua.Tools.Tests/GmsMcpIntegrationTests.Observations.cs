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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.MachineTool;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests
{
    public sealed partial class GmsMcpIntegrationTests
    {
        /// <summary>
        /// Native data observation includes qualification and environmental quality without owning other items.
        /// </summary>
        [Test]
        public async Task DataObservationRetainsQualificationQualityAndCallerSubscription()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            JsonElement equipment = await ReadAsync("Equipment").ConfigureAwait(false);
            string qualified = equipment.GetProperty("sensors")[0].GetProperty("sensors")[0]
                .GetProperty("life")[0].GetProperty("nodeId").GetString()!;
            string temperature = equipment.GetProperty("additionalSensors")[0].GetProperty("nodeId").GetString()!;
            JsonElement observation = Success(await ObserveAsync("Data", 2).ConfigureAwait(false));
            Assert.That(observation.GetProperty("droppedNotifications").GetInt64(), Is.Zero);
            JsonElement[] changes = [.. observation.GetProperty("changes").EnumerateArray()];
            JsonElement life = changes.First(item => item.GetProperty("nodeId").GetString() == qualified);
            DataValue sample = Decode(life.GetProperty("reading"));
            Assert.That(sample.WrappedValue.TryGetValue(out double hours), Is.True);
            Assert.That(hours, Is.EqualTo(-0.25));
            Assert.That(sample.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(sample.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
            JsonElement ambient = changes.First(item => item.GetProperty("nodeId").GetString() == temperature);
            Assert.That(Decode(ambient.GetProperty("reading")).WrappedValue.TryGetValue(out float degrees), Is.True);
            Assert.That(degrees, Is.EqualTo(21.5f));
            Assert.That(life.GetProperty("sequenceNumber").GetUInt32(), Is.GreaterThan(0));
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(unrelated.MonitoredItems.Items.Single().Created, Is.True);
            Assert.That(m_fixture.Session.Connected, Is.True);
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Intermediate observation uses the generated decoder and instance-qualified characteristic placeholders.
        /// </summary>
        [Test]
        public async Task IntermediateObservationPreservesQualifiedCharacteristicsAndTolerances()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            Task<CallToolResult> pending = ObserveAsync("IntermediateResults", 15, 1, Characteristics());
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("intermediate", intermediate: true))
                .ConfigureAwait(false);
            JsonElement result = Success(await pending.ConfigureAwait(false));
            Assert.That(result.GetProperty("subscriptionReady").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("limitReached").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("droppedNotifications").GetInt64(), Is.Zero);
            JsonElement notification = result.GetProperty("events")[0];
            Assert.That(notification.GetProperty("programName").GetString(), Is.EqualTo("InspectBore"));
            Assert.That(notification.GetProperty("acquisitionQualityProvided").GetBoolean(), Is.False);
            Assert.That(notification.GetProperty("eventIdBase64").GetString(), Is.Not.Empty);
            JsonElement characteristic = notification.GetProperty("characteristics")[0];
            Assert.That(characteristic.GetProperty("name").GetString(), Is.EqualTo("Bore"));
            Assert.That(characteristic.GetProperty("namespaceIndex").GetUInt16(),
                Is.EqualTo(m_fixture.System.NodeId.NamespaceIndex));
            Assert.That(EventValue(characteristic, "identifier").TryGetValue(out string identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("bore"));
            Assert.That(EventValue(characteristic, "resultValue").TryGetValue(out double measured), Is.True);
            Assert.That(measured, Is.EqualTo(10.25));
            Assert.That(EventValue(characteristic, "upperTolerance").TryGetValue(out double upper), Is.True);
            Assert.That(upper, Is.EqualTo(10.5));
            Assert.That(EventValue(characteristic, "lowerTolerance").TryGetValue(out double lower), Is.True);
            Assert.That(lower, Is.EqualTo(9.5));
            Assert.That(EventValue(characteristic, "upperToleranceForm").TryGetValue(out int upperForm), Is.True);
            Assert.That(upperForm, Is.EqualTo((int)ToleranceLimitEnum.LimitValue));
            Assert.That(EventValue(characteristic, "lowerToleranceForm").TryGetValue(out int lowerForm), Is.True);
            Assert.That(lowerForm, Is.EqualTo((int)ToleranceLimitEnum.NaturalLimit));
            Assert.That(EventValue(characteristic, "nominal").TryGetValue(out double nominal), Is.True);
            Assert.That(nominal, Is.EqualTo(10));
            Assert.That(EventValue(characteristic, "characteristicsClass").TryGetValue(out byte importance), Is.True);
            Assert.That(importance, Is.EqualTo(4));
            Assert.That(EventValue(characteristic, "isValid").TryGetValue(out bool valid), Is.True);
            Assert.That(valid, Is.True);
            Assert.That(EventValue(characteristic, "formula").TryGetValue(out string formula), Is.True);
            Assert.That(formula, Is.EqualTo("diameter"));
            Assert.That(EventValue(characteristic, "engineeringUnits").TryGetStructure<EUInformation>(
                out EUInformation? units), Is.True);
            Assert.That(units!.UnitId, Is.EqualTo(5066068));
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Empty intermediate windows are successful only after real monitored-item creation.
        /// </summary>
        [TestCase("Events")]
        [TestCase("IntermediateResults")]
        public async Task QuietObservationWindowsReleaseAllOwnedResources(string kind)
        {
            int subscriptions = m_fixture.Subscriptions.Count;
            JsonElement result = Success(await ObserveAsync(kind, 1).ConfigureAwait(false));
            Assert.That(result.GetProperty("events").GetArrayLength(), Is.Zero);
            Assert.That(result.GetProperty("limitReached").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("droppedNotifications").GetInt64(), Is.Zero);
            if (kind == "IntermediateResults")
            {
                Assert.That(result.GetProperty("subscriptionReady").GetBoolean(), Is.True);
                Assert.That(result.GetProperty("completionReason").GetString(), Is.EqualTo("WindowElapsed"));
            }
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Standard result-ready events retain their real identity and qualified fields through passive observation.
        /// </summary>
        [Test]
        public async Task EventObservationRetainsStandardIdentityAndFieldQualification()
        {
            Task<CallToolResult> pending = ObserveAsync("Events", 10, 1);
            await m_fixture.WaitForObservationAsync().ConfigureAwait(false);
            DateTimeUtc before = DateTimeUtc.Now;
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result("event-result")).ConfigureAwait(false);
            DateTimeUtc after = DateTimeUtc.Now;
            JsonElement result = Success(await pending.ConfigureAwait(false));
            Assert.That(result.GetProperty("events").GetArrayLength(), Is.EqualTo(1));
            JsonElement value = result.GetProperty("events")[0];
            Assert.That(value.GetProperty("eventIdBase64").GetString(), Is.Not.Empty);
            Assert.That(value.GetProperty("nativeSensorWarning").GetBoolean(), Is.False);
            Assert.That(value.GetProperty("fields").GetArrayLength(), Is.GreaterThan(10));
            JsonElement time = value.GetProperty("fields").EnumerateArray().Single(field =>
                field.GetProperty("browsePath").GetArrayLength() == 1 &&
                QualifiedName.Parse(field.GetProperty("browsePath")[0].GetString()!).Name == Opc.Ua.BrowseNames.Time);
            Assert.That(EventValue(time, "value").TryGetValue(out DateTimeUtc eventTime), Is.True);
            Assert.That(eventTime, Is.GreaterThanOrEqualTo(before).And.LessThanOrEqualTo(after));
            Assert.That(eventTime, Is.Not.EqualTo(GmsMcpFixture.Acquired),
                "A result-ready event's occurrence time is distinct from its acquired result sample.");
            Assert.That(m_fixture.Subscriptions.Count, Is.Zero);
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Initial retained refresh captures environmental warnings without acknowledging or changing their source.
        /// </summary>
        [Test]
        public async Task RetainedConditionsRefreshWithoutAcknowledging()
        {
            await using var fixture = new GmsMcpFixture();
            await fixture.StartAsync(warnings: true).ConfigureAwait(false);
            await using GmsMcpHarness mcp = await GmsMcpHarness.CreateAsync(fixture.Session).ConfigureAwait(false);
            Assert.That(fixture.System.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? warning),
                Is.True);
            var temperature = (AdditionalSensorState)fixture.System.State.Equipment!.AdditionalSensor!.FindChild(
                fixture.Manager.SystemContext, new QualifiedName("Temperature", fixture.System.NodeId.NamespaceIndex))!;
            ByteString eventId = warning!.EventId!.Value;
            JsonElement result = Success(await mcp.CallAsync("gms_observe", new JsonObject
            {
                ["systemNodeId"] = fixture.System.NodeId.ToString(), ["kind"] = "Conditions", ["durationSeconds"] = 10
            }).ConfigureAwait(false));
            Assert.That(result.GetProperty("refreshCompleted").GetBoolean(), Is.True);
            JsonElement native = result.GetProperty("events").EnumerateArray()
                .Single(item => item.GetProperty("nativeSensorWarning").GetBoolean() &&
                    item.GetProperty("sensorErrorCode").GetString() == "ENV42");
            Assert.That(result.GetProperty("events").EnumerateArray()
                .Count(item => item.GetProperty("nativeSensorWarning").GetBoolean()), Is.EqualTo(2));
            Assert.That(native.GetProperty("sensorErrorCode").GetString(), Is.EqualTo("ENV42"));
            Assert.That(native.GetProperty("eventIdBase64").GetString(),
                Is.EqualTo(Convert.ToBase64String(eventId.Span)));
            Assert.That(warning.AckedState!.Id!.Value, Is.False);
            Assert.That(warning.ActiveState!.Id!.Value, Is.True);
            Assert.That(warning.Retain!.Value, Is.True);
            Assert.That(warning.InputNode!.Value, Is.EqualTo(temperature.NodeId));
            Assert.That(warning.SourceNode!.Value, Is.EqualTo(temperature.NodeId));
            Assert.That(warning.EventId.Value, Is.EqualTo(eventId));
            fixture.Store.Verify(value => value.AcknowledgeAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            CallToolResult limited = await mcp.CallAsync("gms_observe", new JsonObject
            {
                ["systemNodeId"] = fixture.System.NodeId.ToString(), ["kind"] = "Conditions",
                ["maximumNotifications"] = 1, ["durationSeconds"] = 10
            }).ConfigureAwait(false);
            Assert.That(limited.IsError, Is.True);
            Assert.That(GmsMcpHarness.Json(limited).GetProperty("refreshCompleted").GetBoolean(), Is.False);
            Assert.That(warning.AckedState.Id.Value, Is.False);
            Assert.That(fixture.Subscriptions.Count, Is.Zero);
        }

        /// <summary>
        /// Standard MCP cancellation reaches the server token and removes only this call's subscription.
        /// </summary>
        [TestCase("Events")]
        [TestCase("IntermediateResults")]
        public async Task ObservationsCancelAndPreserveCallerOwnedResources(string kind)
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            Task<CallToolResult> pending = ObserveAsync(kind, 30);
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await m_mcp.CancelLastRequestAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            await WaitForSubscriptionCountAsync(subscriptions).ConfigureAwait(false);
            Assert.That(m_mcp.LastRequestToken.IsCancellationRequested, Is.True);
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(unrelated.MonitoredItems.Items.Single().Created, Is.True);
            Assert.That(m_fixture.Session.Connected, Is.True);
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Direct embedding cancellation preserves the caller token independently of MCP local-client behavior.
        /// </summary>
        [TestCase("Data")]
        [TestCase("Events")]
        [TestCase("IntermediateResults")]
        public async Task DirectAdapterCancellationRetainsTheCallerToken(string kind)
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            using var cancellation = new CancellationTokenSource();
            Task<CallToolResult> pending = GmsTools.ObserveAsync(
                m_mcp.Accessor, m_fixture.System.NodeId.ToString(), kind, durationSeconds: 30,
                maximumNotifications: 100, ct: cancellation.Token);
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException error = Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Native event-item creation failures cannot masquerade as quiet successful intermediate windows.
        /// </summary>
        [Test]
        public async Task FailedIntermediateSetupRetainsTheNativeErrorAndCleansUp()
        {
            byte original = m_fixture.System.State.ResultManagement!.EventNotifier;
            m_fixture.System.State.ResultManagement.EventNotifier = EventNotifiers.None;
            try
            {
                StatusCode nativeStatus = await ReadNativeEventCreationStatusAsync().ConfigureAwait(false);
                Assert.That(nativeStatus, Is.EqualTo(StatusCodes.BadNotSupported),
                    "An object without SubscribeToEvents rejects event monitoring on the native service.");
                CallToolResult result = await ObserveAsync("IntermediateResults", 5).ConfigureAwait(false);
                JsonElement error = GmsMcpParameterTests.Error(result, nameof(StatusCodes.BadNotSupported));
                Assert.That(error.TryGetProperty("events", out _), Is.False);
                Assert.That(m_fixture.Subscriptions.Count, Is.Zero);
            }
            finally
            {
                m_fixture.System.State.ResultManagement.EventNotifier = original;
            }
        }

        /// <summary>
        /// A real pending native read consumes the setup window and cannot become an empty successful observation.
        /// </summary>
        [Test]
        public async Task IntermediateEarlyDeadlineDuringNativeSetupIsNotAnEmptySuccess()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BaseVariableState operation = m_fixture.System.State.Monitoring!.MachineTool!.OperationMode!;
            NodeValueEventHandlerAsync? original = operation.OnReadValueAsync;
            operation.OnReadValueAsync = async (_, _, _, _, ct) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return new AttributeReadResult(
                        ServiceResult.Good, Variant.From(0), StatusCodes.Good, GmsMcpFixture.Acquired);
                }
                finally
                {
                    finished.TrySetResult(true);
                }
            };
            try
            {
                Task<CallToolResult> pending = ObserveAsync("IntermediateResults", 1);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                CallToolResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                JsonElement error = GmsMcpParameterTests.Error(result, "BadTimeout");
                Assert.That(error.TryGetProperty("events", out _), Is.False);
                Assert.That(m_fixture.Subscriptions.Count, Is.Zero);
                Assert.That(m_fixture.Session.Connected, Is.True);
                VerifyNoAcknowledgements();
            }
            finally
            {
                release.TrySetResult(true);
                operation.OnReadValueAsync = original;
                if (entered.Task.IsCompleted)
                {
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Complete encoded intermediate payload overflow fails and releases the call's streaming subscription.
        /// </summary>
        [Test]
        public async Task IntermediatePayloadOverflowIsAnErrorAndDoesNotLeak()
        {
            Task<CallToolResult> pending = m_mcp.CallAsync("gms_observe", new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(), ["kind"] = "IntermediateResults",
                ["durationSeconds"] = 10, ["maximumNotifications"] = 1,
                ["characteristics"] = Characteristics(), ["maximumResponseBytes"] = 4096
            });
            await m_fixture.WaitForObservationAsync().ConfigureAwait(false);
            await m_fixture.System.Results.PublishAsync(GmsMcpFixture.Result(new string('x', 3000), intermediate: true))
                .ConfigureAwait(false);
            GmsMcpParameterTests.Error(await pending.ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Assert.That(m_fixture.Subscriptions.Count, Is.Zero);
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Supplies the real characteristic instance namespace rather than the released model's namespace index.
        /// </summary>
        private JsonArray Characteristics() => new(new JsonObject
        {
            ["name"] = "Bore", ["namespaceIndex"] = m_fixture.System.NodeId.NamespaceIndex
        });

        /// <summary>
        /// Sends an actual grouped observation request.
        /// </summary>
        private Task<CallToolResult> ObserveAsync(
            string kind, int seconds, int count = 100, JsonArray? characteristics = null)
        {
            var arguments = new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(), ["kind"] = kind,
                ["durationSeconds"] = seconds, ["maximumNotifications"] = count
            };
            if (characteristics != null)
            {
                arguments["characteristics"] = characteristics;
            }
            return m_mcp.CallAsync("gms_observe", arguments);
        }

        /// <summary>
        /// Decodes a native event field without inventing a DataValue status or timestamp.
        /// </summary>
        private Variant EventValue(JsonElement element, string field)
        {
            using var decoder = new JsonDecoder(
                element.GetProperty(field).GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadVariant("Value");
        }

        /// <summary>
        /// Creates a genuinely unrelated caller-owned monitored subscription for isolation assertions.
        /// </summary>
        private async Task<ISubscription> AddUnrelatedSubscriptionAsync()
        {
            ISubscription subscription = m_fixture.Subscriptions.Add(
                new Mock<ISubscriptionNotificationHandler>().Object,
                new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions()));
            try
            {
                Assert.That(subscription.MonitoredItems.TryAdd("caller-owned",
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                    {
                        StartNodeId = m_fixture.System.State.Monitoring!.MachineTool!.OperationMode!.NodeId,
                        AttributeId = Attributes.Value
                    }), out IMonitoredItem? item), Is.True);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!item!.Created)
                {
                    await Task.Delay(20, timeout.Token).ConfigureAwait(false);
                }
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Reads the independent native creation result, rather than inferring an operation code from server logs.
        /// </summary>
        private async Task<StatusCode> ReadNativeEventCreationStatusAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            CreateSubscriptionResponse subscription = await m_fixture.Session.CreateSubscriptionAsync(
                null, 100, 1000, 10, 0, true, 0, timeout.Token).ConfigureAwait(false);
            try
            {
                var client = new GmsResultClient(
                    m_fixture.Session, m_fixture.System.NodeId, NUnitTelemetryContext.Create());
                CreateMonitoredItemsResponse response = await m_fixture.Session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = m_fixture.System.State.ResultManagement!.NodeId,
                                AttributeId = Attributes.EventNotifier
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1, QueueSize = 100, DiscardOldest = false,
                                Filter = new ExtensionObject(client.CreateIntermediateResultFilter())
                            }
                        }
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results, Has.Count.EqualTo(1));
                return response.Results[0].StatusCode;
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                DeleteSubscriptionsResponse deleted = await m_fixture.Session.DeleteSubscriptionsAsync(
                    null, [subscription.SubscriptionId], cleanup.Token).ConfigureAwait(false);
                Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            }
        }

        /// <summary>
        /// Waits for actual cleanup completion after asynchronous MCP cancellation dispatch.
        /// </summary>
        private async Task WaitForSubscriptionCountAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (m_fixture.Subscriptions.Count != expected)
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
#endif
