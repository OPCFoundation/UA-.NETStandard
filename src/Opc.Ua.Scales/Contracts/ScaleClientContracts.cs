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

namespace Opc.Ua.Scales
{
    /// <summary>
    /// A scale or scale system a client discovered.
    /// </summary>
    /// <param name="NodeId">The node.</param>
    /// <param name="BrowseName">The browse name.</param>
    /// <param name="DisplayName">The display name.</param>
    /// <param name="TypeDefinition">The type definition, possibly a vendor subtype.</param>
    /// <param name="Kind">
    /// The standard scale kind the type is or derives from; null for a scale
    /// system.
    /// </param>
    /// <param name="IsScaleSystem">Whether the node is a <c>ScaleSystemType</c>.</param>
    public sealed record ScaleEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinition,
        ScaleKind? Kind,
        bool IsScaleSystem);

    /// <summary>
    /// A product of a production preset (OPC 40200 §7.8).
    /// </summary>
    /// <param name="NodeId">The product node.</param>
    /// <param name="ProductId">The product id.</param>
    /// <param name="ProductName">The product name.</param>
    /// <param name="TypeDefinition">The product type.</param>
    /// <param name="Processing">
    /// Whether the product is in processing (<c>ProductMode</c>), when
    /// published.
    /// </param>
    public sealed record ScaleProductInfo(
        NodeId NodeId,
        string ProductId,
        LocalizedText ProductName,
        NodeId TypeDefinition,
        bool? Processing);

    /// <summary>
    /// A decoded <c>ScaleEventType</c> event or <c>ScaleAlarmType</c>
    /// condition notification (OPC 40200 §8).
    /// </summary>
    public sealed record ScaleNotificationInfo
    {
        /// <summary>
        /// Gets the node the notification was reported for.
        /// </summary>
        public NodeId SourceNode { get; init; }

        /// <summary>
        /// Gets the source name.
        /// </summary>
        public string? SourceName { get; init; }

        /// <summary>
        /// Gets the time the notification occurred.
        /// </summary>
        public DateTime Time { get; init; }

        /// <summary>
        /// Gets the severity (1..1000).
        /// </summary>
        public ushort Severity { get; init; }

        /// <summary>
        /// Gets the message.
        /// </summary>
        public LocalizedText Message { get; init; }

        /// <summary>
        /// Gets the notification category.
        /// </summary>
        public ScaleNotificationCategory Category { get; init; }

        /// <summary>
        /// Gets the notification id (an Annex C id or a vendor id above 5000).
        /// </summary>
        public uint NotificationId { get; init; }

        /// <summary>
        /// Gets the vendor notification id, when published.
        /// </summary>
        public string? VendorNotificationId { get; init; }

        /// <summary>
        /// Gets the auxiliary parameters of the message, when published.
        /// </summary>
        public ArrayOf<string> AuxParameters { get; init; } = [];

        /// <summary>
        /// Gets whether the notification is an alarm condition.
        /// </summary>
        public bool IsAlarm { get; init; }

        /// <summary>
        /// Gets whether an alarm is active; null for an event.
        /// </summary>
        public bool? Active { get; init; }

        /// <summary>
        /// Gets the notification id as a defined OPC 40200 id, or null for a
        /// vendor id.
        /// </summary>
        public ScaleNotificationId? DefinedId =>
            NotificationId <= ScalesModel.MinimumVendorNotificationId
                ? (ScaleNotificationId)NotificationId
                : null;
    }

    /// <summary>
    /// Everything a client typically wants to know about a scale, read in
    /// one pass.
    /// </summary>
    public sealed record ScaleSnapshot
    {
        /// <summary>
        /// Gets the scale.
        /// </summary>
        public required ScaleEntry Scale { get; init; }

        /// <summary>
        /// Gets the identification.
        /// </summary>
        public required ScaleIdentification Identification { get; init; }

        /// <summary>
        /// Gets the current weight.
        /// </summary>
        public required ScaleReading CurrentWeight { get; init; }

        /// <summary>
        /// Gets the registered weight, when the scale publishes one.
        /// </summary>
        public ScaleReading? RegisteredWeight { get; init; }

        /// <summary>
        /// Gets the weighing ranges.
        /// </summary>
        public ArrayOf<WeighingRangeDefinition> WeighingRanges { get; init; } = [];

        /// <summary>
        /// Gets the engineering units methods accept, when published.
        /// </summary>
        public ArrayOf<EUInformation> AllowedEngineeringUnits { get; init; } = [];

        /// <summary>
        /// Gets the products of the production preset.
        /// </summary>
        public ArrayOf<ScaleProductInfo> Products { get; init; } = [];

        /// <summary>
        /// Gets the ids of the products in processing.
        /// </summary>
        public ArrayOf<string> CurrentProducts { get; init; } = [];

        /// <summary>
        /// Gets the PackML state number of the innermost active state, when
        /// the scale publishes a PackML <c>State</c>.
        /// </summary>
        public uint? PackMLState { get; init; }
    }
}
