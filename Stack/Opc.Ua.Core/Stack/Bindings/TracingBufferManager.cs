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
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides cookie validation plus allocation and ownership tracing.
    /// </summary>
    public sealed class TracingBufferManager : ArrayPoolBufferManagerBase
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        public TracingBufferManager(string name, int maxBufferSize, ITelemetryContext telemetry)
            : base(name, maxBufferSize, telemetry, kMetadataByteCount)
        {
        }

        /// <summary>
        /// Initializes the manager with a caller-supplied pool.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="arrayPool">The array pool to use.</param>
        internal TracingBufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool)
            : this(name, maxBufferSize, telemetry, arrayPool, GetUtcNow)
        {
        }

        /// <summary>
        /// Initializes the manager with a caller-supplied pool and clock.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="arrayPool">The array pool to use.</param>
        /// <param name="utcNow">Returns the current UTC time.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="utcNow"/> is <see langword="null"/>.
        /// </exception>
        internal TracingBufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool,
            Func<DateTime> utcNow)
            : base(name, maxBufferSize, telemetry, arrayPool, kMetadataByteCount)
        {
            m_utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <inheritdoc/>
        public override void Lock(byte[] buffer)
        {
            BufferCookie.Lock(buffer);
        }

        /// <inheritdoc/>
        public override void Unlock(byte[] buffer)
        {
            BufferCookie.Unlock(buffer);
        }

        /// <inheritdoc/>
        protected override void OnBufferTaken(byte[] buffer, string owner)
        {
            BufferCookie.Initialize(buffer);

            lock (m_lock)
            {
                int allocationId = ++m_nextAllocationId;
                byte[] allocationBytes = BitConverter.GetBytes(allocationId);
                Array.Copy(allocationBytes, 0, buffer, buffer.Length - kMetadataByteCount, allocationBytes.Length);

                m_allocatedBytes += buffer.Length;
                m_allocations[allocationId] = new Allocation
                {
                    Buffer = buffer,
                    Id = allocationId,
                    Owner = owner ?? string.Empty,
                    Timestamp = m_utcNow()
                };
            }

            Logger.LogDebug(
                "{Name}: Rented buffer {BufferHash:X} ({BufferLength} bytes) for {Owner}.",
                Name,
                buffer.GetHashCode(),
                buffer.Length,
                owner);
        }

        /// <inheritdoc/>
        protected override void OnBufferTransferred(byte[] buffer, string owner)
        {
            lock (m_lock)
            {
                int allocationId = ReadAllocationId(buffer);

                Allocation? allocation = FindAllocation(buffer, ref allocationId);
                if (allocation != null)
                {
                    allocation.Owner = Utils.Format("{0}/{1}", allocation.Owner, owner);

                    if (allocation.ReportedAgeSeconds > 0)
                    {
                        Logger.LogDebug(
                            "{Name}: Id={AllocationId}; Owner={Owner}; Size={SizeKb} KB; *** TRANSFERRED ***",
                            Name,
                            allocation.Id,
                            allocation.Owner,
                            allocation.Buffer.Length / 1024);
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnBufferReturning(byte[] buffer, string owner)
        {
            BufferCookie.ValidateAndDestroy(buffer);

            lock (m_lock)
            {
                int allocationId = ReadAllocationId(buffer);
                Allocation? allocation = FindAllocation(buffer, ref allocationId) ?? throw new InvalidOperationException("The returned buffer is not tracked.");

                m_allocatedBytes -= buffer.Length;
                allocation.ReleasedBy = owner ?? string.Empty;

                if (allocation.ReportedAgeSeconds > 0)
                {
                    Logger.LogDebug(
                        "{Name}: Id={AllocationId}; Owner={Owner}; ReleasedBy={ReleasedBy}; Size={SizeKb} KB; *** RETURNED ***",
                        Name,
                        allocation.Id,
                        allocation.Owner,
                        allocation.ReleasedBy,
                        allocation.Buffer.Length / 1024);
                }

                m_allocations.Remove(allocationId);

                Logger.LogDebug(
                    "{Name}: Deallocated ID {AllocationId}: {BufferLength}/{AllocatedBytes}",
                    Name,
                    allocationId,
                    buffer.Length,
                    m_allocatedBytes);

                foreach (KeyValuePair<int, Allocation> current in m_allocations)
                {
                    Allocation trackedAllocation = current.Value;

                    if (trackedAllocation.ReleasedBy != null)
                    {
                        continue;
                    }

                    double ageSeconds = Math.Truncate(
                        (m_utcNow() - trackedAllocation.Timestamp).TotalSeconds);

                    if (ageSeconds > 3 &&
                        Math.Truncate(ageSeconds) % 3 == 0 &&
                        trackedAllocation.ReportedAgeSeconds < ageSeconds)
                    {
                        Logger.LogDebug(
                            "{Name}: Id={AllocationId}; Owner={Owner}; Size={SizeKb} KB; Age={AgeSeconds}",
                            Name,
                            trackedAllocation.Id,
                            trackedAllocation.Owner,
                            trackedAllocation.Buffer.Length / 1024,
                            ageSeconds);
                        trackedAllocation.ReportedAgeSeconds = (int)ageSeconds;
                    }
                }

                byte fillValue = string.Equals(Name, "Server", StringComparison.Ordinal)
                    ? (byte)0xFA
                    : (byte)0xFC;

                for (int ii = 0; ii < buffer.Length - kMetadataByteCount; ii++)
                {
                    buffer[ii] = fillValue;
                }
            }
        }

        /// <summary>
        /// Reads the allocation identifier from a traced buffer.
        /// </summary>
        /// <param name="buffer">The traced buffer.</param>
        /// <returns>The allocation identifier.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <paramref name="buffer"/> does not contain tracing metadata.
        /// </exception>
        internal static int ReadAllocationId(byte[] buffer)
        {
            if (buffer.Length < kMetadataByteCount)
            {
                throw new InvalidOperationException("Buffer does not contain tracing metadata.");
            }

            return BitConverter.ToInt32(buffer, buffer.Length - kMetadataByteCount);
        }

        private Allocation? FindAllocation(byte[] buffer, ref int allocationId)
        {
            if (m_allocations.TryGetValue(allocationId, out Allocation? allocation) &&
                ReferenceEquals(allocation.Buffer, buffer))
            {
                return allocation;
            }

            foreach (KeyValuePair<int, Allocation> current in m_allocations)
            {
                if (ReferenceEquals(current.Value.Buffer, buffer))
                {
                    allocationId = current.Key;
                    return current.Value;
                }
            }

            return null;
        }

        private static DateTime GetUtcNow()
        {
            return DateTime.UtcNow;
        }

        private const int kMetadataByteCount = 5;

        private sealed class Allocation
        {
            public byte[] Buffer { get; init; } = [];
            public int Id { get; init; }
            public string Owner { get; set; } = string.Empty;
            public string? ReleasedBy { get; set; }
            public int ReportedAgeSeconds { get; set; }
            public DateTime Timestamp { get; init; }
        }

        private readonly Lock m_lock = new();
        private readonly SortedDictionary<int, Allocation> m_allocations = [];
        private readonly Func<DateTime> m_utcNow = GetUtcNow;
        private int m_allocatedBytes;
        private int m_nextAllocationId;
    }
}
