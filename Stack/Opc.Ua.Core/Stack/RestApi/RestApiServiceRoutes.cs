/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A single OPC UA REST route as defined by the spec OpenAPI document
    /// <c>opc.ua.openapi.allservices.json</c>: a lowercase URL path
    /// (e.g. <c>/read</c>), the spec <c>operationId</c> (e.g. <c>Read</c>),
    /// and the concrete CLR <see cref="IServiceRequest"/> /
    /// <see cref="IServiceResponse"/> pair that carries the body for that
    /// service.
    /// </summary>
    /// <param name="Path">
    /// The route path including its leading slash, e.g. <c>/read</c>.
    /// </param>
    /// <param name="OperationId">
    /// The OpenAPI <c>operationId</c> for this route, e.g. <c>Read</c>.
    /// </param>
    /// <param name="RequestType">
    /// The CLR <see cref="IServiceRequest"/> type used to decode the body
    /// of a request to this route.
    /// </param>
    /// <param name="ResponseType">
    /// The CLR <see cref="IServiceResponse"/> type used to encode the body
    /// of a response from this route.
    /// </param>
    public readonly record struct RestApiServiceRoute(
        string Path,
        string OperationId,
        Type RequestType,
        Type ResponseType);

    /// <summary>
    /// Static route table for the HTTPS REST binding (OPC UA Part 6 §G.3
    /// "OpenAPI Mapping", v1.05.07). Each entry maps a lowercase URL path
    /// from <c>opc.ua.openapi.allservices.json</c> to the concrete CLR
    /// request / response types whose bodies appear at the root of the
    /// JSON document for that path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coverage is the full <c>allservices</c> doc: 28 services across
    /// Discovery, Session lifecycle, Attribute (Read / Write / History),
    /// View (Browse / BrowseNext / Translate / Register / Unregister),
    /// Method (Call), Subscription, and MonitoredItem. The spec doc
    /// deliberately omits NodeManagement (Part 4 §5.8) and Query
    /// (§5.10) — those are not exposed by the REST binding.
    /// </para>
    /// <para>
    /// Path lookup is case-insensitive (consistent with HTTP route
    /// matching). The leading slash is significant; paths do not carry
    /// trailing slashes or query strings.
    /// </para>
    /// <para>
    /// The route table is the single source of truth for both controller
    /// registration on the server side and request dispatch on the
    /// client side; integration tests drive every entry by enumerating
    /// <see cref="Routes"/>.
    /// </para>
    /// </remarks>
    public static class RestApiServiceRoutes
    {
        private static readonly RestApiServiceRoute[] s_routes =
        [
            // Attribute service set (Part 4 §5.11)
            new("/read", "Read",
                typeof(ReadRequest), typeof(ReadResponse)),
            new("/write", "Write",
                typeof(WriteRequest), typeof(WriteResponse)),
            new("/historyread", "HistoryRead",
                typeof(HistoryReadRequest), typeof(HistoryReadResponse)),
            new("/historyupdate", "HistoryUpdate",
                typeof(HistoryUpdateRequest), typeof(HistoryUpdateResponse)),
            // Method service set (Part 4 §5.12)
            new("/call", "Call",
                typeof(CallRequest), typeof(CallResponse)),
            // View service set (Part 4 §5.9)
            new("/browse", "Browse",
                typeof(BrowseRequest), typeof(BrowseResponse)),
            new("/browsenext", "BrowseNext",
                typeof(BrowseNextRequest), typeof(BrowseNextResponse)),
            new("/translate", "TranslateBrowsePathsToNodeIds",
                typeof(TranslateBrowsePathsToNodeIdsRequest),
                typeof(TranslateBrowsePathsToNodeIdsResponse)),
            new("/registernodes", "RegisterNodes",
                typeof(RegisterNodesRequest), typeof(RegisterNodesResponse)),
            new("/unregisternodes", "UnregisterNodes",
                typeof(UnregisterNodesRequest), typeof(UnregisterNodesResponse)),
            // Discovery service set (Part 4 §5.5)
            new("/findservers", "FindServers",
                typeof(FindServersRequest), typeof(FindServersResponse)),
            new("/getendpoints", "GetEndpoints",
                typeof(GetEndpointsRequest), typeof(GetEndpointsResponse)),
            // Session service set (Part 4 §5.7)
            new("/createsession", "CreateSession",
                typeof(CreateSessionRequest), typeof(CreateSessionResponse)),
            new("/activatesession", "ActivateSession",
                typeof(ActivateSessionRequest), typeof(ActivateSessionResponse)),
            new("/closesession", "CloseSession",
                typeof(CloseSessionRequest), typeof(CloseSessionResponse)),
            new("/cancel", "Cancel",
                typeof(CancelRequest), typeof(CancelResponse)),
            // MonitoredItem service set (Part 4 §5.13)
            new("/createmonitoreditems", "CreateMonitoredItems",
                typeof(CreateMonitoredItemsRequest), typeof(CreateMonitoredItemsResponse)),
            new("/modifymonitoreditems", "ModifyMonitoredItems",
                typeof(ModifyMonitoredItemsRequest), typeof(ModifyMonitoredItemsResponse)),
            new("/setmonitoringmode", "SetMonitoringMode",
                typeof(SetMonitoringModeRequest), typeof(SetMonitoringModeResponse)),
            new("/settriggering", "SetTriggering",
                typeof(SetTriggeringRequest), typeof(SetTriggeringResponse)),
            new("/deletemonitoreditems", "DeleteMonitoredItems",
                typeof(DeleteMonitoredItemsRequest), typeof(DeleteMonitoredItemsResponse)),
            // Subscription service set (Part 4 §5.14)
            new("/createsubscription", "CreateSubscription",
                typeof(CreateSubscriptionRequest), typeof(CreateSubscriptionResponse)),
            new("/modifysubscription", "ModifySubscription",
                typeof(ModifySubscriptionRequest), typeof(ModifySubscriptionResponse)),
            new("/setpublishingmode", "SetPublishingMode",
                typeof(SetPublishingModeRequest), typeof(SetPublishingModeResponse)),
            new("/publish", "Publish",
                typeof(PublishRequest), typeof(PublishResponse)),
            new("/republish", "Republish",
                typeof(RepublishRequest), typeof(RepublishResponse)),
            new("/transfersubscriptions", "TransferSubscriptions",
                typeof(TransferSubscriptionsRequest), typeof(TransferSubscriptionsResponse)),
            new("/deletesubscriptions", "DeleteSubscriptions",
                typeof(DeleteSubscriptionsRequest), typeof(DeleteSubscriptionsResponse))
        ];

        private static readonly FrozenDictionary<string, RestApiServiceRoute> s_byPath
            = s_routes.ToFrozenDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        private static readonly FrozenDictionary<string, RestApiServiceRoute> s_byOperationId
            = s_routes.ToFrozenDictionary(r => r.OperationId, StringComparer.OrdinalIgnoreCase);
        private static readonly FrozenDictionary<Type, RestApiServiceRoute> s_byRequestType
            = s_routes.ToFrozenDictionary(r => r.RequestType);

        /// <summary>
        /// All routes defined by <c>opc.ua.openapi.allservices.json</c>,
        /// grouped by service set in spec order.
        /// </summary>
        public static IReadOnlyList<RestApiServiceRoute> Routes => s_routes;

        /// <summary>
        /// Number of routes (services) exposed by the REST binding. Matches
        /// the count in <c>opc.ua.openapi.allservices.json</c>.
        /// </summary>
        public static int Count => s_routes.Length;

        /// <summary>
        /// Looks up a route by its URL path (e.g. <c>/read</c>).
        /// Case-insensitive.
        /// </summary>
        /// <param name="path">
        /// The URL path including its leading slash.
        /// </param>
        /// <param name="route">On success, the matching route entry.</param>
        /// <returns>
        /// <c>true</c> if the path identifies a known service; otherwise
        /// <c>false</c>.
        /// </returns>
        public static bool TryGetByPath(string? path, out RestApiServiceRoute route)
        {
            if (string.IsNullOrEmpty(path))
            {
                route = default;
                return false;
            }
            return s_byPath.TryGetValue(path, out route);
        }

        /// <summary>
        /// Looks up a route by its OpenAPI <c>operationId</c>
        /// (e.g. <c>Read</c>). Case-insensitive.
        /// </summary>
        /// <param name="operationId">
        /// The OpenAPI <c>operationId</c> string.
        /// </param>
        /// <param name="route">On success, the matching route entry.</param>
        /// <returns>
        /// <c>true</c> if the operation identifier matches a known service;
        /// otherwise <c>false</c>.
        /// </returns>
        public static bool TryGetByOperationId(string? operationId, out RestApiServiceRoute route)
        {
            if (string.IsNullOrEmpty(operationId))
            {
                route = default;
                return false;
            }
            return s_byOperationId.TryGetValue(operationId, out route);
        }

        /// <summary>
        /// Looks up a route by its <see cref="IServiceRequest"/> CLR type
        /// (e.g. <c>typeof(ReadRequest)</c>).
        /// </summary>
        /// <param name="requestType">The request type to look up.</param>
        /// <param name="route">On success, the matching route entry.</param>
        /// <returns>
        /// <c>true</c> if the type identifies a known service; otherwise
        /// <c>false</c>.
        /// </returns>
        public static bool TryGetByRequestType(Type? requestType, out RestApiServiceRoute route)
        {
            if (requestType == null)
            {
                route = default;
                return false;
            }
            return s_byRequestType.TryGetValue(requestType, out route);
        }
    }
}
