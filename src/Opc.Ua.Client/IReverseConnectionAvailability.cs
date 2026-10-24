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
using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A reverse connection source that also reports which servers currently
    /// hold a connection it can hand out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With reverse connect (OPC 10000-6, 7.1.3) every member of a
    /// non-transparent redundant Server set (OPC 10000-4, 6.6.2) opens its own
    /// connections to the client. Set a source that implements this interface
    /// as <see cref="ServerRedundancyOptions.MemberConnections"/> to reach the
    /// members over their connections instead of dialing them.
    /// </para>
    /// <para>
    /// <see cref="ReverseConnectManager"/> implements this interface and
    /// reports the connections it holds while no registration matches them.
    /// </para>
    /// </remarks>
    public interface IReverseConnectionAvailability : IReverseConnectionSource
    {
        /// <summary>
        /// Gets the endpoint urls that the admitted connections of a server
        /// announced in their ReverseHello message, for the connections that
        /// are not yet handed out.
        /// </summary>
        /// <param name="serverUri">The application uri of the server.</param>
        /// <returns>The announced endpoint urls, or an empty list when the
        /// server holds no such connection.</returns>
        IReadOnlyList<Uri> GetAvailableEndpointUrls(string serverUri);
    }
}
