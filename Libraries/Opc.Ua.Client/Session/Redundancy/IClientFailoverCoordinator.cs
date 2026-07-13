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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Coordinates backup-client takeover of active-client Subscriptions for OPC 10000-4 §6.6.3 client redundancy.
    /// </summary>
    public interface IClientFailoverCoordinator
    {
        /// <summary>
        /// Discovers subscription ids owned by the active client.
        /// </summary>
        ValueTask<ArrayOf<uint>> DiscoverActiveSubscriptionIdsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default);

        /// <summary>
        /// Transfers active-client subscriptions to the backup client session.
        /// </summary>
        ValueTask<ArrayOf<TransferResult>> TransferActiveSubscriptionsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default);
    }
}
