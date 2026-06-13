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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// CA2000: contract-test disposables are transferred to the environment or released by cleanup paths.
// CA2007: NUnit invokes test code without requiring ConfigureAwait on framework calls.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Shared fake-channel infrastructure for Layer-1 channel-manager contract tests.
    /// </summary>
    public abstract class ContractTestBase
    {
        protected const string DefaultEndpointUrl = "opc.tcp://localhost:4840/Contract";

        protected static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(5);
        protected static readonly TimeSpan ObservationWindow = TimeSpan.FromMilliseconds(250);

        protected static TimeSpan DefaultWait => TimeSpan.FromSeconds(10);

        private static TimeSpan DefaultPollInterval => TimeSpan.FromMilliseconds(20);

        protected static Certificate CreateCertificate(string commonName)
        {
            if (string.IsNullOrWhiteSpace(commonName))
            {
                throw new ArgumentException("A certificate common name is required.", nameof(commonName));
            }

            return s_factory.CreateCertificate($"CN={commonName}").CreateForRSA();
        }

        protected static ContractTestEnvironment CreateEnvironment(
            Certificate applicationCertificate,
            Func<string, FakeTransport>? transportFactory = null,
            IChannelReconnectPolicy? reconnectPolicy = null)
        {
            if (applicationCertificate == null)
            {
                throw new ArgumentNullException(nameof(applicationCertificate));
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var certificateChanges = new CertificateChangeSource();
            var certificateManager = new Mock<ICertificateManager>();
            certificateManager.SetupGet(manager => manager.CertificateChanges).Returns(certificateChanges);

            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = certificateManager.Object
            };
            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier
            {
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = applicationCertificate.Thumbprint
            };
            configuration.SecurityConfiguration.SendCertificateChain = false;

            FakeChannelBindings bindings = transportFactory != null
                ? new FakeChannelBindings(transportFactory)
                : new FakeChannelBindings();
            ClientChannelManager managerInstance = new(
                configuration,
                telemetry,
                bindings,
                reconnectPolicy ?? new ImmediateReconnectPolicy());
            managerInstance.UpdateClientCertificate(applicationCertificate.AddRef(), null);

            return new ContractTestEnvironment(
                configuration,
                certificateManager,
                certificateChanges,
                bindings,
                managerInstance);
        }

        protected static ClientChannelManager CreateManager(
            FakeChannelBindings bindings,
            IChannelReconnectPolicy? reconnectPolicy = null)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ClientChannelManager(
                CreateApplicationConfiguration(telemetry),
                telemetry,
                bindings,
                reconnectPolicy ?? CreateImmediateHarnessReconnectPolicy());
        }

        protected static ContractHarness CreateHarness(
            string endpointUrl = DefaultEndpointUrl,
            IChannelReconnectPolicy? reconnectPolicy = null)
        {
            return new ContractHarness(
                CreateEndpoint(endpointUrl, SecurityPolicies.None, MessageSecurityMode.None),
                reconnectPolicy,
                transportFactory: null);
        }

        protected static ContractHarness CreateHarness(
            Func<string, FakeTransport> transportFactory,
            string endpointUrl = DefaultEndpointUrl,
            IChannelReconnectPolicy? reconnectPolicy = null)
        {
            if (transportFactory == null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }

            return new ContractHarness(
                CreateEndpoint(endpointUrl, SecurityPolicies.None, MessageSecurityMode.None),
                reconnectPolicy,
                transportFactory);
        }

        protected static ManagedChannelDiagnostic GetDiagnostic(
            IClientChannelManager manager,
            ManagedChannelKey key)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return manager.GetChannelDiagnostics().Single(diagnostic => diagnostic.Key.Equals(key));
        }

        protected static ManagedChannelDiagnostic AssertSingleDiagnostic(
            IClientChannelManager manager,
            ManagedChannelKey key,
            int expectedRefcount)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            ManagedChannelDiagnostic diagnostic = diagnostics[0];
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Key, Is.EqualTo(key));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(diagnostic.Refcount, Is.EqualTo(expectedRefcount));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(expectedRefcount));
            });
            return diagnostic;
        }

        protected static void AssertDistinctDiagnostics(
            IClientChannelManager manager,
            ManagedChannelKey firstKey,
            ManagedChannelKey secondKey)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
            Assert.That(diagnostics, Has.Count.EqualTo(2));
            Assert.That(diagnostics, Has.Some.Property(nameof(ManagedChannelDiagnostic.Key)).EqualTo(firstKey));
            Assert.That(diagnostics, Has.Some.Property(nameof(ManagedChannelDiagnostic.Key)).EqualTo(secondKey));
            Assert.Multiple(() =>
            {
                foreach (ManagedChannelDiagnostic diagnostic in diagnostics)
                {
                    Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(diagnostic.Refcount, Is.EqualTo(1));
                    Assert.That(diagnostic.ParticipantCount, Is.EqualTo(1));
                }
            });
        }

        protected static async Task AssertCanSendReadRequestAsync(
            IManagedTransportChannel channel,
            CancellationToken ct)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            IServiceResponse response = await channel
                .SendRequestAsync(CreateServerStatusReadRequest(), ct)
                .ConfigureAwait(false);

            Assert.That(response, Is.TypeOf<ReadResponse>());
            var readResponse = (ReadResponse)response;
            Assert.That(StatusCode.IsGood(readResponse.ResponseHeader.ServiceResult), Is.True);
            Assert.That(readResponse.Results, Has.Count.EqualTo(1));
            Assert.That(readResponse.Results[0].IsNull, Is.False);
        }

        protected static ReadRequest CreateServerStatusReadRequest()
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader(),
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                ]
            };
        }

        protected static ValueTask<ParticipantReconnectResult> ReconnectResultAsync(
            ParticipantReconnectResult result)
        {
            return new ValueTask<ParticipantReconnectResult>(result);
        }

        protected static async Task WaitForBarrierArrivalAsync(
            ChaosBarrier barrier,
            CancellationToken ct)
        {
            if (barrier == null)
            {
                throw new ArgumentNullException(nameof(barrier));
            }

            using var timeoutCts = new CancellationTokenSource(DefaultWait);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                ct);

            try
            {
                await barrier.WaitUntilArrivedAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException("The expected barrier participant did not arrive before timeout.");
            }
        }

        protected static ByteString CreateServerCertificateBlob()
        {
            using Certificate certificate = s_factory
                .CreateCertificate("CN=channel-manager-contract-server")
                .CreateForRSA();

            return new ByteString(certificate.RawData);
        }

        protected static ConfiguredEndpoint CreateEndpoint()
        {
            return CreateEndpoint(DefaultEndpointUrl, SecurityPolicies.None, MessageSecurityMode.None);
        }

        protected static ConfiguredEndpoint CreateEndpoint(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("An endpoint id is required.", nameof(id));
            }

            string endpointUrl = Uri.TryCreate(id, UriKind.Absolute, out _)
                ? id
                : $"opc.tcp://localhost:4840/{id}";
            return CreateEndpoint(endpointUrl, SecurityPolicies.None, MessageSecurityMode.None);
        }

        protected static ConfiguredEndpoint CreateEndpoint(
            string endpointUrl,
            string securityPolicyUri,
            MessageSecurityMode securityMode)
        {
            return CreateEndpoint(endpointUrl, securityPolicyUri, securityMode, ByteString.Empty);
        }

        protected static ConfiguredEndpoint CreateEndpoint(
            string endpointUrl,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            ByteString serverCertificate)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                throw new ArgumentException("An endpoint URL is required.", nameof(endpointUrl));
            }
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            var endpointConfiguration = new EndpointConfiguration
            {
                OperationTimeout = 6000
            };
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                TransportProfileUri = Profiles.UaTcpTransport,
                ServerCertificate = serverCertificate,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = endpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(null, description, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
        }

        protected static CertificateChangeEvent CreateApplicationCertificateUpdatedEvent(
            Certificate oldCertificate,
            Certificate newCertificate)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }
            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            return new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                TrustListIdentifier.Peers,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                oldCertificate,
                newCertificate,
                IssuerChain: null);
        }

        protected static async Task<IReadOnlyList<IManagedTransportChannel>> OpenLeasesAsync(
            ClientChannelManager manager,
            IReadOnlyList<FakeParticipant> participants,
            CancellationToken ct)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            var leases = new List<IManagedTransportChannel>(participants.Count);
            try
            {
                for (int index = 0; index < participants.Count; index++)
                {
                    leases.Add(await manager.GetAsync(participants[index], ct).ConfigureAwait(false));
                }
            }
            catch
            {
                await CloseLeasesAsync(leases).ConfigureAwait(false);
                throw;
            }

            return leases;
        }

        protected static async Task CloseLeasesAsync(IReadOnlyList<IManagedTransportChannel> leases)
        {
            if (leases == null)
            {
                throw new ArgumentNullException(nameof(leases));
            }

            for (int index = leases.Count - 1; index >= 0; index--)
            {
                await leases[index].CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        protected static async Task WaitUntilAsync(
            Func<bool> predicate,
            string failureMessage,
            CancellationToken ct)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            if (failureMessage == null)
            {
                throw new ArgumentNullException(nameof(failureMessage));
            }

            DateTime deadline = DateTime.UtcNow.Add(DefaultWait);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail(failureMessage);
                }

                await Task.Delay(DefaultPollInterval, ct).ConfigureAwait(false);
            }
        }

        protected static long GetClientCertificateVersion(ClientChannelManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return s_clientCertificateVersionField.GetValue(manager) is long version
                ? version
                : throw new InvalidOperationException("Client certificate version field did not contain a long value.");
        }

        protected static Task? GetCertificateRotationTask(ClientChannelManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return s_certificateRotationTaskField.GetValue(manager) as Task;
        }

        protected sealed class ContractTestEnvironment : IAsyncDisposable
        {
            public ContractTestEnvironment(
                ApplicationConfiguration configuration,
                Mock<ICertificateManager> certificateManagerMock,
                CertificateChangeSource certificateChanges,
                FakeChannelBindings bindings,
                ClientChannelManager manager)
            {
                Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                CertificateManagerMock = certificateManagerMock ??
                    throw new ArgumentNullException(nameof(certificateManagerMock));
                CertificateChanges = certificateChanges ?? throw new ArgumentNullException(nameof(certificateChanges));
                Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
                Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            }

            public ApplicationConfiguration Configuration { get; }

            public Mock<ICertificateManager> CertificateManagerMock { get; }

            public CertificateChangeSource CertificateChanges { get; }

            public FakeChannelBindings Bindings { get; }

            public ClientChannelManager Manager { get; }

            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
                Bindings.Dispose();
            }
        }

        protected sealed class CertificateChangeSource : IObservable<CertificateChangeEvent>
        {
            public int ObserverCount
            {
                get
                {
                    lock (m_observers)
                    {
                        return m_observers.Count;
                    }
                }
            }

            public IDisposable Subscribe(IObserver<CertificateChangeEvent> observer)
            {
                if (observer == null)
                {
                    throw new ArgumentNullException(nameof(observer));
                }

                lock (m_observers)
                {
                    m_observers.Add(observer);
                }

                return new Subscription(this, observer);
            }

            public void Raise(CertificateChangeEvent evt)
            {
                IObserver<CertificateChangeEvent>[] snapshot;
                lock (m_observers)
                {
                    snapshot = [.. m_observers];
                }

                for (int index = 0; index < snapshot.Length; index++)
                {
                    snapshot[index].OnNext(evt);
                }
            }

            private void Unsubscribe(IObserver<CertificateChangeEvent> observer)
            {
                lock (m_observers)
                {
                    m_observers.Remove(observer);
                }
            }

            private sealed class Subscription : IDisposable
            {
                public Subscription(
                    CertificateChangeSource owner,
                    IObserver<CertificateChangeEvent> observer)
                {
                    m_owner = owner;
                    m_observer = observer;
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                    {
                        return;
                    }

                    m_owner.Unsubscribe(m_observer);
                }

                private readonly CertificateChangeSource m_owner;
                private readonly IObserver<CertificateChangeEvent> m_observer;
                private int m_disposed;
            }

            private readonly List<IObserver<CertificateChangeEvent>> m_observers = [];
        }

        protected static ApplicationConfiguration CreateApplicationConfiguration(ITelemetryContext telemetry)
        {
            return CreateHarnessConfiguration(telemetry);
        }

        internal static ApplicationConfiguration CreateHarnessConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Opc.Ua.Stress.Tests.Channels.Contract",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:Opc.Ua.Stress.Tests.Channels.Contract",
                ProductUri = "urn:localhost:Opc.Ua.Stress.Tests.Channels.Contract",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 6000,
                    MaxMessageSize = 1_048_576,
                    MaxStringLength = 1_048_576,
                    MaxByteStringLength = 1_048_576,
                    MaxArrayLength = 65_535
                }
            };
        }

        internal static ExponentialBackoffChannelReconnectPolicy CreateImmediateHarnessReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                MaxAttempts = 4
            };
        }

        private sealed class ImmediateReconnectPolicy : IChannelReconnectPolicy
        {
            public TimeSpan GetDelay(int attempt)
            {
                return attempt == 0 ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
            }
        }

        private static FieldInfo GetRequiredField(string name)
        {
            return typeof(ClientChannelManager).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"ClientChannelManager private field '{name}' was not found.");
        }

        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;
        private static readonly FieldInfo s_clientCertificateVersionField =
            GetRequiredField("m_clientCertificateVersion");
        private static readonly FieldInfo s_certificateRotationTaskField =
            GetRequiredField("m_certificateRotationTask");
    }

    /// <summary>
    /// Disposable fake-channel harness for contract tests.
    /// </summary>
    public sealed class ContractHarness : IAsyncDisposable
    {
        public ContractHarness(
            ConfiguredEndpoint endpoint,
            IChannelReconnectPolicy? reconnectPolicy,
            Func<string, FakeTransport>? transportFactory)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Bindings = transportFactory != null
                ? new FakeChannelBindings(transportFactory)
                : new FakeChannelBindings(static _ => new FakeTransport());
            Manager = new ClientChannelManager(
                ContractTestBase.CreateHarnessConfiguration(Telemetry),
                Telemetry,
                Bindings,
                reconnectPolicy ?? ContractTestBase.CreateImmediateHarnessReconnectPolicy());
        }

        public ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

        public ConfiguredEndpoint Endpoint { get; }

        public FakeChannelBindings Bindings { get; }

        public ClientChannelManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync().ConfigureAwait(false);
            Bindings.Dispose();
        }
    }
}
