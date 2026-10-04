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
using Opc.Ua.Di;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// The identification of an asset (OPC 10000-110 §7), read from the
    /// asset and its <c>2:Identification</c> group; empty values count as
    /// not set.
    /// </summary>
    public sealed record AssetIdentificationRecord
    {
        /// <summary>Gets the asset.</summary>
        public NodeId Asset { get; init; }

        /// <summary>Gets the <c>ProductInstanceUri</c>, which identifies the asset.</summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>Gets the <c>AssetId</c> users assigned.</summary>
        public string? AssetId { get; init; }

        /// <summary>Gets the manufacturer.</summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>Gets the URI of the manufacturer.</summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>Gets the model.</summary>
        public LocalizedText Model { get; init; }

        /// <summary>Gets the product code.</summary>
        public string? ProductCode { get; init; }

        /// <summary>Gets the serial number.</summary>
        public string? SerialNumber { get; init; }

        /// <summary>Gets the hardware revision (§10.2).</summary>
        public string? HardwareRevision { get; init; }

        /// <summary>Gets the software revision (§10.2).</summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>Gets the revision counter (§10.2).</summary>
        public int? RevisionCounter { get; init; }
    }

    /// <summary>
    /// A health alarm of an asset (OPC 10000-110 §9.3), from an event or
    /// read from the address space.
    /// </summary>
    public sealed record AssetAlarmRecord
    {
        /// <summary>Gets the condition, which is also the NodeId of the alarm.</summary>
        public NodeId ConditionId { get; init; }

        /// <summary>Gets the EventId, needed to acknowledge the alarm.</summary>
        public ByteString EventId { get; init; }

        /// <summary>Gets the alarm type.</summary>
        public NodeId EventType { get; init; }

        /// <summary>Gets the asset the alarm is about (§9.3).</summary>
        public NodeId SourceNode { get; init; }

        /// <summary>Gets the name of the asset.</summary>
        public string? SourceName { get; init; }

        /// <summary>Gets the name of the alarm.</summary>
        public string? ConditionName { get; init; }

        /// <summary>Gets the time of the event.</summary>
        public DateTimeUtc Time { get; init; }

        /// <summary>Gets the message.</summary>
        public LocalizedText Message { get; init; }

        /// <summary>Gets the severity (OPC 10000-110 Table 15).</summary>
        public ushort Severity { get; init; }

        /// <summary>Gets the fault category of the severity, or null for an inactive one.</summary>
        public AssetFaultSeverity? FaultCategory => AssetFaultSeverities.Classify(Severity);

        /// <summary>Gets the condition class.</summary>
        public NodeId ConditionClassId { get; init; }

        /// <summary>Gets the name of the condition class.</summary>
        public LocalizedText ConditionClassName { get; init; }

        /// <summary>Gets the AMB condition class, or null for another one.</summary>
        public AmbConditionClass? ConditionClass { get; init; }

        /// <summary>Gets whether the alarm is active.</summary>
        public bool IsActive { get; init; }

        /// <summary>Gets whether the alarm is acknowledged.</summary>
        public bool IsAcknowledged { get; init; }

        /// <summary>Gets whether the server retains the alarm.</summary>
        public bool Retain { get; init; }

        /// <summary>Gets the comment of the last acknowledgement or comment, if any.</summary>
        public LocalizedText Comment { get; init; }

        /// <summary>
        /// Gets the potential root causes (§9.4.2): empty when the alarm
        /// itself is the cause, an entry without <c>RootCauseId</c> when the
        /// cause is unknown, and null when the alarm publishes none.
        /// </summary>
        public ArrayOf<RootCauseDataType> PotentialRootCauses { get; init; }
    }

    /// <summary>
    /// A current or future maintenance activity of an asset (OPC 10000-110
    /// §12), from an event or read from the address space.
    /// </summary>
    public sealed record MaintenanceActivityRecord
    {
        /// <summary>Gets the condition.</summary>
        public NodeId ConditionId { get; init; }

        /// <summary>Gets the EventId, needed to acknowledge the condition.</summary>
        public ByteString EventId { get; init; }

        /// <summary>Gets the condition type.</summary>
        public NodeId EventType { get; init; }

        /// <summary>Gets the asset.</summary>
        public NodeId SourceNode { get; init; }

        /// <summary>Gets the name of the activity.</summary>
        public string? ConditionName { get; init; }

        /// <summary>Gets the time of the event.</summary>
        public DateTimeUtc Time { get; init; }

        /// <summary>Gets the message: the description, or what happened.</summary>
        public LocalizedText Message { get; init; }

        /// <summary>Gets the condition class.</summary>
        public NodeId ConditionClassId { get; init; }

        /// <summary>Gets the AMB condition class, or null for another one.</summary>
        public AmbConditionClass? ConditionClass { get; init; }

        /// <summary>Gets whether the condition is active.</summary>
        public bool IsActive { get; init; }

        /// <summary>Gets whether the condition is acknowledged.</summary>
        public bool IsAcknowledged { get; init; }

        /// <summary>Gets whether the server retains the condition.</summary>
        public bool Retain { get; init; }

        /// <summary>Gets the comment of the last acknowledgement or comment, if any.</summary>
        public LocalizedText Comment { get; init; }

        /// <summary>Gets the state, or null when the server published none.</summary>
        public MaintenanceStateKind? State { get; init; }

        /// <summary>Gets the planned date.</summary>
        public DateTimeUtc? PlannedDate { get; init; }

        /// <summary>Gets the estimated downtime.</summary>
        public TimeSpan? EstimatedDowntime { get; init; }

        /// <summary>Gets the maintenance supplier.</summary>
        public NameNodeIdDataType? MaintenanceSupplier { get; init; }

        /// <summary>Gets the qualification of the personnel.</summary>
        public NameNodeIdDataType? QualificationOfPersonnel { get; init; }

        /// <summary>Gets the parts replaced; null when not published.</summary>
        public ArrayOf<NameNodeIdDataType> PartsOfAssetReplaced { get; init; }

        /// <summary>Gets the parts serviced; null when not published.</summary>
        public ArrayOf<NameNodeIdDataType> PartsOfAssetServiced { get; init; }

        /// <summary>Gets the maintenance method.</summary>
        public MaintenanceMethodEnum? MaintenanceMethod { get; init; }

        /// <summary>Gets whether the configuration is to be, or was, changed.</summary>
        public bool? ConfigurationChanged { get; init; }
    }

    /// <summary>
    /// A link of the <c>DocumentationLinks</c> AddIn (OPC 10000-110 §10.5).
    /// </summary>
    /// <param name="NodeId">The link variable.</param>
    /// <param name="BrowseName">Its browse name.</param>
    /// <param name="DisplayName">Its display name.</param>
    /// <param name="Uri">The link.</param>
    /// <param name="IsWritable">Whether the session may write the link.</param>
    public sealed record DocumentationLinkRecord(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        string Uri,
        bool IsWritable)
    {
        /// <summary>
        /// Gets whether a user added the link through <c>AddLink</c>, which
        /// makes it the kind of link <c>RemoveLink</c> accepts (§10.5.4).
        /// </summary>
        /// <remarks>
        /// OPC 10000-110 has no way to tell; the AMB server of this SDK marks
        /// such a link with the
        /// <see cref="DocumentationLinkProperties.UserLink"/> Property. The
        /// value is <see langword="false"/> for every link of a server that
        /// does not.
        /// </remarks>
        public bool IsUserLink { get; init; }
    }

    /// <summary>
    /// A level of a location hierarchy (OPC 10000-110 §13.3.3, §13.4.3).
    /// </summary>
    /// <param name="NodeId">The location object.</param>
    /// <param name="Path">The browse names from the entry point, separated by a slash.</param>
    /// <param name="Assets">The objects the location contains.</param>
    public sealed record AssetLocationNode(NodeId NodeId, string Path, ArrayOf<ExpandedNodeId> Assets);

    /// <summary>
    /// A location that contains an asset.
    /// </summary>
    /// <param name="Kind">The kind of location.</param>
    /// <param name="Location">The location object.</param>
    public sealed record AssetLocationRecord(AssetLocationKind Kind, NodeId Location);

    /// <summary>
    /// Where an asset is and how it is classified (OPC 10000-110 §11, §13).
    /// </summary>
    public sealed record AssetContextRecord
    {
        /// <summary>Gets the <c>HierarchicalLocation</c> Property.</summary>
        public string? HierarchicalLocation { get; init; }

        /// <summary>Gets the <c>OperationalLocation</c> Property.</summary>
        public string? OperationalLocation { get; init; }

        /// <summary>Gets the <c>DigitalLocation</c> Property.</summary>
        public string? DigitalLocation { get; init; }

        /// <summary>Gets the <c>0:LocalTime</c> Property.</summary>
        public TimeZoneDataType? LocalTime { get; init; }

        /// <summary>Gets the dictionary entries that classify the asset.</summary>
        public ArrayOf<ExpandedNodeId> Classifications { get; init; }
    }

    /// <summary>
    /// An entry of the <c>Requirements</c> or <c>Capabilities</c> folder
    /// (OPC 10000-110 §10.6, §10.7).
    /// </summary>
    /// <param name="BrowseName">The browse name.</param>
    /// <param name="Value">The value.</param>
    /// <param name="DictionaryEntries">The dictionary entries it references.</param>
    public sealed record AssetEntryRecord(
        QualifiedName BrowseName,
        Variant Value,
        ArrayOf<ExpandedNodeId> DictionaryEntries);

    /// <summary>
    /// The folders OPC 10000-110 §10.6 and §10.7 define.
    /// </summary>
    public enum AssetEntryFolder
    {
        /// <summary>The <c>Requirements</c> folder.</summary>
        Requirements,

        /// <summary>The <c>Capabilities</c> folder.</summary>
        Capabilities
    }

    /// <summary>
    /// A non-hierarchical relation of an asset to another asset
    /// (OPC 10000-110 §14.2).
    /// </summary>
    /// <param name="ReferenceTypeId">The reference type, for example <c>0:Utilizes</c>.</param>
    /// <param name="IsInverse">Whether the other asset holds the forward reference.</param>
    /// <param name="Target">The other asset.</param>
    public sealed record AssetRelation(NodeId ReferenceTypeId, bool IsInverse, ExpandedNodeId Target);

    /// <summary>
    /// Everything the client reads about one asset in one call.
    /// </summary>
    public sealed record AssetSnapshot
    {
        /// <summary>Gets the asset.</summary>
        public NodeId Asset { get; init; }

        /// <summary>Gets the identification.</summary>
        public AssetIdentificationRecord Identification { get; init; } = new();

        /// <summary>Gets the <c>DeviceHealth</c>, or null when the asset publishes none.</summary>
        public DeviceHealthEnumeration? DeviceHealth { get; init; }

        /// <summary>Gets the health alarms.</summary>
        public ArrayOf<AssetAlarmRecord> HealthAlarms { get; init; }

        /// <summary>Gets the maintenance activities.</summary>
        public ArrayOf<MaintenanceActivityRecord> MaintenanceActivities { get; init; }

        /// <summary>Gets the documentation links.</summary>
        public ArrayOf<DocumentationLinkRecord> DocumentationLinks { get; init; }

        /// <summary>Gets the location Properties, local time and classification.</summary>
        public AssetContextRecord Context { get; init; } = new();

        /// <summary>Gets the locations that contain the asset.</summary>
        public ArrayOf<AssetLocationRecord> Locations { get; init; }
    }
}
