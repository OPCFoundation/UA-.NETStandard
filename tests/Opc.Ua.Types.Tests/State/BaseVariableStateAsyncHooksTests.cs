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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for the asynchronous Value-attribute hooks on
    /// <see cref="BaseVariableState"/> — the four <c>On*ValueAsync</c>
    /// slots and the <see cref="BaseVariableState.ReadAttributeAsync"/>
    /// / <see cref="BaseVariableState.WriteAttributeAsync"/> entrypoint
    /// that routes through them.
    /// </summary>
    /// <remarks>
    /// Coverage focuses on the contracts that distinguish the async
    /// path from the synchronous fallback:
    /// <list type="bullet">
    ///   <item>The handler runs without holding <c>lock(this)</c>.</item>
    ///   <item><see cref="CancellationToken"/> propagates end-to-end.</item>
    ///   <item>Exceptions from the handler propagate to the caller.</item>
    ///   <item>The cached value / status / timestamp are updated on
    ///         successful writes (mirroring the sync flow).</item>
    ///   <item>When no async slot is set, bound Value writes reject without
    ///         synchronous fallback; unbound writes retain the ordinary flow.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableStateAsyncHooksTests
    {
        private static readonly int[] s_initialArray = [1, 2, 3, 4];
        private static readonly int[] s_slice = [20, 30];
        private static readonly int[] s_mergedArray = [1, 20, 30, 4];
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;

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

        /// <summary>
        /// Creates an executor that rejects synchronous writes during an asynchronous operation.
        /// </summary>
        private static NodeWriteOperationExecutor CreateSerializedWriteExecutor()
        {
            var admission = new SemaphoreSlim(1, 1);
            return new NodeWriteOperationExecutor(
                operation => admission.CurrentCount == 0
                    ? ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "A Value write is awaiting an asynchronous core operation.")
                    : operation(),
                async (operation, cancellationToken) =>
                {
                    await admission.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await operation(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        admission.Release();
                    }
                });
        }

        /// <summary>
        /// Creates an executor that invokes every operation without adding admission behavior.
        /// </summary>
        private static NodeWriteOperationExecutor CreateInlineWriteExecutor()
        {
            return new NodeWriteOperationExecutor(
                operation => operation(),
                (operation, cancellationToken) => operation(cancellationToken));
        }

        /// <summary>
        /// Creates a writable scalar whose asynchronous core can be controlled by a test.
        /// </summary>
        private static CoreOverrideVariableState CreateCoreOverrideVariable()
        {
            return new CoreOverrideVariableState
            {
                NodeId = new NodeId("CoreOverride", 0),
                BrowseName = new QualifiedName("CoreOverride", 0),
                DisplayName = new LocalizedText("CoreOverride"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = new Variant(1.0)
            };
        }

        private SystemContext CreateSystemContext()
        {
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                TypeTable = new TypeTable(m_messageContext.NamespaceUris)
            };
        }

        /// <summary>
        /// A variable state that overrides the protected asynchronous Value-write core.
        /// </summary>
        private sealed class CoreOverrideVariableState : BaseDataVariableState
        {
            /// <summary>
            /// Creates a test variable without a parent.
            /// </summary>
            internal CoreOverrideVariableState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets or sets the signal raised when the core starts.
            /// </summary>
            internal TaskCompletionSource<bool>? CoreEntered { get; set; }

            /// <summary>
            /// Gets or sets an optional pause before the native base core.
            /// </summary>
            internal TaskCompletionSource<bool>? CoreRelease { get; set; }

            /// <summary>
            /// Gets or sets whether this core skips the native base implementation.
            /// </summary>
            internal bool SkipBaseCore { get; set; }

            /// <summary>
            /// Gets the attribute id last passed to the core.
            /// </summary>
            internal uint LastAttributeId { get; private set; }

            /// <summary>
            /// Gets or sets whether the first core call invokes a nested public write.
            /// </summary>
            internal bool ReenterPublicWrite { get; set; }

            /// <summary>
            /// Gets or sets the cancellation token used by a nested public write.
            /// </summary>
            internal CancellationToken NestedWriteToken { get; set; }

            /// <summary>
            /// Gets or sets the signal raised before the nested write awaits admission.
            /// </summary>
            internal TaskCompletionSource<bool>? NestedWriteEntered { get; set; }

            /// <summary>
            /// Gets whether the nested public write observed cancellation.
            /// </summary>
            internal bool NestedWriteCancelled { get; private set; }

            /// <summary>
            /// Runs the test-controlled core and optionally delegates to the native implementation.
            /// </summary>
            protected override async ValueTask<ServiceResult> WriteAttributeCoreAsync(
                ISystemContext context,
                uint attributeId,
                NumericRange indexRange,
                DataValue value,
                CancellationToken cancellationToken = default)
            {
                LastAttributeId = attributeId;
                CoreEntered?.TrySetResult(true);
                if (ReenterPublicWrite && Interlocked.Increment(ref m_coreCalls) == 1)
                {
                    ValueTask<ServiceResult> nested = WriteAttributeAsync(
                        context,
                        attributeId,
                        indexRange,
                        new DataValue(new Variant(3.0)),
                        NestedWriteToken);
                    NestedWriteEntered?.TrySetResult(true);
                    try
                    {
                        _ = await nested.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        NestedWriteCancelled = true;
                    }
                }

                if (CoreRelease is { } release)
                {
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                if (SkipBaseCore)
                {
                    return ServiceResult.Good;
                }
                return await base.WriteAttributeCoreAsync(
                    context,
                    attributeId,
                    indexRange,
                    value,
                    cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// The number of entered asynchronous core calls.
            /// </summary>
            private int m_coreCalls;
        }

        private static BaseDataVariableState CreateReadableVariable()
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
        public async Task ReadAttributeAsyncRoutesValueReadsToFullAsyncSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            DateTimeUtc handlerTimestamp = DateTimeUtc.Now;

            v.OnReadValueAsync = (c, n, range, encoding, ct) =>
            {
                Assert.That(c, Is.SameAs(ctx));
                Assert.That(n, Is.SameAs(v));
                return new ValueTask<AttributeReadResult>(
                    new AttributeReadResult(
                        ServiceResult.Good,
                        new Variant(42.5),
                        StatusCodes.Good,
                        handlerTimestamp));
            };

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetDouble(), Is.EqualTo(42.5));
            Assert.That(dv.SourceTimestamp, Is.EqualTo((DateTime)handlerTimestamp));
        }

        /// <summary>
        /// Verifies within a bounded wait that the node-instance lock is available
        /// while the async handler is suspended.
        /// </summary>
        [Test]
        public async Task ReadAttributeAsyncFullSlotReleasesNodeStateLockDuringAwait()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            var inHandlerGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            v.OnReadValueAsync = async (c, n, range, encoding, ct) =>
            {
                inHandlerGate.SetResult(true);
                await releaseGate.Task.WaitAsync(ct).ConfigureAwait(false);
                return new AttributeReadResult(
                    ServiceResult.Good, new Variant(7.0), StatusCodes.Good, DateTimeUtc.Now);
            };

            var dv = new DataValue();
            Task<(ServiceResult, DataValue)> readTask = v.ReadAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).AsTask();
            ServiceResult result;
            try
            {
                Assert.That(await inHandlerGate.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True,
                    "handler did not start");
                Assert.That(readTask.IsCompleted, Is.False,
                    "The read must remain suspended until the test releases the handler.");

                bool lockAcquired = false;
                var lockTask = Task.Run(() =>
                {
#pragma warning disable CA2002
                    lock (v)
#pragma warning restore CA2002
                    {
                        lockAcquired = true;
                    }
                });

                await lockTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(lockTask.Status, Is.EqualTo(TaskStatus.RanToCompletion),
                    "lock(v) was not released during the async handler");
                Assert.That(lockAcquired, Is.True);
                Assert.That(readTask.IsCompleted, Is.False,
                    "Lock acquisition must finish while the handler is still suspended.");
            }
            finally
            {
                releaseGate.TrySetResult(true);
                (result, _) = await readTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task ReadAttributeAsyncRoutesValueReadsToSimpleAsyncSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            // Simple async path reuses the variable's cached StatusCode;
            // BaseVariableState defaults that to BadWaitingForInitialData,
            // so promote it to Good to validate the success path.
            v.StatusCode = StatusCodes.Good;

            v.OnSimpleReadValueAsync = (c, n, ct) => new ValueTask<AttributeSimpleReadResult>(
                new AttributeSimpleReadResult(ServiceResult.Good, new Variant(123.5)));

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetDouble(), Is.EqualTo(123.5));
        }

        [Test]
        public async Task ReadAttributeAsyncSimpleSlotPropagatesCachedStatusCode()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            // The simple async path mirrors OnSimpleReadValue and reuses the
            // variable's cached StatusCode — verify the framework
            // propagates a non-Good cached code to the caller.
            Assert.That(v.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadWaitingForInitialData));

            v.OnSimpleReadValueAsync = (c, n, ct) => new ValueTask<AttributeSimpleReadResult>(
                new AttributeSimpleReadResult(ServiceResult.Good, new Variant(123.5)));

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadWaitingForInitialData));
        }

        /// <summary>
        /// Verifies that caller cancellation reaches the full read hook and is reported as a bad service result.
        /// </summary>
        [Test]
        public async Task ReadAttributeAsyncWrapsCancellationFromFullSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            using var cts = new CancellationTokenSource();
            CancellationToken seenToken = default;

            v.OnReadValueAsync = (c, n, range, encoding, ct) =>
            {
                seenToken = ct;
                ct.ThrowIfCancellationRequested();
                return new ValueTask<AttributeReadResult>(
                    new AttributeReadResult(
                        ServiceResult.Good, new Variant(0.0), StatusCodes.Good, DateTimeUtc.Now));
            };

            await cts.CancelAsync().ConfigureAwait(false);
            var dv = new DataValue();
            // The async hook contract mirrors the sync flow: exceptions
            // (including OperationCanceledException) are caught and surfaced
            // as a Bad ServiceResult, never thrown to the caller.
            ServiceResult result;
            (result, dv) = await v.ReadAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv, cts.Token).ConfigureAwait(false);

            Assert.That(seenToken, Is.EqualTo(cts.Token), "Cancellation token must propagate to the hook.");
            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadUnexpectedError));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public async Task ReadAttributeAsyncWrapsHandlerExceptions()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();

            v.OnReadValueAsync = (c, n, range, encoding, ct) => throw new InvalidOperationException("boom");

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadUnexpectedError));
            Assert.That(dv.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task WriteAttributeAsyncRoutesValueWritesToFullAsyncSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            double observedValue = double.NaN;

            v.OnWriteValueAsync = (c, n, range, value, ct) =>
            {
                observedValue = value.GetDouble();
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good));
            };

            var dv = new DataValue(
                new Variant(99.5),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(observedValue, Is.EqualTo(99.5));
            Assert.That(v.Value.GetDouble(), Is.EqualTo(99.5),
                "BaseVariableState should mirror the value into its cache after a successful async write.");
        }

        /// <summary>
        /// Verifies the bound executor spans async dispatch and preserves copy and metadata semantics.
        /// </summary>
        [Test]
        public async Task BoundWriteExecutorPreservesCopyAndTimestampSemanticsAsync()
        {
            SystemContext ctx = CreateSystemContext();
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Array", 0),
                BrowseName = new QualifiedName("Array", 0),
                DisplayName = new LocalizedText("Array"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                Value = Variant.From(new[] { 1, 2 }),
                CopyPolicy = VariableCopyPolicy.CopyOnWrite
            };
            int synchronousExecutions = 0;
            int asynchronousExecutions = 0;
            variable.BindWriteOperationExecutor(new NodeWriteOperationExecutor(
                operation =>
                {
                    synchronousExecutions++;
                    return operation();
                },
                async (operation, cancellationToken) =>
                {
                    asynchronousExecutions++;
                    return await operation(cancellationToken).ConfigureAwait(false);
                }));
            Variant observedValue = Variant.Null;
            int handlerCalls = 0;
            variable.OnSimpleWriteValueAsync = (_, _, value, _) =>
            {
                handlerCalls++;
                observedValue = value;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good));
            };

            int[] input = [5, 6];
            var sourceTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            ServiceResult result = await variable.WriteAttributeAsync(
                ctx,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(
                    Variant.From(input),
                    StatusCodes.Uncertain,
                    sourceTimestamp)).ConfigureAwait(false);

            input[0] = 99;

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(synchronousExecutions, Is.Zero);
            Assert.That(asynchronousExecutions, Is.EqualTo(1));
            Assert.That(handlerCalls, Is.EqualTo(1));
            Assert.That(observedValue.TryGetValue(out ArrayOf<int> passedValue), Is.True);
            Assert.That(passedValue[0], Is.EqualTo(5));
            Assert.That(passedValue[1], Is.EqualTo(6));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cachedValue), Is.True);
            Assert.That(cachedValue[0], Is.EqualTo(5));
            Assert.That(cachedValue[1], Is.EqualTo(6));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(sourceTimestamp));
        }

        /// <summary>
        /// Verifies the non-virtual public entry keeps an asynchronous core override admitted through cache commit.
        /// </summary>
        [Test]
        public async Task WriteAttributeAsyncIsNonVirtualAndGuardsCoreOverrideThroughCacheCommitAsync()
        {
            Assert.That(
                typeof(NodeState).GetMethod(nameof(NodeState.WriteAttributeAsync))!.IsVirtual,
                Is.False);
            SystemContext context = CreateSystemContext();
            var variable = CreateCoreOverrideVariable();
            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            var coreEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coreRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            variable.CoreEntered = coreEntered;
            variable.CoreRelease = coreRelease;

            Task<ServiceResult> pending = variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0))).AsTask();

            try
            {
                Assert.That(await coreEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                ServiceResult rejected = variable.WriteAttribute(
                    context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(3.0)));
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));

                coreRelease.TrySetResult(true);
                Assert.That(ServiceResult.IsGood(await pending.WaitAsync(TimeSpan.FromSeconds(5))), Is.True);
            }
            finally
            {
                coreRelease.TrySetResult(true);
                _ = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
        }

        /// <summary>
        /// Verifies a core override without a base call is still admitted as one asynchronous write.
        /// </summary>
        [Test]
        public async Task WriteAttributeAsyncGuardsCoreOverrideWithoutBaseCallAsync()
        {
            SystemContext context = CreateSystemContext();
            var variable = CreateCoreOverrideVariable();
            variable.SkipBaseCore = true;
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            var coreEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coreRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            variable.CoreEntered = coreEntered;
            variable.CoreRelease = coreRelease;

            Task<ServiceResult> pending = variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0))).AsTask();

            try
            {
                Assert.That(await coreEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                ServiceResult rejected = variable.WriteAttribute(
                    context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(3.0)));
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
                coreRelease.TrySetResult(true);
                Assert.That(ServiceResult.IsGood(await pending.WaitAsync(TimeSpan.FromSeconds(5))), Is.True);
            }
            finally
            {
                coreRelease.TrySetResult(true);
                _ = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
        }

        /// <summary>
        /// Verifies non-Value async writes dispatch to the protected core without Value-write admission.
        /// </summary>
        [Test]
        public async Task WriteAttributeAsyncDispatchesNonValueAttributesToCoreAsync()
        {
            SystemContext context = CreateSystemContext();
            var variable = CreateCoreOverrideVariable();
            variable.WriteMask = AttributeWriteMask.DisplayName;
            variable.UserWriteMask = AttributeWriteMask.DisplayName;
            int asyncAdmissions = 0;
            variable.BindWriteOperationExecutor(new NodeWriteOperationExecutor(
                operation => operation(),
                async (operation, cancellationToken) =>
                {
                    asyncAdmissions++;
                    return await operation(cancellationToken).ConfigureAwait(false);
                }));

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.DisplayName,
                NumericRange.Null,
                new DataValue(new Variant(new LocalizedText("Updated")))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.LastAttributeId, Is.EqualTo(Attributes.DisplayName));
            Assert.That(asyncAdmissions, Is.Zero);
            Assert.That(variable.DisplayName.Text, Is.EqualTo("Updated"));
        }

        /// <summary>
        /// Verifies a cancelled same-node reentrant call releases its wait without releasing outer admission early.
        /// </summary>
        [Test]
        public async Task CoreOverrideSameNodePublicWriteCancellationReleasesAdmissionAsync()
        {
            SystemContext context = CreateSystemContext();
            var variable = CreateCoreOverrideVariable();
            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            using var nestedCancellation = new CancellationTokenSource();
            variable.NestedWriteToken = nestedCancellation.Token;
            variable.ReenterPublicWrite = true;
            var nestedWriteEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            variable.NestedWriteEntered = nestedWriteEntered;

            Task<ServiceResult> outer = variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0))).AsTask();

            try
            {
                Assert.That(
                    await nestedWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                    Is.True);
                Assert.That(outer.IsCompleted, Is.False);
                nestedCancellation.Cancel();
                Assert.That(ServiceResult.IsGood(await outer.WaitAsync(TimeSpan.FromSeconds(5))), Is.True);
                Assert.That(variable.NestedWriteCancelled, Is.True);

                ServiceResult followup = await variable.WriteAttributeAsync(
                    context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(new Variant(4.0))).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(followup), Is.True);
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(4.0));
            }
            finally
            {
                nestedCancellation.Cancel();
                _ = await outer.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        /// <summary>
        /// Verifies a bound native indexed write retains its merged value and requested metadata.
        /// </summary>
        [Test]
        public async Task BoundWriteAttributeAsyncMergesIndexAndMetadataAsync()
        {
            SystemContext context = CreateSystemContext();
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("BoundArray", 0),
                BrowseName = new QualifiedName("BoundArray", 0),
                DisplayName = new LocalizedText("BoundArray"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                Value = Variant.From(s_initialArray)
            };
            variable.BindWriteOperationExecutor(CreateInlineWriteExecutor());
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            var sourceTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 2);

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Parse("1:2"),
                new DataValue(Variant.From(s_slice), StatusCodes.Uncertain, sourceTimestamp));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.Value.GetInt32Array(), Is.EqualTo(s_mergedArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(sourceTimestamp));
        }

        /// <summary>
        /// Verifies a bound native handler can suppress cache and metadata changes.
        /// </summary>
        [Test]
        public async Task BoundWriteAttributeAsyncHonorsSuppressCacheUpdateAsync()
        {
            SystemContext context = CreateSystemContext();
            var originalTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 3);
            var variable = CreateReadableVariable();
            variable.Value = new Variant(1.0);
            variable.StatusCode = StatusCodes.Uncertain;
            variable.Timestamp = originalTimestamp;
            variable.BindWriteOperationExecutor(CreateInlineWriteExecutor());
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
                new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good) { SuppressCacheUpdate = true });

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(new Variant(2.0), StatusCodes.Good));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(originalTimestamp));
        }

        /// <summary>
        /// Dependent state updates run only after an ordinary cache commit, despite post-handler cancellation.
        /// </summary>
        [TestCase("success")]
        [TestCase("cancel")]
        [TestCase("reject")]
        [TestCase("suppress")]
        [TestCase("range")]
        [TestCase("external")]
        [TestCase("throw")]
        public async Task OrdinaryCachePostCommitHonorsCommitBoundaryAsync(string scenario)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            variable.Value = Variant.From(1.0);
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            double observedValue = 0;
            StatusCode reentrantStatus = StatusCodes.Good;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                if (scenario == "cancel")
                {
                    cancellation.Cancel();
                }
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(
                    scenario == "reject" ? new ServiceResult(StatusCodes.BadOutOfRange) : ServiceResult.Good)
                {
                    UseOrdinaryCacheWrite = scenario != "external",
                    SuppressCacheUpdate = scenario == "suppress",
                    AfterOrdinaryCacheCommit = () =>
                    {
                        calls++;
                        observedValue = variable.Value.GetDouble();
                        reentrantStatus = variable.WriteAttribute(
                            context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(9.0))).StatusCode;
                        if (scenario == "throw")
                        {
                            throw new InvalidOperationException("Dependent state update failed.");
                        }
                    }
                });
            };

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                scenario == "range" ? NumericRange.Parse("0") : NumericRange.Null,
                new DataValue(Variant.From(2.0)),
                cancellation.Token).ConfigureAwait(false);

            bool committed = scenario is "success" or "cancel" or "throw" or "external";
            bool updatedDependentState = scenario is "success" or "cancel" or "throw";
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(committed ? 2.0 : 1.0));
            Assert.That(calls, Is.EqualTo(updatedDependentState ? 1 : 0));
            if (updatedDependentState)
            {
                Assert.That(observedValue, Is.EqualTo(2.0));
                Assert.That(reentrantStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
            uint expected = scenario switch
            {
                "reject" => StatusCodes.BadOutOfRange,
                "range" => StatusCodes.BadIndexRangeNoData,
                "throw" => StatusCodes.BadUnexpectedError,
                _ => StatusCodes.Good
            };
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)expected));
            Assert.That(ServiceResult.IsGood(variable.WriteAttribute(
                context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(5.0)))), Is.True);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(5.0));
        }

        /// <summary>
        /// Existing external full hooks keep accepted-write cache invalidation when a range cannot merge.
        /// </summary>
        [Test]
        public async Task CustomFullAsyncHookRetainsAcceptedInvalidRangeCacheSemanticsAsync()
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.Value = Variant.From(new[] { 1, 2, 3 });
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 9);
            variable.BindWriteOperationExecutor(CreateInlineWriteExecutor());
            int calls = 0;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Parse("4:5"),
                new DataValue(Variant.From(new[] { 20, 30 }))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.IsNull, Is.True);
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(variable.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        [Test]
        public async Task WriteAttributeAsyncSkipsCacheOnHandlerFailure()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.Value = 1.0;

            v.OnWriteValueAsync = (c, n, range, value, ct) =>
                new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(StatusCodes.BadInvalidArgument));

            var dv = new DataValue(
                new Variant(99.5),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
            Assert.That(v.Value.GetDouble(), Is.EqualTo(1.0),
                "Cache must NOT advance when the async hook reports a Bad status.");
        }

        [Test]
        public async Task WriteAttributeAsyncMergesIndexRangeSliceIntoCachedValue()
        {
            SystemContext ctx = CreateSystemContext();
            var v = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Arr", 0),
                BrowseName = new QualifiedName("Arr", 0),
                DisplayName = new LocalizedText("Arr"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                Value = Variant.From(s_initialArray)
            };
            v.BindWriteOperationExecutor(CreateInlineWriteExecutor());
            NumericRange observedRange = NumericRange.Null;

            v.OnWriteValueAsync = (c, n, range, value, ct) =>
            {
                observedRange = range;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good));
            };

            var sourceTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 4);
            var dv = new DataValue(
                Variant.From(s_slice),
                StatusCodes.Uncertain,
                sourceTimestamp);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Parse("1:2"), dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(observedRange.IsNull, Is.False);
            Assert.That(v.Value.GetInt32Array(), Is.EqualTo(s_mergedArray.ToArrayOf()),
                "An index-range write must only replace the addressed elements of the cached value.");
            Assert.That(v.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(v.Timestamp, Is.EqualTo(sourceTimestamp));
        }

        /// <summary>
        /// Verifies that an accepted slice invalidates an unusable cache while still reporting a value change.
        /// </summary>
        [Test]
        public async Task WriteAttributeAsyncReportsChangeWhenCacheCannotAbsorbIndexRangeSlice()
        {
            SystemContext ctx = CreateSystemContext();
            // The handler owns the data; the cached value was never assigned.
            var v = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("ArrNoCache", 0),
                BrowseName = new QualifiedName("ArrNoCache", 0),
                DisplayName = new LocalizedText("ArrNoCache"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                // the write carries a status code and a source timestamp (Part 3 8.57).
                AccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                OnWriteValueAsync = (c, n, range, value, ct) =>
                    new ValueTask<AttributeWriteResult>(
                        new AttributeWriteResult(ServiceResult.Good))
            };

            await v.ClearChangeMasksAsync(ctx, false).ConfigureAwait(false);

            var sourceTimestamp = new DateTimeUtc(2024, 1, 2, 3, 4, 5);
            var dv = new DataValue(
                Variant.From(s_slice),
                StatusCodes.Uncertain,
                sourceTimestamp);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Parse("1:2"), dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(v.Value.IsNull, Is.True,
                "The slice must not be stored as the whole value.");
            Assert.That(v.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value),
                "A successful handler write must still raise a value change.");
            Assert.That(v.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData),
                "An unmergeable slice invalidates the local cache until a fresh read supplies the whole value.");
            Assert.That(v.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task WriteAttributeAsyncRoutesValueWritesToSimpleAsyncSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();

            v.OnSimpleWriteValueAsync = (c, n, value, ct) => new ValueTask<AttributeWriteResult>(
                new AttributeWriteResult(ServiceResult.Good));

            var dv = new DataValue(
                new Variant(11.0),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(v.Value.GetDouble(), Is.EqualTo(11.0));
        }

        [Test]
        public async Task WriteAttributeAsyncRejectsIndexRangeOnSimpleSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.OnSimpleWriteValueAsync = (c, n, value, ct) => new ValueTask<AttributeWriteResult>(
                new AttributeWriteResult(ServiceResult.Good));

            var range = NumericRange.Parse("0:3");
            var dv = new DataValue(
                new Variant(11.0),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, range, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadWriteNotSupported),
                "The simple async write hook does not support index-range writes.");
        }

        /// <summary>
        /// Verifies that the synchronous simple hook rejects indexed writes before calling the handler.
        /// </summary>
        [Test]
        public void WriteAttributeRejectsIndexRangeOnSimpleSyncHandler()
        {
            // Part 4 5.11.4.4: Bad_WriteNotSupported "is also used if writing of
            // IndexRanges is not supported for a Node".
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            bool called = false;
            v.OnSimpleWriteValue = (_, _, ref _) =>
            {
                called = true;
                return ServiceResult.Good;
            };

            ServiceResult result = v.WriteAttribute(
                ctx,
                Attributes.Value,
                NumericRange.Parse("0:3"),
                new DataValue(new Variant(11.0)));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(called, Is.False);
        }

        [Test]
        public async Task ReadAttributeAsyncFallsBackToSyncWhenNoAsyncSlotSet()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.Value = 5.5;

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetDouble(), Is.EqualTo(5.5));
        }

        [Test]
        public async Task UnboundWriteAttributeAsyncFallsBackToSyncWhenNoAsyncSlotSet()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.Value = 1.0;

            var dv = new DataValue(
                new Variant(2.0),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(v.Value.GetDouble(), Is.EqualTo(2.0));
        }

        /// <summary>
        /// Bound writes without native async hooks reject before synchronous callbacks or cache mutation.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task BoundWriteAttributeAsyncRejectsSynchronousFallbackWithoutChangingCacheAsync(int handler)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            variable.Value = new Variant(1.0);
            variable.StatusCode = StatusCodes.Uncertain;
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 5);
            variable.Timestamp = timestamp;
            int calls = 0;
            int accessChecks = 0;
            variable.OnReadUserAccessLevel = (_, _, ref _) =>
            {
                accessChecks++;
                return ServiceResult.Good;
            };
            if (handler == 1)
            {
                variable.OnWriteValue = (
                    ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                    ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            else if (handler == 2)
            {
                variable.OnSimpleWriteValue = (_, _, ref _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            NodeStateChangeMasks changeMasks = variable.ChangeMasks;

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(2.0)));

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(calls, Is.Zero);
            Assert.That(accessChecks, Is.Zero);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(changeMasks));

            ServiceResult synchronous = variable.WriteAttribute(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(3.0)));
            Assert.That(ServiceResult.IsGood(synchronous), Is.True);
            Assert.That(calls, Is.EqualTo(handler == 0 ? 0 : 1));
            Assert.That(accessChecks, Is.EqualTo(1));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(3.0));
        }

        /// <summary>
        /// Unsupported whole and indexed writes cannot invoke sync hooks or change cached value and metadata.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public async Task BoundWriteAttributeAsyncRejectsMetadataBeforeSynchronousFallbackAsync(
            int handler, bool indexed)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.AccessLevel |= AccessLevels.StatusWrite | AccessLevels.TimestampWrite;
            variable.UserAccessLevel |= AccessLevels.StatusWrite | AccessLevels.TimestampWrite;
            variable.Value = Variant.From(new[] { 1, 2, 3, 4 });
            variable.StatusCode = StatusCodes.Uncertain;
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 6);
            variable.Timestamp = timestamp;
            int calls = 0;
            if (handler == 1)
            {
                variable.OnWriteValue = (
                    ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                    ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            else if (handler == 2)
            {
                variable.OnSimpleWriteValue = (_, _, ref _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            variable.BindWriteOperationExecutor(CreateInlineWriteExecutor());
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            NodeStateChangeMasks changeMasks = variable.ChangeMasks;

            ServiceResult result = await variable.WriteAttributeAsync(
                context,
                Attributes.Value,
                indexed ? NumericRange.Parse("1:2") : NumericRange.Null,
                new DataValue(
                    Variant.From(s_slice), StatusCodes.Good, new DateTimeUtc(2026, 1, 1, 0, 0, 8)));

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(calls, Is.Zero);
            Assert.That(variable.Value.GetInt32Array(), Is.EqualTo(s_initialArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(changeMasks));
        }

        /// <summary>
        /// A custom core calling base without native async hooks rejects and releases admission.
        /// </summary>
        [Test]
        public async Task BoundCoreOverrideCallingBaseWithoutAsyncHooksRejectsAndReleasesAdmissionAsync()
        {
            SystemContext context = CreateSystemContext();
            var variable = CreateCoreOverrideVariable();
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            variable.StatusCode = StatusCodes.Uncertain;
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 7);
            variable.Timestamp = timestamp;

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(2.0)));

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(variable.LastAttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));

            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            ServiceResult retry = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(3.0)));
            Assert.That(ServiceResult.IsGood(retry), Is.True);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(3.0));

            variable.OnSimpleWriteValueAsync = null;
            StatusCode statusAfterRetry = variable.StatusCode;
            DateTimeUtc timestampAfterRetry = variable.Timestamp;
            ServiceResult unsupportedAgain = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(4.0)));
            Assert.That(unsupportedAgain.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(3.0));
            Assert.That(variable.StatusCode, Is.EqualTo(statusAfterRetry));
            Assert.That(variable.Timestamp, Is.EqualTo(timestampAfterRetry));
        }

        /// <summary>
        /// Unbound writes continue invoking synchronous handlers and committing their result.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnboundWriteAttributeAsyncPreservesSynchronousHandlersAsync(bool simple)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            int calls = 0;
            if (simple)
            {
                variable.OnSimpleWriteValue = (_, _, ref _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            else
            {
                variable.OnWriteValue = (
                    ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                    ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(2.0)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
        }

        /// <summary>
        /// Native async hooks take precedence over sync hooks on bound nodes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BoundWriteAttributeAsyncPrefersNativeAsyncHooksAsync(bool simple)
        {
            SystemContext context = CreateSystemContext();
            BaseDataVariableState variable = CreateReadableVariable();
            variable.BindWriteOperationExecutor(CreateSerializedWriteExecutor());
            int synchronousCalls = 0;
            int asynchronousCalls = 0;
            int accessChecks = 0;
            variable.OnReadUserAccessLevel = (_, _, ref _) =>
            {
                accessChecks++;
                return ServiceResult.Good;
            };
            variable.OnWriteValue = (
                ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
            {
                synchronousCalls++;
                return StatusCodes.BadInvalidState;
            };
            variable.OnSimpleWriteValue = (_, _, ref _) =>
            {
                synchronousCalls++;
                return StatusCodes.BadInvalidState;
            };
            if (simple)
            {
                variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
                {
                    asynchronousCalls++;
                    return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                };
            }
            else
            {
                variable.OnWriteValueAsync = (_, _, _, _, _) =>
                {
                    asynchronousCalls++;
                    return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                };
            }

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, new DataValue(new Variant(2.0)));
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(asynchronousCalls, Is.EqualTo(1));
            Assert.That(synchronousCalls, Is.Zero);
            Assert.That(accessChecks, Is.EqualTo(1));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
        }

        [Test]
        public async Task ReadAttributeAsyncFallsThroughForNonValueAttribute()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.OnReadValueAsync = (c, n, range, encoding, ct) =>
            {
                Assert.Fail("OnReadValueAsync must not run for non-Value attributes.");
                return new ValueTask<AttributeReadResult>(
                    new AttributeReadResult(
                        ServiceResult.Good, Variant.Null, StatusCodes.Good, DateTimeUtc.Now));
            };

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(
                ctx, Attributes.DisplayName, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetLocalizedText().Text, Is.EqualTo("Var"));
        }

        [Test]
        public async Task ReadAttributeAsyncReturnsBadNotReadableForNoAccessLevel()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.AccessLevel = AccessLevels.None;
            v.OnReadValueAsync = (c, n, range, encoding, ct) =>
            {
                Assert.Fail("Async hook must not run when CurrentRead is denied.");
                return new ValueTask<AttributeReadResult>(default(AttributeReadResult));
            };

            var dv = new DataValue();
            (ServiceResult result, dv) = await v.ReadAttributeAsync(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadNotReadable));
        }

        [Test]
        public async Task WriteAttributeAsyncReturnsBadNotWritableForNoAccessLevel()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.AccessLevel = AccessLevels.CurrentRead;
            v.OnWriteValueAsync = (c, n, range, value, ct) =>
            {
                Assert.Fail("Async hook must not run when CurrentWrite is denied.");
                return new ValueTask<AttributeWriteResult>(default(AttributeWriteResult));
            };

            var dv = new DataValue(
                new Variant(2.0),
                StatusCodes.Good);

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadNotWritable));
        }
    }
}
