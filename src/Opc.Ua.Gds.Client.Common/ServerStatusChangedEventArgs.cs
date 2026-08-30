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
    /// Carries a data change notification for the <c>Server_ServerStatus</c>
    /// variable of the connected server.
    /// </summary>
    /// <remarks>
    /// Raised by <see cref="IGlobalDiscoveryServerClient.ServerStatusChanged"/>
    /// and <see cref="IServerPushConfigurationClient.ServerStatusChanged"/> for
    /// every notification the monitored item delivers, including the initial
    /// value reported when the item is created on the server.
    /// </remarks>
    public sealed class ServerStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ServerStatusChangedEventArgs"/> class.
        /// </summary>
        /// <param name="value">The notified value of the
        /// <c>Server_ServerStatus</c> variable.</param>
        public ServerStatusChangedEventArgs(DataValue value)
        {
            Value = value;
            Status = value.GetValue<ServerStatusDataType?>(null);
        }

        /// <summary>
        /// The raw notification, including status code and timestamps. Consult
        /// <see cref="DataValue.StatusCode"/> before using
        /// <see cref="Status"/>: a bad status leaves it <c>null</c>.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// The decoded server status, or <c>null</c> when
        /// <see cref="Value"/> reports a non-good status code or does not
        /// carry a <see cref="ServerStatusDataType"/> body.
        /// </summary>
        public ServerStatusDataType? Status { get; }
    }
}
