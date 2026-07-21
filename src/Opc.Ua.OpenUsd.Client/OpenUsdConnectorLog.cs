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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Source-generated log messages for the <see cref="OpenUsdConnector"/>.
    /// </summary>
    internal static partial class OpenUsdConnectorLog
    {
        [LoggerMessage(EventId = OpenUsdEventIds.Connector + 0, Level = LogLevel.Debug,
            Message = "Composed {Count} component(s) for '{TargetPrimPath}'.")]
        public static partial void ComponentsResolved(this ILogger logger, int count, string targetPrimPath);

        [LoggerMessage(EventId = OpenUsdEventIds.Connector + 1, Level = LogLevel.Debug,
            Message = "Subscribed to OpenUSD model-change events on {EventSource}.")]
        public static partial void ModelChangeSubscribed(this ILogger logger, NodeId eventSource);

        [LoggerMessage(EventId = OpenUsdEventIds.Connector + 2, Level = LogLevel.Error,
            Message = "OpenUSD recompose failed; the next model-change event will retry.")]
        public static partial void RecomposeFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = OpenUsdEventIds.Connector + 3, Level = LogLevel.Information,
            Message = "Federated cross-server component to {EndpointUrl}.")]
        public static partial void CrossServerFederated(this ILogger logger, string endpointUrl);

        [LoggerMessage(EventId = OpenUsdEventIds.Connector + 4, Level = LogLevel.Warning,
            Message = "Closing connector-owned remote session failed.")]
        public static partial void RemoteSessionCloseFailed(this ILogger logger, Exception exception);
    }
}
