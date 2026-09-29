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

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua
{
    /// <summary>
    /// Optional, read-only access to committed server session bindings.
    /// </summary>
    /// <remarks>
    /// Implementations own lifecycle accounting, including transfer, timeout and shutdown.
    /// This capability supplements existing transport callbacks and session managers without
    /// changing their contracts. It must not run service validation or refresh session activity.
    /// </remarks>
    public interface ISessionBindingProvider
    {
        /// <summary>
        /// Whether a channel has any distinct activated sessions still owned by the manager.
        /// </summary>
        /// <remarks>
        /// This is an indexed membership check, not a user identity or a trusted reservation.
        /// Expired membership is removed by the session timeout/closure lifecycle.
        /// </remarks>
        bool HasSession(string secureChannelId);

        /// <summary>
        /// Looks up a server-issued token and checks its live, activated, unexpired binding
        /// against a context supplied by the transport, not by the request body.
        /// </summary>
        /// <remarks>
        /// Failure grants no session classification. Success is only a point-in-time snapshot:
        /// full execution-time service validation remains mandatory. Re-query after queueing;
        /// a changed snapshot or activation sequence invalidates the earlier classification.
        /// Shared logical channels do not establish a transport peer's user identity.
        /// </remarks>
        bool TryGetSessionContext(
            NodeId authenticationToken,
            SecureChannelContext channelContext,
            [NotNullWhen(true)] out SessionBindingContext? context);
    }
}
