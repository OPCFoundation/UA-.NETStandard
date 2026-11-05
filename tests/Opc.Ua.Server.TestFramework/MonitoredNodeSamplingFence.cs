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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.TestFramework
{
    /// <summary>
    /// Provides a test-only fence for a node's report-by-exception consumer.
    /// </summary>
    public static class MonitoredNodeSamplingFence
    {
        /// <summary>
        /// Waits for admitted notifications without inserting or publishing another sample.
        /// Producers must remain quiescent until the wait completes.
        /// </summary>
        /// <param name="node">The monitored node with its server-installed asynchronous callback.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>Completion after notifications preceding the fence have been processed.</returns>
        public static ValueTask WaitAsync(NodeState node, CancellationToken cancellationToken = default)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (node.OnStateChangedAsync?.Target is not MonitoredNode2 monitored ||
                !ReferenceEquals(monitored.Node, node))
            {
                throw new InvalidOperationException("The node has no report-by-exception consumer callback.");
            }
            return monitored.WaitForPendingNotificationsAsync(cancellationToken);
        }
    }
}
