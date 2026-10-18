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
using System.Collections.Generic;
using Opc.Ua.Machinery;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Static identity and supported capabilities of one physical drying device.
    /// Values and control responses come from its injected provider, not these options.
    /// </summary>
    public sealed record GranulateDryingDeviceOptions
    {
        /// <summary>
        /// Gets the application-owned browse name.
        /// </summary>
        public string BrowseName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the manufacturer's name.
        /// </summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>
        /// Gets the model designation.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Gets the manufacturer's serial number.
        /// </summary>
        public string SerialNumber { get; init; } = string.Empty;

        /// <summary>
        /// Gets the stable globally unique product-instance URI used by asset discovery.
        /// </summary>
        public string ProductInstanceUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets optional generated nameplate configuration, applied before required GDD identity is enforced.
        /// Use generated CreateOrReplace accessors for optional identification fields.
        /// </summary>
        public Action<MachineIdentificationState, ISystemContext>? ConfigureIdentification { get; init; }

        /// <summary>
        /// Gets the optional device features.
        /// </summary>
        public GranulateDryingDeviceFeatures Features { get; init; }

        /// <summary>
        /// Gets the bins in consecutive standard browse-name order, starting at Bin_001.
        /// At least one bin is required.
        /// </summary>
        public ArrayOf<GranulateDryingBinOptions> Bins { get; init; } = [new GranulateDryingBinOptions()];

        /// <summary>
        /// Gets the optional beds, starting at DesiccantBed_001.
        /// An empty collection omits the DesiccantBeds container.
        /// </summary>
        public ArrayOf<GranulateDryingBedOptions> DesiccantBeds { get; init; } = [];

        /// <summary>
        /// Gets maintenance categories, starting at Maintenance_001.
        /// An empty collection omits MaintenanceInformation.
        /// </summary>
        public ArrayOf<GranulateDryingMaintenanceOptions> Maintenance { get; init; } = [];

        /// <summary>
        /// Gets additional standard optional member paths to instantiate on the device.
        /// Paths are resolved only against compiled declarations; unknown names are rejected.
        /// This also selects inherited OPC 40083 parameter, energy, user, and identification members.
        /// </summary>
        public ArrayOf<string> OptionalMembers { get; init; } = [];

        /// <summary>
        /// Gets device-supplied analog engineering units and ranges.
        /// No unit conversion is performed; committed values must already use the declared unit.
        /// Defaults describe SI units and the finite Double domain, not calibrated hardware limits.
        /// </summary>
        public ArrayOf<GranulateDryingAnalogMetadata> AnalogMetadata { get; init; } = [];

        /// <summary>
        /// Gets the maximum number of simultaneously retained error conditions.
        /// </summary>
        public int MaximumErrors { get; init; } = 256;

        /// <summary>
        /// Gets the maximum byte length of one maintenance-instructions document.
        /// </summary>
        public int MaximumInstructionBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Validates identity, instance counts, feature bits, and maintenance identifiers.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BrowseName) ||
                string.IsNullOrWhiteSpace(Manufacturer.Text) ||
                string.IsNullOrWhiteSpace(Model.Text) ||
                string.IsNullOrWhiteSpace(SerialNumber))
            {
                throw new ArgumentException("BrowseName, Manufacturer, Model and SerialNumber are required.");
            }
            if (!Uri.TryCreate(ProductInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("ProductInstanceUri must be an absolute URI.");
            }
            if ((Features & ~GranulateDryingDeviceFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Features));
            }
            if (Bins.IsNull || Bins.Count < 1 || Maintenance.Count > 999)
            {
                throw new ArgumentException("A device needs at least one bin and at most 999 maintenance categories.");
            }
            if (MaximumErrors <= 0 || MaximumInstructionBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumErrors));
            }
            foreach (GranulateDryingBinOptions bin in Bins)
            {
                if (bin == null || (bin.Features & ~GranulateDryingBinFeatures.All) != 0)
                {
                    throw new ArgumentException("Every bin must specify valid feature bits.");
                }
            }
            foreach (GranulateDryingBedOptions bed in DesiccantBeds)
            {
                if (bed == null || bed.Features == GranulateDryingBedFeatures.None ||
                    (bed.Features & ~GranulateDryingBedFeatures.All) != 0)
                {
                    throw new ArgumentException("Every selected desiccant bed needs at least one valid capability.");
                }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (GranulateDryingMaintenanceOptions maintenance in Maintenance)
            {
                if (maintenance == null || string.IsNullOrWhiteSpace(maintenance.Id) ||
                    string.IsNullOrWhiteSpace(maintenance.DisplayName.Text) ||
                    !ids.Add(maintenance.Id))
                {
                    throw new ArgumentException("Maintenance categories need unique identifiers and display names.");
                }
                if (maintenance.Instructions.Span.Length > MaximumInstructionBytes)
                {
                    throw new ArgumentException("Maintenance instructions exceed the configured byte limit.");
                }
                if (maintenance.WarningSeverity is < 1 or > 1000 || maintenance.DueSeverity is < 1 or > 1000)
                {
                    throw new ArgumentException("Maintenance alarm severities must be in the range 1–1000.");
                }
            }
            foreach (GranulateDryingAnalogMetadata metadata in AnalogMetadata)
            {
                if (metadata == null || metadata.EngineeringUnits == null ||
                    string.IsNullOrWhiteSpace(metadata.EngineeringUnits.NamespaceUri) ||
                    metadata.EngineeringRange == null ||
                    double.IsNaN(metadata.EngineeringRange.Low) ||
                    double.IsNaN(metadata.EngineeringRange.High) ||
                    double.IsInfinity(metadata.EngineeringRange.Low) ||
                    double.IsInfinity(metadata.EngineeringRange.High) ||
                    metadata.EngineeringRange.Low > metadata.EngineeringRange.High)
                {
                    throw new ArgumentException("Analog metadata needs explicit units and an ordered finite range.");
                }
            }
        }
    }

    /// <summary>
    /// Selects optional members on one mandatory bin instance.
    /// </summary>
    public sealed record GranulateDryingBinOptions
    {
        /// <summary>
        /// Gets the selected bin capabilities.
        /// </summary>
        public GranulateDryingBinFeatures Features { get; init; }

        /// <summary>
        /// Gets additional compiled standard member paths relative to this bin.
        /// </summary>
        public ArrayOf<string> OptionalMembers { get; init; } = [];
    }

    /// <summary>
    /// Selects optional members on one desiccant-bed instance.
    /// </summary>
    public sealed record GranulateDryingBedOptions
    {
        /// <summary>
        /// Gets the selected bed capabilities.
        /// </summary>
        public GranulateDryingBedFeatures Features { get; init; } =
            GranulateDryingBedFeatures.TemperatureOfDesiccantBed;

        /// <summary>
        /// Gets additional compiled standard member paths relative to this bed.
        /// </summary>
        public ArrayOf<string> OptionalMembers { get; init; } = [];
    }

    /// <summary>
    /// Describes a maintenance category without inventing a schedule or completion.
    /// </summary>
    public sealed record GranulateDryingMaintenanceOptions
    {
        /// <summary>
        /// Gets the stable category identifier.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Gets the category's human-readable name.
        /// </summary>
        public LocalizedText DisplayName { get; init; }

        /// <summary>
        /// Gets the category's description.
        /// </summary>
        public LocalizedText Description { get; init; }

        /// <summary>
        /// Gets optional instructions served by the stack's read-only FileType binding.
        /// A null ByteString omits the file; an empty ByteString publishes an empty file.
        /// </summary>
        public ByteString Instructions { get; init; }

        /// <summary>
        /// Gets the configurable severity used when this maintenance activity is in WARNING.
        /// </summary>
        public ushort WarningSeverity { get; init; } = 400;

        /// <summary>
        /// Gets the configurable severity used when this maintenance activity is DUE.
        /// </summary>
        public ushort DueSeverity { get; init; } = 800;

        /// <summary>
        /// Gets optional inherited maintenance member paths, including RemainingInterval.
        /// </summary>
        public ArrayOf<string> OptionalMembers { get; init; } = [];
    }

    /// <summary>
    /// Exact equipment metadata for one selected standard analog variable.
    /// </summary>
    /// <param name="Target">The standard object containing the analog variable.</param>
    /// <param name="Member">The relative compiled variable path, such as Temperature/ActualValue.</param>
    /// <param name="EngineeringUnits">The unit already used by provider values.</param>
    /// <param name="EngineeringRange">The equipment's engineering range.</param>
    public sealed record GranulateDryingAnalogMetadata(
        GranulateDryingTarget Target,
        string Member,
        EUInformation EngineeringUnits,
        Range EngineeringRange);
}
