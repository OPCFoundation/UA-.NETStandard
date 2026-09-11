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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    public sealed partial class XRegistrySynchronizer
    {
        private async ValueTask DeletePreparedAsync(
            Pass pass, XRegistrySyncObservation destination, XRegistrySyncSide side,
            CancellationToken cancellationToken)
        {
            XRegistrySyncInventory inventory = pass.Inventory(side);
            IXRegistryEndpoint endpoint = side == XRegistrySyncSide.OpcUa ? m_opcUa : m_http;
            if (!Qualified(inventory) ||
                inventory.Description is not { SupportsPreparedMutations: true } ||
                endpoint is not IXRegistryPreparedEndpoint ||
                destination.Kind == XRegistrySyncEntityKind.Registry)
            {
                Unsupported(pass, destination.Path,
                    "Subtree and Version deletion require a prepared destination with global mutation invalidation.");
                return;
            }
            string deletePath = destination.Kind == XRegistrySyncEntityKind.ResourceMeta
                ? XRegistrySyncModel.Parent(destination.Path) : destination.Path;
            string scope = destination.Kind == XRegistrySyncEntityKind.Version
                ? XRegistrySyncModel.ResourcePath(destination.Path) : deletePath;
            ArrayOf<XRegistrySyncObservation> before =
                [.. inventory.Entries.Values.Where(entry => Within(entry.Path, scope))];
            foreach (XRegistrySyncObservation entry in before)
            {
                if (!Within(entry.Path, deletePath) || entry.Path == destination.Path)
                {
                    continue;
                }
                if (!pass.State.Baselines.TryGetValue(entry.Path, out XRegistrySyncBaseline? baseline) ||
                    entry.Fingerprint != (side == XRegistrySyncSide.OpcUa ? baseline.OpcUa : baseline.Http).Fingerprint)
                {
                    Unsupported(pass, destination.Path,
                        "A new or edited descendant lacks an unchanged synchronization baseline.");
                    return;
                }
            }
            if (pass.State.Conflicts.Values.Any(conflict => XRegistrySyncStateManager.IsActive(conflict) &&
                conflict.Path != destination.Path &&
                Within(conflict.Path, scope)))
            {
                Unsupported(pass, destination.Path, "A descendant conflict prevents subtree deletion.");
                return;
            }
            if (!await CheckScopeAsync(pass, cancellationToken).ConfigureAwait(false) ||
                !TakeOperation(pass, destination.Path))
            {
                return;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord(destination.Path, XRegistrySyncRecordKind.Planned,
                    $"Would prepare deletion at {side}, verify descendants and commit with global invalidation."));
                foreach (XRegistrySyncObservation entry in before)
                {
                    if (Within(entry.Path, deletePath))
                    {
                        pass.Processed.Add(entry.Path);
                    }
                }
                return;
            }
            var request = new XRegistryRequest(XRegistryAction.Delete, deletePath)
            {
                View = XRegistryView.Metadata,
                Parameters = [new XRegistryParameter("epoch", destination.Epoch)]
            };
            await PerformAsync(pass, destination.Path, side, XRegistrySyncIntentKind.Delete, request,
                [], before, cancellationToken, preparedDeletion: true).ConfigureAwait(false);
        }

        private async ValueTask<PreparedDeletionResult> CommitPreparedDeletionAsync(
            Pass pass, XRegistrySyncIntent intent, CancellationToken cancellationToken)
        {
            IXRegistryEndpoint target = intent.Destination == XRegistrySyncSide.OpcUa ? m_opcUa : m_http;
            if (target is not IXRegistryPreparedEndpoint preparedEndpoint)
            {
                return new(null, "The destination no longer exposes prepared mutations; no deletion was attempted.");
            }
            XRegistryCallContext context = intent.Destination == XRegistrySyncSide.OpcUa
                ? m_options.OpcUaContext : m_options.HttpContext;
            // Ownership transfers to late cleanup on timeout; otherwise the finally releases it.
            // TODO: Remove when CA2000 models asynchronous ownership transfer across these helpers.
#pragma warning disable CA2000
            var deadline = new XRegistrySyncDeadline(m_timeProvider, m_options.RequestTimeout, cancellationToken);
#pragma warning restore CA2000
            bool deferredCleanup = false;
            IXRegistryPreparedOperation? operation = null;
            try
            {
                Task<IXRegistryPreparedOperation> pendingPreparation = preparedEndpoint.PrepareAsync(
                    intent.Request with { Context = context }, deadline.Token).AsTask();
                try
                {
                    operation = await pendingPreparation.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
                {
                    deferredCleanup = true;
                    _ = ReleaseLatePreparationAsync(pendingPreparation, deadline);
                    throw;
                }
                if (!operation.Response.IsSuccess)
                {
                    return new(operation.Response, null);
                }

                string scope = intent.Path == intent.Request.Path &&
                    XRegistryPath.GetSegments(intent.Path).Count == 6
                        ? XRegistrySyncModel.ResourcePath(intent.Path) : intent.Request.Path;
                var reader = new XRegistrySyncInventoryReader(
                    target, context, m_options, m_timeProvider, intent.Destination);
                XRegistrySyncInventory current = await reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                XRegistrySyncInventory prior = pass.Inventory(intent.Destination);
                if (!current.Complete ||
                    !current.ScopeStable ||
                    current.Description?.RegistryId != prior.Description!.RegistryId ||
                    current.Model?.Signature != prior.Model!.Signature ||
                    current.Description is not { SupportsPreparedMutations: true })
                {
                    return new(null,
                        "The destination inventory is incomplete or changed after preparation; deletion aborted.");
                }
                XRegistrySyncObservation[] scoped = [.. current.Entries.Values.Where(entry => Within(entry.Path,
                    scope))];
                Dictionary<string, XRegistrySyncObservation> expected = intent.DestinationBefore.Span.ToArray()
                    .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
                if (scoped.Length != intent.DestinationBefore.Count ||
                    !scoped.All(entry => expected.TryGetValue(entry.Path, out XRegistrySyncObservation? old) &&
                        SameObservation(old, entry)))
                {
                    foreach (XRegistrySyncObservation entry in scoped)
                    {
                        prior.Entries[entry.Path] = entry;
                    }
                    return new(null,
                        "Descendant observations changed after preparation; deletion aborted.");
                }
                if (!await CheckScopeAsync(pass, deadline.Token).ConfigureAwait(false))
                {
                    return new(null, "Registry scope changed after preparation; deletion aborted.");
                }
                XRegistrySyncObservation? reappeared = await pass.Reader(Other(intent.Destination)).ReadEntityAsync(
                    intent.Path, pass.Inventory(Other(intent.Destination)).Model!,
                        deadline.Token).ConfigureAwait(false);
                if (reappeared is not null)
                {
                    SetObservation(pass.Inventory(Other(intent.Destination)), intent.Path, reappeared);
                    return new(null, "The source identity reappeared after preparation; deletion aborted.");
                }
                // Global-generation invalidation closes the race between the checked
                // descendant observations and publication; a target-only epoch cannot.
                Task<XRegistryResponse> pendingCommit = operation.CommitAsync(deadline.Token).AsTask();
                try
                {
                    XRegistryResponse committed = await pendingCommit.WaitAsync(deadline.Token).ConfigureAwait(false);
                    return new(committed, null);
                }
                catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
                {
                    deferredCleanup = true;
                    _ = ReleaseLateCommitAsync(pendingCommit, operation, deadline);
                    throw;
                }
            }
            finally
            {
                if (!deferredCleanup)
                {
                    await ReleasePreparationAsync(operation, deadline).ConfigureAwait(false);
                }
            }
        }

        private async Task ReleaseLatePreparationAsync(
            Task<IXRegistryPreparedOperation> pending, XRegistrySyncDeadline deadline)
        {
            try
            {
                IXRegistryPreparedOperation? operation = null;
                try
                {
                    operation = await pending.ConfigureAwait(false);
                }
                finally
                {
                    await ReleasePreparationAsync(operation, deadline).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (XRegistrySyncDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                m_logger.PreparationCleanupFailed(exception);
            }
        }

        private async Task ReleaseLateCommitAsync(
            Task<XRegistryResponse> pending, IXRegistryPreparedOperation operation, XRegistrySyncDeadline deadline)
        {
            try
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                finally
                {
                    await ReleasePreparationAsync(operation, deadline).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (XRegistrySyncDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                m_logger.PreparationCleanupFailed(exception);
            }
        }

        private static async ValueTask ReleasePreparationAsync(
            IXRegistryPreparedOperation? operation, XRegistrySyncDeadline deadline)
        {
            try
            {
                if (operation is not null)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await deadline.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static bool Within(string path, string root)
        {
            return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private sealed record PreparedDeletionResult(XRegistryResponse? Response, string? Failure);
    }
}
