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
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises the existing OPC UA executor and borrowed SDK generation against a real encrypted local server.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotRuntimeOpcUaLiveTests
    {
        [Test]
        public async Task LocalEncryptedServerReadLeavesNamedSessionConnectedAsync()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "wot-runtime-ua-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            var serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer? server = null;
            ServiceProvider? provider = null;
            OpcUaSessionManager? manager = null;
            try
            {
                server = await serverFixture.StartAsync(Path.Combine(root, "server-pki")).ConfigureAwait(false);
                var services = new ServiceCollection();
                services.AddOpcUaMcpCore(new OpcUaMcpOptions
                {
                    TransferRoot = root,
                    Execution = new McpExecutionOptions
                    {
                        EnabledCapabilities = [McpCapability.WotRuntime],
                        CallerGrants =
                        [
                            new McpCallerGrant
                            {
                                Caller = WotRuntimeTestContext.Owner,
                                Capabilities = [McpCapability.WotRuntime], SessionNames = ["selected"]
                            }
                        ]
                    }
                }).AddOpcUaMcpWotRuntime();
                services.Configure<OpcUaClientOptions>(options => options.PkiRoot = Path.Combine(root, "client-pki"));
                services.AddSingleton(serviceProvider =>
                {
                    OpcUaSessionManager.SessionInfo info = serviceProvider
                        .GetRequiredService<OpcUaSessionManager>().GetSessionInfoOrThrow("selected");
                    var selected = new Uri(info.Endpoint.EndpointUrl ??
                        throw new InvalidOperationException("The local server must advertise an endpoint."));
                    string resource = NodeId.ToExpandedNodeId(
                        VariableIds.Server_ServerStatus_CurrentTime, info.Session.NamespaceUris).ToString();
                    return new McpNetworkPolicy(serviceProvider.GetRequiredService<IMcpOperationPolicy>(),
                    [
                        new McpNetworkRule
                        {
                            Scheme = selected.Scheme, Host = selected.IdnHost, Port = selected.Port,
                            Networks = ["127.0.0.0/8", "::1/128"], Operations = ["Read"], Resources = [resource],
                            Capability = McpCapability.WotRuntime
                        }
                    ]);
                });
                provider = services.BuildServiceProvider();
                manager = provider.GetRequiredService<OpcUaSessionManager>();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await manager.ConnectAsync("selected", $"opc.tcp://localhost:{serverFixture.Port}",
                    "SignAndEncrypt", "Basic256Sha256", "Anonymous", null, null, true, deadline.Token)
                    .ConfigureAwait(false);
                using IDisposable caller = provider.GetRequiredService<McpCallerContext>()
                    .EnterTrustedCaller(WotRuntimeTestContext.Owner);
                OpcUaSessionManager.SessionInfo session = manager.GetSessionInfoOrThrow("selected");
                WotRuntimeTools tools = provider.GetRequiredService<WotRuntimeTools>();
                string document = new JsonObject
                {
                    ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                    ["title"] = "Encrypted server clock",
                    ["properties"] = new JsonObject
                    {
                        ["temperature"] = new JsonObject
                        {
                            ["forms"] = new JsonArray(new JsonObject
                            {
                                ["href"] = session.Endpoint.EndpointUrl,
                                ["uav:id"] = VariableIds.Server_ServerStatus_CurrentTime.ToString(),
                                ["op"] = "readproperty"
                            })
                        }
                    }
                }.ToJsonString();
                JsonObject opened = WotRuntimeTestContext.Payload(await tools.OpenAsync("clock",
                    new WotDocumentSource { Text = document },
                    WotRuntimeTestContext.Selection(document) with
                    {
                        BindingId = "opc.opcua",
                        BindingVersion = "10101"
                    }, ["readproperty"], options: new WotRuntimeOpenOptions { SessionName = "selected" },
                    ct: deadline.Token).ConfigureAwait(false));
                string handle = opened["runtime"]!["handle"]!.GetValue<string>();
                JsonObject read = WotRuntimeTestContext.Payload(
                    await tools.ReadPropertyAsync(handle, deadline.Token).ConfigureAwait(false));
                using var decoder = new JsonDecoder(read.ToJsonString(), session.Session.MessageContext);
                DataValue value = decoder.ReadDataValue("dataValue");

                Assert.That(value.StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
                Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc timestamp), Is.True);
                Assert.That(timestamp.ToDateTime(), Is.GreaterThan(DateTime.MinValue));
                _ = WotRuntimeTestContext.Payload(await tools.CloseAsync(handle, deadline.Token).ConfigureAwait(false));
                Assert.That(manager.GetSessionOrThrow("selected"), Is.SameAs(session.Session));
                Assert.That(session.Session.Connected, Is.True);
                Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            }
            finally
            {
                if (manager is not null)
                {
                    foreach (OpcUaSessionManager.SessionInfo session in manager.GetAllSessions())
                    {
                        await manager.DisconnectAsync(session.Name).ConfigureAwait(false);
                    }
                }
                if (provider is not null)
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                }
                await serverFixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
#endif
