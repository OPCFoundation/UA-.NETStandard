/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Workspace;

/// <summary>
/// Dispatches document construction, collection notifications and lifecycle callbacks
/// to their owning context. Desktop and headless callers use different adapters here.
/// </summary>
internal interface IWorkspaceDispatcher
{
    void VerifyAccess();

    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatcher for documents without thread-affine presentation.
/// </summary>
internal sealed class InlineWorkspaceDispatcher : IWorkspaceDispatcher
{
    public static InlineWorkspaceDispatcher Instance { get; } = new();

    public void VerifyAccess()
    {
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }
}
