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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService
    {
        internal IDisposable RegisterLifecycleCoordinator(WotMaterializationCoordinator coordinator)
        {
            _ = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            if (Interlocked.CompareExchange(ref m_lifecycleCoordinator, coordinator, null) is not null)
            {
                throw new InvalidOperationException("The registry already has a hosted lifecycle coordinator.");
            }
            return new LifecycleRegistration(() =>
                Interlocked.CompareExchange(ref m_lifecycleCoordinator, null, coordinator));
        }

        internal async ValueTask<WotRegistryLifecyclePlan> PlanVersionLifecycleAsync(
            string groupId, string resourceId, string versionId, WoTDeletePolicyEnum policy,
            long? expectedEpoch, CancellationToken cancellationToken)
        {
            groupId = ResolveAssignedGroupId(groupId);
            resourceId = ResolveAssignedResourceId(groupId, resourceId);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot previous = m_snapshot;
                WotResourceGroup? group = previous.FindGroup(groupId);
                WotResource? resource = group?.Resources.GetValueOrDefault(resourceId);
                WotResourceVersion? version = resource?.FindVersion(versionId);
                if (group is null || resource is null || version is null)
                {
                    return new WotRegistryLifecyclePlan(null, resource, DeleteRefused(
                        WoTOutcomeEnum.Failed, policy, previous.Generation, "Version not found."));
                }
                if (expectedEpoch is { } epoch && epoch != version.Epoch)
                {
                    return new WotRegistryLifecyclePlan(null, resource, DeleteRefused(
                        WoTOutcomeEnum.Rejected, policy, previous.Generation, "Epoch mismatch."));
                }
                if (resource.Versions.Length == 1)
                {
                    return await PlanResourceLifecycleLockedAsync(
                        previous, resource, true, policy, cancellationToken).ConfigureAwait(false);
                }
                WotRegistryLifecyclePlan? retirement = resource.ActiveVersionId == versionId
                    ? await PlanResourceLifecycleLockedAsync(
                        previous, resource, false, policy, cancellationToken).ConfigureAwait(false)
                    : null;
                if (retirement is { Mutation: null })
                {
                    return retirement;
                }
                (WotRegistrySnapshot desired, WotRegistryMutationResult result) = PlanVersionDeletion(
                    previous, group, resource, version);
                if (!result.Changed)
                {
                    return new WotRegistryLifecyclePlan(null, resource, DeleteRefused(
                        result.Outcome, policy, previous.Generation, result.Message));
                }
                var changed = new HashSet<string>(StringComparer.Ordinal) { resource.Xid };
                if (retirement?.Mutation is { } mutation)
                {
                    foreach (string xid in mutation.ChangedResourceXids)
                    {
                        changed.Add(xid);
                        if (xid != resource.Xid && mutation.Desired.FindResourceByXid(xid) is { } dependent)
                        {
                            desired = WithResource(desired, dependent, desired.Generation);
                        }
                    }
                }
                return new WotRegistryLifecyclePlan(new WotRegistryMutationImage(
                    previous, desired.WithPublicationState(previous.Generation, previous.RefreshGeneration),
                    changed.ToArrayOf()), resource, new WotDeleteResult(
                        WoTOutcomeEnum.Success, policy, previous.Generation + 1, true,
                        resource.ActiveVersionId == versionId,
                        retirement?.Result.Dependents ?? [], retirement?.Result.Unloaded ?? [],
                        retirement?.Result.Failed ?? [], retirement?.Result.Unreadable ?? [],
                        "The exact Version was deleted."));
            }
            finally
            {
                m_mutex.Release();
            }
        }

        internal async ValueTask<WotRegistryLifecyclePlan> PlanGroupLifecycleAsync(
            string groupId, WoTDeletePolicyEnum policy, long? expectedEpoch, CancellationToken cancellationToken)
        {
            groupId = ResolveAssignedGroupId(groupId);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot previous = m_snapshot;
                WotResourceGroup? group = previous.FindGroup(groupId);
                if (group is null)
                {
                    return new WotRegistryLifecyclePlan(null, null, DeleteRefused(
                        WoTOutcomeEnum.Failed, policy, previous.Generation, "Group not found."));
                }
                if (expectedEpoch is { } epoch && epoch != group.Epoch)
                {
                    return new WotRegistryLifecyclePlan(null, null, DeleteRefused(
                        WoTOutcomeEnum.Rejected, policy, previous.Generation, "Epoch mismatch."));
                }
                WotRegistrySnapshot desired = previous;
                var changed = new HashSet<string>(StringComparer.Ordinal) { group.Xid };
                var dependents = new HashSet<string>(StringComparer.Ordinal);
                var unloaded = new HashSet<string>(StringComparer.Ordinal);
                var failed = new HashSet<string>(StringComparer.Ordinal);
                var unreadable = new HashSet<string>(StringComparer.Ordinal);
                ArrayOf<string> cohort = group.Resources.Values.Select(resource => resource.Xid).ToArrayOf();
                foreach (WotResource resource in group.Resources.Values)
                {
                    WotRegistryLifecyclePlan plan = await PlanResourceLifecycleLockedAsync(
                        desired, resource, true, policy, cancellationToken, cohort).ConfigureAwait(false);
                    if (plan.Mutation is null)
                    {
                        return new WotRegistryLifecyclePlan(null, null, plan.Result);
                    }
                    desired = plan.Mutation.Desired;
                    changed.UnionWith(plan.Mutation.ChangedResourceXids.ToList());
                    dependents.UnionWith(plan.Result.Dependents);
                    unloaded.UnionWith(plan.Result.Unloaded);
                    failed.UnionWith(plan.Result.Failed);
                    unreadable.UnionWith(plan.Result.Unreadable);
                }
                desired = desired.WithoutGroup(groupId, previous.Generation);
                return new WotRegistryLifecyclePlan(new WotRegistryMutationImage(
                    previous, desired, changed.ToArrayOf()), null, new WotDeleteResult(
                        WoTOutcomeEnum.Success, policy, previous.Generation + 1, true, true,
                        dependents.ToImmutableArray(), unloaded.ToImmutableArray(), failed.ToImmutableArray(),
                        unreadable.ToImmutableArray(), "The group and its Resources were deleted."));
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryMutationResult> DeleteThroughLifecycleAsync(
            WotMaterializationCoordinator coordinator,
            string groupId,
            string resourceId,
            long? expectedEpoch,
            CancellationToken cancellationToken)
        {
            WotDeleteOutcome result = await coordinator.DeleteAsync(new WotDeleteRequest
            {
                GroupId = groupId, ResourceId = resourceId,
                ExpectedEpoch = expectedEpoch, Policy = coordinator.DeletePolicy
            }, cancellationToken).ConfigureAwait(false);
            return new WotRegistryMutationResult(
                result.Delete.Outcome, Current.FindResource(groupId, resourceId),
                result.Delete.Generation, [], result.Delete.Message);
        }

        private WotMaterializationCoordinator? m_lifecycleCoordinator;

        private sealed class LifecycleRegistration(Action release) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = release;
        }
    }
}
