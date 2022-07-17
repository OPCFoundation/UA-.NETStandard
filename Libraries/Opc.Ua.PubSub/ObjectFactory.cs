/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Implementation of Factory pattern - Used to create objects depending on used protocol
    /// </summary>
    internal static class ObjectFactory
    {
        /// <summary>
        /// Create connections from PubSubConnectionDataType configuration objects.
        /// </summary>
        /// <param name="uaPubSubApplication">The parent <see cref="UaPubSubApplication"/></param>
        /// <param name="pubSubConnectionDataType">The configuration object for the new <see cref="UaPubSubConnection"/></param>
        /// <returns>The new instance of <see cref="UaPubSubConnection"/>.</returns>
        public static UaPubSubConnection CreateConnection(UaPubSubApplication uaPubSubApplication, PubSubConnectionDataType pubSubConnectionDataType)
        {
            if (pubSubConnectionDataType.TransportProfileUri == Profiles.PubSubUdpUadpTransport)
            {
                return new UdpPubSubConnection(uaPubSubApplication, pubSubConnectionDataType);
            }
            else if (pubSubConnectionDataType.TransportProfileUri == Profiles.PubSubMqttUadpTransport)
            {
                return new MqttPubSubConnection(uaPubSubApplication, pubSubConnectionDataType, MessageMapping.Uadp);
            }
            else if (pubSubConnectionDataType.TransportProfileUri == Profiles.PubSubMqttJsonTransport)
            {
                return new MqttPubSubConnection(uaPubSubApplication, pubSubConnectionDataType, MessageMapping.Json);
            }
            throw new ArgumentException("Invalid TransportProfileUri.", nameof(pubSubConnectionDataType));
        }
    }
}
