// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using V1 = Opc.Ua.ISA95.JobControl.V1;

namespace Opc.Ua.ISA95.Client
{
    /// <summary>
    /// Direct client for the three ISA-95 Job Control V1 endpoint objects.
    /// </summary>
    public sealed class Isa95JobControlV1Client
    {
        /// <summary>
        /// Initializes a Job Control V1 client.
        /// </summary>
        public Isa95JobControlV1Client(
            ISession session,
            NodeId jobOrderReceiverId,
            NodeId jobResponseProviderId,
            NodeId jobResponseReceiverId,
            ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            ValidateNodeId(jobOrderReceiverId, nameof(jobOrderReceiverId));
            ValidateNodeId(jobResponseProviderId, nameof(jobResponseProviderId));
            ValidateNodeId(jobResponseReceiverId, nameof(jobResponseReceiverId));

            Isa95EncodeableRegistration.Register(session);
            JobOrderReceiverId = jobOrderReceiverId;
            JobResponseProviderId = jobResponseProviderId;
            JobResponseReceiverId = jobResponseReceiverId;
            JobOrderReceiver = new V1.ISA95JobOrderReceiverObjectTypeClient(session, jobOrderReceiverId, telemetry);
            JobResponseProvider = new V1.ISA95JobResponseProviderObjectTypeClient(
                session,
                jobResponseProviderId,
                telemetry);
            JobResponseReceiver = new V1.ISA95JobResponseReceiverObjectTypeClient(
                session,
                jobResponseReceiverId,
                telemetry);
        }

        /// <summary>
        /// Gets the session used by this client.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context used by the generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the Job Order Receiver object identifier.
        /// </summary>
        public NodeId JobOrderReceiverId { get; }

        /// <summary>
        /// Gets the Job Response Provider object identifier.
        /// </summary>
        public NodeId JobResponseProviderId { get; }

        /// <summary>
        /// Gets the Job Response Receiver object identifier.
        /// </summary>
        public NodeId JobResponseReceiverId { get; }

        /// <summary>
        /// Gets the generated Job Order Receiver proxy.
        /// </summary>
        public V1.ISA95JobOrderReceiverObjectTypeClient JobOrderReceiver { get; }

        /// <summary>
        /// Gets the generated Job Response Provider proxy.
        /// </summary>
        public V1.ISA95JobResponseProviderObjectTypeClient JobResponseProvider { get; }

        /// <summary>
        /// Gets the generated Job Response Receiver proxy.
        /// </summary>
        public V1.ISA95JobResponseReceiverObjectTypeClient JobResponseReceiver { get; }

        /// <summary>
        /// Invokes the V1 ReceiveJobOrder method.
        /// </summary>
        public ValueTask<ulong> ReceiveJobOrderAsync(
            V1.ISA95JobOrderCommandEnum command,
            V1.ISA95JobOrderDataType jobOrder,
            CancellationToken ct = default)
        {
            return JobOrderReceiver.ReceiveJobOrderAsync(command, jobOrder, ct);
        }

        /// <summary>
        /// Invokes the V1 RequestJobResponse method.
        /// </summary>
        public ValueTask<(ArrayOf<V1.ISA95JobResponseDataType> Responses, ulong ReturnStatus)>
            RequestJobResponseAsync(
                string jobOrderId,
                V1.ISA95JobOrderStateEnum state,
                CancellationToken ct = default)
        {
            return JobResponseProvider.RequestJobResponseAsync(jobOrderId, state, ct);
        }

        /// <summary>
        /// Invokes the V1 ReceiveJobResponse method.
        /// </summary>
        public ValueTask<ulong> ReceiveJobResponseAsync(
            V1.ISA95JobResponseDataType response,
            CancellationToken ct = default)
        {
            return JobResponseReceiver.ReceiveJobResponseAsync(response, ct);
        }

        private static void ValidateNodeId(NodeId nodeId, string parameterName)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("An endpoint object NodeId is required.", parameterName);
            }
        }
    }
}
