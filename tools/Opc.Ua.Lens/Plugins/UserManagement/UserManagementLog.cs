// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Source-generated logging for <see cref="UserManagementPlugin"/>.
/// Event ids are offset from <see cref="UaLensEventIds.UserManagementBase"/>.
/// </summary>
internal static partial class UserManagementLog
{
    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 0, Level = LogLevel.Debug,
        Message = "User Management tab {Title}: ReadPasswordRestrictions skipped.")]
    public static partial void UserReadPasswordRestrictionsSkipped(
        this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 1, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: Refresh failed.")]
    public static partial void UserRefreshFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 2, Level = LogLevel.Information,
        Message = "User Management tab {Title}: AddUser({User}) succeeded.")]
    public static partial void UserAddSucceeded(this ILogger logger, string title, string user);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 3, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: AddUser failed.")]
    public static partial void UserAddFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 4, Level = LogLevel.Information,
        Message = "User Management tab {Title}: ModifyUser({User}) succeeded.")]
    public static partial void UserModifySucceeded(this ILogger logger, string title, string user);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 5, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: ModifyUser failed.")]
    public static partial void UserModifyFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 6, Level = LogLevel.Information,
        Message = "User Management tab {Title}: RemoveUser({User}) succeeded.")]
    public static partial void UserRemoveSucceeded(this ILogger logger, string title, string user);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 7, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: RemoveUser failed.")]
    public static partial void UserRemoveFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 8, Level = LogLevel.Information,
        Message = "User Management tab {Title}: ChangePassword succeeded.")]
    public static partial void UserChangePasswordSucceeded(this ILogger logger, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 9, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: ChangePassword failed.")]
    public static partial void UserChangePasswordFailed(this ILogger logger, Exception exception, string title);

    [LoggerMessage(EventId = UaLensEventIds.UserManagementBase + 10, Level = LogLevel.Warning,
        Message = "User Management tab {Title}: client construction failed.")]
    public static partial void UserClientConstructionFailed(this ILogger logger, Exception exception, string title);
}
