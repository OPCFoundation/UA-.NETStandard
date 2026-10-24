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
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Retains exact direct-association transactions and nonreuse history within one authenticated context.
    /// The context owns serialization, authorization, deadlines and actual stream admission.
    /// </summary>
    internal sealed class FlowDirectAssociationRegistry
    {
        /// <summary>
        /// Creates finite history which refuses exhaustion rather than recycling tokens.
        /// </summary>
        internal FlowDirectAssociationRegistry(int maxHistory, DataChannelApplicationBudget? budget = null)
        {
            if (maxHistory <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHistory));
            }
            m_maxHistory = maxHistory;
            m_budget = budget;
        }

        /// <summary>
        /// Records a transaction only after its flow, revision, source and requested direction are admitted.
        /// An exact retry returns the original owner without replacing its acknowledgement or stream state.
        /// </summary>
        internal DataChannelResult Admit(
            FlowState flow,
            FlowAssociation control,
            uint revision,
            uint operationId,
            out FlowDirectAssociation? association)
        {
            association = null;
            if (flow.IsTerminal ||
                control.Flow != flow.Id || revision == 0 ||
                operationId == 0 || control.Operation != FlowOperation.Associate ||
                control.Direction > 1 || control.Token == Guid.Empty ||
                !PermitsDirection(flow.Parameters.Direction, control.Direction))
            {
                return DataChannelResult.InvalidState;
            }
            if (m_tokens.TryGetValue(control.Token, out FlowDirectAssociation? recorded))
            {
                if (!ReferenceEquals(recorded.Flow, flow) ||
                    !recorded.Matches(control, revision, operationId))
                {
                    return DataChannelResult.InvalidState;
                }
                association = recorded;
                return DataChannelResult.Good;
            }
            if (flow.State != DataChannelState.Opening || revision != flow.Revision)
            {
                return DataChannelResult.InvalidState;
            }
            if (m_directions.ContainsKey((flow.Id, control.Direction)) ||
                m_operations.Contains((flow.Id, operationId)))
            {
                return DataChannelResult.InvalidState;
            }
            if (m_tokens.Count >= m_maxHistory)
            {
                return DataChannelResult.WouldBlock;
            }
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation = null;
            try
            {
                if (m_budget != null && !m_budget.TryReserveCarrier(HistoryEntryBytes, 0, 1, out reservation))
                {
                    return DataChannelResult.WouldBlock;
                }
                association = new FlowDirectAssociation(flow, control.Direction, control.Token, revision, operationId);
                m_tokens.Add(control.Token, association);
                m_directions.Add((flow.Id, control.Direction), association);
                m_operations.Add((flow.Id, operationId));
                if (reservation != null)
                {
                    m_reservations.Add(reservation);
                    reservation = null;
                }
                return DataChannelResult.Good;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <summary>
        /// Confirms only the exact admitted transaction, including its actual flow owner and all wire fields.
        /// </summary>
        internal DataChannelResult Acknowledge(
            FlowState flow,
            FlowAssociation acknowledgement,
            uint revision,
            uint operationId)
        {
            if (flow.IsTerminal ||
                acknowledgement.Operation != FlowOperation.AssociateAck ||
                !m_tokens.TryGetValue(acknowledgement.Token, out FlowDirectAssociation? association) ||
                !ReferenceEquals(association.Flow, flow) ||
                !association.Matches(acknowledgement, revision, operationId))
            {
                return DataChannelResult.InvalidState;
            }
            association.Acknowledged = true;
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Requires an acknowledged transaction for every sending direction before publishing READY.
        /// </summary>
        internal bool CanBecomeReady(FlowState flow)
        {
            if (flow.IsTerminal)
            {
                return false;
            }
            for (byte direction = 0; direction <= 1; direction++)
            {
                if (PermitsDirection(flow.Parameters.Direction, direction) &&
                    (!m_directions.TryGetValue((flow.Id, direction), out FlowDirectAssociation? association) ||
                        !ReferenceEquals(association.Flow, flow) || !association.Acknowledged))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Finds only a ready, acknowledged and locally authorized sending or receiving direction.
        /// </summary>
        internal bool TryGetUsable(
            FlowState flow,
            bool sending,
            [NotNullWhen(true)] out FlowDirectAssociation? association)
        {
            association = null;
            if (!flow.Ready || flow.IsTerminal || (sending ? !flow.MaySend : !flow.MayReceive))
            {
                return false;
            }
            byte direction = flow.IsLocalOpener == sending ? (byte)0 : (byte)1;
            return m_directions.TryGetValue((flow.Id, direction), out association) &&
                ReferenceEquals(association.Flow, flow) && association.Acknowledged;
        }

        /// <summary>
        /// Resolves only an acknowledged local receiving token in this context, never a retired owner.
        /// </summary>
        internal bool TryGetReceiver(Guid token, [NotNullWhen(true)] out FlowState? flow)
        {
            flow = null;
            if (!m_tokens.TryGetValue(token, out FlowDirectAssociation? association) ||
                association.Flow is not { } owner ||
                !TryGetUsable(owner, false, out FlowDirectAssociation? receiving) ||
                !ReferenceEquals(association, receiving))
            {
                return false;
            }
            flow = owner;
            return true;
        }

        /// <summary>
        /// Releases context-lifetime history only after the complete context has terminated.
        /// </summary>
        internal void Clear()
        {
            m_tokens.Clear();
            m_directions.Clear();
            m_operations.Clear();
            foreach (var reservation in m_reservations)
            {
                reservation.Dispose();
            }
            m_reservations.Clear();
        }

        /// <summary>
        /// Recognizes recorded outcomes without letting an old operation authorize a new transaction.
        /// </summary>
        internal bool IsRecorded(FlowState flow, FlowAssociation control, uint revision, uint operation)
        {
            return m_tokens.TryGetValue(control.Token, out FlowDirectAssociation? association) &&
                ReferenceEquals(association.Flow, flow) && association.Matches(control, revision, operation);
        }

        /// <summary>
        /// Retains only compact token history, not the retired flow's queues, source or asynchronous stream owners.
        /// </summary>
        internal void Retire(FlowState flow)
        {
            for (byte direction = 0; direction <= 1; direction++)
            {
                if (m_directions.TryGetValue((flow.Id, direction), out FlowDirectAssociation? association) &&
                    ReferenceEquals(association.Flow, flow))
                {
                    association.Flow = null;
                    m_directions.Remove((flow.Id, direction));
                }
            }
        }

        /// <summary>
        /// Tests the admitted opener-relative direction without interpreting transport roles.
        /// </summary>
        private static bool PermitsDirection(DataChannelDirection requested, byte direction)
        {
            return requested == DataChannelDirection.Duplex ||
                (direction == 0 && requested == DataChannelDirection.OpenerToAcceptor) ||
                (direction == 1 && requested == DataChannelDirection.AcceptorToOpener);
        }

        /// <summary>The finite context-lifetime history bound.</summary>
        private readonly int m_maxHistory;

        /// <summary>The shared application owner for retained association history.</summary>
        private readonly DataChannelApplicationBudget? m_budget;

        /// <summary>The leases retained until context closure, including compact retired-token history.</summary>
        private readonly List<DataChannelApplicationBudget.DataChannelCarrierReservation> m_reservations = [];

        /// <summary>The bounded allowance for one token, direction, operation and dictionary metadata.</summary>
        internal const int HistoryEntryBytes = 256;

        /// <summary>Token history is never evicted or recycled after flow termination.</summary>
        private readonly Dictionary<Guid, FlowDirectAssociation> m_tokens = [];

        /// <summary>Each flow direction has at most one immutable transaction owner.</summary>
        private readonly Dictionary<(uint Flow, byte Direction), FlowDirectAssociation> m_directions = [];

        /// <summary>One operation cannot authorize two tokens or directions within a flow.</summary>
        private readonly HashSet<(uint Flow, uint Operation)> m_operations = [];
    }

    /// <summary>
    /// Immutable authorization owner for one direct sending direction and its exact acknowledgement.
    /// </summary>
    internal sealed class FlowDirectAssociation(
        FlowState flow,
        byte direction,
        Guid token,
        uint revision,
        uint operationId)
    {
        /// <summary>
        /// Gets or sets the one receiving-token registration handshake.
        /// </summary>
        internal TaskCompletionSource<bool>? ReceiveRegistered { get; set; }

        /// <summary>
        /// Gets the exact flow owner, including its authorized source and admitted limits.
        /// </summary>
        internal FlowState? Flow { get; set; } = flow;

        /// <summary>
        /// Gets the immutable wire identifier without retaining a terminal flow object.
        /// </summary>
        internal uint FlowId { get; } = flow.Id;

        /// <summary>
        /// Gets the sending direction relative to the opener, independently of the transport role.
        /// </summary>
        internal byte Direction { get; } = direction;

        /// <summary>
        /// Gets the context-lifetime single-use token.
        /// </summary>
        internal Guid Token { get; } = token;

        /// <summary>
        /// Gets the revision admitted by ASSOCIATE.
        /// </summary>
        internal uint Revision { get; } = revision;

        /// <summary>
        /// Gets the immutable transaction identifier.
        /// </summary>
        internal uint OperationId { get; } = operationId;

        /// <summary>
        /// Gets or sets whether the exact acknowledgement has been committed or received.
        /// </summary>
        internal bool Acknowledged { get; set; }

        /// <summary>
        /// Compares all transaction fields without accepting a different token, direction or owner.
        /// </summary>
        internal bool Matches(FlowAssociation control, uint revision, uint operationId)
        {
            return control.Flow == FlowId && control.Direction == Direction && control.Token == Token &&
                revision == Revision && operationId == OperationId;
        }
    }
}
