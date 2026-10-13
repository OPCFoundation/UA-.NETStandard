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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tools.Tests.McpCompanion;
using Opc.Ua.Tools.Tests.McpRuntime;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Production DI/authority/registry/SDK binder over entirely local deterministic service and transport seams.
    /// </summary>
    internal sealed class PubSubExpansionTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates one isolated host with no implicit live endpoint or global test namespace setup.
        /// </summary>
        private PubSubExpansionTestContext(
            RemoteModelTestSession remote, bool enabled, McpPubSubOptions? options,
            IMcpPubSubTransportProvider? transports, bool issuance, ISecretStore? issuanceStore,
            string? transferRoot, long maxTransferBytes, ArrayOf<McpNetworkRule> networkRules)
        {
            Remote = remote;
            remote.Session.Object.ConfiguredEndpoint.Description.Server.ApplicationUri = "urn:test:pubsub-expansion";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = transferRoot,
                MaxTransferBytes = maxTransferBytes,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = enabled ? Capabilities : [],
                    NetworkRules = networkRules.IsNull ? [] : networkRules,
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = Owner, Capabilities = Capabilities, SessionNames = [SessionName],
                            SecretAliases = ["mqtt-password"]
                        },
                        new McpCallerGrant
                        {
                            Caller = Other, Capabilities = Capabilities, SessionNames = [SessionName]
                        }
                    ]
                }
            });
            services.AddSingleton<TimeProvider>(Clock);
            var secretRegistry = new SecretRegistry(Store);
            if (issuanceStore is not null)
            {
                secretRegistry.RegisterStore(issuanceStore);
            }
            services.AddOpcUaMcpSecrets(secretRegistry,
                [new McpSecretAlias("mqtt-password", new SecretIdentifier("password", Store.StoreType),
                    "mqtts://broker.test:8883")], issuance ? issuanceStore ?? Store : null);
            services.AddSingleton(transports ?? Transports);
            services.AddOpcUaMcpPubSub(options ?? new McpPubSubOptions());
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaPubSubTools(McpToolProfile.Full);
            Services = services.BuildServiceProvider();
            Sessions = Services.GetRequiredService<OpcUaSessionManager>();
        }

        /// <summary>
        /// Gets the first caller, authorized independently from catalogue selection.
        /// </summary>
        internal static McpCallerIdentity Owner { get; } = new("pubsub-owner", "urn:test:authority");

        /// <summary>
        /// Gets a distinct authorized caller for collision and handle-isolation tests.
        /// </summary>
        internal static McpCallerIdentity Other { get; } = new("pubsub-other", "urn:test:authority");

        /// <summary>
        /// Gets the exact registered session name.
        /// </summary>
        internal const string SessionName = "pubsub-test";

        /// <summary>
        /// Gets the deterministic transport provider.
        /// </summary>
        internal PubSubExpansionTransports Transports { get; } = new();

        /// <summary>
        /// Gets the deterministic monotonic clock and timers.
        /// </summary>
        internal McpRuntimeTestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));

        /// <summary>
        /// Gets the explicitly writable test store; it is never installed by production defaults.
        /// </summary>
        internal InMemorySecretStore Store { get; } = new("PubSubTest");

        /// <summary>
        /// Gets the host services.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the real named-session registry.
        /// </summary>
        internal OpcUaSessionManager Sessions { get; }

        /// <summary>
        /// Gets the deterministic standard service seam.
        /// </summary>
        internal RemoteModelTestSession Remote { get; }

        /// <summary>
        /// Gets the production named runtime facade.
        /// </summary>
        internal McpPubSubRuntimeManager Runtimes => Services.GetRequiredService<McpPubSubRuntimeManager>();

        /// <summary>
        /// Gets the production shared runtime registry.
        /// </summary>
        internal McpRuntimeRegistry Registry => Services.GetRequiredService<McpRuntimeRegistry>();

        /// <summary>
        /// Gets the real protected reference gateway.
        /// </summary>
        internal McpSecretReferences Secrets => Services.GetRequiredService<McpSecretReferences>();

        /// <summary>
        /// Creates an encrypted, non-networked existing session fixture.
        /// </summary>
        internal static async Task<PubSubExpansionTestContext> CreateAsync(
            bool enabled = true, McpPubSubOptions? options = null,
            IMcpPubSubTransportProvider? transports = null, bool issuance = true, ISecretStore? issuanceStore = null,
            string? transferRoot = null, long maxTransferBytes = 64L * 1024 * 1024,
            ArrayOf<McpNetworkRule> networkRules = default)
        {
            RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(false, false)
                .ConfigureAwait(false);
            var context = new PubSubExpansionTestContext(
                remote, enabled, options, transports, issuance, issuanceStore,
                transferRoot, maxTransferBytes, networkRules);
            await context.Sessions.RegisterExistingSessionAsync(SessionName, remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            return context;
        }

        /// <summary>
        /// Enters a trusted caller scope, never an ownership argument.
        /// </summary>
        internal IDisposable Enter(McpCallerIdentity? identity = null)
        {
            return Services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(identity ?? Owner);
        }

        /// <summary>
        /// Invokes the unmodified SDK binder beneath the production schema wrapper.
        /// </summary>
        internal async Task<CallToolResult> InvokeAsync(string name, JsonObject arguments)
        {
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            McpServerTool tool = Services.GetServices<McpServerTool>().Single(tool => tool.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("pubsub-expansion") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = document.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            { MatchedPrimitive = tool };
            return await tool.InvokeAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the production catalogue filter over actual registered SDK tools.
        /// </summary>
        internal async Task<ListToolsResult> CatalogueAsync()
        {
            var request = new RequestContext<ListToolsRequestParams>(Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/list", Id = new RequestId("pubsub-list") },
                new ListToolsRequestParams());
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = PubSubMcpFilters.Catalogue(
                (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = [.. Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)]
                }));
            return await handler(request, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a representative narrow typed schema without I/O.
        /// </summary>
        internal static PubSubApplicationInput Input(string name = "runtime", string mode = "Publisher")
        {
            return new PubSubApplicationInput
            {
                Name = name,
                Mode = mode,
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Udp,
                    Endpoint = "opc.udp://127.0.0.1:4849",
                    NetworkInterface = "test-interface"
                },
                PublisherId = new PubSubPublisherIdInput
                {
                    Type = PubSub.Encoding.PublisherIdType.UInt16,
                    Value = Element("42")
                },
                Fields =
                [
                    new PubSubFieldInput { Name = "Count", Type = BuiltInType.Int32, InitialValue = Element("7") },
                    new PubSubFieldInput
                    {
                        Name = "Enabled", Type = BuiltInType.Boolean, InitialValue = Element("false")
                    }
                ]
            };
        }

        /// <summary>
        /// Detaches a JSON test value from the parse document.
        /// </summary>
        internal static JsonElement Element(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Produces the actual shallow tool arguments from a direct application plan.
        /// </summary>
        internal static JsonObject ApplicationArguments(PubSubApplicationInput input)
        {
            return new JsonObject
            {
                ["input"] = JsonSerializer.SerializeToNode(PubSubApplicationSettingsInput.FromApplication(input),
                    McpPubSubExpansionJsonContext.Default.PubSubApplicationSettingsInput),
                ["connection"] = JsonSerializer.SerializeToNode(input.Connection,
                    McpPubSubExpansionJsonContext.Default.PubSubConnectionInput),
                ["fields"] = JsonSerializer.SerializeToNode(input.Fields,
                    McpPubSubExpansionJsonContext.Default.PubSubFields)
            };
        }

        /// <summary>
        /// Splits raw caller JSON without supplying any omitted defaults before the real SDK binder runs.
        /// </summary>
        internal static JsonObject ApplicationArguments(JsonObject input)
        {
            var settings = (JsonObject)input.DeepClone();
            JsonNode? connection = settings["connection"]?.DeepClone();
            JsonNode? fields = settings["fields"]?.DeepClone();
            settings.Remove("connection");
            settings.Remove("fields");
            return new JsonObject { ["input"] = settings, ["connection"] = connection, ["fields"] = fields };
        }

        /// <summary>
        /// Produces the eight concrete bridge root arguments without rewriting the SDK invocation.
        /// </summary>
        internal static JsonObject BridgeArguments(PubSubBridgeInput input)
        {
            JsonObject source = ApplicationArguments(input.Source);
            JsonObject target = ApplicationArguments(input.Target);
            return new JsonObject
            {
                ["input"] = JsonSerializer.SerializeToNode(PubSubBridgeSettingsInput.FromBridge(input),
                    McpPubSubExpansionJsonContext.Default.PubSubBridgeSettingsInput),
                ["source"] = source["input"]!.DeepClone(),
                ["sourceConnection"] = source["connection"]!.DeepClone(),
                ["sourceFields"] = source["fields"]!.DeepClone(),
                ["target"] = target["input"]!.DeepClone(),
                ["targetConnection"] = target["connection"]!.DeepClone(),
                ["targetFields"] = target["fields"]!.DeepClone(),
                ["renameFields"] = JsonSerializer.SerializeToNode(input.RenameFields,
                    McpPubSubExpansionJsonContext.Default.PubSubRenames)
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync().ConfigureAwait(false);
            await Remote.DisposeAsync().ConfigureAwait(false);
        }

        private static readonly ArrayOf<McpCapability> Capabilities =
        [
            McpCapability.PubSubRuntime, McpCapability.PubSubConfiguration,
            McpCapability.PubSubBridge, McpCapability.PubSubSecurity
        ];
    }

    /// <summary>
    /// A deterministic provider for application lifecycle tests. The application and codecs remain real.
    /// </summary>
    internal sealed class PubSubExpansionTransports : IMcpPubSubTransportProvider
    {
        /// <summary>
        /// Gets how many admitted factories were requested.
        /// </summary>
        internal int Factories => Volatile.Read(ref m_factories);

        /// <summary>
        /// Gets every materialized transport, including failed starts.
        /// </summary>
        internal ConcurrentQueue<PubSubExpansionTransport> Created { get; } = new();

        /// <summary>
        /// Gets or sets an optional deterministic open gate.
        /// </summary>
        internal Task? OpenGate { get; set; }

        /// <summary>
        /// Gets or sets the exact factory request that fails before a transport is constructed.
        /// </summary>
        internal int FailFactoryAt { get; set; }

        /// <summary>
        /// Gets an actual signal that a transport attempted to open.
        /// </summary>
        internal TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc/>
        public JsonArray Capabilities()
        {
            return new JsonArray(new JsonObject { ["transport"] = "test", ["activated"] = false });
        }

        /// <inheritdoc/>
        public IPubSubTransportFactory CreateFactory(PubSubApplicationInput input, ISecretRegistry? credentials = null)
        {
            if (Interlocked.Increment(ref m_factories) == FailFactoryAt)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "Injected factory refusal.");
            }
            return new Factory(this, PubSubInputValidation.Profile(input.Connection));
        }

        /// <inheritdoc/>
        public IPubSubTransportFactory CreateLegacyFactory(string endpoint, bool publisher)
        {
            Interlocked.Increment(ref m_factories);
            return new Factory(this, Profiles.PubSubUdpUadpTransport);
        }

        private int m_factories;

        /// <summary>
        /// Materializes a new owned transport for every actual application activation.
        /// </summary>
        private sealed class Factory(PubSubExpansionTransports owner, string profile) : IPubSubTransportFactory
        {
            /// <inheritdoc/>
            public string TransportProfileUri => profile;

            /// <inheritdoc/>
            public IPubSubTransport Create(
                PubSubConnectionDataType connection, ITelemetryContext telemetry, TimeProvider timeProvider)
            {
                var transport = new PubSubExpansionTransport(profile, owner.OpenGate, owner.OpenStarted,
                    connection.ReaderGroups.Count > 0
                        ? PubSubTransportDirection.Receive : PubSubTransportDirection.Send);
                owner.Created.Enqueue(transport);
                return transport;
            }
        }
    }

    /// <summary>
    /// A strictly owned in-memory transport with observable activation, frame and cleanup boundaries.
    /// </summary>
    internal sealed class PubSubExpansionTransport(
        string profile, Task? openGate, TaskCompletionSource openStarted, PubSubTransportDirection direction)
        : IPubSubTransport
    {
        /// <inheritdoc/>
        public string TransportProfileUri => profile;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => direction;

        /// <inheritdoc/>
        public bool IsConnected { get; private set; }

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Gets the number of underlying disposal invocations.
        /// </summary>
        internal int DisposeCalls => Volatile.Read(ref m_disposals);

        /// <summary>
        /// Gets a signal from the underlying disposal boundary.
        /// </summary>
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets copied real encoded output frames.
        /// </summary>
        internal ConcurrentQueue<byte[]> Sent { get; } = new();

        /// <summary>
        /// Enqueues one real encoded frame through the application's ordinary receive loop.
        /// </summary>
        internal ValueTask InjectAsync(PubSubTransportFrame frame, CancellationToken ct = default)
        {
            return m_frames.Writer.WriteAsync(frame, ct);
        }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            openStarted.TrySetResult();
            if (openGate is not null)
            {
                await openGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            StateChanged?.Invoke(this, new PubSubTransportStateChangedEventArgs(true, StatusCodes.Good, null));
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            m_frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload, string? topic = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Enqueue(payload.ToArray());
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (PubSubTransportFrame frame in m_frames.Reader.ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return frame;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref m_disposals);
            await CloseAsync().ConfigureAwait(false);
            Disposed.TrySetResult();
        }

        private readonly Channel<PubSubTransportFrame> m_frames = Channel.CreateBounded<PubSubTransportFrame>(64);
        private int m_disposals;
    }
}
#endif
