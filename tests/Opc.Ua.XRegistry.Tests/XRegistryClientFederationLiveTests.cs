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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the native federation client against a real secured loopback session that is bound to a
    /// real registry node manager, using only public APIs.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [Category("Integration")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryClientFederationLiveTests
    {
        /// <summary>
        /// Verifies that the trusted application/registry-root target verifies against the real Resource.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceAcceptsTheTrustedResourceAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await context.Client.VerifyLogicalResourceAsync(
                    target, context.EndpointUrl, context.Token).ConfigureAwait(false);

                Assert.That(context.Session.Connected, Is.True, "Verification must not close the owner session.");
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different trusted application URI is rejected as a security failure.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsADifferentTrustedApplicationAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget(serverUri: "urn:other:application");

                await AssertStatusAsync(
                    context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).AsTask(),
                    StatusCodes.BadSecurityChecksFailed).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that an endpoint different from the authorized Session endpoint is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsADifferentEndpointAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await AssertStatusAsync(
                    context.Client.VerifyLogicalResourceAsync(
                        target, "opc.tcp://localhost:1/other", context.Token).AsTask(),
                    StatusCodes.BadSecurityChecksFailed).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different pinned registry root identity is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsADifferentRegistryRootAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget(registryIdentifier: 1234u);

                await AssertStatusAsync(
                    context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).AsTask(),
                    StatusCodes.BadSecurityChecksFailed).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a logical Xid that differs from the real Resource is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsADifferentLogicalXidAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget(xid: "/groups/schemas/resources/other");

                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).ConfigureAwait(false));
                await Task.CompletedTask.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a Resource NodeId that is not the real Resource is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsAnUnknownResourceNodeAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget(resourceIdentifier: "unknown-resource");

                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).ConfigureAwait(false));
                await Task.CompletedTask.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a closed owner Session cannot authorize a verification.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsAClosedSessionAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();
                string endpointUrl = context.EndpointUrl;
                await context.Session.CloseAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await context.Client.VerifyLogicalResourceAsync(
                        target, endpointUrl, context.Token).ConfigureAwait(false));
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a real federation proxy, whose startup admission used a real secured Session to the
        /// registry, is followed to the verified logical Resource.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceReturnsTheVerifiedLogicalResourceAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, proxyNodeId) =>
                {
                    ResourceTypeClient followed = await context.Client.FollowExternalReferenceAsync(
                        proxySession, proxyNodeId, target, context.Token).ConfigureAwait(false);

                    Assert.That(followed, Is.Not.Null);
                    Assert.That(context.Session.Connected, Is.True);
                    Assert.That(proxySession.Connected, Is.True);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different trusted application is not followed even when the proxy metadata matches.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsADifferentTrustedApplicationAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, proxyNodeId) =>
                {
                    XRegistryFederationTarget other = context.CreateTarget(serverUri: "urn:other:application");
                    await AssertStatusAsync(
                        context.Client.FollowExternalReferenceAsync(
                            proxySession, proxyNodeId, other, context.Token).AsTask(),
                        StatusCodes.BadSecurityChecksFailed).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different trusted registry root origin is not followed.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsADifferentRegistryRootAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, proxyNodeId) =>
                {
                    XRegistryFederationTarget other = context.CreateTarget(registryIdentifier: 1234u);
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await context.Client.FollowExternalReferenceAsync(
                            proxySession, proxyNodeId, other, context.Token).ConfigureAwait(false));
                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different trusted logical Resource is not followed.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsADifferentLogicalResourceAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, proxyNodeId) =>
                {
                    XRegistryFederationTarget other = context.CreateTarget(resourceIdentifier: "unknown-resource");
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await context.Client.FollowExternalReferenceAsync(
                            proxySession, proxyNodeId, other, context.Token).ConfigureAwait(false));
                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a different trusted Xid is not followed.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsADifferentLogicalXidAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, proxyNodeId) =>
                {
                    XRegistryFederationTarget other = context.CreateTarget(xid: "/groups/schemas/resources/other");
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await context.Client.FollowExternalReferenceAsync(
                            proxySession, proxyNodeId, other, context.Token).ConfigureAwait(false));
                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a non-proxy Object is not followed.
        /// </summary>
        [Test]
        public async Task FollowExternalReferenceRejectsANonProxyNodeAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();

                await WithProxyAsync(context, target, async (proxySession, _) =>
                {
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await context.Client.FollowExternalReferenceAsync(
                            proxySession, ObjectIds.ObjectsFolder, target, context.Token).ConfigureAwait(false));
                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a logical Resource whose Open method is not executable is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsANonExecutableOpenMethodAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();
                context.Resource.Open!.Executable = false;

                await AssertStatusAsync(
                    context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).AsTask(),
                    StatusCodes.BadNotSupported).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that a logical Resource whose Read method is not user executable is rejected.
        /// </summary>
        [Test]
        public async Task VerifyLogicalResourceRejectsANonUserExecutableReadMethodAsync()
        {
            await WithRegistryAsync(async context =>
            {
                XRegistryFederationTarget target = context.CreateTarget();
                context.Resource.Read!.UserExecutable = false;

                await AssertStatusAsync(
                    context.Client.VerifyLogicalResourceAsync(
                        target, context.EndpointUrl, context.Token).AsTask(),
                    StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static async Task WithProxyAsync(
            RegistryContext context,
            XRegistryFederationTarget target,
            Func<Opc.Ua.Client.ISession, NodeId, Task> action)
        {
            string proxyRoot = Path.Combine(context.Root, "proxy");
            var factory = new FederationNodeManagerFactory(new XRegistryServerOptions
            {
                PublishFederationProxy = true,
                RemoteEndpointUrl = context.EndpointUrl,
                FederationTarget = target,
                FederationProvider = context.Client
            });
            var fixture = new ServerFixture<ProxyServer>(telemetry => new ProxyServer(telemetry, factory))
            {
                AutoAccept = true
            };
            ProxyServer? server = null;
            try
            {
                server = await fixture.StartAsync(proxyRoot).ConfigureAwait(false);
                await using var connection = new ClientFixture(context.Telemetry);
                await connection.LoadClientConfigurationAsync(proxyRoot, "XRegistryFederationProxyClient")
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                try
                {
                    ushort ns = (ushort)session.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);
                    await action(session, new NodeId(XRegistryWellKnown.FederationProxyObject, ns))
                        .ConfigureAwait(false);
                }
                finally
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
            }
        }

        private static async Task AssertStatusAsync(Task action, StatusCode expected)
        {
            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await action.ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(expected.Code));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task WithRegistryAsync(Func<RegistryContext, Task> action)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string root = Path.Combine(Path.GetTempPath(), "xrf-" + Guid.NewGuid().ToString("N")[..8]);
            var factory = new RegistryNodeManagerFactory(new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            });
            var fixture = new ServerFixture<RegistryServer>(context => new RegistryServer(context, factory))
            {
                AutoAccept = true
            };
            RegistryServer? server = null;
            try
            {
                server = await fixture.StartAsync(root).ConfigureAwait(false);
                XRegistryRegistrationNodeManager manager = factory.Manager!;
                ushort registryNamespace = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    XRegistryWellKnown.XRegistryNamespaceUri);
                var registryNodeId = new NodeId(XRegistryWellKnown.RegistryObject, registryNamespace);
                CreateGroupMethodStateResult group = await manager.OnCreateGroupAsync(
                    manager.SystemContext, null!, registryNodeId, "schemas", timeout.Token).ConfigureAwait(false);
                CreateResourceMethodStateResult created = await manager.OnCreateResourceAsync(
                    manager.SystemContext, null!, group.GroupNodeId, "doc", "1", true, timeout.Token)
                    .ConfigureAwait(false);
                var resource = (ResourceState)manager.Find(created.ResourceNodeId)!;
                string resourceXid = resource.Xid!.Value;

                await using var connection = new ClientFixture(telemetry);
                await connection.LoadClientConfigurationAsync(root, "XRegistryFederationClient")
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                try
                {
                    var client = new GenericXRegistryClient(session, telemetry);
                    await action(new RegistryContext(
                        client, session, created.ResourceNodeId, resourceXid, resource, root, telemetry, timeout.Token))
                        .ConfigureAwait(false);
                }
                finally
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private sealed class RegistryContext
        {
            public RegistryContext(
                GenericXRegistryClient client,
                Opc.Ua.Client.ISession session,
                NodeId resourceNodeId,
                string resourceXid,
                ResourceState resource,
                string root,
                ITelemetryContext telemetry,
                CancellationToken token)
            {
                Resource = resource;
                Root = root;
                Telemetry = telemetry;
                Client = client;
                Session = session;
                Token = token;
                m_resourceNodeId = resourceNodeId;
                m_resourceXid = resourceXid;
            }

            public GenericXRegistryClient Client { get; }

            public ResourceState Resource { get; }

            public string Root { get; }

            public ITelemetryContext Telemetry { get; }

            public Opc.Ua.Client.ISession Session { get; }

            public CancellationToken Token { get; }

            public string EndpointUrl => Session.Endpoint.EndpointUrl!;

            public XRegistryFederationTarget CreateTarget(
                string? serverUri = null,
                uint? registryIdentifier = null,
                string? xid = null,
                string? resourceIdentifier = null)
            {
                ushort registryNamespace = (ushort)Session.NamespaceUris.GetIndex(
                    XRegistryWellKnown.XRegistryNamespaceUri);
                string applicationUri = serverUri ?? Session.Endpoint.Server.ApplicationUri!;
                var origin = new RegistryOriginDataType
                {
                    OriginUri = string.Empty,
                    ServerUri = applicationUri,
                    RegistryNodeId = NodeId.ToExpandedNodeId(
                        new NodeId(registryIdentifier ?? XRegistryWellKnown.RegistryObject, registryNamespace),
                        Session.NamespaceUris)
                };
                NodeId resourceNode = resourceIdentifier is null
                    ? m_resourceNodeId
                    : new NodeId(resourceIdentifier, m_resourceNodeId.NamespaceIndex);
                return new XRegistryFederationTarget(
                    origin,
                    applicationUri,
                    NodeId.ToExpandedNodeId(resourceNode, Session.NamespaceUris),
                    xid ?? m_resourceXid);
            }

            private readonly NodeId m_resourceNodeId;
            private readonly string m_resourceXid;
        }

        private sealed class RegistryServer : ReferenceServer
        {
            public RegistryServer(ITelemetryContext telemetry, IAsyncNodeManagerFactory factory)
                : base(telemetry)
            {
                AddNodeManager(factory);
            }
        }

        private sealed class ProxyServer : ReferenceServer
        {
            public ProxyServer(ITelemetryContext telemetry, IAsyncNodeManagerFactory factory)
                : base(telemetry)
            {
                AddNodeManager(factory);
            }
        }

        private sealed class FederationNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public FederationNodeManagerFactory(XRegistryServerOptions options)
            {
                m_options = options;
            }

            public ArrayOf<string> NamespacesUris => [XRegistryWellKnown.XRegistryNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(
                    new XRegistryFederationNodeManager(server, configuration, m_options));
            }

            private readonly XRegistryServerOptions m_options;
        }

        private sealed class RegistryNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public RegistryNodeManagerFactory(XRegistryServerOptions options)
            {
                m_options = options;
            }

            public ArrayOf<string> NamespacesUris => [XRegistryWellKnown.XRegistryNamespaceUri];

            public XRegistryRegistrationNodeManager? Manager { get; private set; }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new XRegistryRegistrationNodeManager(server, configuration, m_options);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }

            private readonly XRegistryServerOptions m_options;
        }
    }
}
