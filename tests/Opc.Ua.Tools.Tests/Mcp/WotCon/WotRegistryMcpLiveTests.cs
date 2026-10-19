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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Mcp;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Drives MCP binding, encrypted UA methods and native event delivery against both hosted WoT models.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotRegistryMcpLiveTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            // Keep the transfer root off macOS's symlinked temporary-directory path.
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"wot-mcp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_root);
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(Path.Combine(m_root, "server-pki")).ConfigureAwait(false);
            var policy = new WotManagementAccessPolicy
            {
                MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                AllowAnonymous = true,
                RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
            };
            await m_server.NodeManagerLifecycle.AddAsync(new WotConnectivityNodeManagerFactory(
                new WotConnectivityServerOptions { ManagementAccess = policy }), callerContext: null)
                .ConfigureAwait(false);
            m_registry = new WotRegistryService();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = policy,
                    XRegistryEvents = new XRegistryServerOptions
                    {
                        EventsEnabled = true,
                        EventSourceUrl = "https://wot-mcp.example.test"
                    }
                }, m_registry, m_coordinator), callerContext: null).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = m_root,
                MaxTransferBytes = 256 * 1024
            }).AddOpcUaMcpWotCon().AddOpcUaMcpXRegistry();
            services.Configure<OpcUaClientOptions>(options => options.PkiRoot = Path.Combine(m_root, "client-pki"));
            var profiles = McpToolProfileSet.Parse("wot,xregistry");
            services.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaMcpFilters()
                .WithOpcUaWotConTools(profiles)
                .WithOpcUaXRegistryTools(profiles);
            m_services = services.BuildServiceProvider();
            m_sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            string endpoint = $"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}";
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await m_sessions.ConnectAsync("encrypted", endpoint, "SignAndEncrypt", "Basic256Sha256",
                "Anonymous", null, null, true, startup.Token).ConfigureAwait(false);
            await m_sessions.ConnectAsync("unencrypted", endpoint, "None", "None",
                "Anonymous", null, null, true, startup.Token).ConfigureAwait(false);
            JsonElement roots = Success(await CallAsync("wot_inspect_server", []).ConfigureAwait(false));
            Assert.That(roots.GetProperty("legacy").GetProperty("supported").GetBoolean(), Is.True);
            Assert.That(roots.GetProperty("registry").GetProperty("supported").GetBoolean(), Is.True);
            m_managementId = roots.GetProperty("legacy").GetProperty("nodeId").GetString()!;
            m_registryId = roots.GetProperty("registry").GetProperty("nodeId").GetString()!;
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_sessions is not null)
            {
                foreach (OpcUaSessionManager.SessionInfo session in m_sessions.GetAllSessions())
                {
                    await m_sessions.DisconnectAsync(session.Name).ConfigureAwait(false);
                }
                m_sessions.Dispose();
            }
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            m_coordinator?.Dispose();
            m_registry?.Dispose();
            m_server?.Dispose();
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [Test]
        public async Task LegacyToolsCreateInspectDeleteAndRefuseUnencryptedMutationAsync()
        {
            JsonElement created = Success(await CallAsync("wot_create_asset", new JsonObject
            {
                ["assetName"] = "mcp-live-asset",
                ["managementNodeId"] = m_managementId
            }).ConfigureAwait(false));
            string asset = created.GetProperty("assetNodeId").GetString()!;
            JsonElement listed = Success(await CallAsync("wot_list_assets", []).ConfigureAwait(false));
            Assert.That(listed.GetProperty("items").EnumerateArray().Select(item =>
                item.GetProperty("nodeId").GetString()), Does.Contain(asset));
            JsonElement inspected = Success(await CallAsync("wot_inspect_asset", new JsonObject
            {
                ["assetNodeId"] = asset
            }).ConfigureAwait(false));
            Assert.That(inspected.GetProperty("assetNodeId").GetString(), Is.EqualTo(asset));

            CallToolResult refused = await CallAsync("wot_delete_asset", new JsonObject
            {
                ["assetNodeId"] = asset,
                ["managementNodeId"] = m_managementId,
                ["sessionName"] = "unencrypted"
            }).ConfigureAwait(false);
            Assert.That(refused.IsError, Is.True);
            Assert.That(refused.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient.Code));
            Success(await CallAsync("wot_delete_asset", new JsonObject
            {
                ["assetNodeId"] = asset,
                ["managementNodeId"] = m_managementId
            }).ConfigureAwait(false));
            JsonElement remaining = Success(await CallAsync("wot_list_assets", []).ConfigureAwait(false));
            Assert.That(remaining.GetProperty("items").EnumerateArray().Select(item =>
                item.GetProperty("nodeId").GetString()), Does.Not.Contain(asset));
        }

        [Test]
        public async Task RegistryToolsKeepExactVersionIdentityThroughUploadDownloadAndStateAsync()
        {
            JsonElement group = Success(await CallAsync("xregistry_create_group", new JsonObject
            {
                ["registryNodeId"] = m_registryId,
                ["groupId"] = WotRegistryGroups.ThingDescriptions,
                ["ifNotExists"] = true
            }).ConfigureAwait(false));
            string groupId = group.GetProperty("groupNodeId").GetString()!;
            JsonElement groups = Success(await CallAsync("xregistry_list", new JsonObject
            {
                ["nodeId"] = m_registryId,
                ["collection"] = "Groups"
            }).ConfigureAwait(false));
            Assert.That(groups.GetProperty("items").EnumerateArray().Select(item =>
                item.GetProperty("nodeId").GetString()), Does.Contain(groupId));
            JsonElement created = Success(await CallAsync("xregistry_create_resource", new JsonObject
            {
                ["groupNodeId"] = groupId,
                ["resourceId"] = "mcp-live-resource",
                ["versionId"] = "v1"
            }).ConfigureAwait(false));
            string version = created.GetProperty("versionNodeId").GetString()!;
            Assert.That(created.GetProperty("versionId").GetString(), Is.EqualTo("v1"));
            const string Document = /*lang=json,strict*/ """
                {"@context":"https://www.w3.org/2022/wot/td/v1.1",
                 "@type":"uav:object","id":"urn:mcp-live-resource","title":"MCP live resource"}
                """;
            await File.WriteAllTextAsync(Path.Combine(m_root!, "upload.json"), Document).ConfigureAwait(false);
            JsonElement uploaded = Success(await CallAsync("xregistry_upload_document", new JsonObject
            {
                ["versionNodeId"] = version,
                ["filePath"] = "upload.json"
            }).ConfigureAwait(false));
            Assert.That(uploaded.GetProperty("versionNodeId").GetString(), Is.EqualTo(version));
            Assert.That(uploaded.GetProperty("bytesTransferred").GetInt64(),
                Is.EqualTo(Encoding.UTF8.GetByteCount(Document)));
            Success(await CallAsync("xregistry_download_document", new JsonObject
            {
                ["nodeId"] = version,
                ["filePath"] = "download.json"
            }).ConfigureAwait(false));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root!, "download.json")).ConfigureAwait(false),
                Is.EqualTo(Document));
            JsonElement metadata = Success(await CallAsync("xregistry_read", new JsonObject
            {
                ["nodeId"] = version
            }).ConfigureAwait(false));
            Assert.That(metadata.GetProperty("role").GetString(), Is.EqualTo("Version"));
            Assert.That(PropertyValue(metadata, "VersionId").GetString(), Is.EqualTo("v1"));
            JsonElement state = Success(await CallAsync("wot_read_resource_state", new JsonObject
            {
                ["nodeId"] = version
            }).ConfigureAwait(false));
            Assert.That(state.GetProperty("nodeId").GetString(), Is.EqualTo(version));
            Assert.That(state.GetProperty("properties").GetProperty("LoadState").GetProperty("supported").GetBoolean(),
                Is.True);
            JsonElement resources = Success(await CallAsync("xregistry_list", new JsonObject
            {
                ["nodeId"] = groupId,
                ["collection"] = "Resources"
            }).ConfigureAwait(false));
            Assert.That(resources.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            string resource = resources.GetProperty("items")[0].GetProperty("nodeId").GetString()!;
            Assert.That(resource, Is.Not.EqualTo(version));
            JsonElement versions = Success(await CallAsync("xregistry_list", new JsonObject
            {
                ["nodeId"] = resource,
                ["collection"] = "Versions"
            }).ConfigureAwait(false));
            Assert.That(versions.GetProperty("items").EnumerateArray().Select(item =>
                item.GetProperty("nodeId").GetString()), Is.EqualTo([version]));
        }

        [Test]
        public async Task NativeEventsCarryRegistryIdentityAndRefreshSummaryWithoutDeletingBorrowedItemsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using IAsyncEnumerator<DataValueChange> retained =
                McpCompanionTools.GetStreaming(m_sessions!.GetSessionOrThrow("encrypted"))
                .SubscribeDataChangesAsync(VariableIds.Server_ServerStatus_CurrentTime, ct: timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.That(await retained.MoveNextAsync().ConfigureAwait(false), Is.True);
            int baseline = MonitoredItemCount();
            Task<CallToolResult> registryObservation = CallAsync("xregistry_observe_events", Observation(),
                timeout.Token);
            await WaitForItemCountAsync(baseline + 1, timeout.Token).ConfigureAwait(false);
            Success(await CallAsync("xregistry_create_group", new JsonObject
            {
                ["registryNodeId"] = m_registryId,
                ["groupId"] = WotRegistryGroups.ThingModels
            }, timeout.Token).ConfigureAwait(false));
            JsonElement registryEvents = Success(await registryObservation.ConfigureAwait(false));
            Assert.That(registryEvents.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            JsonElement registryFields = registryEvents.GetProperty("items")[0].GetProperty("fields");
            Assert.That(EventField(registryFields, "SourceUrl").GetProperty("Value").GetString(),
                Is.EqualTo("https://wot-mcp.example.test"));
            Assert.That(EventField(registryFields, "Subject").GetProperty("Value").GetString(),
                Does.EndWith("/" + WotRegistryGroups.ThingModels));
            await WaitForItemCountAsync(baseline, timeout.Token).ConfigureAwait(false);

            JsonObject wotRequest = Observation();
            wotRequest["maxItems"] = 100;
            wotRequest["durationMs"] = 5000;
            Task<CallToolResult> wotObservation = CallAsync("wot_observe_events", wotRequest, timeout.Token);
            await WaitForItemCountAsync(baseline + 1, timeout.Token).ConfigureAwait(false);
            CallToolResult refresh = await CallAsync("wot_refresh", new JsonObject
            {
                ["registryNodeId"] = m_registryId,
                ["expectedGeneration"] = 0,
                ["allowUnconditional"] = true,
                ["options"] = new JsonObject
                {
                    ["allResources"] = true,
                    ["requestId"] = "mcp-live-refresh"
                }
            }, timeout.Token).ConfigureAwait(false);
            JsonElement refreshResult = refresh.StructuredContent!.Value;
            Assert.That(StatusCode.IsBad(new StatusCode(refreshResult.GetProperty("statusCodeValue").GetUInt32())),
                Is.False);
            JsonElement wotEvents = Success(await wotObservation.ConfigureAwait(false));
            JsonElement wotFields = wotEvents.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("fields"))
                .Single(fields => EventField(fields, "RequestId").TryGetProperty("Value", out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() == "mcp-live-refresh");
            Assert.That(EventField(wotFields, "RequestId").GetProperty("Value").GetString(),
                Is.EqualTo("mcp-live-refresh"));
            Assert.That(EventField(wotFields, "Generation").GetProperty("Value").GetUInt32(),
                Is.EqualTo(refreshResult.GetProperty("newGeneration").GetUInt32()));
            Assert.That(EventField(wotFields, "Summary").GetProperty("Value").GetProperty("RequestId").GetString(),
                Is.EqualTo("mcp-live-refresh"));
            await WaitForItemCountAsync(baseline, timeout.Token).ConfigureAwait(false);
            Assert.That(await retained.MoveNextAsync().ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task CancelledObservationRemovesOnlyItsOwnMonitoredItemAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            int baseline = MonitoredItemCount();
            Task<CallToolResult> observation = CallAsync("wot_observe_events", Observation(), cancellation.Token);
            await WaitForItemCountAsync(baseline + 1, timeout.Token).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
            await WaitForItemCountAsync(baseline, timeout.Token).ConfigureAwait(false);
            Assert.That(m_sessions!.GetSessionOrThrow("encrypted").Connected, Is.True);
        }

        private JsonObject Observation()
        {
            return new JsonObject
            {
                ["notifierNodeId"] = m_registryId,
                ["durationMs"] = 10000,
                ["maxItems"] = 1
            };
        }

        private int MonitoredItemCount()
        {
            NodeId sessionId = m_sessions!.GetSessionOrThrow("encrypted").SessionId;
            return m_server!.CurrentInstance.SubscriptionManager.GetSubscriptions()
                .Where(subscription => subscription.SessionId == sessionId)
                .Sum(subscription => subscription.MonitoredItemCount);
        }

        private async Task WaitForItemCountAsync(int expected, CancellationToken ct)
        {
            while (MonitoredItemCount() != expected)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        private async Task<CallToolResult> CallAsync(string name, JsonObject arguments, CancellationToken ct = default)
        {
            arguments["sessionName"] ??= "encrypted";
            using var json = JsonDocument.Parse(arguments.ToJsonString());
            ServiceProvider services = m_services ??
                throw new InvalidOperationException("The MCP host has not started.");
            McpServerTool tool = services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("live") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = json.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await tool.InvokeAsync(request, timeout.Token).ConfigureAwait(false);
        }

        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.StructuredContent?.GetRawText());
            Assert.That(result.StructuredContent.HasValue, Is.True);
            return result.StructuredContent!.Value;
        }

        private static JsonElement PropertyValue(JsonElement result, string name)
        {
            return result.GetProperty("properties").GetProperty(name).GetProperty("dataValue").GetProperty("Value");
        }

        private static JsonElement EventField(JsonElement fields, string name)
        {
            JsonProperty[] exactMatches = fields.EnumerateObject()
                .Where(field => field.Name == name)
                .ToArray();
            if (exactMatches.Length > 1)
            {
                throw new InvalidOperationException($"Multiple exact event fields match '{name}'.");
            }
            if (exactMatches.Length == 1)
            {
                return exactMatches[0].Value;
            }
            return fields.EnumerateObject().Single(field =>
                field.Name.EndsWith(":" + name, StringComparison.Ordinal)).Value;
        }

        private string? m_root;
        private string? m_managementId;
        private string? m_registryId;
        private ServerFixture<ReferenceServer>? m_fixture;
        private ReferenceServer? m_server;
        private WotRegistryService? m_registry;
        private WotMaterializationCoordinator? m_coordinator;
        private ServiceProvider? m_services;
        private OpcUaSessionManager? m_sessions;
    }
}
#endif
