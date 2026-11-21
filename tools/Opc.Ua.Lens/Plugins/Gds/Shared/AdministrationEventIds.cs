// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua;

internal static partial class UaLensEventIds
{
    /// <summary>
    /// Administration and discovery tools reserve the source-generated
    /// logging event-id block 3000-5999. Each owned tool takes a base
    /// below and offsets its per-message ids from it. Keep these ranges
    /// non-overlapping when adding messages.
    /// </summary>
    public const int CertificateManagerBase = 3000;

    public const int FileSystemBase = 3050;

    public const int FileSystemNodeBase = 3090;

    public const int GdsDiscoveryBase = 3100;

    public const int GdsManagementBase = 3200;

    public const int GdsPushBase = 3300;

    public const int GdsSessionBase = 3400;

    public const int RoleManagementBase = 3450;

    public const int UserManagementBase = 3500;
}
