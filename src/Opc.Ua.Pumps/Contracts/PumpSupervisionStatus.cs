// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// The state of a pump's OPC 40223 supervision signals, read from its
    /// <c>Events</c> group.
    /// </summary>
    /// <remarks>
    /// <c>SupervisionType</c> splits roughly 150 boolean fault signals across
    /// seven functional groups. They are the pump's self-diagnosis, and what a
    /// caller almost always wants is not one named flag but the answer to
    /// "what is this pump complaining about" - which is what
    /// <see cref="ActiveSignals"/> gives.
    /// </remarks>
    public sealed record PumpSupervisionStatus
    {
        /// <summary>
        /// The <c>Events</c> group the signals were read from.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Process-fluid supervision - cavitation, dry running, blockage.
        /// </summary>
        public PumpValueSet ProcessFluid { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Pump-operation supervision - overheat, overload, leakage.
        /// </summary>
        public PumpValueSet PumpOperation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Mechanical supervision - bearings, vibration, imbalance.
        /// </summary>
        public PumpValueSet Mechanics { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Hardware supervision - memory, processor, power supply.
        /// </summary>
        public PumpValueSet Hardware { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Software supervision - application, control, parameters.
        /// </summary>
        public PumpValueSet Software { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Electronics supervision - supply voltage, frequency, insulation.
        /// </summary>
        public PumpValueSet Electronics { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Auxiliary-device supervision - sensors, wiring, wear reserve.
        /// </summary>
        public PumpValueSet AuxiliaryDevice { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// Gets every supervision group, paired with the OPC 40223 browse name
        /// of the functional group it was read from.
        /// </summary>
        public IEnumerable<KeyValuePair<string, PumpValueSet>> Groups
        {
            get
            {
                yield return new(BrowseNames.SupervisionProcessFluid, ProcessFluid);
                yield return new(BrowseNames.SupervisionPumpOperation, PumpOperation);
                yield return new(BrowseNames.SupervisionMechanics, Mechanics);
                yield return new(BrowseNames.SupervisionHardware, Hardware);
                yield return new(BrowseNames.SupervisionSoftware, Software);
                yield return new(BrowseNames.SupervisionElectronics, Electronics);
                yield return new(BrowseNames.SupervisionAuxiliaryDevice, AuxiliaryDevice);
            }
        }

        /// <summary>
        /// Gets every supervision signal that is currently raised, as
        /// (group browse name, signal) pairs.
        /// </summary>
        /// <remarks>
        /// Includes Boolean <c>true</c> readings whose status is not bad.
        /// An empty result means no readable signal is raised; signals that
        /// are missing, non-Boolean or bad are not counted.
        /// </remarks>
        public IEnumerable<KeyValuePair<string, PumpValue>> ActiveSignals
        {
            get
            {
                foreach (KeyValuePair<string, PumpValueSet> group in Groups)
                {
                    foreach (PumpValue signal in group.Value)
                    {
                        if (signal.AsBoolean() == true)
                        {
                            yield return new(group.Key, signal);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets whether any supervision signal is currently raised.
        /// </summary>
        public bool HasActiveSignals
        {
            get
            {
                foreach (KeyValuePair<string, PumpValue> _ in ActiveSignals)
                {
                    return true;
                }
                return false;
            }
        }
    }
}
