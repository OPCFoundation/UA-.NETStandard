/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Tests.Desktop;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class AvaloniaWorkspaceDispatcherTests
{
    [Test]
    public Task VerifyAccessAcceptsUiOwnerAndRejectsWorkerThread()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            int owner = Environment.CurrentManagedThreadId;
            dispatcher.VerifyAccess();
            int worker = await Task.Run(() =>
            {
                Assert.That(dispatcher.VerifyAccess, Throws.InvalidOperationException);
                return Environment.CurrentManagedThreadId;
            }).ConfigureAwait(true);

            Assert.That(worker, Is.Not.EqualTo(owner));
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
            dispatcher.VerifyAccess();
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task InvokeAsyncExecutesInlineOrMarshalsToSameUiOwnerAndAwaitsCompletion(bool worker)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            int owner = Environment.CurrentManagedThreadId;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sequence = new List<string>();
            async Task ActionAsync()
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
                dispatcher.VerifyAccess();
                sequence.Add("entered");
                entered.SetResult();
                await release.Task.ConfigureAwait(true);
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
                sequence.Add("finished");
            }
            Task operation = worker
                ? Task.Run(() => dispatcher.InvokeAsync(ActionAsync))
                : dispatcher.InvokeAsync(ActionAsync);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                Assert.That(
                    sequence,
                    Is.EqualTo(s_invokeAsyncExecutesInlineOrMarshalsToSameUiOwnerAndAwaitsCompExpected));
                Assert.That(operation.IsCompleted, Is.False);
                release.SetResult();
                await operation.ConfigureAwait(true);
                Assert.That(
                    sequence,
                    Is.EqualTo(s_invokeAsyncExecutesInlineOrMarshalsToSameUiOwnerAndAwaitsCompExpected2));
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
            }
            finally
            {
                release.TrySetResult();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task PreCanceledInvocationDoesNotExecuteAction(bool worker)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(true);
            int calls = 0;
            Task InvokeAsync() => dispatcher.InvokeAsync(() =>
            {
                calls++;
                return Task.CompletedTask;
            }, cancellation.Token);

            await Assert.ThatAsync(() => worker ? Task.Run(InvokeAsync) : InvokeAsync(),
                Throws.InstanceOf<OperationCanceledException>()
                    .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(true);

            Assert.That(calls, Is.Zero);
            Assert.That(DesktopInteraction.Owner.IsVisible, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task InvocationPropagatesOriginalFaultAfterActionHasExecuted(bool worker)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dispatcher = new AvaloniaWorkspaceDispatcher();
            var failure = new InvalidOperationException("owned document failed");
            int calls = 0;
            Task InvokeAsync() => dispatcher.InvokeAsync(async () =>
            {
                calls++;
                await Task.Yield();
                dispatcher.VerifyAccess();
                throw failure;
            });

            await Assert.ThatAsync(() => worker ? Task.Run(InvokeAsync) : InvokeAsync(),
                Throws.Exception.SameAs(failure)).ConfigureAwait(true);

            Assert.That(calls, Is.EqualTo(1));
            int recovered = 0;
            await dispatcher.InvokeAsync(() =>
            {
                recovered++;
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            Assert.That(recovered, Is.EqualTo(1));
        });
    }

    private static readonly string[] s_invokeAsyncExecutesInlineOrMarshalsToSameUiOwnerAndAwaitsCompExpected =
    [
        "entered",
    ];
    private static readonly string[] s_invokeAsyncExecutesInlineOrMarshalsToSameUiOwnerAndAwaitsCompExpected2 =
    [
        "entered",
        "finished",
    ];
}
