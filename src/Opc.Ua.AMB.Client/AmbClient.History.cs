/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Historian;
using Opc.Ua.Di;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        /// <summary>
        /// Gets the select-clause identities and ordering used by HistoryFields.
        /// Paths are namespace-qualified using this session's namespace table.
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> GetHistoryEventFields()
        {
            return AmbEventFields.Select(Session.NamespaceUris);
        }

        /// <summary>
        /// Reads past DeviceHealth values, preserving quality and both timestamps.
        /// Part 11 owns ordering, page limits, cancellation and continuation release.
        /// </summary>
        public async IAsyncEnumerable<AssetHealthHistoryValue> ReadHealthHistoryAsync(
            NodeId asset,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            HistoryClientOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequireAsset(asset);
            cancellationToken.ThrowIfCancellationRequested();
            NodeId health = await ResolveAsync(
                asset, cancellationToken, Di(Opc.Ua.Di.BrowseNames.DeviceHealth)).ConfigureAwait(false);
            if (health.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The asset publishes no DeviceHealth.");
            }
            await RequireHistoryReadAsync(health, events: false, cancellationToken).ConfigureAwait(false);
            var history = new HistoryClient(Session, options);
            await foreach (DataValue value in history.ReadRawAsync(
                health,
                startTime,
                endTime,
                maxValuesPerNode,
                timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return new AssetHealthHistoryValue(health, value);
            }
        }

        /// <summary>
        /// Reads historical fault-indication events for one asset, excluding maintenance classes and subtypes.
        /// Null notifier uses the asset; pass a server-specific archive notifier when necessary.
        /// </summary>
        public async IAsyncEnumerable<AssetAlarmRecord> ReadHealthEventsAsync(
            NodeId asset,
            DateTime startTime,
            DateTime endTime,
            NodeId notifier = default,
            uint maxValuesPerNode = 0,
            HistoryClientOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (HistoryEventFieldList fields in ReadAssetEventsAsync(
                asset, startTime, endTime, notifier, false, maxValuesPerNode, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return AmbConditionFields.ToAlarm(fields.EventFields.ToArray()!, Session.MessageContext) with
                {
                    HistoryFields = fields.EventFields
                };
            }
        }

        /// <summary>
        /// Reads historical maintenance transitions and detail updates, including maintenance class subtypes.
        /// Each occurrence keeps its EventId and event time; this never performs condition refresh.
        /// </summary>
        public async IAsyncEnumerable<MaintenanceActivityRecord> ReadPastMaintenanceAsync(
            NodeId asset,
            DateTime startTime,
            DateTime endTime,
            NodeId notifier = default,
            uint maxValuesPerNode = 0,
            HistoryClientOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (HistoryEventFieldList fields in ReadAssetEventsAsync(
                asset, startTime, endTime, notifier, true, maxValuesPerNode, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return AmbConditionFields.ToMaintenance(
                    fields.EventFields.ToArray()!, Session.MessageContext) with
                {
                    HistoryFields = fields.EventFields
                };
            }
        }

        /// <summary>
        /// Reports the asset's data and notifier history access as seen by this session.
        /// A successful empty history read is distinct from an absent capability.
        /// </summary>
        public async ValueTask<AssetHistoryCapabilities> GetHistoryCapabilitiesAsync(
            NodeId asset,
            NodeId notifier = default,
            CancellationToken cancellationToken = default)
        {
            RequireAsset(asset);
            cancellationToken.ThrowIfCancellationRequested();
            NodeId health = await ResolveAsync(
                asset, cancellationToken, Di(Opc.Ua.Di.BrowseNames.DeviceHealth)).ConfigureAwait(false);
            DataValue[] healthAccess = await ReadAsync(
                [health], Attributes.UserAccessLevel, cancellationToken).ConfigureAwait(false);
            DataValue[] eventAccess = await ReadAsync(
                [notifier.IsNull ? asset : notifier], Attributes.EventNotifier, cancellationToken)
                .ConfigureAwait(false);
            return new AssetHistoryCapabilities(
                HasAccess(healthAccess[0], AccessLevels.HistoryRead),
                HasAccess(eventAccess[0], EventNotifiers.HistoryRead));
        }

        /// <summary>
        /// Applies the asset and class predicates on the server before paging event history.
        /// </summary>
        private async IAsyncEnumerable<HistoryEventFieldList> ReadAssetEventsAsync(
            NodeId asset,
            DateTime startTime,
            DateTime endTime,
            NodeId notifier,
            bool maintenance,
            uint maxValuesPerNode,
            HistoryClientOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequireAsset(asset);
            cancellationToken.ThrowIfCancellationRequested();
            NodeId archive = notifier.IsNull ? asset : notifier;
            await RequireHistoryReadAsync(archive, events: true, cancellationToken).ConfigureAwait(false);
            HashSet<NodeId> classes = await MaintenanceClassesWithBudgetAsync(
                CreateBrowseBudget(), tolerateBrowseFailure: false, cancellationToken, requireComplete: true)
                .ConfigureAwait(false);
            EventFilter filter = AmbConditionFields.Filter(Session.NamespaceUris, classes, maintenance, asset);
            var history = new HistoryClient(Session, options);
            await foreach (HistoryEventFieldList fields in history.ReadEventsAsync(
                archive,
                startTime,
                endTime,
                filter,
                maxValuesPerNode,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return fields;
            }
        }

        /// <summary>
        /// Rejects absent or denied history without converting it to an empty result.
        /// </summary>
        private async ValueTask RequireHistoryReadAsync(
            NodeId node,
            bool events,
            CancellationToken cancellationToken)
        {
            DataValue[] access = await ReadAsync(
                [node], events ? Attributes.EventNotifier : Attributes.UserAccessLevel, cancellationToken)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(access[0].StatusCode))
            {
                throw new ServiceResultException(access[0].StatusCode);
            }
            if (!HasAccess(access[0], events ? EventNotifiers.HistoryRead : AccessLevels.HistoryRead))
            {
                if (!events)
                {
                    DataValue[] supported = await ReadAsync(
                        [node], Attributes.AccessLevel, cancellationToken).ConfigureAwait(false);
                    if (StatusCode.IsBad(supported[0].StatusCode))
                    {
                        throw new ServiceResultException(supported[0].StatusCode);
                    }
                    if (HasAccess(supported[0], AccessLevels.HistoryRead))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                }
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The node exposes no history read access.");
            }
        }

        /// <summary>
        /// Gets whether a good access attribute includes the required history bit.
        /// </summary>
        private static bool HasAccess(in DataValue value, byte bit)
        {
            return StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetValue(out byte access) &&
                (access & bit) != 0;
        }

        /// <summary>
        /// Requires a local asset identifier for a history query.
        /// </summary>
        private static void RequireAsset(NodeId asset)
        {
            if (asset.IsNull)
            {
                throw new ArgumentException("An asset identifier is required.", nameof(asset));
            }
        }
    }

    /// <summary>
    /// A historical health sample with its original status and timestamps.
    /// </summary>
    /// <param name="NodeId">The historical health variable.</param>
    /// <param name="Value">The unnormalized historical sample.</param>
    public sealed record AssetHealthHistoryValue(NodeId NodeId, DataValue Value)
    {
        /// <summary>
        /// Gets a good typed health value, or null for a missing, bad or incorrectly typed sample.
        /// The original sample remains available through Value.
        /// </summary>
        public DeviceHealthEnumeration? DeviceHealth =>
            StatusCode.IsGood(Value.StatusCode) && Value.WrappedValue.TryGetValue(out int health)
                ? (DeviceHealthEnumeration)health
                : null;
    }

    /// <summary>
    /// History access advertised on an asset's health variable and selected notifier.
    /// </summary>
    /// <param name="HealthValues">Whether the session can read raw health history.</param>
    /// <param name="Events">Whether the selected notifier advertises event history reads.</param>
    public sealed record AssetHistoryCapabilities(bool HealthValues, bool Events);
}
