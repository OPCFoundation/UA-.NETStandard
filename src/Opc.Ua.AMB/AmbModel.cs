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

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The browse names OPC 10000-110 standardizes without publishing a node
    /// for them, so the generated <see cref="BrowseNames"/> class cannot
    /// carry them.
    /// </summary>
    /// <remarks>
    /// Every name is qualified with the AMB namespace
    /// (<see cref="Namespaces.AMB"/>) when used, as the specification
    /// requires.
    /// </remarks>
    public static class AmbBrowseNames
    {
        /// <summary>
        /// The folder holding the requirements of an asset (§10.6).
        /// </summary>
        public const string Requirements = "Requirements";

        /// <summary>
        /// The folder holding the capabilities of an asset (§10.7).
        /// </summary>
        public const string Capabilities = "Capabilities";

        /// <summary>
        /// The <c>String</c> Property carrying the hierarchical location of
        /// an asset (§13.3.2).
        /// </summary>
        public const string HierarchicalLocation = "HierarchicalLocation";

        /// <summary>
        /// The <c>String</c> Property carrying the operational location of
        /// an asset (§13.4.2).
        /// </summary>
        public const string OperationalLocation = "OperationalLocation";

        /// <summary>
        /// The <c>String</c> (preferably <c>UriString</c>) Property carrying
        /// the digital location of an asset (§13.5).
        /// </summary>
        public const string DigitalLocation = "DigitalLocation";

        /// <summary>
        /// The alias name used in <c>AssetsByAssetId</c> for an asset whose
        /// <c>AssetId</c> is null or empty (§8.2.3). Each such asset gets an
        /// alias object of its own.
        /// </summary>
        public const string NoAssetIdAssigned = "NoAssetIdAssigned";

        /// <summary>
        /// Qualifies one of the AMB browse names with the AMB namespace.
        /// </summary>
        /// <param name="name">The name, typically one of the constants.</param>
        /// <param name="namespaceUris">The namespace table of the server or session.</param>
        /// <returns>
        /// The qualified name, or <see cref="QualifiedName.Null"/> when the
        /// AMB namespace is not in <paramref name="namespaceUris"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> or <paramref name="namespaceUris"/> is <c>null</c>.
        /// </exception>
        public static QualifiedName Qualify(string name, NamespaceTable namespaceUris)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            int index = namespaceUris.GetIndex(Namespaces.AMB);
            return index < 0 ? QualifiedName.Null : new QualifiedName(name, (ushort)index);
        }
    }

    /// <summary>
    /// The standard OPC 10000-9 condition class an AMB condition class
    /// refines.
    /// </summary>
    public enum AmbConditionClassBase
    {
        /// <summary>
        /// <c>SystemConditionClassType</c>, directly or through another AMB
        /// class.
        /// </summary>
        System,

        /// <summary><c>MaintenanceConditionClassType</c>.</summary>
        Maintenance
    }

    /// <summary>
    /// One of the fourteen condition classes OPC 10000-110 defines, used as
    /// the <c>ConditionClassId</c> of an asset health alarm (§9.5) or of a
    /// maintenance condition (§12.4).
    /// </summary>
    /// <remarks>
    /// All AMB condition classes are abstract ObjectTypes, so a condition
    /// references one by its type id; the class name a condition reports as
    /// <c>ConditionClassName</c> is the type's browse name.
    /// </remarks>
    public sealed class AmbConditionClass
    {
        private AmbConditionClass(string name, uint objectTypeId, AmbConditionClassBase baseClass)
        {
            Name = name;
            ObjectTypeId = objectTypeId;
            BaseClass = baseClass;
        }

        /// <summary>The asset lost the connection to a sensor or peer (§9.5.2).</summary>
        public static AmbConditionClass ConnectionFailure { get; } = new(
            BrowseNames.ConnectionFailureConditionClassType,
            ObjectTypes.ConnectionFailureConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>The asset is too hot (§9.5.3).</summary>
        public static AmbConditionClass OverTemperature { get; } = new(
            BrowseNames.OverTemperatureConditionClassType,
            ObjectTypes.OverTemperatureConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>A calibration of the asset is due (§9.5.4).</summary>
        public static AmbConditionClass CalibrationDue { get; } = new(
            BrowseNames.CalibrationDueConditionClassType,
            ObjectTypes.CalibrationDueConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>A self test of the asset failed (§9.5.5).</summary>
        public static AmbConditionClass SelfTestFailure { get; } = new(
            BrowseNames.SelfTestFailureConditionClassType,
            ObjectTypes.SelfTestFailureConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>A firmware update of the asset is in progress (§9.5.6).</summary>
        public static AmbConditionClass FlashUpdateInProgress { get; } = new(
            BrowseNames.FlashUpdateInProgressConditionClassType,
            ObjectTypes.FlashUpdateInProgressConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>A firmware update of the asset failed (§9.5.7).</summary>
        public static AmbConditionClass FlashUpdateFailed { get; } = new(
            BrowseNames.FlashUpdateFailedConditionClassType,
            ObjectTypes.FlashUpdateFailedConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>The configuration of the asset is invalid (§9.5.8).</summary>
        public static AmbConditionClass BadConfiguration { get; } = new(
            BrowseNames.BadConfigurationConditionClassType,
            ObjectTypes.BadConfigurationConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>The asset ran out of a resource (§9.5.9).</summary>
        public static AmbConditionClass OutOfResources { get; } = new(
            BrowseNames.OutOfResourcesConditionClassType,
            ObjectTypes.OutOfResourcesConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>
        /// The asset ran out of memory (§9.5.10), a refinement of
        /// <see cref="OutOfResources"/>.
        /// </summary>
        public static AmbConditionClass OutOfMemory { get; } = new(
            BrowseNames.OutOfMemoryConditionClassType,
            ObjectTypes.OutOfMemoryConditionClassType,
            AmbConditionClassBase.System);

        /// <summary>An inspection of the asset (§12.4.2).</summary>
        public static AmbConditionClass Inspection { get; } = new(
            BrowseNames.InspectionConditionClassType,
            ObjectTypes.InspectionConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>A check of the asset by an external party (§12.4.3).</summary>
        public static AmbConditionClass ExternalCheck { get; } = new(
            BrowseNames.ExternalCheckConditionClassType,
            ObjectTypes.ExternalCheckConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>A servicing of the asset (§12.4.4).</summary>
        public static AmbConditionClass Servicing { get; } = new(
            BrowseNames.ServicingConditionClassType,
            ObjectTypes.ServicingConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>A repair of the asset (§12.4.5).</summary>
        public static AmbConditionClass Repair { get; } = new(
            BrowseNames.RepairConditionClassType,
            ObjectTypes.RepairConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>An improvement of the asset (§12.4.6).</summary>
        public static AmbConditionClass Improvement { get; } = new(
            BrowseNames.ImprovementConditionClassType,
            ObjectTypes.ImprovementConditionClassType,
            AmbConditionClassBase.Maintenance);

        /// <summary>
        /// Gets every AMB condition class, health status classes (§9.5)
        /// first, then the maintenance classes (§12.4).
        /// </summary>
        public static ArrayOf<AmbConditionClass> All { get; } =
        [
            ConnectionFailure,
            OverTemperature,
            CalibrationDue,
            SelfTestFailure,
            FlashUpdateInProgress,
            FlashUpdateFailed,
            BadConfiguration,
            OutOfResources,
            OutOfMemory,
            Inspection,
            ExternalCheck,
            Servicing,
            Repair,
            Improvement
        ];

        /// <summary>
        /// Gets the browse name of the class type, which is also the
        /// <c>ConditionClassName</c> a condition of this class reports.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the numeric id of the class type in the AMB namespace.
        /// </summary>
        public uint ObjectTypeId { get; }

        /// <summary>
        /// Gets the standard condition class this class refines.
        /// </summary>
        public AmbConditionClassBase BaseClass { get; }

        /// <summary>
        /// Gets the NodeId of the standard condition class this class
        /// refines.
        /// </summary>
        public NodeId BaseClassTypeId => BaseClass == AmbConditionClassBase.Maintenance
            ? Ua.ObjectTypeIds.MaintenanceConditionClassType
            : Ua.ObjectTypeIds.SystemConditionClassType;

        /// <summary>
        /// Gets the class type id as an <see cref="ExpandedNodeId"/> that
        /// names the AMB namespace by URI.
        /// </summary>
        public ExpandedNodeId ExpandedTypeId => new(ObjectTypeId, Namespaces.AMB);

        /// <summary>
        /// Resolves the class type id against a namespace table.
        /// </summary>
        /// <param name="namespaceUris">The namespace table of the server or session.</param>
        /// <returns>
        /// The type id, or <see cref="NodeId.Null"/> when the AMB namespace is
        /// not in <paramref name="namespaceUris"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUris"/> is <c>null</c>.
        /// </exception>
        public NodeId GetTypeId(NamespaceTable namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            int index = namespaceUris.GetIndex(Namespaces.AMB);
            return index < 0 ? NodeId.Null : new NodeId(ObjectTypeId, (ushort)index);
        }

        /// <summary>
        /// Finds the AMB condition class of a numeric type id in the AMB
        /// namespace.
        /// </summary>
        /// <param name="objectTypeId">The numeric id.</param>
        /// <returns>
        /// The class, or <see langword="null"/> when the id is not one of the
        /// AMB condition classes.
        /// </returns>
        public static AmbConditionClass? Find(uint objectTypeId)
        {
            foreach (AmbConditionClass conditionClass in All)
            {
                if (conditionClass.ObjectTypeId == objectTypeId)
                {
                    return conditionClass;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the AMB condition class a type id resolves to.
        /// </summary>
        /// <param name="typeId">The type id, for example a <c>ConditionClassId</c>.</param>
        /// <param name="namespaceUris">The namespace table <paramref name="typeId"/> is relative to.</param>
        /// <returns>
        /// The class, or <see langword="null"/> when the id is not an AMB
        /// condition class.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUris"/> is <c>null</c>.
        /// </exception>
        public static AmbConditionClass? Find(NodeId typeId, NamespaceTable namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            if (typeId.IsNull ||
                namespaceUris.GetString(typeId.NamespaceIndex) != Namespaces.AMB ||
                !typeId.TryGetValue(out uint identifier))
            {
                return null;
            }
            return Find(identifier);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }
    }
}
