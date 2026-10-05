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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AMB.Server.DocumentationLinks;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.AMB.Server.Structure;
using Opc.Ua.Di;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// A registered manageable asset.
    /// </summary>
    public interface IAssetHandle
    {
        /// <summary>
        /// Gets the NodeId of the asset object.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the browse name of the asset object.
        /// </summary>
        QualifiedName BrowseName { get; }

        /// <summary>
        /// Gets the current <c>ProductInstanceUri</c> of the asset, or an
        /// empty string when it publishes none.
        /// </summary>
        string ProductInstanceUri { get; }

        /// <summary>
        /// Gets the current <c>AssetId</c> of the asset, or
        /// <see langword="null"/> when it publishes none or none is assigned.
        /// </summary>
        string? AssetId { get; }

        /// <summary>
        /// Gets whether clients can write the <c>AssetId</c> and the server
        /// keeps what they write.
        /// </summary>
        bool IsAssetIdConfigurable { get; }

        /// <summary>
        /// Gets whether the asset is still registered.
        /// </summary>
        bool IsRegistered { get; }

        /// <summary>
        /// Gets the health status of the asset (OPC 10000-110 §9), or
        /// <see langword="null"/> when it was registered without
        /// <c>DeviceHealth</c> and health alarms.
        /// </summary>
        IAssetHealth? Health { get; }

        /// <summary>
        /// Gets the maintenance activities of the asset (OPC 10000-110 §12),
        /// or <see langword="null"/> when it was registered without one.
        /// </summary>
        IAssetMaintenance? Maintenance { get; }

        /// <summary>
        /// Gets the <c>DocumentationLinks</c> AddIn of the asset
        /// (OPC 10000-110 §10.5), or <see langword="null"/> when it was
        /// registered without one.
        /// </summary>
        IDocumentationLinks? DocumentationLinks { get; }

        /// <summary>
        /// Increments the <c>RevisionCounter</c> of the asset after its
        /// configuration changed (OPC 10000-110 §10.2).
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The new value.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the asset has no
        /// <c>RevisionCounter</c>.
        /// </exception>
        ValueTask<int> IncrementRevisionCounterAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the asset from the registry. The asset object stays in its
        /// node manager; what registering added to it is not removed.
        /// </summary>
        /// <param name="cancellationToken">
        /// The cancellation token; honored before the asset is removed, since
        /// a removal that began withdraws its aliases and locations to the end.
        /// </param>
        ValueTask UnregisterAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Adds the OPC 10000-110 building blocks to an asset while it is
    /// registered.
    /// </summary>
    public interface IAssetBuilder
    {
        /// <summary>
        /// Makes the <c>2:AssetId</c> of the asset writable for clients and
        /// persists what they write (OPC 10000-110 §7, "AMB Configurable
        /// Asset Identification").
        /// </summary>
        /// <remarks>
        /// <para>
        /// An existing <c>AssetId</c>, on the asset or in its
        /// <c>2:Identification</c> group, is used. Otherwise the property is
        /// created on the asset, through the slot its type declares when it
        /// declares one, and else as a plain property together with a
        /// <c>HasInterface</c> reference to <c>2:ITagNameplateType</c>.
        /// </para>
        /// <para>
        /// A value persisted earlier wins over <paramref name="defaultAssetId"/>
        /// and over the value the asset was created with; existing write
        /// handlers of the property keep running.
        /// </para>
        /// </remarks>
        /// <param name="defaultAssetId">
        /// The value used while nothing is persisted and the property has no
        /// value yet.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithConfigurableAssetId(string? defaultAssetId = null);

        /// <summary>
        /// Gives the asset the OPC 10000-100 <c>DeviceHealth</c> variable
        /// (OPC 10000-110 §9.2, "AMB Asset Health Status Base").
        /// </summary>
        /// <remarks>
        /// An existing <c>2:DeviceHealth</c> is used; otherwise it is created
        /// through the slot its type declares, and else together with a
        /// <c>HasInterface</c> reference to <c>2:IDeviceHealthType</c>.
        /// </remarks>
        /// <param name="initial">The health the asset starts with.</param>
        /// <param name="deriveFromAlarms">
        /// Whether <c>DeviceHealth</c> follows the active health alarms, the
        /// most severe NE 107 signal first, instead of being set by the
        /// application.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithDeviceHealth(
            DeviceHealthEnumeration initial = DeviceHealthEnumeration.NORMAL,
            bool deriveFromAlarms = false);

        /// <summary>
        /// Gives the asset a health alarm (OPC 10000-110 §9.3): an
        /// OPC 10000-100 alarm with the asset as its source, the condition
        /// class and the potential root causes of §9.4, listed in the
        /// <c>2:DeviceHealthAlarms</c> folder of the asset.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The alarm is created while the node manager that owns the asset
        /// builds its address space, so the asset has to be registered from
        /// its setup.
        /// </para>
        /// <para>
        /// An asset with health alarms has <c>2:DeviceHealth</c> as well
        /// (§9.2): without <see cref="WithDeviceHealth"/> it is created and
        /// follows the active alarms.
        /// </para>
        /// </remarks>
        /// <param name="name">
        /// The name of the alarm, unique on the asset; also its browse name
        /// and <c>ConditionName</c>.
        /// </param>
        /// <param name="kind">The alarm type.</param>
        /// <param name="conditionClass">The condition class the alarm reports.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="name"/> is empty or already used on the asset.
        /// </exception>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="conditionClass"/> is <c>null</c>.
        /// </exception>
        IAssetBuilder WithHealthAlarm(string name, AssetHealthAlarmKind kind, AmbConditionClass conditionClass);

        /// <summary>
        /// Gives the asset a current or future maintenance activity
        /// (OPC 10000-110 §12, "AMB Current and Future Maintenance
        /// Activities"): a condition that implements
        /// <c>IMaintenanceEventType</c>, starts in the state <c>Planned</c>
        /// and is listed in the <c>2:DeviceHealthAlarms</c> folder.
        /// </summary>
        /// <remarks>
        /// Like a health alarm, the condition is created while the node
        /// manager that owns the asset builds its address space.
        /// </remarks>
        /// <param name="name">
        /// The name of the activity, unique among the alarms and activities of
        /// the asset; also its browse name and <c>ConditionName</c>.
        /// </param>
        /// <param name="conditionClass">
        /// The maintenance condition class (§12.1 asks for
        /// <c>MaintenanceConditionClassType</c> or a subtype), for example
        /// <see cref="AmbConditionClass.Inspection"/>.
        /// </param>
        /// <param name="configure">Sets what the activity publishes initially.</param>
        /// <param name="onStateChangedAsync">
        /// Runs after every transition, for example to put a machine into its
        /// maintenance operation mode while the activity executes.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="name"/> is empty or already used on the asset, or
        /// <paramref name="conditionClass"/> is not a maintenance class.
        /// </exception>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="conditionClass"/> is <c>null</c>.
        /// </exception>
        IAssetBuilder WithMaintenance(
            string name,
            AmbConditionClass conditionClass,
            Action<MaintenanceActivityDetails>? configure = null,
            Func<MaintenanceStateKind, CancellationToken, ValueTask>? onStateChangedAsync = null);

        /// <summary>
        /// Gives the asset the <c>DocumentationLinks</c> AddIn
        /// (OPC 10000-110 §10.5): links to documentation managed outside the
        /// server, provided by the manufacturer, editable by users, or added
        /// and removed by them.
        /// </summary>
        /// <remarks>
        /// The AddIn is referenced from the asset with <c>0:HasAddIn</c>.
        /// Links users change or add are persisted in the
        /// <c>IAssetConfigurationStore</c> under the asset's
        /// <c>ProductInstanceUri</c>; <see cref="AmbServerOptions.AuthorizeLinkEdit"/>
        /// decides who may change them. Calling this again adds to the
        /// same AddIn.
        /// </remarks>
        /// <param name="configure">Declares the links.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        IAssetBuilder WithDocumentationLinks(Action<IDocumentationLinksBuilder> configure);

        /// <summary>
        /// Gives the asset its version information (OPC 10000-110 §10.2,
        /// "AMB Version Information"): the OPC 10000-100
        /// <c>HardwareRevision</c> and/or <c>SoftwareRevision</c> and the
        /// <c>RevisionCounter</c>.
        /// </summary>
        /// <remarks>
        /// Properties the asset publishes already, on itself or in its
        /// <c>2:Identification</c> group - a Device Integration device has
        /// all three - get the values passed here; the others are created. A
        /// software-only asset passes no hardware revision and the other way
        /// round (§10.2).
        /// </remarks>
        /// <param name="hardwareRevision">The hardware revision, or null to leave it.</param>
        /// <param name="softwareRevision">The software revision, or null to leave it.</param>
        /// <param name="revisionCounter">
        /// The revision counter, or null to keep an existing one; a created one
        /// starts at 0.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithVersionInformation(
            string? hardwareRevision = null,
            string? softwareRevision = null,
            int? revisionCounter = null);

        /// <summary>
        /// Gives the asset the <c>HierarchicalLocation</c>,
        /// <c>OperationalLocation</c> or <c>DigitalLocation</c> Property in
        /// the AMB namespace (OPC 10000-110 §13.3.2, §13.4.2, §13.5).
        /// </summary>
        /// <param name="kind">The location Property.</param>
        /// <param name="value">The location; unknown when null.</param>
        /// <param name="writable">
        /// Whether clients can write the location; what they write is
        /// persisted under the asset's <c>ProductInstanceUri</c>.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithLocation(AssetLocationKind kind, string? value = null, bool writable = false);

        /// <summary>
        /// Gives the asset the <c>0:LocalTime</c> Property with the time zone
        /// it is in (OPC 10000-110 §13.2).
        /// </summary>
        /// <param name="offset">The offset from UTC in minutes.</param>
        /// <param name="daylightSavingInOffset">Whether the offset includes daylight saving.</param>
        /// <param name="writable">
        /// Whether clients can write it; what they write is persisted.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithLocalTime(short offset, bool daylightSavingInOffset = false, bool writable = false);

        /// <summary>
        /// Puts the asset into a location below <c>HierarchicalLocations</c>
        /// or <c>OperationalLocations</c>, creating the levels as needed; the
        /// <c>HierarchicalContains</c> or <c>OperationalContains</c>
        /// reference is exposed in both directions (§13.1).
        /// </summary>
        /// <param name="kind">
        /// <see cref="AssetLocationKind.Hierarchical"/> or
        /// <see cref="AssetLocationKind.Operational"/>.
        /// </param>
        /// <param name="path">The levels, separated by a slash; the asset belongs to the deepest.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="kind"/> is <see cref="AssetLocationKind.Digital"/>, or
        /// <paramref name="path"/> is empty.
        /// </exception>
        IAssetBuilder LocatedIn(AssetLocationKind kind, string path);

        /// <summary>
        /// Classifies the asset with an entry of an external dictionary such
        /// as IEC CDD or ECLASS, through <c>0:HasDictionaryEntry</c>
        /// (OPC 10000-110 §11).
        /// </summary>
        /// <param name="dictionaryEntry">The dictionary entry.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="dictionaryEntry"/> is null.</exception>
        IAssetBuilder ClassifiedAs(ExpandedNodeId dictionaryEntry);

        /// <summary>
        /// Gives the asset the <c>Requirements</c> folder (OPC 10000-110 §10.6).
        /// </summary>
        /// <param name="configure">Adds the requirements.</param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithRequirements(Action<IAssetEntriesBuilder> configure);

        /// <summary>
        /// Gives the asset the <c>Capabilities</c> folder (OPC 10000-110 §10.7).
        /// </summary>
        /// <param name="configure">Adds the capabilities.</param>
        /// <returns>The same builder, for chaining.</returns>
        IAssetBuilder WithCapabilities(Action<IAssetEntriesBuilder> configure);

        /// <summary>
        /// Relates the asset to another node (OPC 10000-110 §14): with a
        /// hierarchical reference such as <c>HasComponent</c> to a sub-asset,
        /// with a non-hierarchical one such as <c>0:Utilizes</c> or
        /// <c>0:IsPhysicallyConnectedTo</c> (OPC 10000-23) to a related
        /// asset. The inverse reference is added to the target when it
        /// exists.
        /// </summary>
        /// <param name="referenceTypeId">The reference type.</param>
        /// <param name="target">The related node.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentException">An argument is null.</exception>
        IAssetBuilder RelatesTo(NodeId referenceTypeId, NodeId target);
    }
}
