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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Exercises MCP wire requests through a real in-process server and a genuine native GMS opc.tcp endpoint.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed partial class GmsMcpIntegrationTests
    {
        /// <summary>
        /// Creates isolated native provider state and a real MCP client/server pair for each scenario.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_mcp = null!;
            m_fixture = new GmsMcpFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_mcp = await GmsMcpHarness.CreateAsync(m_fixture.Session).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes only resources owned by this test after all bounded workflows have completed.
        /// </summary>
        [TearDown]
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

        /// <summary>
        /// Inventory retains actual vendor subtype identity and capabilities differ between configured instances.
        /// </summary>
        [Test]
        public async Task InventoryAndCapabilitiesReflectActualInstances()
        {
            JsonElement inventory = Success(await m_mcp.CallAsync("gms_list_systems").ConfigureAwait(false));
            Assert.That(inventory.GetProperty("modelPresent").GetBoolean(), Is.True);
            JsonElement[] systems = [.. inventory.GetProperty("systems").EnumerateArray()];
            Assert.That(systems, Has.Length.EqualTo(2));
            JsonElement full = systems.Single(item => item.GetProperty("nodeId").GetString() ==
                m_fixture.System.NodeId.ToString());
            Assert.That(full.GetProperty("typeDefinitionId").GetString(), Is.EqualTo(m_fixture.VendorType.ToString()));
            JsonElement capabilities = await ReadAsync("Capabilities").ConfigureAwait(false);
            foreach (string field in new[]
            {
                "workspace", "loading", "sensorInventory", "accessories", "additionalSensors",
                "calibration", "retainedProductionPlan", "inheritedProductionStatistics", "publishedResults",
                "corrections", "resultFileTransfer"
            })
            {
                Assert.That(capabilities.GetProperty(field).GetBoolean(), Is.True, field);
            }
            Assert.That(capabilities.GetProperty("completeGmsProductionFacetClaimed").GetBoolean(), Is.False);
            Assert.That(capabilities.GetProperty("methodAuthorizationVerified").GetBoolean(), Is.False);
            Assert.That(capabilities.GetProperty("limitation").GetString(),
                Does.Contain("GMS_ProductionStatisticsType"));
            JsonElement minimal = await ReadAsync("Capabilities", m_fixture.Minimal.NodeId).ConfigureAwait(false);
            foreach (string field in new[]
            {
                "workspace", "loading", "sensorInventory", "accessories", "additionalSensors",
                "calibration", "retainedProductionPlan", "corrections", "resultFileTransfer"
            })
            {
                Assert.That(minimal.GetProperty(field).GetBoolean(), Is.False, field);
            }
            Assert.That(minimal.GetProperty("resultManagement").GetBoolean(), Is.True);
            Assert.That(minimal.GetProperty("publishedResults").GetBoolean(), Is.True,
                "The shared result binding retains its explicitly empty Results folder for zero slots.");
            Assert.That(minimal.GetProperty("sensorCount").GetInt32(), Is.Zero);
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Native snapshots preserve numeric types, negative qualification hours, quality and optional absence.
        /// </summary>
        [Test]
        public async Task NativeSnapshotsPreserveQualityTypesAndOptionalPresence()
        {
            JsonElement system = await ReadAsync("System").ConfigureAwait(false);
            DataValue workspace = Variable(system.GetProperty("identification").GetProperty("workspace"));
            Assert.That(workspace.WrappedValue.TryGetStructure<CartesianWorkspaceType>(
                out CartesianWorkspaceType? geometry), Is.True);
            Assert.That(geometry!.Width, Is.EqualTo(100));
            Assert.That(geometry.Length, Is.EqualTo(150));
            Assert.That(geometry.Height, Is.EqualTo(80));
            DataValue load = Variable(system.GetProperty("loading").GetProperty("loadStatus"));
            Assert.That(load.WrappedValue.TryGetValue(out uint loadStatus), Is.True);
            Assert.That(loadStatus, Is.EqualTo(2));
            Assert.That(load.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
            JsonElement equipment = await ReadAsync("Equipment").ConfigureAwait(false);
            Assert.That(equipment.GetProperty("accessoriesPresent").GetBoolean(), Is.True);
            Assert.That(equipment.GetProperty("additionalSensorsPresent").GetBoolean(), Is.True);
            JsonElement multi = equipment.GetProperty("sensors")[0];
            Assert.That(multi.GetProperty("native").GetProperty("class").GetProperty("present").GetBoolean(), Is.False);
            JsonElement probe = multi.GetProperty("sensors")[0];
            DataValue classification = Variable(probe.GetProperty("native").GetProperty("class"));
            Assert.That(classification.WrappedValue.TryGetValue(out ushort sensorClass), Is.True);
            Assert.That(sensorClass, Is.EqualTo(3));
            DataValue capabilities = Variable(probe.GetProperty("native").GetProperty("capabilities"));
            Assert.That(capabilities.WrappedValue.TryGetValue(out ArrayOf<ushort> values), Is.True);
            Assert.That(values, Is.EqualTo((ArrayOf<ushort>)[1, 4]));
            JsonElement life = probe.GetProperty("life")[0].GetProperty("fields");
            DataValue remaining = Decode(life.GetProperty("value"));
            Assert.That(remaining.WrappedValue.TryGetValue(out double hours), Is.True);
            Assert.That(hours, Is.EqualTo(-0.25));
            Assert.That(remaining.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(remaining.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
            Assert.That(Decode(life.GetProperty("isCountingUp")).WrappedValue.TryGetValue(out bool countingUp),
                Is.True);
            Assert.That(countingUp, Is.False);
            JsonElement[] accessories = [.. equipment.GetProperty("accessories").EnumerateArray()];
            Assert.That(accessories, Has.Length.EqualTo(3));
            JsonElement rotary = accessories.Single(item => item.GetProperty("kind").GetString() == "RotaryTable");
            Assert.That(Variable(rotary.GetProperty("fields").GetProperty("numberOfAxes")).WrappedValue
                .TryGetValue(out byte axes), Is.True);
            Assert.That(axes, Is.EqualTo(2));
            Assert.That(Variable(rotary.GetProperty("identification").GetProperty("serialNumber")).WrappedValue
                .TryGetValue(out string serial), Is.True);
            Assert.That(serial, Is.EqualTo("RT-1"));
            Assert.That(rotary.GetProperty("fields").GetProperty("isAvailable").GetProperty("present").GetBoolean(),
                Is.False);
            DataValue temperature = Decode(equipment.GetProperty("additionalSensors")[0].GetProperty("reading"));
            Assert.That(temperature.WrappedValue.TryGetValue(out float celsius), Is.True);
            Assert.That(celsius, Is.EqualTo(21.5f));
            Assert.That(temperature.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(temperature.SourceTimestamp, Is.EqualTo(GmsMcpFixture.Acquired));
            JsonElement calibration = await ReadAsync("Calibration").ConfigureAwait(false);
            Assert.That(calibration.GetProperty("present").GetBoolean(), Is.True);
            JsonElement fields = calibration.GetProperty("calibration").GetProperty("fields");
            Assert.That(Variable(fields.GetProperty("calibrated")).WrappedValue.TryGetValue(out bool calibrated),
                Is.True);
            Assert.That(calibrated, Is.True);
            Assert.That(Variable(fields.GetProperty("calibrationCertificate")).WrappedValue.TryGetValue(
                out ArrayOf<string> certificates), Is.True);
            Assert.That(certificates, Is.EqualTo((ArrayOf<string>)["urn:test:calibration:certificate-1"]));
            JsonElement minimal = await ReadAsync("System", m_fixture.Minimal.NodeId).ConfigureAwait(false);
            Assert.That(minimal.GetProperty("identification").GetProperty("workspace")
                .GetProperty("present").GetBoolean(), Is.False);
            Assert.That(minimal.GetProperty("loading").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(minimal.GetProperty("toolMonitoring").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Decode(minimal.GetProperty("monitoring").GetProperty("operationMode")).StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            JsonElement noCalibration = await ReadAsync("Calibration", m_fixture.Minimal.NodeId).ConfigureAwait(false);
            Assert.That(noCalibration.GetProperty("present").GetBoolean(), Is.False);
            Assert.That(noCalibration.GetProperty("calibration").ValueKind, Is.EqualTo(JsonValueKind.Null));
            JsonElement noEquipment = await ReadAsync("Equipment", m_fixture.Minimal.NodeId).ConfigureAwait(false);
            Assert.That(noEquipment.GetProperty("accessoriesPresent").GetBoolean(), Is.False);
            Assert.That(noEquipment.GetProperty("additionalSensorsPresent").GetBoolean(), Is.False);
            Assert.That(noEquipment.GetProperty("sensors").GetArrayLength(), Is.Zero);
            Assert.That(noEquipment.GetProperty("toolsNodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Native job and part extensions preserve standard counters and their actual scalar/array catalog types.
        /// </summary>
        [Test]
        public async Task ProductionRetainsNativeMetadataWithoutClaimingUndefinedFacet()
        {
            JsonElement result = await ReadAsync("Production").ConfigureAwait(false);
            Assert.That(result.GetProperty("completeGmsProductionFacetClaimed").GetBoolean(), Is.False);
            Assert.That(Decode(result.GetProperty("partsProducedInLifetime")).WrappedValue.TryGetValue(out uint count),
                Is.True);
            Assert.That(count, Is.EqualTo(10));
            JsonElement job = result.GetProperty("jobs")[0];
            Assert.That(job.GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(NodeId.Create(GMS.ObjectTypes.GMSJobType, GmsModel.NamespaceUri,
                    m_fixture.Session.NamespaceUris).ToString()));
            Assert.That(Variable(job.GetProperty("native").GetProperty("batchIdentifier")).WrappedValue
                .TryGetValue(out string batch), Is.True);
            Assert.That(batch, Is.EqualTo("B-2026"));
            Assert.That(Variable(job.GetProperty("native").GetProperty("measurementReason")).WrappedValue
                .TryGetValue(out MeasurementReasonEnum reason), Is.True);
            Assert.That(reason, Is.EqualTo(MeasurementReasonEnum.AuditMeasurement));
            Assert.That(job.GetProperty("programs").GetArrayLength(), Is.EqualTo(1));
            Assert.That(job.GetProperty("partSets")[0].GetProperty("partNodeIds").GetArrayLength(), Is.EqualTo(1));
            JsonElement part = job.GetProperty("parts")[0];
            Assert.That(part.GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(NodeId.Create(GMS.ObjectTypes.GMSPartType, GmsModel.NamespaceUri,
                    m_fixture.Session.NamespaceUris).ToString()));
            Assert.That(Variable(part.GetProperty("nestIdentifier").GetProperty("fields").GetProperty("catalogEntry"))
                .WrappedValue.TryGetValue(out string catalog), Is.True);
            Assert.That(catalog, Is.EqualTo("Nest-42"));
            Assert.That(Decode(part.GetProperty("processParameter").GetProperty("reading")).WrappedValue
                .TryGetValue(out ArrayOf<double> parameters), Is.True);
            Assert.That(parameters, Is.EqualTo((ArrayOf<double>)[1, 2.5]));
            Assert.That(Decode(part.GetProperty("operator").GetProperty("reading")).WrappedValue
                .TryGetValue(out uint operatorId), Is.True);
            Assert.That(operatorId, Is.EqualTo(42));
            JsonElement minimal = await ReadAsync("Production", m_fixture.Minimal.NodeId).ConfigureAwait(false);
            Assert.That(minimal.GetProperty("jobs").GetArrayLength(), Is.Zero);
            Assert.That(minimal.GetProperty("productionPlanId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Native topology limits and complete JSON bounds fail instead of returning successful partial inventories.
        /// </summary>
        [Test]
        public async Task InventoryAndSnapshotBoundsRejectPartialSuccess()
        {
            CallToolResult inventory = await m_mcp.CallAsync("gms_list_systems",
                new JsonObject { ["maximumResults"] = 1 }).ConfigureAwait(false);
            Assert.That(inventory.IsError, Is.True);
            Assert.That(GmsMcpHarness.Json(inventory).TryGetProperty("systems", out _), Is.False);
            CallToolResult small = await m_mcp.CallAsync("gms_read_system", new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(), ["maximumResponseBytes"] = 1024
            }).ConfigureAwait(false);
            JsonElement error = GmsMcpParameterTests.Error(small, "BadEncodingLimitsExceeded");
            Assert.That(error.TryGetProperty("monitoring", out _), Is.False);
            Assert.That(Encoding.UTF8.GetByteCount(small.Content.OfType<TextContentBlock>().Single().Text),
                Is.LessThanOrEqualTo(1024));
            await using GmsMcpHarness narrow = await GmsMcpHarness.CreateAsync(m_fixture.Session,
                new GmsClientOptions { MaximumElements = 1 }).ConfigureAwait(false);
            GmsMcpParameterTests.Error(await narrow.CallAsync("gms_read_system", new JsonObject
            {
                ["systemNodeId"] = m_fixture.System.NodeId.ToString(),
                ["section"] = "Equipment", ["maximumElements"] = 100
            }).ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Non-GMS objects cannot be used as arbitrary method targets, even in an endpoint containing the model.
        /// </summary>
        [Test]
        public async Task InvalidTargetAndMissingArgumentsAreExplicitErrors()
        {
            GmsMcpParameterTests.Error(await m_mcp.CallAsync("gms_read_system",
                new JsonObject { ["systemNodeId"] = "i=85" }).ConfigureAwait(false), "BadTypeDefinitionInvalid");
            CallToolResult missing = await m_mcp.CallAsync("gms_acknowledge_results").ConfigureAwait(false);
            Assert.That(missing.IsError, Is.True);
            Assert.That(missing.Content.OfType<TextContentBlock>().Single().Text,
                Does.Contain("systemNodeId").And.Contain("resultIds"));
            VerifyNoAcknowledgements();
        }

        /// <summary>
        /// Reads one section through the actual MCP protocol rather than directly through the typed facade.
        /// </summary>
        private async Task<JsonElement> ReadAsync(string section, NodeId node = default)
        {
            return Success(await m_mcp.CallAsync("gms_read_system", new JsonObject
            {
                ["systemNodeId"] = (node.IsNull ? m_fixture.System.NodeId : node).ToString(),
                ["section"] = section
            }).ConfigureAwait(false));
        }

        /// <summary>
        /// Decodes original native samples using the connected server's real encodeable context.
        /// </summary>
        private DataValue Decode(JsonElement value)
        {
            using var decoder = new JsonDecoder(value.GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadDataValue("DataValue");
        }

        /// <summary>
        /// Requires a modeled variable and decodes its independently acquired sample.
        /// </summary>
        private DataValue Variable(JsonElement value)
        {
            Assert.That(value.GetProperty("present").GetBoolean(), Is.True);
            return Decode(value.GetProperty("reading"));
        }

        /// <summary>
        /// Requires actual successful MCP completion before interpreting any data.
        /// </summary>
        private static JsonElement Success(CallToolResult value)
        {
            Assert.That(value.IsError, Is.False, value.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
            return GmsMcpHarness.Json(value);
        }

        /// <summary>
        /// Proves that passive workflows never crossed the explicitly state-changing result-store boundary.
        /// </summary>
        private void VerifyNoAcknowledgements()
        {
            m_fixture.Store.Verify(value => value.AcknowledgeAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>The test-owned real endpoint and actual native instances.</summary>
        private GmsMcpFixture m_fixture = null!;

        /// <summary>The real in-process MCP connection pair.</summary>
        private GmsMcpHarness m_mcp = null!;
    }
}
#endif
