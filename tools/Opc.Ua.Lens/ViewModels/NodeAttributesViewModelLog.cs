// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.ViewModels;

internal static partial class NodeAttributesViewModelLog
{
    [LoggerMessage(EventId = UaLensEventIds.NodeAttributesViewModel, Level = LogLevel.Warning,
        Message = "Failed to read attributes for {NodeId}")]
    public static partial void AttributeReadFailed(ILogger logger, Exception exception, NodeId nodeId);
}
