/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public static class SessionObsolete
    {
        /// <summary>
        /// Reconnects to the server after a network failure.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void Reconnect(this ISession session)
        {
            session.ReconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void Reconnect(this ISession session, ITransportWaitingConnection connection)
        {
            session.ReconnectAsync(connection).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reconnects to the server using a new channel.
        /// </summary>
        [Obsolete("Use ReconnectAsync instead.")]
        public static void Reconnect(this ISession session, ITransportChannel channel)
        {
            session.ReconnectAsync(channel).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the local copy of the server's namespace uri and server uri tables.
        /// </summary>
        [Obsolete("Use FetchNamespaceTablesAsync instead.")]
        public static void FetchNamespaceTables(this ISession session)
        {
            session.FetchNamespaceTablesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the cache with the type and its subtypes.
        /// </summary>
        [Obsolete("Use FetchTypeTreeAsync instead.")]
        public static void FetchTypeTree(this ISession session, ExpandedNodeId typeId)
        {
            session.FetchTypeTreeAsync(typeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the cache with the types and its subtypes.
        /// </summary>
        [Obsolete("Use FetchTypeTreeAsync instead.")]
        public static void FetchTypeTree(this ISession session, ExpandedNodeIdCollection typeIds)
        {
            session.FetchTypeTreeAsync(typeIds).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        [Obsolete("Use ReadAvailableEncodingsAsync instead.")]
        public static ReferenceDescriptionCollection ReadAvailableEncodings(
            this ISession session,
            NodeId variableId)
        {
            return session.ReadAvailableEncodingsAsync(variableId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the data description for the encoding.
        /// </summary>
        [Obsolete("Use FindDataDescriptionAsync instead.")]
        public static ReferenceDescription FindDataDescription(
            this ISession session,
            NodeId encodingId)
        {
            return session.FindDataDescriptionAsync(encodingId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        [Obsolete("Use ReadNodeAsync instead.")]
        public static Node ReadNode(this ISession session, NodeId nodeId)
        {
            return session.ReadNodeAsync(nodeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        [Obsolete("Use ReadNodeAsync instead.")]
        public static Node ReadNode(
            this ISession session,
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true)
        {
            return session.ReadNodeAsync(
                nodeId,
                nodeClass,
                optionalAttributes).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        [Obsolete("Use ReadNodesAsync instead.")]
        public static void ReadNodes(
            this ISession session,
            IList<NodeId> nodeIds,
            out IList<Node> nodeCollection,
            out IList<ServiceResult> errors,
            bool optionalAttributes = false)
        {
            (nodeCollection, errors) = session.ReadNodesAsync(
                nodeIds,
                optionalAttributes).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known,
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        [Obsolete("Use ReadNodesAsync instead.")]
        public static void ReadNodes(
            this ISession session,
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            out IList<Node> nodeCollection,
            out IList<ServiceResult> errors,
            bool optionalAttributes = false)
        {
            (nodeCollection, errors) = session.ReadNodesAsync(
                nodeIds,
                nodeClass,
                optionalAttributes).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        [Obsolete("Use ReadValueAsync instead.")]
        public static DataValue ReadValue(this ISession session, NodeId nodeId)
        {
            return session.ReadValueAsync(nodeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads the value for a node an checks that it is the specified type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use ReadValueAsync instead.")]
        public static object ReadValue(this ISession session, NodeId nodeId, Type expectedType)
        {
            DataValue dataValue = session.ReadValueAsync(nodeId).GetAwaiter().GetResult();
            object value = dataValue.Value;

            if (expectedType != null)
            {
                if (value is ExtensionObject extension)
                {
                    value = extension.Body;
                }

                if (!expectedType.IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Server returned value unexpected type: {0}",
                        value != null ? value.GetType().Name : "(null)");
                }
            }
            return value;
        }

        /// <summary>
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        [Obsolete("Use ReadValuesAsync instead.")]
        public static void ReadValues(
            this ISession session,
            IList<NodeId> nodeIds,
            out DataValueCollection values,
            out IList<ServiceResult> errors)
        {
            (DataValueCollection, IList<ServiceResult>) result = session.ReadValuesAsync(
                nodeIds).GetAwaiter().GetResult();
            // Todo: Validate the types are correct
            values = result.Item1;
            errors = result.Item2;
        }

        /// <summary>
        /// Reads a byte string which is too large for the (server side) encoder to handle.
        /// </summary>
        /// <returns></returns>
        [Obsolete("Use ReadByteStringInChunksAsync instead.")]
        public static byte[] ReadByteStringInChunks(
            this ISession session,
            NodeId nodeId)
        {
            return session.ReadByteStringInChunksAsync(nodeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        [Obsolete("Use FetchReferencesAsync instead.")]
        public static ReferenceDescriptionCollection FetchReferences(
            this ISession session,
            NodeId nodeId)
        {
            return session.FetchReferencesAsync(nodeId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Fetches all references for the specified nodes.
        /// </summary>
        [Obsolete("Use FetchReferencesAsync instead.")]
        public static void FetchReferences(
            this ISession session,
            IList<NodeId> nodeIds,
            out IList<ReferenceDescriptionCollection> referenceDescriptions,
            out IList<ServiceResult> errors)
        {
            (referenceDescriptions, errors) = session.FetchReferencesAsync(
                nodeIds).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        [Obsolete("Use OpenAsync instead.")]
        public static void Open(
            this ISession session,
            string sessionName,
            IUserIdentity identity)
        {
            session.OpenAsync(sessionName, identity).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        [Obsolete("Use OpenAsync instead.")]
        public static void Open(
            this ISession session,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            session.OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        [Obsolete("Use OpenAsync instead.")]
        public static void Open(
            this ISession session,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain)
        {
            session.OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                checkDomain).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        [Obsolete("Use OpenAsync instead.")]
        public static void Open(
            this ISession session,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            bool closeChannel)
        {
            session.OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                checkDomain,
                closeChannel).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the preferred locales used for the session.
        /// </summary>
        public static void ChangePreferredLocales(
            this ISession session,
            StringCollection preferredLocales)
        {
            session.ChangePreferredLocalesAsync(preferredLocales)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Updates the user identity and/or locales used for the session.
        /// </summary>
        public static void UpdateSession(
            this ISession session,
            IUserIdentity identity,
            StringCollection preferredLocales)
        {
            session.UpdateSessionAsync(identity, preferredLocales)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Finds the NodeIds for the components for an instance.
        /// </summary>
        public static void FindComponentIds(this ISession session,
            NodeId instanceId,
            IList<string> componentPaths,
            out NodeIdCollection componentIds,
            out IList<ServiceResult> errors)
        {
            (componentIds, errors) = session.FindComponentIdsAsync(
                instanceId,
                componentPaths)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        [Obsolete("Use Use ReadValuesAsync instead.")]
        public static void ReadValues(this ISession session,
            IList<NodeId> variableIds,
            IList<Type> expectedTypes,
            out IList<object> values,
            out IList<ServiceResult> errors)
        {
            (DataValueCollection dataValues, errors) = session.ReadValuesAsync(
                variableIds)
                .GetAwaiter()
                .GetResult();
            values = new object[dataValues.Count];

            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                object value = dataValues[ii].Value;

                // extract the body from extension objects.
                if (value is ExtensionObject extension &&
                    extension.Body is IEncodeable)
                {
                    value = extension.Body;
                }

                // check expected type.
                if (expectedTypes[ii] != null &&
                    !expectedTypes[ii].IsInstanceOfType(value))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "Value {0} does not have expected type: {1}.",
                        value,
                        expectedTypes[ii].Name);

                    continue;
                }

                // suitable value found.
                values[ii] = value;
            }
        }

        /// <summary>
        /// Reads the display name for a set of Nodes.
        /// </summary>
        [Obsolete("Use Use ReadValuesAsync instead.")]
        public static void ReadDisplayName(this ISession session,
            IList<NodeId> nodeIds,
            out IList<string> displayNames,
            out IList<ServiceResult> errors)
        {
            (displayNames, errors) = session.ReadDisplayNameAsync(nodeIds)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Closes the client object and the underlying channel.
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static StatusCode Close(this IClientBase client)
        {
            return client.CloseAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources
        /// with the specified timeout.
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static StatusCode Close(this ISession session, int timeout)
        {
            return session.CloseAsync(timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Close the session with the server and optionally closes the channel.
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static StatusCode Close(this ISession session, bool closeChannel)
        {
            return session.CloseAsync(closeChannel)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources
        /// with the specified timeout.
        /// </summary>
        [Obsolete("Use CloseAsync instead.")]
        public static StatusCode Close(
            this ISession session,
            int timeout,
            bool closeChannel)
        {
            return session.CloseAsync(timeout, closeChannel)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        [Obsolete("Use RemoveSubscriptionAsync instead.")]
        public static bool RemoveSubscription(
            this ISession session,
            Subscription subscription)
        {
            return session.RemoveSubscriptionAsync(subscription)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Remove a list of subscriptions from the session.
        /// </summary>
        [Obsolete("Use RemoveSubscriptionsAsync instead.")]
        public static bool RemoveSubscriptions(
            this ISession session,
            IEnumerable<Subscription> subscriptions)
        {
            return session.RemoveSubscriptionsAsync(subscriptions)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Reactivates a list of subscriptions loaded from storage.
        /// </summary>
        [Obsolete("Use ReactivateSubscriptionsAsync instead.")]
        public static bool ReactivateSubscriptions(
            this ISession session,
            SubscriptionCollection subscriptions,
            bool sendInitialValues)
        {
            return session.ReactivateSubscriptionsAsync(
                subscriptions,
                sendInitialValues)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Transfers a list of subscriptions from another session.
        /// </summary>
        [Obsolete("Use TransferSubscriptionsAsync instead.")]
        public static bool TransferSubscriptions(
            this ISession session,
            SubscriptionCollection subscriptions,
            bool sendInitialValues)
        {
            return session.TransferSubscriptionsAsync(
                subscriptions,
                sendInitialValues).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use BrowseAsync instead.")]
        public static ResponseHeader Browse(
            this ISession session,
            RequestHeader requestHeader,
            ViewDescription view,
            NodeId nodeToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;

            ByteStringCollection continuationPoints;
            (responseHeader, continuationPoints, referencesList, errors) =
                session.BrowseAsync(
                    requestHeader,
                    view,
                    [nodeToBrowse],
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask)
                    .GetAwaiter()
                    .GetResult();

            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(continuationPoints.Count == 1);
            references = referencesList[0];
            continuationPoint = continuationPoints[0];
            return responseHeader;
        }

        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use BrowseNextAsync instead.")]
        public static ResponseHeader BrowseNext(
            this ISession session,
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out byte[] revisedContinuationPoint,
            out ReferenceDescriptionCollection references)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;

            ByteStringCollection revisedContinuationPoints;
            (responseHeader, revisedContinuationPoints, referencesList, errors) =
                session.BrowseNextAsync(
                    requestHeader,
                    [continuationPoint],
                    releaseContinuationPoint)
                    .GetAwaiter()
                    .GetResult();
            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(revisedContinuationPoints.Count == 1);
            references = referencesList[0];
            revisedContinuationPoint = revisedContinuationPoints[0];
            return responseHeader;
        }

        /// <summary>
        /// Execute browse and, if necessary, browse next in one service call.
        /// Takes care of BadNoContinuationPoint and BadInvalidContinuationPoint status codes.
        /// </summary>
        [Obsolete("Use ManagedBrowseAsync instead.")]
        public static void ManagedBrowse(
            this ISession session,
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            out IList<ReferenceDescriptionCollection> result,
            out IList<ServiceResult> errors)
        {
            (result, errors) = session.ManagedBrowseAsync(
                requestHeader,
                view,
                nodesToBrowse,
                maxResultsToReturn,
                browseDirection,
                referenceTypeId,
                includeSubtypes,
                nodeClassMask)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Calls the specified method and returns the output arguments.
        /// </summary>
        [Obsolete("Use CallAsync instead.")]
        public static IList<object> Call(
            this ISession session,
            NodeId objectId,
            NodeId methodId,
            params object[] args)
        {
            return session.CallAsync(
                objectId,
                methodId,
                default,
                args)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a republish request.
        /// </summary>
        [Obsolete("Use RepublishAsync instead.")]
        public static bool Republish(
            this ISession session,
            uint subscriptionId,
            uint sequenceNumber,
            out ServiceResult error)
        {
            (bool result, error) = session.RepublishAsync(
                subscriptionId,
                sequenceNumber)
                .GetAwaiter()
                .GetResult();
            return result;
        }

        /// <summary>
        /// Call the ResendData method on the server for all subscriptions.
        /// </summary>
        [Obsolete("Use ResendDataAsync instead.")]
        public static bool ResendData(
            this ISession session,
            IEnumerable<Subscription> subscriptions,
            out IList<ServiceResult> errors)
        {
            (bool result, errors) = session.ResendDataAsync(subscriptions)
                .GetAwaiter()
                .GetResult();
            return result;
        }
    }
}
