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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.Subscriber
{
    /// <summary>
    /// <see cref="ITargetVariableWriter"/> that applies subscriber-side DataSet
    /// field values to an external OPC UA server through an injected
    /// <see cref="IServerSession"/>. Each resolved field is written with a
    /// single Write service call so the per-field
    /// <see cref="ITargetVariableWriter"/> contract maps one-to-one onto a server
    /// Write.
    /// </summary>
    /// <remarks>
    /// The writer is fail-soft: a service fault, transport error or unexpected
    /// failure never escapes <see cref="WriteAsync"/>. Instead a Bad
    /// <see cref="StatusCode"/> is returned (the fault's status code when known,
    /// otherwise <see cref="StatusCodes.BadCommunicationError"/>) and logged, so
    /// the subscriber receive loop keeps running. Cancellation is always
    /// propagated to the caller.
    /// TODO: batch all fields of a DataSetMessage into a single Write service call
    /// instead of one Write per field for higher throughput.
    /// </remarks>
    public sealed class ServerTargetVariableWriter : ITargetVariableWriter
    {
        private readonly IServerSession m_session;
        private readonly AdapterMetrics? m_metrics;
        private readonly ILogger m_logger;

        /// <summary>
        /// Creates a new external-server target variable writer over the supplied
        /// session.
        /// </summary>
        /// <param name="session">
        /// The external-server session used to issue the Write service calls.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        /// <param name="metrics">
        /// Optional metrics sink that records write activity.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="session"/> or <paramref name="telemetry"/> is
        /// <see langword="null"/>.
        /// </exception>
        public ServerTargetVariableWriter(
            IServerSession session,
            ITelemetryContext telemetry,
            AdapterMetrics? metrics = null)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_metrics = metrics;
            m_logger = telemetry.CreateLogger<ServerTargetVariableWriter>();
        }

        /// <inheritdoc/>
        public async ValueTask<StatusCode> WriteAsync(
            NodeId nodeId,
            uint attributeId,
            string? writeIndexRange,
            DataValue value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!m_session.IsConnected)
                {
                    await m_session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                NodeId targetNodeId = await m_session
                    .ResolveNodeIdAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);

                var writeValue = new WriteValue
                {
                    NodeId = targetNodeId,
                    AttributeId = attributeId,
                    Value = value
                };
                if (!string.IsNullOrEmpty(writeIndexRange))
                {
                    writeValue.IndexRange = writeIndexRange;
                }

                ArrayOf<WriteValue> nodesToWrite = [writeValue];
                ArrayOf<StatusCode> results = await m_session
                    .WriteAsync(nodesToWrite, cancellationToken)
                    .ConfigureAwait(false);

                if (results.IsNull || results.Count == 0)
                {
                    m_metrics?.RecordWrite(false);
                    m_logger.LogInformation(
                        "Write of node {NodeId} returned no status; treating as Bad.",
                        nodeId);
                    return (StatusCode)StatusCodes.BadCommunicationError;
                }
                m_metrics?.RecordWrite(StatusCode.IsGood(results[0]));
                return results[0];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceResultException sre)
            {
                m_metrics?.RecordWrite(false);
                m_logger.LogInformation(
                    sre,
                    "Write of node {NodeId} failed with {StatusCode}; " +
                    "returning Bad status for this field.",
                    nodeId,
                    sre.StatusCode);
                return sre.StatusCode;
            }
            catch (Exception ex)
            {
                m_metrics?.RecordWrite(false);
                m_logger.LogInformation(
                    ex,
                    "Write of node {NodeId} failed; returning Bad status for this field.",
                    nodeId);
                return (StatusCode)StatusCodes.BadCommunicationError;
            }
        }
    }
}
