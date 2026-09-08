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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class DocumentWorkspaceTests
{
    [SetUp]
    public void SetUp()
    {
        m_workspace = new DocumentWorkspace<TestDocument>(NullLogger.Instance);
        m_expectedDisposalFailure = false;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (m_expectedDisposalFailure)
        {
            await Assert.ThatAsync(
                () => m_workspace.DisposeAsync().AsTask(),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }
        else
        {
            await m_workspace.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task OpensOfflineAndKeepsSelectionAndActivationAuthoritative()
    {
        TestDocument first = await OpenAsync("Monitor").ConfigureAwait(false);
        TestDocument second = await OpenAsync("Certificates").ConfigureAwait(false);
        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { first, second }));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(second));
        Assert.That(first.ActivationCount, Is.EqualTo(1));
        Assert.That(first.DeactivationCount, Is.EqualTo(1));
        Assert.That(second.ActivationCount, Is.EqualTo(1));
        Assert.That(first.ConnectionDeliveries, Is.EqualTo(1));
        Assert.That(second.ConnectionDeliveries, Is.EqualTo(1));

        m_workspace.Activate(first);
        m_workspace.Activate(first);
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(first));
        Assert.That(first.ActivationCount, Is.EqualTo(2));
        Assert.That(second.DeactivationCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.Zero);
    }

    [Test]
    public async Task CannotSelectOrAddADocumentOwnedByAnotherWorkspace()
    {
        var other = new DocumentWorkspace<TestDocument>(NullLogger.Instance);
        await using (other.ConfigureAwait(false))
        {
            TestDocument current = await OpenAsync("Current").ConfigureAwait(false);
            TestDocument foreign = await other.OpenAsync(() => new TestDocument("Foreign")).ConfigureAwait(false);

            Assert.That(() => m_workspace.Activate(foreign), Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => ((ICollection<TestDocument>)m_workspace.Documents).Add(foreign),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(m_workspace.ActiveDocument, Is.SameAs(current));
            Assert.That(m_workspace.Documents, Is.EqualTo(new[] { current }));
            Assert.That(foreign.DisposeCount, Is.Zero);
        }
    }

    [Test]
    public async Task ClosingAnInactiveDocumentDoesNotChangeSelection()
    {
        TestDocument first = await OpenAsync("First").ConfigureAwait(false);
        TestDocument active = await OpenAsync("Active").ConfigureAwait(false);

        await m_workspace.CloseAsync(first).ConfigureAwait(false);
        await m_workspace.CloseAsync(first).ConfigureAwait(false);

        Assert.That(m_workspace.ActiveDocument, Is.SameAs(active));
        Assert.That(active.ActivationCount, Is.EqualTo(1));
        Assert.That(active.DeactivationCount, Is.Zero);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ClosingActiveDocumentSelectsANeighborBeforePublishingRemoval()
    {
        TestDocument first = await OpenAsync("First").ConfigureAwait(false);
        TestDocument middle = await OpenAsync("Middle").ConfigureAwait(false);
        TestDocument last = await OpenAsync("Last").ConfigureAwait(false);
        m_workspace.Activate(middle);
        bool selectionWasValid = true;
        m_workspace.Documents.CollectionChanged += (_, _) =>
        {
            selectionWasValid &= m_workspace.ActiveDocument is null
                || m_workspace.Documents.Contains(m_workspace.ActiveDocument);
        };

        await m_workspace.CloseAsync(middle).ConfigureAwait(false);
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(last));
        await m_workspace.CloseAsync(last).ConfigureAwait(false);
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(first));
        await m_workspace.CloseAsync(first).ConfigureAwait(false);

        Assert.That(selectionWasValid, Is.True);
        Assert.That(m_workspace.ActiveDocument, Is.Null);
        Assert.That(m_workspace.Documents, Is.Empty);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(middle.DisposeCount, Is.EqualTo(1));
        Assert.That(last.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReentrantCloseFromDeactivationJoinsTheExistingCleanup()
    {
        TestDocument first = await OpenAsync("First").ConfigureAwait(false);
        TestDocument closing = await OpenAsync("Closing").ConfigureAwait(false);
        Task? nested = null;
        closing.DeactivationAction = () => nested = m_workspace.CloseAsync(closing);

        await m_workspace.CloseAsync(closing).ConfigureAwait(false);
        Assert.That(nested, Is.Not.Null);
        await nested!.ConfigureAwait(false);
        Assert.That(closing.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { first }));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(first));
    }

    [Test]
    public async Task ApplicationDisposalIncludesHiddenDocumentsAndIsExactlyOnce()
    {
        TestDocument first = await OpenAsync("Hidden monitor").ConfigureAwait(false);
        TestDocument second = await OpenAsync("Hidden events").ConfigureAwait(false);
        TestDocument third = await OpenAsync("Active tool").ConfigureAwait(false);

        await Task.WhenAll(
            m_workspace.CloseAsync(second),
            m_workspace.CloseAsync(second),
            m_workspace.DisposeAsync().AsTask(),
            m_workspace.DisposeAsync().AsTask()).ConfigureAwait(false);

        Assert.That(m_workspace.IsClosing, Is.True);
        Assert.That(m_workspace.Documents, Is.Empty);
        Assert.That(m_workspace.ActiveDocument, Is.Null);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(third.DisposeCount, Is.EqualTo(1));
        await Assert.ThatAsync(
            () => OpenAsync("Too late"), Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task CloseAndShutdownAwaitTheSameAsynchronousDocumentDisposal()
    {
        TestDocument document = await OpenAsync("Disposing asynchronously").ConfigureAwait(false);
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        document.AsyncDisposalAction = () =>
        {
            entered.SetResult();
            return new ValueTask(release.Task);
        };
        Task closing = m_workspace.CloseAsync(document);
        Task disposing = m_workspace.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(closing.IsCompleted, Is.False);
            Assert.That(disposing.IsCompleted, Is.False);
            Assert.That(document.DisposeCount, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(closing, disposing).ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task APreCancelledOpenDoesNotInvokeTheFactory()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        int constructions = 0;

        await Assert.ThatAsync(
            () => m_workspace.OpenAsync(
                () =>
                {
                    constructions++;
                    return new TestDocument("Never constructed");
                },
                cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(constructions, Is.Zero);
        Assert.That(m_workspace.Documents, Is.Empty);
    }

    [Test]
    public async Task CloseCancelsAndWaitsForLateInitializationBeforeDisposing()
    {
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        TaskCompletionSource cancellationObserved = NewSignal();
        CancellationToken workToken = default;
        Task<TestDocument> opening = m_workspace.OpenAsync(
            () => new TestDocument("Slow open"),
            async (_, token) =>
            {
                workToken = token;
                using CancellationTokenRegistration registration =
                    token.Register(() => cancellationObserved.TrySetResult());
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            });
        TestDocument document;
        Task closing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            document = m_workspace.Documents[0];
            closing = m_workspace.CloseAsync(document);
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(workToken.IsCancellationRequested, Is.True);
            Assert.That(closing.IsCompleted, Is.False);
            Assert.That(m_workspace.Documents, Is.Empty);
            Assert.That(document.DisposeCount, Is.Zero);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => opening, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await closing.ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
        Assert.That(document.ConnectionDeliveries, Is.Zero);
    }

    [Test]
    public async Task AThrowingCancellationCallbackCannotDisposeWhileDocumentWorkIsStillRunning()
    {
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        TaskCompletionSource cancellationObserved = NewSignal();
        Task<TestDocument> opening = m_workspace.OpenAsync(
            () => new TestDocument("Faulty cancellation"),
            async (_, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() =>
                {
                    cancellationObserved.TrySetResult();
                    throw new InvalidOperationException("Cancellation callback failed");
                });
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            });
        TestDocument document;
        Task closing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            document = m_workspace.Documents[0];
            closing = m_workspace.CloseAsync(document);
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(closing.IsCompleted, Is.False);
            Assert.That(document.DisposeCount, Is.Zero);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => closing, Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => opening, Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ShutdownAlsoDrainsLateInitialization()
    {
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        Task<TestDocument> opening = m_workspace.OpenAsync(
            () => new TestDocument("Opening on shutdown"),
            async (_, _) =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            });
        TestDocument document;
        Task disposing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            document = m_workspace.Documents[0];
            disposing = m_workspace.DisposeAsync().AsTask();
            Assert.That(disposing.IsCompleted, Is.False);
            Assert.That(document.DisposeCount, Is.Zero);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => opening, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await disposing.ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedOpenDisposesOnlyTheNewDocumentAndRestoresSelection()
    {
        TestDocument original = await OpenAsync("Original").ConfigureAwait(false);
        TestDocument? failed = null;

        await Assert.ThatAsync(
            () => m_workspace.OpenAsync(
                () => new TestDocument("Failed"),
                (document, _) =>
                {
                    failed = document;
                    throw new InvalidOperationException("Invalid document configuration");
                }),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { original }));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(original));
        Assert.That(original.DisposeCount, Is.Zero);
        Assert.That(failed?.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.LastError, Does.Contain("Invalid document configuration"));
    }

    [Test]
    public async Task FactoryCannotTransferAnAlreadyOwnedDocumentTwice()
    {
        TestDocument document = await OpenAsync("Original").ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_workspace.OpenAsync(() => document),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { document }));
        Assert.That(document.DisposeCount, Is.Zero);
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(document));
    }

    [Test]
    public async Task AClosedDocumentCannotBeReopenedAndDisposedTwice()
    {
        TestDocument document = await OpenAsync("Closed").ConfigureAwait(false);
        await m_workspace.CloseAsync(document).ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_workspace.OpenAsync(() => document),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Is.Empty);
    }

    [Test]
    public async Task RepeatedCloseObservesTheSameDisposalFailureWithoutRepeatingCleanup()
    {
        TestDocument document = await OpenAsync("Closing failure").ConfigureAwait(false);
        document.DisposalAction = () => throw new InvalidOperationException("Close failed");

        await Assert.ThatAsync(
            () => m_workspace.CloseAsync(document),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => m_workspace.CloseAsync(document),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(document.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Is.Empty);
        Assert.That(m_workspace.LastError, Does.Contain("Close failed"));
    }

    [Test]
    public async Task NewConnectionCancelsAndSerializesOlderDelivery()
    {
        TestDocument document = await OpenAsync("Monitor").ConfigureAwait(false);
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        TaskCompletionSource cancellationObserved = NewSignal();
        int generation = 0;
        int activeCalls = 0;
        var concurrentCalls = new ConcurrentQueue<int>();
        CancellationToken firstToken = default;
        document.ConnectionAction = async token =>
        {
            using CancellationTokenRegistration registration =
                token.Register(() => cancellationObserved.TrySetResult());
            int calls = Interlocked.Increment(ref activeCalls);
            concurrentCalls.Enqueue(calls);
            int current = Interlocked.Increment(ref generation);
            try
            {
                if (current == 1)
                {
                    firstToken = token;
                    entered.SetResult();
                    await release.Task.ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                document.Configuration = $"Generation {current}";
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        };

        Task first = m_workspace.SynchronizeConnectionAsync();
        Task second;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            second = m_workspace.SynchronizeConnectionAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(firstToken.IsCancellationRequested, Is.True);
            Assert.That(generation, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => first, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await second.ConfigureAwait(false);
        Assert.That(concurrentCalls, Is.All.EqualTo(1));
        Assert.That(document.Configuration, Is.EqualTo("Generation 2"));
        Assert.That(document.DisposeCount, Is.Zero);
    }

    [Test]
    public async Task ConnectionChangeDuringOpeningPreservesOfflineConfigurationAndRebindsSerially()
    {
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        TaskCompletionSource cancellationObserved = NewSignal();
        CancellationToken oldConnection = default;
        int deliveries = 0;
        Task<TestDocument> opening = m_workspace.OpenAsync(
            () => new TestDocument("Opening monitor")
            {
                ConnectionAction = async token =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => cancellationObserved.TrySetResult());
                    if (++deliveries == 1)
                    {
                        oldConnection = token;
                        entered.SetResult();
                        await release.Task.ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();
                }
            },
            (document, _) =>
            {
                document.Configuration = "Saved node list";
                return Task.CompletedTask;
            });
        Task updating;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            updating = m_workspace.SynchronizeConnectionAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(oldConnection.IsCancellationRequested, Is.True);
        }
        finally
        {
            release.TrySetResult();
        }

        TestDocument opened = await opening.ConfigureAwait(false);
        await updating.ConfigureAwait(false);
        Assert.That(opened.Configuration, Is.EqualTo("Saved node list"));
        Assert.That(deliveries, Is.EqualTo(2));
        Assert.That(opened.DisposeCount, Is.Zero);
        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { opened }));
    }

    [Test]
    public async Task ClosingOneDocumentDuringDeliveryDoesNotCancelItsSiblings()
    {
        TestDocument closingDocument = await OpenAsync("Closing").ConfigureAwait(false);
        TestDocument sibling = await OpenAsync("Remaining").ConfigureAwait(false);
        TaskCompletionSource entered = NewSignal();
        closingDocument.ConnectionAction = async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
        };

        Task delivery = m_workspace.SynchronizeConnectionAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Task closing = m_workspace.CloseAsync(closingDocument);
        await Task.WhenAll(delivery, closing).ConfigureAwait(false);

        Assert.That(sibling.ConnectionDeliveries, Is.EqualTo(2));
        Assert.That(closingDocument.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(sibling));
    }

    [Test]
    public async Task ConnectionReleaseKeepsDocumentsAndConfigurationAndDoesNotNotifyThemAsConnected()
    {
        TestDocument document = await OpenAsync("Configured monitor").ConfigureAwait(false);
        document.Configuration = "Node list and publishing settings";
        int releases = 0;

        await m_workspace.ReleaseConnectionAsync((_, _) =>
        {
            releases++;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        Assert.That(releases, Is.EqualTo(1));
        Assert.That(document.ConnectionDeliveries, Is.EqualTo(1));
        Assert.That(document.DisposeCount, Is.Zero);
        Assert.That(document.Configuration, Is.EqualTo("Node list and publishing settings"));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(document));
        await m_workspace.SynchronizeConnectionAsync().ConfigureAwait(false);
        Assert.That(document.ConnectionDeliveries, Is.EqualTo(2));
    }

    [Test]
    public async Task ConnectionReleaseCancelsAndDrainsLateBindingBeforeReleasingResources()
    {
        TestDocument document = await OpenAsync("Binding").ConfigureAwait(false);
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource cancellationObserved = NewSignal();
        TaskCompletionSource finishBinding = NewSignal();
        bool bindingFinished = false;
        int releases = 0;
        document.ConnectionAction = async token =>
        {
            using CancellationTokenRegistration registration =
                token.Register(() => cancellationObserved.TrySetResult());
            entered.SetResult();
            await finishBinding.Task.ConfigureAwait(false);
            bindingFinished = true;
            token.ThrowIfCancellationRequested();
        };
        Task binding = m_workspace.SynchronizeConnectionAsync();
        Task releasing;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            releasing = m_workspace.ReleaseConnectionAsync((_, _) =>
            {
                Assert.That(bindingFinished, Is.True);
                releases++;
                return Task.CompletedTask;
            });
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(releases, Is.Zero);
            Assert.That(releasing.IsCompleted, Is.False);
        }
        finally
        {
            finishBinding.TrySetResult();
        }
        await Assert.ThatAsync(
            () => binding, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await releasing.ConfigureAwait(false);
        Assert.That(releases, Is.EqualTo(1));
        Assert.That(document.DisposeCount, Is.Zero);
    }

    [Test]
    public async Task ConnectionReleaseWaitsForAClosingDocumentsAsynchronousDisposal()
    {
        TestDocument document = await OpenAsync("Closing monitor").ConfigureAwait(false);
        TaskCompletionSource disposing = NewSignal();
        TaskCompletionSource release = NewSignal();
        document.AsyncDisposalAction = () =>
        {
            disposing.SetResult();
            return new ValueTask(release.Task);
        };
        Task closing = m_workspace.CloseAsync(document);
        Task barrier;
        int releaseCallbacks = 0;
        try
        {
            await disposing.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            barrier = m_workspace.ReleaseConnectionAsync((_, _) =>
            {
                releaseCallbacks++;
                return Task.CompletedTask;
            });
            Assert.That(barrier.IsCompleted, Is.False);
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(closing, barrier).ConfigureAwait(false);
        Assert.That(document.DisposeCount, Is.EqualTo(1));
        Assert.That(releaseCallbacks, Is.Zero);
    }

    [Test]
    public async Task ANewConnectionNotificationCannotCancelResourceRelease()
    {
        TestDocument document = await OpenAsync("Rebinding").ConfigureAwait(false);
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        CancellationToken releaseToken = default;
        Task barrier = m_workspace.ReleaseConnectionAsync(async (_, token) =>
        {
            releaseToken = token;
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        });
        Task notifying;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            notifying = m_workspace.SynchronizeConnectionAsync();
            Assert.That(releaseToken.IsCancellationRequested, Is.False);
            Assert.That(document.ConnectionDeliveries, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(barrier, notifying).ConfigureAwait(false);
        Assert.That(document.ConnectionDeliveries, Is.EqualTo(2));
    }

    [Test]
    public async Task AReleaseFailureStillAttemptsEveryDocumentAndPreservesMembership()
    {
        TestDocument failed = await OpenAsync("Failure").ConfigureAwait(false);
        TestDocument remaining = await OpenAsync("Remaining").ConfigureAwait(false);
        int released = 0;

        await Assert.ThatAsync(
            () => m_workspace.ReleaseConnectionAsync((document, _) =>
            {
                if (ReferenceEquals(document, failed))
                {
                    throw new InvalidOperationException("Release failed");
                }
                released++;
                return Task.CompletedTask;
            }),
            Throws.TypeOf<AggregateException>()).ConfigureAwait(false);

        Assert.That(released, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { failed, remaining }));
        Assert.That(failed.DisposeCount, Is.Zero);
        Assert.That(remaining.DisposeCount, Is.Zero);
        Assert.That(m_workspace.LastError, Does.Contain("Release failed"));
    }

    [Test]
    public async Task FailedConnectionDeliveryIsReportedAndDoesNotSkipSiblings()
    {
        TestDocument failed = await OpenAsync("Failed").ConfigureAwait(false);
        TestDocument sibling = await OpenAsync("Remaining").ConfigureAwait(false);
        failed.ConnectionAction = _ => throw new InvalidOperationException("Attach failed");

        await Assert.ThatAsync(
            () => m_workspace.SynchronizeConnectionAsync(),
            Throws.TypeOf<AggregateException>()).ConfigureAwait(false);

        Assert.That(sibling.ConnectionDeliveries, Is.EqualTo(2));
        Assert.That(m_workspace.LastError, Does.Contain("Attach failed"));
        Assert.That(m_workspace.Documents, Has.Count.EqualTo(2));
        Assert.That(failed.DisposeCount, Is.Zero);
    }

    [Test]
    public async Task RestoreCommitsOrderSelectionAndConfigurationWithoutReusingOldDocuments()
    {
        TestDocument original = await OpenAsync("Original").ConfigureAwait(false);

        await m_workspace.RestoreAsync(
            [
                new(() => new TestDocument("Restored monitor"),
                    (document, _) =>
                    {
                        document.Configuration = "Publishing 250ms";
                        return Task.CompletedTask;
                    }),
                new(() => new TestDocument("Restored certificates"))
            ],
            selectedIndex: 1).ConfigureAwait(false);

        Assert.That(original.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Has.Count.EqualTo(2));
        Assert.That(m_workspace.Documents[0].Title, Is.EqualTo("Restored monitor"));
        Assert.That(m_workspace.Documents[0].Configuration, Is.EqualTo("Publishing 250ms"));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(m_workspace.Documents[1]));
        Assert.That(m_workspace.Documents[0].ConnectionDeliveries, Is.EqualTo(1));
        Assert.That(m_workspace.Documents[1].ConnectionDeliveries, Is.EqualTo(1));
    }

    [Test]
    public async Task CommittedRestoreDeliversConnectionStateEvenWhenPreviousCleanupFails()
    {
        TestDocument previous = await OpenAsync("Previous").ConfigureAwait(false);
        previous.DisposalAction = () => throw new InvalidOperationException("Previous cleanup failed");

        await Assert.ThatAsync(
            () => m_workspace.RestoreAsync([new(() => new TestDocument("Replacement"))]),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(previous.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Has.Count.EqualTo(1));
        Assert.That(m_workspace.Documents[0].Title, Is.EqualTo("Replacement"));
        Assert.That(m_workspace.Documents[0].ConnectionDeliveries, Is.EqualTo(1));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(m_workspace.Documents[0]));
    }

    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(42)]
    public async Task InvalidEmptyRestoreSelectionDoesNotRemoveExistingDocuments(int selectedIndex)
    {
        TestDocument previous = await OpenAsync("Previous").ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_workspace.RestoreAsync([], selectedIndex),
            Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);

        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { previous }));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(previous));
        Assert.That(previous.DisposeCount, Is.Zero);
    }

    [Test]
    public async Task CancelledRestorePreservesOriginalDocumentsAndDisposesPreparedOnes()
    {
        TestDocument original = await OpenAsync("Original").ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource release = NewSignal();
        TestDocument? prepared = null;
        Task restoring = m_workspace.RestoreAsync(
            [
                new(() => new TestDocument("Prepared"),
                    async (document, _) =>
                    {
                        prepared = document;
                        entered.SetResult();
                        await release.Task.ConfigureAwait(false);
                    })
            ],
            cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => restoring, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(prepared?.DisposeCount, Is.EqualTo(1));
        Assert.That(original.DisposeCount, Is.Zero);
        Assert.That(m_workspace.Documents, Is.EqualTo(new[] { original }));
        Assert.That(m_workspace.ActiveDocument, Is.SameAs(original));
    }

    [Test]
    public async Task ADisposalFailureDoesNotStrandOtherDocumentsOrRepeatDisposal()
    {
        TestDocument failed = await OpenAsync("Failed").ConfigureAwait(false);
        TestDocument sibling = await OpenAsync("Remaining").ConfigureAwait(false);
        failed.DisposalAction = () => throw new InvalidOperationException("Dispose failed");
        m_expectedDisposalFailure = true;

        await Assert.ThatAsync(
            () => m_workspace.DisposeAsync().AsTask(),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(failed.DisposeCount, Is.EqualTo(1));
        Assert.That(sibling.DisposeCount, Is.EqualTo(1));
        Assert.That(m_workspace.Documents, Is.Empty);
        Assert.That(m_workspace.LastError, Does.Contain("Dispose failed"));
    }

    private Task<TestDocument> OpenAsync(string title)
        => m_workspace.OpenAsync(() => new TestDocument(title));

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestDocument : IWorkspaceDocument
    {
        public TestDocument(string title)
        {
            Title = title;
        }

        public string Title { get; set; }
        public string Configuration { get; set; } = string.Empty;
        public int ActivationCount { get; private set; }
        public int DeactivationCount { get; private set; }
        public int ConnectionDeliveries { get; private set; }
        public int DisposeCount { get; private set; }
        public Func<CancellationToken, Task>? ConnectionAction { get; set; }
        public Action? DisposalAction { get; set; }
        public Action? DeactivationAction { get; set; }
        public Func<ValueTask>? AsyncDisposalAction { get; set; }

        public void OnActivated() => ActivationCount++;

        public void OnDeactivated()
        {
            DeactivationCount++;
            DeactivationAction?.Invoke();
        }

        public Task OnConnectionStateChangedAsync(CancellationToken cancellationToken)
        {
            ConnectionDeliveries++;
            return ConnectionAction?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposalAction?.Invoke();
            return AsyncDisposalAction?.Invoke() ?? ValueTask.CompletedTask;
        }
    }

    private DocumentWorkspace<TestDocument> m_workspace = null!;
    private bool m_expectedDisposalFailure;
}
