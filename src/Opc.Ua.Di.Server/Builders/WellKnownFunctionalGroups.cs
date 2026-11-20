// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Well-known DI <c>FunctionalGroupType</c> instance names defined by
    /// OPC 10000-100 §5.6. These names appear as <c>BrowseName</c>s on
    /// <c>DeviceType</c> (and subtypes) and aggregate references to the
    /// device's parameters/measurements by topic.
    /// </summary>
    /// <remarks>
    /// Use these constants with
    /// <see cref="IDeviceBuilder{TDevice}.WithFunctionalGroup(QualifiedName, System.Action{IFunctionalGroupBuilder})"/>
    /// to ensure spec-compliant browse names. The typed convenience
    /// methods on <see cref="IDeviceBuilder{TDevice}"/> (e.g.
    /// <see cref="IDeviceBuilder{TDevice}.WithConfigurationGroup"/>) use
    /// these constants internally.
    /// </remarks>
    public static class WellKnownFunctionalGroups
    {
        /// <summary>
        /// Spec-defined name for the Identification group.
        /// </summary>
        public const string Identification = "Identification";

        /// <summary>
        /// Spec-defined name for the Configuration group.
        /// </summary>
        public const string Configuration = "Configuration";

        /// <summary>
        /// Spec-defined name for the Maintenance group.
        /// </summary>
        public const string Maintenance = "Maintenance";

        /// <summary>
        /// Spec-defined name for the Diagnostics group.
        /// </summary>
        public const string Diagnostics = "Diagnostics";

        /// <summary>
        /// Spec-defined name for the Status group.
        /// </summary>
        public const string Status = "Status";

        /// <summary>
        /// Spec-defined name for the Operational group.
        /// </summary>
        public const string Operational = "Operational";

        /// <summary>
        /// Spec-defined name for the Statistics group.
        /// </summary>
        public const string Statistics = "Statistics";

        /// <summary>
        /// Spec-defined name for the OperationCounters group.
        /// </summary>
        public const string OperationCounters = "OperationCounters";
    }
}
