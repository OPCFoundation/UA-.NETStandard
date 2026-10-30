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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.PubSub.Server.DataChannels
{
    /// <summary>
    /// Resolves actual authorized server flows through the host's sole resource/protocol/carrier accept router.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class ServerDataChannelPubSubFlowProvider : IDataChannelPubSubFlowProvider
    {
        /// <summary>
        /// Borrows the host provider without taking ownership of its contexts, Sessions or other resources.
        /// </summary>
        public ServerDataChannelPubSubFlowProvider(ServerDataChannelProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelPubSubFlow> OpenAsync(
            DataChannelPubSubFlowRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            DataChannelParametersDataType parameters = request.Parameters;
            string resource = parameters.ResourceUri ?? throw new ArgumentException("The flow resource is missing.");
            string protocol = parameters.Protocol ?? throw new ArgumentException("The flow protocol is missing.");
            DataChannel? channel = await m_provider.AcceptAsync(
                resource, protocol, request.CarrierKind, cancellationToken)
                .ConfigureAwait(false);
            if (channel is null)
            {
                throw new DataChannelException(DataChannelResult.Closed);
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new DataChannelPubSubFlow(channel, channel.CarrierKind);
            }
            catch
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private readonly ServerDataChannelProvider m_provider;
    }
}
