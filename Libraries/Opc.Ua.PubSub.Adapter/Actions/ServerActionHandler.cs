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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Actions
{
    /// <summary>
    /// <see cref="IPubSubActionHandler"/> that maps an inbound PubSub Action
    /// request to an OPC UA method call on an external server. The action
    /// target is resolved to an external object/method pair through the
    /// supplied <see cref="ActionMethodMap"/>; the action input fields
    /// become the method's input arguments, in order, and the method's output
    /// arguments are mapped back to named response fields.
    /// </summary>
    /// <remarks>
    /// Handling is fail-soft: an unmapped target, a connection failure, or a
    /// call fault is mapped to a Bad <see cref="StatusCode"/> with empty output
    /// fields and logged. The handler never throws for such faults; only
    /// cancellation is propagated.
    /// </remarks>
    public sealed class ServerActionHandler : IPubSubActionHandler
    {
        private readonly IServerSession m_session;
        private readonly ActionMethodMap m_methodMap;
        private readonly AdapterMetrics? m_metrics;
        private readonly ILogger m_logger;

        /// <summary>
        /// Creates a new external-server action handler.
        /// </summary>
        /// <param name="session">
        /// The external server session used to issue the method call.
        /// </param>
        /// <param name="methodMap">
        /// The map that resolves an action target to the external object and
        /// method to call.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        /// <param name="metrics">
        /// Optional metrics sink that records method-call activity.
        /// </param>
        public ServerActionHandler(
            IServerSession session,
            ActionMethodMap methodMap,
            ITelemetryContext telemetry,
            AdapterMetrics? metrics = null)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_methodMap = methodMap ?? throw new ArgumentNullException(nameof(methodMap));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_metrics = metrics;
            m_logger = telemetry.CreateLogger<ServerActionHandler>();
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubActionHandlerResult> HandleAsync(
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (invocation is null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            if (!m_methodMap.TryResolve(invocation.Target, out ActionMethodBinding binding))
            {
                m_logger.LogInformation(
                    "No external method mapping for action target " +
                    "(DataSetWriterId={DataSetWriterId}, ActionTargetId={ActionTargetId}, " +
                    "ActionName={ActionName}); returning BadNodeIdUnknown.",
                    invocation.Target.DataSetWriterId,
                    invocation.Target.ActionTargetId,
                    invocation.Target.ActionName);
                return new PubSubActionHandlerResult
                {
                    StatusCode = StatusCodes.BadNodeIdUnknown
                };
            }

            try
            {
                if (!m_session.IsConnected)
                {
                    await m_session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                ArrayOf<Variant> inputArguments = MapInputArguments(invocation.InputFields);

                NodeId objectId = await m_session
                    .ResolveNodeIdAsync(binding.ObjectId, cancellationToken)
                    .ConfigureAwait(false);
                NodeId methodId = await m_session
                    .ResolveNodeIdAsync(binding.MethodId, cancellationToken)
                    .ConfigureAwait(false);

                RemoteCallResult result = await m_session.CallAsync(
                    objectId,
                    methodId,
                    inputArguments,
                    cancellationToken).ConfigureAwait(false);

                m_metrics?.RecordCall(StatusCode.IsGood(result.Status));
                return new PubSubActionHandlerResult
                {
                    StatusCode = result.Status,
                    OutputFields = MapOutputFields(result.OutputArguments, binding.OutputFieldNames)
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_metrics?.RecordCall(false);
                m_logger.LogInformation(ex,
                    "External method call failed for action target " +
                    "(DataSetWriterId={DataSetWriterId}, ActionTargetId={ActionTargetId}); " +
                    "returning BadUnexpectedError.",
                    invocation.Target.DataSetWriterId,
                    invocation.Target.ActionTargetId);
                return new PubSubActionHandlerResult
                {
                    StatusCode = StatusCodes.BadUnexpectedError
                };
            }
        }

        private static ArrayOf<Variant> MapInputArguments(ArrayOf<DataSetField> inputFields)
        {
            if (inputFields.IsNull || inputFields.Count == 0)
            {
                return [];
            }
            var arguments = new Variant[inputFields.Count];
            for (int i = 0; i < inputFields.Count; i++)
            {
                DataSetField field = inputFields[i];
                arguments[i] = field is null ? Variant.Null : field.Value;
            }
            return arguments;
        }

        private static ArrayOf<DataSetField> MapOutputFields(
            ArrayOf<Variant> outputArguments,
            ArrayOf<string> outputFieldNames)
        {
            if (outputArguments.IsNull || outputArguments.Count == 0)
            {
                return [];
            }
            bool hasNames = !outputFieldNames.IsNull && outputFieldNames.Count > 0;
            var fields = new DataSetField[outputArguments.Count];
            for (int i = 0; i < outputArguments.Count; i++)
            {
                string name = hasNames &&
                    i < outputFieldNames.Count &&
                    !string.IsNullOrEmpty(outputFieldNames[i])
                    ? outputFieldNames[i]
                    : $"Output{i.ToString(CultureInfo.InvariantCulture)}";
                fields[i] = new DataSetField
                {
                    Name = name,
                    Value = outputArguments[i]
                };
            }
            return fields;
        }
    }
}
