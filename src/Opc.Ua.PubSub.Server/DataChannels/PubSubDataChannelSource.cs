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
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.PubSub.Server.DataChannels
{
    /// <summary>
    /// Projects one configured PubSub flow into the server's existing independently authorized resource model.
    /// </summary>
    public static class PubSubDataChannelSource
    {
        /// <summary>
        /// Creates an exact source declaration without enabling a Session model or broadening resource permissions.
        /// Register it with DataChannelSourceRegistry before accepting connections.
        /// </summary>
        public static DataChannelSource Create(
            PubSubConnectionDataType connection,
            IDataChannelSourcePolicy policy,
            int maximumFlows = 16,
            ExpandedNodeId sourceNodeId = default,
            ArrayOf<ExpandedNodeId> contentNodeIds = default)
        {
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (maximumFlows is < 1 or > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFlows));
            }
            DataChannelParametersDataType parameters = DataChannelPubSubConfiguration.GetParameters(connection);
            string resource = parameters.ResourceUri ?? throw new ArgumentException("The flow resource is missing.");
            string protocol = parameters.Protocol ?? throw new ArgumentException("The flow protocol is missing.");
            string contentType = parameters.ContentType ??
                throw new ArgumentException("The flow content type is missing.");
            return new DataChannelSource(resource, policy)
            {
                SourceNodeId = sourceNodeId,
                ContentNodeIds = contentNodeIds.IsNull ? [] : new ArrayOf<ExpandedNodeId>(contentNodeIds.ToArray()),
                SupportedProtocols = [protocol],
                SupportedDirections = [parameters.Direction],
                SupportedDeliveryModes = [parameters.Delivery],
                SupportedProtectionModes = [parameters.Protection],
                ContentType = contentType,
                MaxMessageSize = parameters.MaxMessageSize,
                MaxReceiveBytes = parameters.MaxReceiveBytes,
                MaxRate = parameters.MaxRate,
                MaxFlows = maximumFlows,
                AllowQueueExpiry = parameters.AllowQueueExpiry
            };
        }
    }
}
