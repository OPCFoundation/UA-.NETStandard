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
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    internal sealed partial class OpcUaWotBindingChannel
    {
        internal static async ValueTask<WotEventSource?> CaptureEventSourceAsync(
            ISession session,
            WotCompiledForm form,
            WotExecutorContext context,
            CancellationToken cancellationToken)
        {
            if (form.Operation is not (WoTBindingCapabilityEnum.SubscribeEvent or
                    WoTBindingCapabilityEnum.InvokeAction) ||
                session is not ISessionBindingProvider provider)
            {
                return null;
            }
            try
            {
                ISessionClient client = await provider.CreateBindingAsync(cancellationToken).ConfigureAwait(false);
                if (client is not ISessionBinding binding)
                {
                    client?.Dispose();
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "The source cannot expose captured binding validity.");
                }
                try
                {
                    return new WotEventSource(binding);
                }
                catch
                {
                    binding.Dispose();
                    throw;
                }
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadNotSupported)
            {
                context.Telemetry.CreateLogger<OpcUaWotBindingChannel>()
                    .EventSourceCaptureUnavailable(exception, form.JsonPointer);
                return null;
            }
        }

        internal static ArrayOf<int> AppendRequiredEventFields(
            EventFilter filter, ArrayOf<Wot.WotResolvedEventSelectClause> requiredSelectClauses)
        {
            var indexes = new int[requiredSelectClauses.Count];
            for (int index = 0; index < indexes.Length; index++)
            {
                Wot.WotResolvedEventSelectClause clause = requiredSelectClauses[index];
                var operand = new SimpleAttributeOperand
                {
                    TypeDefinitionId = NodeId.Parse(clause.TypeDefinitionId),
                    AttributeId = clause.IsConditionIdSelection ? Attributes.NodeId : Attributes.Value,
                    BrowsePath = clause.PathElements.ConvertAll(QualifiedName.From)
                };
                int match = -1;
                for (int candidate = 0; candidate < filter.SelectClauses.Count; candidate++)
                {
                    SimpleAttributeOperand existing = filter.SelectClauses[candidate];
                    if (existing.TypeDefinitionId == operand.TypeDefinitionId &&
                        existing.AttributeId == operand.AttributeId && existing.IndexRange == operand.IndexRange &&
                        existing.BrowsePath.Span.SequenceEqual(operand.BrowsePath.Span))
                    {
                        match = candidate;
                        break;
                    }
                }
                if (match < 0)
                {
                    match = filter.SelectClauses.Count;
                    filter.SelectClauses = filter.SelectClauses.AddItem(operand);
                }
                indexes[index] = match;
            }
            return indexes;
        }

        private WotNotification BuildCapturedEventNotification(
            WotEventSelection selection,
            ArrayOf<Wot.WotResolvedEventSelectClause> captureClauses,
            ArrayOf<int> captureIndexes,
            int fieldCount,
            EventFieldList fields,
            IServiceMessageContext sourceContext)
        {
            if (m_eventSource is not { } source)
            {
                return BuildEventNotification(selection, fields, sourceContext);
            }
            try
            {
                if (fields.EventFields.Count != fieldCount)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "The source event does not match its captured selection.");
                }
                WotCapturedEvent captured = WotCapturedEvent.Capture(
                    source, captureClauses, captureIndexes.ConvertAll(index => fields.EventFields[index]));
                return BuildEventNotification(selection, fields, source.Context, captured).WithCapturedEvent(captured);
            }
            catch (ServiceResultException exception)
            {
                m_logger.EventSourceCaptureInvalidated(exception, Form.JsonPointer);
                return new WotNotification(DataValue.FromStatusCode(exception.StatusCode));
            }
        }
    }

    internal static partial class OpcUaWotBindingChannelLog
    {
        [LoggerMessage(
            EventId = WotConBindingsEventIds.OpcUaWotBindingChannel + 1,
            Level = LogLevel.Warning,
            Message = "Authenticated occurrence capture is unavailable for {Affordance}; " +
                "transparent admission and captured occurrence actions are unavailable.")]
        public static partial void EventSourceCaptureUnavailable(
            this ILogger logger, Exception exception, string affordance);

        [LoggerMessage(
            EventId = WotConBindingsEventIds.OpcUaWotBindingChannel + 2,
            Level = LogLevel.Error,
            Message = "The captured source of event affordance {Affordance} is invalid; " +
                "the notification reports its failure status.")]
        public static partial void EventSourceCaptureInvalidated(
            this ILogger logger, Exception exception, string affordance);
    }
}
