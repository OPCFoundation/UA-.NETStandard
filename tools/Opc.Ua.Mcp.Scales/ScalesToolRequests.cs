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

using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The finite discovery scopes supported by the Scales client.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesDiscoveryScope>))]
    public enum ScalesDiscoveryScope
    {
        /// <summary>
        /// Both standard roots, deduplicated by NodeId.
        /// </summary>
        All,

        /// <summary>
        /// The direct children of an explicitly supplied folder.
        /// </summary>
        Under,

        /// <summary>
        /// The SubDevices of a scale or scale system.
        /// </summary>
        System
    }

    /// <summary>
    /// The finite read facets of an OPC 40200 scale or system.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesReadFacet>))]
    public enum ScalesReadFacet
    {
        /// <summary>
        /// Identification fields and their locales.
        /// </summary>
        Identification,

        /// <summary>
        /// Current measurement, quality and units.
        /// </summary>
        CurrentWeight,

        /// <summary>
        /// The last registered measurement.
        /// </summary>
        RegisteredWeight,

        /// <summary>
        /// Weighing ranges ordered by capacity.
        /// </summary>
        WeighingRanges,

        /// <summary>
        /// Engineering units accepted by commands.
        /// </summary>
        AllowedEngineeringUnits,

        /// <summary>
        /// Products, their processing state and actual DI Lock NodeIds.
        /// </summary>
        Products,

        /// <summary>
        /// Identifiers of products currently in processing.
        /// </summary>
        CurrentProducts,

        /// <summary>
        /// The innermost active PackML state number.
        /// </summary>
        PackMLState,

        /// <summary>
        /// All read facets, with independently bounded collection pages.
        /// </summary>
        Snapshot
    }

    /// <summary>
    /// The finite scale observation streams.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesObservationFacet>))]
    public enum ScalesObservationFacet
    {
        /// <summary>
        /// Current weight changes.
        /// </summary>
        Weight,

        /// <summary>
        /// OPC 40200 events and alarms.
        /// </summary>
        Notifications
    }

    /// <summary>
    /// Standard argument-less OPC 40200 commands; vendor method names are never accepted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesStandardCommand>))]
    public enum ScalesStandardCommand
    {
        /// <summary>
        /// Starts discharging a loss-in-weight scale.
        /// </summary>
        DischargeStart,

        /// <summary>
        /// Stops discharging a loss-in-weight scale.
        /// </summary>
        DischargeStop,

        /// <summary>
        /// Starts refilling a loss-in-weight scale.
        /// </summary>
        RefillStart,

        /// <summary>
        /// Stops refilling a loss-in-weight scale.
        /// </summary>
        RefillStop,

        /// <summary>
        /// Starts leveling a laboratory scale.
        /// </summary>
        StartLeveling,

        /// <summary>
        /// Starts calibrating a laboratory scale.
        /// </summary>
        StartCalibration,

        /// <summary>
        /// Starts the laboratory ionisator.
        /// </summary>
        StartIonisator,

        /// <summary>
        /// Stops the laboratory ionisator.
        /// </summary>
        StopIonisator,

        /// <summary>
        /// Resets a totalizer.
        /// </summary>
        ResetTotalizer,

        /// <summary>
        /// Resets a scale system's global statistics.
        /// </summary>
        ResetGlobalStatistics
    }

    /// <summary>
    /// Standard vehicle weighing operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesVehicleCommand>))]
    public enum ScalesVehicleCommand
    {
        /// <summary>
        /// Registers inbound weighing.
        /// </summary>
        Inbound,

        /// <summary>
        /// Registers outbound weighing.
        /// </summary>
        Outbound,

        /// <summary>
        /// Registers one-pass weighing.
        /// </summary>
        OnePass
    }

    /// <summary>
    /// Standard recipe processing operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScalesRecipeCommand>))]
    public enum ScalesRecipeCommand
    {
        /// <summary>
        /// Starts the selected recipe.
        /// </summary>
        Start,

        /// <summary>
        /// Stops processing the selected recipe.
        /// </summary>
        Stop,

        /// <summary>
        /// Continues the selected recipe.
        /// </summary>
        Continue,

        /// <summary>
        /// Skips the current recipe element.
        /// </summary>
        SkipCurrentElement,

        /// <summary>
        /// Aborts the selected recipe.
        /// </summary>
        Abort
    }

    /// <summary>
    /// Concrete engineering-unit input, independent of the UA encoding's wire representation.
    /// </summary>
    public sealed class ScalesEngineeringUnit
    {
        /// <summary>
        /// Gets or sets the namespace that defines the unit identifier.
        /// </summary>
        [Description("Unit namespace, normally http://www.opcfoundation.org/UA/units/un/cefact.")]
        public string NamespaceUri { get; set; } = "http://www.opcfoundation.org/UA/units/un/cefact";

        /// <summary>
        /// Gets or sets the server-advertised unit identifier.
        /// </summary>
        [Description("Numeric unitId from scales_read AllowedEngineeringUnits; e.g. 4933453 for kilogram.")]
        public required int UnitId { get; set; }

        /// <summary>
        /// Gets or sets the unit's display text.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the unit's descriptive text.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Converts the concrete request to the generated OPC UA type.
        /// </summary>
        internal EUInformation ToUnits()
        {
            return new EUInformation
            {
                NamespaceUri = NamespaceUri,
                UnitId = UnitId,
                DisplayName = DisplayName is null ? LocalizedText.Null : new LocalizedText(DisplayName),
                Description = Description is null ? LocalizedText.Null : new LocalizedText(Description)
            };
        }
    }

    /// <summary>
    /// Concrete recipe-element input with standard JSON-array predecessor NodeIds.
    /// </summary>
    public sealed class ScalesRecipeElementRequest
    {
        /// <summary>
        /// Gets or sets the standard or vendor recipe element type NodeId.
        /// </summary>
        public required string ElementTypeNodeId { get; set; }

        /// <summary>
        /// Gets or sets the name of the new element.
        /// </summary>
        public required string ElementName { get; set; }

        /// <summary>
        /// Gets or sets predecessor NodeIds; use the recipe NodeId for a starting element.
        /// </summary>
        [Description("JSON array of predecessor NodeId strings (1..500). Use the recipe NodeId for a start element.")]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public required ArrayOf<string> PreviousElements { get; set; }
    }
}
