#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Subscriptions;
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Configuration;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// This is a simple client api that exposes the ability of the sdk using a fluent
    /// API that can be used like int he following example:
    /// <code>
    /// var builder = new ClientBuilder();
    /// var client = builder.NewClient()
    ///     .WithName("Test")
    ///     .WithUri("uri")
    ///     .WithProductUri("Pro")
    ///     .WithTransportQuota(o => o.SetMaxBufferSize(100))
    ///     .UpdateApplicationFromExistingCert()
    ///     .WithMaxPooledSessions(10)
    ///     .Build();
    ///
    /// var ownCertificate = await client.Certificates.GetOwnCertificateAsync();
    ///
    /// using var session = await client
    ///     .ConnectTo("endpointUrl")
    ///     .WithSecurityMode(MessageSecurityMode.SignAndEncrypt)
    ///     .WithSecurityPolicy(SecurityPolicies.None)
    ///     .WithServerCertificate([])
    ///     .FromPool
    ///         .WithOption(o => o.WithTimeout(TimeSpan.FromMicroseconds(1000)))
    ///         .WithOption(o => o.WithKeepAliveInterval(TimeSpan.FromSeconds(30)))
    ///         .CreateAsync().ConfigureAwait(false);
    ///
    /// await session.Session.CallAsync(null, null).ConfigureAwait(false);
    /// using var direct = await client
    ///     .ConnectTo("endpointur")
    ///     .UseReverseConnect()
    ///     .WithOption(o => o.WithUser(new UserIdentity()))
    ///     .CreateAsync()
    ///     .ConfigureAwait(false);
    ///
    /// await direct.CallAsync(null, null).ConfigureAwait(false);
    /// </code>
    /// </summary>
    /// <param name="services">A services collection to build a service provider</param>
    public class ClientBuilder(IServiceCollection? services = null) : ClientBuilderBase<
        PooledSessionOptions, Sessions.SessionOptions, SessionCreateOptions, ClientOptions>(services)
    {
        /// <inheritdoc/>
        protected override ISessionBuilder<PooledSessionOptions, Sessions.SessionOptions,
            SessionCreateOptions>
            Build(ServiceProvider provider, ApplicationInstance application,
                string applicationUri, string productUri, ClientOptions options,
                ITelemetryContext telemetry)
        {
            var app = new ClientApplication(application, applicationUri, productUri,
                options, telemetry);
            return new ClientSessionBuilder(provider, app);
        }

        /// <summary>
        /// Session builder
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="application"></param>
        internal class ClientSessionBuilder(ServiceProvider provider,
            SessionManagerBase application) :
            SessionBuilderBase<PooledSessionOptions, Sessions.SessionOptions, SessionCreateOptions,
                SessionCreateOptionsBuilder<SessionCreateOptions>>(application,
                new PooledSessionBuilderBase<PooledSessionOptions, Sessions.SessionOptions,
                    SessionOptionsBuilderBase<Sessions.SessionOptions>>(application))
        {
            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    provider.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Client application built by the client builder and input into the session
        /// builder. It encapsulates the state of the application.
        /// </summary>
        /// <remarks>
        /// Create client application
        /// </remarks>
        /// <remarks>
        /// Create session builder
        /// </remarks>
        /// <param name="instance"></param>
        /// <param name="applicationUri"></param>
        /// <param name="productUri"></param>
        /// <param name="options"></param>
        /// <param name="telemetry"></param>
        internal class ClientApplication(ApplicationInstance instance, string applicationUri,
            string productUri, ClientOptions options, ITelemetryContext telemetry) :
            SessionManagerBase(instance, applicationUri, productUri, options, telemetry)
        {
            /// <summary>
            /// Gets or sets the data change callback.
            /// </summary>
            public DataChangeNotificationHandler? DataChangeCallback { get; set; }

            /// <summary>
            /// Gets or sets the event callback.
            /// </summary>
            public EventNotificationHandler? EventCallback { get; set; }

            /// <summary>
            /// Gets or sets the keep alive callback.
            /// </summary>
            public KeepAliveNotificationHandler? KeepAliveCallback { get; set; }

            /// <inheritdoc/>
            protected override Opc.Ua.Client.Sessions.Session CreateSession(ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions options, ITelemetryContext telemetry)
            {
                return new ClientSession(this, configuration, endpoint, options, telemetry);
            }

            /// <inheritdoc/>
            public override char[] GetPassword(CertificateIdentifier certificateIdentifier)
            {
                return [];
            }
        }

        /// <inheritdoc/>
        internal sealed class ClientSession : Opc.Ua.Client.Sessions.Session
        {
            /// <inheritdoc/>
            public ClientApplication Application { get; }

            /// <inheritdoc/>
            internal ClientSession(ClientApplication application, ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions options, ITelemetryContext telemetry)
                : base(configuration, endpoint, options, telemetry, application.ReverseConnectManager) => Application = application;

            /// <inheritdoc/>
            protected override IManagedSubscription CreateSubscription(ISubscriptionNotificationHandler handler,
                IOptionsMonitor<Subscriptions.SubscriptionOptions> options, IMessageAckQueue queue,
                ITelemetryContext telemetry)
            {
                return new ClientSubscription(this, handler, queue, options, telemetry);
            }
        }

        /// <inheritdoc/>
        internal sealed class ClientSubscription : Subscriptions.Subscription
        {
            /// <inheritdoc/>
            internal ClientSubscription(ClientSession session, ISubscriptionNotificationHandler handler,
                IMessageAckQueue completion, IOptionsMonitor<Subscriptions.SubscriptionOptions> options,
                ITelemetryContext telemetry) :
                base(session, handler, completion, options, telemetry) => _session = session;

            /// <inheritdoc/>
            protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, DataChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                notification.PublishTime = publishTime;
                notification.SequenceNumber = sequenceNumber;
                _session.Application.DataChangeCallback?.Invoke(this, notification, stringTable);
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc/>
            protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
                DateTime publishTime, EventNotificationList notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                notification.PublishTime = publishTime;
                notification.SequenceNumber = sequenceNumber;
                _session.Application.EventCallback?.Invoke(this, notification, stringTable);
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc/>
            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                _session.Application.KeepAliveCallback?.Invoke(this, new NotificationData
                {
                    SequenceNumber = sequenceNumber,
                    PublishTime = publishTime
                });
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc/>
            protected override Subscriptions.MonitoredItems.MonitoredItem CreateMonitoredItem(string name,
                IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions> options, IMonitoredItemContext context,
                ITelemetryContext telemetry)
            {
                return new ClientItem(context, name, options,
                    telemetry.LoggerFactory.CreateLogger<ClientItem>());
            }

            private readonly ClientSession _session;
        }

        /// <inheritdoc/>
        internal sealed class ClientItem : Subscriptions.MonitoredItems.MonitoredItem
        {
            /// <inheritdoc/>
            internal ClientItem(IMonitoredItemContext context, string name,
                IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions> options, ILogger logger)
                : base(context, name, options, logger)
            {
            }
        }
    }

    /// <summary>
    /// The delegate used to receive data change notifications.
    /// </summary>
    /// <param name="subscription"></param>
    /// <param name="notification"></param>
    /// <param name="stringTable"></param>
    public delegate void DataChangeNotificationHandler(ISubscription subscription,
        DataChangeNotification notification, IReadOnlyList<string> stringTable);

    /// <summary>
    /// The delegate used to receive event notifications.
    /// </summary>
    /// <param name="subscription"></param>
    /// <param name="notification"></param>
    /// <param name="stringTable"></param>
    public delegate void EventNotificationHandler(ISubscription subscription,
        EventNotificationList notification, IReadOnlyList<string> stringTable);

    /// <summary>
    /// The delegate used to receive keep alive notifications.
    /// </summary>
    /// <param name="subscription"></param>
    /// <param name="notification"></param>
    public delegate void KeepAliveNotificationHandler(ISubscription subscription,
        NotificationData notification);
}
#endif
