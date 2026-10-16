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

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Proves passive MachineTool workflows through real MCP messages, typed clients and opc.tcp services.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class MachineToolMcpIntegrationTests
    {
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineToolMcpFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_mcp = await MachineToolMcpHarness.CreateAsync(m_fixture.Session).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (m_mcp != null)
            {
                await m_mcp.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PassiveCatalogDescribesRealBoundsAndDeduplicatesConnectionToolsAsync()
        {
            ListToolsResult result = await m_mcp.Client.ListToolsAsync(new ListToolsRequestParams())
                .ConfigureAwait(false);
            Assert.That(result.Tools.Select(tool => tool.Name), Is.Unique);
            foreach (string name in s_passiveNames)
            {
                Tool tool = result.Tools.Single(tool => tool.Name == name);
                Assert.That(tool.Annotations!.ReadOnlyHint, Is.True, name);
                Assert.That(tool.Annotations.DestructiveHint, Is.False, name);
                Assert.That(tool.Annotations.IdempotentHint,
                    Is.EqualTo(!name.StartsWith("machinetool_observe_", StringComparison.Ordinal)), name);
                JsonElement parameters = tool.InputSchema.GetProperty("properties");
                Assert.That(parameters.TryGetProperty("accessor", out _), Is.False);
                Assert.That(parameters.TryGetProperty("ct", out _), Is.False);
                foreach (JsonProperty parameter in parameters.EnumerateObject())
                {
                    Assert.That(parameter.Value.GetProperty("description").GetString(),
                        Has.Length.GreaterThan(20), $"{name}.{parameter.Name}");
                }
                Assert.That(parameters.GetProperty("maximumResponseBytes").GetProperty("minimum").GetInt32(),
                    Is.EqualTo(1024));
                Assert.That(parameters.GetProperty("maximumResponseBytes").GetProperty("maximum").GetInt32(),
                    Is.EqualTo(1048576));
            }
            JsonElement selection = result.Tools.Single(tool => tool.Name == "machinetool_observe_values")
                .InputSchema.GetProperty("properties").GetProperty("nodeIds");
            Assert.That(selection.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(selection.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(selection.GetProperty("maxItems").GetInt32(), Is.EqualTo(256));
            Assert.That(selection.GetProperty("uniqueItems").GetBoolean(), Is.True);
        }

        [Test]
        public async Task InventoryIsActualBoundedAndDoesNotChangeEquipmentAsync()
        {
            CallToolResult result = await m_mcp.CallAsync("machinetool_list_devices").ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True);
            JsonElement json = MachineToolMcpHarness.Json(result);
            Assert.That(json.GetProperty("devices").GetArrayLength(), Is.EqualTo(2));
            Assert.That(json.GetProperty("machineToolModelPresent").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("wmtpModelPresent").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("devices").EnumerateArray().Select(value => value.GetProperty("nodeId").GetString()),
                Does.Contain(m_fixture.Machine.NodeId.ToString()));
            CallToolResult overflow = await m_mcp.CallAsync("machinetool_list_devices",
                new JsonObject { ["maximumResults"] = 1 }).ConfigureAwait(false);
            Assert.That(overflow.IsError, Is.True);
            Assert.That((await m_fixture.Provider.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MonitoringDistinguishesAbsentMembersAndOriginalBadQualityAsync(bool minimal)
        {
            NodeId machine = minimal ? m_fixture.Minimal.NodeId : m_fixture.Machine.NodeId;
            CallToolResult result = await m_mcp.CallAsync("machinetool_read_machine",
                new JsonObject { ["machineNodeId"] = machine.ToString() }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True);
            JsonElement json = MachineToolMcpHarness.Json(result);
            JsonElement monitoring = json.GetProperty("monitoring");
            DataValue mode = Decode(monitoring.GetProperty("operationMode"));
            Assert.That(mode.StatusCode, Is.EqualTo(minimal
                ? StatusCodes.BadWaitingForInitialData : StatusCodes.UncertainLastUsableValue));
            if (!minimal)
            {
                Assert.That(mode.SourceTimestamp, Is.EqualTo(MachineToolMcpFixture.Acquired));
                Assert.That(mode.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo((int)MachineOperationMode.Automatic));
            }
            Assert.That(monitoring.GetProperty("feedOverride").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("jobManagementNodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("capabilities").GetProperty("declarationAvailable").GetBoolean(), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EquipmentKeepsNodeIdentityAndOptionalInventoryPresenceAsync(bool minimal)
        {
            CallToolResult result = await m_mcp.CallAsync("machinetool_read_equipment",
                new JsonObject
                {
                    ["machineNodeId"] = (minimal ? m_fixture.Minimal.NodeId : m_fixture.Machine.NodeId).ToString()
                }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True);
            JsonElement json = MachineToolMcpHarness.Json(result);
            Assert.That(json.GetProperty("tools").GetArrayLength(), Is.EqualTo(minimal ? 0 : 1));
            if (!minimal)
            {
                JsonElement tool = json.GetProperty("tools")[0];
                Assert.That(Decode(tool.GetProperty("readings").GetProperty("identifier")).WrappedValue
                    .TryGetValue(out string identifier), Is.True);
                Assert.That(identifier, Is.EqualTo("tool-a"));
                Assert.That(Decode(tool.GetProperty("readings").GetProperty("placeNumber")).WrappedValue
                    .TryGetValue(out ushort place), Is.True);
                Assert.That(place, Is.EqualTo(4));
                JsonElement life = tool.GetProperty("life")[0].GetProperty("readings").GetProperty("value");
                Assert.That(Decode(life).WrappedValue.TryGetValue(out ulong remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(9007199254740993ul));
                Assert.That(Decode(life).SourceTimestamp, Is.EqualTo(MachineToolMcpFixture.Acquired));
            }
            else
            {
                Assert.That(json.GetProperty("toolsNodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            }
        }

        [Test]
        public async Task ProductionAndPrognosesUseRealNestedDeclarationsAsync()
        {
            JsonObject arguments = new() { ["machineNodeId"] = m_fixture.Machine.NodeId.ToString() };
            CallToolResult production = await m_mcp.CallAsync("machinetool_read_production", arguments)
                .ConfigureAwait(false);
            Assert.That(production.IsError, Is.Not.True);
            JsonElement json = MachineToolMcpHarness.Json(production);
            Assert.That(json.GetProperty("jobs").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("jobs")[0].GetProperty("programs").GetArrayLength(), Is.EqualTo(1));
            JsonElement part = json.GetProperty("jobs")[0].GetProperty("partSets")[0].GetProperty("parts")[0];
            Assert.That(Decode(part.GetProperty("readings").GetProperty("identifier")).WrappedValue
                .TryGetValue(out string partId), Is.True);
            Assert.That(partId, Is.EqualTo("part-1"));
            Assert.That(Decode(part.GetProperty("readings").GetProperty("quality")).WrappedValue
                .TryGetValue(out int quality), Is.True);
            Assert.That(quality, Is.EqualTo((int)PartQuality.NotYetMeasured));
            Assert.That(Decode(json.GetProperty("activeProgram").GetProperty("readings").GetProperty("name"))
                .WrappedValue.TryGetValue(out string name), Is.True);
            Assert.That(name, Is.EqualTo("CUT.NC"));
            CallToolResult forecasts = await m_mcp.CallAsync("machinetool_read_prognoses", arguments)
                .ConfigureAwait(false);
            Assert.That(forecasts.IsError, Is.Not.True);
            JsonElement prognosis = MachineToolMcpHarness.Json(forecasts).GetProperty("entries")[0];
            Assert.That(prognosis.GetProperty("kind").GetString(), Is.EqualTo("Maintenance"));
            Assert.That(Decode(prognosis.GetProperty("readings").GetProperty("activity")).WrappedValue
                .TryGetValue(out LocalizedText activity), Is.True);
            Assert.That(activity.Text, Is.EqualTo("Inspect cutter"));
        }

        [TestCase("machinetool_read_machine")]
        [TestCase("machinetool_read_equipment")]
        [TestCase("machinetool_read_production")]
        public async Task SerializedResponseLimitsReturnErrorsNotPartialResultsAsync(string tool)
        {
            CallToolResult result = await m_mcp.CallAsync(tool, new JsonObject
            {
                ["machineNodeId"] = m_fixture.Machine.NodeId.ToString(), ["maximumResponseBytes"] = 1024
            }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(MachineToolMcpHarness.Json(result).GetProperty("statusCode").GetString(),
                Does.Contain("BadEncodingLimitsExceeded"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataObservationsRetainQualityAndReleaseTheirOwnSubscriptionsAsync(bool selection)
        {
            Assert.That(m_fixture.Session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int baseline = subscriptions!.Count;
            JsonObject args = selection
                ? new JsonObject
                {
                    ["nodeIds"] = new JsonArray(m_fixture.Machine.State.Monitoring!.MachineTool!.OperationMode!.NodeId
                        .ToString())
                }
                : new JsonObject { ["machineNodeId"] = m_fixture.Machine.NodeId.ToString() };
            args["maximumNotifications"] = selection ? 1 : 3;
            args["durationSeconds"] = 5;
            CallToolResult result = await m_mcp.CallAsync(
                selection ? "machinetool_observe_values" : "machinetool_observe_machine", args).ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True, () => MachineToolMcpHarness.Json(result).GetRawText());
            JsonElement json = MachineToolMcpHarness.Json(result);
            Assert.That(json.GetProperty("limitReached").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("changes").GetArrayLength(), Is.EqualTo(selection ? 1 : 3));
            Assert.That(Decode(json.GetProperty("changes")[0].GetProperty("reading")).SourceTimestamp,
                Is.EqualTo(MachineToolMcpFixture.Acquired));
            Assert.That(subscriptions.Count, Is.EqualTo(baseline));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EventWindowsRemainPassiveAndReportRefreshCompletionAsync(bool retained)
        {
            CallToolResult result = await m_mcp.CallAsync("machinetool_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Minimal.NodeId.ToString(),
                ["durationSeconds"] = 1, ["retainedConditions"] = retained
            }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True);
            JsonElement json = MachineToolMcpHarness.Json(result);
            Assert.That(json.GetProperty("events").GetArrayLength(), Is.Zero);
            Assert.That(json.GetProperty("refreshCompleted").GetBoolean(), Is.EqualTo(retained));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        [Test]
        public async Task RetainedNativeAlertFieldsRemainTypedAndUnacknowledgedAsync()
        {
            CallToolResult result = await m_mcp.CallAsync("machinetool_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Machine.NodeId.ToString(),
                ["durationSeconds"] = 5, ["retainedConditions"] = true
            }).ConfigureAwait(false);
            Assert.That(result.IsError, Is.Not.True, () => MachineToolMcpHarness.Json(result).GetRawText());
            JsonElement json = MachineToolMcpHarness.Json(result);
            Assert.That(json.GetProperty("refreshCompleted").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("events").GetArrayLength(), Is.EqualTo(1));
            JsonElement fields = json.GetProperty("events")[0].GetProperty("fields");
            JsonElement message = fields.EnumerateArray().Single(field =>
                field.GetProperty("browsePath").GetArrayLength() == 1 &&
                field.GetProperty("browsePath")[0].GetString() == new QualifiedName(Opc.Ua.BrowseNames.Message).ToString());
            using var decoder = new JsonDecoder(message.GetProperty("value").GetRawText(),
                m_fixture.Session.MessageContext);
            Assert.That(decoder.ReadVariant("Value").TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("Low lubricant"));
            Assert.That(m_fixture.Machine.Notifications!.TryGetAlert("Lubricant", out AlertState? alert), Is.True);
            Assert.That(alert!.AckedState!.Id!.Value, Is.False);
            Assert.That(alert.Retain!.Value, Is.True);
        }

        [Test]
        public async Task ProtocolCancellationReleasesOnlyThePendingObservationAsync()
        {
            Assert.That(m_fixture.Session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int baseline = subscriptions!.Count;
            Task<CallToolResult> pending = m_mcp.CallAsync("machinetool_observe_machine", new JsonObject
            {
                ["machineNodeId"] = m_fixture.Machine.NodeId.ToString(),
                ["durationSeconds"] = 20, ["maximumNotifications"] = 100
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (subscriptions.Count == baseline)
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
            await m_mcp.CancelAsync().ConfigureAwait(false);
            try
            {
                await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or ModelContextProtocol.McpException)
            {
            }
            while (subscriptions.Count != baseline)
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(subscriptions.Count, Is.EqualTo(baseline));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        private DataValue Decode(JsonElement reading)
        {
            using var decoder = new JsonDecoder(reading.GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadDataValue("DataValue");
        }

        private MachineToolMcpFixture m_fixture = null!;
        private MachineToolMcpHarness m_mcp = null!;
        private static readonly string[] s_passiveNames =
        [
            "machinetool_list_devices", "machinetool_read_machine", "machinetool_read_equipment",
            "machinetool_read_production", "machinetool_read_prognoses", "machinetool_observe_values",
            "machinetool_observe_machine", "machinetool_observe_events"
        ];
    }
}
#endif
