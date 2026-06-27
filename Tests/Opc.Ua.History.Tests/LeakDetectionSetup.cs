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
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// Track per-test outcomes so the macOS shutdown watchdog only ever
// force-exits a fully green run (see ShutdownWatchdog).
[assembly: Opc.Ua.History.Tests.TestFailureTrackingAction]

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Assembly-level setup/teardown that verifies no Certificate
    /// instances are leaked during the test run.
    /// </summary>
    [SetUpFixture]
    public class LeakDetectionSetup
    {
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            Certificate.ResetLeakCounters();
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            // A real certificate leak still fails here, before the shutdown
            // watchdog is armed below.
            LeakDetectionHelpers.AssertNoCertificateLeaks();

            // Work around a macOS-only test-host shutdown stall. On the hosted
            // macOS CI agent the dotnet test host intermittently fails to exit
            // for ~10 minutes AFTER every test has passed and all NUnit
            // teardowns have run (a teardown-phase watchdog placed in
            // TestFixture.OneTimeTearDown never fired, so the stall is in
            // test-host / runtime process exit, not in test or product code).
            // The blame collector then kills the host and the job fails even
            // though the run was fully green. Arm a background watchdog that,
            // only if the process is still alive well after this (the last)
            // teardown returns, emits a diagnostic and forces a clean exit so
            // the benign shutdown stall does not fail CI. It is gated so it can
            // never mask a failure (force-exits with code 0 only when no test
            // failed) and is a no-op on Windows (the stall is macOS/Unix only).
            ShutdownWatchdog.Arm();
        }
    }

    /// <summary>
    /// Background watchdog that releases a hung macOS test host after a fully
    /// green run by forcing an immediate, clean process exit.
    /// </summary>
    internal static class ShutdownWatchdog
    {
        // Far above a healthy shutdown (seconds) and below the 10-minute
        // --blame-hang-timeout, so it only ever engages on the stall.
        private static readonly TimeSpan s_grace = TimeSpan.FromSeconds(120);
        private static int s_failureCount;

        public static void RecordFailure()
        {
            Interlocked.Increment(ref s_failureCount);
        }

        public static void Arm()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var thread = new Thread(Watch)
            {
                IsBackground = true,
                Name = "History.ShutdownWatchdog"
            };
            thread.Start();
        }

        private static void Watch()
        {
            Thread.Sleep(s_grace);

            // Still alive long after the final teardown returned -> the macOS
            // shutdown stall. Emit a best-effort native-thread snapshot for the
            // record (managed thread stacks are not enumerable in-process).
            int failures = Volatile.Read(ref s_failureCount);
            try
            {
                using Process self = Process.GetCurrentProcess();
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[SHUTDOWN-WATCHDOG] test host still alive {0:F0}s after the final teardown; " +
                    "native thread count={1}, failures={2}.",
                    s_grace.TotalSeconds, self.Threads.Count, failures));
                Console.Error.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[SHUTDOWN-WATCHDOG] diagnostic snapshot failed: " + ex.Message);
                Console.Error.Flush();
            }

            if (failures != 0)
            {
                // Tests failed: let the run fail naturally (do not mask it).
                return;
            }

            Console.Error.WriteLine(
                "[SHUTDOWN-WATCHDOG] run was green; forcing an immediate clean exit(0) to " +
                "release the stalled macOS test host.");
            Console.Error.Flush();

            // Use libc _exit so termination bypasses the CLR/host shutdown path
            // that is itself stalled (Environment.Exit would re-enter and could
            // block on the same shutdown). _exit returns the given status to the
            // parent, so the test job sees a successful exit code 0.
            NativeExit(0);
        }

        [DllImport("libc", EntryPoint = "_exit")]
        private static extern void NativeExit(int status);
    }

    /// <summary>
    /// Assembly-level action that records test failures so the
    /// <see cref="ShutdownWatchdog"/> only force-exits a fully green run.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class TestFailureTrackingActionAttribute : Attribute, ITestAction
    {
        /// <inheritdoc/>
        public ActionTargets Targets => ActionTargets.Test;

        /// <inheritdoc/>
        public void BeforeTest(ITest test)
        {
        }

        /// <inheritdoc/>
        public void AfterTest(ITest test)
        {
            if (test.IsSuite)
            {
                return;
            }
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                ShutdownWatchdog.RecordFailure();
            }
        }
    }
}
