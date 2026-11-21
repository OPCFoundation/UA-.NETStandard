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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports a candidate that borrows a detected live resource without a cleanup lease.
    /// </summary>
    /// <remarks>
    /// The lifecycle has not prepared or disposed the candidate. The factory or caller retains
    /// responsibility for releasing candidate-owned resources without disposing the borrowed resource.
    /// </remarks>
    public sealed class NodeManagerBorrowedResourceException : InvalidOperationException
    {
        /// <summary>
        /// Initializes the exception.
        /// </summary>
        public NodeManagerBorrowedResourceException()
            : base("A candidate borrowing a live NodeManager resource requires a cleanup lease.")
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerBorrowedResourceException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message and cause.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this error.</param>
        public NodeManagerBorrowedResourceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes the exception with the unprepared candidate.
        /// </summary>
        /// <param name="candidate">The candidate still owned by the factory or caller.</param>
        public NodeManagerBorrowedResourceException(IAsyncNodeManager candidate)
            : this()
        {
            Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }

        /// <summary>
        /// Gets the unprepared candidate, if one was supplied.
        /// </summary>
        public IAsyncNodeManager? Candidate { get; }
    }
}
