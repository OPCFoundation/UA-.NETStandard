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

namespace Opc.Ua
{

    /// <summary>
    /// Source-generated log messages for shared client diagnostics
    /// </summary>
    internal static partial class ClientLog
    {
        [LoggerMessage(EventId = ClientEventIds.Client + 0, Level = LogLevel.Warning,
            Message = "Transfer subscription unsupported, TransferSubscriptionsOnReconnect set to false.")]
        public static partial void TransferSubscriptionUnsupportedTransferSubscriptionsOnReconnectSetFalse(
            this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.Client + 1, Level = LogLevel.Error,
            Message = "{ChannelType} OPC UA certificate validator rejected server certificate.")]
        public static partial void ChannelTypeOPCUACertificateValidatorRejected(
            this ILogger logger,
            string channelType);

        [LoggerMessage(EventId = ClientEventIds.Client + 2, Level = LogLevel.Error,
            Message = "{ChannelType} No certificate validator configured and TLS reported {Errors}; rejecting" +
                " server certificate.")]
        public static partial void ChannelTypeNoCertificateValidatorConfiguredTLS(
            this ILogger logger,
            string channelType,
            System.Net.Security.SslPolicyErrors errors);

        [LoggerMessage(EventId = ClientEventIds.Client + 3, Level = LogLevel.Error,
            Message = "{ChannelType} Failed to validate server certificate.")]
        public static partial void ChannelTypeFailedValidateServerCertificate(
            this ILogger logger,
            Exception? exception,
            string channelType);
    }
}
