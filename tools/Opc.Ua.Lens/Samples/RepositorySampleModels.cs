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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Samples
{
    internal enum RepositorySampleId
    {
        ConsoleReferenceServer,
        PumpSoftwareUpdateSimulator
    }

    internal enum RepositorySampleBuildConfiguration
    {
        Debug,
        Release
    }

    internal enum RepositorySampleFramework
    {
        Net8,
        Net9,
        Net10
    }

    internal enum RepositorySampleBuildLayout
    {
        FrameworkDirectory,
        CurrentRuntimeDirectory
    }

    internal enum RepositorySamplePhase
    {
        RequiresConfiguration,
        Configured,
        Starting,
        AdvertisedReady,
        Stopping,
        Stopped,
        Failed,
        CleanupRequired
    }

    internal enum RepositorySampleFailure
    {
        None,
        Prerequisite,
        ResourceUnavailable,
        Startup,
        Readiness,
        ReadinessTimeout,
        OutputCapture,
        Canceled,
        ExitedBeforeReady,
        NonzeroExit,
        Cleanup
    }

    /// <summary>
    /// An explicit local trust decision. Never populate this from a workspace document.
    /// Only fixed managed build layouts are accepted; publish output is not supported.
    /// </summary>
    internal sealed record RepositorySampleSource(
        string Root,
        RepositorySampleBuildConfiguration Configuration,
        RepositorySampleFramework Framework,
        RepositorySampleBuildLayout Layout = RepositorySampleBuildLayout.FrameworkDirectory);

    internal sealed record RepositorySamplePrerequisite(bool CanLaunch, string Message);

    /// <summary>
    /// A finite run, including startup and cleanup budgets. Cancellation after admission
    /// drains ownership before completing; it cannot abandon a child process.
    /// </summary>
    internal sealed record RepositorySampleRunOptions
    {
        public int RunSeconds { get; init; } = 60;

        public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

        public TimeSpan ShutdownAllowance { get; init; } = TimeSpan.FromSeconds(10);

        public TimeSpan TerminationTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromMilliseconds(250);

        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(RunSeconds, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(RunSeconds, 300);
            ValidateBudget(StartupTimeout, TimeSpan.FromSeconds(60), nameof(StartupTimeout));
            ValidateBudget(ShutdownAllowance, TimeSpan.FromSeconds(30), nameof(ShutdownAllowance));
            ValidateBudget(TerminationTimeout, TimeSpan.FromSeconds(30), nameof(TerminationTimeout));
            ValidateBudget(ProbeInterval, TimeSpan.FromSeconds(5), nameof(ProbeInterval));
        }

        private static void ValidateBudget(TimeSpan value, TimeSpan maximum, string name)
        {
            if (value < TimeSpan.FromMilliseconds(1) || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name, "The sample time budget is outside its finite bounds.");
            }
        }
    }

    /// <summary>
    /// Discovery evidence, not peer trust, session authentication or operation authorization.
    /// The certificate digest belongs to a public certificate in this run's private PKI.
    /// </summary>
    internal sealed record RepositorySampleEvidence(
        Uri Endpoint,
        string ApplicationUri,
        string ApplicationName,
        string ProductUri,
        ByteString CertificateSha256);

    internal sealed record RepositorySampleSnapshot(
        RepositorySampleId Selection,
        RepositorySamplePhase Phase,
        string Message)
    {
        public Guid RunId { get; init; }

        public int? ProcessId { get; init; }

        public int? ExitCode { get; init; }

        public Uri? Endpoint { get; init; }

        public string? PrivatePkiRoot { get; init; }

        public RepositorySampleEvidence? Evidence { get; init; }

        public RepositorySampleFailure Failure { get; init; }

        /// <summary>
        /// A bounded tree-termination request was made. Natural exit can race that request.
        /// </summary>
        public bool ForcedTermination { get; init; }

        public bool OwnsResources { get; init; }

        public ArrayOf<RepositorySampleOutputLine> Output { get; init; } = [];

        public bool SecureConnectAuthorized => false;
    }

    /// <summary>
    /// The caller must authorize each explicit setup/start/stop operation. Construction
    /// and selection restore do not access sources, allocate run resources or launch.
    /// One service owns at most one run, including any failed-cleanup resources.
    /// </summary>
    internal interface IRepositorySampleService : IAsyncDisposable
    {
        RepositorySampleSnapshot Snapshot { get; }

        Task<RepositorySampleSnapshot> Completion { get; }

        Task<RepositorySampleSnapshot> ConfigureAsync(
            RepositorySampleId sample,
            RepositorySampleSource source,
            CancellationToken cancellationToken = default);

        Task RestoreSelectionAsync(
            RepositorySampleId sample,
            CancellationToken cancellationToken = default);

        Task<RepositorySampleSnapshot> StartAsync(
            RepositorySampleRunOptions options,
            CancellationToken cancellationToken = default);

        Task<RepositorySampleSnapshot> StopAsync(CancellationToken cancellationToken = default);
    }

    internal sealed class RepositorySampleException : Exception
    {
        public RepositorySampleException()
            : this(RepositorySampleFailure.Startup, "The repository sample operation failed.")
        {
        }

        public RepositorySampleException(string message)
            : this(RepositorySampleFailure.Startup, message)
        {
        }

        public RepositorySampleException(string message, Exception innerException)
            : this(RepositorySampleFailure.Startup, message, innerException)
        {
        }

        public RepositorySampleException(
            RepositorySampleFailure failure,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
        }

        public RepositorySampleFailure Failure { get; }
    }
}
