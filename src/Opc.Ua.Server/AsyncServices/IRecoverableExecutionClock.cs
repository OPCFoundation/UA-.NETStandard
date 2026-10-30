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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Supplies qualified elapsed-time evidence across the restarts covered by Durable recovery.
    /// </summary>
    /// <remarks>
    /// The application supplies this provider. An unchanged domain must preserve a monotonic
    /// elapsed-time basis across process, Server and host interruption, including suspension and downtime.
    /// A reset process clock or an unchecked UTC clock does not qualify. When the provider cannot
    /// establish this evidence it must throw, not return an invented or reset timestamp.
    /// No default implementation or implicit host-restart qualification is supplied.
    /// </remarks>
    public interface IRecoverableExecutionClock
    {
        /// <summary>
        /// Reads the current verified time. The caller records commit origins only after the corresponding
        /// durable commit returns, and persists the deadline before dispatch or outcome exposure.
        /// </summary>
        ValueTask<RecoverableExecutionTime> GetTimeAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A persistent elapsed-time coordinate, not a UTC timestamp or a Client clock value.
    /// </summary>
    public readonly record struct RecoverableExecutionTime
    {
        /// <summary>
        /// Creates a coordinate with a nonzero time-domain identity and nonnegative 100-nanosecond ticks.
        /// </summary>
        public RecoverableExecutionTime(Uuid domainId, long ticks)
        {
            if (domainId == Uuid.Empty)
            {
                throw new ArgumentException("A qualified time domain is required.", nameof(domainId));
            }
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }
            DomainId = domainId;
            Ticks = ticks;
        }

        /// <summary>
        /// Gets the persistent clock domain. A different domain is not comparable without external proof.
        /// </summary>
        public Uuid DomainId { get; }

        /// <summary>
        /// Gets elapsed-time ticks within the domain; these have no UTC epoch.
        /// </summary>
        public long Ticks { get; }

        /// <summary>
        /// Measures elapsed time without accepting a reset, rollback or a default coordinate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public TimeSpan ElapsedUntil(RecoverableExecutionTime current)
        {
            if (DomainId == Uuid.Empty ||
                current.DomainId != DomainId ||
                current.Ticks < Ticks)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return TimeSpan.FromTicks(current.Ticks - Ticks);
        }
    }

    /// <summary>
    /// Keeps exact wire durations independent of framework-specific TimeSpan floating-point projections.
    /// </summary>
    internal static class RecoverableExecutionDurations
    {
        /// <summary>
        /// Accepts only positive timer-bounded values that roundtrip unchanged through an integral tick count.
        /// </summary>
        public static bool TryFromMilliseconds(double value, out TimeSpan duration)
        {
            duration = default;
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > int.MaxValue)
            {
                return false;
            }
            double ticks = Math.Round(value * TimeSpan.TicksPerMillisecond);
            if (ticks / TimeSpan.TicksPerMillisecond != value)
            {
                return false;
            }
            duration = TimeSpan.FromTicks((long)ticks);
            return true;
        }

        /// <summary>
        /// Uses canonical division rather than the legacy CLR's reciprocal multiplication.
        /// </summary>
        public static double ToMilliseconds(TimeSpan duration)
        {
            return duration.Ticks / (double)TimeSpan.TicksPerMillisecond;
        }

        /// <summary>
        /// Floors nonnegative remaining time in integer ticks without crossing a millisecond boundary by rounding.
        /// </summary>
        public static double RemainingMilliseconds(TimeSpan duration, TimeSpan elapsed)
        {
            return Math.Max(0, (duration - elapsed).Ticks) / TimeSpan.TicksPerMillisecond;
        }
    }
}
