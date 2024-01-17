/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client.Tests
{
    #region Namespace Declarations
    /// <remarks />
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUaClient namespace (.NET code namespace is 'Opc.Ua.Client').
        /// </summary>
        public const string OpcUaClient = "http://opcfoundation.org/UA/Client/Types.xsd";
    }
    #endregion

    /// <summary>
    /// A subclass of a session for testing purposes, e.g. to override some implementations.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaClient)]
    [KnownType(typeof(TestableSubscription))]
    [KnownType(typeof(TestableMonitoredItem))]
    public class TestableSession : Session
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the <see cref="Session"/> class.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint use to initialize the channel.</param>
        public TestableSession(
            ISessionChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint)
        :
            this(channel as ITransportChannel, configuration, endpoint, null)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="ISession"/> class.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint used to initialize the channel.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in GetEndpoints() request.</param>
        /// <remarks>
        /// The application configuration is used to look up the certificate if none is provided.
        /// The clientCertificate must have the private key. This will require that the certificate
        /// be loaded from a certicate store. Converting a DER encoded blob to a X509Certificate2
        /// will not include a private key.
        /// The <i>availableEndpoints</i> and <i>discoveryProfileUris</i> parameters are used to validate
        /// that the list of EndpointDescriptions returned at GetEndpoints matches the list returned at CreateSession.
        /// </remarks>
        public TestableSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
            : base(channel, configuration, endpoint, clientCertificate, availableEndpoints, discoveryProfileUris)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ISession"/> class.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="template">The template session.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public TestableSession(ITransportChannel channel, Session template, bool copyEventHandlers)
        :
            base(channel, template, copyEventHandlers)
        {
        }
        #endregion

        /// <summary>
        /// The timespan offset to be used to modify the request header timestamp.
        /// </summary>
        [DataMember]
        public TimeSpan TimestampOffset { get; set; } = new TimeSpan(0);

        /// <inheritdoc/>
        protected override void UpdateRequestHeader(IServiceRequest request, bool useDefaults, string serviceName)
        {
            base.UpdateRequestHeader(request, useDefaults, serviceName);
            request.RequestHeader.Timestamp = request.RequestHeader.Timestamp + TimestampOffset;
        }

        /// <inheritdoc/>
        public override Session CloneSession(ITransportChannel channel, bool copyEventHandlers)
        {
            return new TestableSession(channel, this, copyEventHandlers) {
                TimestampOffset = this.TimestampOffset,
            };
        }
    }

    /// <summary>
    /// A subclass of the subscription for testing purposes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaClient)]
    [KnownType(typeof(TestableMonitoredItem))]
    public class TestableSubscription : Subscription
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the <see cref="TestableSubscription"/> class.
        /// </summary>
        public TestableSubscription()
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableSubscription"/> class.
        /// </summary>
        public TestableSubscription(Subscription template)
            : this (template, false)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableSubscription"/> class.
        /// </summary>
        public TestableSubscription(Subscription template, bool copyEventHandlers)
            : base(template, copyEventHandlers)
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        protected new void Initialize(StreamingContext context)
        {
            base.Initialize(context);
            Initialize();
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        /// <inheritdoc/>
        public override Subscription CloneSubscription(bool copyEventHandlers)
        {
            return new TestableSubscription(this, copyEventHandlers);
        }
    }

    /// <summary>
    /// A subclass of a monitored item for testing purposes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaClient)]
    [KnownType(typeof(TestableMonitoredItem))]
    public class TestableMonitoredItem : MonitoredItem
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem()
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem(MonitoredItem template)
            : this(template, false, false)
        {

        }

        /// <summary>
        /// Constructs a new instance of the <see cref="TestableMonitoredItem"/> class.
        /// </summary>
        public TestableMonitoredItem(MonitoredItem template, bool copyEventHandlers, bool copyClientHandle)
            : base(template, copyEventHandlers, copyClientHandle)
        {
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        protected new void Initialize(StreamingContext context)
        {
            base.Initialize(context);
            Initialize();
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        /// <inheritdoc/>
        public override MonitoredItem CloneMonitoredItem(bool copyEventHandlers, bool copyClientHandle)
        {
            return new TestableMonitoredItem(this, copyEventHandlers, copyClientHandle);
        }
    }

}
