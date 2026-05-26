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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Default implementation of OPC UA Part 9 alarm and condition
    /// client operations. Uses the well-known method ids on
    /// <c>ConditionType</c> / <c>AcknowledgeableConditionType</c> /
    /// <c>AlarmConditionType</c> / <c>DialogConditionType</c>, allowing
    /// servers that do not expose condition instances to still accept
    /// method calls with the <c>ConditionId</c> as the <c>ObjectId</c>.
    /// </summary>
    public class AlarmClient :
        IAlarmOperations,
        IDialogConditionOperations
    {
        private readonly ISessionClient m_session;

        /// <summary>
        /// Initializes a new AlarmClient over the supplied session.
        /// </summary>
        /// <param name="session">The client session to use.</param>
        public AlarmClient(ISessionClient session)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
        }

        #region Condition Operations

        /// <inheritdoc/>
        public async ValueTask EnableAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ConditionType_Enable,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisableAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ConditionType_Disable,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask AddCommentAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ConditionType_AddComment,
                ct,
                Variant.From(eventId),
                Variant.From(comment)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ConditionRefreshAsync(
            uint subscriptionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh,
                ct,
                Variant.From(subscriptionId)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ConditionRefresh2Async(
            uint subscriptionId,
            uint monitoredItemId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2,
                ct,
                Variant.From(subscriptionId),
                Variant.From(monitoredItemId)).ConfigureAwait(false);
        }

        #endregion

        #region Acknowledgeable Condition Operations

        /// <inheritdoc/>
        public async ValueTask AcknowledgeAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                ct,
                Variant.From(eventId),
                Variant.From(comment)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ConfirmAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                ct,
                Variant.From(eventId),
                Variant.From(comment)).ConfigureAwait(false);
        }

        #endregion

        #region Alarm Operations

        /// <inheritdoc/>
        public async ValueTask SilenceAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.AlarmConditionType_Silence,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask SuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            if (comment.IsNullOrEmpty)
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Suppress,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Suppress2,
                    ct,
                    Variant.From(comment)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask UnsuppressAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            if (comment.IsNullOrEmpty)
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Unsuppress,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Unsuppress2,
                    ct,
                    Variant.From(comment)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask RemoveFromServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            if (comment.IsNullOrEmpty)
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_RemoveFromService,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_RemoveFromService2,
                    ct,
                    Variant.From(comment)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask PlaceInServiceAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            if (comment.IsNullOrEmpty)
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_PlaceInService,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_PlaceInService2,
                    ct,
                    Variant.From(comment)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ResetAsync(
            NodeId conditionId,
            LocalizedText comment = default,
            CancellationToken ct = default)
        {
            if (comment.IsNullOrEmpty)
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Reset,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await CallVoidMethodAsync(
                    conditionId,
                    MethodIds.AlarmConditionType_Reset2,
                    ct,
                    Variant.From(comment)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask TimedShelveAsync(
            NodeId conditionId,
            double shelvingTime,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ShelvedStateMachineType_TimedShelve,
                ct,
                Variant.From(shelvingTime)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask OneShotShelveAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ShelvedStateMachineType_OneShotShelve,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask UnshelveAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.ShelvedStateMachineType_Unshelve,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<NodeId>> GetGroupMembershipsAsync(
            NodeId conditionId,
            CancellationToken ct = default)
        {
            ArrayOf<Variant> outputs = await m_session.CallAsync(
                conditionId,
                MethodIds.AlarmConditionType_GetGroupMemberships,
                ct).ConfigureAwait(false);

            if (outputs.Count == 0)
            {
                return ArrayOf<NodeId>.Empty;
            }

            return ExtractNodeIdArray(outputs[0]);
        }

        #endregion

        #region Dialog Condition Operations

        /// <inheritdoc/>
        public async ValueTask RespondAsync(
            NodeId conditionId,
            int selectedResponse,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.DialogConditionType_Respond,
                ct,
                Variant.From(selectedResponse)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask Respond2Async(
            NodeId conditionId,
            int selectedResponse,
            LocalizedText comment,
            CancellationToken ct = default)
        {
            await CallVoidMethodAsync(
                conditionId,
                MethodIds.DialogConditionType_Respond2,
                ct,
                Variant.From(selectedResponse),
                Variant.From(comment)).ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private async ValueTask CallVoidMethodAsync(
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct,
            params Variant[] args)
        {
            await m_session.CallAsync(objectId, methodId, ct, args).ConfigureAwait(false);
        }

        private static ArrayOf<NodeId> ExtractNodeIdArray(Variant variant)
        {
            if (variant.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }

            object? value = variant.AsBoxedObject();
            return value switch
            {
                NodeId[] arr => new ArrayOf<NodeId>(arr),
                ArrayOf<NodeId> array => array,
                _ => ArrayOf<NodeId>.Empty
            };
        }

        #endregion
    }
}
