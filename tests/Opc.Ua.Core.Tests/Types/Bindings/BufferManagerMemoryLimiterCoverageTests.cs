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
 *
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

#nullable enable

using System;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    [TestFixture]
    [Category("BufferManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class BufferManagerMemoryLimiterCoverageTests
    {
        [Test]
        public void ReserveWithNegativeLengthThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);

            Assert.That(
                () => limiter.Reserve(-1, CancellationToken.None),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("expectedBufferLength"));
        }

        [Test]
        public void ReserveAfterDisposeThrows()
        {
            var limiter = new BufferManagerMemoryLimiter(8);
            limiter.Dispose();

            Assert.That(
                () => limiter.Reserve(0, CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void ReserveWhenCapacityUnavailableAndTokenCanceledThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                () => limiter.Reserve(1, cts.Token),
                Throws.InstanceOf<OperationCanceledException>());

            limiter.Cancel(reservationId);
        }

        [Test]
        public void GetReservationWithUnknownIdentifierThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);

            Assert.That(
                () => limiter.GetReservation(42),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TryBindWhenReservationAlreadyActiveThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            byte[] buffer = new byte[8];
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);

            Assert.That(
                () => limiter.TryBind(reservationId, buffer),
                Throws.TypeOf<InvalidOperationException>());

            limiter.CompleteReturn(limiter.BeginReturn(buffer));
        }

        [Test]
        public void CancelWhenReservationAlreadyActiveThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            byte[] buffer = new byte[8];
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);

            Assert.That(
                () => limiter.Cancel(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.CompleteReturn(limiter.BeginReturn(buffer));
        }

        [Test]
        public void BeginReturnWhenReservationIsNotActiveThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            byte[] buffer = new byte[8];
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);
            BufferManagerMemoryLimiter.Reservation reservation =
                limiter.GetReservation(reservationId);
            reservation.State = BufferManagerMemoryLimiter.ReservationState.Returning;

            Assert.That(
                () => limiter.BeginReturn(buffer),
                Throws.TypeOf<InvalidOperationException>());

            reservation.State = BufferManagerMemoryLimiter.ReservationState.Active;
            limiter.CompleteReturn(limiter.BeginReturn(buffer));
        }

        [Test]
        public void CancelReturnWhenReservationIsNotReturningThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);

            Assert.That(
                () => limiter.CancelReturn(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.Cancel(reservationId);
        }

        [Test]
        public void CancelReturnWhenReturningReservationHasNoBufferThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            byte[] buffer = new byte[8];
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);
            limiter.BeginReturn(buffer);
            BufferManagerMemoryLimiter.Reservation reservation =
                limiter.GetReservation(reservationId);
            reservation.Buffer = null;

            Assert.That(
                () => limiter.CancelReturn(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            reservation.Buffer = buffer;
            reservation.State = BufferManagerMemoryLimiter.ReservationState.Returning;
            limiter.CompleteReturn(reservationId);
        }

        [Test]
        public void CancelReturnWhenBufferOwnershipCannotBeRestoredThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(16);
            byte[] buffer = new byte[8];
            long firstReservationId = limiter.Reserve(8, CancellationToken.None);
            Assert.That(limiter.TryBind(firstReservationId, buffer), Is.True);
            Assert.That(limiter.BeginReturn(buffer), Is.EqualTo(firstReservationId));

            long secondReservationId = limiter.Reserve(8, CancellationToken.None);
            Assert.That(limiter.TryBind(secondReservationId, buffer), Is.True);

            Assert.That(
                () => limiter.CancelReturn(firstReservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.CompleteReturn(limiter.BeginReturn(buffer));
            BufferManagerMemoryLimiter.Reservation firstReservation =
                limiter.GetReservation(firstReservationId);
            firstReservation.State = BufferManagerMemoryLimiter.ReservationState.Returning;
            limiter.CompleteReturn(firstReservationId);
        }

        [Test]
        public void CompleteReturnWhenReservationIsNotReturningThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);

            Assert.That(
                () => limiter.CompleteReturn(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.Cancel(reservationId);
        }

        [Test]
        public void CompleteReturnWhenReturningReservationHasNoBufferThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            byte[] buffer = new byte[8];
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);
            limiter.BeginReturn(buffer);
            BufferManagerMemoryLimiter.Reservation reservation =
                limiter.GetReservation(reservationId);
            reservation.Buffer = null;

            Assert.That(
                () => limiter.CompleteReturn(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            reservation.Buffer = buffer;
            limiter.CompleteReturn(reservationId);
        }

        [Test]
        public void NestedReturnScopesRejectRentUntilFinalExit()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            int activeScopes = 0;

            try
            {
                limiter.EnterReturn();
                activeScopes++;
                limiter.EnterReturn();
                activeScopes++;

                Assert.That(
                    () => limiter.Reserve(1, CancellationToken.None),
                    Throws.TypeOf<InvalidOperationException>());

                limiter.ExitReturn();
                activeScopes--;

                Assert.That(
                    () => limiter.Reserve(1, CancellationToken.None),
                    Throws.TypeOf<InvalidOperationException>());

                limiter.ExitReturn();
                activeScopes--;
            }
            finally
            {
                while (activeScopes > 0)
                {
                    limiter.ExitReturn();
                    activeScopes--;
                }
            }

            limiter.Cancel(reservationId);
            long nextReservationId = limiter.Reserve(8, CancellationToken.None);
            limiter.Cancel(nextReservationId);
        }

        [Test]
        public void ExitReturnWithoutScopeThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);

            Assert.That(
                limiter.ExitReturn,
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TryBindOversizedWithAccountingUnderflowThrowsAndRecovers()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            long outstandingBytes = limiter.SetOutstandingBytesForTesting(0);

            Assert.That(
                () => limiter.TryBind(reservationId, new byte[9]),
                Throws.TypeOf<InvalidOperationException>());

            limiter.SetOutstandingBytesForTesting(outstandingBytes);
            limiter.Cancel(reservationId);
            AssertFullBudgetCanBeReserved(limiter);
        }

        [Test]
        public void TryBindSmallerWithAccountingUnderflowThrowsAndRecovers()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            byte[] buffer = new byte[4];
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            limiter.SetOutstandingBytesForTesting(0);

            Assert.That(
                () => limiter.TryBind(reservationId, buffer),
                Throws.TypeOf<InvalidOperationException>());

            limiter.SetOutstandingBytesForTesting(buffer.Length);
            limiter.CompleteReturn(limiter.BeginReturn(buffer));
            AssertFullBudgetCanBeReserved(limiter);
        }

        [Test]
        public void CancelWithAccountingUnderflowThrowsAndRecovers()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            long outstandingBytes = limiter.SetOutstandingBytesForTesting(0);

            Assert.That(
                () => limiter.Cancel(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.SetOutstandingBytesForTesting(outstandingBytes);
            limiter.Cancel(reservationId);
            AssertFullBudgetCanBeReserved(limiter);
        }

        [Test]
        public void CompleteReturnWithAccountingUnderflowThrowsAndRecovers()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            byte[] buffer = new byte[8];
            long reservationId = limiter.Reserve(8, CancellationToken.None);
            Assert.That(limiter.TryBind(reservationId, buffer), Is.True);
            limiter.BeginReturn(buffer);
            long outstandingBytes = limiter.SetOutstandingBytesForTesting(0);

            Assert.That(
                () => limiter.CompleteReturn(reservationId),
                Throws.TypeOf<InvalidOperationException>());

            limiter.SetOutstandingBytesForTesting(outstandingBytes);
            limiter.CompleteReturn(reservationId);
            AssertFullBudgetCanBeReserved(limiter);
        }

        [Test]
        public void LimitingBufferManagerConstructorWithNullDependencyThrows()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            var inner = new RecordingBufferManager(8, 8);

            Assert.That(
                () => new LimitingBufferManager(null!, limiter),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("innerBufferManager"));
            Assert.That(
                () => new LimitingBufferManager(inner, null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("memoryLimiter"));
        }

        [Test]
        public void LimitingBufferManagerForwardsOperations()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            var inner = new RecordingBufferManager(8, 8);
            var manager = new LimitingBufferManager(inner, limiter);
            byte[] marker = new byte[1];

            Assert.That(manager.Name, Is.EqualTo(nameof(RecordingBufferManager)));
            Assert.That(manager.MaxSuggestedBufferSize, Is.EqualTo(8));
            Assert.That(manager.GetSuggestedBufferSize(3), Is.EqualTo(4));
            Assert.That(manager.GetExpectedBufferSize(3), Is.EqualTo(8));

            manager.TransferBuffer(null, "transfer");
            manager.Lock(marker);
            manager.Unlock(marker);
            byte[] buffer = manager.TakeBuffer(3, "take");
            manager.ReturnBuffer(null, "ignored");
            manager.ReturnBuffer(buffer, "return");

            Assert.That(inner.LastTransferredBuffer, Is.Null);
            Assert.That(inner.LastTransferOwner, Is.EqualTo("transfer"));
            Assert.That(inner.LastLockedBuffer, Is.SameAs(marker));
            Assert.That(inner.LastUnlockedBuffer, Is.SameAs(marker));
            Assert.That(inner.TakeBufferCount, Is.EqualTo(1));
            Assert.That(inner.LastTakeOwner, Is.EqualTo("take"));
            Assert.That(inner.LastCancellationToken, Is.EqualTo(CancellationToken.None));
            Assert.That(inner.ReturnBufferCount, Is.EqualTo(1));
            Assert.That(inner.LastReturnOwner, Is.EqualTo("return"));
        }

        [Test]
        public void LimitingBufferManagerFailedReturnRestoresOwnership()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            var inner = new RecordingBufferManager(8, 8);
            var manager = new LimitingBufferManager(inner, limiter);
            byte[] buffer = manager.TakeBuffer(8, "take");
            inner.ReturnException = new InvalidOperationException("return failed");

            Assert.That(
                () => manager.ReturnBuffer(buffer, "first return"),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("return failed"));

            manager.ReturnBuffer(buffer, "retry return");
            byte[] next = manager.TakeBuffer(8, "next take");
            manager.ReturnBuffer(next, "next return");

            Assert.That(inner.ReturnBufferCount, Is.EqualTo(3));
            Assert.That(inner.LastReturnOwner, Is.EqualTo("next return"));
        }

        private static void AssertFullBudgetCanBeReserved(BufferManagerMemoryLimiter limiter)
        {
            long reservationId = limiter.Reserve(
                checked((int)limiter.MaxOutstandingBytes),
                CancellationToken.None);
            limiter.Cancel(reservationId);
        }

        private sealed class RecordingBufferManager : IBufferManager
        {
            public RecordingBufferManager(int expectedBufferSize, int actualBufferSize)
            {
                MaxSuggestedBufferSize = expectedBufferSize;
                m_actualBufferSize = actualBufferSize;
            }

            public string Name => nameof(RecordingBufferManager);

            public int MaxSuggestedBufferSize { get; }

            public byte[]? LastTransferredBuffer { get; private set; }

            public string? LastTransferOwner { get; private set; }

            public byte[]? LastLockedBuffer { get; private set; }

            public byte[]? LastUnlockedBuffer { get; private set; }

            public int TakeBufferCount { get; private set; }

            public string? LastTakeOwner { get; private set; }

            public CancellationToken LastCancellationToken { get; private set; }

            public int ReturnBufferCount { get; private set; }

            public string? LastReturnOwner { get; private set; }

            public Exception? ReturnException { get; set; }

            public int GetSuggestedBufferSize(int size)
            {
                return size + 1;
            }

            public int GetExpectedBufferSize(int size)
            {
                return MaxSuggestedBufferSize;
            }

            public byte[] TakeBuffer(int size, string owner)
            {
                return TakeBuffer(size, owner, CancellationToken.None);
            }

            public byte[] TakeBuffer(int size, string owner, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                TakeBufferCount++;
                LastTakeOwner = owner;
                LastCancellationToken = ct;
                return new byte[m_actualBufferSize];
            }

            public void TransferBuffer(byte[]? buffer, string owner)
            {
                LastTransferredBuffer = buffer;
                LastTransferOwner = owner;
            }

            public void Lock(byte[] buffer)
            {
                LastLockedBuffer = buffer;
            }

            public void Unlock(byte[] buffer)
            {
                LastUnlockedBuffer = buffer;
            }

            public void ReturnBuffer(byte[]? buffer, string owner)
            {
                ReturnBufferCount++;
                LastReturnOwner = owner;

                if (ReturnException != null)
                {
                    Exception exception = ReturnException;
                    ReturnException = null;
                    throw exception;
                }
            }

            private readonly int m_actualBufferSize;
        }
    }
}
