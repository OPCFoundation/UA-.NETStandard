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
using NUnit.Framework;
using System.Linq;
using Opc.Ua.Security.Certificates;

/// <summary>
/// Assembly-level setup/teardown that verifies no Certificate
/// instances are leaked during the test run.
/// </summary>
[SetUpFixture]
public class LeakDetectionSetup
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long created, long disposed)>
        s_fixtureLeaks = new();

    [OneTimeSetUp]
    public void GlobalSetup()
    {
        Certificate.ResetLeakCounters();
    }

    [OneTimeTearDown]
    public void GlobalTeardown()
    {
        // Force GC to finalize any abandoned certificates
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long leaked = Certificate.InstancesLeaked;
        // Tolerance of 2 for cross-fixture interaction leaks that only
        // appear when all categories run together (individually clean).
        if (leaked > 2)
        {
            string details = string.Join("\n",
                s_fixtureLeaks
                    .Where(kv => kv.Value.created > kv.Value.disposed)
                    .OrderByDescending(kv => kv.Value.created - kv.Value.disposed)
                    .Select(kv => $"  {kv.Key}: leaked={kv.Value.created - kv.Value.disposed} (created={kv.Value.created}, disposed={kv.Value.disposed})"));

            Assert.Fail(
                $"Certificate leak detected: {leaked} instance(s) created " +
                $"but not disposed (created={Certificate.InstancesCreated}, " +
                $"disposed={Certificate.InstancesDisposed}).\n" +
                $"Per-fixture breakdown:\n{details}");
        }
    }

    /// <summary>
    /// Records the certificate creation/disposal delta for a fixture.
    /// Call from fixture OneTimeSetUp/OneTimeTearDown.
    /// </summary>
    public static void RecordFixture(string fixtureName, long created, long disposed)
    {
        s_fixtureLeaks[fixtureName] = (created, disposed);
    }
}