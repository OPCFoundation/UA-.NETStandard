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

#nullable enable

using Opc.Ua.Bindings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Stub to avoid null references
    /// </summary>
    internal sealed class NullChannel : ITransportChannel, ISecureChannel
    {
        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures
            => throw Unexpected(nameof(SupportedFeatures));

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => throw Unexpected(nameof(EndpointDescription));

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => throw Unexpected(nameof(EndpointConfiguration));

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => throw Unexpected(nameof(MessageContext));

        /// <inheritdoc/>
        public byte[] SecureChannelHash
            => throw Unexpected(nameof(SecureChannelHash));

        /// <inheritdoc/>
        public byte[] SessionActivationSecret
            => throw Unexpected(nameof(SessionActivationSecret));

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate
            => throw Unexpected(nameof(ClientChannelCertificate));

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate
            => throw Unexpected(nameof(ServerChannelCertificate));

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add => throw Unexpected(nameof(OnTokenActivated));
            remove => throw Unexpected(nameof(OnTokenActivated));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do nothing
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct)
        {
            throw Unexpected(nameof(CloseAsync));
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            throw Unexpected(nameof(OpenAsync));
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            throw Unexpected(nameof(OpenAsync));
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct)
        {
            throw Unexpected(nameof(ReconnectAsync));
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request,
            CancellationToken ct)
        {
            throw Unexpected(nameof(SendRequestAsync));
        }

        /// <summary>
        /// Throw exception
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static ServiceResultException Unexpected(string name)
        {
#if DEBUG_CHECK
            System.Diagnostics.Debug.Fail(name + " called in unexpected state");
#endif
            return ServiceResultException.Unexpected($"{name} called in unexpected state");
        }
    }
}
