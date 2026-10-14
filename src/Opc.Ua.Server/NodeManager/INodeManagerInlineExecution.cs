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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Declares whether the server may run a request that reaches a NodeManager
    /// on the thread that received the request.
    /// </summary>
    /// <remarks>
    /// With <see cref="ServerBaseConfiguration.InlineRequestExecution"/> set to
    /// <see cref="InlineRequestExecutionMode.Auto"/> (the default), the server runs
    /// a request on the thread that received it, for UA TCP the receive loop of
    /// its secure channel, only when every NodeManager the request can reach
    /// declares <see cref="SupportsInlineExecution"/>. While such a request runs
    /// synchronously, the channel reads no further request, so a NodeManager whose
    /// service calls block (synchronous I/O, waiting for a lock that is held for
    /// long, a device round trip) or compute for long delays every later request
    /// of the client on that channel. A NodeManager that does not implement this
    /// interface does not support inline execution.
    /// <para>
    /// <see cref="AsyncCustomNodeManager"/> supports it, and
    /// <see cref="CustomNodeManager2"/>, whose callbacks are synchronous, does
    /// not; both let a subclass override the default. The value is read when the
    /// NodeManager is registered with the server.
    /// </para>
    /// </remarks>
    public interface INodeManagerInlineExecution
    {
        /// <summary>
        /// Whether a request that reaches this NodeManager may run on the thread
        /// that received it, up to its first asynchronous wait.
        /// </summary>
        /// <value>
        /// <c>true</c> when the service calls of the NodeManager return quickly
        /// or wait asynchronously; <c>false</c> when they can block the calling
        /// thread or compute for more than a few milliseconds.
        /// </value>
        bool SupportsInlineExecution { get; }
    }
}
