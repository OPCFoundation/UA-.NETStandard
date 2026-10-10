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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Mcp;
using Opc.Ua.Server;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises real MCP binding, generated role proxies and streamed results against one local Machinery server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineryIsa95McpLiveTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"machinery-isa95-mcp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_root);
            string endpoint = $"opc.tcp://localhost:{AvailablePort()}/machinery-isa95-mcp";
            var services = new ServiceCollection();
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.AddLogging();
            services.AddInMemoryIsa95JobControlProvider();
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "MachineryIsa95McpTests";
                options.ApplicationUri = "urn:localhost:machinery-isa95-mcp-tests";
                options.ProductUri = "urn:localhost:machinery-isa95-mcp-tests:product";
                options.PkiRoot = Path.Combine(m_root, "pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpoint);
            })
                .AddMachinery()
                .ConfigureMachinery(async (context, ct) =>
                {
                    m_machine = await context.AddMachine(new QualifiedName("Press"))
                        .WithIdentification(id =>
                        {
                            id.Manufacturer = new LocalizedText("Test");
                            id.SerialNumber = "press-1";
                            id.ProductInstanceUri = "urn:mcp-tests:press";
                        })
                        .WithMonitoring(monitoring => monitoring
                            .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                            .WithOperationMode(MachineryOperationModeValue.Setup))
                        .WithJobManagement(jobs => jobs.WithPredefinedParameters())
                        .WithResultManagement(results => results.WithInMemoryStore().WithFileTransfer())
                        .BuildAsync(ct).ConfigureAwait(false);
                    ready.SetResult(true);
                });
            m_serverServices = services.BuildServiceProvider();
            m_serverHost = m_serverServices.GetServices<IHostedService>().Single();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await m_serverHost.StartAsync(startup.Token).ConfigureAwait(false);
            await ready.Task.WaitAsync(startup.Token).ConfigureAwait(false);
            Assert.That(m_machine, Is.Not.Null);

            var clients = new ServiceCollection();
            m_options = new OpcUaMcpOptions { TransferRoot = m_root, MaxTransferBytes = 256 * 1024 };
            clients.AddOpcUaMcpCore(m_options).AddOpcUaMcpMachinery().AddOpcUaMcpIsa95();
            clients.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaMcpFilters()
                .WithOpcUaMachineryTools(McpToolProfile.Machinery)
                .WithOpcUaIsa95Tools(McpToolProfile.Isa95);
            m_clients = clients.BuildServiceProvider();
            m_sessions = m_clients.GetRequiredService<OpcUaSessionManager>();
            await m_sessions.ConnectAsync("plant", endpoint, "None", "None", "Anonymous",
                null, null, true, startup.Token).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_sessions is not null)
            {
                await m_sessions.DisconnectAsync("plant").ConfigureAwait(false);
                m_sessions.Dispose();
            }
            if (m_clients is not null)
            {
                await m_clients.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverHost is not null)
            {
                await m_serverHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_serverServices is not null)
            {
                await m_serverServices.DisposeAsync().ConfigureAwait(false);
            }
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [Test]
        public async Task MachineryEndpointsDriveTypedIsa95StoreAndRealServerRefusalAsync()
        {
            CallToolResult endpoints = await CallAsync("machinery_get_job_endpoints",
                new JsonObject { ["machineNodeId"] = m_machine!.NodeId.ToString() }).ConfigureAwait(false);
            Assert.That(endpoints.IsError, Is.False);
            JsonElement value = endpoints.StructuredContent!.Value;
            string receiver = value.GetProperty("jobOrderReceiverId").GetString()!;
            Assert.That(NodeId.Parse(receiver).IsNull, Is.False);
            Assert.That(NodeId.Parse(value.GetProperty("jobResponseProviderId").GetString()!).IsNull, Is.False);
            Assert.That(NodeId.Parse(value.GetProperty("jobResponseReceiverId").GetString()!).IsNull, Is.True);

            CallToolResult refusal = await CallAsync("isa95_v2_start", new JsonObject
            {
                ["receiverNodeId"] = receiver,
                ["jobOrderId"] = "unknown-job"
            }).ConfigureAwait(false);
            Assert.That(refusal.IsError, Is.True);
            Assert.That(refusal.StructuredContent!.Value.GetProperty("returnStatus").GetUInt64(),
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));

            CallToolResult stored = await CallAsync("isa95_v2_store", new JsonObject
            {
                ["receiverNodeId"] = receiver,
                ["jobOrder"] = JsonNode.Parse(
                    """
                    {"jobOrderId":"live-job","priority":0,"parameters":[
                      {"id":"RunsPlanned","value":{"dataType":"UInt32","value":0}},
                      {"id":"Overproduction","value":{"dataType":"Boolean","value":false}}
                    ]}
                    """)
            }).ConfigureAwait(false);
            Assert.That(stored.IsError, Is.False);
            Assert.That(stored.StructuredContent!.Value.GetProperty("returnStatus").GetUInt64(),
                Is.EqualTo(Isa95JobReturnStatus.Success));

            CallToolResult parameters = await CallAsync("machinery_read_job_parameters", new JsonObject
            {
                ["machineNodeId"] = m_machine.NodeId.ToString(),
                ["jobOrderId"] = "live-job",
                ["list"] = "Orders"
            }).ConfigureAwait(false);
            Assert.That(parameters.IsError, Is.False);
            Assert.That(parameters.StructuredContent!.Value.GetProperty("runsPlanned").GetUInt32(), Is.Zero);
            Assert.That(parameters.StructuredContent.Value.GetProperty("overproduction").GetBoolean(), Is.False);
        }

        [Test]
        public async Task ResultFiltersMetadataAndStreamingDownloadWorkThroughMcpAsync()
        {
            byte[] payload = Enumerable.Range(0, 131079).Select(index => (byte)(index % 251)).ToArray();
            await m_machine!.Results!.PublishAsync(new MachineryResult(
                new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType
                    {
                        ResultId = "live-result",
                        JobId = "result-job",
                        PartId = "part-1",
                        CreationTime = new DateTimeUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                        EncodingMask = (uint)(ResultMetaDataTypeFields.JobId |
                            ResultMetaDataTypeFields.PartId | ResultMetaDataTypeFields.CreationTime)
                    }
                }, new ByteString(payload))).ConfigureAwait(false);
            (string Id, string Job, string Part, int Year)[] excluded =
            [
                ("wrong-job", "other-job", "part-1", 2026),
                ("wrong-part", "result-job", "other-part", 2026),
                ("too-old", "result-job", "part-1", 2025),
                ("too-new", "result-job", "part-1", 2027)
            ];
            foreach ((string id, string job, string part, int year) in excluded)
            {
                await m_machine.Results.PublishAsync(new MachineryResult(new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType
                    {
                        ResultId = id,
                        JobId = job,
                        PartId = part,
                        CreationTime = new DateTimeUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                        EncodingMask = (uint)(ResultMetaDataTypeFields.JobId |
                            ResultMetaDataTypeFields.PartId | ResultMetaDataTypeFields.CreationTime)
                    }
                })).ConfigureAwait(false);
            }

            CallToolResult listed = await CallAsync("machinery_list_results", new JsonObject
            {
                ["machineNodeId"] = m_machine.NodeId.ToString(),
                ["jobId"] = "result-job",
                ["partId"] = "part-1",
                ["createdAfter"] = "2026-01-01T00:00:00Z",
                ["createdBefore"] = "2026-01-01T00:00:00Z"
            }).ConfigureAwait(false);
            Assert.That(listed.IsError, Is.False);
            Assert.That(listed.StructuredContent!.Value.GetProperty("resultIds").EnumerateArray()
                .Select(value => value.GetString()), Is.EqualTo(s_resultIds));

            CallToolResult metadata = await CallAsync("machinery_read_result", new JsonObject
            {
                ["machineNodeId"] = m_machine.NodeId.ToString(),
                ["resultId"] = "live-result"
            }).ConfigureAwait(false);
            Assert.That(metadata.IsError, Is.False);
            Assert.That(metadata.StructuredContent!.Value.GetProperty("result").GetProperty("ResultId").GetString(),
                Is.EqualTo("live-result"));

            CallToolResult download = await CallAsync("machinery_download_result", new JsonObject
            {
                ["machineNodeId"] = m_machine.NodeId.ToString(),
                ["resultId"] = "live-result",
                ["filePath"] = "result.bin"
            }).ConfigureAwait(false);
            Assert.That(download.IsError, Is.False);
            Assert.That(download.StructuredContent!.Value.GetProperty("bytesTransferred").GetInt64(),
                Is.EqualTo(payload.Length));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(m_root!, "result.bin")).ConfigureAwait(false),
                Is.EqualTo(payload));

            m_options!.MaxTransferBytes = 4;
            try
            {
                CallToolResult refused = await CallAsync("machinery_download_result", new JsonObject
                {
                    ["machineNodeId"] = m_machine.NodeId.ToString(),
                    ["resultId"] = "live-result",
                    ["filePath"] = "too-large.bin"
                }).ConfigureAwait(false);
                Assert.That(refused.IsError, Is.True);
                Assert.That(File.Exists(Path.Combine(m_root!, "too-large.bin")), Is.False);
                Assert.That(Directory.GetFiles(m_root!, ".opcua-mcp-*.tmp"), Is.Empty);
            }
            finally
            {
                m_options.MaxTransferBytes = 256 * 1024;
            }
        }

        /// <summary>
        /// The actual MCP binder must reject either timezone-less creation bound as an actionable tool error.
        /// </summary>
        [TestCase("createdAfter")]
        [TestCase("createdBefore")]
        public async Task ResultQueryRejectsTimezoneLessJsonBoundsAsync(string parameterName)
        {
            CallToolResult result = await CallAsync("machinery_list_results", new JsonObject
            {
                ["machineNodeId"] = m_machine!.NodeId.ToString(),
                [parameterName] = "2026-01-01T00:00:00"
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            JsonElement payload = result.StructuredContent!.Value;
            Assert.That(payload.GetProperty("errorType").GetString(), Is.EqualTo(nameof(ArgumentException)));
            Assert.That(payload.GetProperty("message").GetString(), Does.Contain("UTC or offset"));
            Assert.That(payload.GetProperty("message").GetString(), Does.Contain(parameterName));
        }

        private async Task<CallToolResult> CallAsync(string name, JsonObject arguments)
        {
            arguments["sessionName"] = "plant";
            using JsonDocument json = JsonDocument.Parse(arguments.ToJsonString());
            ServiceProvider services = m_clients ??
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

        private static string AvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        }

        private string? m_root;
        private ServiceProvider? m_serverServices;
        private ServiceProvider? m_clients;
        private IHostedService? m_serverHost;
        private OpcUaSessionManager? m_sessions;
        private OpcUaMcpOptions? m_options;
        private IMachineHandle<BaseObjectState>? m_machine;
        private static readonly string[] s_resultIds = ["live-result"];
    }
}
#endif
