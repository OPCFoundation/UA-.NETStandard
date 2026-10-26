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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Resolves an explicitly supported transport scope and its server-assigned default PublisherId.
    /// Implementations must return a nonzero unsigned integer or a nonempty string, or throw
    /// BadNotSupported. This policy does not infer a default from configured connections.
    /// </summary>
    public interface IPubSubDefaultPublisherIdPolicy
    {
        /// <summary>
        /// Resolves the default PublisherId for a supported transport profile.
        /// </summary>
        ValueTask<Variant> ResolveAsync(string transportProfileUri, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Atomic Part 14 communication-ID reservations, independent of configuration and file-handle allocation.
    /// Distributed implementations must serialize reservation, persistence and Session closure across hosts.
    /// </summary>
    public interface IPubSubCommunicationIdReservations
    {
        /// <summary>
        /// Reserves the complete request, excluding configured IDs and outstanding reservations.
        /// The caller must serialize the configuration snapshot with configuration mutations.
        /// </summary>
        ValueTask<PubSubCommunicationIdReservation> ReserveAsync(
            NodeId sessionId,
            string transportProfileUri,
            ushort writerGroupCount,
            ushort dataSetWriterCount,
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks ownership and persists the configuration without releasing pending IDs early.
        /// Reservations are consumed only when the persistence callback succeeds.
        /// The caller must hold its configuration transaction for the entire operation.
        /// </summary>
        ValueTask PersistAsync(
            PubSubConfigurationDataType configuration,
            Func<CancellationToken, ValueTask> persist,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a Session closed immediately. In-flight persistence retains its IDs until completion.
        /// Closed Session identifiers cannot reserve or consume again.
        /// </summary>
        void CloseSession(NodeId sessionId);
    }

    /// <summary>
    /// A complete, typed result of the standard ReserveIds operation.
    /// </summary>
    public sealed record PubSubCommunicationIdReservation(
        Variant DefaultPublisherId,
        ArrayOf<ushort> WriterGroupIds,
        ArrayOf<ushort> DataSetWriterIds);

    /// <summary>
    /// Carries the actual calling Session through asynchronous configuration mutations.
    /// Sessionless direct calls remain possible but cannot consume another Session's reservations.
    /// This context is not an authorization policy; the server must authorize before entering it.
    /// </summary>
    public static class PubSubConfigurationSession
    {
        /// <summary>
        /// Gets the calling Session, or the null NodeId for a direct, sessionless mutation.
        /// </summary>
        public static NodeId Current => s_session.Value;

        /// <summary>
        /// Executes an asynchronous mutation as the actual calling Session.
        /// </summary>
        /// <typeparam name="TResult">Mutation result type.</typeparam>
        public static async ValueTask<TResult> ExecuteAsync<TResult>(
            NodeId sessionId,
            Func<CancellationToken, ValueTask<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            if (sessionId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            NodeId previous = s_session.Value;
            s_session.Value = sessionId;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                s_session.Value = previous;
            }
        }

        /// <summary>Session identity carried by the asynchronous execution context.</summary>
        private static readonly AsyncLocal<NodeId> s_session = new();
    }
}
