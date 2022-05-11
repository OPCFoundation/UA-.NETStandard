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

using System;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The transport bindings available for the UA applications.
    /// </summary>
    public static class TransportBindings
    {
        static TransportBindings()
        {
            Channels = new TransportChannelBindings(new Type[] { typeof(TcpTransportChannelFactory) });
            Listeners = new TransportListenerBindings(new Type[] { typeof(TcpTransportListenerFactory) });
        }

        /// <summary>
        /// The bindings for transport channels (client).
        /// </summary>
        public static TransportChannelBindings Channels { get; private set; }

        /// <summary>
        /// The bindings for transport listeners (server).
        /// </summary>
        public static TransportListenerBindings Listeners { get; private set; }
    }

    /// <summary>
    /// The bindings for the transport channels.
    /// </summary>
    public class TransportChannelBindings :
        TransportBindingsBase<ITransportChannelFactory>
    {
        /// <summary>
        /// Initialize the transport listener.
        /// </summary>
        /// <param name="defaultBindings">List of known default bindings.</param>
        public TransportChannelBindings(Type[] defaultBindings) : base(defaultBindings)
        {
        }

        /// <summary>
        /// Get a transport channel for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme of the transport.</param>
        public ITransportChannel GetChannel(string uriScheme)
        {
            var binding = GetBinding(uriScheme);
            return binding?.Create();
        }
    }

    /// <summary>
    /// The bindings for the transport listeners.
    /// </summary>
    public class TransportListenerBindings :
        TransportBindingsBase<ITransportListenerFactory>
    {
        /// <summary>
        /// Initialize the transport listener.
        /// </summary>
        /// <param name="defaultBindings">List of known default bindings.</param>
        public TransportListenerBindings(Type[] defaultBindings) : base(defaultBindings)
        {
        }

        /// <summary>
        /// Get a transport listener for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme of the transport.</param>
        public ITransportListener GetListener(string uriScheme)
        {
            var binding = GetBinding(uriScheme);
            return binding?.Create();
        }
    }
}
