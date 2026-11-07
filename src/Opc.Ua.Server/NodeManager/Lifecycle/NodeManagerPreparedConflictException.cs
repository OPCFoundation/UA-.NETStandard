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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds a typed stale serving-revision conflict to the prepared batch that observed it.
    /// </summary>
    internal sealed class NodeManagerPreparedConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict with the default stale-publication diagnostic.
        /// </summary>
        public NodeManagerPreparedConflictException()
            : base("A prepared batch observed a stale serving revision.")
        {
        }

        /// <summary>
        /// Creates a conflict with a diagnostic.
        /// </summary>
        /// <param name="message">The stale-publication diagnostic.</param>
        public NodeManagerPreparedConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a conflict with its originating failure.
        /// </summary>
        /// <param name="message">The stale-publication diagnostic.</param>
        /// <param name="innerException">The originating stale serving-revision conflict.</param>
        public NodeManagerPreparedConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a conflict bound to its prepared batch.
        /// </summary>
        /// <param name="failure">The stale serving-revision conflict.</param>
        /// <param name="batch">The prepared batch whose capture became stale.</param>
        internal NodeManagerPreparedConflictException(
            OptimisticPublicationConflictException failure,
            IPreparedNodeManagerBatch batch)
            : base(failure.Message, failure)
        {
            Batch = batch;
        }

        /// <summary>
        /// Gets the prepared batch whose captured serving revision became stale.
        /// </summary>
        internal IPreparedNodeManagerBatch? Batch { get; }
    }
}
