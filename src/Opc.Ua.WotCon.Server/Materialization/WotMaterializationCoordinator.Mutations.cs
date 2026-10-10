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
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotMaterializationCoordinator
    {
        /// <summary>
        /// Gets or sets the effective policy for logical Resource unload and delete operations.
        /// </summary>
        public WoTDeletePolicyEnum DeletePolicy { get; set; } = WoTDeletePolicyEnum.Reject;

        /// <summary>
        /// Changes projection eligibility, coordinating required unloading with the registry decision.
        /// Enabling selects future work and retains the configured automatic-refresh behavior.
        /// </summary>
        public async ValueTask<WotRegistryMutationResult> SetEnabledAsync(
            string groupId,
            string resourceId,
            bool enabled,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            _ = groupId ?? throw new ArgumentNullException(nameof(groupId));
            _ = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            if (enabled)
            {
                return await m_registry.SetEnabledAsync(groupId, resourceId, true, expectedEpoch, cancellationToken)
                    .ConfigureAwait(false);
            }
            WotDeleteOutcome outcome = await ApplyResourceLifecycleAsync(
                groupId, resourceId, false, DeletePolicy, expectedEpoch, "SetEnabled", cancellationToken)
                .ConfigureAwait(false);
            return new WotRegistryMutationResult(
                outcome.Delete.Outcome, m_registry.Current.FindResource(groupId, resourceId),
                outcome.Delete.Generation, [], outcome.Delete.Message);
        }

        internal async ValueTask<WotRegistryMutationResult> DeleteVersionAsync(
            string groupId, string resourceId, string versionId, long? expectedEpoch, CancellationToken cancellationToken)
        {
            WotDeleteOutcome outcome = await ApplyResourceLifecycleAsync(
                groupId, resourceId, true, DeletePolicy, expectedEpoch, "DeleteVersion", cancellationToken, versionId)
                .ConfigureAwait(false);
            return new WotRegistryMutationResult(outcome.Delete.Outcome,
                m_registry.Current.FindResource(groupId, resourceId), outcome.Delete.Generation, [], outcome.Delete.Message);
        }

        internal async ValueTask<WotRegistryMutationResult> DeleteGroupAsync(
            string groupId, long? expectedEpoch, CancellationToken cancellationToken)
        {
            WotDeleteOutcome outcome = await ApplyResourceLifecycleAsync(
                groupId, string.Empty, true, DeletePolicy, expectedEpoch, "DeleteGroup", cancellationToken,
                deleteGroup: true).ConfigureAwait(false);
            return new WotRegistryMutationResult(
                outcome.Delete.Outcome, null, outcome.Delete.Generation, [], outcome.Delete.Message);
        }

        private async ValueTask<WotDeleteOutcome> ApplyResourceLifecycleAsync(
            string groupId,
            string resourceId,
            bool delete,
            WoTDeletePolicyEnum policy,
            long? expectedEpoch,
            string requestId,
            CancellationToken cancellationToken,
            string? versionId = null,
            bool deleteGroup = false)
        {
            _ = groupId ?? throw new ArgumentNullException(nameof(groupId));
            _ = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            if (m_registry is not WotRegistryService registry ||
                m_sourceHost is not IWotInvocationProjectionHost source)
            {
                throw new NotSupportedException("The configured owners cannot prepare lifecycle mutations.");
            }
            if (!TryBeginOperation(allowDisposed: false))
            {
                throw new ObjectDisposedException(nameof(WotMaterializationCoordinator));
            }
            try
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (m_publicationRecoveryRequired)
                    {
                        throw new InvalidOperationException(
                            "Authoritative reload and runtime recovery are required before lifecycle mutation.");
                    }
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        WotRegistryLifecyclePlan plan = deleteGroup
                            ? await registry.PlanGroupLifecycleAsync(
                                groupId, policy, expectedEpoch, cancellationToken).ConfigureAwait(false)
                            : versionId is not null
                                ? await registry.PlanVersionLifecycleAsync(
                                    groupId, resourceId, versionId, policy, expectedEpoch, cancellationToken)
                                    .ConfigureAwait(false)
                                : await registry.PlanResourceLifecycleAsync(
                                    groupId, resourceId, delete, policy, expectedEpoch, cancellationToken)
                                    .ConfigureAwait(false);
                        if (plan.Mutation is not { } mutation)
                        {
                            return new WotDeleteOutcome(plan.Result, new WoTRefreshSummaryDataType
                            {
                                RequestId = requestId, Generation = m_generation, Outcome = plan.Result.Outcome,
                                Atomicity = WoTAtomicityEnum.PerRegistry
                            }, [], m_generation);
                        }
                        if (plan.Resource is { } assigned)
                        {
                            groupId = assigned.GroupId;
                            resourceId = assigned.ResourceId;
                        }
                        var affected = new HashSet<string>(mutation.ChangedResourceXids.ToList(), StringComparer.Ordinal);
                        foreach (ClosureState closure in m_closures.Values)
                        {
                            if (closure.MemberXids.Any(affected.Contains))
                            {
                                affected.UnionWith(closure.MemberXids);
                            }
                        }
                        ArrayOf<WoTResourceSelectorDataType> selection = mutation.Previous.AllResources()
                            .Where(resource => affected.Contains(resource.Xid))
                            .Select(resource => new WoTResourceSelectorDataType
                            {
                                Kind = resource.Kind, Xid = resource.Xid
                            }).ToArrayOf();
                        WotCapturedRefreshRequest request = WotCapturedRefreshRequest.Capture(new WotRefreshRequest
                        {
                            RequestId = requestId,
                            Selection = [.. selection],
                            Options = new WoTRefreshOptionsDataType
                            {
                                Atomicity = WoTAtomicityEnum.PerRegistry, DeletePolicy = policy
                            }
                        });
                        IWotProjectionPublicationCapture nativeCapture = source.CapturePublication();
                        using WotRefreshCapture capture = await CaptureInputsAsync(
                            request, cancellationToken, mutation.Previous.RefreshGeneration,
                            committedInputs: true, mutation).ConfigureAwait(false);
                        IWotRegistryPublication registryPublication = await registry.BeginPublicationAsync(
                            cancellationToken).ConfigureAwait(false);
                        await using var registryLifetime = registryPublication.ConfigureAwait(false);
                        IWotProjectionPublication publication = await nativeCapture.BeginAsync(cancellationToken)
                            .ConfigureAwait(false);
                        await using var sourceLifetime = publication.ConfigureAwait(false);
                        if (!ReferenceEquals(mutation.Previous, registryPublication.Current) || !publication.IsCurrent ||
                            !IsCurrentCapture(capture))
                        {
                            continue;
                        }
                        m_generation = mutation.Previous.RefreshGeneration;
                        WotRefreshResult refreshed = await RefreshPreparedUnitsAsync(
                            capture, mutation.Previous, DateTime.UtcNow, publication, registryPublication,
                            cancellationToken, mutation).ConfigureAwait(false);
                        WotDeleteResult result = refreshed.Summary.Failed == 0
                            ? new WotDeleteResult(
                                refreshed.Summary.Outcome == WoTOutcomeEnum.Warning
                                    ? WoTOutcomeEnum.Warning : plan.Result.Outcome,
                                policy, registry.Current.Generation,
                                plan.Result.Deleted, plan.Result.Retired, plan.Result.Dependents,
                                plan.Result.Unloaded, plan.Result.Failed, plan.Result.Unreadable,
                                refreshed.Summary.Outcome == WoTOutcomeEnum.Warning
                                    ? plan.Result.Message + " The committed lifecycle publication reports a warning."
                                    : plan.Result.Message)
                            : new WotDeleteResult(WoTOutcomeEnum.Failed, policy, registry.Current.Generation,
                                false, false, plan.Result.Dependents, [], [], plan.Result.Unreadable,
                                "The lifecycle candidate was not published.");
                        return new WotDeleteOutcome(result, refreshed.Summary, refreshed.Results, refreshed.NewGeneration);
                    }
                }
                catch (WotRegistryCommitIndeterminateException)
                {
                    m_publicationRecoveryRequired = true;
                    throw;
                }
                finally
                {
                    m_mutex.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }
    }
}
