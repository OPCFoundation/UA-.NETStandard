// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// Source-generated logging for <see cref="GdsDiscoveryPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.GdsDiscoveryBase"/>.
/// </summary>
internal static partial class GdsDiscoveryLog
{
    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 0, Level = LogLevel.Debug,
        Message = "GdsDiscovery: LDS dispose threw.")]
    public static partial void DiscoveryLdsDisposeThrew(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 1, Level = LogLevel.Debug,
        Message = "GdsDiscovery: GDS dispose threw.")]
    public static partial void DiscoveryGdsDisposeThrew(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 2, Level = LogLevel.Warning,
        Message = "GdsDiscovery: loading {Root} failed.")]
    public static partial void DiscoveryLoadRootFailed(this ILogger logger, Exception exception, string root);

    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 3, Level = LogLevel.Debug,
        Message = "GdsDiscovery: enumerating network interfaces failed.")]
    public static partial void DiscoveryEnumerateInterfacesFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 4, Level = LogLevel.Debug,
        Message = "GdsDiscovery: GetEndpoints({Url}) failed.")]
    public static partial void DiscoveryGetEndpointsFailed(this ILogger logger, Exception exception, string url);

    [LoggerMessage(EventId = UaLensEventIds.GdsDiscoveryBase + 5, Level = LogLevel.Warning,
        Message = "GdsDiscovery: loading favourites failed.")]
    public static partial void DiscoveryFavoritesLoadFailed(this ILogger logger, Exception exception);
}
