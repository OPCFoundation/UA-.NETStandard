// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Assembly-level setup/teardown that verifies no Certificate
    /// instances are leaked during the test run. Uses TUnit's assembly
    /// hooks (the AOT test project does not use NUnit).
    /// </summary>
    public static class LeakDetectionSetup
    {
        [Before(Assembly)]
        public static void GlobalSetup(AssemblyHookContext context)
        {
            Certificate.ResetLeakCounters();
        }

        [After(Assembly)]
        public static void GlobalTeardown(AssemblyHookContext context)
        {
            // The certificate leak count is driven by explicit Dispose, not by
            // finalizers, so it needs no forced GC/finalizer sweep (a no-op for
            // the count that can hang on unrelated server/socket finalizers).
            // A positive count is re-checked over a short, hard-bounded poll
            // first so an in-flight background disposal is not misreported as a
            // leak (no finalizer wait, so it cannot hang).
            long leaked = Certificate.InstancesLeaked;
            for (int i = 0; leaked > 0 && i < 50; i++)
            {
                Thread.Sleep(100);
                leaked = Certificate.InstancesLeaked;
            }
            if (leaked > 0)
            {
                // TUnit has no Assert.Warn; throwing from the assembly hook
                // fails the run, which is the intended hard error.
                throw new InvalidOperationException(
                    $"Certificate leak detected: {leaked} instance(s) created " +
                    $"but not disposed (created={Certificate.InstancesCreated}, " +
                    $"disposed={Certificate.InstancesDisposed}).");
            }
        }
    }
}
