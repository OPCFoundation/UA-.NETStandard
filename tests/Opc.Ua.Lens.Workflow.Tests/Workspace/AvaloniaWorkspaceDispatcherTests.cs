/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
[Category("LensDesktopWorkflow")]
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
