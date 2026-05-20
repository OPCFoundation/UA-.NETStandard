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
    /// / <see cref="BaseVariableState.WriteAttributeAsync"/> overrides
    /// that route through them.
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
    ///   <item>When no async slot is set, the override defers to the
    ///         synchronous flow under the existing <c>lock(this)</c>.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BaseVariableStateAsyncHooksTests
    {
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

        private SystemContext CreateSystemContext()
        {
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                TypeTable = new TypeTable(m_messageContext.NamespaceUris)
            };
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

        // -----------------------------------------------------------------
        // OnReadValueAsync (full)
        // -----------------------------------------------------------------

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

        [Test]
        public async Task ReadAttributeAsyncFullSlotReleasesNodeStateLockDuringAwait()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            using var inHandlerGate = new ManualResetEventSlim(false);
            using var releaseGate = new ManualResetEventSlim(false);

            v.OnReadValueAsync = async (c, n, range, encoding, ct) =>
            {
                inHandlerGate.Set();
                // Block on a worker thread that takes lock(this) to prove
                // we are NOT holding it across the await.
                releaseGate.Wait(TimeSpan.FromSeconds(5), ct);
                await Task.Yield();
                return new AttributeReadResult(
                    ServiceResult.Good, new Variant(7.0), StatusCodes.Good, DateTimeUtc.Now);
            };

            var dv = new DataValue();
            ValueTask<(ServiceResult, DataValue)> readTask = v.ReadAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, dv);

            Assert.That(inHandlerGate.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "handler did not start");

            // Acquire lock(this) on a worker thread - this only succeeds
            // if the read path released the lock before awaiting the
            // handler.
            bool lockAcquired = false;
            Task lockTask = Task.Run(() =>
            {
#pragma warning disable CA2002
                lock (v)
#pragma warning restore CA2002
                {
                    lockAcquired = true;
                }
            });

            Assert.That(lockTask.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "lock(v) was not released during the async handler");
            Assert.That(lockAcquired, Is.True);

            releaseGate.Set();
            (ServiceResult result, _) = await readTask.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        // -----------------------------------------------------------------
        // OnSimpleReadValueAsync
        // -----------------------------------------------------------------

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

        // -----------------------------------------------------------------
        // Cancellation
        // -----------------------------------------------------------------

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

            cts.Cancel();
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

        // -----------------------------------------------------------------
        // OnWriteValueAsync (full) - cache update on success
        // -----------------------------------------------------------------

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

            var dv = new DataValue
            {
                WrappedValue = new Variant(99.5),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(observedValue, Is.EqualTo(99.5));
            Assert.That(v.Value.GetDouble(), Is.EqualTo(99.5),
                "BaseVariableState should mirror the value into its cache after a successful async write.");
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

            var dv = new DataValue
            {
                WrappedValue = new Variant(99.5),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
            Assert.That(v.Value.GetDouble(), Is.EqualTo(1.0),
                "Cache must NOT advance when the async hook reports a Bad status.");
        }

        // -----------------------------------------------------------------
        // OnSimpleWriteValueAsync
        // -----------------------------------------------------------------

        [Test]
        public async Task WriteAttributeAsyncRoutesValueWritesToSimpleAsyncSlot()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();

            v.OnSimpleWriteValueAsync = (c, n, value, ct) => new ValueTask<AttributeWriteResult>(
                new AttributeWriteResult(ServiceResult.Good));

            var dv = new DataValue
            {
                WrappedValue = new Variant(11.0),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

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
            var dv = new DataValue
            {
                WrappedValue = new Variant(11.0),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, range, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadIndexRangeInvalid),
                "The simple async write hook does not support index-range writes.");
        }

        // -----------------------------------------------------------------
        // Fallback to sync path when no async slot is set
        // -----------------------------------------------------------------

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
        public async Task WriteAttributeAsyncFallsBackToSyncWhenNoAsyncSlotSet()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateReadableVariable();
            v.Value = 1.0;

            var dv = new DataValue
            {
                WrappedValue = new Variant(2.0),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(v.Value.GetDouble(), Is.EqualTo(2.0));
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

        // -----------------------------------------------------------------
        // Access control
        // -----------------------------------------------------------------

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

            var dv = new DataValue
            {
                WrappedValue = new Variant(2.0),
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTimeUtc.Now
            };

            ServiceResult result = await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo((uint)StatusCodes.BadNotWritable));
        }
    }
}
