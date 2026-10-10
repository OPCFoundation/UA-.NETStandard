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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.ViewModels;

namespace UaLens.Tests.Presentation
{
    [TestFixture]
    public sealed class WriteValueOperationTests
    {
        [Test]
        public async Task DeferredWriteAcceptsOneRequestAndRetainsSuccess()
        {
            var response = new TaskCompletionSource<WriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(response.Task));
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            var value = new DataValue(Variant.From(42));

            Task first = operation.WriteAsync(value);
            Task duplicate = operation.WriteAsync(value);
            WriteValueState pendingState = operation.State;
            response.SetResult(new WriteResponse { Results = [StatusCodes.Good] });
            await first.ConfigureAwait(false);
            Assert.That(pendingState, Is.EqualTo(WriteValueState.Writing));
            Assert.That(duplicate, Is.SameAs(first));
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Succeeded));
            string outcome = operation.Outcome;
            await operation.WriteAsync(value).ConfigureAwait(false);
            await operation.CancelAndDrainAsync().ConfigureAwait(false);
            Assert.That(operation.Outcome, Is.EqualTo(outcome));
            session.Verify(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ClosingWaitsForIgnoredCancellationAndRetainsKnownResult()
        {
            var response = new TaskCompletionSource<WriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISession> session = CreateSession();
            CancellationToken requestToken = default;
            session.Setup(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<WriteValue>, CancellationToken>((_, _, token) => requestToken = token)
                .Returns(new ValueTask<WriteResponse>(response.Task));
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            Task writing = operation.WriteAsync(new DataValue(Variant.From(42)));

            Task closing = operation.CancelAndDrainAsync();
            bool closedEarly = closing.IsCompleted;
            bool cancelled = requestToken.IsCancellationRequested;
            WriteValueState closingState = operation.State;
            response.SetResult(new WriteResponse { Results = [StatusCodes.Good] });
            await Task.WhenAll(writing, closing).ConfigureAwait(false);

            Assert.That(closedEarly, Is.False);
            Assert.That(cancelled, Is.True);
            Assert.That(closingState, Is.EqualTo(WriteValueState.Closing));
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Succeeded));
            Assert.That(operation.Outcome, Does.Contain("succeeded"));
        }

        [Test]
        public async Task CancelledDispatchedRequestExplainsUncertainRemoteOutcome()
        {
            var response = new TaskCompletionSource<WriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(response.Task));
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            Task writing = operation.WriteAsync(new DataValue(Variant.From(42)));
            Task closing = operation.CancelAndDrainAsync();
            response.SetCanceled();
            await Task.WhenAll(writing, closing).ConfigureAwait(false);

            Assert.That(operation.State, Is.EqualTo(WriteValueState.Uncertain));
            Assert.That(operation.Outcome, Does.Contain("may have applied"));
            Assert.That(operation.Outcome, Does.Contain("not rollback"));
            string outcome = operation.Outcome;
            await operation.DisposeAsync().ConfigureAwait(false);
            Assert.That(operation.Outcome, Is.EqualTo(outcome));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidMetadataPreventsAnyWrite(bool denied)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = denied
                        ? [new DataValue().WithStatus(StatusCodes.BadUserAccessDenied),
                            new DataValue(), new DataValue(), new DataValue()]
                        : []
                });
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            await operation.WriteAsync(new DataValue(Variant.From(42))).ConfigureAwait(false);

            Assert.That(operation.State, Is.EqualTo(WriteValueState.Failed));
            Assert.That(operation.Outcome, Does.Contain("metadata read failed"));
            Assert.That(operation.WasDispatched, Is.False);
            session.Verify(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CloseDuringReadRejectsLateMetadataAndNewWrites()
        {
            var response = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(response.Task));
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            Task loading = operation.LoadAsync();
            Task closing = operation.CancelAndDrainAsync();
            bool closedEarly = closing.IsCompleted;
            response.SetResult(Metadata());
            await Task.WhenAll(loading, closing).ConfigureAwait(false);
            await operation.WriteAsync(new DataValue(Variant.From(42))).ConfigureAwait(false);

            Assert.That(closedEarly, Is.False);
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Failed));
            Assert.That(operation.DataType.IsNull, Is.True);
            Assert.That(operation.Outcome, Does.Contain("No write was sent"));
            Assert.That(operation.WasDispatched, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrMalformedWriteRetainsOutcome(bool malformed)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse { Results = malformed ? [] : [StatusCodes.BadNotWritable] });
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            await operation.WriteAsync(new DataValue(Variant.From(42))).ConfigureAwait(false);
            string outcome = operation.Outcome;
            await operation.CancelAndDrainAsync().ConfigureAwait(false);

            Assert.That(operation.State, Is.EqualTo(malformed ? WriteValueState.Uncertain : WriteValueState.Failed));
            Assert.That(operation.Outcome, Is.EqualTo(outcome).And.Not.Empty);
        }

        [Test]
        public async Task MatrixMetadataDimensionsAreCopiedBeforeEditing()
        {
            uint[] dimensions = [2, 3];
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, ids, _) =>
                    {
                        Assert.That(ids.Count, Is.EqualTo(4));
                        Assert.That(ids[3].AttributeId, Is.EqualTo(Attributes.ArrayDimensions));
                    })
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3]))),
                        new DataValue(Variant.From(DataTypeIds.Int32)),
                        new DataValue(Variant.From(2)),
                        new DataValue(Variant.From(new ArrayOf<uint>(dimensions)))
                    ]
                });
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            dimensions[0] = 9;
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Editing));
            Assert.That(operation.ValueRank, Is.EqualTo(2));
            Assert.That(operation.ArrayDimensions.Count, Is.EqualTo(2));
            Assert.That(operation.ArrayDimensions[0], Is.EqualTo(2));
            Assert.That(operation.ArrayDimensions[1], Is.EqualTo(3));
        }

        [Test]
        public async Task MalformedArrayDimensionsPreventWriting()
        {
            Mock<ISession> session = CreateSession();
            session.Setup(value => value.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.From(1)),
                        new DataValue(Variant.From(DataTypeIds.Int32)),
                        new DataValue(Variant.From(ValueRanks.Scalar)),
                        new DataValue(Variant.From("not dimensions"))
                    ]
                });
            await using var operation = new WriteValueOperation(new NodeId("test", 1), session.Object);
            await operation.LoadAsync().ConfigureAwait(false);
            await operation.WriteAsync(new DataValue(Variant.From(2))).ConfigureAwait(false);
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Failed));
            Assert.That(operation.Outcome, Does.Contain("ArrayDimensions"));
            Assert.That(operation.WasDispatched, Is.False);
        }

        [Test]
        public async Task ParentCancellationAfterMetadataPreventsDispatch()
        {
            using var cancellation = new CancellationTokenSource();
            Mock<ISession> session = CreateSession();
            await using var operation = new WriteValueOperation(
                new NodeId("test", 1), session.Object, cancellation.Token);
            await operation.LoadAsync().ConfigureAwait(false);
            Assert.That(operation.State, Is.EqualTo(WriteValueState.Editing));
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(operation.CancellationToken.IsCancellationRequested, Is.True);
            await operation.WriteAsync(new DataValue(Variant.From(2))).ConfigureAwait(false);
            Assert.That(operation.WasDispatched, Is.False);
            session.Verify(value => value.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static Mock<ISession> CreateSession()
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.Setup(value => value.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Metadata());
            return session;
        }

        private static ReadResponse Metadata()
        {
            return new ReadResponse
            {
                Results =
                [
                    new DataValue(Variant.From(1)),
                    new DataValue(Variant.From(DataTypeIds.Int32)),
                    new DataValue(Variant.From(ValueRanks.Scalar)),
                    new DataValue(Variant.From(ArrayOf<uint>.Empty))
                ]
            };
        }
    }
}
