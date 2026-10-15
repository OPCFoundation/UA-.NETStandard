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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UaStatusCodes = Opc.Ua.StatusCodes;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Exercises explicit write-cache ownership through both asynchronous Value-attribute hooks.
    /// </summary>
    [TestFixture]
    public sealed class BaseVariableStateWriteOwnershipTests
    {
        /// <summary>
        /// Existing constructor and default-value callers keep write-through behavior.
        /// </summary>
        [Test]
        public void CacheSuppressionIsOptIn()
        {
            Assert.That(new AttributeWriteResult(ServiceResult.Good).SuppressCacheUpdate, Is.False);
            Assert.That(default(AttributeWriteResult).SuppressCacheUpdate, Is.False);
        }

        /// <summary>
        /// Acknowledgment alone does not publish data when an external owner was explicitly selected.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task SuccessfulWriteRespectsTheSelectedCacheOwner(
            bool simple, bool suppress, bool observationDuringWrite)
        {
            SystemContext context = CreateContext();
            BaseDataVariableState variable = CreateVariable();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Bind(variable, simple, async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return new AttributeWriteResult(UaStatusCodes.GoodClamped) { SuppressCacheUpdate = suppress };
            });

            Task<ServiceResult> pending = variable.WriteAttributeAsync(context, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(180.0), StatusCodes.Good, s_requestedTimestamp)).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (observationDuringWrite)
                {
                    variable.Value = Variant.From(182.0);
                    variable.StatusCode = UaStatusCodes.UncertainLastUsableValue;
                    variable.Timestamp = s_observedTimestamp;
                }
                await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            ServiceResult result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(UaStatusCodes.GoodClamped));
                Assert.That(variable.Value.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.EqualTo(suppress ? observationDuringWrite ? 182.0 : 175.0 : 180.0));
                Assert.That(variable.StatusCode, Is.EqualTo(
                    suppress ? UaStatusCodes.UncertainLastUsableValue : StatusCodes.Good));
                Assert.That(variable.Timestamp, Is.EqualTo(suppress
                    ? observationDuringWrite ? s_observedTimestamp : s_initialTimestamp : s_requestedTimestamp));
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(suppress ? NodeStateChangeMasks.None : NodeStateChangeMasks.Value));
            });
        }

        /// <summary>
        /// Failure and cancellation keep their existing error semantics and never alter independent observations.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FailedWritePreservesTheAuthoritativeCache(bool simple, bool cancel)
        {
            SystemContext context = CreateContext();
            BaseDataVariableState variable = CreateVariable();
            using var cancellation = new CancellationTokenSource();
            Bind(variable, simple, async ct =>
            {
                variable.Value = Variant.From(182.0);
                variable.Timestamp = s_observedTimestamp;
                await variable.ClearChangeMasksAsync(context, false, ct).ConfigureAwait(false);
                if (cancel)
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }
                return new AttributeWriteResult(StatusCodes.BadOutOfRange) { SuppressCacheUpdate = true };
            });

            ServiceResult result = await variable.WriteAttributeAsync(context, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(180.0)), cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode,
                    Is.EqualTo(cancel ? StatusCodes.BadUnexpectedError : StatusCodes.BadOutOfRange));
                Assert.That(variable.Value.TryGetValue(out double value) && value == 182.0, Is.True);
                Assert.That(variable.StatusCode, Is.EqualTo(UaStatusCodes.UncertainLastUsableValue));
                Assert.That(variable.Timestamp, Is.EqualTo(s_observedTimestamp));
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.None));
            });
        }

        /// <summary>
        /// Cache ownership does not bypass the ordinary access, type or metadata checks.
        /// </summary>
        [TestCase(false, "type")]
        [TestCase(true, "type")]
        [TestCase(false, "access")]
        [TestCase(true, "access")]
        [TestCase(false, "user")]
        [TestCase(true, "user")]
        [TestCase(false, "timestamp")]
        [TestCase(true, "timestamp")]
        public async Task CacheSuppressionPreservesPreWriteValidation(bool simple, string invalid)
        {
            SystemContext context = CreateContext();
            BaseDataVariableState variable = CreateVariable();
            StatusCode expected = StatusCodes.BadTypeMismatch;
            var request = new DataValue(Variant.From(180.0));
            switch (invalid)
            {
                case "type":
                    request = request.WithWrappedValue(Variant.From("not a Double"));
                    break;
                case "access":
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    expected = StatusCodes.BadNotWritable;
                    break;
                case "user":
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                    expected = StatusCodes.BadUserAccessDenied;
                    break;
                case "timestamp":
                    request = request.WithServerTimestamp(s_requestedTimestamp);
                    expected = StatusCodes.BadWriteNotSupported;
                    break;
            }
            int calls = 0;
            Bind(variable, simple, _ =>
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good) { SuppressCacheUpdate = true });
            });
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Null, request).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected));
                Assert.That(calls, Is.Zero);
                Assert.That(variable.Value.TryGetValue(out double value) && value == 175.0, Is.True);
                Assert.That(variable.StatusCode, Is.EqualTo(UaStatusCodes.UncertainLastUsableValue));
                Assert.That(variable.Timestamp, Is.EqualTo(s_initialTimestamp));
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.None));
            });
        }

        /// <summary>
        /// An externally applied indexed write must not merge the request into a different local observation.
        /// </summary>
        [Test]
        public async Task SuppressedIndexedWriteLeavesTheCompleteObservationUntouched()
        {
            SystemContext context = CreateContext();
            BaseDataVariableState variable = CreateVariable();
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.OneDimension;
            ArrayOf<int> initial = [1, 2, 3];
            variable.Value = Variant.From(initial);
            variable.OnWriteValueAsync = (_, _, range, value, _) =>
            {
                Assert.That(range.ToString(), Is.EqualTo("1"));
                Assert.That(value.TryGetValue(out ArrayOf<int> slice) && slice[0] == 9, Is.True);
                return new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(ServiceResult.Good) { SuppressCacheUpdate = true });
            };
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            ArrayOf<int> replacement = [9];

            ServiceResult result = await variable.WriteAttributeAsync(context, Attributes.Value,
                NumericRange.Parse("1"), new DataValue(Variant.From(replacement))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> retained), Is.True);
            Assert.That(retained, Is.EqualTo(initial));
            Assert.That(variable.Timestamp, Is.EqualTo(s_initialTimestamp));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Installs the same outcome on either asynchronous write hook.
        /// </summary>
        private static void Bind(
            BaseDataVariableState variable,
            bool simple,
            Func<CancellationToken, ValueTask<AttributeWriteResult>> write)
        {
            if (simple)
            {
                variable.OnSimpleWriteValueAsync = (_, _, _, ct) => write(ct);
            }
            else
            {
                variable.OnWriteValueAsync = (_, _, _, _, ct) => write(ct);
            }
        }

        /// <summary>
        /// Creates a scalar with an existing observation and explicit metadata-write permissions.
        /// </summary>
        private static BaseDataVariableState CreateVariable()
        {
            return new BaseDataVariableState(null)
            {
                NodeId = NodeId.Parse("s=snapshot-owned"),
                BrowseName = new QualifiedName("SnapshotOwned"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = Variant.From(175.0),
                StatusCode = UaStatusCodes.UncertainLastUsableValue,
                Timestamp = s_initialTimestamp,
                AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.TimestampWrite | AccessLevels.StatusWrite,
                UserAccessLevel =
                    AccessLevels.CurrentReadOrWrite | AccessLevels.TimestampWrite | AccessLevels.StatusWrite
            };
        }

        /// <summary>
        /// Supplies ordinary type validation without a running server.
        /// </summary>
        private static SystemContext CreateContext()
        {
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = new NamespaceTable(),
                TypeTable = new TypeTable(new NamespaceTable())
            };
        }

        /// <summary>The prior observation's source time.</summary>
        private static readonly DateTimeUtc s_initialTimestamp = new(2026, 1, 1, 0, 0, 0);

        /// <summary>The independently published observation's source time.</summary>
        private static readonly DateTimeUtc s_observedTimestamp = new(2026, 1, 1, 0, 0, 1);

        /// <summary>The distinct timestamp supplied with the write request.</summary>
        private static readonly DateTimeUtc s_requestedTimestamp = new(2026, 1, 1, 0, 0, 2);
    }
}
