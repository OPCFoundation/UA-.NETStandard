/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Storage;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Internal supervised child mode. Never accepts a store path, key or credentials from arguments.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ProcessRestartHost
    {
        internal static async Task RunAsync(string pipeName, int supervisorPid, CancellationToken ct)
        {
            NamedPipeClientStream authorityStream = await RestartPipe.ConnectAsync(
                pipeName + "-authority", supervisorPid, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable authorityStreamLifetime = authorityStream.ConfigureAwait(false);
            var authority = new RestartPipe(authorityStream);
            await using ConfiguredAsyncDisposable authorityLifetime = authority.ConfigureAwait(false);
            NamedPipeClientStream commands = await RestartPipe.ConnectAsync(
                pipeName + "-commands", supervisorPid, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable commandsLifetime = commands.ConfigureAwait(false);
            byte[] bootstrap = await authority.ExchangeAsync(RestartCommand.Bootstrap, null, ct).ConfigureAwait(false);
            RestartConfiguration configuration;
            RestartSecrets secrets;
            try
            {
                using BinaryReader reader = RestartPipe.Reader(bootstrap, reply: true);
                configuration = RestartConfiguration.Read(reader);
                secrets = await RestartSecrets.ReadAsync(reader, ct).ConfigureAwait(false);
                RestartPipe.End(reader);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bootstrap);
            }
            await using ConfiguredAsyncDisposable secretLifetime = secrets.ConfigureAwait(false);
            using AesCbcHmacRecordProtector protector = secrets.CreateProtector();
            var store = new FileSharedKeyValueStore(
                Path.Combine(configuration.Root, "journal"), configuration.ServerUri + "/journal",
                protector, RestartConfiguration.JournalCapacity);
            await using ConfiguredAsyncDisposable storeLifetime = store.ConfigureAwait(false);
            await store.VerifyDurabilityAsync(ct).ConfigureAwait(false);
            using var clock = new SupervisorClockProxy(authority, configuration.TimeDomain, ct);
            _ = await clock.GetTimeAsync(ct).ConfigureAwait(false);
            var work = new RestartWork(store, authority, configuration.Owner);
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using var telemetryLifetime = telemetry as IDisposable;
            ApplicationInstance application = await RestartSecurity.CreateApplicationAsync(
                "RseRestartTestServer", configuration.ServerUri, Path.Combine(configuration.Root, "server-pki"),
                secrets, telemetry, configuration.EndpointUrl, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable applicationLifetime = application.ConfigureAwait(false);
            using (CertificateEntry certificate = RestartSecurity.AcquireCertificate(application))
            {
                RestartPipe.Require(
                    RestartSecurity.Hash(certificate.Certificate.RawData) == configuration.ServerCertificateHash,
                    "The restarted Server certificate changed; no continuity was authorized.");
            }
            var owners = new ConfiguredRecoverableExecutionOwnerProvider(
                configuration.Owner.AuthorityId,
                [new RecoverableExecutionApplicationMapping(
                    configuration.Owner.ApplicationId, configuration.ClientCertificateHash)],
                [new RecoverableExecutionUserMapping(
                    configuration.Owner.UserId, configuration.UserIssuer, configuration.UserSubject)]);
            // ApplicationInstance owns the successfully started Server, exactly as in the original sample.
            // TODO: Remove when CA2000 models StartAsync's successful ownership transfer.
#pragma warning disable CA2000
            var server = new RestartServer(telemetry, application.ApplicationConfiguration!.CertificateManager!)
            {
                AsyncServices = new AsyncServiceExecutionOptions
                {
                    Enabled = true,
                    RecoveryModes =
                    [
                        ServiceOperationRecoveryMode.Session,
                        ServiceOperationRecoveryMode.Owner,
                        ServiceOperationRecoveryMode.Durable
                    ]
                },
                AsyncServiceExecutionProviderFactory = new DefaultAsyncServiceExecutionProviderFactory(
                    ownerProvider: owners, eligibilityProvider: work, durableStore: store,
                    recordProtector: protector, durableClock: clock)
            };
#pragma warning restore CA2000
            try
            {
                server.AddNodeManager(new RestartNodeManagerFactory(work));
                await application.StartAsync(server, ct).ConfigureAwait(false);
            }
            catch
            {
                if (!ReferenceEquals(application.Server, server))
                {
                    server.Dispose();
                }
                throw;
            }
            // StartAsync includes journal restoration and manager installation;
            // socket availability alone is not readiness.
            await authority.SendAsync(RestartCommand.Ready, null, ct).ConfigureAwait(false);
            await RunCommandsAsync(commands, work, clock, ct).ConfigureAwait(false);
        }

        private static async Task RunCommandsAsync(
            Stream commands, RestartWork work, SupervisorClockProxy clock, CancellationToken ct)
        {
            while (true)
            {
                byte[] request = await RestartPipe.ReadAsync(commands, ct).ConfigureAwait(false);
                try
                {
                    using BinaryReader reader = RestartPipe.Reader(request);
                    switch ((RestartCommand)reader.ReadByte())
                    {
                        case RestartCommand.Arm:
                            int amount = reader.ReadInt32();
                            RestartPipe.End(reader);
                            await work.ArmAsync(amount, ct).ConfigureAwait(false);
                            break;
                        case RestartCommand.Release:
                            RestartPipe.End(reader);
                            work.Release();
                            break;
                        case RestartCommand.Snapshot:
                            RestartPipe.End(reader);
                            RestartSnapshot snapshot = await work.ReadSnapshotAsync(ct).ConfigureAwait(false);
                            await RestartPipe.WriteAsync(commands, writer =>
                            {
                                writer.Write((byte)1);
                                writer.Write(snapshot.Invocations);
                                writer.Write(snapshot.Value);
                                writer.Write(work.DispatchAttempts);
                            }, ct).ConfigureAwait(false);
                            continue;
                        case RestartCommand.RequireUnavailableTime:
                            RestartPipe.End(reader);
                            bool rejected = false;
                            try
                            {
                                _ = await clock.GetTimeAsync(ct).ConfigureAwait(false);
                            }
                            catch (ServiceResultException exception)
                                when (exception.StatusCode == StatusCodes.BadResourceUnavailable)
                            {
                                rejected = true;
                            }
                            RestartPipe.Require(rejected,
                                "Lost/changed supervisor time returned a usable/default coordinate " +
                                "instead of failing closed.");
                            break;
                        case RestartCommand.Stop:
                            RestartPipe.End(reader);
                            await RestartPipe.WriteAsync(commands, writer => writer.Write((byte)1), ct)
                                .ConfigureAwait(false);
                            return;
                        default:
                            throw new InvalidOperationException("Unknown private child command.");
                    }
                    await RestartPipe.WriteAsync(commands, writer => writer.Write((byte)1), ct).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(request);
                }
            }
        }

        private sealed class RestartServer(ITelemetryContext telemetry, ICertificateValidatorEx certificates)
            : StandardServer(telemetry)
        {
            protected override void OnServerStarted(IServerInternal server)
            {
                base.OnServerStarted(server);
                // Use the built-in validator-backed identity, including its validated IIdentityClaims.
                server.IdentityRegistry.Register(new X509Authenticator(certificates));
            }
        }

        private sealed class RestartNodeManagerFactory(RestartWork work) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [DemoWork.NamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // StandardServer owns the returned manager.
                // TODO: Remove when CA2000 models the ValueTask factory ownership transfer.
#pragma warning disable CA2000
                IAsyncNodeManager manager = new DemoNodeManager(server, configuration) { RestartWork = work };
#pragma warning restore CA2000
                return new ValueTask<IAsyncNodeManager>(manager);
            }
        }
    }

    /// <summary>
    /// Reads an explicitly supplied time authority; authority loss, domain changes and rollback are latched failures.
    /// There is deliberately no UTC, child Stopwatch, cache fallback or default restart clock.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SupervisorClockProxy(
        RestartPipe authority, Uuid expectedDomain, CancellationToken lifetime)
        : IRecoverableExecutionClock, IDisposable
    {
        public async ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (m_lost)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                byte[] reply = await authority.ExchangeAsync(RestartCommand.Time, null, linked.Token)
                    .ConfigureAwait(false);
                try
                {
                    using BinaryReader reader = RestartPipe.Reader(reply, reply: true);
                    var time = new RecoverableExecutionTime(RestartPipe.ReadId(reader), reader.ReadInt64());
                    RestartPipe.End(reader);
                    _ = m_last.ElapsedUntil(time);
                    m_last = time;
                    return time;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(reply);
                }
            }
            catch (Exception exception)
            {
                m_lost = true;
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable, exception);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public void Dispose()
        {
            m_gate.Dispose();
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private RecoverableExecutionTime m_last = new(expectedDomain, 0);
        private bool m_lost;
    }

    /// <summary>
    /// Closed qualification for this one generated Method. No external devices, subprocesses, untracked Tasks,
    /// callbacks after return or Session resources exist in this handler. It cannot resume from a saved request.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartWork(
        FileSharedKeyValueStore store, RestartPipe authority, RecoverableExecutionOwnerId expectedOwner)
        : IRecoverableExecutionDurableEligibilityProvider
    {
        internal int DispatchAttempts => Volatile.Read(ref m_dispatchAttempts);

        internal void Bind(NodeState device, MethodState method)
        {
            RestartPipe.Require(m_method == null, "The TEST Method binding is immutable.");
            m_deviceId = device.NodeId;
            m_method = method;
            AssertExactAliasQualification(m_deviceId, method.NodeId, method.MethodDeclarationId);
            method.OnCallMethod2Async = InvokeAsync;
        }

        /// <summary>
        /// Qualifies only this bound Object and this Method's instance/own non-null declaration.
        /// It does not rewrite the original request or introduce a type-wide Method fallback.
        /// </summary>
        private static bool IsExactApplicationMethod(
            NodeId objectId, NodeId methodId, NodeId boundObject, NodeId boundMethod, NodeId declaration)
        {
            return objectId == boundObject &&
                (methodId == boundMethod || (!declaration.IsNull && methodId == declaration));
        }

        /// <summary>
        /// Deterministic startup checks of the exact predicate used by admission, without fake UA calls.
        /// These do not mutate bound metadata, dispatch work or create additional operation checkpoints.
        /// </summary>
        private static void AssertExactAliasQualification(NodeId boundObject, NodeId boundMethod, NodeId declaration)
        {
            NodeId otherObject = ObjectIds.ObjectsFolder;
            NodeId otherDeclaration = MethodIds.ServerType_GetMonitoredItems;
            RestartPipe.Require(!boundObject.IsNull &&
                !boundMethod.IsNull &&
                otherObject != boundObject &&
                otherDeclaration != boundMethod &&
                otherDeclaration != declaration,
                "The exact-alias qualification checks require distinct bound and negative targets.");
            foreach (NodeId candidate in new[] { boundMethod, declaration })
            {
                RestartPipe.Require(
                    IsExactApplicationMethod(boundObject, candidate, boundObject, boundMethod, declaration) ==
                        !candidate.IsNull,
                    "Only the bound instance and its own non-null declaration may qualify.");
                RestartPipe.Require(!IsExactApplicationMethod(
                    otherObject, candidate, boundObject, boundMethod, declaration),
                    "A valid bound Method identity must not qualify for another Object.");
            }
            RestartPipe.Require(!IsExactApplicationMethod(
                boundObject, otherDeclaration, boundObject, boundMethod, declaration),
                "An unrelated Method declaration must not qualify for the bound Object.");
            RestartPipe.Require(
                IsExactApplicationMethod(boundObject, boundMethod, boundObject, boundMethod, default) &&
                !IsExactApplicationMethod(boundObject, default, boundObject, boundMethod, default),
                "An absent declaration must not become a null Method alias or disable the actual instance.");
        }

        internal async Task ArmAsync(int amount, CancellationToken ct)
        {
            RestartPipe.Require(amount is 3 or 7 or 11, "Only the script's three self-contained arguments qualify.");
            Task idle;
            lock (m_gate)
            {
                idle = m_idle;
            }
            // Gate the actual provider reservation release, not a presumed scheduling delay.
            await idle.WaitAsync(ct).ConfigureAwait(false);
            lock (m_gate)
            {
                RestartPipe.Require(!m_fenced && m_lease == null && (m_armed == null || m_armed.Used),
                    "Unproven work or an unconsumed original remains fenced.");
                m_armed = new ArmedCall(amount);
            }
        }

        internal void Release()
        {
            lock (m_gate)
            {
                RestartPipe.Require(m_lease is { EffectCommitted: true } && !m_fenced,
                    "Only an original with a committed business effect can pass the return gate.");
                RestartPipe.Require(m_lease!.Call.Release.TrySetResult(), "The original was released twice.");
            }
        }

        public ValueTask<IAsyncDisposable> AcquireAsync(
            OperationContext context, IServiceRequest request, ServiceOperationRecoveryMode mode,
            RecoverableExecutionOwner owner, CancellationToken cancellationToken = default)
        {
            if (mode != ServiceOperationRecoveryMode.Owner)
            {
                throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
            }
            // Owner has no journal incarnation; this is only the supervisor's local reservation identity.
            return AcquireCoreAsync(context, request, owner, Uuid.NewUuid(), cancellationToken);
        }

        public ValueTask<IAsyncDisposable> AcquireDurableAsync(
            OperationContext context, IServiceRequest request, RecoverableExecutionOwner owner,
            Uuid executionId, CancellationToken cancellationToken = default)
        {
            // This is the production journal's exact unique execution identity, never a synthesized Client ID.
            return AcquireCoreAsync(context, request, owner, executionId, cancellationToken);
        }

        private async ValueTask<IAsyncDisposable> AcquireCoreAsync(
            OperationContext context, IServiceRequest request, RecoverableExecutionOwner owner,
            Uuid executionId, CancellationToken ct)
        {
            Lease lease;
            lock (m_gate)
            {
                if (m_fenced ||
                    m_lease != null ||
                    m_armed == null ||
                    m_armed.Used ||
                    m_method == null ||
                    owner.Id != expectedOwner ||
                    executionId == Uuid.Empty ||
                    request is not CallRequest call ||
                    call.MethodsToCall.Count != 1 ||
                    !IsExactApplicationMethod(call.MethodsToCall[0].ObjectId, call.MethodsToCall[0].MethodId,
                        m_deviceId, m_method.NodeId, m_method.MethodDeclarationId) ||
                    call.MethodsToCall[0].InputArguments.Count != 1 ||
                    !call.MethodsToCall[0].InputArguments[0].TryGetValue(out int amount) ||
                    amount != m_armed.Amount)
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                }
                lease = new Lease(this, m_armed, context.SessionId, call.RequestHeader.RequestHandle);
                m_armed.Used = true;
                m_lease = lease;
                m_idle = lease.Disposed.Task;
            }
            try
            {
                await authority.SendAsync(RestartCommand.Acquire, writer =>
                {
                    RestartPipe.WriteId(writer, executionId);
                    RestartConfiguration.WriteOwner(writer, owner.Id);
                }, ct).ConfigureAwait(false);
                return lease;
            }
            catch
            {
                lock (m_gate)
                {
                    m_fenced = true;
                }
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask<RecoverableExecutionRecoveredWork> RecoverAsync(
            RecoverableExecutionOwner owner, Uuid executionId, CancellationToken cancellationToken = default)
        {
            byte[] reply = await authority.ExchangeAsync(RestartCommand.Recover, writer =>
            {
                RestartPipe.WriteId(writer, executionId);
                RestartConfiguration.WriteOwner(writer, owner.Id);
            }, cancellationToken).ConfigureAwait(false);
            try
            {
                using BinaryReader reader = RestartPipe.Reader(reply, reply: true);
                bool terminated = reader.ReadBoolean();
                RestartPipe.End(reader);
                if (!terminated)
                {
                    lock (m_gate)
                    {
                        m_fenced = true;
                    }
                }
                // The returned wrapper owns the reservation and StandardServer disposes the wrapper.
                // TODO: Teach disposal analysis this public constructor's ownership-transfer contract.
#pragma warning disable CA2000
                return new RecoverableExecutionRecoveredWork(new RecoveryFence(), terminated);
#pragma warning restore CA2000
            }
            finally
            {
                CryptographicOperations.ZeroMemory(reply);
            }
        }

        private async ValueTask<ServiceResult> InvokeAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            ArrayOf<Variant> inputs, List<Variant> outputs, CancellationToken ct)
        {
            // Also count rejected dispatch attempts. A broken runtime must not hide resumption behind this fence.
            Interlocked.Increment(ref m_dispatchAttempts);
            Lease lease;
            lock (m_gate)
            {
                // Native ordinary/session-bound Calls carry a live Session. Only the StandardServer's
                // independently owned Owner/Durable callback has this exact held, copied context.
                if (m_fenced ||
                    m_lease is not { Entered: false } candidate ||
                    context is not ServerSystemContext { OperationContext: { } invocation } ||
                    invocation.Session != null ||
                    invocation.RequestType != RequestType.Call ||
                    invocation.SessionId != candidate.IssuingSession ||
                    invocation.ClientHandle != candidate.RequestHandle ||
                    !ReferenceEquals(method, m_method) ||
                    objectId != m_deviceId ||
                    inputs.Count != 1 ||
                    !inputs[0].TryGetValue(out int amount) ||
                    amount != candidate.Call.Amount)
                {
                    return StatusCodes.BadUserAccessDenied;
                }
                lease = candidate;
                lease.Entered = true;
            }

            RestartSnapshot before = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            RestartSnapshot entered = before with { Invocations = checked(before.Invocations + 1) };
            await SaveSnapshotAsync(entered, ct).ConfigureAwait(false);
            RestartSnapshot completed = entered with { Value = checked(entered.Value + lease.Call.Amount) };
            // RSE3-064: this is the business mutation, not the separate invocation-entry record above.
            // SetAsync returns only after the real protected file-store commit completes.
            await SaveSnapshotAsync(completed, ct).ConfigureAwait(false);
            lock (m_gate)
            {
                lease.EffectCommitted = true;
            }
            await authority.SendAsync(RestartCommand.EffectCommitted, writer =>
            {
                writer.Write(completed.Invocations);
                writer.Write(completed.Value);
            }, ct).ConfigureAwait(false);
            // The interruption kills this process AFTER its effect commit but BEFORE handler return/O.
            // That scenario never releases this gate; no saved request may resume it on recovery.
            await lease.Call.Release.Task.WaitAsync(ct).ConfigureAwait(false);
            RestartPipe.Require(outputs.Count == 1, "The generated Method output shape changed.");
            outputs[0] = Variant.From(completed.Value);
            return ServiceResult.Good;
        }

        internal async Task<RestartSnapshot> ReadSnapshotAsync(CancellationToken ct)
        {
            (bool found, ByteString value) = await store.TryGetAsync(kCounterKey, ct).ConfigureAwait(false);
            if (!found)
            {
                return new RestartSnapshot(0, 0);
            }
            RestartPipe.Require(value.Length == 12 && BinaryPrimitives.ReadInt32LittleEndian(value.Span) == 1,
                "The protected business counter format changed.");
            return new RestartSnapshot(
                BinaryPrimitives.ReadInt32LittleEndian(value.Span[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(value.Span[8..]));
        }

        private ValueTask SaveSnapshotAsync(RestartSnapshot value, CancellationToken ct)
        {
            byte[] bytes = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), value.Invocations);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), value.Value);
            // The file backend protects and flushes this snapshot. This is NOT a transaction with RSE commits.
            return store.SetAsync(kCounterKey, new ByteString(bytes), ct);
        }

        private sealed class ArmedCall(int amount)
        {
            internal int Amount { get; } = amount;
            internal bool Used { get; set; }
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class Lease(RestartWork owner, ArmedCall call, NodeId issuingSession, uint requestHandle)
            : IAsyncDisposable
        {
            internal ArmedCall Call { get; } = call;
            internal NodeId IssuingSession { get; } = issuingSession;
            internal uint RequestHandle { get; } = requestHandle;
            internal bool Entered { get; set; }
            internal bool EffectCommitted { get; set; }
            internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask DisposeAsync()
            {
                lock (owner.m_gate)
                {
                    if (ReferenceEquals(owner.m_lease, this))
                    {
                        owner.m_lease = null;
                    }
                }
                Disposed.TrySetResult();
                // Do not erase the supervisor's execution identity or its unproven-work fence.
                return default;
            }
        }

        private sealed class RecoveryFence : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                // A local Server shutdown does not establish termination or remove any underlying fence.
                return default;
            }
        }

        private const string kCounterKey = "restart-test/business/counter-v1";
        private readonly Lock m_gate = new();
        private NodeId m_deviceId;
        private MethodState? m_method;
        private ArmedCall? m_armed;
        private Lease? m_lease;
        private Task m_idle = Task.CompletedTask;
        private bool m_fenced;
        private int m_dispatchAttempts;
    }
}
