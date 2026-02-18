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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Used to handle renews of user identity tokens before reconnect.
    /// </summary>
    public delegate IUserIdentity RenewUserIdentityEventHandler(
        ISession session,
        IUserIdentity identity);

    /// <summary>
    /// The delegate used to receive keep alive notifications.
    /// </summary>
    public delegate void KeepAliveEventHandler(ISession session, KeepAliveEventArgs e);

    /// <summary>
    /// The delegate used to receive publish notifications.
    /// </summary>
    public delegate void NotificationEventHandler(ISession session, NotificationEventArgs e);

    /// <summary>
    /// The delegate used to receive publish error notifications.
    /// </summary>
    public delegate void PublishErrorEventHandler(ISession session, PublishErrorEventArgs e);

    /// <summary>
    /// The delegate used to modify publish response sequence numbers to acknowledge.
    /// </summary>
    public delegate void PublishSequenceNumbersToAcknowledgeEventHandler(
        ISession session,
        PublishSequenceNumbersToAcknowledgeEventArgs e);

    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public interface ISession : ISessionClient
    {
        /// <summary>
        /// Raised when a keep alive arrives from the server or an error is detected.
        /// </summary>
        /// <remarks>
        /// Once a session is created a timer will periodically read the server state and current time.
        /// If this read operation succeeds this event will be raised each time the keep alive period elapses.
        /// If an error is detected (KeepAliveStopped == true) then this event will be raised as well.
        /// </remarks>
        event KeepAliveEventHandler KeepAlive;

        /// <summary>
        /// Raised when a notification message arrives in a publish response.
        /// </summary>
        /// <remarks>
        /// All publish requests are managed by the Session object. When a response arrives it is
        /// validated and passed to the appropriate Subscription object and this event is raised.
        /// </remarks>
        event NotificationEventHandler Notification;

        /// <summary>
        /// Raised when an exception occurs while processing a publish response.
        /// </summary>
        /// <remarks>
        /// Exceptions in a publish response are not necessarily fatal and the Session will
        /// attempt to recover by issuing Republish requests if missing messages are detected.
        /// That said, timeout errors may be a symptom of a OperationTimeout that is too short
        /// when compared to the shortest PublishingInterval/KeepAliveCount amount the current
        /// Subscriptions. The OperationTimeout should be twice the minimum value for
        /// PublishingInterval*KeepAliveCount.
        /// </remarks>
        event PublishErrorEventHandler PublishError;

        /// <summary>
        /// Raised when a publish request is about to acknowledge sequence numbers.
        /// </summary>
        /// <remarks>
        /// If the client chooses to defer acknowledge of sequence numbers, it is responsible
        /// to transfer these <see cref="SubscriptionAcknowledgement"/> to the deferred list.
        /// </remarks>
        event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge;

        /// <summary>
        /// Raised when a subscription is added or removed
        /// </summary>
        event EventHandler SubscriptionsChanged;

        /// <summary>
        /// Raised to indicate the session is closing.
        /// </summary>
        event EventHandler SessionClosing;

        /// <summary>
        /// Raised to indicate the session configuration changed.
        /// </summary>
        /// <remarks>
        /// An example for a session configuration change is a new user identity,
        /// a new server nonce, a new locale etc.
        /// </remarks>
        event EventHandler SessionConfigurationChanged;

        /// <summary>
        /// The factory which was used to create the session.
        /// </summary>
        ISessionFactory SessionFactory { get; }

        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        ConfiguredEndpoint ConfiguredEndpoint { get; }

        /// <summary>
        /// Gets the name assigned to the session.
        /// </summary>
        string SessionName { get; }

        /// <summary>
        /// Gets the period for wich the server will maintain the session if there
        /// is no communication from the client.
        /// </summary>
        double SessionTimeout { get; }

        /// <summary>
        /// Gets the local handle assigned to the session.
        /// </summary>
        object? Handle { get; }

        /// <summary>
        /// Gets the user identity currently used for the session.
        /// </summary>
        IUserIdentity Identity { get; }

        /// <summary>
        /// Gets a list of user identities that can be used to connect to the server.
        /// </summary>
        IEnumerable<IUserIdentity> IdentityHistory { get; }

        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gest the table of remote server uris known to the server.
        /// </summary>
        StringTable ServerUris { get; }

        /// <summary>
        /// Gets the system context for use with the session.
        /// </summary>
        ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the factory used to create encodeable objects that the server understands.
        /// </summary>
        IEncodeableFactory Factory { get; }

        /// <summary>
        /// Gets the cache of the server's type tree.
        /// </summary>
        ITypeTable TypeTree { get; }

        /// <summary>
        /// Gets the cache of nodes fetched from the server.
        /// </summary>
        INodeCache NodeCache { get; }

        /// <summary>
        /// Gets the context to use for filter operations.
        /// </summary>
        IFilterContext FilterContext { get; }

        /// <summary>
        /// Gets the locales that the server should use when returning localized text.
        /// </summary>
        ArrayOf<string> PreferredLocales { get; }

        /// <summary>
        /// Gets the subscriptions owned by the session.
        /// </summary>
        IEnumerable<Subscription> Subscriptions { get; }

        /// <summary>
        /// Gets the number of subscriptions owned by the session.
        /// </summary>
        int SubscriptionCount { get; }

        /// <summary>
        /// If the subscriptions are deleted when a session is closed.
        /// </summary>
        bool DeleteSubscriptionsOnClose { get; set; }

        /// <summary>
        /// Gets or sets the time in milliseconds to wait for outstanding publish
        /// requests to complete before canceling them during session close.
        /// </summary>
        /// <remarks>
        /// A value of 0 means no waiting - outstanding requests are canceled immediately.
        /// A negative value means wait indefinitely for all outstanding requests to complete.
        /// The default value is 5000 milliseconds (5 seconds).
        /// </remarks>
        int PublishRequestCancelDelayOnCloseSession { get; set; }

        /// <summary>
        /// Gets or Sets the default subscription for the session.
        /// </summary>
        Subscription DefaultSubscription { get; set; }

        /// <summary>
        /// Gets or Sets how frequently the server is pinged to see if communication
        /// is still working.
        /// </summary>
        /// <remarks>
        /// This interval controls how much time elaspes before a communication
        /// error is detected.
        /// If everything is ok the KeepAlive event will be raised each time this
        /// period elapses.
        /// </remarks>
        int KeepAliveInterval { get; set; }

        /// <summary>
        /// Returns true if the session is not receiving keep alives.
        /// </summary>
        /// <remarks>
        /// Set to true if the server does not respond for the KeepAliveInterval times
        /// a configurable factor + a configurable guard band.
        /// Set to false is communication recovers.
        /// </remarks>
        bool KeepAliveStopped { get; }

        /// <summary>
        /// Gets the time of the last keep alive.
        /// This time may not be monotonic if the system time is changed.
        /// </summary>
        DateTime LastKeepAliveTime { get; }

        /// <summary>
        /// Gets the TickCount in ms of the last keep alive based on <see cref="HiResClock.TickCount"/>.
        /// </summary>
        int LastKeepAliveTickCount { get; }

        /// <summary>
        /// Gets the number of outstanding publish or keep alive requests.
        /// </summary>
        int OutstandingRequestCount { get; }

        /// <summary>
        /// Gets the number of outstanding publish or keep alive requests which appear to be missing.
        /// </summary>
        int DefunctRequestCount { get; }

        /// <summary>
        /// Gets the number of good outstanding publish requests.
        /// </summary>
        int GoodPublishRequestCount { get; }

        /// <summary>
        /// Gets and sets the minimum number of publish requests to be used in the session.
        /// </summary>
        int MinPublishRequestCount { get; set; }

        /// <summary>
        /// Gets and sets the maximum number of publish requests to be used in the session.
        /// </summary>
        int MaxPublishRequestCount { get; set; }

        /// <summary>
        /// Whether a session is being reconnected
        /// </summary>
        /// <value><c>true</c> if reconnected; otherwise, <c>false</c>.</value>
        bool Reconnecting { get; }

        /// <summary>
        /// Stores the operation limits of a OPC UA Server.
        /// </summary>
        OperationLimits OperationLimits { get; }

        /// <summary>
        /// Stores the capabilities of a OPC UA server.
        /// </summary>
        ServerCapabilities ServerCapabilities { get; }

        /// <summary>
        /// If the subscriptions are transferred when a session is reconnected.
        /// </summary>
        /// <remarks>
        /// Default <c>false</c>, set to <c>true</c> if subscriptions should
        /// be transferred after reconnect. Service must be supported by server.
        /// </remarks>
        bool TransferSubscriptionsOnReconnect { get; set; }

        /// <summary>
        /// Whether the endpoint Url domain is checked in the certificate.
        /// </summary>
        bool CheckDomain { get; }

        /// <summary>
        /// gets or set the policy which is used to prevent the allocation of too many
        /// Continuation Points in the ManagedBrowse(Async) methods
        /// </summary>
        ContinuationPointPolicy ContinuationPointPolicy { get; set; }

        /// <summary>
        /// Raised before a reconnect operation completes.
        /// </summary>
        event RenewUserIdentityEventHandler RenewUserIdentity;

        /// <summary>
        /// Reconnects to the server after a network failure using
        /// a waiting connection or channel which either is provided.
        /// If none is provided creates a new channel.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        Task ReconnectAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct = default);

        /// <summary>
        ///Reload the own certificate used by the session and the issuer chain when available.
        /// </summary>
        Task ReloadInstanceCertificateAsync(CancellationToken ct = default);

        /// <summary>
        /// Saves a set of subscriptions to a stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="subscriptions"></param>
        /// <param name="knownTypes"></param>
        void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null);

        /// <summary>
        /// Load the list of subscriptions saved in a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="transferSubscriptions">Load the subscriptions for transfer
        /// after load.</param>
        /// <param name="knownTypes">Additional known types that may be needed to
        /// read the saved subscriptions.</param>
        /// <returns>The list of loaded subscriptions</returns>
        IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null);

        /// <summary>
        /// Returns the active session configuration and writes it to a stream.
        /// </summary>
        SessionConfiguration SaveSessionConfiguration(Stream? stream = null);

        /// <summary>
        /// Applies a session configuration.
        /// Using a secure channel, with the session configuration a session can be reconnected.
        /// </summary>
        bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration);

        /// <summary>
        /// Updates the local copy of the server's namespace uri and server uri tables.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        Task FetchNamespaceTablesAsync(CancellationToken ct = default);

        /// <summary>
        /// Updates the cache with the type and its subtypes.
        /// </summary>
        /// <remarks>
        /// This method can be used to ensure the TypeTree is populated.
        /// </remarks>
        Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default);

        /// <summary>
        /// Updates the cache with the types and its subtypes.
        /// </summary>
        /// <remarks>
        /// This method can be used to ensure the TypeTree is populated.
        /// </remarks>
        Task FetchTypeTreeAsync(ExpandedNodeIdCollection typeIds, CancellationToken ct = default);

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain
        /// in the certificate must match the endpoint used.</param>
        /// <param name="closeChannel">If set to <c>true</c> then the channel
        /// is closed when the Open fails.</param>
        /// <param name="ct">The cancellation token.</param>
        Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct = default);

        /// <summary>
        /// Updates the user identity and/or locales used for the session.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        Task UpdateSessionAsync(
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Changes the preferred locales used for the session.
        /// </summary>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        Task ChangePreferredLocalesAsync(
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default);

        /// <summary>
        /// Disconnects from the server and frees any network resources
        /// with the specified timeout.
        /// </summary>
        Task<StatusCode> CloseAsync(
            int timeout,
            bool closeChannel,
            CancellationToken ct = default);

        /// <summary>
        /// Adds a subscription to the session.
        /// </summary>
        /// <param name="subscription">The subscription to add.</param>
        bool AddSubscription(Subscription subscription);

        /// <summary>
        /// Removes a transferred subscription from the session.
        /// Called by the session to which the subscription
        /// is transferred to obtain ownership. Internal.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        bool RemoveTransferredSubscription(Subscription subscription);

        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        Task<bool> RemoveSubscriptionAsync(
            Subscription subscription,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a list of subscriptions from the session.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default);

        /// <summary>
        /// Reactivates a list of subscriptions loaded from storage.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to reactivate.</param>
        /// <param name="sendInitialValues">Send the last value of each monitored item in the subscriptions.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default);

        /// <summary>
        /// Transfers a list of subscriptions from another session.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to transfer.</param>
        /// <param name="sendInitialValues">Send the last value of each monitored item in the subscriptions.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default);

        /// <summary>
        /// Sends an additional publish request.
        /// </summary>
        bool BeginPublish(int timeout);

        /// <summary>
        /// Create the publish requests for the active subscriptions.
        /// </summary>
        void StartPublishing(int timeout, bool fullQueue);

        /// <summary>
        /// Sends a republish request.
        /// </summary>
        Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct = default);
    }
}
