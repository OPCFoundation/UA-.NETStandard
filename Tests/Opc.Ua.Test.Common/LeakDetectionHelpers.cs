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
using System.Threading;

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
    }
}
