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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Carries the state of the connected server, as observed by the session
    /// keep-alive.
    /// </summary>
    public sealed class ServerStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ServerStatusChangedEventArgs"/> class.
        /// </summary>
        /// <param name="status">Status of the keep-alive that observed the
        /// state.</param>
        /// <param name="currentState">The state of the server.</param>
        /// <param name="currentTime">The time reported by the server.</param>
        public ServerStatusChangedEventArgs(
            ServiceResult? status,
            ServerState currentState,
            DateTime currentTime)
        {
            Status = status;
            CurrentState = currentState;
            CurrentTime = currentTime;
        }

        /// <summary>
        /// Status of the keep-alive that observed the state. A bad status means
        /// the server could not be reached, in which case
        /// <see cref="CurrentState"/> is the last state that was read.
        /// </summary>
        public ServiceResult? Status { get; }

        /// <summary>
        /// The state of the server.
        /// </summary>
        public ServerState CurrentState { get; }

        /// <summary>
        /// The time reported by the server.
        /// </summary>
        public DateTime CurrentTime { get; }
    }
}
