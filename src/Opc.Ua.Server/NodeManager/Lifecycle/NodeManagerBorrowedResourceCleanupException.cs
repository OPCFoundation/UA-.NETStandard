/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports unfinished cleanup of a candidate that borrowed a NodeManager resource.
    /// </summary>
    /// <remarks>
    /// An exception returned by the lifecycle retains the cleanup lease. Call
    /// <see cref="RetryAsync"/> to retry the unfinished rollback or retirement.
    /// </remarks>
    public sealed class NodeManagerBorrowedResourceCleanupException : InvalidOperationException
    {
        /// <summary>
        /// Initializes the exception.
        /// </summary>
        public NodeManagerBorrowedResourceCleanupException()
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerBorrowedResourceCleanupException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message and cause.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this error.</param>
        public NodeManagerBorrowedResourceCleanupException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Retains the failed cleanup attempt for the caller.
        /// </summary>
        /// <param name="state">The candidate's cleanup state.</param>
        /// <param name="rollback">Whether the candidate was unpublished.</param>
        /// <param name="innerException">The provider's cleanup failure.</param>
        internal NodeManagerBorrowedResourceCleanupException(
            BorrowedNodeManagerLeaseState state,
            bool rollback,
            Exception innerException)
            : base(
                rollback
                    ? "The borrowed NodeManager candidate could not be rolled back."
                    : "The borrowed NodeManager candidate could not be retired.",
                innerException)
        {
            m_state = state;
            m_rollback = rollback;
        }

        /// <summary>
        /// Retries the unfinished provider cleanup.
        /// </summary>
        /// <returns>A task that completes after cleanup succeeds.</returns>
        /// <exception cref="InvalidOperationException">
        /// This exception was not created by a lifecycle cleanup attempt.
        /// </exception>
        public ValueTask RetryAsync()
        {
            BorrowedNodeManagerLeaseState state = m_state ??
                throw new InvalidOperationException("This exception does not retain a cleanup attempt.");
            return m_rollback ? state.RollbackAsync() : state.RetireAsync();
        }

        /// <summary>The retained candidate cleanup state.</summary>
        private readonly BorrowedNodeManagerLeaseState? m_state;

        /// <summary>Whether the unfinished operation is rollback rather than retirement.</summary>
        private readonly bool m_rollback;
    }
}
