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
        private static readonly TimeSpan s_finalizerSweepTimeout =
            TimeSpan.FromSeconds(30);

        [Before(Assembly)]
        public static void GlobalSetup(AssemblyHookContext context)
        {
            Certificate.ResetLeakCounters();
        }

        [After(Assembly)]
        public static void GlobalTeardown(AssemblyHookContext context)
        {
            // Force GC to finalize any abandoned certificates. Multiple
            // cycles ensure that finalizable objects whose finalizer
            // creates new garbage are themselves collected. The sweep
            // is bounded by a watchdog so a stuck finalizer cannot hang
            // the test host indefinitely during assembly teardown.
            // (The AOT project doesn't reference Opc.Ua.Test.Common, so
            // the same watchdog primitive is inlined here.)
            if (!TryRunFinalizerSweep(s_finalizerSweepTimeout))
            {
                Console.WriteLine(
                    $"[WARNING] Finalizer sweep exceeded {s_finalizerSweepTimeout.TotalSeconds:0}s " +
                    "watchdog; at least one finalizer is stuck. Leak counts below may be inaccurate.");
            }

            long leaked = Certificate.InstancesLeaked;
            if (leaked > 0)
            {
                // TUnit doesn't have Assert.Warn; log via Console (visible
                // in CI test output) without failing the assembly hook.
                Console.WriteLine(
                    $"[WARNING] Certificate leak detected: {leaked} instance(s) created " +
                    $"but not disposed (created={Certificate.InstancesCreated}, " +
                    $"disposed={Certificate.InstancesDisposed}).");
            }
        }

        private static bool TryRunFinalizerSweep(TimeSpan timeout)
        {
            var sweep = new Thread(() =>
            {
                for (int i = 0; i < 5; i++)
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
            return sweep.Join(timeout);
        }
    }
}
