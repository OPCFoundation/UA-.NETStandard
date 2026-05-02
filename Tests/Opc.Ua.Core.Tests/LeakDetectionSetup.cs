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
using NUnit.Framework.Interfaces;
using System.Linq;
using Opc.Ua.Security.Certificates;

// Apply leak tracking action to all test fixtures in the assembly.
[assembly: CoreLeakDetectionFixtureAction]

/// <summary>
/// Assembly-level setup/teardown that verifies no Certificate
/// instances are leaked during the test run.
/// </summary>
[SetUpFixture]
public class CoreLeakDetectionSetup
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
        // Force GC to finalize any abandoned certificates. Multiple
        // cycles ensure that finalizable objects whose finalizer
        // creates new garbage are themselves collected.
        for (int i = 0; i < 5; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        long leaked = Certificate.InstancesLeaked;
        string summary = $"CoreLeakDetectionSetup: tracked {s_fixtureLeaks.Count} fixtures, " +
            $"created={Certificate.InstancesCreated}, " +
            $"disposed={Certificate.InstancesDisposed}, leaked={leaked}";
        Console.WriteLine(summary);
        try
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "core-leak-summary.txt");
            System.IO.File.AppendAllText(path, summary + "\n");
            // Dump ALL fixtures sorted by net delta (positive first, negative last).
            System.IO.File.AppendAllText(path, "\nALL FIXTURES (positive net first):\n");
            foreach (var kv in s_fixtureLeaks
                .OrderByDescending(kv => kv.Value.created - kv.Value.disposed))
            {
                long net = kv.Value.created - kv.Value.disposed;
                System.IO.File.AppendAllText(path,
                    $"  net={net,4} created={kv.Value.created,4} disposed={kv.Value.disposed,4}  {kv.Key}\n");
            }
#if DEBUG
            // In Debug builds, also dump the allocation stack of any
            // live (reachable) certificate that still has a positive
            // refcount. These are the actual leaks.
            System.IO.File.AppendAllText(path, "\nLIVE LEAKED CERTIFICATES (DEBUG):\n");
            foreach (var live in Certificate.EnumerateLiveCertificates())
            {
                System.IO.File.AppendAllText(path,
                    $"\n  Thumbprint={live.Thumbprint}, RefCount={live.RefCount}, " +
                    $"CreatedAt={live.CreatedAt:O}\n  StackTrace:\n{live.StackTrace}\n");
            }
            // Certificates whose finaliser ran while their refcount was
            // still > 0 (AddRef without matching Dispose).
            System.IO.File.AppendAllText(path, "\nFINALIZED-WITH-LEAKED-REF CERTIFICATES (DEBUG):\n");
            foreach (var fin in Certificate.EnumerateFinalizedLeakedCertificates())
            {
                System.IO.File.AppendAllText(path,
                    $"\n  Thumbprint={fin.Thumbprint}, " +
                    $"CreatedAt={fin.CreatedAt:O}\n  StackTrace:\n{fin.StackTrace}\n");
            }
#endif
        }
        catch
        {
            // ignore
        }
        if (leaked > 0)
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
    /// </summary>
    public static void RecordFixture(string fixtureName, long created, long disposed)
    {
        s_fixtureLeaks.AddOrUpdate(
            fixtureName,
            (created, disposed),
            (_, prev) => (prev.created + created, prev.disposed + disposed));
    }
}

/// <summary>
/// Records Certificate counter deltas per individual test so leaks can
/// be attributed to a specific fixture or test. Lightweight: no per-
/// test GC; the global teardown is responsible for the final GC.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CoreLeakDetectionFixtureAction : Attribute, ITestAction
{
    private static readonly System.Threading.AsyncLocal<(long created, long disposed, string name)> s_baseline
        = new();

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        if (!test.IsSuite)
        {
            string parent = test.Parent?.FullName ?? test.FullName;
            s_baseline.Value = (
                Certificate.InstancesCreated,
                Certificate.InstancesDisposed,
                parent);
        }
    }

    public void AfterTest(ITest test)
    {
        if (!test.IsSuite)
        {
            long createdDelta = Certificate.InstancesCreated - s_baseline.Value.created;
            long disposedDelta = Certificate.InstancesDisposed - s_baseline.Value.disposed;
            if (createdDelta > 0 || disposedDelta > 0)
            {
                CoreLeakDetectionSetup.RecordFixture(
                    s_baseline.Value.name,
                    createdDelta,
                    disposedDelta);
            }
        }
    }
}