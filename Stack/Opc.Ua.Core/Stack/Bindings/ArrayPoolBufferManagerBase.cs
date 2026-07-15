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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides shared <see cref="ArrayPool{T}"/>-backed buffer manager behavior.
    /// </summary>
    public abstract class ArrayPoolBufferManagerBase : IBufferManager
    {
        /// <summary>
        /// Initializes a manager with a pool chosen from the configured maximum size.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="metadataByteCount">The number of metadata bytes appended to each buffer.</param>
        protected ArrayPoolBufferManagerBase(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            int metadataByteCount)
            : this(
                name,
                maxBufferSize,
                telemetry,
                CreateArrayPool(maxBufferSize, metadataByteCount),
                metadataByteCount,
                usesInjectedArrayPool: false)
        {
        }

        /// <summary>
        /// Initializes a manager with a caller-supplied pool.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="arrayPool">The pool to rent arrays from.</param>
        /// <param name="metadataByteCount">The number of metadata bytes appended to each buffer.</param>
        internal ArrayPoolBufferManagerBase(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool,
            int metadataByteCount)
            : this(name, maxBufferSize, telemetry, arrayPool, metadataByteCount, usesInjectedArrayPool: true)
        {
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public int MaxSuggestedBufferSize { get; }

        /// <summary>
        /// Gets the logger associated with the concrete implementation.
        /// </summary>
        protected ILogger Logger { get; }

        /// <inheritdoc/>
        public int GetSuggestedBufferSize(int size)
        {
            ValidateRequestedSize(size);
            return DetermineSuggestedBufferSize(size, m_metadataByteCount, logger: null);
        }

        /// <inheritdoc/>
        public int GetExpectedBufferSize(int size)
        {
            ValidateRequestedSize(size);

            if (m_usesInjectedArrayPool)
            {
                return checked(size + m_metadataByteCount);
            }

            return RoundUpToPoolBucket(checked(size + m_metadataByteCount));
        }

        /// <inheritdoc/>
        public byte[] TakeBuffer(int size, string owner)
        {
            return TakeBuffer(size, owner, default);
        }

        /// <inheritdoc/>
        public byte[] TakeBuffer(
            int size,
            string owner,
            System.Threading.CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateRequestedSize(size);

            byte[] buffer = m_arrayPool.Rent(checked(size + m_metadataByteCount));

            try
            {
                OnBufferTaken(buffer, owner);
                return buffer;
            }
            catch
            {
                m_arrayPool.Return(buffer);
                throw;
            }
        }

        /// <inheritdoc/>
        public void TransferBuffer(byte[]? buffer, string owner)
        {
            if (buffer == null)
            {
                return;
            }

            OnBufferTransferred(buffer, owner);
        }

        /// <inheritdoc/>
        public virtual void Lock(byte[] buffer)
        {
        }

        /// <inheritdoc/>
        public virtual void Unlock(byte[] buffer)
        {
        }

        /// <inheritdoc/>
        public void ReturnBuffer(byte[]? buffer, string owner)
        {
            if (buffer == null)
            {
                return;
            }

            OnBufferReturning(buffer, owner);
            m_arrayPool.Return(buffer);
        }

        /// <summary>
        /// Allows derived types to annotate or validate a freshly rented buffer.
        /// </summary>
        /// <param name="buffer">The rented buffer.</param>
        /// <param name="owner">The logical buffer owner.</param>
        protected virtual void OnBufferTaken(byte[] buffer, string owner)
        {
        }

        /// <summary>
        /// Allows derived types to react when buffer ownership changes.
        /// </summary>
        /// <param name="buffer">The buffer whose owner changed.</param>
        /// <param name="owner">The new owner.</param>
        protected virtual void OnBufferTransferred(byte[] buffer, string owner)
        {
        }

        /// <summary>
        /// Allows derived types to validate or clean up a buffer before it is returned.
        /// </summary>
        /// <param name="buffer">The buffer being returned.</param>
        /// <param name="owner">The logical owner returning the buffer.</param>
        protected virtual void OnBufferReturning(byte[] buffer, string owner)
        {
        }

        /// <summary>
        /// Chooses the array pool implementation for a manager.
        /// </summary>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="metadataByteCount">The number of metadata bytes appended to each buffer.</param>
        /// <returns>The selected array pool.</returns>
        internal static ArrayPool<byte> CreateArrayPool(int maxBufferSize, int metadataByteCount)
        {
            if (maxBufferSize <= kSharedPoolThreshold - metadataByteCount)
            {
                return ArrayPool<byte>.Shared;
            }

            int maximumArrayLength = RoundUpToPoolBucket(
                checked(maxBufferSize + metadataByteCount));
            return ArrayPool<byte>.Create(maximumArrayLength, 4);
        }

        /// <summary>
        /// Calculates a suggested payload size that avoids the next pool bucket when possible.
        /// </summary>
        /// <param name="requestedBufferSize">The requested payload size.</param>
        /// <param name="metadataByteCount">The number of metadata bytes appended to each buffer.</param>
        /// <param name="logger">The logger to write warnings to.</param>
        /// <returns>The suggested payload size.</returns>
        internal static int DetermineSuggestedBufferSize(
            int requestedBufferSize,
            int metadataByteCount,
            ILogger? logger)
        {
            int payloadBucketSize = RoundUpToPowerOfTwo(requestedBufferSize);
            int rentBucketSize = RoundUpToPowerOfTwo(checked(requestedBufferSize + metadataByteCount));

            if (payloadBucketSize != rentBucketSize)
            {
                logger?.LogWarning(
                    "BufferManager: Max buffer size {MaxBufferSize} + metadata bytes {MetadataBytes} may waste memory because it allocates buffers in the next bucket!",
                    requestedBufferSize,
                    metadataByteCount);
                return payloadBucketSize - metadataByteCount;
            }

            return requestedBufferSize;
        }

        /// <summary>
        /// Rounds a value up to the next power-of-two bucket.
        /// </summary>
        /// <param name="value">The value to round.</param>
        /// <returns>The rounded value.</returns>
        internal static int RoundUpToPowerOfTwo(int value)
        {
            int result = 1;

            while (result < value && result != 0)
            {
                result <<= 1;
            }

            return result;
        }

        /// <summary>
        /// Rounds a value up to the minimum <see cref="ArrayPool{T}"/> bucket size.
        /// </summary>
        /// <param name="value">The value to round.</param>
        /// <returns>The rounded pool bucket size.</returns>
        internal static int RoundUpToPoolBucket(int value)
        {
            int result = kMinimumPooledArrayLength;

            while (result < value && result != 0)
            {
                result <<= 1;
            }

            return result;
        }

        /// <summary>
        /// Validates a requested payload size.
        /// </summary>
        /// <param name="size">The requested payload size.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="size"/> is outside the configured range.
        /// </exception>
        internal void ValidateRequestedSize(int size)
        {
            if (size < 0 || size > m_maxBufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        /// <summary>
        /// Initializes the base implementation.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="arrayPool">The array pool used for rents and returns.</param>
        /// <param name="metadataByteCount">The number of metadata bytes appended to each buffer.</param>
        /// <param name="usesInjectedArrayPool">
        /// <see langword="true"/> when the pool was supplied by the caller.
        /// </param>
        private ArrayPoolBufferManagerBase(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool,
            int metadataByteCount,
            bool usesInjectedArrayPool)
        {
            if (maxBufferSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBufferSize));
            }

            Name = name ?? string.Empty;
            Logger = telemetry.CreateLogger(GetType().FullName ?? GetType().Name);
            m_arrayPool = arrayPool ?? throw new ArgumentNullException(nameof(arrayPool));
            m_maxBufferSize = maxBufferSize;
            m_metadataByteCount = metadataByteCount;
            m_usesInjectedArrayPool = usesInjectedArrayPool;
            MaxSuggestedBufferSize = DetermineSuggestedBufferSize(maxBufferSize, metadataByteCount, Logger);
        }

        private const int kMinimumPooledArrayLength = 16;
        private const int kSharedPoolThreshold = 1024 * 1024;

        private readonly ArrayPool<byte> m_arrayPool;
        private readonly bool m_usesInjectedArrayPool;
        private readonly int m_maxBufferSize;
        private readonly int m_metadataByteCount;
    }

    /// <summary>
    /// Provides cookie helpers shared by the cookie-based implementations.
    /// </summary>
    internal static class BufferCookie
    {
        /// <summary>
        /// Marks a buffer as unlocked.
        /// </summary>
        /// <param name="buffer">The buffer to initialize.</param>
        internal static void Initialize(byte[] buffer)
        {
            ValidateCookieStorage(buffer);
            buffer[^1] = kCookieUnlocked;
        }

        /// <summary>
        /// Validates that the buffer is unlocked and then marks it as locked.
        /// </summary>
        /// <param name="buffer">The buffer to lock.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the buffer is already locked or does not contain a cookie.
        /// </exception>
        internal static void Lock(byte[] buffer)
        {
            ValidateCookieStorage(buffer);

            if (buffer[^1] != kCookieUnlocked)
            {
                throw new InvalidOperationException("Buffer is already locked.");
            }

            buffer[^1] = kCookieLocked;
        }

        /// <summary>
        /// Validates that the buffer is locked and then marks it as unlocked.
        /// </summary>
        /// <param name="buffer">The buffer to unlock.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the buffer is not locked or does not contain a cookie.
        /// </exception>
        internal static void Unlock(byte[] buffer)
        {
            ValidateCookieStorage(buffer);

            if (buffer[^1] != kCookieLocked)
            {
                throw new InvalidOperationException("Buffer is not locked.");
            }

            buffer[^1] = kCookieUnlocked;
        }

        /// <summary>
        /// Validates that the buffer is unlocked and destroys the cookie.
        /// </summary>
        /// <param name="buffer">The buffer being returned.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the buffer is locked or does not contain a cookie.
        /// </exception>
        internal static void ValidateAndDestroy(byte[] buffer)
        {
            ValidateCookieStorage(buffer);

            if (buffer[^1] != kCookieUnlocked)
            {
                throw new InvalidOperationException("Buffer has been locked.");
            }

            buffer[^1] = kCookieUnlocked ^ kCookieLocked;
        }

        /// <summary>
        /// Ensures the buffer is large enough to hold a cookie.
        /// </summary>
        /// <param name="buffer">The buffer to validate.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the buffer does not contain a cookie byte.
        /// </exception>
        internal static void ValidateCookieStorage(byte[] buffer)
        {
            if (buffer.Length == 0)
            {
                throw new InvalidOperationException("Buffer does not contain a cookie.");
            }
        }

        private const byte kCookieLocked = 0xA5;
        private const byte kCookieUnlocked = 0x5A;
    }
}
