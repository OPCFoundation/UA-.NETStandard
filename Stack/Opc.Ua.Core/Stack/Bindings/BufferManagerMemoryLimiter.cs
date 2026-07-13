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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Tracks a shared outstanding-byte budget across one or more buffer managers.
    /// </summary>
    public sealed class BufferManagerMemoryLimiter : IDisposable
    {
        /// <summary>
        /// Initializes the limiter.
        /// </summary>
        /// <param name="maxOutstandingBytes">The maximum outstanding byte budget.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="maxOutstandingBytes"/> is not positive.
        /// </exception>
        public BufferManagerMemoryLimiter(long maxOutstandingBytes)
        {
            if (maxOutstandingBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingBytes));
            }

            MaxOutstandingBytes = maxOutstandingBytes;
        }

        /// <summary>
        /// Gets the shared outstanding byte budget.
        /// </summary>
        public long MaxOutstandingBytes { get; }

        /// <summary>
        /// Disposes owned resources.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_disposed = true;
                m_capacityChanged.Set();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Reserves budget for a predicted buffer length, blocking until capacity is available.
        /// </summary>
        /// <param name="expectedBufferLength">The conservative expected buffer length.</param>
        /// <param name="ct">Cancellation token for the capacity wait.</param>
        /// <returns>A reservation identifier.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a single reservation exceeds the configured budget.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        internal long Reserve(
            int expectedBufferLength,
            CancellationToken ct)
        {
            if (expectedBufferLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedBufferLength));
            }

            if (expectedBufferLength > MaxOutstandingBytes)
            {
                throw new InvalidOperationException(
                    "The expected buffer length exceeds the configured process budget.");
            }

            while (true)
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        throw new ObjectDisposedException(nameof(BufferManagerMemoryLimiter));
                    }
                    if (m_outstandingBytes + expectedBufferLength <= MaxOutstandingBytes)
                    {
                        long reservationId = ++m_nextReservationId;
                        m_reservations[reservationId] = new Reservation(expectedBufferLength);
                        m_outstandingBytes += expectedBufferLength;
                        return reservationId;
                    }

                    if (IsReturningOnCurrentThread())
                    {
                        throw new InvalidOperationException(
                            "A buffer rent cannot synchronously re-enter the same limiter during a return.");
                    }

                    m_capacityChanged.Reset();
                }

                m_capacityChanged.Wait(ct);
            }
        }

        /// <summary>
        /// Binds the actual buffer length to a reservation.
        /// </summary>
        /// <param name="reservationId">The reservation identifier.</param>
        /// <param name="buffer">The rented buffer.</param>
        /// <returns>
        /// <see langword="true"/> when the actual length fits inside the reservation;
        /// otherwise <see langword="false"/> and the reservation is released.
        /// </returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal bool TryBind(long reservationId, byte[] buffer)
        {
            bool signalCapacityChanged = false;
            bool reservationFits;

            lock (m_lock)
            {
                Reservation reservation = GetReservation(reservationId);

                if (reservation.State != ReservationState.Reserved)
                {
                    throw new InvalidOperationException("The reservation is not awaiting a buffer.");
                }

                if (buffer.Length > reservation.ReservedBytes)
                {
                    if (m_outstandingBytes < reservation.ReservedBytes)
                    {
                        throw new InvalidOperationException("The outstanding byte budget would underflow.");
                    }

                    m_reservations.Remove(reservationId);
                    m_outstandingBytes -= reservation.ReservedBytes;
                    signalCapacityChanged = true;
                    reservationFits = false;
                }
                else
                {
                    reservation.Buffer = buffer;
                    reservation.ActualBytes = buffer.Length;
                    reservation.State = ReservationState.Active;
                    m_buffers[buffer] = reservationId;

                    int returnedCapacity = reservation.ReservedBytes - buffer.Length;

                    if (returnedCapacity > 0)
                    {
                        if (m_outstandingBytes < returnedCapacity)
                        {
                            throw new InvalidOperationException(
                                "The outstanding byte budget would underflow.");
                        }

                        m_outstandingBytes -= returnedCapacity;
                        signalCapacityChanged = true;
                    }

                    reservationFits = true;
                }
            }

            if (signalCapacityChanged)
            {
                m_capacityChanged.Set();
            }

            return reservationFits;
        }

        /// <summary>
        /// Releases a reservation after an inner rent failure.
        /// </summary>
        /// <param name="reservationId">The reservation identifier.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void Cancel(long reservationId)
        {
            bool signalCapacityChanged;
            lock (m_lock)
            {
                Reservation reservation = GetReservation(reservationId);

                if (reservation.State != ReservationState.Reserved)
                {
                    throw new InvalidOperationException("Only pending reservations can be canceled.");
                }

                if (m_outstandingBytes < reservation.ReservedBytes)
                {
                    throw new InvalidOperationException("The outstanding byte budget would underflow.");
                }

                m_reservations.Remove(reservationId);
                m_outstandingBytes -= reservation.ReservedBytes;
                signalCapacityChanged = true;
            }

            if (signalCapacityChanged)
            {
                m_capacityChanged.Set();
            }
        }

        /// <summary>
        /// Marks a tracked buffer as being returned.
        /// </summary>
        /// <param name="buffer">The buffer being returned.</param>
        /// <returns>The reservation identifier associated with the buffer.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal long BeginReturn(byte[] buffer)
        {
            lock (m_lock)
            {
                if (!m_buffers.TryGetValue(buffer, out long reservationId))
                {
                    throw new InvalidOperationException(
                        "The supplied buffer is not owned by this limiting buffer manager.");
                }

                Reservation reservation = GetReservation(reservationId);

                if (reservation.State != ReservationState.Active)
                {
                    throw new InvalidOperationException("The supplied buffer is already being returned.");
                }

                reservation.State = ReservationState.Returning;
                m_buffers.Remove(buffer);
                return reservationId;
            }
        }

        /// <summary>
        /// Restores a reservation after an inner return failure.
        /// </summary>
        /// <param name="reservationId">The reservation identifier.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void CancelReturn(long reservationId)
        {
            lock (m_lock)
            {
                Reservation reservation = GetReservation(reservationId);

                if (reservation.State != ReservationState.Returning)
                {
                    throw new InvalidOperationException("Only in-flight returns can be canceled.");
                }

                reservation.State = ReservationState.Active;
                if (reservation.Buffer == null ||
                    !m_buffers.TryAdd(reservation.Buffer, reservationId))
                {
                    throw new InvalidOperationException(
                        "The returned buffer ownership could not be restored.");
                }
            }
        }

        /// <summary>
        /// Releases the reservation after a successful inner return.
        /// </summary>
        /// <param name="reservationId">The reservation identifier.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void CompleteReturn(long reservationId)
        {
            bool signalCapacityChanged;
            lock (m_lock)
            {
                Reservation reservation = GetReservation(reservationId);

                if (reservation.State != ReservationState.Returning || reservation.Buffer == null)
                {
                    throw new InvalidOperationException("The reservation is not awaiting completion.");
                }

                if (m_outstandingBytes < reservation.ActualBytes)
                {
                    throw new InvalidOperationException(
                        "The outstanding byte budget would underflow.");
                }
                m_reservations.Remove(reservationId);
                m_outstandingBytes -= reservation.ActualBytes;
                signalCapacityChanged = true;
            }

            if (signalCapacityChanged)
            {
                m_capacityChanged.Set();
            }
        }

        /// <summary>
        /// Marks the current thread as executing an inner return.
        /// </summary>
        internal void EnterReturn()
        {
            s_returningLimiters ??= [];
            s_returningLimiters.TryGetValue(this, out int count);
            s_returningLimiters[this] = count + 1;
        }

        /// <summary>
        /// Clears an inner-return marker for the current thread.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal void ExitReturn()
        {
            if (s_returningLimiters == null ||
                !s_returningLimiters.TryGetValue(this, out int count))
            {
                throw new InvalidOperationException("No limiter return scope is active.");
            }

            if (count == 1)
            {
                s_returningLimiters.Remove(this);
            }
            else
            {
                s_returningLimiters[this] = count - 1;
            }
        }

        private bool IsReturningOnCurrentThread()
        {
            return s_returningLimiters?.ContainsKey(this) == true;
        }

        /// <summary>
        /// Returns the reservation associated with an identifier.
        /// </summary>
        /// <param name="reservationId">The reservation identifier.</param>
        /// <returns>The reservation record.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal Reservation GetReservation(long reservationId)
        {
            if (!m_reservations.TryGetValue(reservationId, out Reservation? reservation))
            {
                throw new InvalidOperationException("The reservation is not active.");
            }

            return reservation;
        }

        /// <summary>
        /// Stores per-buffer accounting data.
        /// </summary>
        internal sealed class Reservation
        {
            /// <summary>
            /// Initializes the reservation.
            /// </summary>
            /// <param name="reservedBytes">The initially reserved byte count.</param>
            internal Reservation(int reservedBytes)
            {
                ReservedBytes = reservedBytes;
                ActualBytes = reservedBytes;
            }

            /// <summary>
            /// Gets the byte count reserved before the inner rent executes.
            /// </summary>
            internal int ReservedBytes { get; }

            /// <summary>
            /// Gets or sets the actual outstanding byte count after the inner rent completes.
            /// </summary>
            internal int ActualBytes { get; set; }

            /// <summary>
            /// Gets or sets the tracked buffer.
            /// </summary>
            internal byte[]? Buffer { get; set; }

            /// <summary>
            /// Gets or sets the current reservation state.
            /// </summary>
            internal ReservationState State { get; set; }
        }

        /// <summary>
        /// Identifies the state of a shared budget reservation.
        /// </summary>
        internal enum ReservationState
        {
            /// <summary>
            /// The reservation has been created but not yet bound to a buffer.
            /// </summary>
            Reserved,

            /// <summary>
            /// The reservation is bound to an outstanding buffer.
            /// </summary>
            Active,

            /// <summary>
            /// The reservation is being completed by a return operation.
            /// </summary>
            Returning
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<byte[], long> m_buffers = [];
        private readonly Dictionary<long, Reservation> m_reservations = [];

        // Waiters can still be unwinding when the DI-owned limiter is disposed.
        // TODO: Replace this shared signal with a disposable waiter registry.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Disposing while blocked renters unwind races with Set/Wait; the signal is reclaimed with the limiter.")]
        private readonly ManualResetEventSlim m_capacityChanged = new(initialState: true);

        private long m_nextReservationId;
        private long m_outstandingBytes;
        private bool m_disposed;

        [ThreadStatic]
        private static Dictionary<BufferManagerMemoryLimiter, int>? s_returningLimiters;
    }
}
