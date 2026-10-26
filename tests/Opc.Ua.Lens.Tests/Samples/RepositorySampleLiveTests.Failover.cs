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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;
using UaLens.Connection;
using UaLens.Plugins.Continuity;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        /// <summary>
        /// Uses real redundancy discovery to take over on a distinct, explicitly trusted cold peer.
        /// </summary>
        [Test]
        [Explicit("Hosts two private-PKI servers and exercises configured, nonphysical cold-server takeover.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateConfiguredColdFailoverReportsRecreationAndRetainedSourceWithoutDisposingPrimaryAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-owned-failover-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            using var firstPort = new TcpListener(IPAddress.Loopback, 0);
            using var secondPort = new TcpListener(IPAddress.Loopback, 0);
            firstPort.Start();
            secondPort.Start();
            string sourceEndpoint = $"opc.tcp://localhost:{((IPEndPoint)firstPort.LocalEndpoint).Port}/UaLensPrimary";
            string targetEndpoint = $"opc.tcp://localhost:{((IPEndPoint)secondPort.LocalEndpoint).Port}/UaLensStandby";
            firstPort.Stop();
            secondPort.Stop();
            string sourceUri = Utils.ReplaceLocalhost("urn:localhost:UaLensOwnedPrimary");
            string targetUri = Utils.ReplaceLocalhost("urn:localhost:UaLensOwnedStandby");
            string sourcePki = Path.Combine(root, "primary");
            string targetPki = Path.Combine(root, "standby");
            var sourceReady = new OwnedCompanionStartup();
            var targetReady = new OwnedCompanionStartup();
            using var sourceLevel = new LeaderServiceLevelProvider(new StaticLeaderElection(true));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                using IHost sourceHost = CreateOwnedContinuityPeerHost(
                    "UaLensOwnedPrimary", sourceUri, sourceEndpoint, targetUri, targetEndpoint,
                    sourcePki, sourceReady, sourceLevel);
                using IHost targetHost = CreateOwnedContinuityPeerHost(
                    "UaLensOwnedStandby", targetUri, targetEndpoint, sourceUri, sourceEndpoint,
                    targetPki, targetReady, new ConstantServiceLevelProvider());
                try
                {
                    await targetHost.StartAsync(deadline.Token).ConfigureAwait(false);
                    await WaitForOwnedCompanionStartupAsync(targetHost, targetReady, deadline.Token)
                        .ConfigureAwait(false);
                    await sourceHost.StartAsync(deadline.Token).ConfigureAwait(false);
                    var sourceServer = await WaitForOwnedCompanionStartupAsync(
                        sourceHost, sourceReady, deadline.Token).ConfigureAwait(false);
                    var targetServer = await targetReady.Ready.ConfigureAwait(false);
                    if (sourceServer is not Opc.Ua.Server.IServerInternal sourceInternal ||
                        targetServer is not Opc.Ua.Server.IServerInternal targetInternal)
                    {
                        throw new AssertionException("The owned server subscription managers are not available.");
                    }
                    ITelemetryContext telemetry = sourceHost.Services.GetRequiredService<ITelemetryContext>();
                    ConnectionService? primaryOwner = null;
                    await ExercisePrivateCompanionAsync(telemetry, root, sourcePki, sourceEndpoint,
                        (_, _) => Task.CompletedTask, async (context, _, _, token) =>
                        {
                            ApplicationConfiguration configuration = await primaryOwner!.GetConfigAsync(token)
                                .ConfigureAwait(false);
                            var connectionBackend = new StackConnectionBackend(telemetry);
                            await using (connectionBackend.ConfigureAwait(false))
                            {
                                ArrayOf<EndpointDescription> endpoints = await connectionBackend.DiscoverAsync(
                                    configuration, targetEndpoint, token).ConfigureAwait(false);
                                EndpointDescription target = endpoints.ToList().Single(endpoint =>
                                    endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                                    endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                                Assert.That(target.Server.ApplicationUri, Is.EqualTo(targetUri));
                                using ICertificateStore own = new CertificateStoreIdentifier(
                                    targetPki, CertificateStoreType.Directory).OpenStore(telemetry);
                                using CertificateCollection certificates = await own.EnumerateAsync(token)
                                    .ConfigureAwait(false);
                                Assert.That(certificates.Any(certificate =>
                                    certificate.RawData.AsSpan().SequenceEqual(target.ServerCertificate.Span)),
                                    Is.True);
                                await TrustOwnedPeersAsync(configuration, target, targetPki, telemetry, token)
                                    .ConfigureAwait(false);
                                var redundancy = new DefaultServerRedundancyHandler(
                                    new DefaultRedundantServerEndpointResolver(telemetry));
                                ServerRedundancyInfo healthy = await redundancy.FetchRedundancyInfoAsync(
                                    context.Session, token).ConfigureAwait(false);
                                Assert.That(healthy.Mode, Is.EqualTo(RedundancySupport.Cold));
                                Assert.That(redundancy.ShouldFailover(healthy, context.Session.ConfiguredEndpoint)
                                    .IsFailoverWarranted, Is.False);
                                var credentials = new ProfileCredentialProvider();
                                var opened = new List<ContinuitySessionPurpose>();
                                ManagedSession? targetSession = null;
                                var sessions = new ConfiguredContinuitySessionFactory(async (purpose, ct) =>
                                {
                                    opened.Add(purpose);
                                    EndpointDescription selected = context.Session.Endpoint;
                                    if (purpose == ContinuitySessionPurpose.RedundantTarget)
                                    {
                                        ServerRedundancyInfo degraded = await redundancy.FetchRedundancyInfoAsync(
                                            context.Session, ct).ConfigureAwait(false);
                                        Assert.That(redundancy.ShouldFailover(
                                            degraded, context.Session.ConfiguredEndpoint).IsFailoverWarranted, Is.True);
                                        ConfiguredEndpoint peer = redundancy.SelectFailoverTarget(
                                            degraded, context.Session.ConfiguredEndpoint) ??
                                            throw new AssertionException(
                                                "The configured healthy peer was not resolved.");
                                        selected = peer.Description;
                                        Assert.That(selected.Server.ApplicationUri, Is.EqualTo(targetUri));
                                        Assert.That(ConnectionProfile.EndpointUrlsMatch(
                                            selected.EndpointUrl, target.EndpointUrl), Is.True);
                                        Assert.That(selected.ServerCertificate, Is.EqualTo(target.ServerCertificate));
                                        sourceLevel.SetServiceLevel(ServiceLevels.Maximum);
                                    }
                                    else
                                    {
                                        Assert.That(purpose, Is.EqualTo(ContinuitySessionPurpose.Source));
                                    }
                                    UserTokenPolicy policy = selected.UserIdentityTokens.ToList()
                                        .Single(candidate => candidate.TokenType == UserTokenType.Anonymous);
                                    ConnectionProfile profile = ConnectionProfile.Create(
                                        selected, policy, SubscriptionEngineKind.ChannelV2);
                                    IClientIdentityProvider identity = await credentials.GetAsync(profile, ct)
                                        .ConfigureAwait(false);
                                    IConnectionSession connection = await connectionBackend.ConnectAsync(
                                        configuration, selected, profile,
                                        new ProfileIdentityProvider(profile, identity), ct).ConfigureAwait(false);
                                    if (purpose == ContinuitySessionPurpose.RedundantTarget)
                                    {
                                        targetSession = connection.Session as ManagedSession;
                                    }
                                    return connection;
                                }, redundantSetConfigured: true);
                                var timeline = new ContinuityTimeline();
                                var backend = new V2ContinuityBackend(
                                    () => context.Session, timeline, telemetry, sessions: sessions);
                                await using (backend.ConfigureAwait(false))
                                {
                                    ContinuityConfiguration settings = ContinuityState.Validate(new ContinuityStateDto
                                    {
                                        Scenario = (int)ContinuityScenario.ConfiguredFailover,
                                        Targets = ["i=2258"],
                                        PublishingIntervalMs = 100,
                                        SamplingIntervalMs = 20,
                                        QueueSize = 17,
                                        KeepAliveCount = 7,
                                        LifetimeCount = 1000
                                    });
                                    NodeId primarySessionId = context.Session.SessionId;
                                    await backend.StartAsync(settings, token).ConfigureAwait(false);
                                    await WaitForContinuityAsync(() => timeline.Snapshot().Counters.Values > 0, token)
                                        .ConfigureAwait(false);
                                    Dictionary<string, string> before = ContinuityMetrics(backend);
                                    uint retainedId = uint.Parse(before["Server partition ids"],
                                        CultureInfo.InvariantCulture);
                                    long values = timeline.Snapshot().Counters.Values;
                                    long creations = timeline.Snapshot().Counters.CreationObservations;
                                    sourceLevel.SetServiceLevel(ServiceLevels.DegradedMaximum);

                                    ContinuityStepResult result = await backend.StepAsync(token).ConfigureAwait(false);

                                    Assert.That(result.WaitingForRestore, Is.False);
                                    Assert.That(result.Description,
                                        Does.StartWith("Recreation observed, not seamless"));
                                    await WaitForContinuityAsync(
                                        () => timeline.Snapshot().Counters.Values > values, token).ConfigureAwait(false);
                                    Dictionary<string, string> after = ContinuityMetrics(backend);
                                    foreach (string setting in s_continuityRevisedSettings.Take(4))
                                    {
                                        Assert.That(after[setting], Is.EqualTo(before[setting]), setting);
                                    }
                                    Assert.That(opened, Is.EqualTo(new[]
                                    {
                                        ContinuitySessionPurpose.Source, ContinuitySessionPurpose.RedundantTarget
                                    }));
                                    Assert.That(targetSession, Is.Not.Null);
                                    Assert.That(targetSession!.Endpoint.Server.ApplicationUri, Is.EqualTo(targetUri));
                                    Assert.That(targetSession.Endpoint.EndpointUrl,
                                        Is.Not.EqualTo(context.Session.Endpoint.EndpointUrl));
                                    Assert.That(timeline.Snapshot().Counters.CreationObservations,
                                        Is.GreaterThan(creations));
                                    Assert.That(timeline.Snapshot().Counters.TransferObservations, Is.Zero);
                                    Assert.That(timeline.Snapshot().Counters.BadValues, Is.Zero);
                                    Assert.That(timeline.Snapshot().Counters.UnknownPartitionValues, Is.Zero);
                                    Assert.That(timeline.Snapshot().Entries.ToList()
                                        .Where(entry => entry.Kind == ContinuityEvidenceKind.Data)
                                        .Select(entry => entry.ClientSessionId).Distinct().Count(), Is.EqualTo(2));

                                    await Assert.ThatAsync(() => backend.StopAsync(token),
                                        Throws.TypeOf<InvalidOperationException>().With.Message
                                            .Contains("Retained source-resource cleanup remains unconfirmed"))
                                        .ConfigureAwait(false);

                                    Assert.That(targetSession.Disposed, Is.True);
                                    Assert.That(targetInternal.SubscriptionManager.GetSubscriptions(), Is.Empty);
                                    Assert.That(sourceInternal.SubscriptionManager.TryGetSubscription(
                                        retainedId, out _), Is.True);
                                    Assert.That(timeline.Snapshot().Entries.ToList().Any(entry =>
                                        entry.Kind == ContinuityEvidenceKind.CleanupUncertain &&
                                        entry.PartitionServerId == retainedId), Is.True);
                                    Assert.That(context.Session.Disposed, Is.False);
                                    Assert.That(context.Session.SessionId, Is.EqualTo(primarySessionId));
                                    await RequireOwnedReadAsync(context.Session, token).ConfigureAwait(false);
                                }
                            }
                        }, deadline.Token, (owner, _) =>
                        {
                            primaryOwner = owner;
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await sourceHost.StopAsync(cleanup.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await targetHost.StopAsync(cleanup.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static IHost CreateOwnedContinuityPeerHost(
            string name,
            string applicationUri,
            string endpoint,
            string peerUri,
            string peerEndpoint,
            string pki,
            OwnedCompanionStartup started,
            IServiceLevelProvider serviceLevel)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            IOpcUaServerBuilder server = builder.Services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = name;
                options.ApplicationUri = applicationUri;
                options.ProductUri = "urn:opcfoundation.org:UaLensOwnedCompanion";
                options.PkiRoot = pki;
                options.AutoAcceptUntrustedCertificates = false;
                options.IncludeUnsecurePolicyNone = false;
                options.EndpointUrls.Add(endpoint);
                options.ConfigureBuilder = configuration => configuration.SetShutdownDelay(0);
            });
            server.AddServerServiceLevel(serviceLevel).AddServerRedundancy(options =>
            {
                options.Mode = RedundancySupport.Cold;
                options.AddRedundantPeer(peerUri, [peerEndpoint]);
            });
            builder.Services.AddSingleton<IServerStartupTask>(started);
            return builder.Build();
        }
    }
}
