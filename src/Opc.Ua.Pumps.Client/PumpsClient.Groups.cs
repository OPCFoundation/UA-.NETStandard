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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Reads a pump's <c>Configuration</c> group - what it was designed
        /// to do, how it was installed, and what the plant requires of it.
        /// </summary>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The group, or <see langword="null"/> when the pump publishes none.
        /// </returns>
        public async ValueTask<PumpConfigurationData?> ReadConfigurationAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolveDiGroupAsync(
                pump,
                PumpsModel.DiGroups.Configuration,
                cancellationToken).ConfigureAwait(false);
            if (group.IsNull)
            {
                return null;
            }
            return new PumpConfigurationData
            {
                NodeId = group,
                Design = await ReadPumpsChildSetAsync(
                    group, BrowseNames.Design, options, cancellationToken)
                    .ConfigureAwait(false),
                Implementation = await ReadPumpsChildSetAsync(
                    group, BrowseNames.Implementation, options, cancellationToken)
                    .ConfigureAwait(false),
                SystemRequirements = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SystemRequirements, options, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads a pump's <c>Operational</c> group - everything that changes
        /// while it runs.
        /// </summary>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The group, or <see langword="null"/> when the pump publishes none.
        /// </returns>
        public async ValueTask<PumpOperationalData?> ReadOperationalAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolveOperationalAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            if (group.IsNull)
            {
                return null;
            }
            return new PumpOperationalData
            {
                NodeId = group,
                Measurements = await ReadPumpsChildSetAsync(
                    group, BrowseNames.Measurements, options, cancellationToken)
                    .ConfigureAwait(false),
                Signals = await ReadPumpsChildSetAsync(
                    group, BrowseNames.Signals, options, cancellationToken)
                    .ConfigureAwait(false),
                Control = await ReadPumpsChildSetAsync(
                    group, BrowseNames.Control, options, cancellationToken)
                    .ConfigureAwait(false),
                PumpActuation = await ReadPumpsChildSetAsync(
                    group, BrowseNames.PumpActuation, options, cancellationToken)
                    .ConfigureAwait(false),
                BypassActuation = await ReadPumpsChildSetAsync(
                    group, BrowseNames.BypassActuation, options, cancellationToken)
                    .ConfigureAwait(false),
                ThrottleValveActuation = await ReadPumpsChildSetAsync(
                    group, BrowseNames.ThrottleValveActuation, options, cancellationToken)
                    .ConfigureAwait(false),
                MultiPump = await ReadMultiPumpOfGroupAsync(group, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Resolves a pump's <c>Operational</c> group, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveOperationalAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            return ResolveDiGroupAsync(
                pump,
                PumpsModel.DiGroups.Operational,
                cancellationToken);
        }

        /// <summary>
        /// Reads only a pump's live <c>Measurements</c> - the cheapest useful
        /// read, and the one to poll on a cycle.
        /// </summary>
        /// <remarks>
        /// Pass <see cref="PumpReadOptions.ValuesOnly"/> when polling: the
        /// engineering units and ranges do not change, so fetching them on
        /// every cycle costs a round trip for nothing.
        /// </remarks>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<PumpValueSet> ReadMeasurementsAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolveOperationalAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            return await ReadPumpsChildSetAsync(
                group,
                BrowseNames.Measurements,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads only a pump's discrete <c>Signals</c>.
        /// </summary>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<PumpValueSet> ReadSignalsAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolveOperationalAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            return await ReadPumpsChildSetAsync(
                group,
                BrowseNames.Signals,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a pump's <c>MultiPump</c> configuration, or
        /// <see langword="null"/> when it is not part of a multi-pump group.
        /// </summary>
        /// <remarks>
        /// Despite the name, <c>MultiPumpType</c> is not a set of pumps: it is
        /// a functional group below <c>Operational</c> describing this pump's
        /// role in one.
        /// </remarks>
        /// <param name="pump">The pump to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MultiPumpConfiguration?> ReadMultiPumpAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            NodeId operational = await ResolveOperationalAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            return await ReadMultiPumpOfGroupAsync(operational, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the <c>MultiPump</c> object below an already-resolved
        /// <c>Operational</c> group.
        /// </summary>
        private async ValueTask<MultiPumpConfiguration?> ReadMultiPumpOfGroupAsync(
            NodeId operationalGroup,
            CancellationToken cancellationToken)
        {
            NodeId group = await ResolvePumpsChildAsync(
                operationalGroup,
                BrowseNames.MultiPump,
                cancellationToken).ConfigureAwait(false);
            if (group.IsNull)
            {
                return null;
            }
            PumpValueSet values = await ReadValueSetAsync(
                group,
                PumpReadOptions.ValuesOnly,
                cancellationToken).ConfigureAwait(false);
            return new MultiPumpConfiguration
            {
                NodeId = group,
                PumpRole = values.GetEnum<PumpRoleEnum>(BrowseNames.PumpRole),
                OperationMode = values.GetEnum<MultiPumpOperationModeEnum>(
                    BrowseNames.MultiPumpOperationMode),
                DistributionType = values.GetEnum<DistributionTypeEnum>(
                    BrowseNames.DistributionType),
                ExchangeMode = values.GetEnum<ExchangeModeEnum>(BrowseNames.ExchangeMode),
                ExchangeTime = values.GetDateTime(BrowseNames.ExchangeTime),
                ExchangeTimeDifference = values.GetDouble(
                    BrowseNames.ExchangeTimeDifference),
                NumberOfPumps = values.GetUInt32(BrowseNames.NumberOfPumps),
                MaximumNumberOfPumpsInOperation = values.GetUInt32(
                    BrowseNames.MaximumNumberOfPumpsInOperation),
                DistributionPriority = values.GetStringArray(BrowseNames.DistributionPriority),
                PumpCollectiveIDs = values.GetStringArray(BrowseNames.PumpCollectiveIDs),
                RedundantPumpIDs = values.GetStringArray(BrowseNames.RedundantPumpIDs)
            };
        }

        /// <summary>
        /// Reads a pump's <c>Events</c> group - the seven OPC 40223
        /// supervision categories.
        /// </summary>
        /// <remarks>
        /// The supervision signals are booleans, so the metadata round trip is
        /// skipped regardless of <paramref name="options"/>.
        /// </remarks>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">
        /// Only <see cref="PumpReadOptions.IncludeTimestamps"/> is honored;
        /// engineering units and ranges are never read for these boolean
        /// signals.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The supervision state, or <see langword="null"/> when the pump
        /// publishes no <c>Events</c> group.
        /// </returns>
        public async ValueTask<PumpSupervisionStatus?> ReadSupervisionAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolvePumpsChildAsync(
                pump,
                BrowseNames.Events,
                cancellationToken).ConfigureAwait(false);
            if (group.IsNull)
            {
                return null;
            }
            PumpReadOptions booleans = PumpReadOptions.ValuesOnly with
            {
                IncludeTimestamps = options?.IncludeTimestamps ?? false
            };
            return new PumpSupervisionStatus
            {
                NodeId = group,
                ProcessFluid = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionProcessFluid, booleans, cancellationToken)
                    .ConfigureAwait(false),
                PumpOperation = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionPumpOperation, booleans, cancellationToken)
                    .ConfigureAwait(false),
                Mechanics = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionMechanics, booleans, cancellationToken)
                    .ConfigureAwait(false),
                Hardware = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionHardware, booleans, cancellationToken)
                    .ConfigureAwait(false),
                Software = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionSoftware, booleans, cancellationToken)
                    .ConfigureAwait(false),
                Electronics = await ReadPumpsChildSetAsync(
                    group, BrowseNames.SupervisionElectronics, booleans, cancellationToken)
                    .ConfigureAwait(false),
                AuxiliaryDevice = await ReadPumpsChildSetAsync(
                    group,
                    BrowseNames.SupervisionAuxiliaryDevice,
                    booleans,
                    cancellationToken).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads a pump's <c>Maintenance</c> group - its four OPC 40223
        /// maintenance categories.
        /// </summary>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The maintenance data, or <see langword="null"/> when the pump
        /// publishes no <c>Maintenance</c> group.
        /// </returns>
        public async ValueTask<PumpMaintenanceData?> ReadMaintenanceAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolveDiGroupAsync(
                pump,
                PumpsModel.DiGroups.Maintenance,
                cancellationToken).ConfigureAwait(false);
            if (group.IsNull)
            {
                return null;
            }
            return new PumpMaintenanceData
            {
                NodeId = group,
                General = await ReadPumpsChildSetAsync(
                    group, BrowseNames.GeneralMaintenance, options, cancellationToken)
                    .ConfigureAwait(false),
                Preventive = await ReadPumpsChildSetAsync(
                    group, BrowseNames.PreventiveMaintenance, options, cancellationToken)
                    .ConfigureAwait(false),
                ConditionBased = await ReadPumpsChildSetAsync(
                    group,
                    BrowseNames.ConditionBasedMaintenance,
                    options,
                    cancellationToken).ConfigureAwait(false),
                Breakdown = await ReadPumpsChildSetAsync(
                    group, BrowseNames.BreakdownMaintenance, options, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads a pump's <c>Documentation</c> group. The values are the
        /// document links; the documents themselves are <c>FileType</c>
        /// objects to transfer over the File API.
        /// </summary>
        /// <param name="pump">The pump to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<PumpValueSet> ReadDocumentationAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            NodeId group = await ResolvePumpsChildAsync(
                pump,
                BrowseNames.Documentation,
                cancellationToken).ConfigureAwait(false);
            return await ReadValueSetAsync(
                group,
                PumpReadOptions.ValuesOnly,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a Pumps-namespace child of <paramref name="parent"/> and
        /// reads every value below it.
        /// </summary>
        private async ValueTask<PumpValueSet> ReadPumpsChildSetAsync(
            NodeId parent,
            string browseName,
            PumpReadOptions? options,
            CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                return PumpValueSet.Empty;
            }
            NodeId child = await ResolvePumpsChildAsync(
                parent,
                browseName,
                cancellationToken).ConfigureAwait(false);
            return await ReadValueSetAsync(child, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
