/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Security;
using MQTTnet;
using MQTTnet.Client.ExtendedAuthenticationExchange;
using MQTTnet.Client.Options;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Formatter;
using MQTTnet.Packets;

namespace Opc.Ua.PubSub.Mqtt
{
    public enum EnumMqttProtocolVersion
    {
        Unknown = MqttProtocolVersion.Unknown,
        V310 = MqttProtocolVersion.V310,
        V311 = MqttProtocolVersion.V311,
        V500 = MqttProtocolVersion.V500
    }
    /// <summary>
    /// The implementation of the Mqtt specific client configuration
    /// </summary>
    public class MqttClientProtocolConfiguration : ITransportProtocolConfiguration
    {
        #region Private
        SecureString m_userName;
        SecureString m_password;
        bool m_cleanSession;
        EnumMqttProtocolVersion m_protocolVersion;
        bool m_useSsl;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public MqttClientProtocolConfiguration()
        {
            m_userName = null;
            m_password = null;
            m_cleanSession = true;
            m_protocolVersion = EnumMqttProtocolVersion.V310;
            m_useSsl = false;
        }

        /// <summary>
        /// Constructor 
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <param name="cleanSession"></param>
        /// <param name="version"></param>
        /// <param name="useSsl"></param>
        public MqttClientProtocolConfiguration(SecureString userName = null,
                                               SecureString password = null,
                                               bool cleanSession = true,
                                               EnumMqttProtocolVersion version = EnumMqttProtocolVersion.V310,
                                               bool useSsl = false)
        {
            m_userName = userName;
            m_password = password;
            m_cleanSession = cleanSession;
            m_protocolVersion = version;
            m_useSsl = useSsl;
        }
        #endregion

        #region Public Properties
        public SecureString UserName { get => m_userName; set => m_userName = value; }

        public SecureString Password { get => m_password; set => m_password = value; }

        public bool CleanSession { get => m_cleanSession; set => m_cleanSession = value; }

        public bool UseCredentials { get => m_userName != null; }

        public EnumMqttProtocolVersion ProtocolVersion { get => m_protocolVersion; set => m_protocolVersion = value; }

        public bool UseSSL { get => m_useSsl; set => m_useSsl = value; }

        #endregion
    }
}
