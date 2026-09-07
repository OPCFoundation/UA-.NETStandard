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
using System.Threading;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Holds the not-yet-resolved slots for one structured (field-mapped)
    /// target variable and lazily resolves the target's structure
    /// <see cref="IEncodeableType"/> plus every field's
    /// <see cref="WotFieldPathPlan"/> on first use.
    /// <para>
    /// Resolution cannot run while a runtime NodeSet is being wired: the
    /// generation's binding runtime is wired by
    /// <see cref="Ua.Server.RuntimeNodeSet.RuntimeNodeSetOptions.ConfigureAsync"/>,
    /// which completes before <c>NodeManagerLifecycle.RefreshComplexTypesAsync</c>
    /// registers the server's custom structure types into the shared
    /// <see cref="IEncodeableFactory"/>. Deferring resolution to the first
    /// structured read or write lets that same factory instance — mutated in
    /// place by <c>RefreshComplexTypesAsync</c> before the NodeManager is
    /// published — already carry the type by the time it is needed.
    /// </para>
    /// <para>
    /// Thread-safe and retryable: concurrent first use resolves at most once
    /// under a single lock; a successful resolution is cached forever, and a
    /// failed resolution (the type is still unavailable, or validation still
    /// fails) is retried, uncached, on every subsequent call.
    /// </para>
    /// </summary>
    internal sealed class WotStructuredGroupState
    {
        /// <summary>
        /// Initializes a new lazily-resolved structured group.
        /// </summary>
        /// <param name="factory">
        /// The server's <see cref="IEncodeableFactory"/>. Captured by reference
        /// so a later mutation (type registration) is visible to resolution.
        /// </param>
        /// <param name="namespaceUris">The node manager's namespace table.</param>
        /// <param name="dataTypeId">The target variable's declared DataType.</param>
        /// <param name="targetNodeId">The target variable's NodeId, for diagnostics.</param>
        /// <param name="readSlots">
        /// The read-direction field paths and their channel slots, already
        /// duplicate-checked and ordered by <see cref="WotProjectionBindingRuntime.Wire"/>.
        /// </param>
        /// <param name="writeSlots">
        /// The write-direction field paths and their channel slots, already
        /// duplicate-checked and ordered by <see cref="WotProjectionBindingRuntime.Wire"/>.
        /// </param>
        public WotStructuredGroupState(
            IEncodeableFactory factory,
            NamespaceTable namespaceUris,
            NodeId dataTypeId,
            NodeId targetNodeId,
            List<(string Path, WotBindingChannelSlot Slot)> readSlots,
            List<(string Path, WotBindingChannelSlot Slot)> writeSlots)
        {
            m_factory = factory;
            m_namespaceUris = namespaceUris;
            m_dataTypeId = dataTypeId;
            TargetNodeId = targetNodeId;
            m_readSlots = readSlots;
            m_writeSlots = writeSlots;
        }

        /// <summary>
        /// Gets the target variable's NodeId.
        /// </summary>
        public NodeId TargetNodeId { get; }

        /// <summary>
        /// Resolves the structure type and every field's navigation plan on
        /// first call, and returns the cached result on every later call.
        /// A failed attempt resolves nothing permanently: the next call
        /// retries from scratch against the (possibly by-then-populated)
        /// factory.
        /// </summary>
        /// <returns>
        /// On success, <see cref="ServiceResult.Good"/> together with the
        /// resolved root type and field plans. On failure, the deterministic
        /// <see cref="ServiceResult"/> describing why resolution could not
        /// complete, with empty field plan lists.
        /// </returns>
        public WotStructuredGroupResolution EnsureResolved()
        {
            lock (m_gate)
            {
                if (m_resolution is { } cached)
                {
                    return cached;
                }

                try
                {
                    var rootTypeId = NodeId.ToExpandedNodeId(m_dataTypeId, m_namespaceUris);
                    if (!m_factory.TryGetEncodeableType(rootTypeId, out IEncodeableType? rootType) ||
                        rootType.CreateInstance() is not IStructure)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "Target '{0}' has DataType '{1}', which is not a registered structure type; " +
                            "'uav:mapByFieldPath' requires a structured target.",
                            TargetNodeId,
                            m_dataTypeId);
                    }

                    var resolution = new WotStructuredGroupResolution(
                        ServiceResult.Good,
                        rootType,
                        BuildFieldPlans(rootType, m_readSlots),
                        BuildFieldPlans(rootType, m_writeSlots));

                    // Cache only the successful resolution; a failure below is
                    // never cached so the next first use retries.
                    m_resolution = resolution;
                    return resolution;
                }
                catch (ServiceResultException ex)
                {
                    return WotStructuredGroupResolution.Failed(new ServiceResult(ex));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return WotStructuredGroupResolution.Failed(new ServiceResult(
                        StatusCodes.BadConfigurationError,
                        new LocalizedText(
                            $"Target '{TargetNodeId}' structure mapping could not be resolved: {ex.Message}")));
                }
            }
        }

        private List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> BuildFieldPlans(
            IEncodeableType rootType, List<(string Path, WotBindingChannelSlot Slot)> slots)
        {
            var plans = new List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)>(slots.Count);
            foreach ((string path, WotBindingChannelSlot slot) in slots)
            {
                plans.Add((
                    WotStructuredFieldNavigator.BuildPlan(m_factory, m_namespaceUris, rootType, path, TargetNodeId),
                    slot));
            }
            return plans;
        }

        private readonly IEncodeableFactory m_factory;
        private readonly NamespaceTable m_namespaceUris;
        private readonly NodeId m_dataTypeId;
        private readonly List<(string Path, WotBindingChannelSlot Slot)> m_readSlots;
        private readonly List<(string Path, WotBindingChannelSlot Slot)> m_writeSlots;
        private readonly Lock m_gate = new();
        private WotStructuredGroupResolution? m_resolution;
    }

    /// <summary>
    /// The outcome of <see cref="WotStructuredGroupState.EnsureResolved"/>:
    /// either the resolved structure type with its field plans, or the
    /// deterministic failure status naming why resolution could not complete.
    /// </summary>
    internal sealed class WotStructuredGroupResolution
    {
        /// <summary>
        /// Initializes a new resolution outcome.
        /// </summary>
        public WotStructuredGroupResolution(
            ServiceResult error,
            IEncodeableType? rootType,
            List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> readFields,
            List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> writeFields)
        {
            Error = error;
            RootType = rootType;
            ReadFields = readFields;
            WriteFields = writeFields;
        }

        /// <summary>
        /// Gets the failure status, or <see cref="ServiceResult.Good"/> when
        /// resolution succeeded.
        /// </summary>
        public ServiceResult Error { get; }

        /// <summary>
        /// Gets whether resolution succeeded.
        /// </summary>
        public bool Success => ServiceResult.IsGood(Error);

        /// <summary>
        /// Gets the resolved structure type, or <c>null</c> on failure.
        /// </summary>
        public IEncodeableType? RootType { get; }

        /// <summary>
        /// Gets the resolved read-direction field plans; empty on failure.
        /// </summary>
        public List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> ReadFields { get; }

        /// <summary>
        /// Gets the resolved write-direction field plans; empty on failure.
        /// </summary>
        public List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> WriteFields { get; }

        /// <summary>
        /// Creates a failed resolution outcome carrying only the error.
        /// </summary>
        public static WotStructuredGroupResolution Failed(ServiceResult error)
        {
            return new(error, null, [], []);
        }
    }
}
