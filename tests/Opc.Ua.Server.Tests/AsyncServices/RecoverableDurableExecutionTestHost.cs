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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Redundancy;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Storage;
using Opc.Ua.TestModels.AsyncExecution;
using Model = Opc.Ua.TestModels.AsyncExecution;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Composes the existing generated-model fixture, real SessionEndpoint and ordinary handlers.
    /// Only transport listening and app-owned qualification are synthetic. No journal API dispatches work.
    /// </summary>
    internal sealed class DurableExecutionTestHost : IAsyncDisposable
    {
        internal const int MessageLimit = 65_536;
        internal static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(20);

        internal static readonly ServiceOperationRecoveryMode[] AllModes =
        [
            ServiceOperationRecoveryMode.Session,
            ServiceOperationRecoveryMode.Owner,
            ServiceOperationRecoveryMode.Durable
        ];

        private DurableExecutionTestHost(
            ServerFixture<DurableExecutionTestServer> fixture, DurableExecutionTestState state)
        {
            m_fixture = fixture;
            State = state;
            m_endpoint = new SessionEndpoint(fixture.Server);
        }

        internal DurableExecutionTestState State { get; }
        internal DurableExecutionTestServer Server => m_fixture.Server;
        internal AsyncServiceExecutionProvider Provider => Server.Provider!;
        internal DurableExecutionPeer First { get; private set; } = null!;

        internal DefaultServerResourceIsolationProvider Isolation =>
            (DefaultServerResourceIsolationProvider)Server.ResourceIsolationProvider!;

        internal ApplicationConfiguration Configuration => m_fixture.Config;

        internal RecoverableExecutionManagerState Manager => Server.CurrentInstance.DiagnosticsNodeManager
            .FindPredefinedNode<RecoverableExecutionManagerState>(Node(RseObjectIds.RecoverableExecutionManager));

        internal static async Task<DurableExecutionTestHost> StartAsync(
            DurableExecutionTestState state, bool enabled = true, bool delay = true,
            ServiceOperationRecoveryMode[]? modes = null, long? ownerExecutionLimit = null,
            Action<DurableExecutionTestServer>? configure = null, bool auditing = false)
        {
            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterListenerFactory(new NoSocketListenerFactory());
            var fixture = new ServerFixture<DurableExecutionTestServer>(telemetry =>
            {
                var server = new DurableExecutionTestServer(telemetry, state, enabled, modes)
                {
                    Scenario = { Delay = delay }
                };
                state.LastServer = server;
                if (ownerExecutionLimit.HasValue)
                {
                    server.ResourceIsolationOptions.Stages =
                    [
                        new ResourceIsolationStageOptions
                        {
                            Stage = ResourceIsolationStage.RequestExecution,
                            OwnerHardLimit = ownerExecutionLimit.Value
                        }
                    ];
                }
                configure?.Invoke(server);
                return server;
            })
            {
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                TransportBindingRegistry = registry
            };
            try
            {
                await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                fixture.Config.TransportQuotas!.MaxMessageSize = MessageLimit;
                fixture.Config.ServerConfiguration!.AuditingEnabled = auditing;
                fixture.Config.ServerConfiguration.UserTokenPolicies =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "durable-synthetic", TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ];
                // A positive metadata-only port avoids the fixture's free-port socket probe.
                DurableExecutionTestServer server = await fixture.StartAsync(port: 1).ConfigureAwait(false);
                server.CurrentInstance.IdentityRegistry.Register(new UserNamePasswordAuthenticator((handler, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (handler.UserName != "operator" ||
                        handler.Token is not UserNameIdentityToken token ||
                        token.Password != ByteString.From(Encoding.UTF8.GetBytes(kSyntheticPassword)))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    return new ValueTask<IUserIdentity>(new SyntheticOwnerIdentity
                    {
                        DisplayName = "durable-synthetic-operator",
                        PolicyId = handler.Token.PolicyId!,
                        TokenHandler = handler.Copy(),
                        Issuer = "urn:synthetic:issuer",
                        Subject = "operator",
                        EffectiveRoles = [ObjectIds.WellKnownRole_AuthenticatedUser, ObjectIds.WellKnownRole_Operator]
                    });
                }));
                if (enabled)
                {
                    // This independent decoder is only a storage oracle. It never admits or restores work.
                    RecoverableExecutionTypeContext types = await new RecoverableExecutionTypeCompatibility(
                        state.Codecs).CaptureAsync(server.MessageContext).ConfigureAwait(false);
                    state.Store.Codec = new RecoverableExecutionRecordCodec(types.MessageContext, types.Witness);
                }
                var host = new DurableExecutionTestHost(fixture, state);
                host.First = await host.AddPeerAsync().ConfigureAwait(false);
                return host;
            }
            catch
            {
                state.Store.ReleaseGates();
                state.Owners.ValidationGate?.Release();
                state.Eligibility.ReleaseGate?.Release();
                state.LastServer?.Scenario.Release();
                await fixture.StopAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task<DurableExecutionPeer> AddPeerAsync(RecoverableExecutionOwnerId? owner = null)
        {
            UserTokenPolicy policy = Server.GetEndpoints().Span.ToArray()
                .SelectMany(endpoint => endpoint.UserIdentityTokens.Span.ToArray())
                .First(candidate => candidate.TokenType == UserTokenType.UserName &&
                    candidate.SecurityPolicyUri == SecurityPolicies.None);
            (RequestHeader header, SecureChannelContext channel) = await Server.CreateAndActivateSessionAsync(
                "durable-in-process-" + Interlocked.Increment(ref m_peer),
                identityToken: new UserNameIdentityToken
                {
                    PolicyId = policy.PolicyId,
                    UserName = "operator",
                    Password = ByteString.From(Encoding.UTF8.GetBytes(kSyntheticPassword))
                }).ConfigureAwait(false);
            var session = (ServerSession)Server.CurrentInstance.SessionManager.GetSession(header.AuthenticationToken)!;
            State.Owners.Set(session.Id, owner ?? DurableExecutionOwners.Owner);
            return new DurableExecutionPeer(header, channel, session);
        }

        internal NodeId Node(ExpandedNodeId id)
        {
            return ExpandedNodeId.ToNodeId(id, Server.MessageContext.NamespaceUris);
        }

        internal SubmitRequestDataType Request(
            string family = "Call", ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Durable,
            double execution = 10_000, double retention = 200, double wait = 0, int value = 5)
        {
            var header = new RequestHeader
            {
                RequestHandle = 71,
                TimeoutHint = 1,
                ReturnDiagnostics = (uint)(DiagnosticsMasks.OperationAll | DiagnosticsMasks.ServiceAll)
            };
            IServiceRequest original = family switch
            {
                "Read" => new ReadRequest
                {
                    RequestHeader = header,
                    TimestampsToReturn = TimestampsToReturn.Both,
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = Node(Model.VariableIds.Probe_Blob), AttributeId = Attributes.Value
                        }
                    ]
                },
                "Write" => new WriteRequest
                {
                    RequestHeader = header,
                    NodesToWrite =
                    [
                        new WriteValue
                        {
                            NodeId = Node(Model.VariableIds.Probe_Counter), AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(value))
                        }
                    ]
                },
                "Call" => new CallRequest
                {
                    RequestHeader = header,
                    MethodsToCall =
                    [
                        new CallMethodRequest
                        {
                            ObjectId = Node(Model.ObjectIds.Probe), MethodId = Node(Model.MethodIds.Probe_DeclaredAdd),
                            InputArguments = [new Variant(value)]
                        }
                    ]
                },
                "QueryFirst" => new QueryFirstRequest { RequestHeader = header },
                "QueryNext" => new QueryNextRequest { RequestHeader = header },
                _ => throw new ArgumentException("Unknown original family.", nameof(family))
            };
            return new SubmitRequestDataType
            {
                ClientOperationId = Uuid.NewUuid(),
                RecoveryMode = mode,
                Request = new ExtensionObject(original),
                ExecutionAllowance = execution,
                ResultRetentionTime = retention,
                WaitTime = wait
            };
        }

        internal Task<DurableExecutionReply<SubmitResponseDataType>> SubmitAsync(
            SubmitRequestDataType request, DurableExecutionPeer? peer = null)
        {
            return InvokeAsync<SubmitResponseDataType>(RseBrowseNames.Submit, request, peer);
        }

        internal Task<DurableExecutionReply<GetResultResponseDataType>> GetAsync(
            ByteString token = default, Uuid clientId = default, DurableExecutionPeer? peer = null,
            ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Durable, double wait = 0)
        {
            return InvokeAsync<GetResultResponseDataType>(RseBrowseNames.GetResult, new GetResultRequestDataType
            {
                RecoveryMode = mode,
                ServerToken = token,
                ClientOperationId = clientId,
                WaitTime = wait
            }, peer);
        }

        internal Task<DurableExecutionReply<ControlResponseDataType>> ControlAsync(
            string method, ByteString token, DurableExecutionPeer? peer = null)
        {
            return InvokeAsync<ControlResponseDataType>(
                method, new OperationControlRequestDataType { ServerToken = token }, peer);
        }

        internal async Task<IServiceResponse> SendAsync(
            string method, IEncodeable argument, DurableExecutionPeer? peer = null, uint? handle = null)
        {
            peer ??= First;
            RecoverableExecutionManagerState manager = Manager;
            MethodState target = method switch
            {
                RseBrowseNames.Submit => manager.Submit!,
                RseBrowseNames.GetResult => manager.GetResult!,
                RseBrowseNames.Acknowledge => manager.Acknowledge!,
                RseBrowseNames.Cancel => manager.Cancel!,
                RseBrowseNames.Abandon => manager.Abandon!,
                _ => throw new ArgumentException("Not a manager Method.", nameof(method))
            };
            RequestHeader header = Header(peer);
            if (handle.HasValue)
            {
                header.RequestHandle = handle.Value;
            }
            return await m_endpoint.ProcessRequestAsync(peer.Channel, new CallRequest
            {
                RequestHeader = header,
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = manager.NodeId, MethodId = target.NodeId,
                        InputArguments = [Variant.FromStructure(argument)]
                    }
                ]
            }, m_watchdog.Token).ConfigureAwait(false);
        }

        internal async Task<uint> CancelExchangeAsync(uint requestHandle)
        {
            IServiceResponse response = await m_endpoint.ProcessRequestAsync(First.Channel, new CancelRequest
            {
                RequestHeader = Header(First),
                RequestHandle = requestHandle
            }, m_watchdog.Token).ConfigureAwait(false);
            try
            {
                Assert.That(response, Is.InstanceOf<CancelResponse>());
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                return ((CancelResponse)response).CancelCount;
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
            }
        }

        internal async Task RejectAsync(
            string method, IEncodeable argument, StatusCode status, DurableExecutionPeer? peer = null)
        {
            IServiceResponse response = await SendAsync(method, argument, peer).ConfigureAwait(false);
            try
            {
                Assert.That(response, Is.InstanceOf<CallResponse>(), "The failure belongs to the manager Method.");
                AssertFailure(response, status);
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
            }
        }

        internal static void AssertFailure(IServiceResponse response, StatusCode status)
        {
            if (response is ServiceFault fault)
            {
                Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(status));
                return;
            }
            Assert.That(response, Is.InstanceOf<CallResponse>());
            var call = (CallResponse)response;
            Assert.That(call.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(call.Results, Has.Count.EqualTo(1));
            Assert.That(call.Results[0].StatusCode, Is.EqualTo(status));
            Assert.That(call.Results[0].OutputArguments, Is.Empty);
        }

        internal async Task FinishAsync()
        {
            Server.Scenario.Release();
            await WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await State.Eligibility.WaitForReleasesAsync(
                State.Eligibility.OwnerAcquisitions + State.Eligibility.AcquiredIds.Count).ConfigureAwait(false);
        }

        internal async Task WaitForUsageAsync(ResourceIsolationStage stage, long expected)
        {
            TaskCompletionSource<bool> reached = DurableExecutionGate.Signal();
            void Changed(ResourceIsolationStage released)
            {
                if (released == stage && Isolation.GetUsage(stage) == expected)
                {
                    reached.TrySetResult(true);
                }
            }
            Isolation.CapacityAvailable += Changed;
            try
            {
                Changed(stage);
                await reached.Task.WaitAsync(Watchdog).ConfigureAwait(false);
                Assert.That(Isolation.GetUsage(stage), Is.EqualTo(expected));
            }
            finally
            {
                Isolation.CapacityAvailable -= Changed;
            }
        }

        internal Task StopAsync()
        {
            return m_stop ??= m_fixture.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            State.Store.ReleaseGates();
            State.Owners.ValidationGate?.Release();
            State.Policy.Gate?.Release();
            State.Eligibility.RecoveryGate?.Release();
            State.Eligibility.ReleaseGate?.Release();
            Server.Scenario.Release();
            try
            {
                await StopAsync().WaitAsync(Watchdog).ConfigureAwait(false);
            }
            finally
            {
                m_watchdog.Dispose();
            }
        }

        private async Task<DurableExecutionReply<T>> InvokeAsync<T>(
            string method, IEncodeable argument, DurableExecutionPeer? peer) where T : class, IEncodeable
        {
            IServiceResponse response = await SendAsync(method, argument, peer).ConfigureAwait(false);
            if (response is not CallResponse call)
            {
                try
                {
                    Assert.Fail("Expected a manager CallResponse, received " + response.ResponseHeader.ServiceResult);
                }
                finally
                {
                    (response as IPooledEncodeable)?.Reuse();
                }
                throw new InvalidOperationException("Unreachable after Assert.Fail.");
            }
            return new DurableExecutionReply<T>(call, Server.MessageContext);
        }

        private RequestHeader Header(DurableExecutionPeer peer)
        {
            return new RequestHeader
            {
                AuthenticationToken = peer.Header.AuthenticationToken,
                RequestHandle = (uint)Interlocked.Increment(ref m_handle),
                Timestamp = State.Clock.Local.GetUtcNow().UtcDateTime,
                TimeoutHint = 15_000
            };
        }

        private sealed class NoSocketListenerFactory : TcpServiceHost
        {
            public override string UriScheme => Utils.UriSchemeOpcTcp;

            public override ITransportListener Create(ITelemetryContext telemetry)
            {
                var listener = new Mock<ITransportListener>();
                listener.SetupGet(value => value.ListenerId).Returns("durable-in-process-no-socket");
                listener.SetupGet(value => value.UriScheme).Returns(Utils.UriSchemeOpcTcp);
                return listener.Object;
            }
        }

        private const string kSyntheticPassword = "RSE-DURABLE-SYNTHETIC-NONPRODUCTION";
        private readonly ServerFixture<DurableExecutionTestServer> m_fixture;
        private readonly SessionEndpoint m_endpoint;
        private readonly CancellationTokenSource m_watchdog = new(Watchdog);
        private Task? m_stop;
        private int m_handle;
        private int m_peer;
    }

    internal sealed record DurableExecutionPeer(
        RequestHeader Header, SecureChannelContext Channel, ServerSession Session);

    /// <summary>
    /// Observes the existing Server audit event route, including initialization before manager publication.
    /// </summary>
    internal sealed class DurableExecutionAuditCapture : IDisposable
    {
        internal ConcurrentQueue<AuditServiceOperationEventState> Events { get; } = new();

        internal void Attach(StandardServer server)
        {
            if (m_server != null)
            {
                throw new InvalidOperationException("An audit capture belongs to one Server lifetime.");
            }
            m_server = server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            m_server.EventReported += Observe;
        }

        public void Dispose()
        {
            // Keep the ownership reset in the same guarded cleanup block as unsubscription.
            // TODO: remove this suppression when IDE0031 preserves the guarded assignment.
#pragma warning disable IDE0031
            if (m_server != null)
            {
                m_server.EventReported -= Observe;
                m_server = null;
            }
#pragma warning restore IDE0031
        }

        private void Observe(ISystemContext context, NodeState sender, IFilterTarget value)
        {
            if (value is AuditServiceOperationEventState audit)
            {
                Events.Enqueue(audit);
            }
        }

        private ServerObjectState? m_server;
    }

    internal sealed class DurableExecutionReply<T> : IDisposable where T : class, IEncodeable
    {
        internal DurableExecutionReply(CallResponse response, IServiceMessageContext context)
        {
            m_response = response;
            try
            {
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].OutputArguments, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out ExtensionObject body), Is.True);
                Assert.That(body.TryGetValue(out T? value, context), Is.True);
                Value = value!;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal T Value { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref m_response, null)?.Reuse();
        }

        private CallResponse? m_response;
    }

    internal sealed class DurableExecutionTestServer : StandardServer
    {
        internal DurableExecutionTestServer(
            ITelemetryContext telemetry, DurableExecutionTestState state, bool enabled,
            ServiceOperationRecoveryMode[]? modes)
            : base(telemetry, state.Clock.Local)
        {
            m_state = state;
            AsyncServices.Enabled = enabled;
            if (enabled || modes != null)
            {
                AsyncServices.RecoveryModes = modes ?? DurableExecutionTestHost.AllModes;
            }
            AsyncServiceExecutionProviderFactory = new Factory(this, state);
            AddNodeManager(new AsyncExecutionScenarioFactory(Scenario));
        }

        internal AsyncServiceExecutionProvider? Provider { get; private set; }

        internal AsyncExecutionScenario Scenario { get; } = new()
        {
            Delay = true,
            IgnoreCancellation = true,
            RecoverableHint = true,
            Blob = ByteString.From([3, 1, 4, 1, 5])
        };

        internal int Dispatches => Volatile.Read(ref m_dispatches);
        internal ConcurrentQueue<DurableExecutionWrite[]> DispatchCommits { get; } = new();
        internal StatusCode? OwnedValidationFailure { get; set; }
        internal OperationContext? OwnedContext { get; private set; }
        internal TaskCompletionSource<bool> DispatchEntered { get; } = DurableExecutionGate.Signal();

        internal Task WaitForDispatchesAsync(int count)
        {
            TaskCompletionSource<bool> signal = m_dispatchCounts.GetOrAdd(count, _ => DurableExecutionGate.Signal());
            if (Dispatches >= count)
            {
                signal.TrySetResult(true);
            }
            return signal.Task.WaitAsync(DurableExecutionTestHost.Watchdog);
        }

        protected override async ValueTask OnRequestValidatedAsync(OperationContext context)
        {
            if (context.IsOwnedInvocation)
            {
                OwnedContext = context;
                // Preserve ConcurrentQueue.ToArray's explicit snapshot for dispatch evidence.
                // TODO: reconsider once collection-spread snapshot semantics are pinned across supported TFMs.
#pragma warning disable IDE0305
                DispatchCommits.Enqueue(m_state.Store.ConfirmedWrites.ToArray());
#pragma warning restore IDE0305
                int count = Interlocked.Increment(ref m_dispatches);
                if (m_dispatchCounts.TryGetValue(count, out TaskCompletionSource<bool>? signal))
                {
                    signal.TrySetResult(true);
                }
                DispatchEntered.TrySetResult(true);
                if (OwnedValidationFailure is { } failure)
                {
                    throw new ServiceResultException(failure);
                }
            }
            await base.OnRequestValidatedAsync(context).ConfigureAwait(false);
        }

        private sealed class Factory(DurableExecutionTestServer owner, DurableExecutionTestState state) :
            IAsyncServiceExecutionProviderFactory
        {
            public IAsyncServiceExecutionProvider Create(
                StandardServer server, ApplicationConfiguration configuration,
                AsyncServiceExecutionOptions options, TimeProvider timeProvider)
            {
                // Server startup replaces constructor telemetry with the configuration's message context.
                // Observe the exact logger factory the provider uses, including its background continuations.
                server.MessageContext.Telemetry.LoggerFactory.AddProvider(state.Logs);
                // A stable custom registration makes codec qualification/revocation observable even when
                // the generated scenario happens to declare only built-in argument types.
                Assert.That(server.MessageContext.Factory.TryGetEncodeableType(
                    DataTypeIds.ReadResponse, out IEncodeableType? codec), Is.True);
                server.MessageContext.Factory.Builder.AddEncodeableType(
                    DurableExecutionTestState.CodecAlias, codec!).Commit();
                state.BeforeProviderCreate?.Invoke(server);
                owner.Provider = new AsyncServiceExecutionProvider(
                    server, configuration, options, timeProvider, state.Policy, state.Owners,
                    state.Eligibility, state.Store, state.Protector, state.Clock, state.Codecs);
                return owner.Provider;
            }
        }

        private readonly DurableExecutionTestState m_state;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> m_dispatchCounts = new();
        private int m_dispatches;
    }

    /// <summary>
    /// Owns dependencies that survive an in-process Server replacement. Forks copy only actually committed bytes.
    /// Memory qualification is a deterministic test seam, not a claim of physical restart durability.
    /// </summary>
    internal sealed class DurableExecutionTestState : IAsyncDisposable
    {
        internal const int ProtectedSlotBytes = (4 * DurableExecutionTestHost.MessageLimit) + 8192 + 4096 + 8;
        internal const int HistorySlotBudget = 8192;
        internal const int SnapshotOverheadBudget = 4096;

        internal static readonly ExpandedNodeId CodecAlias = new(
            "durable-test-qualified-response-alias", RseNamespaces.Rse);

        internal DurableExecutionTestState(
            string? ownedFileParent = null,
            int maximumSnapshotBytes = ProtectedSlotBytes + HistorySlotBudget + SnapshotOverheadBudget)
            : this(NewKey(), new DurableExecutionClock(), ownedFileParent, maximumSnapshotBytes)
        {
        }

        private DurableExecutionTestState(
            byte[] key, DurableExecutionClock clock, string? ownedFileParent, int maximumSnapshotBytes)
        {
            if (ownedFileParent != null)
            {
                // Each file-store test supplies a unique parent and deletes only that parent in its finally.
                // The real store synchronizes this owned directory, never the shared system temp directory.
                Directory.CreateDirectory(ownedFileParent);
                m_fileDirectory = Path.Combine(ownedFileParent, "store");
            }
            m_key = [.. key];
            m_maximumSnapshotBytes = maximumSnapshotBytes;
            Clock = clock;
            Protector = new AesCbcHmacRecordProtector(m_key);
            m_backend = CreateBackend();
            Store = new DurableExecutionStore(m_backend, Protector);
        }

        internal DurableExecutionClock Clock { get; }
        internal DurableExecutionOwners Owners { get; } = new();
        internal DurableExecutionEligibility Eligibility { get; } = new();
        internal DurableExecutionPolicy Policy { get; } = new();
        internal DurableExecutionCodecs Codecs { get; } = new();
        internal DurableExecutionLogs Logs { get; } = new();
        internal AesCbcHmacRecordProtector Protector { get; }
        internal DurableExecutionStore Store { get; private set; }
        internal DurableExecutionTestServer? LastServer { get; set; }
        internal Action<StandardServer>? BeforeProviderCreate { get; set; }

        internal async Task<DurableExecutionTestState> ForkAsync(
            IReadOnlyList<KeyValuePair<string, ByteString>> image)
        {
            var next = new DurableExecutionTestState(m_key, Clock.Fork(), null, m_maximumSnapshotBytes);
            next.Store.Codec = Store.Codec;
            next.Codecs.Revision = Codecs.Revision;
            try
            {
                foreach (KeyValuePair<string, ByteString> item in image)
                {
                    await next.m_backend.SetAsync(item.Key, item.Value).ConfigureAwait(false);
                }
                return next;
            }
            catch
            {
                await next.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task ReopenFileAsync()
        {
            Assert.That(m_fileDirectory, Is.Not.Null, "Only the file-backed composition can be reopened.");
            RecoverableExecutionRecordCodec? codec = Store.Codec;
            await DisposeBackendAsync().ConfigureAwait(false);
            m_backend = CreateBackend();
            Store = new DurableExecutionStore(m_backend, Protector) { Codec = codec };
        }

        public async ValueTask DisposeAsync()
        {
            Store.ReleaseGates();
            Owners.ValidationGate?.Release();
            Eligibility.RecoveryGate?.Release();
            Eligibility.ReleaseGate?.Release();
            Policy.Gate?.Release();
            await DisposeBackendAsync().ConfigureAwait(false);
            Protector.Dispose();
            Logs.Dispose();
            Array.Clear(m_key, 0, m_key.Length);
        }

        private ISharedKeyValueStore CreateBackend()
        {
            return m_fileDirectory == null
                ? new InMemorySharedKeyValueStore()
                : new FileSharedKeyValueStore(
                    m_fileDirectory, "rse-durable-composition-test", Protector, m_maximumSnapshotBytes);
        }

        private static byte[] NewKey()
        {
            byte[] key = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(key);
            return key;
        }

        private async ValueTask DisposeBackendAsync()
        {
            if (m_backend is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                (m_backend as IDisposable)?.Dispose();
            }
        }

        private readonly byte[] m_key;
        private readonly string? m_fileDirectory;
        private readonly int m_maximumSnapshotBytes;
        private ISharedKeyValueStore m_backend;
    }

    internal sealed class DurableExecutionClock : IRecoverableExecutionClock
    {
        internal DurableExecutionClock(DateTimeOffset? localStart = null)
        {
            // Session transport timestamps have a normal UTC origin; all asserted durable elapsed time
            // uses the fixed domain/tick coordinate below, never wall-clock passage.
            Local = new DurableExecutionLocalClock(localStart ?? DateTimeOffset.UtcNow);
        }

        internal DurableExecutionLocalClock Local { get; }
        internal bool Available { get; set; } = true;
        internal RecoverableExecutionTime Now => new(m_domain, Interlocked.Read(ref m_ticks));

        internal void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref m_ticks, elapsed.Ticks);
            Local.Advance(elapsed);
        }

        internal DurableExecutionClock Fork()
        {
            return new DurableExecutionClock(Local.GetUtcNow())
            {
                m_domain = m_domain,
                m_ticks = Interlocked.Read(ref m_ticks)
            };
        }

        public ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Available ? new ValueTask<RecoverableExecutionTime>(Now) :
                throw new IOException("Synthetic qualified time is unavailable.");
        }

        private Uuid m_domain = new("77000000-0000-0000-0000-000000000001");
        private long m_ticks = TimeSpan.TicksPerSecond;
    }

    internal sealed class DurableExecutionLocalClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        internal Action<TimeSpan>? TimerCreated { get; set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            TimerCreated?.Invoke(dueTime);
            return timer;
        }
    }

    internal sealed class DurableExecutionOwners : IRecoverableExecutionOwnerProvider
    {
        internal static readonly RecoverableExecutionOwnerId Owner = new(
            new Uuid("71000000-0000-0000-0000-000000000001"),
            new Uuid("72000000-0000-0000-0000-000000000001"),
            new Uuid("73000000-0000-0000-0000-000000000001"));

        internal StatusCode? Failure { get; set; }
        internal DurableExecutionGate? ValidationGate { get; set; }
        internal int Validations => Volatile.Read(ref m_validations);

        internal void Set(NodeId session, RecoverableExecutionOwnerId owner)
        {
            m_owners[session] = owner;
        }

        public ValueTask<RecoverableExecutionOwner> ResolveAsync(
            OperationContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } error)
            {
                throw new ServiceResultException(error);
            }
            if (!m_owners.TryGetValue(context.SessionId, out RecoverableExecutionOwnerId owner))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return new ValueTask<RecoverableExecutionOwner>(
                new RecoverableExecutionOwner(owner, ByteString.From([7, 1])));
        }

        public ValueTask ValidatePersistedOwnerAsync(
            RecoverableExecutionOwner owner, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_validations);
            if (Failure is { } error)
            {
                throw new ServiceResultException(error);
            }
            if (owner.ContinuityContext != ByteString.From([7, 1]))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return ValidationGate == null ? default : new ValueTask(ValidationGate.HoldAsync());
        }

        private readonly ConcurrentDictionary<NodeId, RecoverableExecutionOwnerId> m_owners = new();
        private int m_validations;
    }

    internal sealed class DurableExecutionEligibility : IRecoverableExecutionDurableEligibilityProvider
    {
        internal bool TerminationEstablished { get; set; } = true;
        internal DurableExecutionGate? RecoveryGate { get; set; }
        internal DurableExecutionGate? ReleaseGate { get; set; }
        internal bool FailRelease { get; set; }
        internal StatusCode? OwnerRejection { get; set; }
        internal ConcurrentQueue<Uuid> AcquiredIds { get; } = new();
        internal ConcurrentQueue<Uuid> RecoveredIds { get; } = new();
        internal int OwnerAcquisitions => Volatile.Read(ref m_ownerAcquisitions);
        internal int Releases => Volatile.Read(ref m_releases);
        internal int ReleaseCalls => Volatile.Read(ref m_releaseCalls);
        internal int RecoveryReleases => Volatile.Read(ref m_recoveryReleases);
        internal int ActiveFences => RecoveredIds.Count - RecoveryReleases;

        internal Task WaitForReleasesAsync(int count)
        {
            TaskCompletionSource<bool> signal = m_releaseCounts.GetOrAdd(count, _ => DurableExecutionGate.Signal());
            if (Releases >= count)
            {
                signal.TrySetResult(true);
            }
            return signal.Task.WaitAsync(DurableExecutionTestHost.Watchdog);
        }

        public ValueTask<IAsyncDisposable> AcquireAsync(
            OperationContext context, IServiceRequest request, ServiceOperationRecoveryMode mode,
            RecoverableExecutionOwner owner, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(mode, Is.EqualTo(ServiceOperationRecoveryMode.Owner),
                "Durable must acquire an execution-identified reservation, not ordinary eligibility.");
            if (OwnerRejection is { } rejection)
            {
                throw new ServiceResultException(rejection);
            }
            Interlocked.Increment(ref m_ownerAcquisitions);
            return new ValueTask<IAsyncDisposable>(new Reservation(ReleaseAsync));
        }

        public ValueTask<IAsyncDisposable> AcquireDurableAsync(
            OperationContext context, IServiceRequest request, RecoverableExecutionOwner owner,
            Uuid executionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(executionId, Is.Not.EqualTo(Uuid.Empty));
            AcquiredIds.Enqueue(executionId);
            return new ValueTask<IAsyncDisposable>(new Reservation(ReleaseAsync));
        }

        public async ValueTask<RecoverableExecutionRecoveredWork> RecoverAsync(
            RecoverableExecutionOwner owner, Uuid executionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoveredIds.Enqueue(executionId);
            if (RecoveryGate != null)
            {
                await RecoveryGate.HoldAsync().ConfigureAwait(false);
            }
            return new RecoverableExecutionRecoveredWork(
                new Reservation(() =>
                {
                    Interlocked.Increment(ref m_recoveryReleases);
                    return default;
                }), TerminationEstablished);
        }

        private sealed class Reservation(Func<ValueTask> release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                // Count every call so double disposal is visible to the test, not hidden by an idempotent fake.
                return release();
            }
        }

        private async ValueTask ReleaseAsync()
        {
            Interlocked.Increment(ref m_releaseCalls);
            if (ReleaseGate != null)
            {
                await ReleaseGate.HoldAsync().ConfigureAwait(false);
            }
            if (FailRelease)
            {
                throw new IOException("Synthetic asynchronous eligibility disposal failure.");
            }
            int count = Interlocked.Increment(ref m_releases);
            if (m_releaseCounts.TryGetValue(count, out TaskCompletionSource<bool>? signal))
            {
                signal.TrySetResult(true);
            }
        }

        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> m_releaseCounts = new();
        private int m_ownerAcquisitions;
        private int m_releaseCalls;
        private int m_releases;
        private int m_recoveryReleases;
    }

    internal sealed class DurableExecutionPolicy : IAsyncServiceAuthorizationPolicy
    {
        internal AsyncServiceAccess? Denied { get; set; }
        internal AsyncServiceAccess? Gated { get; set; }
        internal DurableExecutionGate? Gate { get; set; }
        internal TaskCompletionSource<bool>? CollectionEntered { get; set; }
        internal ConcurrentQueue<(NodeId Session, AsyncServiceAccess Access)> Checks { get; } = new();

        public async ValueTask AuthorizeAsync(
            OperationContext context, Uuid clientOperationId, NodeId serviceId,
            AsyncServiceAccess access, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Checks.Enqueue((context.SessionId, access));
            if (access == AsyncServiceAccess.Collect)
            {
                CollectionEntered?.TrySetResult(true);
            }
            if (Gated == access && Gate != null)
            {
                await Gate.HoldAsync().ConfigureAwait(false);
            }
            if (Denied == access)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }
    }

    internal sealed class DurableExecutionCodecs : IRecoverableExecutionCodecQualifier
    {
        internal ByteString Revision { get; set; } = ByteString.From([0x44, 0x55, 1]);
        internal int Calls => Volatile.Read(ref m_calls);

        public ValueTask<ByteString> GetRevisionAsync(
            ExpandedNodeId registrationId, IType codec, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_calls);
            return new ValueTask<ByteString>(Revision);
        }

        private int m_calls;
    }

    /// <summary>
    /// Observes the readiness-withdrawal event after invalidation, so failure tests need no scheduling guesses.
    /// No message, owner evidence, token, payload or credential is retained by this observer.
    /// </summary>
    internal sealed class DurableExecutionLogs : ILoggerProvider
    {
        internal TaskCompletionSource<bool> Unavailable { get; } = DurableExecutionGate.Signal();

        public ILogger CreateLogger(string categoryName)
        {
            return new Observer(this);
        }

        public void Dispose()
        {
        }

        private sealed class Observer(DurableExecutionLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (eventId.Name == "RecoverableDurabilityUnavailable")
                {
                    owner.Unavailable.TrySetResult(true);
                }
            }
        }
    }

    internal sealed class DurableExecutionGate : IDisposable
    {
        internal TaskCompletionSource<bool> Entered { get; } = Signal();

        internal async Task HoldAsync()
        {
            Entered.TrySetResult(true);
            await m_release.Task.WaitAsync(DurableExecutionTestHost.Watchdog).ConfigureAwait(false);
        }

        internal void Release()
        {
            m_release.TrySetResult(true);
        }

        public void Dispose()
        {
            Release();
        }

        internal static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly TaskCompletionSource<bool> m_release = Signal();
    }

    internal sealed record DurableExecutionWrite(
        string Key, ByteString Bytes, RecoverableExecutionDurableRecord? Record,
        RecoverableExecutionDurableRecord? Previous);

    /// <summary>
    /// Faults are selected by provider-written phase, not by a fragile total CAS count.
    /// A thrown post-CAS error deliberately means the write may have committed without confirmation.
    /// </summary>
    internal sealed class DurableExecutionStore(ISharedKeyValueStore inner, IRecordProtector protector) :
        ISharedKeyValueStore, ISharedKeyValueStoreConsistency,
        ISharedKeyValueStoreDurability, ISharedKeyValueStoreCapacity
    {
        internal RecoverableExecutionRecordCodec? Codec { get; set; }
        internal bool Available { get; set; } = true;
        internal bool Linearizable { get; set; } = true;
        internal Func<RecoverableExecutionDurableRecord, bool>? FailWrite { get; set; }
        internal bool FailAfterCommit { get; set; }
        internal int FailScanAt { get; set; }
        internal int GateScanAt { get; set; }
        internal DurableExecutionGate? ScanGate { get; set; }
        internal ConcurrentQueue<DurableExecutionWrite> Writes { get; } = new();
        internal ConcurrentQueue<DurableExecutionWrite> ConfirmedWrites { get; } = new();
        internal int Attempts => Volatile.Read(ref m_attempts);
        internal int Failures => Volatile.Read(ref m_failures);
        internal int CapacityRejections => Volatile.Read(ref m_capacityRejections);

        internal DurableExecutionGate Gate(
            Func<RecoverableExecutionDurableRecord, bool> predicate, bool afterCommit = false)
        {
            var gate = new DurableExecutionGate();
            m_gates.Add(new WriteGate(predicate, afterCommit, gate));
            return gate;
        }

        internal void ReleaseGates()
        {
            foreach (WriteGate gate in m_gates)
            {
                gate.Gate.Release();
            }
            ScanGate?.Release();
        }

        internal async Task<IReadOnlyList<KeyValuePair<string, ByteString>>> ImageAsync()
        {
            var image = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> item in inner.ScanAsync("rse/server/")
                .ConfigureAwait(false))
            {
                image.Add(new KeyValuePair<string, ByteString>(item.Key, item.Value.Copy()));
            }
            return (KeyValuePair<string, ByteString>[])[.. image.OrderBy(item => item.Key, StringComparer.Ordinal)];
        }

        internal async Task<RecoverableExecutionDurableRecord> RecordAsync(Uuid clientId)
        {
            IReadOnlyList<KeyValuePair<string, ByteString>> image = await ImageAsync().ConfigureAwait(false);
            return image.Select(item => Decode(item.Key, item.Value)!).Single(record => record.ClientId == clientId);
        }

        internal RecoverableExecutionDurableRecord? Decode(string key, ByteString bytes)
        {
            if (bytes.IsNull)
            {
                return null;
            }
            return (Codec ?? throw new InvalidOperationException("The test's storage oracle has not been captured."))
                .Unprotect(bytes, protector, RecordProtectionContext.Create("OpcUa.RSE.Server/0.4/v1", key));
        }

        public async ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Available)
            {
                throw new IOException("Synthetic loss of durable storage qualification.");
            }
            if (inner is ISharedKeyValueStoreDurability durable)
            {
                await durable.VerifyDurabilityAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public bool IsLinearizable(string key)
        {
            return Linearizable;
        }

        public bool IsProcessLocal(string key)
        {
            return true;
        }

        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return inner.TryGetAsync(key, ct);
        }

        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            return inner.SetAsync(key, value, ct);
        }

        public async ValueTask<bool> CompareAndSwapAsync(
            string key, ByteString expected, ByteString value, CancellationToken ct = default)
        {
            return await WriteAsync(key, expected, value, capacityAware: false, ct).ConfigureAwait(false) ==
                SharedKeyValueStoreWriteResult.Applied;
        }

        public ValueTask<SharedKeyValueStoreWriteResult> TryCompareAndSwapAsync(
            string key, ByteString expected, ByteString value, CancellationToken cancellationToken = default)
        {
            return WriteAsync(key, expected, value, capacityAware: true, cancellationToken);
        }

        private async ValueTask<SharedKeyValueStoreWriteResult> WriteAsync(
            string key, ByteString expected, ByteString value, bool capacityAware, CancellationToken ct)
        {
            Interlocked.Increment(ref m_attempts);
            RecoverableExecutionDurableRecord? record = Decode(key, value);
            RecoverableExecutionDurableRecord? previous = Decode(key, expected);
            if (record != null)
            {
                await EnterGateAsync(record, afterCommit: false).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            bool fail = record != null && FailWrite?.Invoke(record) == true;
            if (fail && !FailAfterCommit)
            {
                Interlocked.Increment(ref m_failures);
                throw new IOException("Synthetic unconfirmed write before publication.");
            }
            SharedKeyValueStoreWriteResult result = capacityAware && inner is ISharedKeyValueStoreCapacity capacity
                ? await capacity.TryCompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false)
                : await inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false)
                    ? SharedKeyValueStoreWriteResult.Applied : SharedKeyValueStoreWriteResult.ConditionNotMet;
            bool committed = result == SharedKeyValueStoreWriteResult.Applied;
            if (result == SharedKeyValueStoreWriteResult.CapacityRejected)
            {
                Interlocked.Increment(ref m_capacityRejections);
            }
            if (committed)
            {
                Writes.Enqueue(new DurableExecutionWrite(key, value.Copy(), record, previous));
            }
            if (committed && record != null)
            {
                await EnterGateAsync(record, afterCommit: true).ConfigureAwait(false);
            }
            if (committed && fail)
            {
                Interlocked.Increment(ref m_failures);
                throw new IOException("Synthetic lost commit confirmation after publication.");
            }
            if (committed)
            {
                ConfirmedWrites.Enqueue(new DurableExecutionWrite(key, value.Copy(), record, previous));
            }
            return result;
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return inner.DeleteAsync(key, ct);
        }

        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
        {
            int index = 0;
            await foreach (KeyValuePair<string, ByteString> item in inner.ScanAsync(keyPrefix, ct)
                .ConfigureAwait(false))
            {
                index++;
                if (index == GateScanAt && ScanGate != null)
                {
                    await ScanGate.HoldAsync().ConfigureAwait(false);
                }
                if (index == FailScanAt)
                {
                    throw new IOException("Synthetic later-record load failure.");
                }
                yield return item;
            }
        }

        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            return inner.WatchAsync(keyPrefix, ct);
        }

        private async Task EnterGateAsync(RecoverableExecutionDurableRecord record, bool afterCommit)
        {
            foreach (WriteGate gate in m_gates)
            {
                if (gate.AfterCommit == afterCommit &&
                    gate.Predicate(record) &&
                    Interlocked.CompareExchange(ref gate.Used, 1, 0) == 0)
                {
                    await gate.Gate.HoldAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class WriteGate(
            Func<RecoverableExecutionDurableRecord, bool> predicate, bool afterCommit, DurableExecutionGate gate)
        {
            internal Func<RecoverableExecutionDurableRecord, bool> Predicate { get; } = predicate;
            internal bool AfterCommit { get; } = afterCommit;
            internal DurableExecutionGate Gate { get; } = gate;
            internal int Used;
        }

        private readonly List<WriteGate> m_gates = [];
        private int m_attempts;
        private int m_failures;
        private int m_capacityRejections;
    }
}
