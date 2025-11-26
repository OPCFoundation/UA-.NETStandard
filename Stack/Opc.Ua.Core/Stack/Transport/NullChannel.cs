/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
