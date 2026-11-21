// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Configures the shaking incubator used by the server tests.
    /// </summary>
    internal static class LadsIncubator
    {
        public const string DeviceName = "Incubator";
        public const string UnitName = "Chamber";
        public const string QuickTemplate = "Quick";
        public const string LongTemplate = "Long";
        public const string MaintenanceName = "Calibration";

        /// <summary>
        /// Adds the incubator device.
        /// </summary>
        public static void Configure(ILadsSetupContext context)
        {
            Configure(context, DeviceName, null);
        }

        /// <summary>
        /// Adds an incubator device with optional extra device configuration.
        /// </summary>
        public static ILadsDevice Configure(
            ILadsSetupContext context,
            string name,
            Action<ILadsDeviceBuilder>? extra)
        {
            return context.AddDevice(name, device =>
            {
                device
                    .Identification(id =>
                    {
                        id.Manufacturer = new LocalizedText("OPC Foundation");
                        id.Model = new LocalizedText("Shaking incubator");
                        id.SerialNumber = "SN-0001";
                        id.SoftwareRevision = "1.0.0";
                    })
                    .Location("Lab 1", "Site/Lab 1")
                    .MaintenanceTask(MaintenanceName, task =>
                    {
                        task.Message = new LocalizedText("Calibrate the CO2 sensor.");
                        task.RecurrencePeriod = TimeSpan.FromDays(30);
                        task.PlannedDate = (DateTimeUtc)new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    })
                    .Component("Shaker", id => id.Manufacturer = new LocalizedText("OPC Foundation"))
                    .FunctionalUnit(UnitName, unit => unit
                        .AnalogControl("Temperature", options =>
                        {
                            options.EngineeringUnits = LadsUnits.Celsius;
                            options.Range = new Range(60, 0);
                            options.InitialTargetValue = 37;
                            options.InitialCurrentValue = 22;
                        })
                        .AnalogControl("ShakerSpeed", options =>
                        {
                            options.EngineeringUnits = LadsUnits.RevolutionsPerMinute;
                            options.Range = new Range(1000, 0);
                        })
                        .AnalogScalarSensor("CO2", options =>
                        {
                            options.EngineeringUnits = LadsUnits.Percent;
                            options.Range = new Range(20, 0);
                            options.InitialValue = 5;
                        })
                        .Cover("Door")
                        .Timer("Timer")
                        .SupportedProperty("Volume")
                        .Programs(programs => programs
                            .Template(QuickTemplate, template => template
                                .WithDescription(new LocalizedText("A quick incubation."))
                                .Step(
                                    "Heat",
                                    TimeSpan.Zero,
                                    new LadsSetpoint("Temperature", Variant.From(37.0)),
                                    new LadsSetpoint("ShakerSpeed", Variant.From(200.0)))
                                .Step("Hold", TimeSpan.FromMilliseconds(10)))
                            .Template(LongTemplate, template => template
                                .Step("Hold", TimeSpan.FromMinutes(10)))));
                extra?.Invoke(device);
            });
        }
    }
}
