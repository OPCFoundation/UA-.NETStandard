/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua;

/// <summary>
/// Desktop-shell event IDs. The shell reserves the 400-419 range, separate from
/// the ConnectionService (0-99), Main/Document (100-299) and ResourceMonitor
/// (300-399) ranges documented elsewhere.
/// </summary>
internal static partial class UaLensEventIds
{
    public const int ShellNodeSelectionFailed = 400;
    public const int ShellNodeActionFailed = 401;
    public const int ShellConnectFailed = 402;
    public const int ShellWorkspaceOperationFailed = 403;
    public const int ShellTabActionFailed = 404;
    public const int ShellCommandDispatchFailed = 405;
}
