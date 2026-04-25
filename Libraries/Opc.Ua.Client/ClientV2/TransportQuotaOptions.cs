#if OPCUA_CLIENT_V2
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
#endif
