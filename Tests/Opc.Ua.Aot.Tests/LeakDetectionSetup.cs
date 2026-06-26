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
                System.Threading.Thread.Sleep(100);
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
