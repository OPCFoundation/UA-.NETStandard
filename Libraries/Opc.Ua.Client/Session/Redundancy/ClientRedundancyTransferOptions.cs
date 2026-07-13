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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for OPC 10000-4 §6.6.3 client redundancy Subscription takeover with <c>TransferSubscriptions</c>.
    /// </summary>
    public sealed record ClientRedundancyTransferOptions
    {
        /// <summary>
        /// Sends initial values after the backup client takes ownership of transferred subscriptions.
        /// </summary>
        public bool SendInitialValues { get; init; } = true;

        /// <summary>
        /// The active client's session id. If omitted, <see cref="ActiveSessionName"/> is used.
        /// </summary>
        public NodeId ActiveSessionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The active client's session name used to find its session diagnostics.
        /// </summary>
        public string ActiveSessionName { get; init; } = string.Empty;

        /// <summary>
        /// Expected active-client user display name. When set, the backup session must use the same user.
        /// </summary>
        public string ActiveUserDisplayName { get; init; } = string.Empty;
    }
}
