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
