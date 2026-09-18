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

        private WotNotification BuildCapturedEventNotification(
            WotEventSelection selection,
            EventFieldList fields,
            IServiceMessageContext sourceContext)
        {
            if (m_eventSource is not { } source)
            {
                return BuildEventNotification(selection, fields, sourceContext);
            }
            try
            {
                WotCapturedEvent captured = WotCapturedEvent.Capture(
                    source, selection.Clauses, fields.EventFields);
                return BuildEventNotification(selection, fields, source.Context).WithCapturedEvent(captured);
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
