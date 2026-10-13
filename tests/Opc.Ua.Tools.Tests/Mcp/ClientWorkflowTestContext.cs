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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Mcp;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Tools.Tests.McpCompanion.ClientWorkflows
{
    /// <summary>
    /// Exercises real workflow tools/generated proxies/MCP binding above a deterministic UA service boundary.
    /// </summary>
    internal sealed class ClientWorkflowTestContext : IAsyncDisposable
    {
        private ClientWorkflowTestContext(
            RemoteModelTestSession remote, bool enabled, ClientWorkflowOptions? workflowOptions, bool files,
            McpToolProfileSet profiles, bool certificateAdministration)
        {
            Remote = remote;
            Remote.Session.Object.ConfiguredEndpoint.Description.Server.ApplicationUri = "urn:workflow:test-server";
            Root = files
                ? Path.Combine(TestContext.CurrentContext.WorkDirectory, $"workflow-files-{Guid.NewGuid():N}") : null;
            if (Root is not null)
            {
                Directory.CreateDirectory(Root);
            }
            ArrayOf<McpCapability> capabilities = certificateAdministration
                ? [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration]
                : [McpCapability.IdentityAdministration];
            Options = new OpcUaMcpOptions
            {
                TransferRoot = Root,
                MaxTransferBytes = 8192,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = enabled ? capabilities : [],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = Owner, Capabilities = capabilities,
                            SessionNames = ["selected"], SecretAliases = ["old", "new", "foreign"]
                        },
                        new McpCallerGrant
                        {
                            Caller = Other, Capabilities = [McpCapability.IdentityAdministration],
                            SessionNames = ["selected"], SecretAliases = ["old"]
                        },
                        new McpCallerGrant
                        {
                            Caller = WrongSession, Capabilities = [McpCapability.IdentityAdministration],
                            SessionNames = ["other-session"], SecretAliases = ["old", "new"]
                        }
                    ]
                }
            };
            SecretRegistry.Setup(registry => registry.GetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier identifier, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (SecretFailure is not null)
                    {
                        throw SecretFailure;
                    }
                    var view = new WorkflowTestSecret(Encoding.UTF8.GetBytes(identifier.Name + "-test-only"));
                    Secrets.Add(view);
                    return ValueTask.FromResult<ISecret?>(view);
                });
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(Options);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IClientWorkflowStreams>(Streams);
            services.AddSingleton<IClientWorkflowSessionFactory>(SessionScopes);
            string resource = "opcua:urn:workflow:test-server#" +
                NodeId.ToExpandedNodeId(ObjectIds.UserManagement, Remote.Context.NamespaceUris);
            services.AddOpcUaMcpSecrets(SecretRegistry.Object,
                [
                    new McpSecretAlias("old", new SecretIdentifier("old", "workflow-test"), resource),
                    new McpSecretAlias("new", new SecretIdentifier("new", "workflow-test"), resource),
                    new McpSecretAlias("foreign", new SecretIdentifier("foreign", "workflow-test"), "urn:foreign")
                ]);
            services.AddOpcUaMcpClientWorkflows(workflowOptions);
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaClientWorkflowTools(profiles);
            Services = services.BuildServiceProvider();
            Manager = Services.GetRequiredService<OpcUaSessionManager>();
            SetupHistoryAndWrites();
        }

        /// <summary>
        /// Gets the baseline trusted identity.
        /// </summary>
        internal static McpCallerIdentity Owner { get; } = new("workflow-owner", "urn:test:issuer");

        /// <summary>
        /// Gets another authorized identity used to prove ownership isolation.
        /// </summary>
        internal static McpCallerIdentity Other { get; } = new("workflow-other", "urn:test:issuer");

        /// <summary>
        /// Gets an identity that lacks permission for the selected session.
        /// </summary>
        internal static McpCallerIdentity WrongSession { get; } = new("wrong-session", "urn:test:issuer");

        /// <summary>
        /// Gets the existing mock-UA transport fixture.
        /// </summary>
        internal RemoteModelTestSession Remote { get; }

        /// <summary>
        /// Gets the composed workflow host.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the single shared session registry.
        /// </summary>
        internal OpcUaSessionManager Manager { get; }

        /// <summary>
        /// Gets the host limits for file-boundary tests.
        /// </summary>
        internal OpcUaMcpOptions Options { get; }

        /// <summary>
        /// Gets the optional per-fixture transfer root.
        /// </summary>
        internal string? Root { get; }

        /// <summary>
        /// Gets a clock controlled directly by cursor tests.
        /// </summary>
        internal WorkflowTestClock Clock { get; } = new();

        /// <summary>
        /// Gets the deterministic borrowed-subscription provider.
        /// </summary>
        internal WorkflowTestStreams Streams { get; } = new();

        /// <summary>
        /// Gets test-only scopes; local integration tests use the real SDK implementation instead.
        /// </summary>
        internal ClientWorkflowTestSessionFactory SessionScopes { get; } = new();

        /// <summary>
        /// Gets the mock secret-provider boundary.
        /// </summary>
        internal Mock<ISecretRegistry> SecretRegistry { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets all allocated provider views for disposal assertions.
        /// </summary>
        internal List<WorkflowTestSecret> Secrets { get; } = [];

        /// <summary>
        /// Gets or sets an intentionally sensitive provider failure.
        /// </summary>
        internal Exception? SecretFailure { get; set; }

        /// <summary>
        /// Gets recorded Write requests.
        /// </summary>
        internal List<WriteValue> Writes { get; } = [];

        /// <summary>
        /// Gets or sets the per-attribute Write status.
        /// </summary>
        internal StatusCode WriteStatus { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Gets queued bounded historical pages.
        /// </summary>
        internal Queue<HistoryReadResult> HistoryPages { get; } = [];

        /// <summary>
        /// Gets every history query and release operation.
        /// </summary>
        internal List<WorkflowHistoryRead> HistoryReads { get; } = [];

        /// <summary>
        /// Gets every explicit historical update detail object.
        /// </summary>
        internal List<ExtensionObject> HistoryUpdates { get; } = [];

        /// <summary>
        /// Gets or sets update operation statuses in exact server order.
        /// </summary>
        internal ArrayOf<StatusCode> HistoryOperationResults { get; set; } = [StatusCodes.Good];

        /// <summary>
        /// Gets or sets the node-level HistoryUpdate status.
        /// </summary>
        internal StatusCode HistoryUpdateStatus { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Gets the real role tools.
        /// </summary>
        internal RoleWorkflowTools Roles => Services.GetRequiredService<RoleWorkflowTools>();

        /// <summary>
        /// Gets the real user tools.
        /// </summary>
        internal UserWorkflowTools Users => Services.GetRequiredService<UserWorkflowTools>();

        /// <summary>
        /// Gets the real file tools.
        /// </summary>
        internal FileWorkflowTools Files => Services.GetRequiredService<FileWorkflowTools>();

        /// <summary>
        /// Gets the real alarm tools.
        /// </summary>
        internal AlarmWorkflowTools Alarms => Services.GetRequiredService<AlarmWorkflowTools>();

        /// <summary>
        /// Gets the real historian tools.
        /// </summary>
        internal HistoryWorkflowTools History => Services.GetRequiredService<HistoryWorkflowTools>();

        /// <summary>
        /// Gets the real history registry for deterministic expiry cleanup.
        /// </summary>
        internal HistoryCursorRegistry Cursors => Services.GetRequiredService<HistoryCursorRegistry>();

        /// <summary>
        /// Creates an encrypted registered session without network traffic.
        /// </summary>
        internal static async Task<ClientWorkflowTestContext> CreateAsync(
            bool enabled = true, MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            ClientWorkflowOptions? options = null, bool files = false, McpToolProfileSet? profiles = null,
            bool certificateAdministration = false)
        {
            RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false, mode: mode).ConfigureAwait(false);
            var fixture = new ClientWorkflowTestContext(remote, enabled, options, files,
                profiles ??
                new McpToolProfileSet(
                    [
                        McpToolProfile.Roles, McpToolProfile.Users, McpToolProfile.Files,
                        McpToolProfile.Alarms, McpToolProfile.History
                    ]), certificateAdministration);
            await fixture.Manager.RegisterExistingSessionAsync("selected", remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            return fixture;
        }

        /// <summary>
        /// Establishes a trusted direct-test caller scope, never a user-controlled argument.
        /// </summary>
        internal IDisposable Enter(McpCallerIdentity? identity = null)
        {
            return Services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(identity ?? Owner);
        }

        /// <summary>
        /// Invokes the real MCP SDK binder and source-generated input converters.
        /// </summary>
        internal async Task<CallToolResult> InvokeAsync(
            string name, JsonObject arguments, CancellationToken ct = default)
        {
            if (name is not "history_next" and not "history_release")
            {
                arguments["sessionName"] = "selected";
            }
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("workflow-test") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = document.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            return await tool.InvokeAsync(request, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the actual module list filter around SDK-registered tool definitions.
        /// </summary>
        internal async Task<ListToolsResult> ListAsync()
        {
            var request = new RequestContext<ListToolsRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/list", Id = new RequestId("workflow-list") },
                new ListToolsRequestParams());
            return await ClientWorkflowFilters.Catalogue((_, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools = [.. Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)]
            }))(request, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Configures the baseline typed role and all fixed mapping methods.
        /// </summary>
        internal NodeId Role()
        {
            NodeId role = Remote.Id("role");
            Remote.Type(role, ObjectTypeIds.RoleType);
            string[] names =
            [
                BrowseNames.AddIdentity, BrowseNames.RemoveIdentity, BrowseNames.AddApplication,
                BrowseNames.RemoveApplication, BrowseNames.AddEndpoint, BrowseNames.RemoveEndpoint
            ];
            foreach (string name in names)
            {
                Remote.Child(role, new QualifiedName(name), Remote.Id(name));
            }
            return role;
        }

        /// <summary>
        /// Configures an ordinary directory/file pair with explicit child ownership.
        /// </summary>
        internal (NodeId Directory, NodeId File) FileTree()
        {
            NodeId directory = Remote.Id("directory");
            NodeId file = Remote.Id("file");
            Remote.Type(directory, ObjectTypeIds.FileDirectoryType);
            Remote.Type(file, ObjectTypeIds.FileType);
            Remote.Browse(directory, ReferenceTypeIds.HasComponent,
                [Remote.Reference(file, ObjectTypeIds.FileType, "source.bin")]);
            return (directory, file);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync().ConfigureAwait(false);
            await Remote.DisposeAsync().ConfigureAwait(false);
            if (Root is not null)
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        /// <summary>
        /// Adds only service seams not already provided by the shared model fixture.
        /// </summary>
        private void SetupHistoryAndWrites()
        {
            Remote.Session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var results = new StatusCode[values.Count];
                    for (int index = 0; index < values.Count; index++)
                    {
                        Writes.Add(values[index]);
                        results[index] = WriteStatus;
                    }
                    return ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
            Remote.Session.Setup(session => session.HistoryReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ExtensionObject details, TimestampsToReturn timestamps, bool release,
                    ArrayOf<HistoryReadValueId> nodes, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    HistoryReads.Add(new WorkflowHistoryRead(details, timestamps, release, nodes, token));
                    Assert.That(nodes.Count, Is.EqualTo(1));
                    HistoryReadResult result = release ? new HistoryReadResult { StatusCode = StatusCodes.Good } :
                        HistoryPages.Count == 0 ? new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            HistoryData = new ExtensionObject(new HistoryData { DataValues = [] })
                        } : HistoryPages.Dequeue();
                    return ValueTask.FromResult(new HistoryReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [result]
                    });
                });
            Remote.Session.Setup(session => session.HistoryUpdateAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<ExtensionObject> details, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(details.Count, Is.EqualTo(1));
                    HistoryUpdates.Add(details[0]);
                    return ValueTask.FromResult(new HistoryUpdateResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new HistoryUpdateResult
                            {
                                StatusCode = HistoryUpdateStatus, OperationResults = HistoryOperationResults
                            }
                        ]
                    });
                });
        }
    }

    /// <summary>
    /// Recorded historian service request, including the independent cleanup token.
    /// </summary>
    internal sealed record WorkflowHistoryRead(
        ExtensionObject Details, TimestampsToReturn Timestamps, bool Release,
        ArrayOf<HistoryReadValueId> Nodes, CancellationToken Token);

    /// <summary>
    /// Secret test view whose disposal is directly observable.
    /// </summary>
    internal sealed class WorkflowTestSecret(byte[] bytes) : ISecret
    {
        /// <inheritdoc/>
        public ReadOnlySpan<byte> Bytes => bytes;

        /// <summary>
        /// Gets whether the gateway disposed the provider view.
        /// </summary>
        internal bool Disposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Disposed = true;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Manual clock for exact before/at/after historical lease boundaries.
    /// </summary>
    internal sealed class WorkflowTestClock : TimeProvider
    {
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return m_now;
        }

        /// <summary>
        /// Advances the clock without sleeping.
        /// </summary>
        internal void Advance(TimeSpan elapsed)
        {
            m_now += elapsed;
        }

        private DateTimeOffset m_now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Deterministic observation source whose returned enumerator, but not borrowed stream, is owned by a tool.
    /// </summary>
    internal sealed class WorkflowTestStreams : IClientWorkflowStreams
    {
        /// <summary>
        /// Gets the borrowed stream mock, which fails if tools attempt to dispose it.
        /// </summary>
        internal Mock<IStreamingSubscription> Stream { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the ordered notifications to yield.
        /// </summary>
        internal Queue<EventNotification> Notifications { get; } = [];

        /// <summary>
        /// Gets or sets whether an exhausted sequence waits for cancellation.
        /// </summary>
        internal bool WaitAfterSequence { get; set; }

        /// <summary>
        /// Gets the number of owned enumerators disposed.
        /// </summary>
        internal int Disposals { get; private set; }

        /// <summary>
        /// Gets the last requested refresh flag.
        /// </summary>
        internal bool Refresh { get; private set; }

        /// <summary>
        /// Gets the exact requested event filter.
        /// </summary>
        internal EventFilter? Filter { get; private set; }

        /// <summary>
        /// Gets the exact requested monitored-item settings.
        /// </summary>
        internal MonitoringOptions? Options { get; private set; }

        /// <inheritdoc/>
        public IStreamingSubscription GetStreaming(Client.ISession session)
        {
            return Stream.Object;
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<EventNotification> Subscribe(
            IStreamingSubscription streaming, NodeId notifier, EventFilter filter,
            MonitoringOptions options, bool refresh, CancellationToken ct = default)
        {
            Assert.That(streaming, Is.SameAs(Stream.Object));
            Filter = filter;
            Options = options;
            Refresh = refresh;
            return EventsAsync(ct);
        }

        /// <summary>
        /// Yields notifications while proving cleanup on count, completion and cancellation exits.
        /// </summary>
        private async IAsyncEnumerable<EventNotification> EventsAsync([EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                while (Notifications.TryDequeue(out EventNotification notification))
                {
                    ct.ThrowIfCancellationRequested();
                    yield return notification;
                }
                if (WaitAfterSequence)
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                Disposals++;
            }
        }
    }
}
#endif
