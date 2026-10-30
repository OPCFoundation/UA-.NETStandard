/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// A deliberately named TEST clock. Its steps are simulated elapsed time, NOT host downtime evidence.
    /// Only the supervisor owns this coordinate; no restarted child creates or resets it.
    /// </summary>
    internal sealed class SupervisorTestClock : IRecoverableExecutionClock
    {
        public ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<RecoverableExecutionTime>(new RecoverableExecutionTime(m_domain, m_ticks));
            }
        }

        internal void Advance(TimeSpan elapsed)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks);
            lock (m_gate)
            {
                m_ticks = checked(m_ticks + elapsed.Ticks);
            }
        }

        /// <summary>Deliberately violates continuity ONLY to assert that the live child fails closed.</summary>
        internal Uuid ChangeDomainForFaultInjection()
        {
            lock (m_gate)
            {
                Uuid previous = m_domain;
                m_domain = Uuid.NewUuid();
                return previous;
            }
        }

        internal void RestoreDomainForFailureLatchCheck(Uuid previous)
        {
            lock (m_gate)
            {
                m_domain = previous;
            }
        }

        private readonly Lock m_gate = new();
        private Uuid m_domain = Uuid.NewUuid();
        private long m_ticks;
    }

    /// <summary>
    /// Holds the TEST authority and termination evidence outside the processes being killed.
    /// Neither a PID change, local reservation disposal nor a timeout establishes work termination.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartSupervisor(
        RestartConfiguration configuration,
        RestartSecrets secrets,
        IRecoverableExecutionClock clock,
        RecoverableExecutionTime initialTime,
        string? childHostPath = null) : IAsyncDisposable
    {
        internal int KilledChildren { get; private set; }
        internal int LaunchedChildren => m_children.Count;
        internal int RegisteredOriginals => m_executions.Count;
        internal int ProvenRecoveries { get; private set; }
        internal int InterruptedRecoveryProofs { get; private set; }
        internal bool AllChildrenExited => m_children.TrueForAll(child => !child.WasStarted || child.ExitObserved);

        internal async Task<Child> StartAsync(CancellationToken ct)
        {
            RestartPipe.Require(m_children.Count < 5, "The bounded five-incarnation script was exceeded.");
            RestartPipe.Require(AllChildrenExited, "A replacement cannot bypass an unobserved child exit.");
            _ = await ReadTimeAsync(ct).ConfigureAwait(false);
            var child = new Child(this);
            m_children.Add(child);
            await child.StartAsync(ct).ConfigureAwait(false);
            return child;
        }

        private async ValueTask<RecoverableExecutionTime> ReadTimeAsync(CancellationToken ct)
        {
            await m_timeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_timeLost)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                RecoverableExecutionTime current = await clock.GetTimeAsync(ct).ConfigureAwait(false);
                _ = m_lastTime.ElapsedUntil(current);
                RestartPipe.Require(current.DomainId == m_configuration.TimeDomain, "The TEST time authority changed.");
                m_lastTime = current;
                return current;
            }
            catch
            {
                m_timeLost = true;
                throw;
            }
            finally
            {
                m_timeGate.Release();
            }
        }

        private void Register(Child child, Uuid executionId, RecoverableExecutionOwnerId owner)
        {
            lock (m_fences)
            {
                RestartPipe.Require(owner == m_configuration.Owner && executionId != Uuid.Empty,
                    "Unqualified execution identity.");
                RestartPipe.Require(m_unproven.Count == 0, "Unproven surviving work is still fenced.");
                RestartPipe.Require(!child.ExitObserved &&
                    m_executions.Count < 3 &&
                    m_executions.TryAdd(executionId, (owner, child)),
                    "The script permits exactly three original invocations, each admitted once.");
            }
        }

        private bool Recover(Uuid executionId, RecoverableExecutionOwnerId owner)
        {
            lock (m_fences)
            {
                bool proven = owner == m_configuration.Owner &&
                    m_executions.TryGetValue(
                        executionId, out (RecoverableExecutionOwnerId Owner, Child Child) execution) &&
                    execution.Owner == owner &&
                    execution.Child.ExitObserved;
                if (!proven)
                {
                    m_unproven.Add(executionId);
                    return false;
                }
                m_unproven.Remove(executionId);
                ProvenRecoveries++;
                if (executionId == m_interruptedExecutionId)
                {
                    InterruptedRecoveryProofs++;
                }
                return true;
            }
        }

        private void RequireUnprovenTermination(Child child)
        {
            lock (m_fences)
            {
                int checkedExecutions = 0;
                foreach (KeyValuePair<Uuid, (RecoverableExecutionOwnerId Owner, Child Child)> execution in m_executions)
                {
                    if (ReferenceEquals(execution.Value.Child, child))
                    {
                        m_interruptedExecutionId = execution.Key;
                        RestartPipe.Require(!Recover(execution.Key, execution.Value.Owner),
                            "A live child was incorrectly treated as terminated.");
                        checkedExecutions++;
                    }
                }
                RestartPipe.Require(checkedExecutions == 1 && !child.ExitObserved && m_unproven.Count == 1,
                    "The running interrupted original must remain identified and fenced until actual exit.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (Child child in m_children)
                {
                    await child.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_timeGate.Dispose();
            }
        }

        /// <summary>Owns one exact Process object and its two current-user-only, PID-verified IPC peers.</summary>
        internal sealed class Child : IAsyncDisposable
        {
            internal Child(RestartSupervisor owner)
            {
                m_owner = owner;
                m_pipeName = "ua-rse-restart-test-" + Guid.NewGuid().ToString("N");
                m_authority = RestartPipe.CreateServer(m_pipeName + "-authority");
                m_commands = RestartPipe.CreateServer(m_pipeName + "-commands");
                m_commandClient = new RestartPipe(m_commands);
            }

            internal bool ExitObserved => Volatile.Read(ref m_exitObserved);
            internal bool WasStarted { get; private set; }

            internal async Task StartAsync(CancellationToken ct)
            {
                try
                {
                    await StartCoreAsync(ct).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    throw await DescribeStartupFailureAsync(exception, ct).ConfigureAwait(false);
                }
            }

            private async Task StartCoreAsync(CancellationToken ct)
            {
                m_startupStage = "resolving the named sample child host";
                string host = m_owner.m_childHostPath ??
                    Path.Combine(AppContext.BaseDirectory, "RecoverableServiceDemo.exe");
                if (m_owner.m_childHostPath == null && !File.Exists(host))
                {
                    host = Path.Combine(AppContext.BaseDirectory, "RecoverableServiceDemo.dll");
                }
                string name = Path.GetFileName(host);
                RestartPipe.Require(
                    string.Equals(name, "RecoverableServiceDemo.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "RecoverableServiceDemo.dll", StringComparison.OrdinalIgnoreCase),
                    "The explicit child host must be the named sample apphost or DLL, never NUnit/testhost.");
                RestartPipe.Require(File.Exists(host),
                    $"The required built sample child host is absent: {host}. " +
                    "Build/stage it before running; no fallback is permitted.");
                bool managedDll = host.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                var start = new ProcessStartInfo
                {
                    FileName = managedDll ? "dotnet" : host,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(host)!
                };
                if (managedDll)
                {
                    start.ArgumentList.Add(host);
                }
                start.ArgumentList.Add("--process-restart-child");
                start.ArgumentList.Add(m_pipeName);
                start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                m_startupStage = "launching the Server child";
                m_process = new Process { StartInfo = start };
                RestartPipe.Require(m_process.Start(), "The Server child could not be launched.");
                WasStarted = true;
                m_stdout = DrainAsync(m_process.StandardOutput, new BoundedChildOutput());
                m_stderr = DrainAsync(m_process.StandardError, m_stderrOutput);
                m_exit = ObserveExitAsync(m_process);

                m_startupStage = "authenticating the two local IPC peers";
                using var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
                var connections = Task.WhenAll(
                    RestartPipe.AcceptAsync(m_authority, m_process.Id, connecting.Token),
                    RestartPipe.AcceptAsync(m_commands, m_process.Id, connecting.Token));
                Task first = await Task.WhenAny(connections, m_exit).WaitAsync(ct).ConfigureAwait(false);
                if (ReferenceEquals(first, m_exit))
                {
                    await connecting.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await connections.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Observe the cancelled connection tasks; an early process exit is a failure, not readiness.
                    }
                    throw new InvalidOperationException("The Server child exited during authenticated IPC setup.");
                }
                await connections.ConfigureAwait(false);
                m_startupStage = "awaiting Server readiness";
                m_authorityLoop = ServeAuthorityAsync(m_lifetime.Token);
                await m_ready.Task.WaitAsync(ct).ConfigureAwait(false);
                m_startupStage = "ready";
            }

            /// <summary>
            /// Adds child evidence without replacing the original failure or inventing a fresh timeout.
            /// The outer supervisor still owns kill/exit cleanup before any run directory can be removed.
            /// </summary>
            private async Task<InvalidOperationException> DescribeStartupFailureAsync(
                Exception failure, CancellationToken ct)
            {
                bool observationBudgetExpired = false;
                string drainFailure = string.Empty;
                if (WasStarted)
                {
                    try
                    {
                        // EOF can precede the child's exception printing. Allow its exit and bounded
                        // stderr drain only within the caller's original remaining cancellation budget.
                        await m_exit.WaitAsync(ct).ConfigureAwait(false);
                        _ = await m_stderr.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        observationBudgetExpired = true;
                    }
                    catch (IOException exception)
                    {
                        drainFailure = $" stderr observation failed: {exception.GetType().Name}.";
                    }
                }
                string state = !WasStarted
                    ? "not launched"
                    : m_process!.HasExited
                        ? $"exited with code {m_process.ExitCode.ToString(CultureInfo.InvariantCulture)}"
                        : "still running";
                string captured = m_stderrOutput.Snapshot();
                if (captured.Length == 0)
                {
                    captured = "<empty>";
                }
                return new InvalidOperationException(
                    $"Server child startup failed while {m_startupStage}; " +
                    $"authority stage={Volatile.Read(ref m_authorityStage)}, " +
                    $"last completed authority command={Volatile.Read(ref m_lastAuthorityCommand)}; " +
                    $"child {state}; exitObserved={ExitObserved}; " +
                    $"observationBudgetExpired={observationBudgetExpired}." +
                    drainFailure +
                    Environment.NewLine +
                    $"Captured child stderr (first {BoundedChildOutput.MaximumCharacters} characters):" +
                    Environment.NewLine +
                    captured, failure);
            }

            internal async Task ArmAsync(int amount, CancellationToken ct)
            {
                RestartPipe.Require(m_effectCommitted == null || m_effectCommitted.Task.IsCompleted,
                    "The previous business-effect commit observation is unfinished.");
                m_effectCommitted = new TaskCompletionSource<RestartSnapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                await m_commandClient.SendAsync(RestartCommand.Arm, writer => writer.Write(amount), ct)
                    .ConfigureAwait(false);
            }

            internal Task<RestartSnapshot> WaitForEffectCommitAsync(CancellationToken ct)
            {
                return (m_effectCommitted?.Task ?? throw new InvalidOperationException("Arm the handler first."))
                    .WaitAsync(ct);
            }

            internal ValueTask ReleaseAsync(CancellationToken ct)
            {
                return m_commandClient.SendAsync(RestartCommand.Release, null, ct);
            }

            internal ValueTask RequireUnavailableTimeAsync(CancellationToken ct)
            {
                return m_commandClient.SendAsync(RestartCommand.RequireUnavailableTime, null, ct);
            }

            internal void RequireUnprovenTermination()
            {
                RestartPipe.Require(WasStarted && !m_process!.HasExited,
                    "The termination-negative check requires the actual launched child to still be alive.");
                m_owner.RequireUnprovenTermination(this);
            }

            internal async Task<RestartSnapshot> SnapshotAsync(CancellationToken ct)
            {
                byte[] reply = await m_commandClient.ExchangeAsync(RestartCommand.Snapshot, null, ct)
                    .ConfigureAwait(false);
                try
                {
                    using BinaryReader reader = RestartPipe.Reader(reply, reply: true);
                    var snapshot = new RestartSnapshot(reader.ReadInt32(), reader.ReadInt32());
                    int dispatchAttempts = reader.ReadInt32();
                    RestartPipe.End(reader);
                    RestartPipe.Require(dispatchAttempts == Volatile.Read(ref m_effectNotifications),
                        "The ordinary handler was redispatched, even though its fence may have rejected that attempt.");
                    return snapshot;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(reply);
                }
            }

            internal async Task KillAsync(CancellationToken ct)
            {
                Process process = m_process ?? throw new InvalidOperationException("No launched child.");
                RestartPipe.Require(!process.HasExited, "The child exited before the requested fault boundary.");
                // Never select a process by name, reacquire a PID, or kill an unrelated process tree.
                process.Kill(entireProcessTree: false);
                await m_exit.WaitAsync(ct).ConfigureAwait(false);
                RestartPipe.Require(ExitObserved && process.HasExited, "An actual Server child exit was not observed.");
                m_owner.KilledChildren++;
            }

            internal async Task StopAsync(CancellationToken ct)
            {
                await m_commandClient.SendAsync(RestartCommand.Stop, null, ct).ConfigureAwait(false);
                await m_exit.WaitAsync(ct).ConfigureAwait(false);
                RestartPipe.Require(m_process!.ExitCode == 0,
                    "The last Server child did not shut down cleanly: " + await m_stderr.ConfigureAwait(false));
            }

            private async Task ObserveExitAsync(Process process)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                Volatile.Write(ref m_exitObserved, true);
                var failure = new InvalidOperationException("The Server child exited before an expected phase event.");
                m_ready.TrySetException(failure);
                m_effectCommitted?.TrySetException(failure);
            }

            private async Task ServeAuthorityAsync(CancellationToken ct)
            {
                bool bootstrapped = false;
                try
                {
                    while (true)
                    {
                        Volatile.Write(ref m_authorityStage, "reading an authority frame");
                        byte[] request = await RestartPipe.ReadAsync(m_authority, ct).ConfigureAwait(false);
                        BinaryReader? reader = null;
                        try
                        {
                            // Every switch arm/failed await reaches the per-frame finally below.
                            // CA2000 loses that disposal proof across this asynchronous receive loop.
                            // TODO: Remove when CA2000 recognizes per-frame async receive-loop disposal.
#pragma warning disable CA2000
                            reader = RestartPipe.Reader(request);
#pragma warning restore CA2000
                            var command = (RestartCommand)reader.ReadByte();
                            Volatile.Write(ref m_authorityStage, $"handling {command}");
                            RestartPipe.Require(bootstrapped || command == RestartCommand.Bootstrap,
                                "The private IPC peer has not bootstrapped.");
                            switch (command)
                            {
                                case RestartCommand.Bootstrap:
                                    RestartPipe.Require(!bootstrapped, "The private bootstrap is single-use.");
                                    RestartPipe.End(reader);
                                    _ = await m_owner.ReadTimeAsync(ct).ConfigureAwait(false);
                                    await RestartPipe.WriteAsync(m_authority, writer =>
                                    {
                                        writer.Write((byte)1);
                                        m_owner.m_configuration.Write(writer);
                                        m_owner.m_secrets.Write(writer);
                                    }, ct).ConfigureAwait(false);
                                    bootstrapped = true;
                                    break;
                                case RestartCommand.Time:
                                    RestartPipe.End(reader);
                                    RecoverableExecutionTime time = await m_owner.ReadTimeAsync(ct)
                                        .ConfigureAwait(false);
                                    await RestartPipe.WriteAsync(m_authority, writer =>
                                    {
                                        writer.Write((byte)1);
                                        RestartPipe.WriteId(writer, time.DomainId);
                                        writer.Write(time.Ticks);
                                    }, ct).ConfigureAwait(false);
                                    break;
                                case RestartCommand.Acquire:
                                    Uuid admitted = RestartPipe.ReadId(reader);
                                    RecoverableExecutionOwnerId admittedOwner = RestartConfiguration.ReadOwner(reader);
                                    RestartPipe.End(reader);
                                    m_owner.Register(this, admitted, admittedOwner);
                                    await ReplyAsync(ct).ConfigureAwait(false);
                                    break;
                                case RestartCommand.Recover:
                                    Uuid recovered = RestartPipe.ReadId(reader);
                                    RecoverableExecutionOwnerId recoveredOwner = RestartConfiguration.ReadOwner(reader);
                                    RestartPipe.End(reader);
                                    bool terminated = m_owner.Recover(recovered, recoveredOwner);
                                    await RestartPipe.WriteAsync(m_authority, writer =>
                                    {
                                        writer.Write((byte)1);
                                        writer.Write(terminated);
                                    }, ct).ConfigureAwait(false);
                                    break;
                                case RestartCommand.Ready:
                                    RestartPipe.End(reader);
                                    RestartPipe.Require(m_ready.TrySetResult(), "The child reported readiness twice.");
                                    await ReplyAsync(ct).ConfigureAwait(false);
                                    break;
                                case RestartCommand.EffectCommitted:
                                    var snapshot = new RestartSnapshot(reader.ReadInt32(), reader.ReadInt32());
                                    RestartPipe.End(reader);
                                    Interlocked.Increment(ref m_effectNotifications);
                                    RestartPipe.Require(
                                        m_effectCommitted != null && m_effectCommitted.TrySetResult(snapshot),
                                        "An unexpected/repeated business-effect commit was reported.");
                                    await ReplyAsync(ct).ConfigureAwait(false);
                                    break;
                                default:
                                    throw new InvalidOperationException("Unknown private authority command.");
                            }
                            Volatile.Write(ref m_lastAuthorityCommand, command.ToString());
                        }
                        finally
                        {
                            try
                            {
                                reader?.Dispose();
                            }
                            finally
                            {
                                CryptographicOperations.ZeroMemory(request);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    m_ready.TrySetException(exception);
                    m_effectCommitted?.TrySetException(exception);
                    // Closing the authority never returns cached/reset time or grants termination.
                    await m_authority.DisposeAsync().ConfigureAwait(false);
                }
            }

            private ValueTask ReplyAsync(CancellationToken ct)
            {
                return RestartPipe.WriteAsync(m_authority, writer => writer.Write((byte)1), ct);
            }

            private static async Task<string> DrainAsync(StreamReader reader, BoundedChildOutput diagnostic)
            {
                char[] buffer = new char[1024];
                int count;
                while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
                {
                    diagnostic.Append(buffer, count);
                }
                return diagnostic.Snapshot();
            }

            /// <summary>
            /// Drains all output but retains a bounded, thread-safe snapshot even before process exit.
            /// </summary>
            private sealed class BoundedChildOutput
            {
                internal const int MaximumCharacters = 8192;

                internal void Append(char[] buffer, int count)
                {
                    lock (m_gate)
                    {
                        int keep = Math.Min(count, MaximumCharacters - m_text.Length);
                        if (keep > 0)
                        {
                            m_text.Append(buffer, 0, keep);
                        }
                    }
                }

                internal string Snapshot()
                {
                    lock (m_gate)
                    {
                        return m_text.ToString();
                    }
                }

                private readonly Lock m_gate = new();
                private readonly StringBuilder m_text = new();
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                try
                {
                    if (WasStarted)
                    {
                        if (!m_process!.HasExited)
                        {
                            m_process.Kill(entireProcessTree: false);
                        }
                        // A cleanup bound is not termination evidence. If it expires, retain the run directory.
                        await m_exit.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await m_lifetime.CancelAsync().ConfigureAwait(false);
                    m_authority.Dispose();
                    await m_commandClient.DisposeAsync().ConfigureAwait(false);
                    m_commands.Dispose();
                    await m_authorityLoop.ConfigureAwait(false);
                    if (!WasStarted || ExitObserved)
                    {
                        _ = await m_stdout.ConfigureAwait(false);
                        _ = await m_stderr.ConfigureAwait(false);
                        m_process?.Dispose();
                    }
                    m_lifetime.Dispose();
                }
            }

            private readonly RestartSupervisor m_owner;
            private readonly string m_pipeName;
            private readonly NamedPipeServerStream m_authority;
            private readonly NamedPipeServerStream m_commands;
            private readonly RestartPipe m_commandClient;
            private readonly CancellationTokenSource m_lifetime = new();
            private readonly TaskCompletionSource m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TaskCompletionSource<RestartSnapshot>? m_effectCommitted;
            private Process? m_process;
            private Task m_exit = Task.CompletedTask;
            private Task m_authorityLoop = Task.CompletedTask;
            private Task<string> m_stdout = Task.FromResult(string.Empty);
            private Task<string> m_stderr = Task.FromResult(string.Empty);
            private readonly BoundedChildOutput m_stderrOutput = new();
            private string m_startupStage = "not started";
            private string m_authorityStage = "not started";
            private string m_lastAuthorityCommand = "none";
            private bool m_exitObserved;
            private int m_effectNotifications;
            private int m_disposed;
        }

        private readonly List<Child> m_children = [];
        private readonly RestartConfiguration m_configuration = configuration;
        private readonly string? m_childHostPath = childHostPath;

        // The caller owns these secrets beyond supervisor disposal through Client/PKI cleanup.
        // TODO: Remove when CA2213 recognizes this borrowed run-lifetime ownership.
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "Borrowed run-lifetime secrets. RunCoreAsync disposes them after the supervisor " +
                "and Client/PKI resources.")]
        private readonly RestartSecrets m_secrets = secrets;

        private readonly Dictionary<Uuid, (RecoverableExecutionOwnerId Owner, Child Child)> m_executions = [];
        private readonly HashSet<Uuid> m_unproven = [];
        private readonly Lock m_fences = new();
        private readonly SemaphoreSlim m_timeGate = new(1, 1);
        private RecoverableExecutionTime m_lastTime = initialTime;
        private Uuid m_interruptedExecutionId;
        private bool m_timeLost;
    }

    internal readonly record struct RestartSnapshot(int Invocations, int Value);
}
