/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
