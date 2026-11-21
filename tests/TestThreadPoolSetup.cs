// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using NUnit.Framework;

#pragma warning disable CA1050 // A SetUpFixture outside any namespace applies to the whole test assembly.

/// <summary>
/// Raises the thread pool minimum of the test host for every test assembly.
/// </summary>
/// <remarks>
/// The server no longer changes the process-wide thread pool. Up to 2.0.1 every
/// <c>ServerBase</c> raised the minimum to at least its <c>MinRequestThreadCount</c>
/// (10 by default), and test hosts on small CI runners depend on that minimum: tests
/// running in parallel block pool threads, and a queued request could otherwise wait
/// seconds for the thread pool to inject a thread.
/// </remarks>
[SetUpFixture]
public sealed class TestThreadPoolSetup
{
    private const int kMinThreads = 10;

    [OneTimeSetUp]
    public void RaiseThreadPoolMinimum()
    {
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(
            Math.Max(workerThreads, kMinThreads),
            Math.Max(completionPortThreads, kMinThreads));
    }
}
