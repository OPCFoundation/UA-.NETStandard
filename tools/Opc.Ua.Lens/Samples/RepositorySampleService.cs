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
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Samples
{
    /// <summary>
    /// Serializes admission and gives one lifetime task exclusive ownership of every
    /// process/resource operation. Stop joins that task; failed cleanup blocks new
    /// starts and retains the same handles for an explicit stop retry.
    /// </summary>
    internal sealed class RepositorySampleService : IRepositorySampleService
    {
        public RepositorySampleService(ITelemetryContext telemetry)
            : this(
                new RepositorySampleFiles(),
                new RepositorySamplePortAllocator(),
                new RepositorySampleRuntime(),
                new RepositorySampleDiscoveryProbe(telemetry),
                TimeProvider.System)
        {
        }

        public RepositorySampleService(
            RepositorySampleFiles files,
            IRepositorySamplePortAllocator ports,
            IRepositorySampleRuntime runtime,
            IRepositorySampleProbe probe,
            TimeProvider timeProvider)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_ports = ports ?? throw new ArgumentNullException(nameof(ports));
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_probe = probe ?? throw new ArgumentNullException(nameof(probe));
            m_time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_snapshot = RequiresConfiguration(RepositorySampleId.ConsoleReferenceServer);
            m_completion = Task.FromResult(m_snapshot);
        }

        public RepositorySampleSnapshot Snapshot
        {
            get
            {
                lock (m_gate)
                {
                    return m_active?.Output is RepositorySampleOutput output
                        ? m_snapshot with { Output = output.Snapshot }
                        : m_snapshot;
                }
            }
        }

        public Task<RepositorySampleSnapshot> Completion
        {
            get
            {
                lock (m_gate)
                {
                    return m_completion;
                }
            }
        }

        public Task<RepositorySampleSnapshot> ConfigureAsync(
            RepositorySampleId sample,
            RepositorySampleSource source,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                EnsureIdle();
                RepositorySamplePrerequisite prerequisite = m_files.Inspect(source, sample);
                m_source = source with { Root = RepositorySamplePaths.ValidateRoot(source.Root) };
                m_snapshot = new RepositorySampleSnapshot(
                    sample,
                    prerequisite.CanLaunch
                        ? RepositorySamplePhase.Configured
                        : RepositorySamplePhase.RequiresConfiguration,
                    prerequisite.Message);
                m_completion = Task.FromResult(m_snapshot);
                return m_completion;
            }
        }

        public Task RestoreSelectionAsync(
            RepositorySampleId sample,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = RepositorySampleCatalog.Get(sample);
            lock (m_gate)
            {
                EnsureIdle();
                m_source = null;
                m_snapshot = RequiresConfiguration(sample);
                m_completion = Task.FromResult(m_snapshot);
                return Task.CompletedTask;
            }
        }

        public Task<RepositorySampleSnapshot> StartAsync(
            RepositorySampleRunOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Run run;
            lock (m_gate)
            {
                EnsureIdle();
                RepositorySamplePrerequisite prerequisite = m_files.Inspect(m_source, m_snapshot.Selection);
                if (!prerequisite.CanLaunch || m_source is null)
                {
                    m_snapshot = RequiresConfiguration(m_snapshot.Selection) with { Message = prerequisite.Message };
                    throw new RepositorySampleException(RepositorySampleFailure.Prerequisite, prerequisite.Message);
                }
                run = new Run(m_snapshot.Selection, m_source, options, m_time, cancellationToken);
                m_active = run;
                m_snapshot = new(run.Sample, RepositorySamplePhase.Starting, "Preparing the owned repository sample.")
                {
                    OwnsResources = true
                };
                m_completion = run.Completion = ExecuteAsync(run);
                ObserveFault(m_completion);
            }
            run.Admitted.TrySetResult();
            return AwaitReadyAsync(run);
        }

        /// <summary>
        /// Cancellation applies before admission only. Accepted stop waits for the
        /// configured graceful deadline, then the bounded exact-handle fallback.
        /// These CLIs have no verified portable immediate-stop IPC.
        /// </summary>
        public Task<RepositorySampleSnapshot> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return StopAdmitted();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposeRequested = true;
                if (m_disposal is null || (m_disposal.IsFaulted && m_active is not null))
                {
                    m_disposal = DisposeCoreAsync();
                }
                return new ValueTask(m_disposal);
            }
        }

        private async Task<RepositorySampleSnapshot> ExecuteAsync(Run run)
        {
            await run.Admitted.Task.ConfigureAwait(false);
            using var gracefulCancellation = new CancellationTokenSource();
            run.GracefulDeadline = Task.Delay(RemainingGracefulBudget(run), m_time, gracefulCancellation.Token);
            bool observed = false;
            try
            {
                try
                {
                    TimeSpan remaining = run.Options.StartupTimeout - m_time.GetElapsedTime(run.StartedTimestamp);
                    using var deadline = new CancellationTokenSource(
                        remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, m_time);
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(
                        run.CallerCancellation, deadline.Token);
                    run.StartupToken = startup.Token;
                    if (remaining <= TimeSpan.Zero)
                    {
                        await deadline.CancelAsync().ConfigureAwait(false);
                    }
                    if (run.StopRequested)
                    {
                        await startup.CancelAsync().ConfigureAwait(false);
                    }
                    Task work = ExecuteOwnedRunAsync(run);
                    try
                    {
                        await Task.WhenAny(work, run.StopSignal.Task).ConfigureAwait(false);
                        if (run.StopRequested)
                        {
                            await startup.CancelAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await work.ConfigureAwait(false);
                    }
                    observed = true;
                }
                catch (OperationCanceledException) when (run.StartupToken.IsCancellationRequested)
                {
                    run.Failure = run.CallerCancellation.IsCancellationRequested || run.StopRequested
                        ? RepositorySampleFailure.Canceled
                        : RepositorySampleFailure.ReadinessTimeout;
                    run.Message = run.Failure == RepositorySampleFailure.Canceled
                        ? "Sample startup canceled; draining the exact owned process and resources."
                        : "Sample readiness exceeded its startup budget; no connection was authorized.";
                    observed = true;
                }
                catch (RepositorySampleException exception)
                {
                    run.Failure = exception.Failure;
                    run.Message = exception.Message;
                    observed = true;
                }
                catch (ServiceResultException exception)
                {
                    run.Failure = RepositorySampleFailure.Readiness;
                    run.Message = string.Format(
                        CultureInfo.InvariantCulture, "OPC UA discovery failed ({0}).", exception.StatusCode);
                    observed = true;
                }
                catch (Exception exception) when (
                    IsResourceFailure(exception) || exception is OperationCanceledException)
                {
                    run.Failure = run.Process is null
                        ? RepositorySampleFailure.Startup
                        : RepositorySampleFailure.Readiness;
                    run.Message = "The sample operation failed: " + DescribeFailure(exception) + ".";
                    observed = true;
                }
            }
            finally
            {
                if (!observed)
                {
                    run.Failure = RepositorySampleFailure.Startup;
                    run.Message = "An unexpected sample failure occurred; inspect the faulted Completion task.";
                }
                try
                {
                    await FinishAsync(run).ConfigureAwait(false);
                }
                finally
                {
                    await gracefulCancellation.CancelAsync().ConfigureAwait(false);
                }
            }
            return run.FinalSnapshot!;
        }

        private async Task ExecuteOwnedRunAsync(Run run)
        {
            run.StartupToken.ThrowIfCancellationRequested();
            run.Port = await m_ports.ReserveAsync(run.StartupToken).ConfigureAwait(false);
            run.Files = m_files.AllocateRun();
            await run.Files.InitializeAsync(run.StartupToken).ConfigureAwait(false);
            run.Launch = await m_files.PrepareAsync(
                run.Source, run.Sample, run.Port.Port, run.Options, run.Files, run.StartupToken)
                .ConfigureAwait(false);
            run.Output = new RepositorySampleOutput([run.Source.Root, run.Launch.Files.Root]);
            Publish(run, RepositorySamplePhase.Starting, "Starting the owned sample; no readiness claim yet.");
            run.StartupToken.ThrowIfCancellationRequested();
            await run.Port.ReleaseSocketForLaunchAsync().ConfigureAwait(false);
            run.Process = await m_runtime.StartAsync(run.Launch, run.Output, run.StartupToken).ConfigureAwait(false);
            Publish(run, RepositorySamplePhase.Starting, "Waiting for owned endpoint/application evidence.");
            run.StartupToken.ThrowIfCancellationRequested();
            await WaitForReadinessAsync(run).ConfigureAwait(false);
            await WaitForStopOrExitAsync(run).ConfigureAwait(false);
        }

        private async Task WaitForReadinessAsync(Run run)
        {
            RepositorySampleLaunch launch = run.Launch!;
            IRepositorySampleProcess process = run.Process!;
            while (true)
            {
                run.StartupToken.ThrowIfCancellationRequested();
                RejectCaptureFailure(run);
                await RejectEarlyExitAsync(run).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints;
                try
                {
                    endpoints = await m_probe.DiscoverAsync(launch.Endpoint, run.StartupToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsTransientDiscoveryFailure(exception))
                {
                    run.StartupToken.ThrowIfCancellationRequested();
                    string reason = exception switch
                    {
                        ServiceResultException service => service.StatusCode.ToString(),
                        SocketException socket => socket.SocketErrorCode.ToString(),
                        _ => throw new InvalidOperationException("Unexpected transient discovery failure.")
                    };
                    Publish(run, RepositorySamplePhase.Starting,
                        $"OPC UA discovery is not yet available ({reason}).");
                    await Task.Delay(run.Options.ProbeInterval, m_time, run.StartupToken).ConfigureAwait(false);
                    continue;
                }
                run.StartupToken.ThrowIfCancellationRequested();
                RepositorySampleEvidence? evidence = await RepositorySampleEvidenceVerifier.VerifyAsync(
                    launch, endpoints, run.StartupToken).ConfigureAwait(false);
                run.StartupToken.ThrowIfCancellationRequested();
                RejectCaptureFailure(run);
                await RejectEarlyExitAsync(run).ConfigureAwait(false);
                if (evidence is not null)
                {
                    lock (m_gate)
                    {
                        run.StartupToken.ThrowIfCancellationRequested();
                        if (run.StopRequested)
                        {
                            throw new OperationCanceledException(run.StartupToken);
                        }
                        if (process.Exit.IsCompleted)
                        {
                            throw new RepositorySampleException(
                                RepositorySampleFailure.ExitedBeforeReady, "The sample exited before readiness.");
                        }
                        run.Evidence = evidence;
                        run.WasReady = true;
                        Publish(
                            run,
                            RepositorySamplePhase.AdvertisedReady,
                            "Owned sample advertised readiness. Secure connection still requires peer trust.");
                        run.Ready.TrySetResult(m_snapshot);
                    }
                    return;
                }
                Publish(run, RepositorySamplePhase.Starting, "Waiting for the secure endpoint/owned certificate.");
                await Task.Delay(run.Options.ProbeInterval, m_time, run.StartupToken).ConfigureAwait(false);
            }
        }

        private static async Task RejectEarlyExitAsync(Run run)
        {
            if (run.Process!.Exit.IsCompleted)
            {
                run.ExitCode = await run.Process.Exit.ConfigureAwait(false);
                throw new RepositorySampleException(
                    RepositorySampleFailure.ExitedBeforeReady, "The sample exited before readiness was established.");
            }
        }

        private static void RejectCaptureFailure(Run run)
        {
            if (run.Output!.CaptureFailure.IsCompleted)
            {
                throw new RepositorySampleException(
                    RepositorySampleFailure.OutputCapture, "Capturing sample output failed; stopping the owned run.");
            }
        }

        private async Task WaitForStopOrExitAsync(Run run)
        {
            await Task.WhenAny(run.Process!.Exit, run.StopSignal.Task,
                run.GracefulDeadline ?? throw new InvalidOperationException("The sample deadline is unavailable."),
                run.Output!.CaptureFailure).ConfigureAwait(false);
            RejectCaptureFailure(run);
        }

        private async Task FinishAsync(Run run)
        {
            bool cleaned = false;
            try
            {
                await StopAndCleanAsync(run).ConfigureAwait(false);
                cleaned = true;
                if (run.Failure == RepositorySampleFailure.None && run.Output?.CaptureFailure.IsCompleted == true)
                {
                    run.Failure = RepositorySampleFailure.OutputCapture;
                    run.Message = "Capturing sample output failed; the owned run was stopped and cleaned.";
                }
                if (run.Failure == RepositorySampleFailure.None && run.ExitCode is not (null or 0))
                {
                    if (!run.ForcedTermination || !run.StopRequested)
                    {
                        run.Failure = RepositorySampleFailure.NonzeroExit;
                        run.Message = run.ForcedTermination
                            ? "The sample exceeded its bounded lifetime and required termination."
                            : "The sample exited with a nonzero exit code.";
                    }
                }
            }
            catch (Exception exception) when (IsResourceFailure(exception) || exception is OperationCanceledException)
            {
                run.Failure = RepositorySampleFailure.Cleanup;
                run.Message = "Sample cleanup failed: " +
                    DescribeFailure(exception) +
                    ". Ownership is retained. Retry Stop; do not start another sample.";
            }
            finally
            {
                lock (m_gate)
                {
                    RepositorySamplePhase phase = !cleaned
                        ? RepositorySamplePhase.CleanupRequired
                        : run.Failure is RepositorySampleFailure.None or RepositorySampleFailure.Canceled
                            ? RepositorySamplePhase.Stopped
                            : RepositorySamplePhase.Failed;
                    string message = run.Failure == RepositorySampleFailure.None
                        ? run.ForcedTermination
                            ? "The owned sample exited after a bounded process-tree termination request."
                            : "The sample exited and its owned run resources were removed."
                        : run.Message;
                    Publish(run, phase, message);
                    m_snapshot = m_snapshot with { OwnsResources = !cleaned };
                    run.FinalSnapshot = m_snapshot;
                    if (cleaned && ReferenceEquals(m_active, run))
                    {
                        m_active = null;
                    }
                    if (!run.WasReady)
                    {
                        if (run.Failure == RepositorySampleFailure.Canceled)
                        {
                            run.Ready.TrySetCanceled(new CancellationToken(canceled: true));
                        }
                        else
                        {
                            run.Ready.TrySetException(new RepositorySampleException(run.Failure, message));
                        }
                    }
                }
            }
        }

        private async Task StopAndCleanAsync(Run run)
        {
            if (run.Process is IRepositorySampleProcess process && !run.ProcessDisposed)
            {
                if (!process.Exit.IsCompleted)
                {
                    Publish(
                        run,
                        RepositorySamplePhase.Stopping,
                        "Waiting for the sample's configured graceful shutdown deadline.");
                    await Task.WhenAny(process.Exit, run.GracefulDeadline ??
                        throw new InvalidOperationException("The sample deadline is unavailable."))
                        .ConfigureAwait(false);
                    if (!process.Exit.IsCompleted)
                    {
                        run.ForcedTermination = true;
                        using var termination = new CancellationTokenSource(run.Options.TerminationTimeout, m_time);
                        await process.TerminateOwnedTreeAsync(termination.Token).ConfigureAwait(false);
                        await process.Exit.WaitAsync(termination.Token).ConfigureAwait(false);
                    }
                }
                run.ExitCode = await process.Exit.ConfigureAwait(false);
                run.ProcessDisposal ??= process.DisposeAsync().AsTask();
                await run.ProcessDisposal.WaitAsync(run.Options.TerminationTimeout, m_time).ConfigureAwait(false);
                run.ProcessDisposed = true;
            }
            if (run.Files is not null && !run.FilesRemoved)
            {
                await run.Files.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
                run.FilesRemoved = true;
            }
            if (run.Port is not null && !run.PortReleased)
            {
                await run.Port.DisposeAsync().ConfigureAwait(false);
                run.PortReleased = true;
            }
        }

        private Task<RepositorySampleSnapshot> StopAdmitted()
        {
            if (m_active is not Run run)
            {
                return Task.FromResult(m_snapshot);
            }
            run.RequestStop();
            TaskCompletionSource? admitted = null;
            if (run.Completion.IsCompleted)
            {
                run.Failure = RepositorySampleFailure.None;
                run.Message = string.Empty;
                admitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                m_completion = run.Completion = RetryCleanupAsync(run, admitted.Task);
                ObserveFault(m_completion);
            }
            Publish(run, RepositorySamplePhase.Stopping, "Stopping the owned repository sample.");
            admitted?.TrySetResult();
            return run.Completion;
        }

        private async Task<RepositorySampleSnapshot> RetryCleanupAsync(Run run, Task admitted)
        {
            await admitted.ConfigureAwait(false);
            await FinishAsync(run).ConfigureAwait(false);
            return run.FinalSnapshot!;
        }

        private async Task DisposeCoreAsync()
        {
            RepositorySampleSnapshot result = await StopAdmitted().ConfigureAwait(false);
            if (result.OwnsResources)
            {
                throw new RepositorySampleException(RepositorySampleFailure.Cleanup, result.Message);
            }
        }

        private TimeSpan RemainingGracefulBudget(Run run)
        {
            TimeSpan total = run.Options.StartupTimeout +
                TimeSpan.FromSeconds(run.Options.RunSeconds) +
                run.Options.ShutdownAllowance;
            TimeSpan remaining = total - m_time.GetElapsedTime(run.StartedTimestamp);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private void Publish(Run run, RepositorySamplePhase phase, string message)
        {
            lock (m_gate)
            {
                if (run.StopRequested &&
                    phase is RepositorySamplePhase.Starting or RepositorySamplePhase.AdvertisedReady)
                {
                    phase = RepositorySamplePhase.Stopping;
                    message = "Stopping the owned repository sample.";
                }
                m_snapshot = new RepositorySampleSnapshot(run.Sample, phase, message)
                {
                    RunId = run.Files?.RunId ?? Guid.Empty,
                    ProcessId = run.Process?.Id,
                    ExitCode = run.ExitCode,
                    Endpoint = run.Launch?.Endpoint,
                    PrivatePkiRoot = run.Files?.PkiRoot,
                    Evidence = phase == RepositorySamplePhase.AdvertisedReady ? run.Evidence : null,
                    Failure = run.Failure,
                    ForcedTermination = run.ForcedTermination,
                    OwnsResources = true,
                    Output = run.Output is null ? [] : run.Output.Snapshot
                };
            }
        }

        private void EnsureIdle()
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            if (m_active is not null)
            {
                throw new InvalidOperationException("A sample still owns a run; stop it before changing setup.");
            }
        }

        private static async Task<RepositorySampleSnapshot> AwaitReadyAsync(Run run)
        {
            await Task.WhenAny(run.Ready.Task, run.Completion).ConfigureAwait(false);
            if (run.Ready.Task.IsCompleted)
            {
                return await run.Ready.Task.ConfigureAwait(false);
            }
            RepositorySampleSnapshot result = await run.Completion.ConfigureAwait(false);
            throw new RepositorySampleException(result.Failure, result.Message);
        }

        private static RepositorySampleSnapshot RequiresConfiguration(RepositorySampleId sample)
        {
            return new RepositorySampleSnapshot(
                sample,
                RepositorySamplePhase.RequiresConfiguration,
                "Requires configuration: select a trusted local source root and managed build. Restore never starts.");
        }

        private static bool IsResourceFailure(Exception exception)
        {
            return exception is IOException or UnauthorizedAccessException or SocketException or Win32Exception or
                InvalidOperationException or XmlException or TimeoutException or
                RepositorySampleException or AggregateException;
        }

        private static bool IsTransientDiscoveryFailure(Exception exception)
        {
            if (exception is SocketException socket)
            {
                return socket.SocketErrorCode is SocketError.ConnectionRefused or
                    SocketError.ConnectionReset or SocketError.TimedOut;
            }
            if (exception is not ServiceResultException service)
            {
                return false;
            }
            StatusCode code = service.StatusCode;
            return code == StatusCodes.BadCommunicationError ||
                code == StatusCodes.BadConnectionClosed ||
                code == StatusCodes.BadServerNotConnected ||
                code == StatusCodes.BadServerHalted ||
                code == StatusCodes.BadNoCommunication ||
                ReconnectPolicy.IsServerBusySignal(code);
        }

        private static string DescribeFailure(Exception exception)
        {
            return exception switch
            {
                IOException => "local file or pipe I/O failed",
                UnauthorizedAccessException => "local access was denied",
                SocketException socket => $"a socket operation failed ({socket.SocketErrorCode})",
                Win32Exception => "the sample process operation failed",
                XmlException => "the sample configuration XML is invalid",
                TimeoutException => "the operation exceeded its time budget",
                OperationCanceledException => "an underlying operation was canceled",
                RepositorySampleException => "the sample operation was rejected",
                AggregateException => "owned operations reported multiple errors",
                InvalidOperationException => "the owned runtime is not in the expected state",
                _ => throw new ArgumentException("Unsupported sample resource failure.", nameof(exception))
            };
        }

        private static void ObserveFault(Task task)
        {
            // Keep the original fault awaitable while the explicit failed snapshot
            // also covers a UI that is not currently awaiting Completion.
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private readonly RepositorySampleFiles m_files;
        private readonly IRepositorySamplePortAllocator m_ports;
        private readonly IRepositorySampleRuntime m_runtime;
        private readonly IRepositorySampleProbe m_probe;
        private readonly TimeProvider m_time;
        private readonly Lock m_gate = new();
        private RepositorySampleSnapshot m_snapshot;
        private Task<RepositorySampleSnapshot> m_completion;
        private RepositorySampleSource? m_source;
        private Run? m_active;
        private bool m_disposeRequested;
        private Task? m_disposal;

        /// <summary>
        /// Retained process/file/port state for cleanup retries. ExecuteAsync owns
        /// the shorter-lived cancellation sources; this state only keeps their token.
        /// </summary>
        private sealed class Run
        {
            public Run(
                RepositorySampleId sample,
                RepositorySampleSource source,
                RepositorySampleRunOptions options,
                TimeProvider time,
                CancellationToken callerCancellation)
            {
                Sample = sample;
                Source = source;
                Options = options;
                CallerCancellation = callerCancellation;
                StartedTimestamp = time.GetTimestamp();
            }

            public RepositorySampleId Sample { get; }

            public RepositorySampleSource Source { get; }

            public RepositorySampleRunOptions Options { get; }

            public CancellationToken CallerCancellation { get; }

            public CancellationToken StartupToken { get; set; }

            public Task? GracefulDeadline { get; set; }

            public long StartedTimestamp { get; }

            public bool StopRequested => StopSignal.Task.IsCompleted;

            public TaskCompletionSource Admitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource StopSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<RepositorySampleSnapshot> Ready { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<RepositorySampleSnapshot> Completion { get; set; } = null!;

            public IRepositorySamplePortLease? Port { get; set; }

            public RepositorySampleRunFiles? Files { get; set; }

            public RepositorySampleLaunch? Launch { get; set; }

            public RepositorySampleOutput? Output { get; set; }

            public IRepositorySampleProcess? Process { get; set; }

            public Task? ProcessDisposal { get; set; }

            public RepositorySampleEvidence? Evidence { get; set; }

            public RepositorySampleSnapshot? FinalSnapshot { get; set; }

            public RepositorySampleFailure Failure { get; set; }

            public string Message { get; set; } = string.Empty;

            public int? ExitCode { get; set; }

            public bool WasReady { get; set; }

            public bool ForcedTermination { get; set; }

            public bool ProcessDisposed { get; set; }

            public bool FilesRemoved { get; set; }

            public bool PortReleased { get; set; }

            public void RequestStop()
            {
                StopSignal.TrySetResult();
            }
        }
    }
}
