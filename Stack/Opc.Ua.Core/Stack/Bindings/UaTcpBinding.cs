/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.ServiceModel.Channels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A dummy binding for the UA-TCP .NET implementation. 
    /// </summary>
    public class UaTcpBinding : BaseBinding
    {
        #region Constructors
        /// <summary>
        /// Initializes the binding.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <param name="factory">The factory.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="descriptions">The descriptions.</param>
        public UaTcpBinding(
            NamespaceTable               namespaceUris,
            EncodeableFactory            factory,
            EndpointConfiguration        configuration,
            params EndpointDescription[] descriptions)
        :
            base(namespaceUris, factory, configuration)
        {
            m_transport = new TcpTransportBindingElement();
            m_transport.MaxReceivedMessageSize = configuration.MaxMessageSize;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// The URL scheme for the UA TCP protocol.
        /// </summary>
        /// <value></value>
        /// <returns>The URI scheme that is used by the channels or listeners that are created by the factories built by the current binding.</returns>
        public override string Scheme 
        {
            get { return Utils.UriSchemeOpcTcp; } 
        }

        /// <summary>
        /// Create the set of binding elements that make up this binding.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.ICollection`1"/> object of type <see cref="T:System.ServiceModel.Channels.BindingElement"/> that contains the binding elements from the current binding object in the correct order.
        /// </returns>
        public override BindingElementCollection CreateBindingElements()
        {
            BindingElementCollection elements = new BindingElementCollection();

            elements.Add(m_transport);

            return elements.Clone();
        }
        #endregion

        #region Private Fields
        private TcpTransportBindingElement m_transport;
        #endregion
    }
}
