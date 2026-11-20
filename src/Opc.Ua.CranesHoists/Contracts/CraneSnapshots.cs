// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.CranesHoists
{
    /// <summary>
    /// A discovered crane instance with its actual, potentially vendor-specific, type identity.
    /// </summary>
    /// <param name="NodeId">The instance identifier.</param>
    /// <param name="BrowseName">The namespace-qualified instance name.</param>
    /// <param name="DisplayName">The server-provided display name.</param>
    /// <param name="TypeDefinition">The actual type, not a substituted standard base.</param>
    /// <param name="Kind">The closest supported standard crane type.</param>
    public sealed record CraneEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinition,
        CraneElementKind Kind);

    /// <summary>
    /// A signal's real node, access capabilities and original value.
    /// </summary>
    /// <param name="Signal">The standard signal selector.</param>
    /// <param name="NodeId">The real signal node; null means the optional member is absent.</param>
    /// <param name="Value">The complete value, including bad quality and acquisition time.</param>
    /// <param name="Writable">Whether the server reports current-user write access.</param>
    public sealed record CraneSignalValue(CraneSignal Signal, NodeId NodeId, DataValue Value, bool Writable);

    /// <summary>
    /// A standard relationship retaining its real reference type and direction.
    /// </summary>
    /// <param name="Source">The source node.</param>
    /// <param name="ReferenceType">The real reference type.</param>
    /// <param name="Target">The target node.</param>
    public sealed record CraneRelationship(NodeId Source, NodeId ReferenceType, NodeId Target);

    /// <summary>
    /// The actual shared Machinery nameplate, retaining status and absence for each read field.
    /// </summary>
    /// <param name="NodeId">The existing Identification add-in.</param>
    /// <param name="Manufacturer">The DI-qualified manufacturer.</param>
    /// <param name="SerialNumber">The DI-qualified serial number.</param>
    /// <param name="ProductInstanceUri">The product-instance URI, mandatory for a machine nameplate.</param>
    /// <param name="Model">The optional model designation.</param>
    /// <param name="ProductCode">The optional product code.</param>
    public sealed record CraneIdentificationSnapshot(
        NodeId NodeId,
        DataValue Manufacturer,
        DataValue SerialNumber,
        DataValue ProductInstanceUri,
        DataValue Model,
        DataValue ProductCode);

    /// <summary>
    /// A typed component snapshot preserving optional absence, quality, source times and topology.
    /// </summary>
    /// <param name="Entry">The actual component identity.</param>
    /// <param name="Signals">The component's standard signals, including explicit optional absence.</param>
    /// <param name="Relationships">Physical composition and protection relationships.</param>
    /// <param name="AxisFunctionValues">The actual discrete-value metadata, when present.</param>
    /// <param name="Identification">The original nameplate, when present.</param>
    public sealed record CraneElementSnapshot(
        CraneEntry Entry,
        ArrayOf<CraneSignalValue> Signals,
        ArrayOf<CraneRelationship> Relationships,
        ArrayOf<EnumValueType> AxisFunctionValues,
        CraneIdentificationSnapshot? Identification = null);

    /// <summary>
    /// A bounded inventory rooted at one crane, not a namespace-wide capability claim.
    /// </summary>
    /// <param name="System">The root snapshot.</param>
    /// <param name="Elements">The actual configured motion devices, axes, motors, brakes and protections.</param>
    public sealed record CraneSystemSnapshot(
        CraneElementSnapshot System,
        ArrayOf<CraneElementSnapshot> Elements);
}
