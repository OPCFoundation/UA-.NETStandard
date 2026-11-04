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
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;

namespace AmbSample
{
    /// <summary>
    /// The assets of the sample plant: a cooling pump and the flow sensor it
    /// relies on, both Device Integration devices, and a hydraulic press
    /// built by <see cref="PressConfigurator"/>.
    /// </summary>
    internal sealed class Plant
    {
        /// <summary>The hierarchical location of the assets.</summary>
        public const string Line = "Plant1/Hall3/Line2";

        /// <summary>The operational location of the cooling circuit.</summary>
        public const string Cooling = "Utilities/CoolingCircuit";

        /// <summary>The IRDI of an ECLASS class of centrifugal pumps, for the classification.</summary>
        public const string CentrifugalPumpIrdi = "0173-1#01-AKE798#019";

        /// <summary>
        /// The dictionary entry of the class (OPC 10000-19), which the AMB node
        /// manager defines below Server/Dictionaries.
        /// </summary>
        public static readonly ExpandedNodeId CentrifugalPump = new(
            CentrifugalPumpIrdi,
            AmbServerOptions.IrdiNamespaceUri);

        /// <summary>Gets the cooling pump, once registered.</summary>
        public IAssetHandle? Pump { get; private set; }

        /// <summary>Gets the flow sensor, once registered.</summary>
        public IAssetHandle? Sensor { get; private set; }

        /// <summary>Gets the press, once registered.</summary>
        public IAssetHandle? Press { get; set; }

        /// <summary>
        /// Creates the devices and registers them as assets. Runs while the
        /// Machinery node manager builds its address space, which is when
        /// health alarms and maintenance activities can be created.
        /// </summary>
        public async ValueTask ConfigureDevicesAsync(IDiPostSetupContext context)
        {
            IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
            CancellationToken ct = context.CancellationToken;
            ushort ns = context.Manager.InstanceNamespaceIndex;

            IDeviceBuilder<DeviceState> sensor = await context
                .CreateDeviceAsync(new QualifiedName("FlowSensor", ns))
                .ConfigureAwait(false);
            sensor.WithIdentification(identification =>
            {
                identification.Manufacturer = new LocalizedText("en", "Acme Sensors");
                identification.Model = new LocalizedText("en", "FlowMag 20");
                identification.SerialNumber = "FM20-1187";
                identification.ProductInstanceUri = "urn:acme-sensors:flowmag20:FM20-1187";
                identification.HardwareRevision = "C";
                identification.SoftwareRevision = "1.8.0";
                identification.RevisionCounter = 3;
            });
            Sensor = await sensor.RegisterAsAssetAsync(
                assets,
                asset => asset
                    .WithConfigurableAssetId()
                    .WithDeviceHealth()
                    .WithHealthAlarm("SelfTest", AssetHealthAlarmKind.CheckFunction, AmbConditionClass.SelfTestFailure)
                    .WithMaintenance(
                        "Calibration",
                        AmbConditionClass.CalibrationDue,
                        details =>
                        {
                            details.Description = new LocalizedText("en", "Calibrate the flow sensor.");
                            details.PlannedDate = DateTime.UtcNow.AddDays(30);
                            details.EstimatedDowntime = TimeSpan.FromMinutes(30);
                        })
                    .LocatedIn(AssetLocationKind.Operational, Cooling)
                    .WithLocation(AssetLocationKind.Operational, Cooling),
                ct).ConfigureAwait(false);

            IDeviceBuilder<DeviceState> pump = await context
                .CreateDeviceAsync(new QualifiedName("CoolingPump", ns))
                .ConfigureAwait(false);
            pump.WithIdentification(identification =>
            {
                identification.Manufacturer = new LocalizedText("en", "Acme Pumps");
                identification.Model = new LocalizedText("en", "CW-80");
                identification.SerialNumber = "CW80-0042";
                identification.ProductInstanceUri = "urn:acme-pumps:cw80:CW80-0042";
            });
            Pump = await pump.RegisterAsAssetAsync(
                assets,
                asset => asset
                    .WithConfigurableAssetId("CP-01")
                    .WithDeviceHealth(deriveFromAlarms: true)
                    .WithHealthAlarm(
                        "BearingTemperature",
                        AssetHealthAlarmKind.OffSpec,
                        AmbConditionClass.OverTemperature)
                    .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure)
                    .WithMaintenance(
                        "AnnualInspection",
                        AmbConditionClass.Inspection,
                        details =>
                        {
                            details.Description = new LocalizedText("en", "Annual inspection of the cooling pump.");
                            details.PlannedDate = DateTime.UtcNow.AddMinutes(2);
                            details.EstimatedDowntime = TimeSpan.FromHours(2);
                            details.MaintenanceSupplier = new NameNodeIdDataType
                            {
                                Name = new LocalizedText("en", "Acme Service"),
                                NodeId = NodeId.Null
                            };
                            details.MaintenanceMethod = MaintenanceMethodEnum.Local;
                        })
                    .WithDocumentationLinks(links => links
                        .Add("OperatingManual", "https://acme-pumps.example/cw80/manual.pdf")
                        .Add("SpareParts", "https://acme-pumps.example/cw80/spares")
                        .AddEditable("OperatorHandbook")
                        .AllowUserLinks())
                    .WithVersionInformation("B", "2.4.1", 1)
                    .LocatedIn(AssetLocationKind.Hierarchical, Line)
                    .LocatedIn(AssetLocationKind.Operational, Cooling)
                    .WithLocation(AssetLocationKind.Hierarchical, Line, writable: true)
                    .WithLocalTime(60, writable: true)
                    .ClassifiedAs(CentrifugalPump)
                    .WithRequirements(requirements => requirements
                        .Add("SupplyVoltage", Variant.From(400.0))
                        .Add("CoolingWaterTemperature", Variant.From(25.0)))
                    .WithCapabilities(capabilities => capabilities
                        .Add("RatedFlow", Variant.From(25.0))
                        .Add("Medium", Variant.From("Water")))
                    .RelatesTo(Opc.Ua.ReferenceTypeIds.Utilizes, Sensor.NodeId),
                ct).ConfigureAwait(false);
        }
    }
}
