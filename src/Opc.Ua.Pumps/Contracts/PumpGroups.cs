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

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// A pump's OPC 40223 <c>Configuration</c> group: what the pump was
    /// designed to do, how it was built into the plant, and what the plant
    /// requires of it.
    /// </summary>
    public sealed record PumpConfigurationData
    {
        /// <summary>
        /// The <c>Configuration</c> group.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// <c>DesignType</c> - the 78 parameters of the pump as designed.
        /// </summary>
        public PumpValueSet Design { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>ImplementationType</c> - the 33 parameters of the pump as
        /// installed.
        /// </summary>
        public PumpValueSet Implementation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>SystemRequirementsType</c> - the 37 parameters the installation
        /// requires.
        /// </summary>
        public PumpValueSet SystemRequirements { get; init; } = PumpValueSet.Empty;
    }

    /// <summary>
    /// A pump's OPC 40223 <c>Operational</c> group: everything that changes
    /// while the pump runs.
    /// </summary>
    public sealed record PumpOperationalData
    {
        /// <summary>
        /// The <c>Operational</c> group.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// <c>MeasurementsType</c> - the 50 live process readings.
        /// </summary>
        public PumpValueSet Measurements { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>SignalsType</c> - the 21 discrete input signals. Each is a
        /// <c>DiscreteInputObjectType</c>, so the value read is its
        /// <c>DiscreteInputValue</c>.
        /// </summary>
        public PumpValueSet Signals { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>ControlType</c> - the closed-loop controller's variables and
        /// coefficients.
        /// </summary>
        public PumpValueSet Control { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>PumpActuationType</c> - the pump's own actuation surface:
        /// enable, control and operation mode, valve requests, pump kick.
        /// </summary>
        public PumpValueSet PumpActuation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>ActuationType</c> for the bypass valve, when the pump has one.
        /// </summary>
        public PumpValueSet BypassActuation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>ActuationType</c> for the throttle valve, when the pump has one.
        /// </summary>
        public PumpValueSet ThrottleValveActuation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>MultiPumpType</c> - the pump's role in a multi-pump group, when
        /// it is part of one.
        /// </summary>
        public MultiPumpConfiguration? MultiPump { get; init; }
    }

    /// <summary>
    /// A pump's OPC 40223 <c>Maintenance</c> group.
    /// </summary>
    public sealed record PumpMaintenanceData
    {
        /// <summary>
        /// The <c>Maintenance</c> group.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// <c>GeneralMaintenanceType</c> - operating, idle and down times,
        /// failure rates, and the item's state.
        /// </summary>
        public PumpValueSet General { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>PreventiveMaintenanceType</c> - installation, inspection and
        /// servicing dates.
        /// </summary>
        public PumpValueSet Preventive { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>ConditionBasedMaintenanceType</c> - availability, reliability
        /// and maintainability figures.
        /// </summary>
        public PumpValueSet ConditionBased { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// <c>BreakdownMaintenanceType</c> - failures, criticality, severity.
        /// </summary>
        public PumpValueSet Breakdown { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Gets the state OPC 40223 assigns the item, from the
        /// <c>GeneralMaintenance</c> group.
        /// </summary>
        public StateOfTheItemEnum? StateOfTheItem =>
            General.GetEnum<StateOfTheItemEnum>(BrowseNames.StateOfTheItem);

        /// <summary>
        /// Gets the maintenance level OPC 40223 assigns the item, from the
        /// <c>GeneralMaintenance</c> group.
        /// </summary>
        public MaintenanceLevelEnum? MaintenanceLevel =>
            General.GetEnum<MaintenanceLevelEnum>(BrowseNames.MaintenanceLevel);

        /// <summary>
        /// Gets whether the <c>BreakdownMaintenance</c> group reports a
        /// failure.
        /// </summary>
        public bool? HasFailure => Breakdown.GetBoolean(BrowseNames.Failure);

        /// <summary>
        /// Gets every maintenance group, paired with the OPC 40223 browse name
        /// of the functional group it was read from.
        /// </summary>
        public IEnumerable<KeyValuePair<string, PumpValueSet>> Groups
        {
            get
            {
                yield return new(BrowseNames.GeneralMaintenance, General);
                yield return new(BrowseNames.PreventiveMaintenance, Preventive);
                yield return new(BrowseNames.ConditionBasedMaintenance, ConditionBased);
                yield return new(BrowseNames.BreakdownMaintenance, Breakdown);
            }
        }
    }

    /// <summary>
    /// A pump's role in an OPC 40223 multi-pump group, read from the
    /// <c>MultiPump</c> object below <c>Operational</c>.
    /// </summary>
    /// <remarks>
    /// <c>MultiPumpType</c> declares exactly these eleven variables
    /// (OPC 40223 §7.34, Table 76), so unlike the open groups it is modelled
    /// as a record. <c>DistributionPriority</c>, <c>PumpCollectiveIDs</c> and
    /// <c>RedundantPumpIDs</c> are one-dimensional string arrays; each member
    /// is <see langword="null"/> when the server does not publish it.
    /// </remarks>
    public sealed record MultiPumpConfiguration
    {
        /// <summary>
        /// The <c>MultiPump</c> object.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// This pump's role in the group.
        /// </summary>
        public PumpRoleEnum? PumpRole { get; init; }

        /// <summary>
        /// How the group is operated.
        /// </summary>
        public MultiPumpOperationModeEnum? OperationMode { get; init; }

        /// <summary>
        /// How load is distributed across the group.
        /// </summary>
        public DistributionTypeEnum? DistributionType { get; init; }

        /// <summary>
        /// How the group exchanges the lead pump.
        /// </summary>
        public ExchangeModeEnum? ExchangeMode { get; init; }

        /// <summary>
        /// The absolute time of the next lead-pump exchange.
        /// </summary>
        public DateTime? ExchangeTime { get; init; }

        /// <summary>
        /// The interval between lead-pump exchanges, in seconds.
        /// </summary>
        public double? ExchangeTimeDifference { get; init; }

        /// <summary>
        /// How many pumps the group contains.
        /// </summary>
        public uint? NumberOfPumps { get; init; }

        /// <summary>
        /// How many of them may run at once.
        /// </summary>
        public uint? MaximumNumberOfPumpsInOperation { get; init; }

        /// <summary>
        /// The pumps in ascending order of priority for the addition operation
        /// mode (<c>DistributionPriority</c>), or <see cref="ArrayOf{T}.Null"/>
        /// when the pump system does not publish them.
        /// </summary>
        public ArrayOf<string> DistributionPriority { get; init; }

        /// <summary>
        /// The pumps of the pump system (<c>PumpCollectiveIDs</c>), or
        /// <see cref="ArrayOf{T}.Null"/> when not published.
        /// </summary>
        public ArrayOf<string> PumpCollectiveIDs { get; init; }

        /// <summary>
        /// The currently redundant pumps (<c>RedundantPumpIDs</c>), or
        /// <see cref="ArrayOf{T}.Null"/> when not published.
        /// </summary>
        public ArrayOf<string> RedundantPumpIDs { get; init; }
    }
}
