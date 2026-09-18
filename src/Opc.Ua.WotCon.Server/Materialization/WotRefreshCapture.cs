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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Produces the same selected, leased input image used by the materialization coordinator.
    /// Capture performs no conversion, retirement, activation or registry metadata commit.
    /// </summary>
    public interface IWotRefreshCaptureProvider
    {
        /// <summary>
        /// Captures one invocation and its exact inputs. The caller owns the returned leases.
        /// A nonzero stale ExpectedGeneration is rejected before acquiring document bodies.
        /// Publication still requires the publication owner's final revision checks.
        /// </summary>
        ValueTask<WotRefreshCapture> CaptureAsync(
            WotRefreshRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Immutable caller inputs captured before an asynchronous refresh operation can yield.
    /// Generated selector values returned to a caller are independent copies.
    /// </summary>
    public sealed class WotCapturedRefreshRequest
    {
        private WotCapturedRefreshRequest(WotRefreshRequest request)
        {
            m_selection = request.Selection.IsDefaultOrEmpty
                ? []
                : request.Selection.ToArrayOf().ConvertAll(CopySelector);
            RequestId = request.RequestId ?? string.Empty;
            ExpectedGeneration = request.ExpectedGeneration;
            WoTRefreshOptionsDataType? options = request.Options;
            Atomicity = options?.Atomicity ?? WoTAtomicityEnum.PerClosure;
            Force = options?.Force ?? false;
            DryRun = options?.DryRun ?? false;
            IncludeDependents = options?.IncludeDependents ?? false;
            DeletePolicy = options?.DeletePolicy ?? WoTDeletePolicyEnum.Reject;
            MaxParallelism = options?.MaxParallelism ?? 0;
            Timeout = options?.Timeout ?? 0;
        }

        /// <summary>
        /// Gets the invocation identifier, independent of subsequent caller mutation.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// Gets the caller's expected generation. Zero does not waive final stale-input checks.
        /// </summary>
        public uint ExpectedGeneration { get; }

        /// <summary>
        /// Gets a caller-owned copy of the original selectors, not the matched resource set.
        /// </summary>
        public ArrayOf<WoTResourceSelectorDataType> Selection => m_selection.ConvertAll(CopySelector);

        /// <summary>
        /// Gets whether selection was omitted or empty, rather than nonempty and unmatched.
        /// </summary>
        public bool SelectsAll => m_selection.IsEmpty;

        /// <summary>
        /// Gets the requested atomicity, not a claim about the eventual publication units.
        /// </summary>
        public WoTAtomicityEnum Atomicity { get; }

        /// <summary>
        /// Gets whether fresh preparation was requested.
        /// </summary>
        public bool Force { get; }

        /// <summary>
        /// Gets whether this invocation must remain diagnostic only.
        /// </summary>
        public bool DryRun { get; }

        /// <summary>
        /// Gets whether indexed reverse dependencies join the initial selection.
        /// </summary>
        public bool IncludeDependents { get; }

        /// <summary>
        /// Gets the requested dependent-deletion policy.
        /// </summary>
        public WoTDeletePolicyEnum DeletePolicy { get; }

        /// <summary>
        /// Gets the requested preparation concurrency limit.
        /// </summary>
        public uint MaxParallelism { get; }

        /// <summary>
        /// Gets the requested overall time budget.
        /// </summary>
        public double Timeout { get; }

        /// <summary>
        /// Copies one request without resolving selection or acquiring content.
        /// </summary>
        public static WotCapturedRefreshRequest Capture(WotRefreshRequest request)
        {
            _ = request ?? throw new ArgumentNullException(nameof(request));
            return new WotCapturedRefreshRequest(request);
        }

        private static WoTResourceSelectorDataType CopySelector(WoTResourceSelectorDataType selector)
        {
            if (selector is null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A selector must not be null.");
            }
            return new WoTResourceSelectorDataType
            {
                Xid = selector.Xid,
                GroupId = selector.GroupId,
                ResourceId = selector.ResourceId,
                VersionId = selector.VersionId,
                Kind = selector.Kind
            };
        }

        private readonly ArrayOf<WoTResourceSelectorDataType> m_selection;
    }

    /// <summary>
    /// The coordinator's captured invocation, exact registry inputs and origin context.
    /// It is preparation input, not a prepared publication unit or an integrity-validation token.
    /// </summary>
    public sealed class WotRefreshCapture : IDisposable
    {
        internal WotRefreshCapture(
            WotCapturedRefreshRequest request,
            uint preparationGeneration,
            WotMaterializationSnapshot inputs,
            WotRegistryOrigin? registryOrigin,
            bool supportsDependencySnapshots,
            int maxJsonDepth,
            WotDocumentSetMode documentSetMode,
            WotProjectionCompatibilityMode projectionCompatibilityMode,
            string binderRevision,
            bool strictBindings,
            WotProjectionRetirementPolicy retirementPolicy,
            Func<WotResource, WotResourceVersion, ExpandedNodeId>? versionNodeIdResolver)
        {
            Request = request;
            PreparationGeneration = preparationGeneration;
            Inputs = inputs;
            RegistryOrigin = registryOrigin;
            SupportsDependencySnapshots = supportsDependencySnapshots;
            MaxJsonDepth = maxJsonDepth;
            DocumentSetMode = documentSetMode;
            ProjectionCompatibilityMode = projectionCompatibilityMode;
            BinderRevision = binderRevision;
            StrictBindings = strictBindings;
            RetirementPolicy = retirementPolicy;
            var nodeIds = ImmutableDictionary.CreateBuilder<string, ExpandedNodeId>(StringComparer.Ordinal);
            if (supportsDependencySnapshots && versionNodeIdResolver is not null)
            {
                foreach (WotResource resource in inputs.Resources)
                {
                    if (resource.DefaultVersion is { } version)
                    {
                        nodeIds.Add(
                            WotDependencyGraph.VersionXid(resource, version),
                            versionNodeIdResolver(resource, version));
                    }
                }
            }
            m_versionNodeIds = nodeIds.ToImmutable();
        }

        /// <summary>
        /// Gets the immutable caller inputs used by this capture.
        /// </summary>
        public WotCapturedRefreshRequest Request { get; }

        /// <summary>
        /// Gets the committed coordinator generation against which capture started.
        /// </summary>
        public uint PreparationGeneration { get; }

        /// <summary>
        /// Gets the single selected input image, including failed and resolution-only inputs.
        /// </summary>
        public WotMaterializationSnapshot Inputs { get; }

        /// <summary>
        /// Gets the authoritative origin captured before body acquisition, or null when unavailable.
        /// </summary>
        public WotRegistryOrigin? RegistryOrigin { get; }

        /// <summary>
        /// Gets whether this capture has the same owner's snapshot and lease capabilities and an origin.
        /// </summary>
        public bool SupportsDependencySnapshots { get; }

        /// <summary>
        /// Gets the JSON depth used for this capture.
        /// </summary>
        public int MaxJsonDepth { get; }

        /// <summary>
        /// Gets the explicit import mode captured for this invocation.
        /// </summary>
        public WotDocumentSetMode DocumentSetMode { get; }

        /// <summary>
        /// Gets the projection compatibility mode captured before body acquisition.
        /// </summary>
        public WotProjectionCompatibilityMode ProjectionCompatibilityMode { get; }

        /// <summary>
        /// Gets the binder capability revision observed before acquisition.
        /// </summary>
        public string BinderRevision { get; }

        /// <summary>
        /// Gets the captured strict-binding policy.
        /// </summary>
        public bool StrictBindings { get; }

        /// <summary>
        /// Gets the captured generation-retirement policy.
        /// </summary>
        public WotProjectionRetirementPolicy RetirementPolicy { get; }

        /// <summary>
        /// Produces origin-scoped exact targets from this capture without consulting current
        /// registry metadata, a changed origin, or a later Version NodeId resolver.
        /// </summary>
        public ArrayOf<WotDependencyTargetPin> GetDependencyTargets(WotDependencyClosure closure)
        {
            RequireClosure(closure);
            if (!SupportsDependencySnapshots || RegistryOrigin is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Authoritative origin pins are unavailable.");
            }
            var targets = ImmutableArray.CreateBuilder<WotDependencyTargetPin>();
            for (int i = 0; i < closure.Dependencies.Length; i++)
            {
                WotDependency edge = closure.Dependencies[i];
                if (!edge.Resolved)
                {
                    continue;
                }
                WotResource target = closure.Members.Single(member => member.Xid == edge.TargetXid);
                WotResourceVersion version = target.DefaultVersion ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A resolved dependency has no captured exact Version.");
                string versionXid = WotDependencyGraph.VersionXid(target, version);
                targets.Add(new WotDependencyTargetPin(
                    (uint)i, RegistryOrigin, versionXid, version.DocumentId ?? string.Empty,
                    m_versionNodeIds.TryGetValue(versionXid, out ExpandedNodeId nodeId) ? nodeId : ExpandedNodeId.Null,
                    version.Digest));
            }
            return targets.ToImmutable().ToArrayOf();
        }

        /// <summary>
        /// Produces the registry-input fingerprint used by the existing coordinator.
        /// This is not evidence that external artifacts, admission or publication revisions
        /// have been captured or rechecked by an atomic publication owner.
        /// </summary>
        public ByteString GetRegistryInputDigest(WotDependencyClosure closure)
        {
            RequireClosure(closure);
            if (!closure.IsProjectable)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Incomplete captured inputs have no complete registry-input digest.");
            }
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                foreach (WotResource member in closure.OrderedResources.OrderBy(
                    member => member.Xid, StringComparer.Ordinal))
                {
                    writer.Write(member.Xid);
                    writer.Write(member.Enabled);
                    writer.Write((int)member.Kind);
                    writer.Write(member.DefaultVersionId ?? string.Empty);
                    writer.Write(member.DefaultVersion?.Format ?? string.Empty);
                    writer.Write(member.DefaultVersion?.ContentType ?? string.Empty);
                    ByteString digest = member.DefaultVersion is null ? ByteString.Empty : member.DefaultVersion.Digest;
                    writer.Write(digest.Length);
                    writer.Write(digest.Span.ToArray());
                }
                writer.Write(MaxJsonDepth);
                writer.Write((int)DocumentSetMode);
                writer.Write((int)ProjectionCompatibilityMode);
                writer.Write(BinderRevision);
            }
            return WotContentDigest.Compute(buffer.ToArray());
        }

        /// <summary>
        /// Produces a caller-owned LastRefreshPlan value from this invocation and the publication
        /// owner's actual final grouping. This does not select units, recheck revisions, publish
        /// the Property or confer permission to commit. Dry runs cannot produce this observation.
        /// </summary>
        public WoTRefreshPlanDataType CreateRefreshPlan(WoTAtomicityEnum appliedAtomicity, uint unitCount)
        {
            if (Request.DryRun)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A dry run must not publish LastRefreshPlan.");
            }
            if (appliedAtomicity is not (WoTAtomicityEnum.PerResource or WoTAtomicityEnum.PerGroup or
                WoTAtomicityEnum.PerClosure or WoTAtomicityEnum.PerRegistry))
            {
                throw new ArgumentOutOfRangeException(nameof(appliedAtomicity));
            }
            return new WoTRefreshPlanDataType
            {
                RequestId = Request.RequestId,
                PreparationGeneration = PreparationGeneration,
                RequestedAtomicity = Request.Atomicity,
                AppliedAtomicity = appliedAtomicity,
                UnitCount = unitCount
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Inputs.Dispose();
        }

        private void RequireClosure(WotDependencyClosure closure)
        {
            _ = closure ?? throw new ArgumentNullException(nameof(closure));
            if (!Inputs.Closures.Contains(candidate => ReferenceEquals(candidate, closure)))
            {
                throw new ArgumentException("The closure does not belong to this capture.", nameof(closure));
            }
        }

        private readonly ImmutableDictionary<string, ExpandedNodeId> m_versionNodeIds;
    }
}
