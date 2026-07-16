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
using System.Collections.Generic;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configuration settings for a channel.
    /// </summary>
    public class TransportListenerSettings
    {
        /// <summary>
        /// Gets or sets the descriptions for the endpoints supported by the listener.
        /// </summary>
        public List<EndpointDescription>? Descriptions { get; set; }

        /// <summary>
        /// Gets or sets the configuration for the endpoints.
        /// </summary>
        public EndpointConfiguration? Configuration { get; set; }

        /// <summary>
        /// Gets or sets the registry that exposes the server's instance
        /// certificates and chain blobs.
        /// </summary>
        public ICertificateRegistry? ServerCertificates { get; set; }

        /// <summary>
        /// Gets or Sets the certificate validator.
        /// </summary>
        /// <remarks>
        /// This is the object used by the channel to validate received certificates.
        /// Validatation errors are reported to the application via this object.
        /// </remarks>
        public ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// Gets or sets a reference to the table of namespaces for the server.
        /// </summary>
        /// <remarks>
        /// This is a thread safe object that may be updated by the application at any time.
        /// This table is used to lookup the NamespaceURI for the DataTypeEncodingId when decoding ExtensionObjects.
        /// If the NamespaceURI can be found the decoder will use the Factory to create an instance of a .NET object.
        /// The raw data is passed to application if the NamespaceURI cannot be found or there is no .NET class
        /// associated with the DataTypeEncodingId then.
        /// </remarks>
        /// <seealso cref="Factory" />
        public NamespaceTable? NamespaceUris { get; set; }

        /// <summary>
        /// Gets or sets the table of known encodeable objects.
        /// </summary>
        /// <remarks>
        /// This is a thread safe object that may be updated by the application at any time.
        /// This is a table of .NET types indexed by their DataTypeEncodingId.
        /// The decoder uses this table to automatically create the appropriate .NET objects when it
        /// encounters an ExtensionObject in the message being decoded.
        /// The table uses DataTypeEncodingIds with the URI explicitly specified so multiple channels
        /// with different servers can share the same table.
        /// The NamespaceUris table is used to lookup the NamespaceURI from the NamespaceIndex provide
        /// in the encoded message.
        /// </remarks>
        /// <seealso cref="NamespaceUris" />
        public IEncodeableFactory? Factory { get; set; }

        /// <summary>
        /// Indicates if the transport listener is used as an endpoint for a reverse connection.
        /// </summary>
        public bool ReverseConnectListener { get; set; }

        /// <summary>
        /// Indicates the max number of channels that can be created by the listener.
        /// 0 indicates no limit.
        /// </summary>
        public int MaxChannelCount { get; set; }

        /// <summary>
        /// The size of the listener socket's pending-connection backlog.
        /// 0 selects the transport default. A larger backlog absorbs bursts of
        /// simultaneous connects so they are not dropped by the OS before the
        /// accept loop can service them.
        /// </summary>
        public int ListenBacklog { get; set; }

        /// <summary>
        /// Optional connection admission rate limiter consulted for each inbound
        /// connection before a channel is created. When <c>null</c>, no
        /// connection rate limiting is applied.
        /// </summary>
        public IConnectionRateLimiter? ConnectionRateLimiter { get; set; }

        /// <summary>
        /// Indicates if Http listener requires mutual TLS
        /// Handled only by HttpsTransportListener
        /// In case true, the client should provide it's own valid TLS certificate to the TLS layer for the connection to succeed.
        /// </summary>
        public bool HttpsMutualTls { get; set; }

        /// <summary>
        /// Optional decorator applied to the byte transport of every channel
        /// accepted by the listener. When set, the raw transport is passed
        /// through this function and the returned transport is used instead,
        /// allowing an in-process diagnostic tap (for example the OPC UA Pcap
        /// capture binding) to observe wire-level chunks. When <c>null</c>
        /// (the default) the accepted transport is used unchanged and there is
        /// no runtime cost.
        /// </summary>
        /// <remarks>
        /// Honored by the <c>opc.tcp</c> listener; other transports ignore it.
        /// </remarks>
        public Func<IUaSCByteTransport, IUaSCByteTransport>? AcceptedTransportDecorator { get; set; }

        /// <summary>
        /// Optional callback invoked once for every channel accepted by the
        /// listener, immediately before the channel starts processing wire
        /// messages. A diagnostic binding uses it to subscribe to the
        /// channel's key-material notifications
        /// (<see cref="TcpListenerChannel.OnTokenActivated"/>). When <c>null</c>
        /// (the default) no callback is invoked.
        /// </summary>
        /// <remarks>
        /// ⚠️ Subscribing to the accepted channel grants access to symmetric
        /// channel keys; see the remarks on
        /// <see cref="TcpListenerChannel.OnTokenActivated"/>. Honored by the
        /// <c>opc.tcp</c> listener; other transports ignore it.
        /// </remarks>
        public Action<TcpListenerChannel>? OnAcceptedChannel { get; set; }
    }
}
