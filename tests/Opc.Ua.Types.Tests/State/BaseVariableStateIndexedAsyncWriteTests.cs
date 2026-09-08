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
    [TestFixture]
    public sealed class BaseVariableStateIndexedAsyncWriteTests
    {
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

            ServiceResult result = await variable.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Parse("1"),
                new DataValue(new Variant(replacement), StatusCodes.Good, timestamp)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached.Count, Is.EqualTo(3));
            Assert.That(cached[0], Is.EqualTo(1));
            Assert.That(cached[1], Is.EqualTo(9));
            Assert.That(cached[2], Is.EqualTo(3));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SuccessfulWriteWithoutAUsableCacheWaitsForFreshData(bool staleCache)
        {
            ArrayOf<int> stale = [1];
            BaseDataVariableState variable = CreateVariable(staleCache ? new Variant(stale) : Variant.Null);
            int writes = 0;
            variable.OnWriteValueAsync = (_, _, _, _, _) =>
            {
                writes++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            ArrayOf<int> replacement = [9];
            SystemContext context = CreateContext();

            ServiceResult result = await variable.WriteAttributeAsync(
                context, Attributes.Value, NumericRange.Parse("2"), new DataValue(new Variant(replacement)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(variable.Value.IsNull, Is.True);
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(variable.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
            (ServiceResult read, DataValue value) = await variable.ReadAttributeAsync(
                context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

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

            ServiceResult result = await variable.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Parse("1"), new DataValue(new Variant(replacement)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached.Count, Is.EqualTo(3));
            Assert.That(cached[1], Is.EqualTo(2));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(TelemetryExtensions.InternalOnly__TelemetryHook())
            {
                NamespaceUris = new NamespaceTable()
            };
        }

        private static BaseDataVariableState CreateVariable(Variant value)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Value", 0),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = value,
                StatusCode = StatusCodes.Good
            };
        }
    }
}
