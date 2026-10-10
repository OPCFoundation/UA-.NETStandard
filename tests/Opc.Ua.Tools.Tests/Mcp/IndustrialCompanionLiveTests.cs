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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di.Server;
using Opc.Ua.Mcp;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises real MCP argument binding and typed companion calls over local UA sessions.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class IndustrialCompanionLiveTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(Path.GetTempPath(), $"industrial-mcp-{Guid.NewGuid():N}");
            string endpoint = $"opc.tcp://localhost:{AvailablePort()}/industrial-mcp";
            var scaleReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pumpReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assetReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "IndustrialMcpTests";
                options.ApplicationUri = "urn:localhost:industrial-mcp-tests";
                options.ProductUri = "urn:localhost:industrial-mcp-tests:product";
                options.PkiRoot = Path.Combine(m_root, "pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            })
                .AddAssetManagement()
                .AddScales()
                .ConfigureDevicesFor<ScalesNodeManager>(async context =>
                {
                    var device = await context.CreateDeviceAsync(
                        new QualifiedName("Asset", context.Manager.InstanceNamespaceIndex)).ConfigureAwait(false);
                    device.WithIdentification(identity =>
                    {
                        identity.ProductInstanceUri = "urn:industrial-mcp:asset:1";
                        identity.SerialNumber = "asset-serial";
                    });
                    IAssetHandle asset = await device.RegisterAsAssetAsync(
                        context.GetRequiredService<IAssetManagement>(),
                        builder => builder.WithConfigurableAssetId("ASSET-1"),
                        context.CancellationToken).ConfigureAwait(false);
                    m_assetId = asset.NodeId;
                    assetReady.SetResult(true);
                })
                .ConfigureScales(async context =>
                {
                    m_scale = await context.Manager.CreateScaleAsync(
                        new QualifiedName("Scale", context.Manager.InstanceNamespaceIndex),
                        ScaleKind.Simple,
                        builder => builder
                            .WithIdentification(new ScaleIdentification
                            {
                                Manufacturer = new LocalizedText("Test"),
                                SerialNumber = "scale-serial",
                                ProductInstanceUri = "urn:industrial-mcp:scale:1"
                            })
                            .WithWeighingRange(new WeighingRangeDefinition(0, 10, 0.01, 0.01))
                            .WithWeightDetails()
                            .WithZeroAndTare()
                            .WithRegisterWeight()
                            .WithAllowedEngineeringUnits(ScaleUnits.Kilogram),
                        context.CancellationToken).ConfigureAwait(false);
                    scaleReady.SetResult(true);
                });

            m_serverServices = services.BuildServiceProvider();
            m_serverHost = m_serverServices.GetServices<IHostedService>().Single();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await m_serverHost.StartAsync(startup.Token).ConfigureAwait(false);
            await AwaitReadyAsync(m_serverHost, Task.WhenAll(scaleReady.Task, assetReady.Task), startup.Token)
                .ConfigureAwait(false);

            string pumpEndpoint = $"opc.tcp://localhost:{AvailablePort()}/pump-mcp";
            var pumpServices = new ServiceCollection();
            pumpServices.AddLogging();
            pumpServices.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "PumpMcpTests";
                options.ApplicationUri = "urn:localhost:pump-mcp-tests";
                options.ProductUri = "urn:localhost:pump-mcp-tests:product";
                options.PkiRoot = Path.Combine(m_root, "pump-pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(pumpEndpoint);
            })
                .AddPumps()
                .ConfigurePumps(async context =>
                {
                    var pump = await context.Manager.CreatePumpAsync(
                        new QualifiedName("Pump", context.Manager.InstanceNamespaceIndex),
                        context.CancellationToken).ConfigureAwait(false);
                    pump.WithNameplate(new PumpNameplate
                    {
                        NodeId = NodeId.Null,
                        SerialNumber = "pump-serial",
                        Manufacturer = new LocalizedText("Test"),
                        ProductInstanceUri = "urn:industrial-mcp:pump:1"
                    });
                    pump.Measurements.SetAnalog(Opc.Ua.Pumps.BrowseNames.MassFlow, 12.5,
                        new EUInformation { DisplayName = new LocalizedText("kg/s") });
                    pump.Signals.SetDiscrete(Opc.Ua.Pumps.BrowseNames.PumpOperation, false);
                    pumpReady.SetResult(true);
                });
            m_pumpServices = pumpServices.BuildServiceProvider();
            m_pumpHost = m_pumpServices.GetServices<IHostedService>().Single();
            await m_pumpHost.StartAsync(startup.Token).ConfigureAwait(false);
            await AwaitReadyAsync(m_pumpHost, pumpReady.Task, startup.Token).ConfigureAwait(false);

            var clients = new ServiceCollection();
            clients.AddOpcUaMcpCore();
            clients.AddOpcUaMcpAmb();
            clients.AddOpcUaMcpScales();
            clients.AddOpcUaMcpPumps();
            McpToolProfileSet profiles = McpToolProfileSet.Parse("amb,scales,pumps");
            clients.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(profiles)
                .WithOpcUaAmbTools(profiles)
                .WithOpcUaScalesTools(profiles)
                .WithOpcUaPumpsTools(profiles);
            m_clientServices = clients.BuildServiceProvider();
            m_sessions = m_clientServices.GetRequiredService<OpcUaSessionManager>();
            await m_sessions.ConnectAsync("plant", endpoint, "None", "None", "Anonymous",
                null, null, true, startup.Token).ConfigureAwait(false);
            await m_sessions.ConnectAsync("second", endpoint, "None", "None", "Anonymous",
                null, null, true, startup.Token).ConfigureAwait(false);
            await m_sessions.ConnectAsync("pumps", pumpEndpoint, "None", "None", "Anonymous",
                null, null, true, startup.Token).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_sessions != null)
            {
                foreach (OpcUaSessionManager.SessionInfo session in m_sessions.GetAllSessions())
                {
                    await m_sessions.DisconnectAsync(session.Name).ConfigureAwait(false);
                }
                m_sessions.Dispose();
            }
            if (m_clientServices != null)
            {
                await m_clientServices.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverHost != null)
            {
                await m_serverHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_serverServices != null)
            {
                await m_serverServices.DisposeAsync().ConfigureAwait(false);
            }
            if (m_pumpHost != null)
            {
                await m_pumpHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_pumpServices != null)
            {
                await m_pumpServices.DisposeAsync().ConfigureAwait(false);
            }
            if (m_root != null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Real tool binding reaches the asset model and preserves an explicit asset mutation.
        /// </summary>
        [Test]
        public async Task AmbToolsDiscoverWriteAndReadThroughMcpAsync()
        {
            CallToolResult discovery = await CallAsync("amb_discover_assets", """{"sessionName":"plant"}""")
                .ConfigureAwait(false);
            Assert.That(discovery.IsError, Is.False);
            Assert.That(discovery.StructuredContent!.Value.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("nodeId").GetString()), Does.Contain(m_assetId.ToString()));

            CallToolResult write = await CallAsync("amb_write_asset_id", JsonSerializer.Serialize(new
            {
                assetNodeId = m_assetId.ToString(),
                assetId = "ASSET-2",
                sessionName = "plant"
            })).ConfigureAwait(false);
            Assert.That(write.IsError, Is.False);

            CallToolResult read = await CallAsync("amb_read_asset", JsonSerializer.Serialize(new
            {
                assetNodeId = m_assetId.ToString(),
                facet = "Identification",
                sessionName = "second"
            })).ConfigureAwait(false);
            Assert.That(read.IsError, Is.False);
            Assert.That(read.StructuredContent!.Value.GetProperty("data").GetProperty("assetId").GetString(),
                Is.EqualTo("ASSET-2"));
        }

        /// <summary>
        /// Weight commands preserve numeric values, unit metadata and a real server refusal.
        /// </summary>
        [Test]
        public async Task ScaleToolsPreserveWeightAndServerRefusalAsync()
        {
            m_scale!.PublishLoad(1.5);
            string args = JsonSerializer.Serialize(new { nodeId = m_scale.NodeId.ToString(), sessionName = "plant" });
            CallToolResult tare = await CallAsync("scales_set_tare", args).ConfigureAwait(false);
            Assert.That(tare.IsError, Is.False);
            m_scale.PublishLoad(2.0);
            CallToolResult read = await CallAsync("scales_read", JsonSerializer.Serialize(new
            {
                nodeId = m_scale.NodeId.ToString(),
                facet = "CurrentWeight",
                sessionName = "plant"
            })).ConfigureAwait(false);
            Assert.That(read.IsError, Is.False);
            JsonElement reading = read.StructuredContent!.Value.GetProperty("value").GetProperty("reading");
            Assert.That(reading.GetProperty("gross").GetDouble(), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(reading.GetProperty("net").GetDouble(), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(reading.GetProperty("engineeringUnits").GetProperty("UnitId").GetInt32(),
                Is.EqualTo(ScaleUnits.Kilogram.UnitId));

            CallToolResult zero = await CallAsync("scales_set_zero", args).ConfigureAwait(false);
            Assert.That(zero.IsError, Is.True);
            Assert.That(zero.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        /// <summary>
        /// Dual-folder pump discovery is deduplicated and zero/false retain their JSON types.
        /// </summary>
        [Test]
        public async Task PumpsToolsDiscoverAndPreserveMeasurementsAsync()
        {
            CallToolResult discovery = await CallAsync("pumps_list_pumps", """{"sessionName":"pumps"}""")
                .ConfigureAwait(false);
            Assert.That(discovery.IsError, Is.False);
            JsonElement items = discovery.StructuredContent!.Value.GetProperty("items");
            Assert.That(items.GetArrayLength(), Is.EqualTo(1));
            string pump = items[0].GetProperty("nodeId").GetString()!;
            CallToolResult measurements = await CallAsync("pumps_read_group", JsonSerializer.Serialize(new
            {
                pumpNodeId = pump,
                facet = "Measurements",
                sessionName = "pumps"
            })).ConfigureAwait(false);
            Assert.That(measurements.IsError, Is.False);
            JsonElement flow = measurements.StructuredContent!.Value.GetProperty("values").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == Opc.Ua.Pumps.BrowseNames.MassFlow);
            Assert.That(flow.GetProperty("value").GetProperty("Value").GetDouble(), Is.EqualTo(12.5));
            Assert.That(flow.GetProperty("engineeringUnits").GetProperty("DisplayName").GetProperty("Text").GetString(),
                Is.EqualTo("kg/s"));

            CallToolResult signals = await CallAsync("pumps_read_group", JsonSerializer.Serialize(new
            {
                pumpNodeId = pump,
                facet = "Signals",
                sessionName = "pumps"
            })).ConfigureAwait(false);
            Assert.That(signals.IsError, Is.False);
            JsonElement operation = signals.StructuredContent!.Value.GetProperty("values").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == Opc.Ua.Pumps.BrowseNames.PumpOperation);
            Assert.That(operation.GetProperty("value").GetProperty("Value").GetBoolean(), Is.False);
        }

        /// <summary>
        /// Ambiguous or missing sessions cannot silently select another connection.
        /// </summary>
        [TestCase("{}")]
        [TestCase("{\"sessionName\":\"missing\"}")]
        public async Task CompanionToolsRejectAmbiguousOrMissingSessionAsync(string arguments)
        {
            CallToolResult result = await CallAsync("amb_discover_assets", arguments).ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("session").IgnoreCase);
        }

        /// <summary>
        /// Recipe predecessor arrays bind as JSON arrays and reach the server, which rejects the non-recipe target.
        /// </summary>
        [Test]
        public async Task RecipeArrayInputReachesTheTypedClientAsync()
        {
            CallToolResult result = await CallAsync("scales_add_recipe_element", JsonSerializer.Serialize(new
            {
                nodeId = m_scale!.NodeId.ToString(),
                input = new
                {
                    elementTypeNodeId = "i=58",
                    elementName = "step",
                    previousElements = new[] { m_scale.NodeId.ToString() }
                },
                sessionName = "plant"
            })).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(
                result.StructuredContent!.Value.TryGetProperty("statusCodeValue", out JsonElement status), Is.True,
                "A UA status proves the JSON array reached the typed client instead of failing argument binding.");
            Assert.That(StatusCode.IsBad(new StatusCode(status.GetUInt32())), Is.True);
        }

        /// <summary>
        /// Invokes the SDK's real tool binder, not a direct wrapper method call.
        /// </summary>
        private async Task<CallToolResult> CallAsync(string name, string arguments)
        {
            using JsonDocument json = JsonDocument.Parse(arguments);
            ServiceProvider services = m_clientServices ??
                throw new InvalidOperationException("The MCP client host is not initialized.");
            McpServerTool tool = services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("test") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = json.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await tool.InvokeAsync(request, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reserves an ephemeral port without sharing a fixed test endpoint.
        /// </summary>
        private static string AvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Waits for the configured address space while preserving startup exceptions.
        /// </summary>
        private static async Task AwaitReadyAsync(IHostedService host, Task ready, CancellationToken ct)
        {
            if (host is BackgroundService { ExecuteTask: { } execution })
            {
                Task completed = await Task.WhenAny(ready, execution).WaitAsync(ct).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                if (completed == execution && !ready.IsCompleted)
                {
                    throw new InvalidOperationException("The server stopped before configuring its companion model.");
                }
            }
            await ready.WaitAsync(ct).ConfigureAwait(false);
        }

        private string? m_root;
        private ServiceProvider? m_serverServices;
        private ServiceProvider? m_clientServices;
        private IHostedService? m_serverHost;
        private ServiceProvider? m_pumpServices;
        private IHostedService? m_pumpHost;
        private OpcUaSessionManager? m_sessions;
        private ScaleHandle? m_scale;
        private NodeId m_assetId;
    }
}
#endif
