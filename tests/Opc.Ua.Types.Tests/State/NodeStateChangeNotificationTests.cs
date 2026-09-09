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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for the asynchronous change-notification API on <see cref="NodeState"/>:
    /// <see cref="NodeState.ClearChangeMasksAsync"/> / <see cref="NodeState.OnStateChangedAsync"/>
    /// and <see cref="NodeState.ReportEventAsync"/> / <see cref="NodeState.OnReportEventAsync"/>,
    /// including that the synchronous entry points still drive the asynchronous sinks.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateChangeNotificationTests
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_messageContext as IDisposable)?.Dispose();
        }

        private SystemContext CreateSystemContext()
        {
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                TypeTable = new TypeTable(m_messageContext.NamespaceUris)
            };
        }

        private static BaseDataVariableState CreateVariable()
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Var", 0),
                BrowseName = new QualifiedName("Var", 0),
                DisplayName = new LocalizedText("Var"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
        }

        [Test]
        public async Task ClearChangeMasksAsyncInvokesAsyncAndSyncSinks()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            // Flush any change masks set while constructing the node so the assertion below
            // observes only the mask raised by this test.
            v.ClearChangeMasks(ctx, includeChildren: false);

            NodeStateChangeMasks syncMask = NodeStateChangeMasks.None;
            NodeStateChangeMasks asyncMask = NodeStateChangeMasks.None;

            v.OnStateChanged = (c, n, mask) => syncMask = mask;
            v.OnStateChangedAsync = (c, n, mask, ct) =>
            {
                asyncMask = mask;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            await v.ClearChangeMasksAsync(ctx, includeChildren: false).ConfigureAwait(false);

            Assert.That(syncMask, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(asyncMask, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        [Test]
        public void ClearChangeMasksSyncDrivesAsyncSink()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            // Flush any change masks set while constructing the node.
            v.ClearChangeMasks(ctx, includeChildren: false);

            NodeStateChangeMasks asyncMask = NodeStateChangeMasks.None;
            v.OnStateChangedAsync = (c, n, mask, ct) =>
            {
                asyncMask = mask;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            v.ClearChangeMasks(ctx, includeChildren: false);

            Assert.That(asyncMask, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        [Test]
        public async Task ClearChangeMasksAsyncAwaitsStateChangedAsyncEvent()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            bool invoked = false;
            v.StateChangedAsync += (c, n, mask, ct) =>
            {
                invoked = true;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            await v.ClearChangeMasksAsync(ctx, includeChildren: false).ConfigureAwait(false);

            Assert.That(invoked, Is.True);
        }

        [Test]
        public async Task ReportEventAsyncInvokesAsyncAndSyncSinks()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();
            var target = new BaseObjectState(null);

            IFilterTarget syncTarget = null;
            IFilterTarget asyncTarget = null;

            v.OnReportEvent = (c, n, e) => syncTarget = e;
            v.OnReportEventAsync = (c, n, e, ct) =>
            {
                asyncTarget = e;
                return default;
            };

            await v.ReportEventAsync(ctx, target).ConfigureAwait(false);

            Assert.That(syncTarget, Is.SameAs(target));
            Assert.That(asyncTarget, Is.SameAs(target));
        }

        [Test]
        public void ReportEventSyncDrivesAsyncSink()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();
            var target = new BaseObjectState(null);

            IFilterTarget asyncTarget = null;
            v.OnReportEventAsync = (c, n, e, ct) =>
            {
                asyncTarget = e;
                return default;
            };

            v.ReportEvent(ctx, target);

            Assert.That(asyncTarget, Is.SameAs(target));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportEventWithoutLocalSinksForwardsOnlyToInverseNotifierAsync(bool hasCallerContext)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var inverse = new BaseObjectState(null);
            var forward = new BaseObjectState(null);
            int inverseCalls = 0;
            int forwardCalls = 0;

            await RunLegacyCallerAsync(() => node.ReportEvent(context, target), hasCallerContext)
                .WaitAsync(s_timeout).ConfigureAwait(false);

            inverse.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(inverse));
                Assert.That(e, Is.SameAs(target));
                inverseCalls++;
            };
            forward.OnReportEvent = (c, n, e) => forwardCalls++;
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, false, forward);
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, null);
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, inverse);

            await RunLegacyCallerAsync(() => node.ReportEvent(context, target), hasCallerContext)
                .WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(inverseCalls, Is.EqualTo(1));
            Assert.That(forwardCalls, Is.Zero);
            Assert.That(node.OnReportEvent, Is.Null);
            Assert.That(node.OnReportEventAsync, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportEventWithSyncSinkOnlyRunsOnCallerThreadAsync(bool hasCallerContext)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            int calls = 0;

            await RunLegacyCallerAsync(() =>
            {
                int callerThread = Environment.CurrentManagedThreadId;
                SynchronizationContext callerContext = SynchronizationContext.Current;
                node.OnReportEvent = (c, n, e) =>
                {
                    Assert.That(c, Is.SameAs(context));
                    Assert.That(n, Is.SameAs(node));
                    Assert.That(e, Is.SameAs(target));
                    Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(callerThread));
                    Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));
                    calls++;
                };

                node.ReportEvent(context, target);

                Assert.That(calls, Is.EqualTo(1));
            }, hasCallerContext).WaitAsync(s_timeout).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportEventCompletedAsyncSinkUsesExpectedThreadAndFinishesBeforeReturnAsync(
            bool hasCallerContext)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var calls = new List<string>();

            await RunLegacyCallerAsync(() =>
            {
                int callerThread = Environment.CurrentManagedThreadId;
                SynchronizationContext callerContext = SynchronizationContext.Current;
                node.OnReportEvent = (c, n, e) =>
                {
                    Assert.That(c, Is.SameAs(context));
                    Assert.That(n, Is.SameAs(node));
                    Assert.That(e, Is.SameAs(target));
                    Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(callerThread));
                    Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));
                    calls.Add("sync");
                };
                node.OnReportEventAsync = (c, n, e, ct) =>
                {
                    Assert.That(c, Is.SameAs(context));
                    Assert.That(n, Is.SameAs(node));
                    Assert.That(e, Is.SameAs(target));
                    Assert.That(ct, Is.EqualTo(CancellationToken.None));
                    Assert.That(SynchronizationContext.Current, Is.Null);
                    Assert.That(Environment.CurrentManagedThreadId == callerThread, Is.EqualTo(!hasCallerContext));
                    calls.Add("async");
                    return default;
                };

                node.ReportEvent(context, target);
                calls.Add("return");

                string[] expectedCalls = ["sync", "async", "return"];
                Assert.That(calls, Is.EqualTo(expectedCalls));
            }, hasCallerContext).WaitAsync(s_timeout).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReportEventReadsAsyncHandlerAfterSyncSinkAsync(bool hasCallerContext, bool removeHandler)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var calls = new List<string>();
            node.OnReportEventAsync = (c, n, e, ct) =>
            {
                calls.Add("original");
                return default;
            };
            ValueTask Replacement(ISystemContext c, NodeState n, IFilterTarget e, CancellationToken ct)
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(node));
                Assert.That(e, Is.SameAs(target));
                Assert.That(ct, Is.EqualTo(CancellationToken.None));
                Assert.That(SynchronizationContext.Current, Is.Null);
                calls.Add("replacement");
                return default;
            }
            NodeStateReportEventAsyncHandler replacement = Replacement;
            node.OnReportEvent = (c, n, e) =>
            {
                calls.Add("sync");
                node.OnReportEventAsync = removeHandler ? null : replacement;
            };

            await RunLegacyCallerAsync(() => node.ReportEvent(context, target), hasCallerContext)
                .WaitAsync(s_timeout).ConfigureAwait(false);

            string[] expectedCalls = removeHandler ? ["sync"] : ["sync", "replacement"];
            Assert.That(calls, Is.EqualTo(expectedCalls));
            Assert.That(node.OnReportEventAsync, Is.SameAs(removeHandler ? null : replacement));
        }

        [TestCase(false, "Success")]
        [TestCase(true, "Success")]
        [TestCase(false, "Fault")]
        [TestCase(true, "Fault")]
        [TestCase(false, "Cancel")]
        [TestCase(true, "Cancel")]
        public async Task ReportEventWaitsForSuspendedSinkBeforeReturningOrForwardingAsync(
            bool hasCallerContext,
            string outcome)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var inverse = new BaseObjectState(null);
            var lateInverse = new BaseObjectState(null);
            var calls = new List<string>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception expectedException = outcome switch
            {
                "Fault" => new InvalidOperationException("Suspended sink failed."),
                "Cancel" => new OperationCanceledException(new CancellationToken(canceled: true)),
                _ => null
            };
            int callerThread = 0;
            node.OnReportEvent = (c, n, e) => calls.Add("sync");
            inverse.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(inverse));
                Assert.That(e, Is.SameAs(target));
                calls.Add("inverse");
            };
            lateInverse.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(lateInverse));
                Assert.That(e, Is.SameAs(target));
                calls.Add("late");
            };
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, inverse);
            node.OnReportEventAsync = async (c, n, e, ct) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(node));
                Assert.That(e, Is.SameAs(target));
                Assert.That(ct, Is.EqualTo(CancellationToken.None));
                Assert.That(SynchronizationContext.Current, Is.Null);
                Assert.That(Environment.CurrentManagedThreadId == callerThread, Is.EqualTo(!hasCallerContext));
                calls.Add("async-start");
                entered.TrySetResult(true);

                // Deliberately capture a context: legacy dispatch must have removed it before invoking the sink.
                await release.Task.ConfigureAwait(true);

                Assert.That(SynchronizationContext.Current, Is.Null);
                calls.Add("async-end");
                node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, lateInverse);
                if (expectedException != null)
                {
                    throw expectedException;
                }
            };

            Task caller = RunLegacyCallerAsync(() =>
            {
                callerThread = Environment.CurrentManagedThreadId;
                if (expectedException == null)
                {
                    node.ReportEvent(context, target);
                }
                else
                {
                    Assert.That(() => node.ReportEvent(context, target), Throws.Exception.SameAs(expectedException));
                }
                calls.Add("return");
            }, hasCallerContext);

            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);

                Assert.That(caller.IsCompleted, Is.False);
                string[] beforeCompletion = ["sync", "async-start"];
                Assert.That(calls, Is.EqualTo(beforeCompletion));
            }
            finally
            {
                release.TrySetResult(true);
                await caller.WaitAsync(s_timeout).ConfigureAwait(false);
            }

            string[] expectedCalls = expectedException == null
                ? ["sync", "async-start", "async-end", "inverse", "late", "return"]
                : ["sync", "async-start", "async-end", "return"];
            Assert.That(calls, Is.EqualTo(expectedCalls));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportEventSyncExceptionPreservesIdentityAndStopsDispatchAsync(bool hasCallerContext)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var inverse = new BaseObjectState(null);
            var expectedException = new InvalidOperationException("Sync sink failed.");
            var calls = new List<string>();
            node.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(node));
                Assert.That(e, Is.SameAs(target));
                calls.Add("sync");
                throw expectedException;
            };
            node.OnReportEventAsync = (c, n, e, ct) =>
            {
                calls.Add("async");
                return default;
            };
            inverse.OnReportEvent = (c, n, e) => calls.Add("inverse");
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, inverse);

            await RunLegacyCallerAsync(
                () => Assert.That(() => node.ReportEvent(context, target), Throws.Exception.SameAs(expectedException)),
                hasCallerContext).WaitAsync(s_timeout).ConfigureAwait(false);

            string[] expectedCalls = ["sync"];
            Assert.That(calls, Is.EqualTo(expectedCalls));
        }

        [TestCase(false, "Throw")]
        [TestCase(true, "Throw")]
        [TestCase(false, "Fault")]
        [TestCase(true, "Fault")]
        [TestCase(false, "Cancel")]
        [TestCase(true, "Cancel")]
        public async Task ReportEventAsyncSinkImmediateFailureStopsForwardingAsync(
            bool hasCallerContext,
            string failure)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var inverse = new BaseObjectState(null);
            var expectedException = new InvalidOperationException("Async sink failed.");
            var canceledToken = new CancellationToken(canceled: true);
            var calls = new List<string>();
            node.OnReportEvent = (c, n, e) => calls.Add("sync");
            node.OnReportEventAsync = (c, n, e, ct) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(node));
                Assert.That(e, Is.SameAs(target));
                Assert.That(ct, Is.EqualTo(CancellationToken.None));
                Assert.That(SynchronizationContext.Current, Is.Null);
                calls.Add("async");
                return failure switch
                {
                    "Throw" => throw expectedException,
                    "Fault" => new ValueTask(Task.FromException(expectedException)),
                    _ => new ValueTask(Task.FromCanceled(canceledToken))
                };
            };
            inverse.OnReportEvent = (c, n, e) => calls.Add("inverse");
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, inverse);

            await RunLegacyCallerAsync(() =>
            {
                if (failure == "Cancel")
                {
                    Assert.That(
                        () => node.ReportEvent(context, target),
                        Throws.InstanceOf<OperationCanceledException>()
                            .With.Property(nameof(OperationCanceledException.CancellationToken))
                            .EqualTo(canceledToken));
                }
                else
                {
                    Assert.That(() => node.ReportEvent(context, target), Throws.Exception.SameAs(expectedException));
                }
            }, hasCallerContext).WaitAsync(s_timeout).ConfigureAwait(false);

            string[] expectedCalls = ["sync", "async"];
            Assert.That(calls, Is.EqualTo(expectedCalls));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportEventNotifierMutationAffectsNextSnapshotOnlyAsync(bool hasCallerContext)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState node = CreateVariable();
            var target = new BaseObjectState(null);
            var first = new BaseObjectState(null);
            var second = new BaseObjectState(null);
            var added = new BaseObjectState(null);
            var calls = new List<string>();
            node.OnReportEvent = (c, n, e) => calls.Add("sync");
            node.OnReportEventAsync = (c, n, e, ct) =>
            {
                calls.Add("async");
                return default;
            };
            first.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(first));
                Assert.That(e, Is.SameAs(target));
                calls.Add("first");
                node.RemoveNotifier(context, second, bidirectional: false);
                node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, added);
            };
            second.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(second));
                Assert.That(e, Is.SameAs(target));
                calls.Add("second");
            };
            added.OnReportEvent = (c, n, e) =>
            {
                Assert.That(c, Is.SameAs(context));
                Assert.That(n, Is.SameAs(added));
                Assert.That(e, Is.SameAs(target));
                calls.Add("added");
            };
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, first);
            node.AddNotifier(context, ReferenceTypeIds.HasEventSource, true, second);

            await RunLegacyCallerAsync(() =>
            {
                node.ReportEvent(context, target);

                string[] firstReport = ["sync", "async", "first", "second"];
                Assert.That(calls, Is.EqualTo(firstReport));
                calls.Clear();

                node.ReportEvent(context, target);

                string[] nextReport = ["sync", "async", "first", "added"];
                Assert.That(calls, Is.EqualTo(nextReport));
            }, hasCallerContext).WaitAsync(s_timeout).ConfigureAwait(false);
        }

        private static Task RunLegacyCallerAsync(Action report, bool hasCallerContext)
        {
            return Task.Factory.StartNew(() =>
            {
                SynchronizationContext originalContext = SynchronizationContext.Current;
                SynchronizationContext callerContext = hasCallerContext ? new SynchronizationContext() : null;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(callerContext);
                    report();
                    Assert.That(SynchronizationContext.Current, Is.SameAs(callerContext));
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(originalContext);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;
    }
}
