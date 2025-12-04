/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
