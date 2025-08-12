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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The binding for the UA native stack
    /// </summary>
    public abstract class BaseBinding
    {
        /// <summary>
        /// Initializes the binding.
        /// </summary>
        protected BaseBinding(
            NamespaceTable namespaceUris,
            IEncodeableFactory factory,
            EndpointConfiguration configuration
        )
        {
            MessageContext = new ServiceMessageContext
            {
                MaxStringLength = configuration.MaxStringLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxArrayLength = configuration.MaxArrayLength,
                MaxMessageSize = configuration.MaxMessageSize,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                Factory = factory,
                NamespaceUris = namespaceUris
            };
        }

        /// <summary>
        /// The message context to use with the binding.
        /// </summary>
        public IServiceMessageContext MessageContext { get; set; }
    }
}
