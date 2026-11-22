// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
