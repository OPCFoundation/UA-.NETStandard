/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UaLens.Workspace;

/// <summary>
/// Document lifecycle independent of its desktop presentation. Connection work must
/// finish, including cleanup after cancellation, before the returned task completes.
/// </summary>
internal interface IWorkspaceDocument : IAsyncDisposable
{
    string Title { get; set; }

    void OnActivated();

    void OnDeactivated();

    Task OnConnectionStateChangedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A prepared, non-secret document configuration. Configuration must not start a
/// benchmark or other write workload. The workspace owns the factory result immediately.
/// </summary>
internal sealed record DocumentRestore<TDocument>(
    Func<TDocument> Create,
    Func<TDocument, CancellationToken, Task>? ConfigureAsync = null)
    where TDocument : class, IWorkspaceDocument;
