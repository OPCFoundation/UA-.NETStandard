// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The set of OPC UA services the REST binding (OPC 10000-6, G.3
    /// "OpenAPI Mapping") exposes. Selects both the routes that are
    /// mapped and the routes an OpenAPI document describes.
    /// </summary>
    public enum WebApiServiceSet
    {
        /// <summary>
        /// All 28 services in <see cref="WebApiServiceRoutes.Routes"/>:
        /// Discovery, Session, View, Attribute, Method, MonitoredItem and
        /// Subscription.
        /// </summary>
        AllServices = 0,

        /// <summary>
        /// The eight services a client can invoke without creating a
        /// session (OPC 10000-4, 6.3): Read, Write, HistoryRead,
        /// HistoryUpdate, Call, Browse, BrowseNext and
        /// TranslateBrowsePathsToNodeIds.
        /// </summary>
        Sessionless
    }
}
