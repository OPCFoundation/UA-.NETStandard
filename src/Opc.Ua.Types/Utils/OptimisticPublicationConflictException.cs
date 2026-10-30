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

namespace Opc.Ua
{
    /// <summary>
    /// Identifies a serving revision that changed after an optimistic publication captured it.
    /// </summary>
    internal sealed class OptimisticPublicationConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default stale-revision diagnostic.
        /// </summary>
        public OptimisticPublicationConflictException()
            : base("A serving revision changed after it was captured.")
        {
        }

        /// <summary>
        /// Creates a conflict for a stale serving revision.
        /// </summary>
        /// <param name="message">The stale revision diagnostic.</param>
        public OptimisticPublicationConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The stale revision diagnostic.</param>
        /// <param name="innerException">The failure that caused the conflict.</param>
        public OptimisticPublicationConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
