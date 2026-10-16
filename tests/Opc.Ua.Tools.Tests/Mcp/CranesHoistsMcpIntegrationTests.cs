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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Sends real MCP requests through the adapter and typed client to a real opc.tcp crane runtime.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class CranesHoistsMcpIntegrationTests
    {
        /// <summary>
        /// Starts the endpoint and borrows two distinct real identities into the shared MCP session manager.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new CranesHoistsMcpFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_mcp = await CranesHoistsMcpHarness.CreateAsync(m_fixture.Session,
                new CranesHoistsClientOptions { BrowsePageSize = 1 }).ConfigureAwait(false);
            await m_mcp.Manager.RegisterExistingSessionAsync("unauthorized", m_fixture.UnprivilegedSession, "Anonymous")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Restores the independent equipment refusal policy.
        /// </summary>
        [TearDown]
        public void Restore()
        {
            m_fixture.RefuseRequests = false;
        }

        /// <summary>
        /// Stops MCP first, verifying its borrowed sessions still belong to the test fixture.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            try
            {
                if (m_mcp != null)
                {
                    await m_mcp.DisposeAsync().ConfigureAwait(false);
                    Assert.That(m_fixture.Session.Connected, Is.True, "MCP must not dispose borrowed sessions.");
                    Assert.That(m_fixture.UnprivilegedSession.Connected, Is.True);
                }
            }
            finally
            {
                if (m_fixture != null)
                {
                    await m_fixture.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Discovery follows real continuations/vendor inheritance and returns both actual entry points.
        /// </summary>
        [Test]
        public async Task InventoryUsesActualSubtypesAndBothDiscoveryPathsWithoutWriting()
        {
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement result = Success(await CallAsync("craneshoists_list_systems").ConfigureAwait(false));
            Assert.That(result.GetProperty("modelPresent").GetBoolean(), Is.True);
            JsonElement[] systems = [.. result.GetProperty("systems").EnumerateArray()];
            Assert.That(systems, Has.Length.EqualTo(2));
            JsonElement crane = systems.Single(system => system.GetProperty("nodeId").GetString() ==
                m_fixture.Crane.NodeId.ToString());
            Assert.That(crane.GetProperty("typeDefinition").GetString(), Is.EqualTo(m_fixture.VendorType.ToString()));
            Assert.That(crane.GetProperty("kind").GetString(), Is.EqualTo("System"));
            NodeId machines = NodeId.Create(Machinery.Objects.Machines, Machinery.Namespaces.Machinery,
                m_fixture.Session.NamespaceUris);
            JsonElement organized = Success(await CallAsync("craneshoists_list_systems",
                new JsonObject { ["rootNodeId"] = machines.ToString() }).ConfigureAwait(false));
            Assert.That(organized.GetProperty("systems").EnumerateArray()
                .Select(system => system.GetProperty("nodeId").GetString()),
                Is.EquivalentTo(systems.Select(system => system.GetProperty("nodeId").GetString())));
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Snapshots expose actual values and relationships for systems, devices, axes, loads, brakes and protections.
        /// </summary>
        [Test]
        public async Task SystemSnapshotPreservesNativeCapabilitiesQualityTimeAndProtectsAxis()
        {
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement result = Success(await CallAsync("craneshoists_read_system",
                new JsonObject { ["systemNodeId"] = m_fixture.Crane.NodeId.ToString() }).ConfigureAwait(false));
            JsonElement system = result.GetProperty("system");
            Assert.That(system.GetProperty("entry").GetProperty("typeDefinition").GetString(),
                Is.EqualTo(m_fixture.VendorType.ToString()));
            Assert.That(Read(Signal(system, "SystemOn")).WrappedValue.TryGetValue(out bool systemOn), Is.True);
            Assert.That(systemOn, Is.True);
            DataValue serial = Decode(system.GetProperty("identification").GetProperty("serialNumber"));
            Assert.That(serial.WrappedValue.TryGetValue(out string? serialNumber), Is.True);
            Assert.That(serialNumber, Is.EqualTo("McpCrane"));
            JsonElement[] elements = [.. result.GetProperty("elements").EnumerateArray()];
            Assert.That(elements, Has.Length.EqualTo(5));
            JsonElement device = Element(elements, "MotionDevice");
            AssertDouble(Read(Signal(device, "DesignedWorkingPeriod")), 92.5);
            JsonElement axis = Element(elements, "Axis");
            DataValue position = Read(Signal(axis, "ActualPosition"));
            AssertDouble(position, 12.75);
            Assert.That(position.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(position.SourceTimestamp, Is.EqualTo(CranesHoistsMcpFixture.SourceTime));
            AssertDouble(Read(Signal(axis, "LoadMass")), 153.0);
            AssertDouble(Read(Signal(axis, "RatedSpeed")), 2.5);
            Assert.That(axis.GetProperty("axisFunctionValues").GetArrayLength(), Is.EqualTo(9));
            Assert.That(axis.GetProperty("axisFunctionValues")[2].GetProperty("value").GetInt64(), Is.EqualTo(2));
            JsonElement active = Signal(axis, "SpeedLimitDirPlus");
            Assert.That(active.GetProperty("writable").GetBoolean(), Is.False);
            Assert.That(active.GetProperty("configurationRequest").GetBoolean(), Is.False);
            Assert.That(Signal(axis, "SpeedLimitDirPlusSetpoint").GetProperty("configurationRequest").GetBoolean(),
                Is.True);
            Assert.That(Signal(Element(elements, "Motor"), "BrakeReleased").GetProperty("present").GetBoolean(),
                Is.True);
            Assert.That(Read(Signal(Element(elements, "Brake"), "BrakeReleased"))
                .WrappedValue.TryGetValue(out bool released), Is.True);
            Assert.That(released, Is.False);
            JsonElement protection = Element(elements, "ProtectiveFunction");
            JsonElement relationship = protection.GetProperty("relationships")[0];
            NodeId protectsAxis = NodeId.Create(CranesHoists.ReferenceTypes.ProtectsAxis,
                CranesHoistsModel.NamespaceUri, m_fixture.Session.NamespaceUris);
            Assert.That(relationship.GetProperty("referenceType").GetString(), Is.EqualTo(protectsAxis.ToString()));
            Assert.That(relationship.GetProperty("source").GetString(),
                Is.EqualTo(protection.GetProperty("entry").GetProperty("nodeId").GetString()));
            Assert.That(relationship.GetProperty("target").GetString(),
                Is.EqualTo(axis.GetProperty("entry").GetProperty("nodeId").GetString()));
            Assert.That(relationship.GetProperty("isForward").GetBoolean(), Is.True);
            Assert.That(Read(Signal(protection, "Active")).WrappedValue.TryGetValue(out bool activeProtection),
                Is.True);
            Assert.That(activeProtection, Is.False);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// A missing optional feature differs from a present variable with bad acquisition quality.
        /// </summary>
        [Test]
        public async Task MandatoryOnlySnapshotDistinguishesAbsenceFromBadData()
        {
            JsonElement axis = await ReadElementAsync(m_fixture.Minimal.GetElement("Lift").NodeId)
                .ConfigureAwait(false);
            JsonElement absent = Signal(axis, "AxisFunction");
            Assert.That(absent.GetProperty("present").GetBoolean(), Is.False);
            Assert.That(absent.GetProperty("nodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(absent.GetProperty("reading").ValueKind, Is.EqualTo(JsonValueKind.Null));
            JsonElement position = Signal(axis, "ActualPosition");
            Assert.That(position.GetProperty("present").GetBoolean(), Is.True);
            Assert.That(Read(position).StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            JsonElement unsupported = Error(await ConfigureAsync(
                m_fixture.Minimal.GetElement("Lift").NodeId, "SpeedLimitDirPlusSetpoint", JsonValue.Create(25),
                confirm: true).ConfigureAwait(false), "BadNotSupported");
            Assert.That(unsupported.GetProperty("message").GetString(), Does.Contain("not exposed"));
        }

        /// <summary>
        /// Native load frame/vector structures survive both opc.tcp decoding and MCP JSON serialization.
        /// </summary>
        [Test]
        public async Task LoadSnapshotsPreserveNativeGeometryAndOriginalQuality()
        {
            JsonElement axis = await ReadElementAsync(m_fixture.Crane.GetElement("Lift").NodeId)
                .ConfigureAwait(false);
            DataValue inertia = Read(Signal(axis, "LoadInertia"));
            DataValue center = Read(Signal(axis, "LoadCenterOfMass"));
            Assert.That(inertia.WrappedValue.TryGetValue<ThreeDVector>(out ThreeDVector? vector,
                m_fixture.Session.MessageContext), Is.True);
            Assert.That(center.WrappedValue.TryGetValue<ThreeDFrame>(out ThreeDFrame? frame,
                m_fixture.Session.MessageContext), Is.True);
            Assert.That(vector!.X, Is.EqualTo(1));
            Assert.That(vector.Y, Is.EqualTo(2));
            Assert.That(vector.Z, Is.EqualTo(3));
            Assert.That(frame!.CartesianCoordinates.X, Is.EqualTo(4));
            Assert.That(frame.CartesianCoordinates.Y, Is.EqualTo(5));
            Assert.That(frame.CartesianCoordinates.Z, Is.EqualTo(6));
            Assert.That(frame.Orientation.A, Is.EqualTo(10));
            Assert.That(center.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(inertia.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(center.SourceTimestamp, Is.EqualTo(CranesHoistsMcpFixture.SourceTime));
            Assert.That(inertia.SourceTimestamp, Is.EqualTo(CranesHoistsMcpFixture.SourceTime));
        }

        /// <summary>
        /// Both boundaries and an interior setpoint reach the provider without overwriting reported active limits.
        /// </summary>
        [TestCase("SpeedLimitDirPlusSetpoint", 0.0)]
        [TestCase("SpeedLimitDirPlusSetpoint", 100.0)]
        [TestCase("SpeedLimitDirPlusSetpoint", 48.75)]
        [TestCase("SpeedLimitDirMinusSetpoint", 0.0)]
        [TestCase("SpeedLimitDirMinusSetpoint", 100.0)]
        [TestCase("SpeedLimitDirMinusSetpoint", 48.75)]
        public async Task AcceptedSetpointsAreRequestsNotActiveHardwareState(string signal, double requested)
        {
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement response = Success(await ConfigureAsync(axis, signal,
                JsonValue.Create(requested), confirm: true).ConfigureAwait(false));
            Assert.That(response.GetProperty("requestAccepted").GetBoolean(), Is.True);
            Assert.That(response.GetProperty("activeValueVerified").GetBoolean(), Is.False);
            Assert.That(response.GetProperty("requestedValue").GetDouble(), Is.EqualTo(requested));
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes + 1));
            CraneLimitRequest accepted = m_fixture.Provider.AcceptedRequests[^1];
            Assert.That(accepted.Value.TryGetValue(out double actualRequest), Is.True);
            Assert.That(actualRequest, Is.EqualTo(requested));
            JsonElement actual = await ReadElementAsync(axis).ConfigureAwait(false);
            AssertDouble(Read(Signal(actual, signal)), requested);
            AssertDouble(Read(Signal(actual, "SpeedLimitDirPlus")), 31.5);
            AssertDouble(Read(Signal(actual, "SpeedLimitDirMinus")), 27.5);
        }

        /// <summary>
        /// Responses report the accepted wire Double, not an arbitrarily large or higher-precision JSON spelling.
        /// </summary>
        [Test]
        public async Task ConfigurationResponseReportsTheActualWireValueWithoutEchoingUnboundedInput()
        {
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            JsonElement response = Success(await ConfigureAsync(axis, "SpeedLimitDirPlusSetpoint",
                JsonNode.Parse("25." + new string('0', 2048)), confirm: true).ConfigureAwait(false));
            Assert.That(response.GetProperty("requestedValue").GetRawText(), Is.EqualTo("25"));
            Assert.That(m_fixture.Provider.AcceptedRequests[^1].Value.TryGetValue(out double accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(25.0));
            Assert.That(response.GetRawText(), Has.Length.LessThan(2048));
            JsonElement actual = await ReadElementAsync(axis).ConfigureAwait(false);
            AssertDouble(Read(Signal(actual, "SpeedLimitDirPlus")), 31.5);
        }

        /// <summary>
        /// Each Boolean request retains its native type and the actual server-side distinction from active limits.
        /// </summary>
        [TestCase("SpeedLimitGlobalEnabled", true)]
        [TestCase("SpeedLimitGlobalEnabled", false)]
        [TestCase("SpeedLimitEnabledDirMinus", true)]
        [TestCase("SpeedLimitEnabledDirPlus", false)]
        public async Task AcceptedEnableRequestsRemainSeparateFromReportedLimits(string signal, bool value)
        {
            NodeId node = signal == "SpeedLimitGlobalEnabled"
                ? m_fixture.Crane.NodeId : m_fixture.Crane.GetElement("Lift").NodeId;
            JsonElement response = Success(await ConfigureAsync(node, signal, JsonValue.Create(value), confirm: true)
                .ConfigureAwait(false));
            Assert.That(response.GetProperty("requestAccepted").GetBoolean(), Is.True);
            Assert.That(response.GetProperty("requestedValue").GetBoolean(), Is.EqualTo(value));
            Assert.That(response.GetProperty("activeValueVerified").GetBoolean(), Is.False);
            JsonElement actual = await ReadElementAsync(node).ConfigureAwait(false);
            Assert.That(Read(Signal(actual, signal)).WrappedValue.TryGetValue(out bool accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(value));
            JsonElement axis = await ReadElementAsync(m_fixture.Crane.GetElement("Lift").NodeId).ConfigureAwait(false);
            AssertDouble(Read(Signal(axis, "SpeedLimitDirPlus")), 31.5);
            AssertDouble(Read(Signal(axis, "SpeedLimitDirMinus")), 27.5);
        }

        /// <summary>
        /// Client consent is mandatory even for a server-authorized identity.
        /// </summary>
        [Test]
        public async Task MissingExplicitConsentCannotReachTheProvider()
        {
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement before = await ReadElementAsync(axis).ConfigureAwait(false);
            Error(await ConfigureAsync(axis, "SpeedLimitDirPlusSetpoint", JsonValue.Create(25), confirm: false)
                .ConfigureAwait(false), "BadUserAccessDenied");
            JsonElement after = await ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Read(Signal(after, "SpeedLimitDirPlusSetpoint")).WrappedValue,
                Is.EqualTo(Read(Signal(before, "SpeedLimitDirPlusSetpoint")).WrappedValue));
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Client consent never bypasses real session authorization or provider refusal.
        /// </summary>
        [TestCase(false, "BadUserAccessDenied")]
        [TestCase(true, "BadOutOfService")]
        public async Task RealServerDenialsPreserveRequestAndActiveValues(bool providerRefusal, string status)
        {
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            JsonElement before = await ReadElementAsync(axis).ConfigureAwait(false);
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            m_fixture.RefuseRequests = providerRefusal;
            Error(await ConfigureAsync(axis, "SpeedLimitDirPlusSetpoint", JsonValue.Create(21.0), confirm: true,
                sessionName: providerRefusal ? "crane" : "unauthorized").ConfigureAwait(false), status);
            JsonElement after = await ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Read(Signal(after, "SpeedLimitDirPlusSetpoint")).WrappedValue,
                Is.EqualTo(Read(Signal(before, "SpeedLimitDirPlusSetpoint")).WrappedValue));
            AssertDouble(Read(Signal(after, "SpeedLimitDirPlus")), 31.5);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Out-of-range, nonfinite and incorrectly typed inputs cannot create configuration side effects.
        /// </summary>
        [TestCase("-0.00001")]
        [TestCase("100.00001")]
        [TestCase("1e999")]
        [TestCase("\"NaN\"")]
        [TestCase("true")]
        public async Task InvalidBoundaryRequestsLeaveTheActualCraneUnchanged(string value)
        {
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement before = await ReadElementAsync(axis).ConfigureAwait(false);
            Error(await ConfigureAsync(axis, "SpeedLimitDirPlusSetpoint", JsonNode.Parse(value), confirm: true)
                .ConfigureAwait(false), "BadOutOfRange");
            JsonElement after = await ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Read(Signal(after, "SpeedLimitDirPlusSetpoint")).WrappedValue,
                Is.EqualTo(Read(Signal(before, "SpeedLimitDirPlusSetpoint")).WrappedValue));
            AssertDouble(Read(Signal(after, "SpeedLimitDirPlus")), 31.5);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// The adapter never offers raw writes to active limits, protective state or control/motion variables.
        /// </summary>
        [TestCase("SpeedLimitDirPlus")]
        [TestCase("Active")]
        [TestCase("DirPlusStop")]
        [TestCase("ExternalControlRequest")]
        public async Task ProtectedActiveValuesAreNotConfigurationSelectors(string signal)
        {
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            NodeId axis = m_fixture.Crane.GetElement("Lift").NodeId;
            Error(await ConfigureAsync(axis, signal, JsonValue.Create(25), confirm: true).ConfigureAwait(false),
                "BadInvalidArgument");
            JsonElement result = await ReadElementAsync(axis).ConfigureAwait(false);
            AssertDouble(Read(Signal(result, "SpeedLimitDirPlus")), 31.5);
            Assert.That(Signal(result, "SpeedLimitDirPlus").GetProperty("writable").GetBoolean(), Is.False);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Wrong element types and failed reads propagate as server errors, never successful absence.
        /// </summary>
        [Test]
        public async Task InvalidTargetsAndFailedReadsReturnTheirOriginalErrors()
        {
            Error(await ConfigureAsync(m_fixture.Crane.NodeId, "SpeedLimitDirPlusSetpoint", JsonValue.Create(25),
                confirm: true).ConfigureAwait(false), "BadTypeMismatch");
            Error(await CallAsync("craneshoists_read_element", new JsonObject { ["elementNodeId"] = "i=85" })
                .ConfigureAwait(false), "BadTypeMismatch");
            CallToolResult missing = await CallAsync("craneshoists_read_system",
                new JsonObject { ["systemNodeId"] = "ns=1;s=no-such-crane" }).ConfigureAwait(false);
            Assert.That(missing.IsError, Is.True);
            Assert.That(CranesHoistsMcpHarness.Json(missing).GetProperty("statusCode").GetString(),
                Does.StartWith("Bad"));
        }

        /// <summary>
        /// Both object-count and serialized-byte bounds fail explicitly rather than returning truncated inventory.
        /// </summary>
        [Test]
        public async Task InventoryAndSerializedSnapshotBoundsReturnErrorsNotPartialResults()
        {
            Error(await CallAsync("craneshoists_list_systems", new JsonObject { ["maximumResults"] = 1 })
                .ConfigureAwait(false), "BadTooManyOperations");
            CallToolResult result = await CallAsync("craneshoists_read_system", new JsonObject
            {
                ["systemNodeId"] = m_fixture.Crane.NodeId.ToString(),
                ["maximumResponseBytes"] = 1024
            }).ConfigureAwait(false);
            JsonElement error = Error(result, "BadEncodingLimitsExceeded");
            Assert.That(error.TryGetProperty("system", out _), Is.False);
            Assert.That(Encoding.UTF8.GetByteCount(result.Content.OfType<TextContentBlock>().Single().Text),
                Is.LessThanOrEqualTo(1024));
        }

        /// <summary>
        /// A real bounded data subscription retains source quality/time without removing another live subscription.
        /// </summary>
        [Test]
        public async Task DataObservationKeepsOriginalValuesAndOnlyReleasesItsOwnSubscription()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            NodeId position = m_fixture.Crane.GetSignal(new("Lift", CraneSignal.ActualPosition)).NodeId;
            JsonElement response = Success(await CallAsync("craneshoists_observe_values", new JsonObject
            {
                ["nodeIds"] = new JsonArray(position.ToString()),
                ["maximumNotifications"] = 1,
                ["durationSeconds"] = 10
            }).ConfigureAwait(false));
            Assert.That(response.GetProperty("completionReason").GetString(), Is.EqualTo("LimitReached"));
            Assert.That(response.GetProperty("values").GetArrayLength(), Is.EqualTo(1));
            JsonElement item = response.GetProperty("values")[0];
            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(position.ToString()));
            DataValue value = Decode(item.GetProperty("reading"));
            AssertDouble(value, 12.75);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(value.SourceTimestamp, Is.EqualTo(CranesHoistsMcpFixture.SourceTime));
            Assert.That(response.GetProperty("droppedNotifications").GetInt64(), Is.Zero);
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(unrelated.MonitoredItems.Items.Single().Created, Is.True);
            Assert.That(m_fixture.Session.Connected, Is.True);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Real event identity, source time and payload survive the MCP JSON boundary unchanged.
        /// </summary>
        [Test]
        public async Task EventObservationPreservesIdentityQualifiedFieldsAndSubscriptionOwnership()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            Task<CallToolResult> observing = CallAsync("craneshoists_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Crane.EventNotifierId.ToString(),
                ["maximumNotifications"] = 1,
                ["durationSeconds"] = 15
            });
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await m_fixture.PublishEventAsync().ConfigureAwait(false);
            JsonElement response = Success(await observing.ConfigureAwait(false));
            Assert.That(response.GetProperty("completionReason").GetString(), Is.EqualTo("LimitReached"));
            Assert.That(response.GetProperty("events").GetArrayLength(), Is.EqualTo(1));
            JsonElement notification = response.GetProperty("events")[0];
            Assert.That(notification.GetProperty("eventIdBase64").GetString(), Is.EqualTo("BwgJ"));
            Assert.That(notification.GetProperty("eventType").GetString(), Is.EqualTo(m_fixture.EventType.ToString()));
            using var decoder = new JsonDecoder(notification.GetProperty("fields").GetRawText(),
                m_fixture.Session.MessageContext);
            ArrayOf<Variant> fields = decoder.ReadVariantArray("Fields");
            JsonElement[] paths = [.. response.GetProperty("fieldPaths").EnumerateArray()];
            Assert.That(fields.Count, Is.EqualTo(paths.Length));
            int time = Array.FindIndex(paths, path => path.GetArrayLength() == 1 &&
                QualifiedName.Parse(path[0].GetString()!).Name == Opc.Ua.BrowseNames.Time);
            Assert.That(time, Is.GreaterThanOrEqualTo(0));
            Assert.That(fields[time].TryGetValue(out DateTimeUtc eventTime), Is.True);
            Assert.That(eventTime, Is.EqualTo(CranesHoistsMcpFixture.SourceTime));
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(m_fixture.Session.Connected, Is.True);
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Window completion is an explicitly bounded empty observation, not a subscription left running.
        /// </summary>
        [Test]
        public async Task EventWindowEndsWithoutChangingSettingsOrKeepingResources()
        {
            int subscriptions = m_fixture.Subscriptions.Count;
            int writes = m_fixture.Provider.AcceptedRequests.Count;
            JsonElement response = Success(await CallAsync("craneshoists_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Crane.EventNotifierId.ToString(),
                ["durationSeconds"] = 1
            }).ConfigureAwait(false));
            Assert.That(response.GetProperty("completionReason").GetString(), Is.EqualTo("WindowElapsed"));
            Assert.That(response.GetProperty("events").GetArrayLength(), Is.Zero);
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Provider.AcceptedRequests.Count, Is.EqualTo(writes));
        }

        /// <summary>
        /// Missing monitored nodes retain the original server error and release only the failed call's resources.
        /// </summary>
        [Test]
        public async Task FailedObservationPreservesTheServerErrorAndUnrelatedSubscription()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            Error(await CallAsync("craneshoists_observe_values", new JsonObject
            {
                ["nodeIds"] = new JsonArray("ns=1;s=missing-monitored-node"),
                ["durationSeconds"] = 10
            }).ConfigureAwait(false), "BadNodeIdUnknown");
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Cancellation traverses the actual MCP transport and drains only the observation it created.
        /// </summary>
        [Test]
        public async Task McpCancellationReleasesOnlyTheOwnedSubscription()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            Task<CallToolResult> observing = CallAsync("craneshoists_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Crane.EventNotifierId.ToString(),
                ["durationSeconds"] = 30
            });
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await m_mcp.CancelLastRequestAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () => await observing.ConfigureAwait(false));
            Assert.That(m_mcp.LastToolCancellationToken.CanBeCanceled, Is.True);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (m_fixture.Subscriptions.Count != subscriptions)
            {
                await Task.Delay(20, deadline.Token).ConfigureAwait(false);
            }
            Assert.That(m_mcp.LastToolCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Direct embedding callers retain their cancellation identity through the typed adapter and cleanup.
        /// </summary>
        [Test]
        public async Task DirectAdapterCancellationRetainsTheCallerTokenAndCleansUp()
        {
            await using ISubscription unrelated = await AddUnrelatedSubscriptionAsync().ConfigureAwait(false);
            int subscriptions = m_fixture.Subscriptions.Count;
            using var cancellation = new CancellationTokenSource();
            Task<CallToolResult> observing = CranesHoistsTools.ObserveEventsAsync(
                m_mcp.Accessor, m_fixture.Crane.EventNotifierId.ToString(), durationSeconds: 30,
                sessionName: "crane", ct: cancellation.Token);
            await m_fixture.WaitForObservationAsync(unrelated).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException error = Assert.CatchAsync<OperationCanceledException>(
                async () => await observing.ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Subscriptions.Items, Does.Contain(unrelated));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Oversized real event payloads fail without truncating a successful result or leaking a subscription.
        /// </summary>
        [Test]
        public async Task OversizedEventPayloadFailsAndReleasesItsSubscription()
        {
            int subscriptions = m_fixture.Subscriptions.Count;
            Task<CallToolResult> observing = CallAsync("craneshoists_observe_events", new JsonObject
            {
                ["notifierNodeId"] = m_fixture.Crane.EventNotifierId.ToString(),
                ["maximumNotifications"] = 1,
                ["maximumResponseBytes"] = 1024,
                ["durationSeconds"] = 15
            });
            await m_fixture.WaitForObservationAsync().ConfigureAwait(false);
            await m_fixture.PublishEventAsync(new string('x', 4096)).ConfigureAwait(false);
            Error(await observing.ConfigureAwait(false), "BadEncodingLimitsExceeded");
            Assert.That(m_fixture.Subscriptions.Count, Is.EqualTo(subscriptions));
            Assert.That(m_fixture.Session.Connected, Is.True);
        }

        /// <summary>
        /// Creates an actual second monitored subscription whose lifetime must not be altered by the tools.
        /// </summary>
        private async Task<ISubscription> AddUnrelatedSubscriptionAsync()
        {
            ISubscription subscription = m_fixture.Subscriptions.Add(
                new Mock<ISubscriptionNotificationHandler>().Object,
                new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions()));
            try
            {
                Assert.That(subscription.MonitoredItems.TryAdd("borrowed",
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                    {
                        StartNodeId = m_fixture.Crane.GetSignal(new("McpCrane", CraneSignal.SystemOn)).NodeId,
                        AttributeId = Attributes.Value
                    }), out IMonitoredItem? item), Is.True);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!item!.Created)
                {
                    await Task.Delay(20, deadline.Token).ConfigureAwait(false);
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
        /// Calls MCP with an explicit session, because the manager deliberately contains two connected identities.
        /// </summary>
        private Task<CallToolResult> CallAsync(
            string name, JsonObject? arguments = null, CancellationToken cancellationToken = default)
        {
            arguments ??= new JsonObject();
            if (!arguments.ContainsKey("sessionName"))
            {
                arguments["sessionName"] = "crane";
            }
            return m_mcp.CallAsync(name, arguments, cancellationToken);
        }

        /// <summary>
        /// Sends one explicit JSON configuration request through MCP, not directly to the typed client.
        /// </summary>
        private Task<CallToolResult> ConfigureAsync(
            NodeId node, string signal, JsonNode? value, bool confirm, string sessionName = "crane")
        {
            return CallAsync("craneshoists_configure_limit", new JsonObject
            {
                ["elementNodeId"] = node.ToString(),
                ["signal"] = signal,
                ["value"] = value,
                ["confirmConfiguration"] = confirm,
                ["sessionName"] = sessionName
            });
        }

        /// <summary>
        /// Reads the focused public MCP snapshot.
        /// </summary>
        private async Task<JsonElement> ReadElementAsync(NodeId node)
        {
            return Success(await CallAsync("craneshoists_read_element",
                new JsonObject { ["elementNodeId"] = node.ToString() }).ConfigureAwait(false));
        }

        /// <summary>
        /// Decodes the exact native UA DataValue delivered in an MCP reading.
        /// </summary>
        private DataValue Decode(JsonElement reading)
        {
            using var decoder = new JsonDecoder(reading.GetRawText(), m_fixture.Session.MessageContext);
            return decoder.ReadDataValue("DataValue");
        }

        /// <summary>
        /// Reads a present signal's DataValue.
        /// </summary>
        private DataValue Read(JsonElement signal)
        {
            Assert.That(signal.GetProperty("present").GetBoolean(), Is.True);
            return Decode(signal.GetProperty("reading"));
        }

        /// <summary>
        /// Locates a signal by its documented selector, independently of serialized field order.
        /// </summary>
        private static JsonElement Signal(JsonElement element, string name)
        {
            return element.GetProperty("signals").EnumerateArray()
                .Single(signal => signal.GetProperty("signal").GetString() == name);
        }

        /// <summary>
        /// Locates the sole component of a configured test kind.
        /// </summary>
        private static JsonElement Element(JsonElement[] elements, string kind)
        {
            return elements.Single(element => element.GetProperty("entry").GetProperty("kind").GetString() == kind);
        }

        /// <summary>
        /// Asserts a native numeric value without boxing or flattening the wire type.
        /// </summary>
        private static void AssertDouble(in DataValue value, double expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Requires actual MCP success before inspecting the payload.
        /// </summary>
        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
            return CranesHoistsMcpHarness.Json(result);
        }

        /// <summary>
        /// Requires both the protocol error indicator and the original OPC UA code.
        /// </summary>
        private static JsonElement Error(CallToolResult result, string code)
        {
            Assert.That(result.IsError, Is.True);
            JsonElement json = CranesHoistsMcpHarness.Json(result);
            Assert.That(json.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("statusCode").GetString(), Does.StartWith(code));
            return json;
        }

        /// <summary>The real endpoint and borrowed sessions.</summary>
        private CranesHoistsMcpFixture m_fixture = null!;

        /// <summary>The in-process MCP client/server pair.</summary>
        private CranesHoistsMcpHarness m_mcp = null!;
    }
}
#endif
