/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Defines numerous re-useable utility functions.
    /// </summary>
    public static class FormUtils
    {
        /// <summary>
        /// The known event types which can be constructed by ConstructEvent()
        /// </summary>
        public static NodeId[] KnownEventTypes = new NodeId[] 
        {
            ObjectTypeIds.BaseEventType,
            ObjectTypeIds.ConditionType,
            ObjectTypeIds.DialogConditionType,
            ObjectTypeIds.AlarmConditionType,
            ObjectTypeIds.ExclusiveLimitAlarmType,
            ObjectTypeIds.NonExclusiveLimitAlarmType,
            ObjectTypeIds.AuditEventType,
            ObjectTypeIds.AuditUpdateMethodEventType
        };

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="useSecurity">if set to <c>true</c> select an endpoint that uses security.</param>
        /// <returns>The best available endpoint.</returns>
        public static EndpointDescription SelectEndpoint(string discoveryUrl, bool useSecurity)
        {
            // needs to add the '/discovery' back onto non-UA TCP URLs.
            if (!discoveryUrl.StartsWith(Utils.UriSchemeOpcTcp))
            {
                if (!discoveryUrl.EndsWith("/discovery"))
                {
                    discoveryUrl += "/discovery";
                }
            }

            // parse the selected URL.
            Uri uri = new Uri(discoveryUrl);

            // set a short timeout because this is happening in the drop down event.
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;

            EndpointDescription selectedEndpoint = null;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (DiscoveryClient client = DiscoveryClient.Create(uri, configuration))
            {
                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);

                // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
                for (int ii = 0; ii < endpoints.Count; ii++)
                {
                    EndpointDescription endpoint = endpoints[ii];

                    // check for a match on the URL scheme.
                    if (endpoint.EndpointUrl.StartsWith(uri.Scheme))
                    {
                        // pick the first available endpoint by default.
                        if (selectedEndpoint == null)
                        {
                            selectedEndpoint = endpoint;
                        }

                        // check if security was requested.
                        if (useSecurity)
                        {
                            if (endpoint.SecurityMode != MessageSecurityMode.None)
                            {
                                // The security level is a relative measure assigned by the server to the 
                                // endpoints that it returns. Clients should always pick the highest level
                                // unless they have a reason not too.
                                if (endpoint.SecurityLevel > selectedEndpoint.SecurityLevel)
                                {
                                    selectedEndpoint = endpoint;
                                }
                            }
                        }

                        // look for an unsecured endpoint if requested.
                        else
                        {
                            if (endpoint.SecurityMode == MessageSecurityMode.None)
                            {
                                selectedEndpoint = endpoint;
                            }
                        }
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null && endpoints.Count > 0)
                    {
                        selectedEndpoint = endpoints[0];
                    }
                }
            }

            // if a server is behind a firewall it may return URLs that are not accessible to the client.
            // This problem can be avoided by assuming that the domain in the URL used to call 
            // GetEndpoints can be used to access any of the endpoints. This code makes that conversion.
            // Note that the conversion only makes sense if discovery uses the same protocol as the endpoint.

            Uri endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);

            if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
            {
                UriBuilder builder = new UriBuilder(endpointUrl);
                builder.Host = uri.DnsSafeHost;
                builder.Port = uri.Port;
                selectedEndpoint.EndpointUrl = builder.ToString();
            }

            // return the selected endpoint.
            return selectedEndpoint;
        }

        /// <summary>
        /// Finds the type of the event for the notification.
        /// </summary>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="notification">The notification.</param>
        /// <returns>The NodeId of the EventType.</returns>
        public static NodeId FindEventType(MonitoredItem monitoredItem, EventFieldList notification)
        {
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            if (filter != null)
            {
                for (int ii = 0; ii < filter.SelectClauses.Count; ii++)
                {
                    SimpleAttributeOperand clause = filter.SelectClauses[ii];

                    if (clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == BrowseNames.EventType)
                    {
                        return notification.EventFields[ii].Value as NodeId;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Constructs an event object from a notification.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="monitoredItem">The monitored item that produced the notification.</param>
        /// <param name="notification">The notification.</param>
        /// <param name="eventTypeMappings">Mapping between event types and known event types.</param>
        /// <returns>
        /// The event object. Null if the notification is not a valid event type.
        /// </returns>
        public static BaseEventState ConstructEvent(
            Session session,
            MonitoredItem monitoredItem,
            EventFieldList notification,
            Dictionary<NodeId,NodeId> eventTypeMappings)
        {
            // find the event type.
            NodeId eventTypeId = FindEventType(monitoredItem, notification);

            if (eventTypeId == null)
            {
                return null;
            }

            // look up the known event type.
            NodeId knownTypeId = null;

            if (!eventTypeMappings.TryGetValue(eventTypeId, out knownTypeId))
            {
                // check for a known type
                for (int jj = 0; jj < KnownEventTypes.Length; jj++)
                {
                    if (KnownEventTypes[jj] == eventTypeId)
                    {
                        knownTypeId = eventTypeId;
                        eventTypeMappings.Add(eventTypeId, eventTypeId);
                        break;
                    }
                }
                
                // browse for the supertypes of the event type.
                if (knownTypeId == null)
                {
                    ReferenceDescriptionCollection supertypes = FormUtils.BrowseSuperTypes(session, eventTypeId, false);

                    // can't do anything with unknown types.
                    if (supertypes == null)
                    {
                        return null;
                    }

                    // find the first supertype that matches a known event type.
                    for (int ii = 0; ii < supertypes.Count; ii++)
                    {
                        for (int jj = 0; jj < KnownEventTypes.Length; jj++)
                        {
                            if (KnownEventTypes[jj] == supertypes[ii].NodeId)
                            {
                                knownTypeId = KnownEventTypes[jj];
                                eventTypeMappings.Add(eventTypeId, knownTypeId);
                                break;
                            }
                        }

                        if (knownTypeId != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (knownTypeId == null)
            {
                return null;
            }

            // all of the known event types have a UInt32 as identifier.
            uint? id = knownTypeId.Identifier as uint?;

            if (id == null)
            {
                return null;
            }

            // construct the event based on the known event type.
            BaseEventState e = null;

            switch (id.Value)
            {
                case ObjectTypes.ConditionType: { e = new ConditionState(null); break; }
                case ObjectTypes.DialogConditionType: { e = new DialogConditionState(null); break; }
                case ObjectTypes.AlarmConditionType: { e = new AlarmConditionState(null); break; }
                case ObjectTypes.ExclusiveLimitAlarmType: { e = new ExclusiveLimitAlarmState(null); break; }
                case ObjectTypes.NonExclusiveLimitAlarmType: { e = new NonExclusiveLimitAlarmState(null); break; }
                case ObjectTypes.AuditEventType: { e = new AuditEventState(null); break; }
                case ObjectTypes.AuditUpdateMethodEventType: { e = new AuditUpdateMethodEventState(null); break; }

                default:
                {
                    e = new BaseEventState(null);
                    break;
                }
            }

            // get the filter which defines the contents of the notification.
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            // initialize the event with the values in the notification.
            e.Update(session.SystemContext, filter.SelectClauses, notification);

            // save the orginal notification.
            e.Handle = notification;

            return e;
        }
        
        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeToBrowse">The NodeId for the starting node.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static ReferenceDescriptionCollection Browse(Session session, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            try
            {
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                nodesToBrowse.Add(nodeToBrowse);

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Browse(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                do
                {
                    // check for error.
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        throw new ServiceResultException(results[0].StatusCode);
                    }

                    // process results.
                    for (int ii = 0; ii < results[0].References.Count; ii++)
                    {
                        references.Add(results[0].References[ii]);
                    }

                    // check if all references have been fetched.
                    if (results[0].References.Count == 0 || results[0].ContinuationPoint == null)
                    {
                        break;
                    }

                    // continue browse operation.
                    ByteStringCollection continuationPoints = new ByteStringCollection();
                    continuationPoints.Add(results[0].ContinuationPoint);

                    session.BrowseNext(
                        null,
                        false,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                }
                while (true);

                //return complete list.
                return references;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Browses the address space and returns all of the supertypes of the specified type node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="typeId">The NodeId for a type node in the address space.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static ReferenceDescriptionCollection BrowseSuperTypes(Session session, NodeId typeId, bool throwOnError)
        {
            ReferenceDescriptionCollection supertypes = new ReferenceDescriptionCollection();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types. 
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;
                                
                ReferenceDescriptionCollection references = Browse(session, nodeToBrowse, throwOnError);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]); 

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = Browse(session, nodeToBrowse, throwOnError);
                }

                // return complete list.
                return supertypes;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }
    }
}
