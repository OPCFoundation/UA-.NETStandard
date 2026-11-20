/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.GMS.Contracts
{
    public sealed partial record GmsSystemSnapshot
    {
        /// <summary>
        /// Gets optional GMS metadata for actual jobs in MachineTool.Production.
        /// Omitted metadata does not replace native job types with base types.
        /// </summary>
        public ArrayOf<GmsJobObservation> Jobs { get; init; }

        /// <summary>
        /// Gets optional GMS metadata for actual retained production parts, addressed by local slot keys.
        /// </summary>
        public ArrayOf<GmsPartObservation> Parts { get; init; }
    }

    /// <summary>
    /// Native metadata of a monitored retained job. This contract exposes no GMS job-control verb.
    /// </summary>
    public sealed record GmsJobObservation
    {
        /// <summary>
        /// Gets the inherited job slot key, or its Identifier when the inherited Key is empty.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets optional batch identity.
        /// </summary>
        public DataValue BatchIdentifier { get; init; }

        /// <summary>
        /// Gets optional elapsed Duration milliseconds.
        /// </summary>
        public DataValue Duration { get; init; }

        /// <summary>
        /// Gets optional provider-reported remaining Duration milliseconds.
        /// </summary>
        public DataValue RemainingTime { get; init; }

        /// <summary>
        /// Gets the optional released MeasurementReasonEnum observation.
        /// </summary>
        public DataValue MeasurementReason { get; init; }
    }

    /// <summary>
    /// A native CatalogType sample and its required externally defined catalog metadata.
    /// </summary>
    public sealed record GmsCatalogObservation
    {
        /// <summary>
        /// Gets the original Number sample, or Number array for ProcessParameter.
        /// </summary>
        public DataValue Value { get; init; }

        /// <summary>
        /// Gets the required name of the externally defined catalog.
        /// </summary>
        public string CatalogName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the required reference to the entry in that catalog.
        /// </summary>
        public string CatalogEntry { get; init; } = string.Empty;

        /// <summary>
        /// Gets the explicit mandatory MultiStateValueDiscrete EnumValues metadata.
        /// </summary>
        public ArrayOf<EnumValueType> EnumValues { get; init; }

        /// <summary>
        /// Gets the native MultiStateValueDiscrete textual observation; unavailable text is explicitly bad.
        /// </summary>
        public DataValue ValueAsText { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);
    }

    /// <summary>
    /// Native part metadata attached to an actual GMSPartType in the retained production plan.
    /// </summary>
    public sealed record GmsPartObservation
    {
        /// <summary>
        /// Gets the containing inherited job slot key.
        /// </summary>
        public string JobKey { get; init; } = string.Empty;

        /// <summary>
        /// Gets the containing inherited part-set slot key.
        /// </summary>
        public string PartSetKey { get; init; } = string.Empty;

        /// <summary>
        /// Gets the inherited part slot key.
        /// </summary>
        public string PartKey { get; init; } = string.Empty;

        /// <summary>
        /// Gets optional descriptive text.
        /// </summary>
        public DataValue PartDescription { get; init; }

        /// <summary>
        /// Gets optional amendment status.
        /// </summary>
        public DataValue PartAmendmentStatus { get; init; }

        /// <summary>
        /// Gets optional production number.
        /// </summary>
        public DataValue ProductionNumber { get; init; }

        /// <summary>
        /// Gets optional native nest-catalog metadata.
        /// </summary>
        public GmsCatalogObservation? NestIdentifier { get; init; }

        /// <summary>
        /// Gets optional native operator-catalog metadata.
        /// </summary>
        public GmsCatalogObservation? Operator { get; init; }

        /// <summary>
        /// Gets optional native part-carrier catalog metadata.
        /// </summary>
        public GmsCatalogObservation? PartCarrierIdentifier { get; init; }

        /// <summary>
        /// Gets optional native processing-machine catalog metadata.
        /// </summary>
        public GmsCatalogObservation? ProcessingMachineIdentifier { get; init; }

        /// <summary>
        /// Gets optional native Number-array process parameters and their catalog metadata.
        /// </summary>
        public GmsCatalogObservation? ProcessParameter { get; init; }
    }
}
