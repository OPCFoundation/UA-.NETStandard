// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral personnel, equipment or physical-asset resource entry
    /// captured by the in-memory engine. The same structure describes a job-order
    /// requirement and a job-response actual for all three resource kinds in both
    /// Job Control V1 and V2. The V1 <c>UoM</c> string and the V2
    /// <c>EngineeringUnits</c> structure are unified onto
    /// <see cref="EngineeringUnits"/>.
    /// </summary>
    internal sealed record Isa95ResourceRequirement
    {
        /// <summary>
        /// The identifier of the resource.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// The localized descriptions of the resource. V1 carries a single string
        /// description which is normalized to a single element here.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The use of the resource (the V1/V2 <c>PersonnelUse</c>,
        /// <c>EquipmentUse</c> or <c>PhysicalAssetUse</c> field).
        /// </summary>
        public string? Use { get; init; }

        /// <summary>
        /// The quantity of the resource.
        /// </summary>
        public string? Quantity { get; init; }

        /// <summary>
        /// The engineering unit of measure as the version-neutral core
        /// <see cref="EUInformation"/> structure.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The properties of the resource.
        /// </summary>
        public ArrayOf<Isa95Property> Properties { get; init; } = [];
    }
}
