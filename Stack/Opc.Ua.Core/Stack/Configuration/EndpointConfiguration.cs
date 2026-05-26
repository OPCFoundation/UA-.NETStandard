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
