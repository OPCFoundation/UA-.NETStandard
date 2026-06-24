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
        /// Runs <paramref name="cycles"/> consecutive
        /// <see cref="GC.Collect(int, GCCollectionMode, bool)"/> +
        /// <see cref="GC.WaitForPendingFinalizers"/> cycles on a
        /// background thread, bounded by <paramref name="timeout"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="GC.WaitForPendingFinalizers"/> blocks
        /// indefinitely if any finalizer is stuck (waiting on a thread
        /// or lock that is itself stuck). Running it on a background
        /// thread that the caller joins with a timeout converts a
        /// process-killing hang during assembly teardown into a
        /// recoverable, observable event: the caller can emit a soft
        /// warning and let the test host shut down promptly (the
        /// runtime applies its own bounded finalizer execution at
        /// process exit).
        /// </para>
        /// <para>
        /// When the timeout is exceeded the sweep thread is left
        /// running (it is a background thread, so it does not prevent
        /// process exit) and any subsequent leak counters may be
        /// inaccurate.
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
        /// Runs the bounded finalizer sweep and then asserts that no
        /// <see cref="Certificate"/> instances were leaked during the test
        /// run (i.e. <see cref="Certificate.InstancesLeaked"/> is zero).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended to be called from an assembly-level
        /// <c>[OneTimeTearDown]</c>. Both a stuck-finalizer watchdog timeout
        /// and a positive leak count are reported as hard test failures via
        /// <see cref="Assert.Fail(string)"/> (a sweep timeout makes the leak
        /// counters unreliable, so it cannot be treated as a pass).
        /// </para>
        /// <para>
        /// In <c>DEBUG</c> builds the failure message additionally includes
        /// the allocation stack traces of every live (reachable) certificate
        /// with a positive refcount and of every certificate whose finalizer
        /// ran while still referenced, so the leak source is visible directly
        /// in the CI test output.
        /// </para>
        /// </remarks>
        /// <param name="detail">Optional caller-supplied breakdown (for
        /// example a per-fixture leak summary) appended to the failure
        /// message.</param>
        public static void AssertNoCertificateLeaks(string detail = null)
        {
            if (!TryRunFinalizerSweep())
            {
                Assert.Fail(
                    $"Finalizer sweep exceeded {DefaultFinalizerSweepTimeout.TotalSeconds:0}s " +
                    "watchdog; at least one finalizer is stuck. Certificate leak counts are " +
                    "unreliable, so the run cannot be considered leak-free.");
            }

            long leaked = Certificate.InstancesLeaked;
            if (leaked <= 0)
            {
                return;
            }

            var message = new StringBuilder();
            message.Append(CultureInfo.InvariantCulture,
                $"Certificate leak detected: {leaked} instance(s) created but not disposed " +
                $"(created={Certificate.InstancesCreated}, disposed={Certificate.InstancesDisposed}).");

            if (!string.IsNullOrEmpty(detail))
            {
                message.AppendLine();
                message.Append(detail);
            }

            AppendDebugLeakDumps(message);
            Assert.Fail(message.ToString());
        }

        /// <summary>
        /// Appends the DEBUG-only allocation stack traces of leaked
        /// certificates to <paramref name="message"/>. No-op in release
        /// builds (the per-instance tracking is compiled out).
        /// </summary>
        private static void AppendDebugLeakDumps(StringBuilder message)
        {
#if DEBUG
            message.AppendLine();
            message.AppendLine("LIVE LEAKED CERTIFICATES (DEBUG):");
            foreach ((string thumbprint, int refCount, DateTime createdAt, string stackTrace) in
                Certificate.EnumerateLiveCertificates())
            {
                message.AppendLine(CultureInfo.InvariantCulture,
                    $"  Thumbprint={thumbprint}, RefCount={refCount}, CreatedAt={createdAt:O}");
                message.AppendLine(CultureInfo.InvariantCulture, $"  StackTrace:\n{stackTrace}");
            }

            message.AppendLine("FINALIZED-WITH-LEAKED-REF CERTIFICATES (DEBUG):");
            foreach ((string thumbprint, DateTime createdAt, string stackTrace) in
                Certificate.EnumerateFinalizedLeakedCertificates())
            {
                message.AppendLine(CultureInfo.InvariantCulture,
                    $"  Thumbprint={thumbprint}, CreatedAt={createdAt:O}");
                message.AppendLine(CultureInfo.InvariantCulture, $"  StackTrace:\n{stackTrace}");
            }
#endif
        }
    }
}
