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
    /// Identifies a node whose references a pending publication has reserved until its decision completes.
    /// </summary>
    /// <remarks>
    /// The reservation ends when the publication commits or is abandoned, so a caller can retry the rejected
    /// mutation afterwards. Any other <see cref="InvalidOperationException"/> is not a reservation conflict.
    /// </remarks>
    internal sealed class NodeReferencesReservedException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default reservation diagnostic.
        /// </summary>
        public NodeReferencesReservedException()
            : base("The node references have a pending publication.")
        {
        }

        /// <summary>
        /// Creates a conflict with a specific reservation diagnostic.
        /// </summary>
        /// <param name="message">The reservation diagnostic.</param>
        public NodeReferencesReservedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The reservation diagnostic.</param>
        /// <param name="innerException">The failure that caused the conflict.</param>
        public NodeReferencesReservedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}