// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public static class EndpointConfigurationExtensions
    {
        extension(EndpointConfiguration)
        {
            /// <summary>
            /// Creates an instance of a configuration with reasonable default values.
            /// </summary>
            public static EndpointConfiguration Create()
            {
                return new EndpointConfiguration
                {
                    // message defaults
                    OperationTimeout = TcpMessageLimits.DefaultOperationTimeout,
                    UseBinaryEncoding = true,
                    MaxMessageSize = TcpMessageLimits.DefaultMaxMessageSize,
                    MaxBufferSize = TcpMessageLimits.DefaultMaxBufferSize,
                    ChannelLifetime = TcpMessageLimits.DefaultChannelLifetime,
                    SecurityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime,

                    // encoding defaults
                    MaxArrayLength = DefaultEncodingLimits.MaxArrayLength,
                    MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength,
                    MaxStringLength = DefaultEncodingLimits.MaxStringLength,
                    MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels,
                    MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries
                };
            }

            /// <summary>
            /// Creates an instance of a configuration with reasonable default values.
            /// </summary>
            public static EndpointConfiguration Create(
                ApplicationConfiguration applicationConfiguration)
            {
                if (applicationConfiguration == null ||
                    applicationConfiguration.TransportQuotas == null)
                {
                    return Create();
                }

                return new EndpointConfiguration
                {
                    OperationTimeout = applicationConfiguration.TransportQuotas.OperationTimeout,
                    UseBinaryEncoding = true,
                    MaxArrayLength = applicationConfiguration.TransportQuotas.MaxArrayLength,
                    MaxByteStringLength = applicationConfiguration.TransportQuotas.MaxByteStringLength,
                    MaxMessageSize = applicationConfiguration.TransportQuotas.MaxMessageSize,
                    MaxStringLength = applicationConfiguration.TransportQuotas.MaxStringLength,
                    MaxBufferSize = applicationConfiguration.TransportQuotas.MaxBufferSize,
                    MaxEncodingNestingLevels = applicationConfiguration.TransportQuotas
                        .MaxEncodingNestingLevels,
                    MaxDecoderRecoveries = applicationConfiguration.TransportQuotas
                        .MaxDecoderRecoveries,
                    ChannelLifetime = applicationConfiguration.TransportQuotas.ChannelLifetime,
                    SecurityTokenLifetime = applicationConfiguration.TransportQuotas
                        .SecurityTokenLifetime
                };
            }
        }
    }
}
