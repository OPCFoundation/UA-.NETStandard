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
    /// Optional capability of a monitored-item queue whose contents are kept in
    /// storage, such as a queue an <see cref="ISubscriptionStore"/> returns from its
    /// queue restore methods or an <see cref="IMonitoredItemQueueFactory"/> creates.
    /// It lets the server restore durable subscriptions all or nothing: the stored
    /// contents change only once every stored subscription was restored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While <see cref="SubscriptionManager.RestoreSubscriptionsAsync"/> restores the
    /// durable subscriptions it calls <see cref="BeginRestore"/> on each queue of a
    /// restored monitored item before it changes the queue. From then on the queue
    /// must not write to its storage. A monitored item restored by other means, e.g.
    /// by <see cref="Subscription.RestoreAsync(IServerInternal, IStoredSubscription, System.Threading.CancellationToken)"/>
    /// called directly, does not stage its queues.
    /// </para>
    /// <para>
    /// When the restore completes, the server calls <see cref="CommitRestore"/>: the
    /// queue writes its current contents once and then keeps its storage up to date
    /// as before. The server calls it after the subscription store was told that the
    /// restore completed, when the restore can no longer be rolled back, so
    /// <see cref="CommitRestore"/> must not throw: a queue that cannot write its
    /// contents handles the failure itself, e.g. logs it and writes again on its next
    /// change. The server logs an exception thrown by <see cref="CommitRestore"/> or
    /// <see cref="AbortRestore"/> and continues with the other queues.
    /// </para>
    /// <para>
    /// When the restore fails or is cancelled, the server calls
    /// <see cref="AbortRestore"/> and then disposes the queue. The queue must not
    /// start a write to its storage again, must drop every write it has not started
    /// yet, and disposing it must not remove the stored contents, which a retry of
    /// the restore reads again. A write that is already in progress when
    /// <see cref="AbortRestore"/> is called cannot be recalled and may complete.
    /// A queue staged by <see cref="BeginRestore"/> starts no write before
    /// <see cref="CommitRestore"/>, so aborting it keeps the stored contents exactly
    /// as they were before the restore. <see cref="AbortRestore"/> may also be called
    /// on a queue <see cref="BeginRestore"/> was not called on; its storage then keeps
    /// what its writes started before the abort stored.
    /// </para>
    /// <para>
    /// A queue that is disposed after <see cref="BeginRestore"/> and before
    /// <see cref="CommitRestore"/> or <see cref="AbortRestore"/>, e.g. because the
    /// creation of its monitored item failed, behaves as if
    /// <see cref="AbortRestore"/> was called: disposing it keeps the stored contents.
    /// </para>
    /// <para>
    /// A queue the server does not call <see cref="BeginRestore"/> on keeps writing to
    /// its storage as usual.
    /// </para>
    /// </remarks>
    public interface IRestorableMonitoredItemQueue
    {
        /// <summary>
        /// Stops writing changes of the queue to its storage until
        /// <see cref="CommitRestore"/> or <see cref="AbortRestore"/> is called.
        /// </summary>
        void BeginRestore();

        /// <summary>
        /// Writes the current contents of the queue to its storage and resumes
        /// writing every change. Does nothing unless <see cref="BeginRestore"/>
        /// was called and <see cref="AbortRestore"/> was not. Must not throw.
        /// </summary>
        void CommitRestore();

        /// <summary>
        /// Stops the queue from starting writes to or removals of its storage for
        /// good and drops the writes it has not started, so that disposing the queue
        /// releases only its local contents. A write already in progress may complete.
        /// </summary>
        void AbortRestore();
    }
}
