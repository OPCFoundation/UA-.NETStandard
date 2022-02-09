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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public interface ISession : ISessionClient, IDisposable
    {
        #region Events
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
        /// Raised when a subscription is added or removed
        /// </summary>
        event EventHandler SubscriptionsChanged;

        /// <summary>
        /// Raised to indicate the session is closing.
        /// </summary>
        event EventHandler SessionClosing;
        #endregion

        #region Public Properties
        /// <summary>
        /// 
        /// </summary>
        bool CheckDomain { get; }
        /// <summary>
        /// 
        /// </summary>
        uint MaxRequestMessageSize { get; }
        /// <summary>
        /// 
        /// </summary>
        public X509Certificate2 InstanceCertificate { get; }

        /// <summary>
        /// 
        /// </summary>
        public X509Certificate2Collection InstanceCertificateChain { get; }

        /// <summary>
        /// The configuration for the client application.
        /// </summary>
        ApplicationConfiguration Configuration { get; }
        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        ConfiguredEndpoint ConfiguredEndpoint { get; }

        /// <summary>
        /// Gets the name assigned to the session.
        /// </summary>
        string SessionName { get; }

        /// <summary>
        /// Gets the period for wich the server will maintain the session if there is no communication from the client.
        /// </summary>
        double SessionTimeout { get; }

        /// <summary>
        /// Gets the local handle assigned to the session.
        /// </summary>
        object Handle { get; }

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
        FilterContext FilterContext { get; }

        /// <summary>
        /// Gets the locales that the server should use when returning localized text.
        /// </summary>
        StringCollection PreferredLocales { get; }

        /// <summary>
        /// Gets the data type system dictionaries in use.
        /// </summary>
        IReadOnlyDictionary<NodeId, DataDictionary> DataTypeSystem { get; }

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
        /// Gets or Sets the default subscription for the session.
        /// </summary>
        Subscription DefaultSubscription { get; set; }

        /// <summary>
        /// Gets or Sets how frequently the server is pinged to see if communication is still working.
        /// </summary>
        /// <remarks>
        /// This interval controls how much time elaspes before a communication error is detected.
        /// If everything is ok the KeepAlive event will be raised each time this period elapses.
        /// </remarks>
        int KeepAliveInterval { get; set; }

        /// <summary>
        /// Returns true if the session is not receiving keep alives.
        /// </summary>
        /// <remarks>
        /// Set to true if the server does not respond for 2 times the KeepAliveInterval.
        /// Set to false is communication recovers.
        /// </remarks>
        bool KeepAliveStopped { get; }

        /// <summary>
        /// Gets the time of the last keep alive.
        /// </summary>
        DateTime LastKeepAliveTime { get; }

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
        #endregion

        #region Delegates and Events
        /// <summary>
        /// Raised before a reconnect operation completes.
        /// </summary>
        event RenewUserIdentityEventHandler RenewUserIdentity;

        #endregion

        #region Public Methods
        /// <summary>
        /// Reconnects to the server after a network failure.
        /// </summary>
        void Reconnect();

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        void Reconnect(ITransportWaitingConnection connection);

        /// <summary>
        /// Saves all the subscriptions of the session.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        void Save(string filePath);

        /// <summary>
        /// Saves a set of subscriptions to a stream.
        /// </summary>
        void Save(Stream stream, IEnumerable<Subscription> subscriptions);

        /// <summary>
        /// Saves a set of subscriptions to a file.
        /// </summary>
        void Save(string filePath, IEnumerable<Subscription> subscriptions);

        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The list of loaded subscriptions</returns>
        IEnumerable<Subscription> Load(Stream stream);

        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The list of loaded subscriptions</returns>
        IEnumerable<Subscription> Load(string filePath);

        /// <summary>
        /// Updates the local copy of the server's namespace uri and server uri tables.
        /// </summary>
        void FetchNamespaceTables();

        /// <summary>
        /// Updates the cache with the type and its subtypes.
        /// </summary>
        /// <remarks>
        /// This method can be used to ensure the TypeTree is populated.
        /// </remarks>
        void FetchTypeTree(ExpandedNodeId typeId);

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        /// <param name="variableId">The variable node.</param>
        ReferenceDescriptionCollection ReadAvailableEncodings(NodeId variableId);

        /// <summary>
        /// Returns the data description for the encoding.
        /// </summary>
        /// <param name="encodingId">The encoding Id.</param>
        ReferenceDescription FindDataDescription(NodeId encodingId);

        /// <summary>
        ///  Returns the data dictionary that contains the description.
        /// </summary>
        /// <param name="descriptionId">The description id.</param>
        Task<DataDictionary> FindDataDictionary(NodeId descriptionId);

        /// <summary>
        ///  Returns the data dictionary that contains the description.
        /// </summary>
        /// <param name="dictionaryNode">The dictionary id.</param>
        /// <param name="forceReload"></param>
        /// <returns>The dictionary.</returns>
        Task<DataDictionary> LoadDataDictionary(ReferenceDescription dictionaryNode, bool forceReload = false);

        /// <summary>
        /// Loads all dictionaries of the OPC binary or Xml schema type system.
        /// </summary>
        /// <param name="dataTypeSystem">The type system.</param>
        Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(NodeId dataTypeSystem = null);

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="nodeId">The nodeId.</param>
        Node ReadNode(NodeId nodeId);

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="nodeId">The node Id.</param>
        DataValue ReadValue(NodeId nodeId);

        /// <summary>
        /// Reads the value for a node an checks that it is the specified type.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="expectedType">The expected type.</param>
        object ReadValue(NodeId nodeId, Type expectedType);

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        ReferenceDescriptionCollection FetchReferences(NodeId nodeId);

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="identity">The user identity.</param>
        void Open(string sessionName, IUserIdentity identity);

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        void Open(string sessionName, uint sessionTimeout, IUserIdentity identity, IList<string> preferredLocales);

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        void Open(string sessionName, uint sessionTimeout, IUserIdentity identity, IList<string> preferredLocales, bool checkDomain);

        /// <summary>
        /// Updates the preferred locales used for the session.
        /// </summary>
        /// <param name="preferredLocales">The preferred locales.</param>
        void ChangePreferredLocales(StringCollection preferredLocales);

        /// <summary>
        /// Updates the user identity and/or locales used for the session.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        void UpdateSession(IUserIdentity identity, StringCollection preferredLocales);

        /// <summary>
        /// Finds the NodeIds for the components for an instance.
        /// </summary>
        void FindComponentIds(NodeId instanceId, IList<string> componentPaths, out NodeIdCollection componentIds, out List<ServiceResult> errors);

        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        /// <param name="variableIds">The variable ids.</param>
        /// <param name="expectedTypes">The expected types.</param>
        /// <param name="values">The list of returned values.</param>
        /// <param name="errors">The list of returned errors.</param>
        void ReadValues(IList<NodeId> variableIds, IList<Type> expectedTypes, out List<object> values, out List<ServiceResult> errors);

        /// <summary>
        /// Reads the display name for a set of Nodes.
        /// </summary>
        void ReadDisplayName(IList<NodeId> nodeIds, out List<string> displayNames, out List<ServiceResult> errors);
        #endregion

        #region Close Methods

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        StatusCode Close(int timeout);
        #endregion

        #region Subscription Methods
        /// <summary>
        /// Adds a subscription to the session.
        /// </summary>
        /// <param name="subscription">The subscription to add.</param>
        bool AddSubscription(Subscription subscription);

        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        bool RemoveSubscription(Subscription subscription);

        /// <summary>
        /// Removes a list of subscriptions from the session.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        bool RemoveSubscriptions(IEnumerable<Subscription> subscriptions);

        /// <summary>
        /// Removes a transferred subscription from the session.
        /// Called by the session to which the subscription
        /// is transferred to obtain ownership. Internal.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        bool RemoveTransferredSubscription(Subscription subscription);

        /// <summary>
        /// Transfers a list of Subscriptions from another session.
        /// </summary>
        bool TransferSubscriptions(SubscriptionCollection subscriptions, bool sendInitialValues);

        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        Task<bool> RemoveSubscriptionAsync(Subscription subscription);

        /// <summary>
        /// Removes a list of subscriptions from the sessiont.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        Task<bool> RemoveSubscriptionsAsync(IEnumerable<Subscription> subscriptions);
        #endregion

        #region Browse Methods

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view to browse.</param>
        /// <param name="nodeToBrowse">The node to browse.</param>
        /// <param name="maxResultsToReturn">The maximum number of returned values.</param>
        /// <param name="browseDirection">The browse direction.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="includeSubtypes">If set to <c>true</c> the subtypes of the ReferenceType will be included in the browse.</param>
        /// <param name="nodeClassMask">The node class mask.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="references">The list of node references.</param>
        ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeId nodeToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references);

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view to browse.</param>
        /// <param name="nodeToBrowse">The node to browse.</param>
        /// <param name="maxResultsToReturn">The maximum number of returned values..</param>
        /// <param name="browseDirection">The browse direction.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="includeSubtypes">If set to <c>true</c> the subtypes of the ReferenceType will be included in the browse.</param>
        /// <param name="nodeClassMask">The node class mask.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="asyncState"></param>
        IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeId nodeToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="references">The list of node references.</param>
        ResponseHeader EndBrowse(
            IAsyncResult result,
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references);
        #endregion

        #region BrowseNext Methods
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out byte[] revisedContinuationPoint,
            out ReferenceDescriptionCollection references);

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out byte[] revisedContinuationPoint,
            out ReferenceDescriptionCollection references);
        #endregion

        #region Call Methods
        /// <summary>
        /// Calls the specified method and returns the output arguments.
        /// </summary>
        /// <param name="objectId">The NodeId of the object that provides the method.</param>
        /// <param name="methodId">The NodeId of the method to call.</param>
        /// <param name="args">The input arguments.</param>
        /// <returns>The list of output argument values.</returns>
        IList<object> Call(NodeId objectId, NodeId methodId, params object[] args);
        #endregion

        #region Publish Methods
        /// <summary>
        /// Sends an additional publish request.
        /// </summary>
        IAsyncResult BeginPublish(int timeout);

        /// <summary>
        /// Sends a republish request.
        /// </summary>
        bool Republish(uint subscriptionId, uint sequenceNumber);

        #endregion
    }
}
