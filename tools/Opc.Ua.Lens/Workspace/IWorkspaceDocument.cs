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
