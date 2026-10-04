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

using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Classifies a failed call of the optional Server.GetMonitoredItems
    /// method (OPC 10000-5, 9.1) that a client uses to map the monitored
    /// items of a transferred subscription.
    /// </summary>
    internal static class GetMonitoredItemsFallback
    {
        /// <summary>
        /// Whether the call failed with a transient error after which a
        /// retry can still succeed (timeout, busy server, lost connection or
        /// session). Any other error means the server does not provide the
        /// method: stacks answer BadMethodInvalid, BadNotSupported,
        /// BadNotImplemented, BadNothingToDo or BadInternalError, or an
        /// unusable result.
        /// </summary>
        public static bool IsTransientFailure(StatusCode status)
        {
            return s_transientFailures.Contains(status.CodeBits);
        }

        private static readonly HashSet<uint> s_transientFailures =
        [
            StatusCodes.BadTimeout.CodeBits,
            StatusCodes.BadRequestTimeout.CodeBits,
            StatusCodes.BadTooManyOperations.CodeBits,
            StatusCodes.BadServerTooBusy.CodeBits,
            StatusCodes.BadResourceUnavailable.CodeBits,
            StatusCodes.BadOutOfMemory.CodeBits,
            StatusCodes.BadCommunicationError.CodeBits,
            StatusCodes.BadConnectionClosed.CodeBits,
            StatusCodes.BadNotConnected.CodeBits,
            StatusCodes.BadServerNotConnected.CodeBits,
            StatusCodes.BadNoCommunication.CodeBits,
            StatusCodes.BadDisconnect.CodeBits,
            StatusCodes.BadSecureChannelClosed.CodeBits,
            StatusCodes.BadSecureChannelIdInvalid.CodeBits,
            StatusCodes.BadSessionClosed.CodeBits,
            StatusCodes.BadSessionIdInvalid.CodeBits,
            StatusCodes.BadSessionNotActivated.CodeBits,
            StatusCodes.BadRequestInterrupted.CodeBits,
            StatusCodes.BadRequestCancelledByClient.CodeBits,
            StatusCodes.BadOperationAbandoned.CodeBits,
            StatusCodes.BadShutdown.CodeBits,
            StatusCodes.BadServerHalted.CodeBits
        ];
    }
}
