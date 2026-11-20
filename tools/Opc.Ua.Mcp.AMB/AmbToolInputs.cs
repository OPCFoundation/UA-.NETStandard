/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The finite set of alias categories exposed by AMB discovery.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AmbAssetCategory>))]
    public enum AmbAssetCategory
    {
        /// <summary>
        /// Both AMB alias subcategories.
        /// </summary>
        Assets,


        /// <summary>
        /// Globally unique product-instance aliases.
        /// </summary>
        ByProductInstanceUri,


        /// <summary>
        /// User-assigned asset identifiers.
        /// </summary>
        ByAssetId
    }

    /// <summary>
    /// The read-only facets of one AMB asset.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AmbAssetFacet>))]
    public enum AmbAssetFacet
    {
        /// <summary>
        /// Identification and revision properties.
        /// </summary>
        Identification,


        /// <summary>
        /// The current DeviceHealth enumeration.
        /// </summary>
        Health,


        /// <summary>
        /// Current health alarms, including inactive conditions published in the address space.
        /// </summary>
        HealthAlarms,


        /// <summary>
        /// Only current health alarms whose Retain flag is true.
        /// </summary>
        RetainedAlarms,


        /// <summary>
        /// Current maintenance activities.
        /// </summary>
        Maintenance,


        /// <summary>
        /// Documentation links and their AddIn NodeId.
        /// </summary>
        Documentation,


        /// <summary>
        /// Location properties, local time and classifications.
        /// </summary>
        Context,


        /// <summary>
        /// Location objects containing the asset.
        /// </summary>
        Locations,


        /// <summary>
        /// Requirement entries with typed values and dictionary references.
        /// </summary>
        Requirements,


        /// <summary>
        /// Capability entries with typed values and dictionary references.
        /// </summary>
        Capabilities,


        /// <summary>
        /// Hierarchical sub-assets; remote targets are reported but not followed.
        /// </summary>
        SubAssets,


        /// <summary>
        /// Non-hierarchical asset relations in both directions.
        /// </summary>
        Relations,


        /// <summary>
        /// Identification, health, maintenance, documentation, context and locations.
        /// </summary>
        Snapshot
    }

    /// <summary>
    /// The finite AMB event streams available for a bounded observation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AmbObservationKind>))]
    public enum AmbObservationKind
    {
        /// <summary>
        /// Changes to the server's AMB alias categories.
        /// </summary>
        AssetSetChanges,


        /// <summary>
        /// Health alarm changes from the selected notifier.
        /// </summary>
        HealthAlarms,


        /// <summary>
        /// Maintenance activity changes from the selected notifier.
        /// </summary>
        Maintenance
    }

    /// <summary>
    /// Location kinds that have browsable location objects.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AmbLocationKind>))]
    public enum AmbLocationKind
    {
        /// <summary>
        /// The hierarchical location tree.
        /// </summary>
        Hierarchical,


        /// <summary>
        /// The operational location tree.
        /// </summary>
        Operational
    }
}
