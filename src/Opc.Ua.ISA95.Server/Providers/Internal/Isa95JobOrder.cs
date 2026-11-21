// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral job order tracked by the in-memory engine. Every standard
    /// Job Control V1/V2 job-order field is captured, including the work master
    /// references and the personnel, equipment, physical-asset and material
    /// requirement collections, so that no generated version-specific payload is
    /// stored by the engine and orders round-trip losslessly within a version.
    /// </summary>
    internal sealed record Isa95JobOrder
    {
        /// <summary>
        /// The identifier of the job order.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// The localized descriptions of the job order.
        /// </summary>
        public ArrayOf<LocalizedText> Description { get; init; } = [];

        /// <summary>
        /// The work master references of the job order.
        /// </summary>
        public ArrayOf<Isa95WorkMaster> WorkMasters { get; init; } = [];

        /// <summary>
        /// The priority of the job order.
        /// </summary>
        public short Priority { get; init; }

        /// <summary>
        /// The requested start time of the job order.
        /// </summary>
        public DateTimeUtc StartTime { get; init; }

        /// <summary>
        /// The requested end time of the job order.
        /// </summary>
        public DateTimeUtc EndTime { get; init; }

        /// <summary>
        /// The job order parameters.
        /// </summary>
        public ArrayOf<Isa95Parameter> Parameters { get; init; } = [];

        /// <summary>
        /// The personnel requirements of the job order.
        /// </summary>
        public ArrayOf<Isa95ResourceRequirement> PersonnelRequirements { get; init; } = [];

        /// <summary>
        /// The equipment requirements of the job order.
        /// </summary>
        public ArrayOf<Isa95ResourceRequirement> EquipmentRequirements { get; init; } = [];

        /// <summary>
        /// The physical-asset requirements of the job order.
        /// </summary>
        public ArrayOf<Isa95ResourceRequirement> PhysicalAssetRequirements { get; init; } = [];

        /// <summary>
        /// The material requirements of the job order.
        /// </summary>
        public ArrayOf<Isa95Material> MaterialRequirements { get; init; } = [];

        /// <summary>
        /// The latest audit-relevant localized comment retained with the job order.
        /// This carries the OPC-10031-4 V2 method <c>Comment</c> argument of the most
        /// recent operation that supplied one; it is engine-side audit state and is
        /// not part of the version-specific job-order structures.
        /// </summary>
        public ArrayOf<LocalizedText> Comment { get; init; } = [];
    }
}
