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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.Redaction;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        private async ValueTask<XRegistryResponse> ExecuteAsync(
            ISystemContext context, XRegistryRequest request, CancellationToken ct)
        {
            try
            {
                XRegistryResponse response = await ExecuteCoreAsync(context, request, ct).ConfigureAwait(false);
                AuditResult(context, request.Action, request.Path, response.StatusCode);
                return response;
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception))
            {
                AuditResult(context, request.Action, request.Path, 0);
                throw;
            }
        }

        private void AuditResult(
            ISystemContext context, XRegistryAction action, string path, int status, NodeId methodId = default)
        {
            ILogger logger = Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>();
            XRegistryCallContext caller = XRegistryBridgeNativeOptions.CreateContext(context);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.NativeRegistryAudit(action, Redact.Create(caller.Subject),
                    Redact.Create(m_options.ProjectionContext.Subject), Redact.Create(path), status);
            }
            if (!methodId.IsNull && action is not (XRegistryAction.Read or XRegistryAction.Describe))
            {
                Server.ReportAuditUpdateMethodEvent(context, RegistryNodeId, methodId,
                    [Variant.From(action.ToString()), Variant.From(Redact.Create(path).ToString())],
                    "xRegistry transaction",
                        status is >= 200 and < 300 ? StatusCodes.Good : StatusCodes.BadUnexpectedError,
                    logger);
            }
        }
    }

    internal static partial class XRegistryBridgeNodeManagerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 8, Level = LogLevel.Information,
            Message =
                "xRegistry native audit: {Action} caller={Caller} upstream={Upstream} target={Target} status={Status}. "
                + "Zero status denotes an unconfirmed outcome; payloads and credentials are omitted.")]
        public static partial void NativeRegistryAudit(
            this ILogger logger, XRegistryAction action, RedactionWrapper<string> caller,
            RedactionWrapper<string> upstream, RedactionWrapper<string> target, int status);
    }
}
