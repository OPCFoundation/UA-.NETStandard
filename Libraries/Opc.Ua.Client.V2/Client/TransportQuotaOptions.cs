// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Opc.Ua.Bindings;
    using System;

    /// <summary>
    /// Transport quota configuration
    /// </summary>
    public sealed record class TransportQuotaOptions
    {
        /// <summary>
        /// Max array length
        /// </summary>
        public int MaxArrayLength { get; init; }
            = DefaultEncodingLimits.MaxArrayLength;

        /// <summary>
        /// Max string length
        /// </summary>
        public int MaxByteStringLength { get; init; }
            = DefaultEncodingLimits.MaxByteStringLength;

        /// <summary>
        /// Max message size
        /// </summary>
        public int MaxMessageSize { get; init; }
            = DefaultEncodingLimits.MaxMessageSize;

        /// <summary>
        /// Max string length
        /// </summary>
        public int MaxStringLength { get; init; }
            = DefaultEncodingLimits.MaxStringLength;

        /// <summary>
        /// Max buffer size
        /// </summary>
        public int MaxBufferSize { get; init; }
            = TcpMessageLimits.MaxBufferSize;

        /// <summary>
        /// Operation timeout in milliseconds.
        /// </summary>
        public TimeSpan OperationTimeout { get; init; }
            = TimeSpan.FromMilliseconds(TcpMessageLimits.DefaultOperationTimeout);

        /// <summary>
        /// Channel lifetime in milliseconds.
        /// </summary>
        public TimeSpan ChannelLifetime { get; init; }
            = TimeSpan.FromMilliseconds(TcpMessageLimits.DefaultChannelLifetime);

        /// <summary>
        /// Security token lifetime in milliseconds.
        /// </summary>
        public TimeSpan SecurityTokenLifetime { get; init; }
            = TimeSpan.FromMilliseconds(TcpMessageLimits.DefaultSecurityTokenLifeTime);
    }
}
