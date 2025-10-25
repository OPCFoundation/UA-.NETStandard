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
            Channels = new TransportChannelBindings(
                [
                    typeof(TcpTransportChannelFactory),
                    typeof(HttpsTransportChannelFactory),
                    typeof(OpcHttpsTransportChannelFactory)
                ]);
            Listeners = new TransportListenerBindings(
                [
                    typeof(TcpTransportListenerFactory)
                ]);
        }

        /// <summary>
        /// The bindings for transport channels (client).
        /// </summary>
        public static ITransportChannelBindings Channels { get; }

        /// <summary>
        /// The bindings for transport listeners (server).
        /// </summary>
        public static ITransportListenerBindings Listeners { get; }
    }

    /// <summary>
    /// Transport channel binding registry
    /// </summary>
    public interface ITransportChannelBindings
    {
        /// <summary>
        /// Create a channel for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        ITransportChannel Create(string uriScheme, ITelemetryContext telemetry);
    }

    /// <summary>
    /// Transport listener binding registry.
    /// </summary>
    public interface ITransportListenerBindings : ITransportBindings<ITransportListenerFactory>
    {
        /// <summary>
        /// Create listener for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        ITransportListener Create(string uriScheme, ITelemetryContext telemetry);
    }

    /// <summary>
    /// The bindings for the transport channels.
    /// </summary>
    internal class TransportChannelBindings : TransportBindingsBase<ITransportChannelFactory>,
        ITransportChannelBindings
    {
        /// <summary>
        /// Initialize the transport listener.
        /// </summary>
        /// <param name="defaultBindings">List of known default bindings.</param>
        public TransportChannelBindings(params Type[] defaultBindings)
            : base(defaultBindings)
        {
        }

        /// <summary>
        /// Get a transport channel for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        public ITransportChannel Create(string uriScheme, ITelemetryContext telemetry)
        {
            ITransportChannelFactory binding = GetBinding(uriScheme, telemetry);
            return binding?.Create(telemetry);
        }
    }

    /// <summary>
    /// The bindings for the transport listeners.
    /// </summary>
    internal class TransportListenerBindings : TransportBindingsBase<ITransportListenerFactory>,
        ITransportListenerBindings
    {
        /// <summary>
        /// Initialize the transport listener.
        /// </summary>
        /// <param name="defaultBindings">List of known default bindings.</param>
        public TransportListenerBindings(params Type[] defaultBindings)
            : base(defaultBindings)
        {
        }

        /// <summary>
        /// Get a transport listener for the specified uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        public ITransportListener Create(string uriScheme, ITelemetryContext telemetry)
        {
            ITransportListenerFactory binding = GetBinding(uriScheme, telemetry);
            return binding?.Create(telemetry);
        }
    }
}
