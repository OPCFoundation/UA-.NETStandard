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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tools.Tests.McpCompanion;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Real encrypted standard PubSub server integration; server transports remain explicitly injected and inactive.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("PubSubLocal")]
    [NonParallelizable]
    public sealed class PubSubExpansionRemoteTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "pubsub-remote-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            var fakeTransport = new PubSubExpansionTransports();
            string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA7!";
            m_fixture = new ServerFixture<ReferenceServer>(telemetry =>
            {
                PubSubApplicationInput input = Input();
                var source = new McpPubSubDataSetSource(input, new McpPubSubOptions(),
                    ServiceMessageContext.Create(telemetry), TimeProvider.System);
                PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(input, source.BuildMetaData());
                configuration.Connections = [];
                m_application = new PubSubApplicationBuilder(telemetry)
                    .UseConfiguration(configuration)
                    .AddTransportFactory(fakeTransport.CreateFactory(input))
                    .AddDataSetSource(input.DataSetName, source)
                    .Build();
                var server = new ReferenceServer(telemetry);
                server.UserDatabase.CreateUser("pubsub-test-admin", Encoding.UTF8.GetBytes(password),
                    [
                        Server.Role.SecurityAdmin,
                        Server.Role.ConfigureAdmin,
                        Server.Role.AuthenticatedUser
                    ]);
                server.AddNodeManager(new PubSubNodeManagerFactory(m_application, null,
                    new PubSubServerOptions { ExposeConfigurationMethods = true }, telemetry));
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = false,
                OperationLimits = false
            };
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_root, "server-pki")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies =
                [new UserTokenPolicy(UserTokenType.Anonymous), new UserTokenPolicy(UserTokenType.UserName)];
            _ = await m_fixture.StartAsync().ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = m_root,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = [McpCapability.PubSubConfiguration],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = PubSubExpansionTestContext.Owner,
                            Capabilities = [McpCapability.PubSubConfiguration],
                            SessionNames = ["pubsub-live"]
                        }
                    ]
                }
            });
            services.AddOpcUaMcpPubSub(new McpPubSubOptions
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = "pubsub-live", NodeId = ObjectIds.PublishSubscribe.ToString(),
                        Operations =
                        [
                            "pubsub_configuration_add_connection", "pubsub_configuration_remove_connection",
                            "pubsub_configuration_export", "pubsub_configuration_import"
                        ]
                    }
                ],
                DatagramRules =
                [
                    new McpPubSubDatagramRule
                    {
                        Endpoint = Input().Connection.Endpoint,
                        NetworkInterface = Input().Connection.NetworkInterface!,
                        LocalAddress = "127.0.0.1", Operations = ["configure-publish"]
                    }
                ]
            });
            services.Configure<OpcUaClientOptions>(options => options.PkiRoot = Path.Combine(m_root, "client-pki"));
            m_services = services.BuildServiceProvider();
            OpcUaSessionManager sessions = m_services.GetRequiredService<OpcUaSessionManager>();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _ = await sessions.ConnectAsync("pubsub-live", $"opc.tcp://localhost:{m_fixture.Port}",
                "SignAndEncrypt", "Basic256Sha256", "Username", "pubsub-test-admin", password, true, startup.Token)
                .ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (m_application is not null)
            {
                await m_application.DisposeAsync().ConfigureAwait(false);
            }
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [Test]
        public async Task RemoteConfigurationUsesGeneratedProxiesForAddExportImportAndRemoveAsync()
        {
            using IDisposable owner = m_services.GetRequiredService<McpCallerContext>()
                .EnterTrustedCaller(PubSubExpansionTestContext.Owner);
            PubSubConfigurationTools tools = m_services.GetRequiredService<PubSubConfigurationTools>();
            OpcUaSessionManager.SessionInfo info = m_services.GetRequiredService<OpcUaSessionManager>()
                .GetSessionInfoOrThrow("pubsub-live");
            Assert.That(info.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            var standard = new PublishSubscribeTypeClient(
                info.Session, ObjectIds.PublishSubscribe, info.Session.MessageContext.Telemetry);
            PubSubConfigurationTypeClient? file = await standard.GetPubSubConfigurationAsync(
                info.Session.MessageContext.Telemetry).ConfigureAwait(false);
            Assert.That(file, Is.Not.Null, "The standard namespace-zero configuration file must be exposed.");
            Assert.That(file!.ObjectId, Is.EqualTo(ObjectIds.PublishSubscribe_PubSubConfiguration));

            ReadResponse writable = await info.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.PublishSubscribe_PubSubConfiguration_Writable,
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = VariableIds.PublishSubscribe_PubSubConfiguration_UserWritable,
                        AttributeId = Attributes.Value
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(writable.Results, Has.Count.EqualTo(2));
            foreach (DataValue value in writable.Results)
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.True);
            }

            CallResponse invalid = await info.Session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.PublishSubscribe,
                        MethodId = ExpandedNodeId.ToNodeId(
                            MethodIds.PublishSubscribeType_AddConnection, info.Session.NamespaceUris),
                        InputArguments = []
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(invalid.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
            Assert.That(m_application.GetConfiguration().Connections, Is.Empty);

            CallToolResult added = await tools.AddConnectionAsync(Input(), "pubsub-live").ConfigureAwait(false);
            Assert.That(added.IsError, Is.False, RemoteModelTestSession.Payload(added).GetRawText());
            string node = RemoteModelTestSession.Payload(added).GetProperty("nodeId").GetString()!;
            PubSubConnectionDataType actual = m_application.GetConfiguration().Connections.ToList().Single();
            Assert.That(actual.Name, Is.EqualTo("RemoteConnection"));
            Assert.That(actual.PublisherId.TryGetValue(out ushort publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(42));

            CallToolResult exported = await tools.ExportAsync("configuration.xml", "pubsub-live").ConfigureAwait(false);
            Assert.That(exported.IsError, Is.False, RemoteModelTestSession.Payload(exported).GetRawText());
            JsonElement artifact = RemoteModelTestSession.Payload(exported);
            Assert.That(artifact.GetProperty("credentialsIncluded").GetBoolean(), Is.False);
            Assert.That(artifact.GetProperty("redactedItems").GetInt32(), Is.Zero);
            Assert.That(new FileInfo(Path.Combine(m_root, "configuration.xml")).Length, Is.GreaterThan(0));

            CallToolResult imported = await tools.ImportAsync("configuration.xml", true, "pubsub-live")
                .ConfigureAwait(false);
            Assert.That(imported.IsError, Is.False, RemoteModelTestSession.Payload(imported).GetRawText());
            Assert.That(RemoteModelTestSession.Payload(imported)
                .GetProperty("serverReportedChangesApplied").GetBoolean(),
                Is.True);
            Assert.That(m_application.GetConfiguration().Connections.ToList().Single().Name,
                Is.EqualTo("RemoteConnection"));

            CallToolResult removed = await tools.RemoveConnectionAsync(node, "pubsub-live").ConfigureAwait(false);
            Assert.That(removed.IsError, Is.False, RemoteModelTestSession.Payload(removed).GetRawText());
            Assert.That(m_application.GetConfiguration().Connections, Is.Empty);
        }

        private static PubSubApplicationInput Input()
        {
            return PubSubExpansionTestContext.Input("RemoteConnection") with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Udp,
                    Endpoint = "opc.udp://127.0.0.1:64999",
                    NetworkInterface = "explicit-remote-interface"
                }
            };
        }

        private string m_root = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private IPubSubApplication m_application = null!;
        private ServiceProvider m_services = null!;
    }
}
#endif
