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

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Verifies the cache and operation-result contracts of asynchronous indexed writes.
    /// </summary>
    [TestFixture]
    public sealed class BaseVariableStateIndexedAsyncWriteTests
    {
        /// <summary>
        /// Verifies that a mergeable slice retains the surrounding elements and incoming metadata.
        /// </summary>
        /// <param name="clamped">Whether the handler returns GoodClamped rather than Good.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SuccessfulIndexedWriteMergesTheCacheAndRetainsTheOperationStatus(bool clamped)
        {
            ArrayOf<int> initial = [1, 2, 3];
            BaseDataVariableState variable = CreateVariable(new Variant(initial));
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            StatusCode status = clamped ? Ua.StatusCodes.GoodClamped : StatusCodes.Good;
            variable.OnWriteValueAsync = (_, _, range, value, _) =>
            {
                Assert.That(range.ToString(), Is.EqualTo("1"));
                Assert.That(value.TryGetValue(out ArrayOf<int> incoming), Is.True);
                Assert.That(incoming.Count, Is.EqualTo(1));
                Assert.That(incoming[0], Is.EqualTo(9));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(new ServiceResult(status)));
            };
            ArrayOf<int> replacement = [9];
            SystemContext context = CreateContext();
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Parse("1"),
                new DataValue(new Variant(replacement), StatusCodes.Uncertain, timestamp)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached.Count, Is.EqualTo(3));
            Assert.That(cached[0], Is.EqualTo(1));
            Assert.That(cached[1], Is.EqualTo(9));
            Assert.That(cached[2], Is.EqualTo(3));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// Verifies that an accepted slice clears missing or stale cached data without changing the handler result.
        /// </summary>
        /// <param name="staleCache">Whether the cache contains an array too short for the accepted range.</param>
        /// <param name="clamped">Whether the handler returns GoodClamped rather than Good.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SuccessfulWriteWithoutAUsableCacheWaitsForFreshData(bool staleCache, bool clamped)
        {
            ArrayOf<int> stale = [1];
            BaseDataVariableState variable = CreateVariable(staleCache ? new Variant(stale) : Variant.Null);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            variable.Timestamp = timestamp;
            StatusCode status = clamped ? Ua.StatusCodes.GoodClamped : StatusCodes.Good;
            int writes = 0;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                writes++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(new ServiceResult(status)));
            };
            ArrayOf<int> replacement = [9];
            SystemContext context = CreateContext();
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Parse("2"),
                new DataValue(new Variant(replacement), StatusCodes.Uncertain, timestamp)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(variable.Value.IsNull, Is.True);
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(variable.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            (ServiceResult read, DataValue value) = await variable.ReadAttributeAsync(
                context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(variable.Timestamp, Is.EqualTo(DateTimeUtc.MinValue),
                "A fallback read may synthesize a response timestamp but must leave the invalidated cache untouched.");
        }

        /// <summary>
        /// Verifies that a rejected slice leaves the cached value, metadata and change mask untouched.
        /// </summary>
        [Test]
        public async Task RejectedIndexedWriteDoesNotChangeTheCache()
        {
            ArrayOf<int> initial = [1, 2, 3];
            BaseDataVariableState variable = CreateVariable(new Variant(initial));
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            variable.Timestamp = timestamp;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
                new ValueTask<AttributeWriteResult>(
                    new AttributeWriteResult(new ServiceResult(StatusCodes.BadNotWritable)));
            ArrayOf<int> replacement = [9];
            SystemContext context = CreateContext();
            await variable.ClearChangeMasksAsync(context, false).ConfigureAwait(false);

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Parse("1"),
                new DataValue(new Variant(replacement), StatusCodes.Uncertain, new DateTimeUtc(2026, 1, 1, 0, 0, 1)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached.Count, Is.EqualTo(3));
            Assert.That(cached[0], Is.EqualTo(1));
            Assert.That(cached[1], Is.EqualTo(2));
            Assert.That(cached[2], Is.EqualTo(3));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies that data-type and rank validation reject invalid slices before invoking the handler.
        /// </summary>
        /// <param name="scalar">Whether the replacement has scalar rank instead of the wrong element type.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidSliceTypeDoesNotInvokeTheHandlerOrChangeTheCache(bool scalar)
        {
            ArrayOf<int> initial = [1, 2, 3];
            BaseDataVariableState variable = CreateVariable(new Variant(initial));
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                Assert.Fail("The handler must not receive a slice with an invalid type or rank.");
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            ArrayOf<string> strings = ["invalid"];
            Variant replacement = scalar ? new Variant(9) : new Variant(strings);

            ServiceResult result = await variable.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Parse("1"), new DataValue(replacement))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached, Is.EqualTo(initial));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that node and user access checks reject indexed writes before invoking the handler.
        /// </summary>
        /// <param name="userDenied">Whether only user access, rather than node access, denies writing.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DeniedAccessDoesNotInvokeTheHandlerOrChangeTheCache(bool userDenied)
        {
            ArrayOf<int> initial = [1, 2, 3];
            BaseDataVariableState variable = CreateVariable(new Variant(initial));
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            if (!userDenied)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
            }
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                Assert.Fail("The handler must not run when write access is denied.");
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            ArrayOf<int> replacement = [9];

            ServiceResult result = await variable.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Parse("1"), new DataValue(new Variant(replacement)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode,
                Is.EqualTo(userDenied ? StatusCodes.BadUserAccessDenied : StatusCodes.BadNotWritable));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached, Is.EqualTo(initial));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that unsupported status and timestamp writes do not reach the indexed-write handler.
        /// </summary>
        /// <param name="metadata">The unsupported data-value metadata supplied by the write.</param>
        [TestCase("StatusCode")]
        [TestCase("SourceTimestamp")]
        [TestCase("ServerTimestamp")]
        public async Task UnsupportedMetadataDoesNotInvokeTheHandlerOrChangeTheCache(string metadata)
        {
            ArrayOf<int> initial = [1, 2, 3];
            BaseDataVariableState variable = CreateVariable(new Variant(initial));
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                Assert.Fail("The handler must not run when metadata cannot be written.");
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            ArrayOf<int> replacement = [9];
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            var value = new DataValue(
                new Variant(replacement),
                metadata == "StatusCode" ? StatusCodes.Uncertain : StatusCodes.Good,
                metadata == "SourceTimestamp" ? timestamp : DateTimeUtc.MinValue,
                metadata == "ServerTimestamp" ? timestamp : DateTimeUtc.MinValue);

            ServiceResult result = await variable.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Parse("1"), value).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached, Is.EqualTo(initial));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
        }

        /// <summary>
        /// Creates a system context with the namespace and type tables needed by value validation.
        /// </summary>
        /// <returns>A context for the isolated variable under test.</returns>
        private static SystemContext CreateContext()
        {
            var namespaces = new NamespaceTable();
            return new SystemContext(TelemetryExtensions.InternalOnly__TelemetryHook())
            {
                NamespaceUris = namespaces,
                TypeTable = new TypeTable(namespaces)
            };
        }

        /// <summary>
        /// Creates a writable Int32 array variable with explicit status and source-timestamp permissions.
        /// </summary>
        /// <param name="value">The initial cached value, or a null value when no cache is available.</param>
        /// <returns>The variable used by indexed-write tests.</returns>
        private static BaseDataVariableState CreateVariable(Variant value)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Value", 0),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite |
                    AccessLevels.TimestampWrite,
                Value = value,
                StatusCode = StatusCodes.Good
            };
        }
    }
}
