/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Storage;
using RecoverableServiceDemo.Model;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Bounded native-wire integration infrastructure with REAL Server child termination/restart.
    /// This is not physical-host/power-loss qualification or a production Durable-facet claim.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class ProcessRestartDemo
    {
        /// <summary>
        /// Runs the explicitly named TEST scenario with a supervisor-owned, deterministically stepped clock.
        /// Intended callable entry for a future coordinated regression binding. Throws on any failed invariant.
        /// </summary>
        public static Task<ProcessRestartDemoResult> RunTestAsync(TextWriter output, CancellationToken ct = default)
        {
            var clock = new SupervisorTestClock();
            return RunBoundedAsync(clock, clock, output, null, ct);
        }

        /// <summary>
        /// Runs the same TEST with an explicitly deployed sample child apphost or DLL.
        /// A missing explicit host fails rather than falling back to NUnit/testhost or another deployment.
        /// </summary>
        public static Task<ProcessRestartDemoResult> RunTestAsync(
            string childHostPath, TextWriter output, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(childHostPath);
            var clock = new SupervisorTestClock();
            return RunBoundedAsync(clock, clock, output, Path.GetFullPath(childHostPath), ct);
        }

        /// <summary>
        /// Runs the same child-process/owner checks with an explicitly supplied qualified clock.
        /// No clock default is provided. This path does not manipulate time or claim the TEST H-expiry assertion.
        /// The application remains responsible for the clock's actual interruption/host qualification.
        /// </summary>
        public static Task<ProcessRestartDemoResult> RunWithQualifiedClockAsync(
            IRecoverableExecutionClock clock, TextWriter output, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(clock);
            return RunBoundedAsync(clock, null, output, null, ct);
        }

        private static async Task<ProcessRestartDemoResult> RunBoundedAsync(
            IRecoverableExecutionClock clock, SupervisorTestClock? testClock, TextWriter output,
            string? childHostPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(output);
            ct.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "This TEST requires Windows current-user-only, peer-verified pipes.");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            // The store's parent is uniquely owned by this run, NOT C:\Windows\TEMP itself.
            string root = Directory.CreateTempSubdirectory("ua-rse-process-restart-test-").FullName;
            var ownership = new RunOwnership();
            ProcessRestartDemoResult result;
            try
            {
                await output.WriteLineAsync(
                    "TEST ONLY: real child-process restart, native loopback SignAndEncrypt " +
                    "and generated X.509 identities. " +
                    "Not a host reboot, power-failure test, or production Durable qualification.")
                    .ConfigureAwait(false);
                await output.WriteLineAsync(testClock != null
                    ? "Time evidence: one surviving supervisor domain with explicit SIMULATED time steps."
                    : "Time evidence: explicitly injected application clock; " +
                        "exact TEST history-expiry step is disabled.")
                    .ConfigureAwait(false);
                result = await RunCoreAsync(clock, testClock, output, root, ownership, childHostPath, timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (ownership.Supervisor?.AllChildrenExited != false)
                {
                    Directory.Delete(root, recursive: true);
                }
                else
                {
                    await output.WriteLineAsync(
                        "FAIL: child exit is unproven; retaining only this run's private directory: " + root)
                        .ConfigureAwait(false);
                }
            }
            await output.WriteLineAsync(
                $"PASS: Owner={result.OwnerValue}, retained original={result.RetainedValue}, " +
                $"invocations={result.Invocations}, business value={result.BusinessValue}, " +
                $"actual child kills={result.KilledChildren}, " +
                $"exact TEST H boundary={result.HistoryBoundaryVerified}, " +
                $"clock failure checked={result.ClockFailureVerified}.")
                .ConfigureAwait(false);
            return result;
        }

        private static async Task<ProcessRestartDemoResult> RunCoreAsync(
            IRecoverableExecutionClock clock, SupervisorTestClock? testClock, TextWriter output,
            string root, RunOwnership ownership, string? childHostPath, CancellationToken ct)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using var telemetryLifetime = telemetry as IDisposable;
            RestartSecrets secrets = await RestartSecrets.CreateAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable secretLifetime = secrets.ConfigureAwait(false);
            string runId = Guid.NewGuid().ToString("N");
            string serverUri = "urn:localhost:OPCFoundation:RseRestartTestServer:" + runId;
            string endpoint = $"opc.tcp://127.0.0.1:{GetFreeLoopbackPort()}/RseRestartTest";
            ApplicationInstance clientApplication = await RestartSecurity.CreateApplicationAsync(
                "RseRestartTestClient", "urn:localhost:OPCFoundation:RseRestartTestClient:" + runId,
                Path.Combine(root, "client-pki"), secrets, telemetry, null, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable clientApplicationLifetime = clientApplication.ConfigureAwait(false);
            ApplicationInstance userApplication = await RestartSecurity.CreateApplicationAsync(
                "RseRestartTestUser", "urn:localhost:OPCFoundation:RseRestartTestUser:" + runId,
                Path.Combine(root, "user-pki"), secrets, telemetry, null, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable userApplicationLifetime = userApplication.ConfigureAwait(false);
            using CertificateEntry clientCertificate = RestartSecurity.AcquireCertificate(clientApplication);
            using CertificateEntry userCertificate = RestartSecurity.AcquireCertificate(userApplication);
            RecoverableExecutionTime initialTime = await clock.GetTimeAsync(ct).ConfigureAwait(false);
            RestartPipe.Require(initialTime.DomainId != Uuid.Empty, "The supplied clock has no qualified domain.");
            RestartConfiguration configuration;
            // Generate/pin Server PKI once without starting any in-process Server.
            ApplicationInstance seed = await RestartSecurity.CreateApplicationAsync(
                "RseRestartTestServer", serverUri, Path.Combine(root, "server-pki"),
                secrets, telemetry, endpoint, ct).ConfigureAwait(false);
            await using (seed.ConfigureAwait(false))
            {
                using CertificateEntry serverCertificate = RestartSecurity.AcquireCertificate(seed);
                await RestartSecurity.TrustAsync(seed, TrustListIdentifier.Peers, clientCertificate.Certificate, ct)
                    .ConfigureAwait(false);
                await RestartSecurity.TrustAsync(seed, TrustListIdentifier.Users, userCertificate.Certificate, ct)
                    .ConfigureAwait(false);
                await RestartSecurity.TrustAsync(
                    clientApplication, TrustListIdentifier.Peers, serverCertificate.Certificate, ct)
                    .ConfigureAwait(false);
                configuration = new RestartConfiguration
                {
                    Root = root,
                    EndpointUrl = endpoint,
                    ServerUri = serverUri,
                    Owner = new RecoverableExecutionOwnerId(Uuid.NewUuid(), Uuid.NewUuid(), Uuid.NewUuid()),
                    ServerCertificateHash = RestartSecurity.Hash(serverCertificate.Certificate.RawData),
                    ClientCertificateHash = RestartSecurity.Hash(clientCertificate.Certificate.RawData),
                    UserCertificateHash = RestartSecurity.Hash(userCertificate.Certificate.RawData),
                    UserIssuer = userCertificate.Certificate.Issuer,
                    UserSubject = userCertificate.Certificate.Subject,
                    TimeDomain = initialTime.DomainId
                };
            }
            var userId = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = userApplication.ApplicationConfiguration!.SecurityConfiguration
                    .ApplicationCertificates[0].StorePath,
                Thumbprint = userCertificate.Certificate.Thumbprint,
                SubjectName = userCertificate.Certificate.Subject,
                CertificateType = userCertificate.CertificateType
            };
            UserIdentity user = await UserIdentity.CreateAsync(userId, secrets.Passwords,
                userApplication.ApplicationConfiguration!.CertificateManager!.CertificateProvider, ct)
                .ConfigureAwait(false);
            using AesCbcHmacRecordProtector protector = secrets.CreateProtector();
            var client = new ClientState(
                configuration, clientApplication.ApplicationConfiguration!, user, protector, telemetry);
            await using ConfiguredAsyncDisposable clientLifetime = client.ConfigureAwait(false);
            var supervisor = new RestartSupervisor(configuration, secrets, clock, initialTime, childHostPath);
            await using ConfiguredAsyncDisposable supervisorLifetime = supervisor.ConfigureAwait(false);
            ownership.Supervisor = supervisor;

            RestartSupervisor.Child first = await supervisor.StartAsync(ct).ConfigureAwait(false);
            await AssertCounterAsync(first, 0, 0, ct).ConfigureAwait(false);
            int ownerValue = await OwnerReplacementSessionAsync(client, first, output, ct).ConfigureAwait(false);
            OriginalEvidence retained = await RetainedUnacknowledgedOutcomeAsync(client, first, output, ct)
                .ConfigureAwait(false);
            AdvanceTestTime(testClock, 1000);
            await client.ReopenAsync().ConfigureAwait(false);

            RestartSupervisor.Child second = await supervisor.StartAsync(ct).ConfigureAwait(false);
            await AssertCounterAsync(second, 2, 10, ct).ConfigureAwait(false);
            ServiceOperationInfoDataType released = await RecoverRetainedAndReleaseAsync(
                client, second, retained, testClock != null, output, ct).ConfigureAwait(false);
            AdvanceTestTime(testClock, 1000);
            await client.ReopenAsync().ConfigureAwait(false);

            RestartSupervisor.Child third = await supervisor.StartAsync(ct).ConfigureAwait(false);
            await ReleasedHistorySurvivesRestartAsync(client, third, retained, released, testClock != null, output, ct)
                .ConfigureAwait(false);
            PendingEvidence pending = await InterruptAfterBusinessEffectAsync(client, third, output, ct)
                .ConfigureAwait(false);
            AdvanceTestTime(testClock, 1000);
            await client.ReopenAsync().ConfigureAwait(false);

            RestartSupervisor.Child fourth = await supervisor.StartAsync(ct).ConfigureAwait(false);
            ServiceOperationInfoDataType indeterminate = await InterruptedOriginalBecomesIndeterminateAsync(
                client, fourth, pending, testClock, output, ct).ConfigureAwait(false);
            AdvanceTestTime(testClock, 1000);
            await client.ReopenAsync().ConfigureAwait(false);

            RestartSupervisor.Child fifth = await supervisor.StartAsync(ct).ConfigureAwait(false);
            await IndeterminateRetentionIsNotRenewedAsync(
                client, fifth, pending, indeterminate, testClock, output, ct).ConfigureAwait(false);
            await AssertCounterAsync(fifth, 3, 21, ct).ConfigureAwait(false);
            RestartSnapshot final = await fifth.SnapshotAsync(ct).ConfigureAwait(false);
            RestartPipe.Require((await client.Checkpoints.GetOperationIdsAsync(ct).ConfigureAwait(false)).Count == 3,
                "The script must preserve exactly three protected Client operation checkpoints.");
            // O can be visible before the normal invocation's termination marker commits. If that
            // boundary is killed, proving its exit too is legitimate; do not invent a scheduling assumption.
            RestartPipe.Require(supervisor.RegisteredOriginals == 3 &&
                supervisor.ProvenRecoveries is >= 1 and <= 2 &&
                supervisor.InterruptedRecoveryProofs == 1,
                "Original admission or supervisor termination proof was repeated/missing.");
            if (testClock != null)
            {
                await ClockAuthorityChangeFailsClosedAsync(fifth, testClock, output, ct).ConfigureAwait(false);
            }
            await fifth.StopAsync(ct).ConfigureAwait(false);
            RestartPipe.Require(supervisor.LaunchedChildren == 5 &&
                supervisor.KilledChildren == 4 &&
                supervisor.AllChildrenExited, "The real five-incarnation restart script did not complete.");
            RestartPipe.Require(retained.Response.Results[0].OutputArguments[0].TryGetValue(out int retainedValue),
                "The retained typed original could not be reported.");
            return new ProcessRestartDemoResult(ownerValue, retainedValue, final.Invocations, final.Value,
                supervisor.LaunchedChildren, supervisor.KilledChildren, testClock != null, testClock != null);
        }

        private static async Task ClockAuthorityChangeFailsClosedAsync(
            RestartSupervisor.Child child, SupervisorTestClock clock, TextWriter output, CancellationToken ct)
        {
            Uuid previous = clock.ChangeDomainForFaultInjection();
            await child.RequireUnavailableTimeAsync(ct).ConfigureAwait(false);
            // Restoring the old coordinate is NOT permission to un-latch an already lost authority.
            clock.RestoreDomainForFailureLatchCheck(previous);
            await child.RequireUnavailableTimeAsync(ct).ConfigureAwait(false);
            await AssertCounterAsync(child, 3, 21, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "ClockAuthorityChangeFailsClosed: live child's time request rejected; authority pipe lost; " +
                "restoring a coordinate does not reset deadlines.")
                .ConfigureAwait(false);
        }

        /// <summary>Same validated owner, different real Session, one still-running original.</summary>
        private static async Task<int> OwnerReplacementSessionAsync(
            ClientState client, RestartSupervisor.Child child, TextWriter output, CancellationToken ct)
        {
            Uuid id;
            NodeId issuingSession;
            await child.ArmAsync(3, ct).ConfigureAwait(false);
            ManagedSession first = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using (first.ConfigureAwait(false))
            {
                issuingSession = first.SessionId;
                AsyncServiceOperation<int> original = await client.CreateAsync(
                    first, ServiceOperationRecoveryMode.Owner, 3, expectedValue: 3, ct).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable originalLifetime = original.ConfigureAwait(false);
                id = original.ClientOperationId;
                ServiceOperationInfoDataType pending = await original.SubmitAsync(
                    RestartConfiguration.ExecutionMilliseconds, RestartConfiguration.RetentionMilliseconds, ct: ct)
                    .ConfigureAwait(false);
                AssertPending(pending, ServiceOperationRecoveryMode.Owner);
                RestartSnapshot effect = await child.WaitForEffectCommitAsync(ct).ConfigureAwait(false);
                RestartPipe.Require(effect == new RestartSnapshot(1, 3),
                    "The Owner original did not commit exactly one business increment.");
                await client.AssertCheckpointAsync(id, ct).ConfigureAwait(false);
            } // Closes the actual issuing Session while the independent handler remains gated.

            ManagedSession replacement = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable replacementLifetime = replacement.ConfigureAwait(false);
            RestartPipe.Require(replacement.SessionId != issuingSession, "Owner recovery reused its issuing Session.");
            AsyncServiceOperation<int> recovery = await client.ReattachAsync(
                replacement, ServiceOperationRecoveryMode.Owner, id, 3, expectedValue: 3, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable recoveryLifetime = recovery.ConfigureAwait(false);
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(replacement, ct)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType pendingWire = await manager.GetAsync(id, ServiceOperationRecoveryMode.Owner, ct)
                .ConfigureAwait(false);
            AssertPending(pendingWire.Operation, ServiceOperationRecoveryMode.Owner);
            RestartPipe.Require(pendingWire.Result.IsNull, "Pending Owner work disclosed a payload.");
            await child.ReleaseAsync(ct).ConfigureAwait(false);
            AsyncServiceResult<int> result = await recovery.WaitForResultAsync(ct: ct).ConfigureAwait(false);
            AssertServiceResult(result, 3);
            await client.AssertAcceptanceAsync(id, result, replacement.MessageContext, ct).ConfigureAwait(false);
            AssertAcknowledgement(
                await recovery.AcknowledgeAsync(ct).ConfigureAwait(false), ServiceOperationControlResult.Applied);
            await AssertCounterAsync(child, 1, 3, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "OwnerReplacementSession: new authenticated Session; value=3, original invocations=1; explicit ACK.")
                .ConfigureAwait(false);
            return result.GetValueOrThrow();
        }

        /// <summary>Collect O before killing: retained-unacknowledged recovery, NOT fabricated reply loss.</summary>
        private static async Task<OriginalEvidence> RetainedUnacknowledgedOutcomeAsync(
            ClientState client, RestartSupervisor.Child child, TextWriter output, CancellationToken ct)
        {
            await child.ArmAsync(7, ct).ConfigureAwait(false);
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            AsyncServiceOperation<int> original = await client.CreateAsync(
                session, ServiceOperationRecoveryMode.Durable, 7, expectedValue: 10, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable originalLifetime = original.ConfigureAwait(false);
            AssertPending(await original.SubmitAsync(
                RestartConfiguration.ExecutionMilliseconds, RestartConfiguration.RetentionMilliseconds, ct: ct)
                .ConfigureAwait(false),
                ServiceOperationRecoveryMode.Durable);
            RestartPipe.Require(
                await child.WaitForEffectCommitAsync(ct).ConfigureAwait(false) == new RestartSnapshot(2, 10),
                "The retained original did not commit exactly one business increment.");
            await child.ReleaseAsync(ct).ConfigureAwait(false);
            AsyncServiceResult<int> result = await original.WaitForResultAsync(ct: ct).ConfigureAwait(false);
            CallResponse response = AssertServiceResult(result, 10);
            await client.AssertCheckpointAsync(original.ClientOperationId, ct).ConfigureAwait(false);
            await client.AssertAcceptanceAsync(original.ClientOperationId, result, session.MessageContext, ct)
                .ConfigureAwait(false);
            RestartPipe.Require(
                original.LastAcknowledgement == null, "The retained outcome was acknowledged before the fault.");
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType wire = await manager.GetAsync(
                original.ClientOperationId, ServiceOperationRecoveryMode.Durable, ct)
                .ConfigureAwait(false);
            AssertWireOriginal(wire, response, session.MessageContext, 10);
            await AssertCounterAsync(child, 2, 10, ct).ConfigureAwait(false);
            var evidence = new OriginalEvidence(original.ClientOperationId, original.ServerToken,
                session.SessionId, CoreUtils.Clone(response)!, CoreUtils.Clone(wire.Operation)!);
            await output.WriteLineAsync(
                "RetainedUnacknowledgedOutcome: original Call/header collected and protected acceptance saved; " +
                "NO ACK; killing that Server child.")
                .ConfigureAwait(false);
            await child.KillAsync(ct).ConfigureAwait(false);
            return evidence;
        }

        private static async Task<ServiceOperationInfoDataType> RecoverRetainedAndReleaseAsync(
            ClientState client, RestartSupervisor.Child child, OriginalEvidence saved, bool deterministic,
            TextWriter output, CancellationToken ct)
        {
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            RestartPipe.Require(
                session.SessionId != saved.SessionId, "Durable recovery reused the old Server Session.");
            AsyncServiceOperation<int> recovery = await client.ReattachAsync(
                session, ServiceOperationRecoveryMode.Durable, saved.Id, 7, expectedValue: 10, ct)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable recoveryLifetime = recovery.ConfigureAwait(false);
            AsyncServiceResult<int> result = await recovery.WaitForResultAsync(ct: ct).ConfigureAwait(false);
            CallResponse restored = AssertServiceResult(result, 10);
            RestartPipe.Require(
                CoreUtils.IsEqual(saved.Response, restored), "The protected accepted original/header changed.");
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType wire = await manager.GetAsync(saved.Id, ServiceOperationRecoveryMode.Durable, ct)
                .ConfigureAwait(false);
            AssertSameIdentity(wire.Operation, saved.Id, saved.Token);
            AssertWireOriginal(wire, saved.Response, session.MessageContext, 10);
            AssertRemaining(saved.Operation.ResultRemaining, wire.Operation.ResultRemaining, 1000, deterministic);
            await client.AssertAcceptanceAsync(saved.Id, result, session.MessageContext, ct).ConfigureAwait(false);
            RestartPipe.Require(
                recovery.LastAcknowledgement == null, "Reattachment acknowledged without explicit acceptance.");
            ControlResponseDataType ack = await recovery.AcknowledgeAsync(ct).ConfigureAwait(false);
            AssertAcknowledgement(ack, ServiceOperationControlResult.Applied);
            ControlResponseDataType retry = await manager.AcknowledgeAgainAsync(saved.Token, ct).ConfigureAwait(false);
            AssertAcknowledgement(retry, ServiceOperationControlResult.AlreadyApplied);
            GetResultResponseDataType history = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertReleased(history, saved.Id, saved.Token, ServiceOperationOutcome.ServiceResponse, StatusCodes.Good);
            AssertRemaining(ack.Operation.HistoryRemaining, history.Operation.HistoryRemaining, 0, deterministic);
            await AssertCounterAsync(child, 2, 10, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "Recovered retained original: fresh wire Call result/header match exactly, " +
                "value=10, invocations=2; ACK committed; killing after release.")
                .ConfigureAwait(false);
            await child.KillAsync(ct).ConfigureAwait(false);
            return CoreUtils.Clone(history.Operation)!;
        }

        private static async Task ReleasedHistorySurvivesRestartAsync(
            ClientState client, RestartSupervisor.Child child, OriginalEvidence saved,
            ServiceOperationInfoDataType released, bool deterministic, TextWriter output, CancellationToken ct)
        {
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType history = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertReleased(history, saved.Id, saved.Token, ServiceOperationOutcome.ServiceResponse, StatusCodes.Good);
            AssertRemaining(released.HistoryRemaining, history.Operation.HistoryRemaining, 1000, deterministic);
            ControlResponseDataType retry = await manager.AcknowledgeAgainAsync(saved.Token, ct).ConfigureAwait(false);
            AssertAcknowledgement(retry, ServiceOperationControlResult.AlreadyApplied);
            AssertRemaining(history.Operation.HistoryRemaining, retry.Operation.HistoryRemaining, 0, deterministic);
            await AssertCounterAsync(child, 2, 10, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "ReleasedHistorySurvivesRestart: Acknowledged/AlreadyApplied, " +
                "no payload, no renewed H or original execution.")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// RSE3-064: kill after the business effect commits, while handler return and RSE O are gated.
        /// </summary>
        private static async Task<PendingEvidence> InterruptAfterBusinessEffectAsync(
            ClientState client, RestartSupervisor.Child child, TextWriter output, CancellationToken ct)
        {
            await child.ArmAsync(11, ct).ConfigureAwait(false);
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            AsyncServiceOperation<int> original = await client.CreateAsync(
                session, ServiceOperationRecoveryMode.Durable, 11, expectedValue: null, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable originalLifetime = original.ConfigureAwait(false);
            AssertPending(await original.SubmitAsync(
                RestartConfiguration.ExecutionMilliseconds, RestartConfiguration.RetentionMilliseconds, ct: ct)
                .ConfigureAwait(false),
                ServiceOperationRecoveryMode.Durable);
            RestartPipe.Require(
                await child.WaitForEffectCommitAsync(ct).ConfigureAwait(false) == new RestartSnapshot(3, 21),
                "The interrupted original did not commit the real business value 10 + 11 exactly once.");
            // Independently reread the protected business record before observing the still-Pending RSE state.
            await AssertCounterAsync(child, 3, 21, ct).ConfigureAwait(false);
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType pending = await manager.GetAsync(
                original.ClientOperationId, ServiceOperationRecoveryMode.Durable, ct)
                .ConfigureAwait(false);
            AssertPending(pending.Operation, ServiceOperationRecoveryMode.Durable);
            RestartPipe.Require(
                pending.Result.IsNull && !original.Completion.IsCompleted && original.LastAcknowledgement == null,
                "The gated original selected an outcome before the interruption.");
            await client.AssertCheckpointAsync(original.ClientOperationId, ct).ConfigureAwait(false);
            var evidence = new PendingEvidence(original.ClientOperationId, original.ServerToken, session.SessionId);
            // Exercise the negative proof with a REAL still-running process. Disposal/absence of a local
            // Task is not used. The supervisor fence remains until qualified recovery observes actual exit.
            child.RequireUnprovenTermination();
            await output.WriteLineAsync(
                "InterruptedOriginal RSE3-064: invocation=3, business value=21 committed and reread; " +
                "RSE still Pending; post-effect return gate NEVER released; killing before final O.")
                .ConfigureAwait(false);
            await child.KillAsync(ct).ConfigureAwait(false);
            return evidence;
        }

        private static async Task<ServiceOperationInfoDataType> InterruptedOriginalBecomesIndeterminateAsync(
            ClientState client, RestartSupervisor.Child child, PendingEvidence saved,
            SupervisorTestClock? testClock, TextWriter output, CancellationToken ct)
        {
            await AssertCounterAsync(child, 3, 21, ct).ConfigureAwait(false);
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            RestartPipe.Require(
                session.SessionId != saved.SessionId, "The interrupted original did not use a new Session.");
            AsyncServiceOperation<int> recovery = await client.ReattachAsync(
                session, ServiceOperationRecoveryMode.Durable, saved.Id, 11, expectedValue: null, ct)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable recoveryLifetime = recovery.ConfigureAwait(false);
            AsyncServiceResult<int> result = await recovery.WaitForResultAsync(ct: ct).ConfigureAwait(false);
            AssertIndeterminate(result);
            await client.AssertAcceptanceAsync(saved.Id, result, session.MessageContext, ct).ConfigureAwait(false);
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType first = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertWireIndeterminate(first, saved);
            if (testClock != null)
            {
                RestartPipe.Require(first.Operation.ResultRemaining == RestartConfiguration.RetentionMilliseconds,
                    "Interrupted admission did not receive one fresh R at indeterminate outcome commit.");
            }
            AdvanceTestTime(testClock, 1000);
            GetResultResponseDataType repeated = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertWireIndeterminate(repeated, saved);
            AssertRemaining(
                first.Operation.ResultRemaining, repeated.Operation.ResultRemaining, 1000, testClock != null);
            RestartPipe.Require(
                recovery.LastAcknowledgement == null, "The indeterminate result was implicitly acknowledged.");
            await AssertCounterAsync(child, 3, 21, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "ExecutionIndeterminate/BadUnexpectedError: NO payload, committed business value remains21, " +
                "new R once, no saved-request dispatch; killing again to check R is not renewed.")
                .ConfigureAwait(false);
            await child.KillAsync(ct).ConfigureAwait(false);
            return CoreUtils.Clone(repeated.Operation)!;
        }

        private static async Task IndeterminateRetentionIsNotRenewedAsync(
            ClientState client, RestartSupervisor.Child child, PendingEvidence saved,
            ServiceOperationInfoDataType previous, SupervisorTestClock? testClock,
            TextWriter output, CancellationToken ct)
        {
            ManagedSession session = await client.ConnectAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
            AsyncServiceOperation<int> recovery = await client.ReattachAsync(
                session, ServiceOperationRecoveryMode.Durable, saved.Id, 11, expectedValue: null, ct)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable recoveryLifetime = recovery.ConfigureAwait(false);
            AsyncServiceResult<int> result = await recovery.WaitForResultAsync(ct: ct).ConfigureAwait(false);
            AssertIndeterminate(result);
            RestartManagerProbe manager = await RestartManagerProbe.DiscoverAsync(session, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable managerLifetime = manager.ConfigureAwait(false);
            GetResultResponseDataType wire = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertWireIndeterminate(wire, saved);
            AssertRemaining(previous.ResultRemaining, wire.Operation.ResultRemaining, 1000, testClock != null);
            await client.AssertAcceptanceAsync(saved.Id, result, session.MessageContext, ct).ConfigureAwait(false);
            AssertAcknowledgement(
                await recovery.AcknowledgeAsync(ct).ConfigureAwait(false), ServiceOperationControlResult.Applied);
            GetResultResponseDataType released = await manager.GetAsync(
                saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
            AssertReleased(
                released, saved.Id, saved.Token,
                ServiceOperationOutcome.ExecutionIndeterminate, StatusCodes.BadUnexpectedError);
            if (testClock != null)
            {
                RestartPipe.Require(released.Operation.HistoryRemaining == RestartConfiguration.RetentionMilliseconds,
                    "Explicit indeterminate acceptance did not start its bounded history.");
                AdvanceTestTime(testClock, checked((int)RestartConfiguration.RetentionMilliseconds - 1));
                GetResultResponseDataType before = await manager.GetAsync(
                    saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
                AssertReleased(
                    before, saved.Id, saved.Token,
                    ServiceOperationOutcome.ExecutionIndeterminate, StatusCodes.BadUnexpectedError);
                RestartPipe.Require(
                    before.Operation.HistoryRemaining == 1, "History was lost before its exact TEST deadline.");
                ControlResponseDataType retry = await manager.AcknowledgeAgainAsync(saved.Token, ct)
                    .ConfigureAwait(false);
                AssertAcknowledgement(retry, ServiceOperationControlResult.AlreadyApplied);
                RestartPipe.Require(retry.Operation.HistoryRemaining == 1, "An ACK retry renewed H.");
                AdvanceTestTime(testClock, 1);
                GetResultResponseDataType expired = await manager.GetAsync(
                    saved.Id, ServiceOperationRecoveryMode.Durable, ct).ConfigureAwait(false);
                AssertUnknown(expired.Operation, saved.Id);
                RestartPipe.Require(expired.Result.IsNull, "Expired history disclosed an original payload.");
                ControlResponseDataType forgotten = await manager.AcknowledgeAgainAsync(saved.Token, ct)
                    .ConfigureAwait(false);
                RestartPipe.Require(forgotten.Result == ServiceOperationControlResult.UnknownOperation,
                    "ACK after H expiry retained an unbounded disposition.");
                AssertUnknown(forgotten.Operation, Uuid.Empty);
            }
            await AssertCounterAsync(child, 3, 21, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                "IndeterminateRetentionIsNotRenewed: second restart retained the same outcome/R; " +
                "explicit acceptance/ACK; " +
                (testClock != null
                    ? "TEST H checked at R-1ms and exactly R."
                    : "exact H-expiry not exercised with the injected clock."))
                .ConfigureAwait(false);
        }

        private static void AssertPending(ServiceOperationInfoDataType info, ServiceOperationRecoveryMode mode)
        {
            RestartPipe.Require(info.State == ServiceOperationState.Pending &&
                info.RecoveryMode == mode &&
                info.OutcomeKind == ServiceOperationOutcome.None &&
                info.OutcomeStatus == StatusCodes.Good &&
                info.ExecutionRemaining > 0 &&
                info.ResultRemaining == 0 &&
                info.HistoryRemaining == 0,
                "The exact admitted original is not Pending without an outcome.");
        }

        private static CallResponse AssertServiceResult(AsyncServiceResult<int> result, int expected)
        {
            RestartPipe.Require(result.HasValue &&
                result.GetValueOrThrow() == expected &&
                result.Operation.State == ServiceOperationState.OutcomeAvailable &&
                result.Operation.OutcomeKind == ServiceOperationOutcome.ServiceResponse &&
                result.Operation.OutcomeStatus == StatusCodes.Good,
                "The typed original result is not the expected committed value.");
            RestartPipe.Require(
                result.OriginalResponse is CallResponse, "The original generated CallResponse was not retained.");
            var response = (CallResponse)result.OriginalResponse!;
            AssertCall(response, expected);
            return response;
        }

        private static void AssertCall(CallResponse response, int expected)
        {
            RestartPipe.Require(response.ResponseHeader.ServiceResult == StatusCodes.Good &&
                response.ResponseHeader.RequestHandle != 0 &&
                !response.ResponseHeader.Timestamp.IsNull &&
                response.Results.Count == 1 &&
                response.Results[0].StatusCode == StatusCodes.Good &&
                response.Results[0].OutputArguments.Count == 1 &&
                response.Results[0].OutputArguments[0].TryGetValue(out int value) &&
                value == expected,
                "The complete original header/per-item Method output is invalid.");
        }

        private static void AssertWireOriginal(
            GetResultResponseDataType wire, CallResponse original, IServiceMessageContext context, int expected)
        {
            RestartPipe.Require(wire.Operation.State == ServiceOperationState.OutcomeAvailable &&
                wire.Operation.OutcomeKind == ServiceOperationOutcome.ServiceResponse &&
                wire.Operation.OutcomeStatus == StatusCodes.Good &&
                wire.Operation.ResultRemaining > 0,
                "Fresh native GetResult did not recover the retained original.");
            RestartPipe.Require(wire.Result.TryGetValue(out CallResponse? received, context) && received != null,
                "Fresh native GetResult did not carry the generated original CallResponse.");
            AssertCall(received, expected);
            RestartPipe.Require(CoreUtils.IsEqual(original, received) &&
                BinaryEncoder.EncodeMessage(original, context).AsSpan()
                    .SequenceEqual(BinaryEncoder.EncodeMessage(received, context)),
                "The fresh wire original differs in header, diagnostics, item status or output " +
                "from the committed original.");
        }

        private static void AssertIndeterminate(AsyncServiceResult<int> result)
        {
            RestartPipe.Require(!result.HasValue &&
                !result.TryGetValue(out _) &&
                result.OriginalResponse == null &&
                result.Operation.State == ServiceOperationState.OutcomeAvailable &&
                result.Operation.OutcomeKind == ServiceOperationOutcome.ExecutionIndeterminate &&
                result.Operation.OutcomeStatus == StatusCodes.BadUnexpectedError,
                "Interrupted execution fabricated an original result or different protocol outcome.");
            bool failedAsExpected = false;
            try
            {
                _ = result.GetValueOrThrow();
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUnexpectedError)
            {
                failedAsExpected = true;
            }
            RestartPipe.Require(failedAsExpected, "The typed projection turned indeterminate execution into success.");
        }

        private static void AssertWireIndeterminate(GetResultResponseDataType wire, PendingEvidence saved)
        {
            AssertSameIdentity(wire.Operation, saved.Id, saved.Token);
            RestartPipe.Require(wire.Operation.State == ServiceOperationState.OutcomeAvailable &&
                wire.Operation.OutcomeKind == ServiceOperationOutcome.ExecutionIndeterminate &&
                wire.Operation.OutcomeStatus == StatusCodes.BadUnexpectedError &&
                wire.Result.IsNull &&
                wire.Operation.ExecutionRemaining == 0 &&
                wire.Operation.ResultRemaining > 0 &&
                wire.Operation.HistoryRemaining == 0,
                "The native indeterminate outcome/payload/lifetime is incorrect.");
        }

        private static void AssertSameIdentity(ServiceOperationInfoDataType info, Uuid id, ByteString token)
        {
            RestartPipe.Require(info.ClientOperationId == id &&
                info.ServerToken == token &&
                !token.IsEmpty &&
                info.RecoveryMode == ServiceOperationRecoveryMode.Durable &&
                info.ServiceId == DataTypeIds.CallRequest &&
                info.ExecutionAllowance == RestartConfiguration.ExecutionMilliseconds &&
                info.ResultRetentionTime == RestartConfiguration.RetentionMilliseconds,
                "The recovered operation identity, exact mode or accepted E/R changed.");
        }

        private static void AssertAcknowledgement(ControlResponseDataType ack, ServiceOperationControlResult expected)
        {
            RestartPipe.Require(ack.Result == expected &&
                ack.Operation.State == ServiceOperationState.Acknowledged &&
                ack.Operation.ResultRemaining == 0 &&
                ack.Operation.HistoryRemaining > 0,
                "Explicit ACK did not commit the expected bounded release/history disposition.");
        }

        private static void AssertReleased(
            GetResultResponseDataType response, Uuid id, ByteString token,
            ServiceOperationOutcome kind, StatusCode status)
        {
            AssertSameIdentity(response.Operation, id, token);
            RestartPipe.Require(response.Operation.State == ServiceOperationState.Acknowledged &&
                response.Operation.OutcomeKind == kind &&
                response.Operation.OutcomeStatus == status &&
                response.Operation.ExecutionRemaining == 0 &&
                response.Operation.ResultRemaining == 0 &&
                response.Operation.HistoryRemaining > 0 &&
                response.Result.IsNull,
                "Released history changed its disposition or retained a payload.");
        }

        private static void AssertUnknown(ServiceOperationInfoDataType info, Uuid echoedId)
        {
            RestartPipe.Require(info.State == ServiceOperationState.Unknown &&
                info.ClientOperationId == echoedId &&
                info.ServerToken.IsNull &&
                info.ServiceId.IsNull &&
                info.RecoveryMode == ServiceOperationRecoveryMode.Unspecified &&
                info.OutcomeKind == ServiceOperationOutcome.None &&
                info.OutcomeStatus == StatusCodes.Good &&
                info.ExecutionAllowance == 0 &&
                info.ResultRetentionTime == 0 &&
                info.ExecutionRemaining == 0 &&
                info.ResultRemaining == 0 &&
                info.HistoryRemaining == 0 &&
                !info.CancellationRequested &&
                info.RetryAfter == 0,
                "History expiration did not produce canonical Unknown metadata.");
        }

        private static void AssertRemaining(double previous, double current, int simulatedElapsed, bool deterministic)
        {
            RestartPipe.Require(current > 0 &&
                current <= previous &&
                (!deterministic || current == previous - simulatedElapsed),
                "Collection/restart/control renewed or prematurely expired a retained deadline.");
        }

        private static async Task AssertCounterAsync(
            RestartSupervisor.Child child, int invocations, int value, CancellationToken ct)
        {
            RestartPipe.Require(
                await child.SnapshotAsync(ct).ConfigureAwait(false) == new RestartSnapshot(invocations, value),
                "The protected invocation/business counter proves repeated, resumed or missing original work.");
        }

        private static void AdvanceTestTime(SupervisorTestClock? clock, int milliseconds)
        {
            clock?.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }

        private static int GetFreeLoopbackPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        private sealed record OriginalEvidence(
            Uuid Id, ByteString Token, NodeId SessionId, CallResponse Response, ServiceOperationInfoDataType Operation);

        private sealed record PendingEvidence(Uuid Id, ByteString Token, NodeId SessionId);

        private sealed class RunOwnership
        {
            internal RestartSupervisor? Supervisor { get; set; }
        }

        /// <summary>
        /// Reopens the actual protected Client store; no private checkpoint fields are inspected or rewritten.
        /// </summary>
        private sealed class ClientState : IAsyncDisposable
        {
            internal ClientState(RestartConfiguration configuration, ApplicationConfiguration application,
                UserIdentity identity, IRecordProtector protector, ITelemetryContext telemetry)
            {
                m_configuration = configuration;
                m_application = application;
                m_identity = identity;
                m_protector = protector;
                m_telemetry = telemetry;
                m_identityMapping = new RestartClientIdentity(configuration);
                m_backend = OpenBackend();
                Checkpoints = new RecoverableOperationCheckpointStore(
                    m_backend, protector, "restart-test/checkpoints/");
            }

            internal RecoverableOperationCheckpointStore Checkpoints { get; private set; }

            internal async Task<ManagedSession> ConnectAsync(CancellationToken ct)
            {
                ManagedSession session = await new ManagedSessionBuilder(m_application, m_telemetry)
                    .UseEndpoint(m_configuration.EndpointUrl)
                    .WithUserIdentity(m_identity)
                    .WithCheckDomain()
                    .ConnectAsync(ct).ConfigureAwait(false);
                try
                {
                    // Register the generated canonical types before any capture; never substitute a codec on recovery.
                    session.Factory.Builder.AddOpcUaRse().Commit();
                    _ = await m_identityMapping.VerifyAsync(session, ct).ConfigureAwait(false);
                    return session;
                }
                catch
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            internal async Task<AsyncServiceOperation<int>> CreateAsync(
                ManagedSession session, ServiceOperationRecoveryMode mode, int amount,
                int? expectedValue, CancellationToken ct)
            {
                AsyncServiceOperation<int> operation = await Factory(mode).CreateMethodAsync(
                    session, Method(session, amount), ct).ConfigureAwait(false);
                operation.RequireExplicitAcknowledgement().WithAcceptancePolicy(
                    (result, token) => AcceptAsync(
                        operation.ClientOperationId, result, expectedValue, session.MessageContext, token));
                return operation;
            }

            internal async Task<AsyncServiceOperation<int>> ReattachAsync(
                ManagedSession session, ServiceOperationRecoveryMode mode, Uuid id, int amount,
                int? expectedValue, CancellationToken ct)
            {
                await AssertCheckpointAsync(id, ct).ConfigureAwait(false);
                return await Factory(mode).ReattachMethodAsync(
                    session, id, Method(session, amount),
                    (result, token) => AcceptAsync(id, result, expectedValue, session.MessageContext, token), ct)
                    .ConfigureAwait(false);
            }

            internal async Task ReopenAsync()
            {
                await m_backend.DisposeAsync().ConfigureAwait(false);
                m_backend = OpenBackend();
                Checkpoints = new RecoverableOperationCheckpointStore(
                    m_backend, m_protector, "restart-test/checkpoints/");
            }

            internal async Task AssertCheckpointAsync(Uuid id, CancellationToken ct)
            {
                RestartPipe.Require((await Checkpoints.GetOperationIdsAsync(ct).ConfigureAwait(false)).Contains(id),
                    "The protected v2 Client recovery checkpoint was not preserved.");
            }

            private async ValueTask AcceptAsync(
                Uuid id, AsyncServiceResult<int> result, int? expectedValue,
                IServiceMessageContext context, CancellationToken ct)
            {
                if (expectedValue.HasValue)
                {
                    _ = AssertServiceResult(result, expectedValue.Value);
                }
                else
                {
                    AssertIndeterminate(result);
                }
                string key = AcceptanceKey(id);
                ByteString bytes = AcceptanceBytes(id, result, context);
                ByteString protectionContext = RecordProtectionContext.Create("RestartTest.Acceptance.1", key);
                (bool found, ByteString existing) = await m_backend.TryGetAsync(key, ct).ConfigureAwait(false);
                if (!found)
                {
                    ByteString protectedReceipt = m_protector.Protect(protectionContext, bytes);
                    RestartPipe.Require(
                        await m_backend.CompareAndSwapAsync(key, default, protectedReceipt, ct).ConfigureAwait(false),
                        "Application acceptance changed concurrently; no original retry is permitted.");
                }
                else
                {
                    RestartPipe.Require(
                        m_protector.TryUnprotect(protectionContext, existing, out ByteString saved) && saved == bytes,
                        "Previously accepted application evidence differs from this original outcome.");
                }
            }

            internal async Task AssertAcceptanceAsync(
                Uuid id, AsyncServiceResult<int> result, IServiceMessageContext context, CancellationToken ct)
            {
                string key = AcceptanceKey(id);
                (bool found, ByteString receipt) = await m_backend.TryGetAsync(key, ct).ConfigureAwait(false);
                RestartPipe.Require(found &&
                    m_protector.TryUnprotect(
                        RecordProtectionContext.Create("RestartTest.Acceptance.1", key),
                        receipt, out ByteString saved) &&
                    saved == AcceptanceBytes(id, result, context),
                    "Explicit ACK would precede verified, protected application acceptance.");
            }

            private static ByteString AcceptanceBytes(
                Uuid id, AsyncServiceResult<int> result, IServiceMessageContext context)
            {
                // A private sample acceptance receipt, not a replacement for any generated RSE DTO.
                return new ByteString(RestartPipe.Encode(writer =>
                {
                    writer.Write(1);
                    RestartPipe.WriteId(writer, id);
                    writer.Write((int)result.Operation.OutcomeKind);
                    writer.Write((uint)result.Operation.OutcomeStatus);
                    writer.Write(result.HasValue);
                    if (result.HasValue)
                    {
                        writer.Write(result.GetValueOrThrow());
                        writer.Write(SHA256.HashData(BinaryEncoder.EncodeMessage(result.OriginalResponse!, context)));
                    }
                }));
            }

            private static string AcceptanceKey(Uuid id)
            {
                return "restart-test/acceptance/" + id.Guid.ToString("N");
            }

            private AsyncServiceClient Factory(ServiceOperationRecoveryMode mode)
            {
                return new AsyncServiceClient(new RecoverableSessionOptions
                {
                    RecoveryMode = mode,
                    IdentityProvider = m_identityMapping,
                    CheckpointStore = Checkpoints,
                    ExecutionAllowance = RestartConfiguration.ExecutionMilliseconds,
                    ResultRetentionTime = RestartConfiguration.RetentionMilliseconds
                });
            }

            private Func<ISessionClient, CancellationToken, ValueTask<int>> Method(ManagedSession session, int amount)
            {
                var deviceId = ExpandedNodeId.ToNodeId(Model.ObjectIds.DemoDevice, session.NamespaceUris);
                return (client, token) => new DemoDeviceTypeClient(client, deviceId, m_telemetry)
                    .SlowIncrementAsync(amount, token);
            }

            private FileSharedKeyValueStore OpenBackend()
            {
                return new FileSharedKeyValueStore(Path.Combine(m_configuration.Root, "client-checkpoints"),
                    m_configuration.ServerUri + "/client", m_protector, RestartConfiguration.CheckpointCapacity);
            }

            public ValueTask DisposeAsync()
            {
                return m_backend.DisposeAsync();
            }

            private readonly RestartConfiguration m_configuration;
            private readonly ApplicationConfiguration m_application;
            private readonly UserIdentity m_identity;
            private readonly IRecordProtector m_protector;
            private readonly ITelemetryContext m_telemetry;
            private readonly RestartClientIdentity m_identityMapping;
            private FileSharedKeyValueStore m_backend;
        }
    }

    /// <summary>
    /// Only returned after the bounded scenario's concrete assertions and actual process exits succeed.
    /// </summary>
    /// <param name="OwnerValue">The replacement-Session Method result.</param>
    /// <param name="RetainedValue">The exact unacknowledged original recovered after restart.</param>
    /// <param name="Invocations">
    /// Total persisted original invocations: one per scenario, including the interrupted original.
    /// </param>
    /// <param name="BusinessValue">
    /// Persisted value including the interrupted original's effect; indeterminate recovery does not roll it back.
    /// </param>
    /// <param name="LaunchedChildren">Number of explicitly launched Server incarnations.</param>
    /// <param name="KilledChildren">
    /// Number killed at observed boundaries and subsequently awaited to actual exit.
    /// </param>
    /// <param name="HistoryBoundaryVerified">
    /// True only for the explicitly stepped TEST-clock H-boundary assertions.
    /// </param>
    /// <param name="ClockFailureVerified">
    /// True only when the TEST clock-domain fault and sticky failure were asserted.
    /// </param>
    public sealed record ProcessRestartDemoResult(
        int OwnerValue, int RetainedValue, int Invocations, int BusinessValue,
        int LaunchedChildren, int KilledChildren, bool HistoryBoundaryVerified, bool ClockFailureVerified);
}
