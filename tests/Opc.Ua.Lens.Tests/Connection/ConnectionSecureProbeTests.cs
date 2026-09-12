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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens.Tests.Connection;

/// <summary>
/// Opt-in end-to-end recovery probe. Set UALENS_SECURE_PROBE_ENDPOINT to a
/// task-owned server that accepts the probe's generated client certificate.
/// Every run uses a fresh PKI beneath the working directory and deletes only
/// that run's directory after awaiting connection/configuration disposal.
/// </summary>
[TestFixture]
[Explicit("Requires UALENS_SECURE_PROBE_ENDPOINT and a task-owned server.")]
[Category("SecureConnectionProbe")]
[NonParallelizable]
public sealed class ConnectionSecureProbeTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ExplicitSecureAnonymousConnectionReceivesDataAndReacquiresTrustAfterDisconnect(bool useV2)
    {
        string endpointUrl = Environment.GetEnvironmentVariable("UALENS_SECURE_PROBE_ENDPOINT") ??
            throw new InvalidOperationException("Set UALENS_SECURE_PROBE_ENDPOINT to the task-owned server.");
        string directory = Path.Combine("TestResults", "UaLensSecureConnection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var telemetry = new ProbeTelemetry();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            string pkiRoot = Path.GetFullPath(directory);
            var backend = new RecordingBackend(new StackConnectionBackend(
                telemetry, ct => AppConfig.BuildAsync(telemetry, pkiRoot, ct)));
            var service = new ConnectionService(telemetry, null, backend, new ProfileCredentialProvider());
            await using (service.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await service.GetConfigAsync().ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = await backend
                    .DiscoverAsync(configuration, endpointUrl, deadline.Token)
                    .ConfigureAwait(false);
                EndpointDescription? selected = null;
                UserTokenPolicy? anonymousPolicy = null;
                foreach (EndpointDescription candidate in endpoints)
                {
                    if (candidate.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                        candidate.SecurityPolicyUri != SecurityPolicies.Basic256Sha256)
                    {
                        continue;
                    }
                    foreach (UserTokenPolicy policy in candidate.UserIdentityTokens)
                    {
                        if (policy.TokenType == UserTokenType.Anonymous)
                        {
                            selected = candidate;
                            anonymousPolicy = policy;
                            break;
                        }
                    }
                    if (selected is not null)
                    {
                        break;
                    }
                }
                Assert.That(selected, Is.Not.Null, "The probe never substitutes an unsecured endpoint.");
                Assert.That(anonymousPolicy, Is.Not.Null);

                SubscriptionEngineKind engine = useV2
                    ? SubscriptionEngineKind.ChannelV2
                    : SubscriptionEngineKind.Classic;
                int prompts = 0;
                var identity = new UserIdentity(new AnonymousIdentityToken())
                {
                    PolicyId = anonymousPolicy!.PolicyId!
                };
                await service.ConnectAsync(
                    new ConnectionOptions
                    {
                        EndpointUrl = selected!.EndpointUrl!,
                        UseSecurity = true,
                        Engine = engine
                    },
                    selected,
                    identity,
                    (_, error) =>
                    {
                        Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                        prompts++;
                        return Task.FromResult(TrustChoice.AcceptOnce);
                    },
                    deadline.Token).ConfigureAwait(false);

                Assert.That(service.IsConnected, Is.True);
                Assert.That(service.Profile!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(service.Profile.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(service.Session!.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
                Assert.That(
                    service.Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(
                    service.Session.ConfiguredEndpoint.Description.SecurityPolicyUri,
                    Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(service.Session.Identity, Is.Not.SameAs(identity));
                Assert.That(prompts, Is.EqualTo(1));
                Assert.That(backend.ConnectAttempts, Is.EqualTo(2));

                DataValue value = await service.Session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True);

                ISubscriptionAdapter adapter = service.CreateAdapter(trackLifetime: false);
                ISubscriptionAdapter? documentAdapter = adapter;
                int adapterDisposals = 0;
                service.ConnectionChangedAsync += async _ =>
                {
                    if (service.CurrentSession is not null)
                    {
                        return;
                    }
                    ISubscriptionAdapter? detached = Interlocked.Exchange(ref documentAdapter, null);
                    if (detached is not null)
                    {
                        await detached.DisposeAsync().ConfigureAwait(false);
                        adapterDisposals++;
                    }
                };
                await adapter.ApplySubscriptionAsync(new SubscriptionConfig
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 5,
                    LifetimeCount = 30,
                    PublishingEnabled = true
                }, deadline.Token).ConfigureAwait(false);
                await adapter.AddItemAsync(new MonitoredItemConfig
                {
                    DisplayName = "Server current time",
                    NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = TimeSpan.FromMilliseconds(100),
                    QueueSize = 1,
                    DiscardOldest = true,
                    MonitoringMode = MonitoringMode.Reporting
                }, deadline.Token).ConfigureAwait(false);
                while (adapter.Counters.DataValues == 0)
                {
                    await adapter.Events.ReadAsync(deadline.Token).ConfigureAwait(false);
                }
                Assert.That(adapter.Counters.DataValues, Is.GreaterThan(0));

                IUserIdentity firstIdentity = service.Session.Identity;
                await service.DisconnectAsync().ConfigureAwait(false);
                Assert.That(adapterDisposals, Is.EqualTo(1));
                await service.ReconnectAsync(engine, deadline.Token).ConfigureAwait(false);

                Assert.That(service.IsConnected, Is.True);
                Assert.That(service.Session!.Identity, Is.Not.SameAs(firstIdentity));
                Assert.That(service.Session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
                Assert.That(service.Profile!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(prompts, Is.EqualTo(2), "Accept-once must be reacquired after disconnect.");
                Assert.That(backend.ConnectAttempts, Is.EqualTo(4));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingBackend : IConnectionBackend
    {
        public RecordingBackend(IConnectionBackend inner)
        {
            m_inner = inner;
        }

        public int ConnectAttempts { get; private set; }

        public Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
        {
            return m_inner.CreateConfigurationAsync(ct);
        }

        public Task<ArrayOf<EndpointDescription>> DiscoverAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            CancellationToken ct)
        {
            return m_inner.DiscoverAsync(configuration, endpointUrl, ct);
        }

        public Task<IConnectionSession> ConnectAsync(
            ApplicationConfiguration configuration,
            EndpointDescription endpoint,
            ConnectionProfile profile,
            IClientIdentityProvider identityProvider,
            CancellationToken ct)
        {
            ConnectAttempts++;
            return m_inner.ConnectAsync(configuration, endpoint, profile, identityProvider, ct);
        }

        private readonly IConnectionBackend m_inner;
    }

    private sealed class ProbeTelemetry : ITelemetryContext, IDisposable
    {
        public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

        public ActivitySource ActivitySource { get; } = new(nameof(ConnectionSecureProbeTests));

        public Meter CreateMeter()
        {
            return new Meter(nameof(ConnectionSecureProbeTests));
        }

        public void Dispose()
        {
            ActivitySource.Dispose();
        }
    }
}
