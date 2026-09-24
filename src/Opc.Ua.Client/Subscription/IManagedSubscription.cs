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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Internal lifecycle contract used by the subscription manager to coordinate physical subscriptions.
    /// </summary>
    internal interface IManagedSubscription : ISubscription, IMessageProcessor
    {
        /// <summary>
        /// Gets whether a CreateSubscription request is awaiting its server identifier.
        /// </summary>
        /// <value>
        /// <c>true</c> while creation is in progress; otherwise, <c>false</c>.
        /// </value>
        bool IsCreationInProgress { get; }

        /// <summary>
        /// Completes a successful server transfer by synchronizing monitored-item handles and recovering notifications.
        /// </summary>
        /// <param name="availableSequenceNumbers">
        /// Sequence numbers of notification messages retained in the server's retransmission queue.
        /// </param>
        /// <param name="ct">The token used to cancel synchronization and notification recovery.</param>
        /// <returns>
        /// <c>true</c> if local transfer completion succeeds; <c>false</c> if monitored-item handles cannot
        /// be synchronized.
        /// </returns>
        ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct = default);

        /// <summary>
        /// Notifies the subscription that the subscription manager has paused or resumed publishing.
        /// </summary>
        /// <param name="paused">
        /// <c>true</c> when publishing is paused; <c>false</c> when publishing resumes.
        /// </param>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
