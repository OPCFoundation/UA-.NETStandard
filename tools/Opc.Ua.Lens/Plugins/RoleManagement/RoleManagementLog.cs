/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Source-generated logging for <see cref="RoleManagementPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.RoleManagementBase"/>.
/// </summary>
internal static partial class RoleManagementLog
{
    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 0, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} ListRoles failed.")]
    public static partial void RoleListRolesFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 1, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} AddRole dialog failed.")]
    public static partial void RoleAddDialogFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 2, Level = LogLevel.Information,
        Message = "Role Management tab {Title}: added role {Name} → {Id}.")]
    public static partial void RoleAdded(this ILogger logger, string title, string name, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 3, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} AddRole({Name}) failed.")]
    public static partial void RoleAddFailed(this ILogger logger, Exception exception, string title, string name);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 4, Level = LogLevel.Information,
        Message = "Role Management tab {Title}: removed role {Name} ({Id}).")]
    public static partial void RoleRemoved(this ILogger logger, string title, string name, NodeId id);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 5, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} RemoveRole({Name}) failed.")]
    public static partial void RoleRemoveFailed(this ILogger logger, Exception exception, string title, string name);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 6, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} AddIdentity failed.")]
    public static partial void RoleAddIdentityFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 7, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} RemoveIdentity failed.")]
    public static partial void RoleRemoveIdentityFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 8, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} AddApplication failed.")]
    public static partial void RoleAddApplicationFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 9, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} RemoveApplication failed.")]
    public static partial void RoleRemoveApplicationFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 10, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} SetApplicationsExclude failed.")]
    public static partial void RoleSetApplicationsExcludeFailed(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 11, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} AddEndpoint failed.")]
    public static partial void RoleAddEndpointFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 12, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} RemoveEndpoint failed.")]
    public static partial void RoleRemoveEndpointFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 13, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} SetEndpointsExclude failed.")]
    public static partial void RoleSetEndpointsExcludeFailed(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.RoleManagementBase + 14, Level = LogLevel.Warning,
        Message = "Role Management tab {Title} SetCustomConfiguration failed.")]
    public static partial void RoleSetCustomConfigurationFailed(
        this ILogger logger, Exception exception, string title);
}
