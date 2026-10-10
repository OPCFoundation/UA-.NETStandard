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
