// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    /// <summary>
    /// Measures the cost of capturing type registration snapshots at increasing model sizes.
    /// </summary>
    [TestFixture]
    public sealed class TypeTableSnapshotPerformanceTests
    {
        /// <summary>
        /// Reports snapshot time and allocation without imposing a machine-dependent threshold.
        /// </summary>
        /// <param name="typeCount">The number of registered types in the source model.</param>
        [TestCase(128)]
        [TestCase(2048)]
        [TestCase(16384)]
        [Explicit("Run alone on a quiet host to compare snapshot scaling.")]
        public void CaptureSnapshotReportsTimeAndAllocation(int typeCount)
        {
            var namespaceUris = new NamespaceTable();
            var typeTable = new TypeTable(namespaceUris);
            for (int ii = 1; ii <= typeCount; ii++)
            {
                typeTable.AddSubtype(new NodeId((uint)ii), NodeId.Null);
            }

            _ = typeTable.CaptureSnapshot(out _, out _);
            _ = typeTable.CaptureSnapshot(out _, out _);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int operationCount = 5;
            var snapshots = new TypeTable[operationCount];
            long captureAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var captureStopwatch = Stopwatch.StartNew();
            for (int ii = 0; ii < operationCount; ii++)
            {
                snapshots[ii] = typeTable.CaptureSnapshot(out _, out _);
            }
            captureStopwatch.Stop();
            long captureAllocated = GC.GetAllocatedBytesForCurrentThread() - captureAllocatedBefore;

            long mutationAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var mutationStopwatch = Stopwatch.StartNew();
            for (int ii = 0; ii < operationCount; ii++)
            {
                snapshots[ii].AddSubtype(new NodeId((uint)(typeCount + 100_000 + ii)), NodeId.Null);
            }
            mutationStopwatch.Stop();
            long mutationAllocated = GC.GetAllocatedBytesForCurrentThread() - mutationAllocatedBefore;

            TestContext.Out.WriteLine(
                $"Types: {typeCount}; captures: {operationCount}; elapsed: {captureStopwatch.Elapsed.TotalMilliseconds:F3} ms; " +
                $"allocated: {captureAllocated} bytes; per capture: {captureAllocated / operationCount} bytes.");
            TestContext.Out.WriteLine(
                $"Types: {typeCount}; first mutations: {operationCount}; " +
                $"elapsed: {mutationStopwatch.Elapsed.TotalMilliseconds:F3} ms; " +
                $"allocated: {mutationAllocated} bytes; per mutation: {mutationAllocated / operationCount} bytes.");
            Assert.That(snapshots, Has.All.Not.Null);
        }
    }
}
