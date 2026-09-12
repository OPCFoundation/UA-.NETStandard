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
