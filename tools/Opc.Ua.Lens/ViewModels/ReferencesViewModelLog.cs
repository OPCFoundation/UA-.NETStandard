// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.ViewModels;

internal static partial class ReferencesViewModelLog
{
    [LoggerMessage(EventId = UaLensEventIds.ReferencesViewModel, Level = LogLevel.Warning,
        Message = "Failed to browse references for {NodeId}")]
    public static partial void ReferenceBrowseFailed(ILogger logger, Exception exception, NodeId nodeId);
}
