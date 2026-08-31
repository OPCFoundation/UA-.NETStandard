/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Helpers shared by assembly-level leak-detection set-up fixtures
    /// across the test projects.
    /// </summary>
    public static class LeakDetectionHelpers
    {
        /// <summary>
        /// Default total time budget for <see cref="TryRunFinalizerSweep"/>.
        /// </summary>
        public static readonly TimeSpan DefaultFinalizerSweepTimeout =
            TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default grace period before <see cref="ArmProcessExitWatchdog"/>
        /// force-exits the test host.
        /// </summary>
        public static readonly TimeSpan DefaultProcessExitWatchdogTimeout =
            TimeSpan.FromSeconds(60);

        /// <summary>
        /// Runs <paramref name="cycles"/> consecutive
        /// <see cref="GC.Collect(int, GCCollectionMode, bool)"/> +
        /// <see cref="GC.WaitForPendingFinalizers"/> cycles on a
        /// background thread, bounded by <paramref name="timeout"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is an <b>opt-in best-effort</b> helper for callers that need to
        /// drain finalizers before taking a measurement (for example a memory
        /// snapshot in a soak test). It is deliberately <b>not</b> used by
        /// <see cref="AssertNoCertificateLeaks"/>: the certificate leak count is
        /// driven exclusively by explicit <c>Dispose</c> (it is incremented in
        /// <c>CertificateCore.Release</c>), never by a finalizer, so forcing a
        /// finalizer sweep cannot change <see cref="Certificate.InstancesLeaked"/>.
        /// Worse, in a process that hosts OPC UA servers the forced sweep blocks
        /// on unrelated finalizers (sockets, <c>SslStream</c>, listeners) and on a
        /// slow CI agent can hang the test host past the test platform's hang-dump
        /// timeout. Never call this from an assembly cert-leak teardown.
        /// </para>
        /// <para>
        /// <see cref="GC.WaitForPendingFinalizers"/> blocks indefinitely if any
        /// finalizer is stuck (waiting on a thread or lock that is itself stuck).
        /// Running it on a background thread that the caller joins with a timeout
        /// bounds the wait; on timeout the sweep thread is left running (it is a
        /// background thread, so it does not prevent process exit) and any
        /// subsequent measurement may be inaccurate.
        /// </para>
        /// </remarks>
        /// <param name="timeout">Total time budget for all cycles. When
        /// <c>null</c>, <see cref="DefaultFinalizerSweepTimeout"/> is
        /// used.</param>
        /// <param name="cycles">Number of GC+WaitForPendingFinalizers
        /// cycles to run. Multiple cycles ensure that finalizable
        /// objects whose finalizer creates new garbage are themselves
        /// collected.</param>
        /// <returns><c>true</c> if all cycles completed within the
        /// budget; <c>false</c> if at least one finalizer blocked long
        /// enough to exceed it.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool TryRunFinalizerSweep(
            TimeSpan? timeout = null,
            int cycles = 5)
        {
            if (cycles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cycles));
            }

            var sweep = new Thread(() =>
            {
                for (int i = 0; i < cycles; i++)
                {
                    GC.Collect(
                        GC.MaxGeneration,
                        GCCollectionMode.Forced,
                        blocking: true);
                    GC.WaitForPendingFinalizers();
                }
            })
            {
                IsBackground = true,
                Name = "LeakDetection.FinalizerSweep"
            };
            sweep.Start();
            return sweep.Join(timeout ?? DefaultFinalizerSweepTimeout);
        }

        /// <summary>
        /// Arms a background watchdog that force-exits the test host
        /// process if it has not exited within <paramref name="timeout"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The watchdog runs on an <see cref="Thread.IsBackground"/>
        /// thread so it does not by itself prevent process exit. If the
        /// runtime shuts down cleanly the thread is torn down before
        /// the watchdog fires.
        /// </para>
        /// <para>
        /// If a foreground/managed thread, native resource, or external
        /// data collector keeps the test host alive past the budget,
        /// the watchdog calls <see cref="Environment.Exit(int)"/> with
        /// exit code <c>0</c>. This converts the otherwise-fatal
        /// 10-minute MS Test Platform <c>--blame-hang-timeout</c> abort
        /// (which yields exit code 1 and a hang dump) into a clean
        /// shutdown.
        /// </para>
        /// <para>
        /// Intended for use at the end of an assembly-level
        /// <c>[OneTimeTearDown]</c> after all fixture teardowns and the
        /// finalizer sweep have completed. Calling it earlier risks
        /// terminating in-progress test work.
        /// </para>
        /// </remarks>
        /// <param name="timeout">How long to wait before force-exiting.
        /// When <c>null</c>, <see cref="DefaultProcessExitWatchdogTimeout"/>
        /// is used.</param>
        public static void ArmProcessExitWatchdog(TimeSpan? timeout = null)
        {
            TimeSpan budget = timeout ?? DefaultProcessExitWatchdogTimeout;
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(budget);
                // If we get here, the runtime has not torn this thread
                // down — something is keeping the process alive past the
                // watchdog budget. Force a clean exit so the MS Test
                // Platform --blame-hang-timeout abort does not fire.
                Environment.Exit(0);
            })
            {
                IsBackground = true,
                Name = "LeakDetection.ProcessExitWatchdog"
            };
            watchdog.Start();
        }

        /// <summary>
        /// Asserts that no <see cref="Certificate"/> instances were leaked
        /// during the test run (i.e. <see cref="Certificate.InstancesLeaked"/>
        /// is zero).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended to be called from an assembly-level
        /// <c>[OneTimeTearDown]</c>. A positive leak count is reported as a hard
        /// test failure via <see cref="Assert.Fail(string)"/>.
        /// </para>
        /// <para>
        /// The leak count is driven exclusively by explicit construction versus
        /// <c>Dispose</c> (the disposed counter is incremented in
        /// <c>CertificateCore.Release</c>, never by a finalizer). It therefore
        /// requires <b>no</b> forced GC / <see cref="GC.WaitForPendingFinalizers"/>
        /// sweep — which would be a no-op for the count and, in a process that
        /// hosts OPC UA servers, can block on unrelated finalizers (sockets,
        /// <c>SslStream</c>, listeners) and hang the test host on a slow CI agent.
        /// </para>
        /// <para>
        /// Some certificate disposals run on background cleanup tasks (for
        /// example fire-and-forget channel / session teardown) that may not have
        /// completed the instant the assembly teardown executes. To avoid
        /// misreporting such an in-flight disposal as a leak, a positive count is
        /// re-checked over a short, bounded poll (no finalizer wait, so it cannot
        /// hang). A genuine leak stays positive across the poll and still fails.
        /// </para>
        /// <para>
        /// When certificate allocation tracking is enabled, the failure message
        /// additionally includes allocation stack traces for live certificates
        /// with a positive refcount and unreachable handles that were not
        /// disposed, so the leak source is visible directly in the CI test
        /// output.
        /// </para>
        /// </remarks>
        /// <param name="detail">Optional caller-supplied breakdown (for
        /// example a per-fixture leak summary) appended to the failure
        /// message.</param>
        public static void AssertNoCertificateLeaks(string detail = null)
        {
            long leaked = WaitForOutstandingDisposals();
            if (leaked <= 0)
            {
                return;
            }

            var message = new StringBuilder();
            message.AppendFormat(CultureInfo.InvariantCulture,
                "Certificate leak detected: {0} instance(s) created but not disposed " +
                "(created={1}, disposed={2}).",
                leaked, Certificate.InstancesCreated, Certificate.InstancesDisposed);

            if (!string.IsNullOrEmpty(detail))
            {
                message.AppendLine()
                    .Append(detail);
            }

            string fixtureSummary = CertificateLeakAttribution.BuildSummary();
            if (!string.IsNullOrEmpty(fixtureSummary))
            {
                message.AppendLine()
                    .Append(fixtureSummary);
            }

            AppendLeakDumps(message);
            Assert.Fail(message.ToString());
        }

        /// <summary>
        /// Returns <see cref="Certificate.InstancesLeaked"/>, waiting first for the count to
        /// settle so that an in-flight background disposal (fire-and-forget channel / session
        /// cleanup) is not mistaken for a leak. The poll is hard-bounded and never calls
        /// <see cref="GC.WaitForPendingFinalizers"/>, so it cannot hang the test host. Suites
        /// with no outstanding instances return immediately.
        /// </summary>
        /// <remarks>
        /// Waiting a fixed budget cannot separate the two cases it needs to tell apart. A
        /// genuine leak holds the count steady, while a suite still draining its background
        /// disposals has a count that is falling - and on a loaded CI agent the drain can
        /// outlast any budget short enough to keep a real leak's failure prompt. This waits
        /// for <b>quiescence</b> instead: progress resets the clock, and the count is only
        /// reported once it has stopped moving. A real leak is therefore reported sooner than
        /// a fixed budget would, and a slow drain is given as long as it keeps making progress.
        /// The Sessions WSS fixtures can leave the final TLS callback cleanup queued slightly
        /// longer on loaded Linux CI agents, so the steady-count window is deliberately a few
        /// seconds rather than a sub-second race against that transport teardown.
        /// </remarks>
        /// <param name="maxAttempts">Hard cap on polls, so the wait always terminates.</param>
        /// <param name="delayMilliseconds">Delay between polls.</param>
        /// <param name="stableReadsRequired">Consecutive unchanged reads that count as settled.</param>
        public static long WaitForOutstandingDisposals(
            int maxAttempts = 600,
            int delayMilliseconds = 100,
            int stableReadsRequired = 50)
        {
            long leaked = Certificate.InstancesLeaked;
            if (leaked <= 0)
            {
                return leaked;
            }

            int stableReads = 0;
            for (int i = 0; i < maxAttempts; i++)
            {
                Thread.Sleep(delayMilliseconds);
                long current = Certificate.InstancesLeaked;
                if (current <= 0)
                {
                    return current;
                }

                if (current < leaked)
                {
                    // Still draining - a disposal completed since the last read, so do
                    // not start counting toward "settled" yet.
                    stableReads = 0;
                }
                else if (++stableReads >= stableReadsRequired)
                {
                    return current;
                }

                leaked = current;
            }
            return leaked;
        }

        /// <summary>
        /// Appends allocation stack traces of leaked certificates to
        /// <paramref name="message"/> when allocation tracking is enabled.
        /// </summary>
        internal static void AppendLeakDumps(StringBuilder message)
        {
            if (!Certificate.LeakTrackingEnabled)
            {
                message.AppendLine()
                    .AppendLine(
                        "Certificate allocation tracking was disabled. Set " +
                        "OPCUA_CERTIFICATE_LEAK_TRACKING=1 before starting the test host " +
                        "to include allocation stacks.");
                return;
            }

            message.AppendLine()
                .AppendLine("LIVE LEAKED CERTIFICATES:");
            int liveCount = 0;
            foreach ((
                string thumbprint,
                int refCount,
                DateTime createdAt,
                string stackTrace,
                string fixtureName) in
                Certificate.EnumerateLiveCertificates())
            {
                liveCount++;
                if (liveCount <= c_maxLeakDumpEntries)
                {
                    message.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "  Thumbprint={0}, RefCount={1}, CreatedAt={2:O}, Fixture={3}",
                        thumbprint,
                        refCount,
                        createdAt,
                        fixtureName ?? "(unattributed)");
                    message.AppendLine()
                        .AppendLine(FormattableString.Invariant($"  StackTrace:\n{stackTrace}"));
                }
            }
            AppendOmittedCount(message, liveCount);

            message.AppendLine("UNREACHABLE UNDISPOSED CERTIFICATES:");
            int unreachableCount = 0;
            foreach ((DateTime createdAt, string stackTrace, string fixtureName) in
                Certificate.EnumerateUnreachableUndisposedCertificates())
            {
                unreachableCount++;
                if (unreachableCount <= c_maxLeakDumpEntries)
                {
                    message.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "  CreatedAt={0:O}, Fixture={1}",
                        createdAt,
                        fixtureName ?? "(unattributed)");
                    message.AppendLine()
                        .AppendLine(FormattableString.Invariant($"  StackTrace:\n{stackTrace}"));
                }
            }
            AppendOmittedCount(message, unreachableCount);
        }

        /// <summary>
        /// Appends the number of leak entries omitted by the diagnostic cap.
        /// </summary>
        private static void AppendOmittedCount(StringBuilder message, int count)
        {
            if (count > c_maxLeakDumpEntries)
            {
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  ... {0} additional allocation(s) omitted.",
                    count - c_maxLeakDumpEntries);
                message.AppendLine();
            }
        }

        private const int c_maxLeakDumpEntries = 50;
    }
}
