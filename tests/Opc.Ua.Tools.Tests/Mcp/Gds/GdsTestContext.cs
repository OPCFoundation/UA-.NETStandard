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
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Uses real GDS tools, generated model clients, caller policy and the existing deterministic UA service seam.
    /// </summary>
    internal sealed class GdsTestContext : IAsyncDisposable
    {
        private GdsTestContext(
            RemoteModelTestSession remote, bool enabled, bool writable, int maxReferences, bool files,
            bool callerCreated)
        {
            Remote = remote;
            m_callerCreated = callerCreated;
            Remote.Context.NamespaceUris.GetIndexOrAppend("urn:test:unrelated-before-gds");
            Remote.Context.NamespaceUris.GetIndexOrAppend(Gds.Namespaces.OpcUaGds);
            Remote.Context.NamespaceUris.GetIndexOrAppend(Onboarding.Namespaces.OpcUaOnboarding);
            Remote.Session.Object.ConfiguredEndpoint.Description.Server.ApplicationUri = "urn:test:gds-server";
            Root = files ? Path.Combine(TestContextWorkDirectory(), $"gds-mcp-{Guid.NewGuid():N}") : null;
            if (Root is not null)
            {
                Directory.CreateDirectory(Root);
            }
            Options = new OpcUaMcpOptions
            {
                TransferRoot = Root,
                MaxTransferBytes = 1024 * 1024,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = enabled ? s_capabilities : [],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = Owner, Capabilities = s_capabilities,
                            SessionNames = ["selected", "replacement"],
                            SecretAliases = ["ticket-a", "ticket-b", "password", "proof", "auth-data", "nonce"]
                        },
                        new McpCallerGrant
                        {
                            Caller = Other, Capabilities = s_capabilities,
                            SessionNames = ["selected"], SecretAliases = ["ticket-a"]
                        }
                    ]
                }
            };
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(Logs));
            services.AddOpcUaMcpCore(Options);
            services.AddSingleton<TimeProvider>(Clock);
            if (callerCreated)
            {
                services.AddSingleton<IMcpSessionConnector>(new TestSessionConnector(this));
            }
            var registry = new SecretRegistry(Store);
            var registrar = new SecretIdentifier("ticket", Store.StoreType);
            services.AddOpcUaMcpSecrets(registry,
                [
                    new McpSecretAlias("ticket-a", registrar, RegistrarResource),
                    new McpSecretAlias(
                        "ticket-b", new SecretIdentifier("ticket-b", Store.StoreType), RegistrarResource),
                    new McpSecretAlias(
                        "password", new SecretIdentifier("password", Store.StoreType), DirectoryResource),
                    new McpSecretAlias("proof", new SecretIdentifier("proof", Store.StoreType), "urn:resource"),
                    new McpSecretAlias("auth-data", new SecretIdentifier("auth-data", Store.StoreType),
                        AuthorizationResource),
                    new McpSecretAlias("nonce", new SecretIdentifier("nonce", Store.StoreType),
                        ConfigurationResource)
                ], writable ? Store : null, maxIssuedReferences: maxReferences);
            services.AddOpcUaMcpGds();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaGdsTools(McpToolProfile.Full);
            Services = services.BuildServiceProvider();
            Manager = Services.GetRequiredService<OpcUaSessionManager>();
            Remote.Type(Remote.Id("directory"), Gds.ObjectTypeIds.CertificateDirectoryType);
            Subtype(Remote.Model(Gds.ObjectTypeIds.CertificateDirectoryType), Gds.ObjectTypeIds.DirectoryType);
            Remote.Type(Remote.Id("key-service"), Gds.ObjectTypeIds.KeyCredentialServiceType);
            Remote.Property(Remote.Id("key-service"),
                Remote.Name(Gds.Namespaces.OpcUaGds, "ResourceUri"), Variant.From("urn:resource"));
            Remote.Type(Remote.Id("auth-service"), Gds.ObjectTypeIds.AuthorizationServiceType);
            Remote.Type(Remote.Id("configuration"), ObjectTypeIds.ServerConfigurationType);
            Remote.Type(Remote.Id("registrar"), Onboarding.ObjectTypeIds.DeviceRegistrarAdminType);
        }

        /// <summary>
        /// Gets the authorized baseline caller.
        /// </summary>
        internal static McpCallerIdentity Owner { get; } = new("gds-owner", "urn:test:identity");

        /// <summary>
        /// Gets a distinct, also-authorized caller for ownership tests.
        /// </summary>
        internal static McpCallerIdentity Other { get; } = new("gds-other", "urn:test:identity");

        /// <summary>
        /// Gets the actual address-space service seam.
        /// </summary>
        internal RemoteModelTestSession Remote { get; }

        /// <summary>
        /// Gets the isolated dependency-injection host.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the tools' only session registry.
        /// </summary>
        internal OpcUaSessionManager Manager { get; }

        /// <summary>
        /// Gets the real managed/generation-bound dispatch fixture beneath the tools.
        /// </summary>
        internal GdsManagedSession Connection { get; private set; } = null!;

        /// <summary>
        /// Gets the host's initial configuration.
        /// </summary>
        internal OpcUaMcpOptions Options { get; }

        /// <summary>
        /// Gets the writable test provider.
        /// </summary>
        internal GdsTestSecretStore Store { get; } = new();

        /// <summary>
        /// Gets recorded logging values without retaining exception objects.
        /// </summary>
        internal GdsTestLogProvider Logs { get; } = new();

        /// <summary>
        /// Gets the deterministic expiry clock.
        /// </summary>
        internal GdsTestClock Clock { get; } = new();

        /// <summary>
        /// Gets the protected reference gateway under test.
        /// </summary>
        internal McpSecretReferences Secrets => Services.GetRequiredService<McpSecretReferences>();

        /// <summary>
        /// Gets the public transfer root, when requested by a file test.
        /// </summary>
        internal string? Root { get; }

        /// <summary>
        /// Gets the exact registrar resource binding used by configured aliases.
        /// </summary>
        internal string RegistrarResource => "opcua:urn:test:gds-server#" +
            NodeId.ToExpandedNodeId(Remote.Id("registrar"), Remote.Context.NamespaceUris);

        /// <summary>
        /// Gets the directory-root binding used for password aliases.
        /// </summary>
        internal string DirectoryResource => "opcua:urn:test:gds-server#" +
            NodeId.ToExpandedNodeId(Remote.Id("directory"), Remote.Context.NamespaceUris);

        /// <summary>
        /// Gets the public authorization-service label used by a legacy configured input alias.
        /// </summary>
        internal string AuthorizationResource => "opcua:urn:test:gds-server#" +
            NodeId.ToExpandedNodeId(Remote.Id("auth-service"), Remote.Context.NamespaceUris);

        /// <summary>
        /// Gets the public push-configuration label used by a legacy nonce alias.
        /// </summary>
        internal string ConfigurationResource => "opcua:urn:test:gds-server#" +
            NodeId.ToExpandedNodeId(Remote.Id("configuration"), Remote.Context.NamespaceUris);

        /// <summary>
        /// Creates one registered encrypted session without network traffic.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The managed test endpoint has no URL.
        /// </exception>
        internal static async Task<GdsTestContext> CreateAsync(
            bool enabled = true, bool writable = true, int maxReferences = 128, bool files = false,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            bool callerCreated = false, bool autoAcceptCertificates = false)
        {
            RemoteModelTestSession remote = await RemoteModelTestSession.CreateAsync(
                includeWot: false, includeRegistry: false, mode: mode).ConfigureAwait(false);
            var fixture = new GdsTestContext(remote, enabled, writable, maxReferences, files, callerCreated);
            fixture.Connection = await GdsManagedSession.CreateAsync(remote, fixture.Clock).ConfigureAwait(false);
            if (callerCreated)
            {
                using IDisposable caller = fixture.Enter();
                _ = await fixture.Manager.ConnectAsync(
                    "selected", fixture.Connection.Managed.ConfiguredEndpoint.Description.EndpointUrl ??
                        throw new InvalidOperationException("The managed test endpoint has no URL."),
                    "SignAndEncrypt", SecurityPolicies.Basic256Sha256, "Anonymous", null, null,
                    autoAcceptCertificates, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await fixture.Manager.RegisterExistingSessionAsync("selected", fixture.Connection.Managed, "Anonymous")
                    .ConfigureAwait(false);
            }
            return fixture;
        }

        /// <summary>
        /// Enters a real trusted caller scope, never a tool argument claiming ownership.
        /// </summary>
        internal IDisposable Enter(McpCallerIdentity? owner = null)
        {
            return Services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(owner ?? Owner);
        }

        /// <summary>
        /// Advertises a subtype at the transport model so the real bound Session's NodeCache can resolve it.
        /// </summary>
        internal void Subtype(NodeId actual, ExpandedNodeId parent)
        {
            Remote.Subtype(actual, parent);
            Remote.Values[(actual, Attributes.NodeClass)] = new DataValue((int)NodeClass.ObjectType);
            Remote.Values[(actual, Attributes.IsAbstract)] = new DataValue(Variant.From(false));
            NodeId parentId = Remote.Model(parent);
            Remote.Values[(parentId, Attributes.NodeClass)] = new DataValue((int)NodeClass.ObjectType);
            Remote.Values[(parentId, Attributes.IsAbstract)] = new DataValue(Variant.From(false));
            Remote.Browse(actual, ReferenceTypeIds.HasSubtype,
                [new ReferenceDescription
                {
                    NodeId = parentId, NodeClass = NodeClass.ObjectType,
                    BrowseName = new QualifiedName("Supertype"), IsForward = false
                }], BrowseDirection.Inverse);
            Remote.Browse(actual, NodeId.Null,
                [new ReferenceDescription
                {
                    NodeId = parentId, NodeClass = NodeClass.ObjectType, ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    BrowseName = new QualifiedName("Supertype"), IsForward = false
                }], BrowseDirection.Both);
        }

        /// <summary>
        /// Invokes the original SDK binder, typed DTO converters and tool method.
        /// </summary>
        internal async Task<CallToolResult> InvokeAsync(
            string name, JsonObject arguments, CancellationToken ct = default)
        {
            arguments["sessionName"] = "selected";
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(value => value.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("gds-test") },
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
        /// Reads the real list filter with generated SDK metadata and host capability policy.
        /// </summary>
        internal async Task<ListToolsResult> ListAsync()
        {
            var request = new RequestContext<ListToolsRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/list", Id = new RequestId("gds-list") },
                new ListToolsRequestParams());
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = GdsMcpFilters.Catalogue(
                (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = [.. Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)]
                }));
            return await handler(request, CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Store.SetFailure = null;
            Store.RemoveFailure = null;
            Store.GetFailure = null;
            if (m_callerCreated)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            await Services.DisposeAsync().ConfigureAwait(false);
            if (!m_callerCreated)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            await Remote.DisposeAsync().ConfigureAwait(false);
            if (Root is not null)
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        /// <summary>
        /// Uses the runner's configured artifact directory, not an OS temporary directory.
        /// </summary>
        private static string TestContextWorkDirectory()
        {
            return NUnit.Framework.TestContext.CurrentContext.WorkDirectory;
        }

        /// <summary>
        /// Returns the test-owned real managed session after Core Connect has checked caller registration authority.
        /// No discovery, socket, certificate store or private session-state bypass is involved.
        /// </summary>
        private sealed class TestSessionConnector(GdsTestContext context) : IMcpSessionConnector
        {
            /// <inheritdoc/>
            public ValueTask<ISession> ConnectAsync(
                string name, string endpointUrl, string? securityMode, string? securityPolicy, string authType,
                string? username, string? password, bool autoAcceptCertificates, string? certificateIdentity,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult<ISession>(context.Connection.Managed);
            }
        }

        /// <summary>
        /// Whether Core Connect owns the managed test session rather than borrowing a host registration.
        /// </summary>
        private readonly bool m_callerCreated;

        private static readonly ArrayOf<McpCapability> s_capabilities =
        [
            McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration,
            McpCapability.CredentialIssuance, McpCapability.Onboarding
        ];
    }

    /// <summary>
    /// A provider with observable commit-then-fail and lookup/removal race seams.
    /// </summary>
    internal sealed class GdsTestSecretStore : ISecretStore
    {
        /// <inheritdoc/>
        public string StoreType => "GdsTest";

        /// <summary>
        /// Gets or sets a failure raised after Set has committed.
        /// </summary>
        internal Exception? SetFailure { get; set; }

        /// <summary>
        /// Gets or sets a cleanup failure before removal.
        /// </summary>
        internal Exception? RemoveFailure { get; set; }

        /// <summary>
        /// Gets or sets a lookup failure.
        /// </summary>
        internal Exception? GetFailure { get; set; }

        /// <summary>
        /// Gets or sets a deterministic asynchronous gate after a write.
        /// </summary>
        internal Func<ValueTask>? AfterSet { get; set; }

        /// <summary>
        /// Gets or sets a deterministic asynchronous gate after lookup captured a provider view.
        /// </summary>
        internal Func<ValueTask>? AfterGet { get; set; }

        /// <summary>
        /// Gets stored test bytes by the provider's exact routing key.
        /// </summary>
        internal Dictionary<SecretIdentifier, byte[]> Values { get; } = [];

        /// <summary>
        /// Gets every key submitted for removal.
        /// </summary>
        internal List<SecretIdentifier> Removed { get; } = [];

        /// <summary>
        /// Gets the write tokens, allowing post-commit cancellation independence assertions.
        /// </summary>
        internal List<CancellationToken> SetTokens { get; } = [];

        /// <summary>
        /// Gets the materialized provider views for disposal assertions.
        /// </summary>
        internal List<GdsTestSecret> Views { get; } = [];

        /// <inheritdoc/>
        public ISecret? TryGet(SecretIdentifier id)
        {
            if (!Values.TryGetValue(id, out byte[]? bytes))
            {
                return null;
            }
            var secret = new GdsTestSecret([.. bytes]);
            Views.Add(secret);
            return secret;
        }

        /// <inheritdoc/>
        public async ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (GetFailure is not null)
            {
                throw GetFailure;
            }
            ISecret? secret = TryGet(id);
            if (AfterGet is not null)
            {
                await AfterGet().ConfigureAwait(false);
            }
            return secret;
        }

        /// <inheritdoc/>
        public async ValueTask SetAsync(
            SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Values[id] = bytes.ToArray();
            SetTokens.Add(ct);
            if (AfterSet is not null)
            {
                await AfterSet().ConfigureAwait(false);
            }
            if (SetFailure is not null)
            {
                throw SetFailure;
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Removed.Add(id);
            if (RemoveFailure is not null)
            {
                throw RemoveFailure;
            }
            return ValueTask.FromResult(Values.Remove(id));
        }

        /// <summary>
        /// Seeds a host-authored alias value without going through the issuance API.
        /// </summary>
        internal void Seed(string name, string text)
        {
            Values[new SecretIdentifier(name, StoreType)] = Encoding.UTF8.GetBytes(text);
        }
    }

    /// <summary>
    /// Records deterministic cleanup of a provider's per-call view.
    /// </summary>
    internal sealed class GdsTestSecret(byte[] bytes) : ISecret
    {
        /// <inheritdoc/>
        public ReadOnlySpan<byte> Bytes => bytes;

        /// <summary>
        /// Gets whether the provider view was released.
        /// </summary>
        internal bool Disposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Disposed = true;
        }
    }

    /// <summary>
    /// Manually advances UTC for deterministic expiry boundaries; no background maintenance timer is installed.
    /// </summary>
    internal sealed class GdsTestClock : TimeProvider
    {
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return m_now;
        }

        /// <summary>
        /// Advances the local validity clock explicitly.
        /// </summary>
        internal void Advance(TimeSpan elapsed)
        {
            m_now += elapsed;
        }

        private DateTimeOffset m_now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    }
}
#endif
