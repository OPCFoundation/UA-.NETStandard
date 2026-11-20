// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using UaLens.Workspace;

namespace UaLens.Tests.ViewModels;

internal sealed class ControlledWorkspaceDispatcher : IWorkspaceDispatcher
{
    private readonly AvaloniaWorkspaceDispatcher m_owner = new();
    private readonly Lock m_gate = new();
    private PausedInvocation? m_next;

    public PausedInvocation PauseNext()
    {
        lock (m_gate)
        {
            if (m_next is not null)
            {
                throw new InvalidOperationException("A dispatcher pause is already reserved.");
            }
            return m_next = new PausedInvocation();
        }
    }

    public void VerifyAccess() => m_owner.VerifyAccess();

    public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        PausedInvocation? pause;
        lock (m_gate)
        {
            pause = m_next;
            m_next = null;
        }
        if (pause is null)
        {
            await m_owner.InvokeAsync(action, cancellationToken).ConfigureAwait(false);
            return;
        }
        pause.Entered.TrySetResult();
        try
        {
            await pause.Release.Task.ConfigureAwait(false);
            await m_owner.InvokeAsync(action, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pause.Completed.TrySetResult();
        }
    }

    internal sealed class PausedInvocation
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ResumeAsync()
        {
            Release.TrySetResult();
            await Completed.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
    }
}
