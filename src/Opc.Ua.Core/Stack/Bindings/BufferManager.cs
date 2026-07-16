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

//#define TRACE_MEMORY
//#define TRACK_MEMORY

using System;
using System.Buffers;
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A collection of buffers.
    /// </summary>
    public class BufferCollection : List<ArraySegment<byte>>
    {
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public BufferCollection()
        {
        }

        /// <summary>
        /// Creates an empty collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public BufferCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Creates a collection with a single element.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public BufferCollection(ArraySegment<byte> segment)
        {
            Add(segment);
        }

        /// <summary>
        /// Creates a collection with a single element.
        /// </summary>
        /// <param name="array">The array.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="count">The count.</param>
        public BufferCollection(byte[] array, int offset, int count)
        {
            Add(new ArraySegment<byte>(array, offset, count));
        }

        /// <summary>
        /// Returns the buffers to the manager before clearing the collection.
        /// </summary>
        /// <param name="bufferManager">The buffer manager.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>Length of all buffers in this collection</returns>
        public int Release(BufferManager bufferManager, string owner)
        {
            int count = 0;

            foreach (ArraySegment<byte> buffer in this)
            {
                count += buffer.Count;
                bufferManager.ReturnBuffer(buffer.GetArray(), owner);
            }

            Clear();
            return count;
        }

        /// <summary>
        /// Returns the total amount of data in the buffers.
        /// </summary>
        /// <value>The total size.</value>
        public int TotalSize
        {
            get
            {
                int count = 0;

                for (int ii = 0; ii < Count; ii++)
                {
                    count += this[ii].Count;
                }

                return count;
            }
        }
    }

    /// <summary>
    /// A compatibility facade for the stack buffer manager implementation.
    /// </summary>
    public class BufferManager : IBufferManager
    {
        /// <summary>
        /// Constructs the buffer manager.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="maxBufferSize">Max size of the buffer.</param>
        /// <param name="telemetry">The telemetry context used to create observability instruments.</param>
        public BufferManager(string name, int maxBufferSize, ITelemetryContext telemetry)
            : this(CreateDefaultBufferManager(name, maxBufferSize, telemetry))
        {
        }

        /// <summary>
        /// Constructs the buffer manager with an explicit implementation.
        /// </summary>
        /// <param name="bufferManager">The implementation to wrap.</param>
        public BufferManager(IBufferManager bufferManager)
        {
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        }

        /// <summary>
        /// Constructs the buffer manager with the specified array pool.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="maxBufferSize">Max size of the buffer.</param>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        /// <param name="arrayPool">The array pool to use.</param>
        internal BufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte> arrayPool)
            : this(CreateDefaultBufferManager(name, maxBufferSize, telemetry, arrayPool))
        {
        }

        /// <summary>
        /// Returns a buffer with at least the specified size.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>The buffer content</returns>
        public byte[] TakeBuffer(int size, string owner)
        {
            return m_bufferManager.TakeBuffer(size, owner);
        }

        /// <inheritdoc/>
        public byte[] TakeBuffer(
            int size,
            string owner,
            System.Threading.CancellationToken ct)
        {
            return m_bufferManager.TakeBuffer(size, owner, ct);
        }

        /// <summary>
        /// Changes the owner of a buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="owner">The owner.</param>
        public void TransferBuffer(byte[]? buffer, string owner)
        {
            m_bufferManager.TransferBuffer(buffer, owner);
        }

        /// <inheritdoc/>
        public void Lock(byte[] buffer)
        {
            m_bufferManager.Lock(buffer);
        }

        /// <inheritdoc/>
        public void Unlock(byte[] buffer)
        {
            m_bufferManager.Unlock(buffer);
        }

        /// <summary>
        /// Locks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void LockBuffer(byte[] buffer)
        {
#if TRACK_MEMORY || DEBUG
            BufferCookie.Lock(buffer);
#endif
        }

        /// <summary>
        /// Unlocks the buffer (used for debugging only).
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        public static void UnlockBuffer(byte[] buffer)
        {
#if TRACK_MEMORY || DEBUG
            BufferCookie.Unlock(buffer);
#endif
        }

        /// <summary>
        /// Release the buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="owner">The owner.</param>
        public void ReturnBuffer(byte[]? buffer, string owner)
        {
            m_bufferManager.ReturnBuffer(buffer, owner);
        }

        /// <summary>
        /// Returns the suggested rent size for the requested amount of data.
        /// </summary>
        /// <param name="size">The requested amount of data.</param>
        /// <returns>The suggested rent size.</returns>
        public int GetSuggestedBufferSize(int size)
        {
            return m_bufferManager.GetSuggestedBufferSize(size);
        }

        /// <summary>
        /// Returns the conservative expected array length for the requested amount of data.
        /// </summary>
        /// <param name="size">The requested amount of data.</param>
        /// <returns>The expected array length.</returns>
        public int GetExpectedBufferSize(int size)
        {
            return m_bufferManager.GetExpectedBufferSize(size);
        }

        /// <summary>
        /// Returns the max size of data in the buffers.
        /// </summary>
        /// <remarks>
        /// Due to the underlying implementation of the ArrayPool,
        /// the actual buffer size may be larger than this value.
        /// To avoid memory waste, use this value as a guideline
        /// for the maximum buffer size when taking buffers.
        /// </remarks>
        public int MaxSuggestedBufferSize => m_bufferManager.MaxSuggestedBufferSize;

        /// <summary>
        /// Gets the diagnostic name.
        /// </summary>
        public string Name => m_bufferManager.Name;

        /// <summary>
        /// Returns the build-specific default implementation kind.
        /// </summary>
        /// <returns>The default implementation kind.</returns>
        internal static BufferManagerImplementationKind GetDefaultImplementationKind()
        {
#if TRACK_MEMORY
            return BufferManagerImplementationKind.MemoryTracing;
#elif DEBUG
            return BufferManagerImplementationKind.Cookie;
#else
            return BufferManagerImplementationKind.Fast;
#endif
        }

        private static IBufferManager CreateDefaultBufferManager(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            ArrayPool<byte>? arrayPool = null)
        {
            return CreateImplementation(
                name,
                maxBufferSize,
                telemetry,
                GetDefaultImplementationKind(),
                arrayPool);
        }

        /// <summary>
        /// Creates a specific compatibility facade implementation.
        /// </summary>
        /// <param name="name">The diagnostic name.</param>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="telemetry">The telemetry context used for diagnostics.</param>
        /// <param name="implementationKind">The implementation to create.</param>
        /// <param name="arrayPool">The optional array pool to use.</param>
        /// <returns>The created buffer manager.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal static IBufferManager CreateImplementation(
            string name,
            int maxBufferSize,
            ITelemetryContext telemetry,
            BufferManagerImplementationKind implementationKind,
            ArrayPool<byte>? arrayPool = null)
        {
            return implementationKind switch
            {
                BufferManagerImplementationKind.Fast => arrayPool == null
                    ? new FastBufferManager(name, maxBufferSize, telemetry)
                    : new FastBufferManager(name, maxBufferSize, telemetry, arrayPool),
                BufferManagerImplementationKind.Cookie => arrayPool == null
                    ? new CookieBufferManager(name, maxBufferSize, telemetry)
                    : new CookieBufferManager(name, maxBufferSize, telemetry, arrayPool),
                BufferManagerImplementationKind.MemoryTracing => arrayPool == null
                    ? new TracingBufferManager(name, maxBufferSize, telemetry)
                    : new TracingBufferManager(name, maxBufferSize, telemetry, arrayPool),
                _ => throw new InvalidOperationException(
                    "The buffer manager implementation kind is invalid.")
            };
        }

        private readonly IBufferManager m_bufferManager;
    }
}
