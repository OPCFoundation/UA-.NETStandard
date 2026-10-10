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
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins the Machinery catalog, absent roles and lossless numeric projections.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class MachineryMcpToolsTests
    {
        [Test]
        public async Task CatalogHasNineteenToolsAndNoMachineStateSetters()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaMachineryTools(McpToolProfile.Machinery);
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] all = provider.GetServices<McpServerTool>().ToArray();
            McpServerTool[] tools = all.Where(tool =>
                tool.ProtocolTool.Name.StartsWith("machinery_", StringComparison.Ordinal)).ToArray();

            Assert.That(tools, Has.Length.EqualTo(19));
            Assert.That(all.Count(tool => tool.ProtocolTool.Name == "Connect"), Is.EqualTo(1));
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Has.None.Contains("set_state"));
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Has.None.Contains("set_mode"));
            foreach (McpServerTool tool in tools)
            {
                string name = tool.ProtocolTool.Name;
                bool control = name == "machinery_zero_point_adjustment";
                bool file = name == "machinery_download_result";
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.EqualTo(!control && !file), name);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.EqualTo(control), name);
                Assert.That(tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .TryGetProperty("sessionName", out _),
                    Is.True, name);
            }
        }

        [Test]
        public void ProcessProjectionPreservesZeroMissingAndNonFiniteMeasurements()
        {
            JsonObject result = MachineryJson.ProcessValue(new MachineryProcessValue
            {
                NodeId = new NodeId("pv", 2),
                SignalNodeId = new NodeId("signal", 2),
                Value = 0,
                Setpoint = double.NaN,
                LowLimit = double.NegativeInfinity,
                HighLimit = double.PositiveInfinity,
                Status = 0
            }, ServiceMessageContext.Create(NUnitTelemetryContext.Create()))!;

            Assert.That(result["value"]!.GetValue<double>(), Is.Zero);
            Assert.That(result["setpoint"]!.GetValue<string>(), Is.EqualTo("NaN"));
            Assert.That(result["lowLimit"]!.GetValue<string>(), Is.EqualTo("-Infinity"));
            Assert.That(result["highLimit"]!.GetValue<string>(), Is.EqualTo("Infinity"));
            Assert.That(result["status"]!.GetValue<ushort>(), Is.Zero);
            Assert.That(result["lowLowLimit"], Is.Null);
            Assert.That(result["engineeringUnits"], Is.Null);
        }

        [Test]
        public void StateProjectionPreservesQualityAndDistinctStateAndTransitionIds()
        {
            var state = new FiniteStateSnapshot(
                new NodeId("state-machine", 2), new LocalizedText("en", "Executing"), new NodeId("running", 2),
                new LocalizedText("en", "Start"), new NodeId("start", 2),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), StatusCodes.Uncertain);

            JsonObject json = MachineryJson.State(state)!;

            Assert.That(json["currentStateId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=running"));
            Assert.That(json["lastTransitionId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=start"));
            Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(json["timestamp"]!.GetValue<string>(), Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
            Assert.That(json["currentState"]!["locale"]!.GetValue<string>(), Is.EqualTo("en"));
        }

        [Test]
        public async Task AbsentJobManagementDoesNotFabricateRolesAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = JobMcpTestSession.Create(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);

            CallToolResult result = await services.GetRequiredService<MachineryTools>()
                .GetJobEndpointsAsync("ns=5;s=machine", "selected").ConfigureAwait(false);
            JsonElement value = result.StructuredContent!.Value;

            Assert.That(result.IsError, Is.False);
            Assert.That(NodeId.Parse(value.GetProperty("jobManagementId").GetString()!).IsNull, Is.True);
            Assert.That(NodeId.Parse(value.GetProperty("jobOrderReceiverId").GetString()!).IsNull, Is.True);
            Assert.That(NodeId.Parse(value.GetProperty("jobResponseProviderId").GetString()!).IsNull, Is.True);
            Assert.That(NodeId.Parse(value.GetProperty("jobResponseReceiverId").GetString()!).IsNull, Is.True);
            Assert.That(value.GetProperty("definesResponseReceiver").GetBoolean(), Is.False);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void ResultFiltersKeepAllClausesAndValidateBounds()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(Ua.Machinery.Result.Namespaces.MachineryResult);
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            (ContentFilter filter, ArrayOf<RelativePath> order) = MachineryJson.ResultFilter(
                namespaces, "job-1", "part-1", start, start.AddDays(1), MachineryResultOrder.CreationTime);

            ContentFilterElement[] elements = filter.Elements.ToArray()!;
            Assert.That(elements.Count(element => element.FilterOperator == FilterOperator.Equals),
                Is.EqualTo(2));
            Assert.That(elements.Count(element => element.FilterOperator == FilterOperator.And), Is.EqualTo(3));
            Assert.That(elements.Count(element => element.FilterOperator == FilterOperator.GreaterThanOrEqual),
                Is.EqualTo(1));
            Assert.That(elements.Count(element => element.FilterOperator == FilterOperator.LessThanOrEqual),
                Is.EqualTo(1));
            Assert.That(order[0].Elements[0].TargetName.Name, Is.EqualTo("ResultMetaData"));
            Assert.That(order[0].Elements[1].TargetName.Name, Is.EqualTo("CreationTime"));
            Assert.That(() => MachineryJson.ResultFilter(
                namespaces, null, null, start.AddDays(1), start, MachineryResultOrder.CreationTime),
                Throws.ArgumentException);
        }
    }
}
#endif
