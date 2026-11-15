/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports that NodeManager cleanup remains owned after a caller's wait has ended.
    /// </summary>
    /// <remarks>
    /// The owner retains the NodeManager and its dependencies until <see cref="Completion"/> finishes.
    /// The task reports physical cleanup or its failure, not merely the transfer of ownership.
    /// For an explicitly registered contributor, unregistering it permits its reads to drain.
    /// </remarks>
    // TODO: Revisit standard exception constructors if they can represent a missing cleanup owner safely.
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A deferred-cleanup exception must carry the actual cleanup completion task.")]
    public sealed class NodeManagerCleanupDeferredException : InvalidOperationException
    {
        /// <summary>
        /// Creates an exception carrying the physical cleanup completion task.
        /// </summary>
        /// <param name="completion">The task owned by the lifecycle until cleanup finishes.</param>
        /// <param name="innerException">An immediate cleanup failure, if one was also encountered.</param>
        internal NodeManagerCleanupDeferredException(Task completion, Exception? innerException = null)
            : base("NodeManager cleanup remains pending; observe Completion for its outcome.", innerException)
        {
            Completion = completion ?? throw new ArgumentNullException(nameof(completion));
        }

        /// <summary>
        /// Gets the task that completes after physical cleanup, or faults if cleanup fails.
        /// </summary>
        public Task Completion { get; }
    }
}
