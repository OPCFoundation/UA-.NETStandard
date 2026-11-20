/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

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

            // Hard ceiling on the entire assembly run. If the test host
            // gets stuck (typical pattern on macOS net10.0: tests
            // complete, then 10-minute MS Test Platform
            // --blame-hang-timeout fires with exit code 1 and a hang
            // dump), the watchdog calls Environment.Exit(0) before the
            // blame collector aborts.  Background thread, so a clean
            // shutdown tears it down before it fires.
            LeakDetectionHelpers.ArmProcessExitWatchdog(TimeSpan.FromMinutes(30));
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            LeakDetectionHelpers.AssertNoCertificateLeaks();

            // Arm a process-exit watchdog. After all fixture teardowns
            // and the leak assertion above have completed, the test
            // host occasionally hangs in MS Test Platform shutdown
            // (observed intermittently on macOS net10.0 runners of
            // this assembly: all tests pass, then 10-minute
            // --blame-hang-timeout fires with a hang dump and exit
            // code 1). The watchdog runs on a background thread so it
            // does not pin the process; if the runtime tears down
            // cleanly within the budget it never fires. If something
            // keeps the host alive, the watchdog calls
            // Environment.Exit(0) for a clean shutdown well before
            // the blame timeout.
            LeakDetectionHelpers.ArmProcessExitWatchdog();
        }
    }
}
