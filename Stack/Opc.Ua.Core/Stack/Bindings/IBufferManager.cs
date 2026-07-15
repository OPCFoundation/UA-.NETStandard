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

namespace Opc.Ua
{
    /// <summary>
    /// Defines buffer rental, ownership transfer, debug locking and return semantics.
    /// </summary>
    public interface IBufferManager
    {
        /// <summary>
        /// Gets the logical name used for diagnostics.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the largest suggested payload size that avoids unnecessary pool bucket growth.
        /// </summary>
        int MaxSuggestedBufferSize { get; }

        /// <summary>
        /// Returns a suggested payload size for the requested amount of data.
        /// </summary>
        /// <param name="size">The requested payload size.</param>
        /// <returns>
        /// The suggested payload size to request from <see cref="TakeBuffer(int, string)"/>.
        /// </returns>
        int GetSuggestedBufferSize(int size);

        /// <summary>
        /// Returns a conservative upper bound for the array length returned by the next rent.
        /// </summary>
        /// <param name="size">The requested payload size.</param>
        /// <returns>The expected array length in bytes.</returns>
        int GetExpectedBufferSize(int size);

        /// <summary>
        /// Rents a buffer that can hold at least the requested payload size.
        /// </summary>
        /// <param name="size">The requested payload size.</param>
        /// <param name="owner">The logical owner requesting the buffer.</param>
        /// <returns>The rented buffer.</returns>
        byte[] TakeBuffer(int size, string owner);

        /// <summary>
        /// Rents a buffer while observing cancellation during any capacity wait.
        /// </summary>
        /// <param name="size">The requested payload size.</param>
        /// <param name="owner">The logical owner requesting the buffer.</param>
        /// <param name="ct">Cancellation token for a blocking rent.</param>
        /// <returns>The rented buffer.</returns>
        byte[] TakeBuffer(int size, string owner, System.Threading.CancellationToken ct);

        /// <summary>
        /// Updates the logical owner associated with a rented buffer.
        /// </summary>
        /// <param name="buffer">The buffer whose owner changes.</param>
        /// <param name="owner">The new logical owner.</param>
        void TransferBuffer(byte[]? buffer, string owner);

        /// <summary>
        /// Marks a buffer as locked for debug validation.
        /// </summary>
        /// <param name="buffer">The buffer to lock.</param>
        void Lock(byte[] buffer);

        /// <summary>
        /// Marks a buffer as unlocked for debug validation.
        /// </summary>
        /// <param name="buffer">The buffer to unlock.</param>
        void Unlock(byte[] buffer);

        /// <summary>
        /// Returns a buffer to the manager.
        /// </summary>
        /// <param name="buffer">The buffer to return.</param>
        /// <param name="owner">The logical owner returning the buffer.</param>
        void ReturnBuffer(byte[]? buffer, string owner);
    }
}
