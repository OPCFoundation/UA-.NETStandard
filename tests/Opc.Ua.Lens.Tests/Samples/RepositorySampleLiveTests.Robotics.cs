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

#if NET10_0_OR_GREATER
extern alias RobotIntentSample;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Samples;
using UaLens.Tests.Companions;
using ISession = Opc.Ua.Client.ISession;
using RobotSample = RobotIntentSample::Robotics.IntentEnabledRobot;
using RobotSimulation = RobotIntentSample::Robotics.IntentEnabledRobot.Simulation;

namespace UaLens.Tests.Samples;

public sealed partial class RepositorySampleLiveTests
{
    /// <summary>
    /// Runs only the catalog-owned finite MinimalRobotServer, observing real program state and absent cause methods.
    /// </summary>
    [Test]
    [Explicit("Requires built MinimalRobotServer and a private UALENS_ROBOTICS_PROBE_ROOT.")]
    [Category("RepositorySampleProbe")]
    public async Task OwnedMinimalRobotExposesPublishedProgramStateWithoutInventingCommandsAsync()
    {
        string source = Environment.GetEnvironmentVariable("UALENS_SAMPLE_SOURCE_ROOT") ??
            throw new InvalidOperationException("Set UALENS_SAMPLE_SOURCE_ROOT to the trusted built checkout.");
        string root = CreateRoboticsProbeRoot("minimal-robot");
        ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
        var samples = new RepositorySampleService(
            new RepositorySampleFiles(Path.Combine(root, "runs"), LocalFileSystem.Instance),
            new RepositorySamplePortAllocator(), new RepositorySampleRuntime(),
            new RepositorySampleDiscoveryProbe(telemetry), TimeProvider.System);
        await using (samples.ConfigureAwait(false))
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                RepositorySampleSnapshot configured = await samples.ConfigureAsync(
                    RepositorySampleId.MinimalRobotServer,
                    new RepositorySampleSource(source, RepositorySampleBuildConfiguration.Release,
                        RepositorySampleFramework.Net10, RepositorySampleBuildLayout.FrameworkDirectory),
                    deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(configured.Phase, Is.EqualTo(RepositorySamplePhase.Configured), configured.Message);
                RepositorySampleSnapshot ready = await samples.StartAsync(
                    new RepositorySampleRunOptions { RunSeconds = 20, AllowCompanionOperations = true }, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady), ready.Message);
                RepositorySampleEvidence evidence = ready.Evidence ??
                    throw new AssertionException("The owned robot returned no peer evidence.");
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
                            configuration, evidence.Endpoint.AbsoluteUri, deadline.Token).ConfigureAwait(false);
                        EndpointDescription endpoint = endpoints.ToList().Single(candidate =>
                            candidate.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                            candidate.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                        Assert.That(ByteString.From(SHA256.HashData(endpoint.ServerCertificate.Span)),
                            Is.EqualTo(evidence.CertificateSha256));
                        await TrustOwnedPeersAsync(configuration, endpoint,
                            ready.PrivatePkiRoot ?? throw new AssertionException("The owned PKI is missing."),
                            telemetry, deadline.Token).ConfigureAwait(false);
                        UserTokenPolicy anonymous = endpoint.UserIdentityTokens.ToList()
                            .Single(value => value.TokenType == UserTokenType.Anonymous);
                        var identity = new UserIdentity(new AnonymousIdentityToken())
                        {
                            PolicyId = anonymous.PolicyId!
                        };
                        await connection.ConnectAsync(new ConnectionOptions
                        {
                            EndpointUrl = evidence.Endpoint.AbsoluteUri,
                            UseSecurity = true,
                            Engine = SubscriptionEngineKind.ChannelV2
                        }, endpoint, identity,
                            (_, error) => throw new AssertionException("Unexpected trust request: " + error.StatusCode),
                            deadline.Token).ConfigureAwait(false);
                        ISession session = connection.CurrentSession ??
                            throw new AssertionException("The owned robot session is missing.");
                        var provider = new RoboticsCompanionProvider(new RepositorySampleCompanionPolicy(samples));
                        var workspace = new CompanionWorkspace([provider], telemetry);
                        await using (workspace.ConfigureAwait(false))
                        {
                            await workspace.BindAsync(session, deadline.Token).ConfigureAwait(false);
                            ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("robotics", deadline.Token)
                                .ConfigureAwait(false);
                            Assert.That(targets.ToList().Any(target => target.TypeName == "MotionDeviceSystem"),
                                Is.True);
                            CompanionTarget task = targets.ToList().Single(target => target.TypeName == "TaskControl");
                            CompanionInspection inspection = await workspace.InspectAsync(task, deadline.Token)
                                .ConfigureAwait(false);
                            Assert.That(inspection.Values.ToList()
                                .Single(value => value.Name == "Program loaded").Value,
                                Is.EqualTo(Variant.From(false)));
                            Assert.That(inspection.Values.ToList().Single(value => value.Name == "Program name").Value,
                                Is.EqualTo(Variant.From(string.Empty)));
                            Assert.That(inspection.Operations, Has.Count.EqualTo(1));
                            Assert.That(inspection.Operations[0].Id, Is.EqualTo("snapshot"));
                            Assert.That(inspection.Summary, Does.Contain("no program/operation handlers"));
                        }
                        await RequireOwnedReadAsync(session, deadline.Token).ConfigureAwait(false);
                    }
                }
                await samples.StopAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(samples.Snapshot.OwnsResources, Is.False);
            }
            catch (RepositorySampleException)
            {
                await TestContext.Out.WriteLineAsync(string.Join(Environment.NewLine,
                    samples.Snapshot.Output.ToList().Select(line => line.Text))).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await samples.StopAsync().ConfigureAwait(false);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Composes the actual IntentEnabledRobot cell and executor inside a private authenticated bounded host.
    /// No joint, gripper, physical output or real-time transport command is issued.
    /// </summary>
    [Test]
    [Explicit("Hosts a private-PKI authenticated IntentEnabledRobot composition; requires UALENS_ROBOTICS_PROBE_ROOT.")]
    [Category("RepositorySampleProbe")]
    public async Task OwnedIntentRobotQualifiesAdmissionAuthorityCancellationLeaseExpiryRebindAndUnknownOutcomeAsync()
    {
        string root = CreateRoboticsProbeRoot("intent-robot");
        string serverPki = Path.Combine(root, "server");
        byte[] password = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        int number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        string endpointUrl = $"opc.tcp://localhost:{number}/OwnedIntentRobot";
        var started = new OwnedCompanionStartup();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IServerStartupTask>(started);
        var database = new LinqUserDatabase();
        Assert.That(database.CreateUser("owned-operator", password, [Role.Operator], default,
            "Ephemeral owned robot probe"), Is.True);
        builder.Services.AddSingleton<IUserDatabase>(database);
        builder.Services.AddSingleton<RobotSimulation.SimulatedArmExecutor>();
        builder.Services.AddSingleton<RobotSample.SampleSafetySource>();
        builder.Services.AddSingleton<RobotSample.IntentRobotCell>();
        builder.Services.AddSingleton<IRobotIntentModelProvider, RobotSample.OpenUsdIntentModelProvider>();
        builder.Services.AddOpcUa().AddSecureServer(options =>
        {
            options.ApplicationName = "UaLensOwnedIntentRobot";
            options.ApplicationUri = "urn:localhost:UaLensOwnedIntentRobot";
            options.ProductUri = "uri:opcfoundation.org:IntentEnabledRobot";
            options.PkiRoot = serverPki;
            options.AutoAcceptUntrustedCertificates = false;
            options.EndpointUrls.Add(endpointUrl);
        }).ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
        {
            Name = Opc.Ua.BrowseNames.WellKnownRole_Operator,
            Identities =
            {
                new RoleIdentityMappingOptions
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "owned-operator"
                }
            }
        })).AddRobotIntent().AddRobotIntentExecutor<RobotSimulation.SimulatedArmExecutor>()
            .ConfigureRobotIntent(async (context, token) =>
                await context.GetRequiredService<RobotSample.IntentRobotCell>().ConfigureAsync(context, token)
                    .ConfigureAwait(false));
        using IHost host = builder.Build();
        try
        {
            await host.StartAsync(deadline.Token).ConfigureAwait(false);
            IServerContext liveServer = await started.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
            ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
            RobotSimulation.SimulatedArmExecutor simulator =
                host.Services.GetRequiredService<RobotSimulation.SimulatedArmExecutor>();
            ArrayOf<double> before = [.. simulator.CurrentSnapshot.JointAngles];
            await WithAuthenticatedRoboticsSessionAsync(telemetry, root, "primary", serverPki, endpointUrl, password,
                async (connection, context, token) =>
                {
                    var clock = new RoboticsTestClock { Now = DateTimeOffset.UtcNow };
                    var samples = new Mock<IRepositorySampleService>(MockBehavior.Strict);
                    RepositorySampleSnapshot snapshot = OwnedIntentSnapshot(context, clock.Now.AddMinutes(2));
                    samples.SetupGet(service => service.Snapshot).Returns(() => snapshot);
                    var policy = new RepositorySampleCompanionPolicy(samples.Object, timeProvider: clock);
                    var provider = new RobotIntentCompanionProvider(policy, clock);
                    var workspace = new CompanionWorkspace(
                        [provider], telemetry, timeProvider: clock, deploymentPolicy: policy);
                    await using (workspace.ConfigureAwait(false))
                    {
                        await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                        ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("robot-intent", token)
                            .ConfigureAwait(false);
                        CompanionTarget target = targets.ToList().Single();
                        var reader = new UaRobotIntentTransport(context.Session, target.NodeId, telemetry);
                        Assert.That(((IServerInternal)liveServer).SessionManager.GetSessions()
                            .Single(session => session.Id == context.Session.SessionId)
                            .EffectiveIdentity.GrantedRoleIds.ToList(),
                            Does.Contain(Opc.Ua.ObjectIds.WellKnownRole_Operator));
                        await RunOwnedRoboticsTaskAsync(workspace, target, "preview-wait",
                            WaitInputs("preview", 25), token).ConfigureAwait(false);
                        Assert.That(await reader.ReadControlOwnerAsync(token).ConfigureAwait(false),
                            Is.EqualTo(NodeId.Null));
                        CompanionOperationResult authority = await RunOwnedRoboticsTaskAsync(
                            workspace, target, "request-control", [], token).ConfigureAwait(false);
                        Assert.That(Value(authority, "Granted"), Is.EqualTo(Variant.From(true)));
                        Assert.That(await reader.ReadControlOwnerAsync(token).ConfigureAwait(false),
                            Is.EqualTo(context.Session.SessionId));

                        await WithAuthenticatedRoboticsSessionAsync(telemetry, root, "challenger", serverPki,
                            endpointUrl, password, async (_, challenger, challengerToken) =>
                            {
                                CompanionTaskInput request = await provider.PrepareInputAsync(
                                    challenger, target, "request-control", [], challengerToken).ConfigureAwait(false);
                                CompanionOperationResult denied = await provider.ExecutePreparedAsync(
                                    challenger, target, "request-control", request, null, challengerToken)
                                    .ConfigureAwait(false);
                                Assert.That(Value(denied, "Granted"), Is.EqualTo(Variant.From(false)));
                                Assert.That(Value(denied, "Control owner"),
                                    Is.EqualTo(Variant.From(context.Session.SessionId)));
                            }, token).ConfigureAwait(false);

                        CompanionOperationResult admitted = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "submit-wait", WaitInputs("complete-wait", 25), token).ConfigureAwait(false);
                        NodeId completed = OperationNode(admitted);
                        await RequireRobotIntentTerminalAsync(
                            reader, completed, false, ExecutionStateEnum.Succeeded, token)
                            .ConfigureAwait(false);
                        CompanionOperationResult mission = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "submit-wait-mission",
                            [.. WaitInputs("complete-mission", 25), new("count", Variant.From(2u))],
                            token).ConfigureAwait(false);
                        await RequireRobotIntentTerminalAsync(reader, OperationNode(mission), true,
                            ExecutionStateEnum.Succeeded, token).ConfigureAwait(false);

                        CompanionOperationResult waiting = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "submit-wait", WaitInputs("cancel-wait", 10000), token).ConfigureAwait(false);
                        NodeId waitingNode = OperationNode(waiting);
                        CompanionOperationResult cancelled = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "cancel-intent",
                            [new("operation", Variant.From(waitingNode)),
                                new("stopMode", Variant.From((int)StopModeEnum.OnPath))], token).ConfigureAwait(false);
                        Assert.That(Value(cancelled, "Accepted"), Is.EqualTo(Variant.From(true)));
                        await RequireRobotIntentTerminalAsync(
                            reader, waitingNode, false, ExecutionStateEnum.Cancelled, token)
                            .ConfigureAwait(false);

                        CompanionOperationResult waitingMission = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "submit-wait-mission",
                            [.. WaitInputs("cancel-mission", 5000), new("count", Variant.From(2u))],
                            token).ConfigureAwait(false);
                        NodeId waitingMissionNode = OperationNode(waitingMission);
                        CompanionOperationResult cancelledMission = await RunOwnedRoboticsTaskAsync(workspace, target,
                            "cancel-mission",
                            [new("operation", Variant.From(waitingMissionNode)),
                                new("stopMode", Variant.From((int)StopModeEnum.OnPath))], token).ConfigureAwait(false);
                        Assert.That(Value(cancelledMission, "Accepted"), Is.EqualTo(Variant.From(true)));
                        await RequireRobotIntentTerminalAsync(reader, waitingMissionNode, true,
                            ExecutionStateEnum.Cancelled, token).ConfigureAwait(false);

                        CompanionInspection state = await workspace.InspectAsync(target, token).ConfigureAwait(false);
                        Assert.That(state.Values.ToList().First(value => value.Name == "Real-time channel").Value
                            .TryGetValue(out NodeId channel), Is.True);
                        clock.Now = DateTimeOffset.UtcNow;
                        CompanionOperationResult lease = await RunOwnedRoboticsTaskAsync(
                            workspace, target, "open-channel",
                            [new("channel", Variant.From(channel)), new("lease", Variant.From(500d))], token)
                            .ConfigureAwait(false);
                        Assert.That(Value(lease, "Granted"), Is.EqualTo(Variant.From(true)));
                        Assert.That(Value(lease, "Lease expiry").TryGetValue(out DateTimeUtc expires), Is.True);
                        channel = await FindHeldRobotChannelAsync(context, state, token).ConfigureAwait(false);
                        CompanionTaskInput expiredLease = await provider.PrepareInputAsync(
                            context, target, "close-channel", [new("channel", Variant.From(channel))], token)
                            .ConfigureAwait(false);
                        NodeId publishedExpiry = await IndustrialCompanionAccess.ResolveChildAsync(context, channel,
                            Opc.Ua.RobotIntent.Namespaces.RobotIntent, "LeaseExpiry", true, token)
                            .ConfigureAwait(false);
                        TimeSpan remaining = expires.ToDateTime() - DateTime.UtcNow;
                        if (remaining > TimeSpan.Zero)
                        {
                            await Task.Delay(remaining + TimeSpan.FromMilliseconds(25), token).ConfigureAwait(false);
                        }
                        clock.Now = DateTimeOffset.UtcNow;
                        if (publishedExpiry.IsNull)
                        {
                            CompanionOperationResult lapsed = await provider.ExecutePreparedAsync(
                                context, target, "close-channel", expiredLease, null, token).ConfigureAwait(false);
                            Assert.That(Value(lapsed, "Closed"), Is.EqualTo(Variant.From(false)));
                        }
                        else
                        {
                            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                                context, target, "close-channel", expiredLease, null, token).AsTask(),
                                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                        }
                        await RunOwnedRoboticsTaskAsync(workspace, target, "open-channel",
                            [new("channel", Variant.From(channel)), new("lease", Variant.From(5000d))], token)
                            .ConfigureAwait(false);
                        CompanionOperationResult closed = await RunOwnedRoboticsTaskAsync(
                            workspace, target, "close-channel",
                            [new("channel", Variant.From(channel))], token).ConfigureAwait(false);
                        Assert.That(Value(closed, "Closed"), Is.EqualTo(Variant.From(true)));

                        await RequireUnknownRoboticsReplyAsync(context, target, policy, clock, reader, token)
                            .ConfigureAwait(false);
                        await RunOwnedRoboticsTaskAsync(workspace, target, "release-control", [], token)
                            .ConfigureAwait(false);
                        Assert.That(await reader.ReadControlOwnerAsync(token).ConfigureAwait(false),
                            Is.EqualTo(NodeId.Null));
                        CompanionTaskInput expired = await provider.PrepareInputAsync(
                            context, target, "request-control", [], token).ConfigureAwait(false);
                        clock.Now = snapshot.CompanionOperationsExpireAt!.Value;
                        await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                            context, target, "request-control", expired, null, token).AsTask(),
                            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                        clock.Now = DateTimeOffset.UtcNow;
                        CompanionTaskInput stale = await provider.PrepareInputAsync(
                            context, target, "request-control", [], token).ConfigureAwait(false);
                        await connection.ReconnectAsync(SubscriptionEngineKind.Classic, token).ConfigureAwait(false);
                        ISession replacement = connection.CurrentSession ??
                            throw new AssertionException("The replacement authenticated session is missing.");
                        Assert.That(replacement.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                        var rebound = new CompanionContext(replacement, telemetry);
                        await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                            rebound, target, "request-control", stale, null, token).AsTask(),
                            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                        await workspace.BindAsync(replacement, token).ConfigureAwait(false);
                        await workspace.DiscoverAsync("robot-intent", token).ConfigureAwait(false);
                        NodeId ownerVariable = await IndustrialCompanionAccess.ResolveChildAsync(rebound, target.NodeId,
                            Opc.Ua.RobotIntent.Namespaces.RobotIntent, "ControlOwner", false, token)
                            .ConfigureAwait(false);
                        DataValue ownerAfterRebind = await CompanionMethodMetadata.ReadOneAsync(
                            rebound, ownerVariable, Attributes.Value, token).ConfigureAwait(false);
                        Assert.That(ownerAfterRebind.WrappedValue, Is.EqualTo(Variant.From(NodeId.Null)));
                        snapshot = snapshot with { CompanionOperationsAuthorized = false };
                        await Assert.ThatAsync(() => provider.PrepareInputAsync(
                            rebound, target, "request-control", [], token).AsTask(),
                            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                        await RequireOwnedReadAsync(replacement, token).ConfigureAwait(false);
                    }
                    await RequireOwnedReadAsync(connection.CurrentSession!, token).ConfigureAwait(false);
                }, deadline.Token).ConfigureAwait(false);
            Assert.That(simulator.CurrentSnapshot.JointAngles, Is.EqualTo(before));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(cleanup.Token).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(password);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RequireUnknownRoboticsReplyAsync(
        CompanionContext context, CompanionTarget target, RepositorySampleCompanionPolicy policy,
        RoboticsTestClock clock,
        IRobotIntentTransport reader, CancellationToken token)
    {
        ISession real = context.Session;
        var proxy = new Mock<ISession>(MockBehavior.Strict);
        proxy.SetupGet(session => session.Factory).Returns(real.Factory);
        proxy.SetupGet(session => session.MessageContext).Returns(real.MessageContext);
        proxy.SetupGet(session => session.NamespaceUris).Returns(real.NamespaceUris);
        proxy.SetupGet(session => session.Connected).Returns(() => real.Connected);
        proxy.SetupGet(session => session.SessionId).Returns(real.SessionId);
        proxy.SetupGet(session => session.Identity).Returns(real.Identity);
        proxy.SetupGet(session => session.Endpoint).Returns(real.Endpoint);
        proxy.SetupGet(session => session.TransportChannel).Returns(real.TransportChannel);
        proxy.Setup(session => session.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
            It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, double age, TimestampsToReturn stamps, ArrayOf<ReadValueId> nodes,
                CancellationToken ct) => real.ReadAsync(header, age, stamps, nodes, ct));
        proxy.Setup(session => session.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
            It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, ViewDescription? view, uint maximum, ArrayOf<BrowseDescription> nodes,
                CancellationToken ct) => real.BrowseAsync(header, view, maximum, nodes, ct));
        proxy.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(It.IsAny<RequestHeader>(),
            It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, ArrayOf<BrowsePath> paths, CancellationToken ct) =>
                real.TranslateBrowsePathsToNodeIdsAsync(header, paths, ct));
        int calls = 0;
        NodeId admitted = NodeId.Null;
        proxy.Setup(session => session.CallAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
            It.IsAny<CancellationToken>()))
            .Returns(async (RequestHeader? header, ArrayOf<CallMethodRequest> methods, CancellationToken ct) =>
            {
                Interlocked.Increment(ref calls);
                CallResponse response = await real.CallAsync(header, methods, ct).ConfigureAwait(false);
                Assert.That(response.Results[0].OutputArguments[0], Is.EqualTo(Variant.From(true)));
                Assert.That(response.Results[0].OutputArguments[2].TryGetValue(out admitted), Is.True);
                throw new TimeoutException("Owned fixture intentionally withheld one accepted submission reply.");
            });
        var uncertain = new CompanionContext(proxy.Object, context.Telemetry);
        var provider = new RobotIntentCompanionProvider(policy, clock);
        CompanionTaskInput task = await provider.PrepareInputAsync(
            uncertain, target, "submit-wait", WaitInputs("unknown-reply-wait", 25), token).ConfigureAwait(false);
        await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
            uncertain, target, "submit-wait", task, null, token).AsTask(), Throws.TypeOf<TimeoutException>())
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
            uncertain, target, "submit-wait", task, null, token).AsTask(), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(admitted.IsNull, Is.False);
        await RequireRobotIntentTerminalAsync(reader, admitted, false, ExecutionStateEnum.Succeeded, token)
            .ConfigureAwait(false);
        proxy.Verify(session => session.Dispose(), Times.Never);
        await RequireOwnedReadAsync(real, token).ConfigureAwait(false);
    }

    private static async Task WithAuthenticatedRoboticsSessionAsync(
        ITelemetryContext telemetry, string root, string name, string serverPki, string endpointUrl, byte[] password,
        Func<ConnectionService, CompanionContext, CancellationToken, Task> exercise, CancellationToken token)
    {
        var backend = new StackConnectionBackend(telemetry,
            ct => AppConfig.BuildAsync(telemetry, Path.Combine(root, name), ct));
        await using (backend.ConfigureAwait(false))
        {
            var connection = new ConnectionService(telemetry, null, backend, new ProfileCredentialProvider());
            await using (connection.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await connection.GetConfigAsync(token).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = await backend.DiscoverAsync(configuration, endpointUrl, token)
                    .ConfigureAwait(false);
                EndpointDescription endpoint = endpoints.ToList().Single(value =>
                    value.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    value.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                Assert.That(endpoint.UserIdentityTokens.ToList()
                    .Any(value => value.TokenType == UserTokenType.Anonymous),
                    Is.False);
                await TrustOwnedPeersAsync(configuration, endpoint, serverPki, telemetry, token).ConfigureAwait(false);
                UserTokenPolicy userPolicy = endpoint.UserIdentityTokens.ToList().Single(
                    value => value.TokenType == UserTokenType.UserName);
                ConnectionProfile profile = ConnectionProfile.Create(endpoint, userPolicy,
                    SubscriptionEngineKind.ChannelV2, identityName: "owned-operator");
                await ConnectOwnedUserNameAsync(connection, endpoint, profile, password, token).ConfigureAwait(false);
                ISession session = connection.CurrentSession ??
                    throw new AssertionException("The authenticated robot session is missing.");
                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                await exercise(connection, new CompanionContext(session, telemetry), token).ConfigureAwait(false);
            }
        }
    }

    private static RepositorySampleSnapshot OwnedIntentSnapshot(CompanionContext context, DateTimeOffset expires)
    {
        EndpointDescription endpoint = context.Session.Endpoint;
        return new RepositorySampleSnapshot(
            RepositorySampleId.IntentEnabledRobot, RepositorySamplePhase.AdvertisedReady,
            "Owned IntentEnabledRobot host")
        {
            RunId = Guid.NewGuid(),
            OwnsResources = true,
            CompanionOperationsAuthorized = true,
            CompanionOperationsExpireAt = expires,
            Evidence = new RepositorySampleEvidence(new Uri(endpoint.EndpointUrl!),
                endpoint.Server.ApplicationUri!, endpoint.Server.ApplicationName.Text!, endpoint.Server.ProductUri!,
                ByteString.From(SHA256.HashData(endpoint.ServerCertificate.Span)))
        };
    }

    private static async Task<CompanionOperationResult> RunOwnedRoboticsTaskAsync(
        CompanionWorkspace workspace, CompanionTarget target, string operation, ArrayOf<CompanionValue> inputs,
        CancellationToken token)
    {
        CompanionInspection inspection = await workspace.InspectAsync(target, token).ConfigureAwait(false);
        Assert.That(inspection.Operations.ToList().Any(value => value.Id == operation), Is.True,
            operation +
            " was not offered: " +
            string.Join("; ", inspection.Values.ToList()
                .Where(value => !StatusCode.IsGood(value.Quality)).Select(value => value.Name + ": " + value.Text)));
        CompanionOperationDraft prepared = await workspace.PrepareTaskAsync(target, operation, inputs, token)
            .ConfigureAwait(false);
        return await workspace.ExecuteTaskAsync(prepared, false, true, null, token).ConfigureAwait(false);
    }

    private static NodeId OperationNode(CompanionOperationResult result)
    {
        Assert.That(Value(result, "Accepted"), Is.EqualTo(Variant.From(true)), result.Summary);
        Assert.That(Value(result, "Operation").TryGetValue(out NodeId operation), Is.True);
        Assert.That(operation.IsNull, Is.False);
        return operation;
    }

    private static async Task<NodeId> FindHeldRobotChannelAsync(
        CompanionContext context, CompanionInspection inspection, CancellationToken token)
    {
        NodeId held = NodeId.Null;
        foreach (CompanionValue field in inspection.Values.ToList().Where(value => value.Name == "Real-time channel"))
        {
            Assert.That(field.Value.TryGetValue(out NodeId channel), Is.True);
            NodeId holder = await IndustrialCompanionAccess.ResolveChildAsync(context, channel,
                Opc.Ua.RobotIntent.Namespaces.RobotIntent, "LeaseHolder", false, token).ConfigureAwait(false);
            DataValue current = await CompanionMethodMetadata.ReadOneAsync(context, holder, Attributes.Value, token)
                .ConfigureAwait(false);
            Assert.That(current.WrappedValue.TryGetValue(out NodeId owner), Is.True);
            if (owner == context.Session.SessionId)
            {
                Assert.That(held.IsNull, Is.True, "The fixture must expose one active broker entry for this session.");
                held = channel;
            }
        }
        Assert.That(held.IsNull, Is.False, "No broker channel reports this authenticated session as its holder.");
        return held;
    }

    private static async Task RequireRobotIntentTerminalAsync(
        IRobotIntentTransport reader, NodeId operation, bool mission, ExecutionStateEnum expected,
        CancellationToken token)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            ExecutionStateEnum actual = mission
                ? (await reader.ReadMissionSnapshotAsync(operation, token).ConfigureAwait(false)).ExecutionState
                : (await reader.ReadOperationSnapshotAsync(operation, token).ConfigureAwait(false)).ExecutionState;
            if (actual == expected)
            {
                return;
            }
            if (actual is ExecutionStateEnum.Succeeded or ExecutionStateEnum.Failed or ExecutionStateEnum.Cancelled)
            {
                Assert.That(actual, Is.EqualTo(expected));
            }
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        Assert.Fail(
            "The owned wait operation did not reach the expected terminal state within the bounded observation.");
    }

    private static ArrayOf<CompanionValue> WaitInputs(string id, double duration)
    {
        return [new("id", Variant.From(id)), new("duration", Variant.From(duration))];
    }

    private static string CreateRoboticsProbeRoot(string name)
    {
        string root = Environment.GetEnvironmentVariable("UALENS_ROBOTICS_PROBE_ROOT") ??
            throw new InvalidOperationException(
                "Set UALENS_ROBOTICS_PROBE_ROOT to a private writable artifact directory.");
        if (!Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException(
                "UALENS_ROBOTICS_PROBE_ROOT must be an absolute private artifact directory.");
        }
        string owned = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        return owned;
    }
}
#endif
