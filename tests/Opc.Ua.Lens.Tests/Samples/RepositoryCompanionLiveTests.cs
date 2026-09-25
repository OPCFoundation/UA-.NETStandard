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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Generators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.OpenUsd;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.XRegistry;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using ISession = Opc.Ua.Client.ISession;
using Wot = Opc.Ua.WotCon;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        [Test]
        [Explicit("Hosts a private secure WoT registry and exercises actual desktop lifecycle clients.")]
        [Category("RepositorySampleProbe")]
        public Task PrivateRegistryPreservesImmutableVersionsEpochsAndDeletionScope()
        {
            return WithPrivateCompanionServerAsync(false, async (context, _, _, token) =>
            {
                var provider = new WotCompanionProvider();
                ArrayOf<CompanionTarget> discovered = await provider.DiscoverAsync(context, token)
                    .ConfigureAwait(false);
                CompanionTarget registry = discovered.ToList().Single(target => target.TypeName == "WoT registry");
                var registryClient = new WotRegistryClient(context.Session, registry.NodeId, context.Telemetry);
                (NodeId seededGroup, bool created) = await registryClient.Proxy.GetOrCreateGroupAsync(
                    WotRegistryClient.ThingModelsGroupId, token).ConfigureAwait(false);
                Assert.That(seededGroup.IsNull, Is.False);
                Assert.That(created, Is.True);
                const string identifier = "owned-model";
                const string document = """
                    {"@context":"https://www.w3.org/2022/wot/td/v1.1","@type":"tm:ThingModel",
                     "title":"Owned model","properties":{"temperature":{"type":"number","readOnly":true}}}
                    """;
                NodeId firstVersion = NodeId.Null;
                NodeId secondVersion = NodeId.Null;
                foreach (string version in s_ownedVersions)
                {
                    uint epoch = await ReadUnsignedAsync(
                        context, registry.NodeId, XRegistryWellKnown.XRegistryNamespaceUri, "Epoch", token)
                        .ConfigureAwait(false);
                    CompanionOperationResult result = await RunFixtureTaskAsync(provider, context, registry,
                        "register-version",
                        [
                            new("epoch", Variant.From(epoch)),
                            new("group", Variant.From(WotRegistryClient.ThingModelsGroupId)),
                            new("id", Variant.From(identifier)),
                            new("version", Variant.From(version)),
                            new("document", Variant.From(document)),
                            new("acceptAutoRefresh", Variant.From(false))
                        ], token).ConfigureAwait(false);
                    Assert.That(Value(result, "Version ID").TryGetValue(out string? returnedVersion), Is.True);
                    Assert.That(returnedVersion, Is.EqualTo(version));
                    Assert.That(Value(result, "Version NodeId").TryGetValue(out NodeId node), Is.True);
                    if (version == "v1")
                    {
                        firstVersion = node;
                    }
                    else
                    {
                        secondVersion = node;
                    }
                }
                Assert.That(firstVersion.IsNull, Is.False);
                Assert.That(secondVersion.IsNull, Is.False);
                Assert.That(secondVersion, Is.Not.EqualTo(firstVersion));
                NodeId group = await IndustrialCompanionAccess.ResolveChildAsync(context, registry.NodeId,
                    Wot.Namespaces.WotCon, WotRegistryClient.ThingModelsGroupId, false, token).ConfigureAwait(false);
                NodeId resource = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, group, Wot.Namespaces.WotCon, identifier, false, token).ConfigureAwait(false);
                var target = new CompanionTarget("wot", resource, identifier, "Thing Model");
                uint metaEpoch = await ReadUnsignedAsync(
                    context, resource, XRegistryWellKnown.XRegistryNamespaceUri, "MetaEpoch", token)
                    .ConfigureAwait(false);
                CompanionTaskInput stale = await provider.PrepareInputAsync(context, target, "set-enabled",
                    [new("epoch", Variant.From(metaEpoch)), new("enabled", Variant.From(true))], token)
                    .ConfigureAwait(false);
                await RunFixtureTaskAsync(provider, context, target, "set-enabled",
                    [new("epoch", Variant.From(metaEpoch)), new("enabled", Variant.From(false))], token)
                    .ConfigureAwait(false);
                await Assert.ThatAsync(() => provider.ExecutePreparedAsync(context, target, "set-enabled",
                    stale, null, token).AsTask(), Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                ArrayOf<CompanionValue> disabled = await IndustrialCompanionAccess.ReadPropertiesAsync(
                    context, resource, Wot.Namespaces.WotCon, ["Enabled"], token).ConfigureAwait(false);
                Assert.That(disabled[0].Value.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.False);

                var versions = new[]
                {
                    new CompanionTarget("wot", firstVersion, "v1", "Thing Model"),
                    new CompanionTarget("wot", secondVersion, "v2", "Thing Model")
                };
                ByteString expectedDigest = ByteString.From(SHA256.HashData(Encoding.UTF8.GetBytes(document)));
                foreach (CompanionTarget version in versions)
                {
                    ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                        context, version.NodeId, Wot.Namespaces.WotCon, ["ContentDigest"], token).ConfigureAwait(false);
                    Assert.That(fields[0].Value.TryGetValue(out ByteString digest), Is.True);
                    Assert.That(digest, Is.EqualTo(expectedDigest));
                }

                metaEpoch = await ReadUnsignedAsync(
                    context, resource, XRegistryWellKnown.XRegistryNamespaceUri, "MetaEpoch", token)
                    .ConfigureAwait(false);
                await RunFixtureTaskAsync(provider, context, target, "select-default",
                    [new("epoch", Variant.From(metaEpoch)), new("version", Variant.From("v2"))], token)
                    .ConfigureAwait(false);
                uint firstEpoch = await ReadUnsignedAsync(
                    context, firstVersion, XRegistryWellKnown.XRegistryNamespaceUri, "Epoch", token)
                    .ConfigureAwait(false);
                await RunFixtureTaskAsync(new RegistryCompanionProvider(), context,
                    new CompanionTarget("xregistry", firstVersion, "v1", "Registry resource/version"),
                    "delete-resource",
                    [new("epoch", Variant.From(firstEpoch)), new("includeChildren", Variant.From(true))], token)
                    .ConfigureAwait(false);
                ReadResponse readback = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = firstVersion, AttributeId = Attributes.NodeClass },
                        new ReadValueId { NodeId = secondVersion, AttributeId = Attributes.NodeClass },
                        new ReadValueId { NodeId = resource, AttributeId = Attributes.NodeClass }
                    ], token).ConfigureAwait(false);
                Assert.That(readback.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(readback.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(readback.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        [Explicit("Hosts the private managed generator sample and exercises bounded desktop telemetry capture.")]
        [Category("RepositorySampleProbe")]
        public Task PrivateGeneratorBindingsRetainSourceAndPublishEvidence()
        {
            return WithPrivateCompanionServerAsync(true, async (context, server, _, token) =>
            {
                var provider = new OpenUsdCompanionProvider();
                ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(context, token).ConfigureAwait(false);
                Assert.That(targets.IsEmpty, Is.False);
                CompanionTarget? selected = null;
                for (int index = 0; index < targets.Count; index++)
                {
                    CompanionTarget target = targets[index];
                    CompanionInspection inspection = await provider.InspectAsync(context, target, token)
                        .ConfigureAwait(false);
                    if (inspection.Values.ToList().Single(value => value.Name == "Binding count").Value
                        .TryGetValue(out int bindings) &&
                        bindings > 0)
                    {
                        selected = target;
                        break;
                    }
                }
                Assert.That(selected, Is.Not.Null,
                    string.Join(", ", targets.ToList().Select(target => target.DisplayName)));
                await Assert.ThatAsync(() => RunFixtureTaskAsync(
                    provider, context, selected!, "read-bindings", [], token),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadTypeMismatch)).ConfigureAwait(false);
                OpenUsdRepresentationState representation =
                    server.FindNodeManagers<GeneratorNodeManager>().Single()
                        .FindPredefinedNode<OpenUsdRepresentationState>(selected!.NodeId) ??
                    throw new AssertionException("The owned representation is missing.");
                var children = new System.Collections.Generic.List<BaseInstanceState>();
                representation.GetChildren(server.DefaultSystemContext, children);
                OpenUsdLiveBindingState textReadout = children.OfType<OpenUsdLiveBindingState>()
                    .Single(binding => binding.TargetPropertyName?.Value == "ua:operatingState");
                Assert.That(textReadout.Enabled!.Value, Is.True);
                // Configure the owned fixture's supported numeric/color/visibility profile explicitly.
                textReadout.Enabled.Value = false;
                await textReadout.ClearChangeMasksAsync(
                    server.DefaultSystemContext, includeChildren: true, CancellationToken.None).ConfigureAwait(false);
                CompanionOperationResult values = await RunFixtureTaskAsync(
                    provider, context, selected!, "read-bindings", [], token).ConfigureAwait(false);
                Assert.That(Value(values, "Sample count").TryGetValue(out int count), Is.True);
                Assert.That(count, Is.GreaterThan(0).And.LessThanOrEqualTo(127));
                Assert.That(Value(values, "Source session generation").TryGetValue(out NodeId sessionId), Is.True);
                Assert.That(sessionId, Is.EqualTo(context.Session.SessionId));
                Assert.That(Value(values, "Sample 1 source value").TryGetValue(out DataValue source), Is.True);
                Assert.That(StatusCode.IsGood(source.StatusCode), Is.True);
                Assert.That(Value(values, "Sample 1 source").TryGetValue(out ExpandedNodeId sourceId), Is.True);
                Assert.That(sourceId.IsNull, Is.False);

                CompanionOperationResult observed = await RunFixtureTaskAsync(provider, context, selected!,
                    "observe-bindings", [new("seconds", Variant.From(1u)), new("maxSamples", Variant.From(32u))],
                    token).ConfigureAwait(false);
                Assert.That(Value(observed, "Sample count").TryGetValue(out int received), Is.True);
                Assert.That(received, Is.GreaterThan(0).And.LessThanOrEqualTo(32));
                Assert.That(Value(observed, "Sample 1 sequence").TryGetValue(out uint sequence), Is.True);
                Assert.That(sequence, Is.GreaterThan(0));
                Assert.That(Value(observed, "Sample 1 publish time").TryGetValue(out DateTimeUtc publishTime), Is.True);
                Assert.That(publishTime, Is.Not.Default);
                Assert.That(context.Session.Connected, Is.True, "The borrowed primary session must remain owned.");
            });
        }

        private static async Task<uint> ReadUnsignedAsync(
            CompanionContext context, NodeId parent, string namespaceUri, string name, CancellationToken token)
        {
            ArrayOf<CompanionValue> fields = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, parent, namespaceUri, [name], token).ConfigureAwait(false);
            Assert.That(fields[0].Value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
            Assert.That(fields[0].Value.TryGetValue(out uint value), Is.True);
            Assert.That(value, Is.GreaterThan(0));
            return value;
        }

        private static async Task WithPrivateCompanionServerAsync(
            bool generator, Func<CompanionContext, IServerContext, string, CancellationToken, Task> exercise)
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-companion-probe-" + Guid.NewGuid().ToString("N"));
            string serverPki = Path.Combine(root, "server");
            Directory.CreateDirectory(root);
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int portNumber = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            string endpointUrl = $"opc.tcp://localhost:{portNumber}/UaLensOwnedCompanion";
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var started = new OwnedCompanionStartup();
            builder.Services.AddSingleton<IServerStartupTask>(started);
            IOpcUaBuilder opc = builder.Services.AddOpcUa();
            var server = opc.AddServer(options =>
            {
                options.ApplicationName = "UaLensOwnedCompanion";
                options.ApplicationUri = "urn:localhost:UaLensOwnedCompanion";
                options.ProductUri = "urn:opcfoundation.org:UaLensOwnedCompanion";
                options.PkiRoot = serverPki;
                options.AutoAcceptUntrustedCertificates = false;
                options.IncludeUnsecurePolicyNone = false;
                options.EndpointUrls.Add(endpointUrl);
            });
            if (generator)
            {
                builder.Services.Configure<GeneratorDeviceIntegrationOptions>(options =>
                {
                    options.GeneratorCount = 1;
                    options.InjectFaults = false;
                });
                server.ConfigureDevicesFor<GeneratorNodeManager>(static _ => { })
                    .AddNodeManager<GeneratorNodeManagerFactory>();
            }
            else
            {
                opc.AddWotRegistryServer(options =>
                {
                    options.AutoRefresh = false;
                    options.ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Opc.Ua.ObjectIds.WellKnownRole_Anonymous
                    };
                });
            }
            using IHost host = builder.Build();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await host.StartAsync(deadline.Token).ConfigureAwait(false);
                IServerContext serverContext = await started.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
                ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
                var backend = new StackConnectionBackend(telemetry,
                    token => AppConfig.BuildAsync(telemetry, Path.Combine(root, "client"), token));
                await using (backend.ConfigureAwait(false))
                {
                    var connection = new ConnectionService(telemetry, null, backend, new ProfileCredentialProvider());
                    await using (connection.ConfigureAwait(false))
                    {
                        ApplicationConfiguration configuration = await connection.GetConfigAsync(deadline.Token)
                            .ConfigureAwait(false);
                        ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(
                            configuration, endpointUrl, deadline.Token).ConfigureAwait(false);
                        EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                            candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                            candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                        using ICertificateStore own = new CertificateStoreIdentifier(
                            serverPki, CertificateStoreType.Directory).OpenStore(telemetry);
                        using CertificateCollection certificates = await own.EnumerateAsync(deadline.Token)
                            .ConfigureAwait(false);
                        Assert.That(certificates.Any(certificate =>
                            certificate.RawData.AsSpan().SequenceEqual(endpoint.ServerCertificate.Span)), Is.True);
                        await TrustOwnedPeersAsync(configuration, endpoint, serverPki, telemetry, deadline.Token)
                            .ConfigureAwait(false);
                        UserTokenPolicy policy = endpoint.UserIdentityTokens.ToList()
                            .Single(candidate => candidate.TokenType == UserTokenType.Anonymous);
                        var identity = new UserIdentity(new AnonymousIdentityToken()) { PolicyId = policy.PolicyId! };
                        await connection.ConnectAsync(new ConnectionOptions
                        {
                            EndpointUrl = endpointUrl,
                            UseSecurity = true,
                            Engine = SubscriptionEngineKind.ChannelV2
                        }, endpoint, identity,
                            (_, error) => throw new AssertionException("Unexpected trust request: " + error.StatusCode),
                            deadline.Token).ConfigureAwait(false);
                        ISession session = connection.CurrentSession ??
                            throw new AssertionException("The private secure session was not created.");
                        Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                        await exercise(new CompanionContext(session, telemetry, maxFields: 1024),
                            serverContext, root, deadline.Token)
                            .ConfigureAwait(false);
                        await connection.DisconnectAsync().ConfigureAwait(false);
                        Assert.That(connection.CurrentSession, Is.Null);
                    }
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                Directory.Delete(root, recursive: true);
            }
        }

        private static readonly string[] s_ownedVersions = ["v1", "v2"];

        private sealed class OwnedCompanionStartup : IServerStartupTask
        {
            public Task<IServerContext> Ready => m_ready.Task;

            public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_ready.TrySetResult(server);
                return ValueTask.CompletedTask;
            }

            private readonly TaskCompletionSource<IServerContext> m_ready =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
