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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Discovers Robot Intent controllers below Server/RobotIntent/Controllers.
    /// </summary>
    public sealed class RobotIntentClient
    {
        /// <summary>
        /// Creates a Robot Intent discovery client.
        /// </summary>
        public RobotIntentClient(
            ISession session,
            ITelemetryContext telemetry,
            IStreamingSubscription? streaming = null)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_streaming = streaming;
            RegisterEncodeableTypes(m_session);
        }

        /// <summary>
        /// Discovers all intent controller instances.
        /// </summary>
        public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> DiscoverControllersAsync(
            CancellationToken cancellationToken = default)
        {
            return CreateTransport(global::Opc.Ua.ObjectIds.ObjectsFolder, observeReconnect: false)
                .BrowseControllersAsync(cancellationToken);
        }

        /// <summary>
        /// Opens a high-level controller client.
        /// </summary>
        public RobotIntentControllerClient Controller(NodeId controllerId)
        {
            return new RobotIntentControllerClient(CreateTransport(controllerId));
        }

        private UaRobotIntentTransport CreateTransport(NodeId controllerId)
        {
            return CreateTransport(controllerId, observeReconnect: true);
        }

        private UaRobotIntentTransport CreateTransport(NodeId controllerId, bool observeReconnect)
        {
            return new UaRobotIntentTransport(m_session, controllerId, m_telemetry, m_streaming, observeReconnect);
        }

        private static void RegisterEncodeableTypes(ISession session)
        {
            RegisterEncodeableTypes(session.Factory);
            if (!ReferenceEquals(session.MessageContext.Factory, session.Factory))
            {
                RegisterEncodeableTypes(session.MessageContext.Factory);
            }
        }

        private static void RegisterEncodeableTypes(IEncodeableFactory factory)
        {
            var probe = new Pose3DDataType();
            if (!factory.TryGetEncodeableType(probe.BinaryEncodingId, out _))
            {
                factory.Builder.AddOpcUaRobotIntent().Commit();
            }
        }

        private readonly ISession m_session;
        private readonly ITelemetryContext m_telemetry;
        private readonly IStreamingSubscription? m_streaming;
    }

    /// <summary>
    /// High-level awaitable client for one IntentControllerType instance.
    /// </summary>
    public sealed class RobotIntentControllerClient
    {
        /// <summary>
        /// Creates a controller client over a transport.
        /// </summary>
        public RobotIntentControllerClient(IRobotIntentTransport transport)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>
        /// Gets the transport used by this controller client.
        /// </summary>
        public IRobotIntentTransport Transport { get; }

        /// <summary>
        /// Gets the controller NodeId.
        /// </summary>
        public NodeId ControllerId => Transport.ControllerId;

        /// <summary>
        /// Reads the controller capabilities and derives its facets.
        /// </summary>
        public ValueTask<RobotIntentControllerInfo> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Transport.ReadControllerAsync(cancellationToken);
        }

        /// <summary>
        /// Reads the current runtime state of the controller.
        /// </summary>
        public ValueTask<RobotIntentControllerState> ReadStateAsync(CancellationToken cancellationToken = default)
        {
            return Transport.ReadControllerStateAsync(cancellationToken);
        }

        /// <summary>
        /// Submits an intent and returns an awaitable operation handle when accepted.
        /// </summary>
        public async ValueTask<IntentOperationHandle> SubmitIntentAsync(
            IntentDataType intent,
            CancellationToken cancellationToken = default)
        {
            IntentSubmissionResult result = await Transport.SubmitIntentAsync(intent, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Accepted)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadRequestNotAllowed,
                    "Intent refused: {0} {1}",
                    result.Failure,
                    result.Message.Text ?? string.Empty);
            }
            IntentOperationHandle handle = new(this, result.IntentId, result.Operation);
            await handle.StartAsync(cancellationToken).ConfigureAwait(false);
            return handle;
        }

        /// <summary>
        /// Submits an intent and returns the refusal-aware submission result.
        /// </summary>
        public ValueTask<IntentSubmissionResult> TrySubmitIntentAsync(
            IntentDataType intent,
            CancellationToken cancellationToken = default)
        {
            return Transport.SubmitIntentAsync(intent, cancellationToken);
        }

        /// <summary>
        /// Opens an awaitable operation handle for an existing operation node.
        /// </summary>
        public async ValueTask<IntentOperationHandle> TrackOperationAsync(
            string intentId,
            NodeId operation,
            CancellationToken cancellationToken = default)
        {
            IntentOperationHandle handle = new(this, intentId, operation);
            await handle.StartAsync(cancellationToken).ConfigureAwait(false);
            return handle;
        }

        /// <summary>
        /// Lists operations published below the controller's Intents folder.
        /// </summary>
        public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(
            CancellationToken cancellationToken = default)
        {
            return Transport.ListOperationsAsync(cancellationToken);
        }

        /// <summary>
        /// Lists missions published below the controller's Missions folder.
        /// </summary>
        public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken cancellationToken = default)
        {
            return Transport.ListMissionsAsync(cancellationToken);
        }

        /// <summary>
        /// Cancels an intent and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> CancelIntentAsync(
            string intentId,
            StopModeEnum stopMode,
            CancellationToken cancellationToken = default)
        {
            return Transport.CancelIntentAsync(intentId, stopMode, cancellationToken);
        }

        /// <summary>
        /// Cancels all outstanding work and returns how many items the server acted on.
        /// </summary>
        public ValueTask<uint> CancelAllAsync(
            StopModeEnum stopMode,
            CancellationToken cancellationToken = default)
        {
            return Transport.CancelAllAsync(stopMode, cancellationToken);
        }

        /// <summary>
        /// Requests Pause and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken cancellationToken = default)
        {
            return Transport.PauseAsync(cancellationToken);
        }

        /// <summary>
        /// Requests Resume and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken cancellationToken = default)
        {
            return Transport.ResumeAsync(cancellationToken);
        }

        /// <summary>
        /// Retries an intent and returns the admission outcome for the new operation.
        /// </summary>
        public ValueTask<IntentSubmissionResult> RetryAsync(
            string intentId,
            CancellationToken cancellationToken = default)
        {
            return Transport.RetryAsync(intentId, cancellationToken);
        }

        /// <summary>
        /// Releases command authority held by this session.
        /// </summary>
        public ValueTask ReleaseControlAsync(CancellationToken cancellationToken = default)
        {
            return Transport.ReleaseControlAsync(cancellationToken);
        }

        /// <summary>
        /// Requests command authority and returns a lease.
        /// </summary>
        /// <remarks>
        /// Command authority is arbitration between clients, not authorisation. A Session
        /// holding authority but lacking the required role can still be refused by the Server.
        /// </remarks>
        public async ValueTask<CommandAuthorityLease> RequestAuthorityAsync(
            CancellationToken cancellationToken = default)
        {
            CommandAuthorityOutcome outcome = await Transport.RequestControlAsync(cancellationToken)
                .ConfigureAwait(false);
            CommandAuthorityLease lease = new(Transport, outcome.Granted, outcome.CurrentOwner);
            await lease.StartAsync(cancellationToken).ConfigureAwait(false);
            return lease;
        }

        /// <summary>
        /// Requests command authority and throws when the Server refuses it.
        /// </summary>
        /// <remarks>
        /// Use <see cref="RequestAuthorityAsync"/> when refusal is expected and the caller wants to branch on the
        /// lease.
        /// </remarks>
        public async ValueTask<CommandAuthorityLease> RequireAuthorityAsync(
            CancellationToken cancellationToken = default)
        {
            CommandAuthorityLease lease = await RequestAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (lease.Granted)
            {
                return lease;
            }

            NodeId currentOwner = lease.CurrentOwner;
            await lease.DisposeAsync().ConfigureAwait(false);
            if (!currentOwner.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadRequestNotAllowed,
                    "Command authority refused. CurrentOwner={0}.",
                    currentOwner);
            }

            throw ServiceResultException.Create(
                StatusCodes.BadRequestNotAllowed,
                "Command authority refused. No current owner was reported.");
        }

        /// <summary>
        /// Submits a mission and records its update id for local stale-update checks.
        /// </summary>
        public async ValueTask<MissionSubmissionResult> SubmitMissionAsync(
            MissionDataType mission,
            CancellationToken cancellationToken = default)
        {
            if (mission is null)
            {
                throw new ArgumentNullException(nameof(mission));
            }
            MissionSubmissionResult result = await Transport.SubmitMissionAsync(mission, cancellationToken)
                .ConfigureAwait(false);
            if (result.Accepted)
            {
                string missionId = result.MissionId.Length == 0 ? mission.MissionId ?? string.Empty : result.MissionId;
                lock (m_missionUpdateLock)
                {
                    m_lastMissionUpdateIds[missionId] = mission.MissionUpdateId;
                }
            }
            return result;
        }

        /// <summary>
        /// Replaces the mission horizon after locally rejecting non-increasing update ids.
        /// </summary>
        public async ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
            string missionId,
            uint missionUpdateId,
            ArrayOf<MissionStepDataType> horizonSteps,
            CancellationToken cancellationToken = default)
        {
            lock (m_missionUpdateLock)
            {
                if (m_lastMissionUpdateIds.TryGetValue(missionId, out uint lastMissionUpdateId) &&
                    missionUpdateId <= lastMissionUpdateId)
                {
                    Transport.Logger.MissionUpdateRefusedLocal(
                        missionId,
                        missionUpdateId,
                        MissionUpdateResultEnum.Outdated,
                        "MissionUpdateId shall be strictly greater than the mission's current value.");
                    return new MissionUpdateOutcome(
                        MissionUpdateResultEnum.Outdated,
                        new LocalizedText("MissionUpdateId shall be strictly greater than the mission's current value."));
                }
            }
            MissionUpdateOutcome outcome = await Transport.UpdateMissionAsync(
                missionId,
                missionUpdateId,
                horizonSteps,
                cancellationToken).ConfigureAwait(false);
            if (outcome.Result == MissionUpdateResultEnum.Accepted)
            {
                lock (m_missionUpdateLock)
                {
                    m_lastMissionUpdateIds[missionId] = missionUpdateId;
                }
            }
            return outcome;
        }

        /// <summary>
        /// Cancels a mission and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> CancelMissionAsync(
            string missionId,
            StopModeEnum stopMode,
            CancellationToken cancellationToken = default)
        {
            return Transport.CancelMissionAsync(missionId, stopMode, cancellationToken);
        }

        /// <summary>
        /// Opens a renewable real-time channel lease.
        /// </summary>
        public async ValueTask<RealTimeChannelLease> OpenRealTimeChannelAsync(
            string channelId,
            TimeSpan requestedLease,
            CancellationToken cancellationToken = default)
        {
            RealTimeChannelLease lease = new(Transport, channelId, requestedLease);
            await lease.OpenAsync(cancellationToken).ConfigureAwait(false);
            return lease;
        }

        private readonly Lock m_missionUpdateLock = new();
        private readonly Dictionary<string, uint> m_lastMissionUpdateIds = new(StringComparer.Ordinal);
    }

    internal static partial class RobotIntentControllerClientLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.MissionUpdateRefused,
            Level = LogLevel.Warning,
            Message = "Robot Intent mission update refused locally. MissionId={MissionId}, UpdateId={UpdateId}, " +
                "Result={Result}, Message={Message}.")]
        public static partial void MissionUpdateRefusedLocal(
            this ILogger logger,
            string missionId,
            uint updateId,
            MissionUpdateResultEnum result,
            string message);
    }
}
