/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace UaLens.Workspace;

/// <summary>
/// Runs complete asynchronous document operations on Avalonia's original UI thread.
/// </summary>
internal sealed class AvaloniaWorkspaceDispatcher : IWorkspaceDispatcher
{
    public void VerifyAccess() => Dispatcher.UIThread.VerifyAccess();

    public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        Task operation = await Dispatcher.UIThread.InvokeAsync<Task>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            },
            DispatcherPriority.Normal,
            cancellationToken).GetTask().ConfigureAwait(false);
        await operation.ConfigureAwait(false);
    }
}
