// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.ViewModels;

internal static partial class BrowserViewModelLog
{
    [LoggerMessage(EventId = UaLensEventIds.BrowserViewModel, Level = LogLevel.Warning,
        Message = "Browse failed for {NodeId}")]
    public static partial void BrowseFailed(ILogger logger, Exception exception, NodeId nodeId);

    [LoggerMessage(EventId = UaLensEventIds.BrowserViewModel + 1, Level = LogLevel.Debug,
        Message = "GetChildVariablesAsync failed for {NodeId}")]
    public static partial void ChildVariablesFailed(ILogger logger, Exception exception, NodeId nodeId);

    [LoggerMessage(EventId = UaLensEventIds.BrowserViewModel + 2, Level = LogLevel.Debug,
        Message = "Parse RelativePath '{Path}' failed.")]
    public static partial void RelativePathFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = UaLensEventIds.BrowserViewModel + 3, Level = LogLevel.Warning,
        Message = "TranslateBrowsePathsToNodeIds failed.")]
    public static partial void TranslateFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = UaLensEventIds.BrowserViewModel + 4, Level = LogLevel.Debug,
        Message = "EventNotifier read failed for {NodeId}")]
    public static partial void EventNotifierFailed(ILogger logger, Exception exception, NodeId nodeId);
}
