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
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Server.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Connects PubSub to a server's registered real accepted-flow router.
    /// </summary>
    public static class ServerDataChannelPubSubBuilderExtensions
    {
        /// <summary>
        /// Adds a carrier factory backed by the host's ServerDataChannelProvider.
        /// Source policies and carrier listeners must be registered independently.
        /// </summary>
        public static IPubSubBuilder AddServerDataChannelTransport(
            this IPubSubBuilder builder,
            DataChannelCarrierKind carrierKind)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddDataChannelTransport(carrierKind, provider =>
                new ServerDataChannelPubSubFlowProvider(provider.GetRequiredService<ServerDataChannelProvider>()));
        }
    }
}
