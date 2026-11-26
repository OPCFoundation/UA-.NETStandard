// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// A version-neutral job response tracked by the in-memory engine. Every
    /// standard Job Control V1/V2 job-response field is captured, including the
    /// personnel, equipment, physical-asset and material actual collections, so
    /// that responses round-trip losslessly within a version.
    /// </summary>
    public sealed record Isa95JobResponse
    {
        internal uint V2EncodingMask { get; init; }

        internal Isa95V2StateSnapshot? V2StateSnapshot { get; init; }

        public required string Id { get; init; }
        public required string JobOrderId { get; init; }
        public ArrayOf<LocalizedText> Description { get; init; } = [];
        public DateTimeUtc StartTime { get; init; }
        public DateTimeUtc EndTime { get; init; }
        public Isa95JobCanonicalState State { get; init; }
        public ArrayOf<Isa95Parameter> ResponseData { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> PersonnelActuals { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> EquipmentActuals { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> PhysicalAssetActuals { get; init; } = [];
        public ArrayOf<Isa95Material> MaterialActuals { get; init; } = [];
        public DateTimeUtc ReceivedAt { get; init; }
    }
}
