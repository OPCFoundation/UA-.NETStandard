// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;
using Opc.Ua.LADS.Server;

namespace LadsSample.Server
{
    /// <summary>
    /// Describes the shaking incubator with the LADS fluent builder.
    /// </summary>
    internal static class ShakingIncubator
    {
        public const string DeviceName = "Incubator";
        public const string UnitName = "Chamber";

        /// <summary>
        /// The ambient temperature of the simulated chamber in degrees Celsius.
        /// </summary>
        public const double AmbientTemperature = 22;

        /// <summary>
        /// Adds the incubator device with one functional unit and two incubation programs.
        /// </summary>
        public static void Configure(ILadsSetupContext context)
        {
            context.AddDevice(DeviceName, device => device
                .Identification(id =>
                {
                    id.Manufacturer = new LocalizedText("OPC Foundation");
                    id.Model = new LocalizedText("Shaking incubator");
                    id.SerialNumber = "SN-0001";
                    id.ProductInstanceUri = "urn:opcfoundation.org:LadsServer:Incubator:SN-0001";
                    id.SoftwareRevision = "1.0.0";
                })
                .AssetManagement(asset => asset.WithConfigurableAssetId("Incubator-1").WithDeviceHealth())
                .Locking()
                .Location("Lab 1", "Site/Building A/Lab 1")
                .MaintenanceTask("Calibration", task =>
                {
                    task.Message = new LocalizedText("Calibrate the CO2 sensor.");
                    task.RecurrencePeriod = TimeSpan.FromDays(30);
                })
                .FunctionalUnit(UnitName, unit => unit
                    .RunningOperations()
                    .AnalogControl("Temperature", options =>
                    {
                        options.EngineeringUnits = LadsUnits.Celsius;
                        options.Range = new Opc.Ua.Range(60, 4);
                        options.InitialTargetValue = AmbientTemperature;
                        options.InitialCurrentValue = AmbientTemperature;
                        options.Pid = new LadsPidOptions { CtrlP = 1.5, CtrlTi = 10, CtrlTd = 0 };
                    })
                    .AnalogControl("ShakerSpeed", options =>
                    {
                        options.EngineeringUnits = LadsUnits.RevolutionsPerMinute;
                        options.Range = new Opc.Ua.Range(1000, 0);
                    })
                    .AnalogScalarSensor("CO2", options =>
                    {
                        options.EngineeringUnits = LadsUnits.Percent;
                        options.Range = new Opc.Ua.Range(20, 0);
                        options.InitialValue = 0.04;
                    })
                    .Cover("Door")
                    .Timer("Timer")
                    .SupportedProperty("Volume")
                    .Programs(LadsTemplateOperations.All, programs => programs
                        .Template("Incubation", template => template
                            .WithDescription(new LocalizedText("Warm up, shake and cool down a culture."))
                            .Step("WarmUp", Seconds(5), Setpoint("Temperature", 37), Setpoint("ShakerSpeed", 150))
                            .Step("Shake", Seconds(5), Setpoint("ShakerSpeed", 250))
                            .Step("CoolDown", Seconds(3), Setpoint("Temperature", 25), Setpoint("ShakerSpeed", 0)))
                        .Template("Overnight", template => template
                            .WithDescription(new LocalizedText("Shake at 37 degrees for twelve hours."))
                            .Step(
                                "Incubate",
                                Seconds(43200),
                                Setpoint("Temperature", 37),
                                Setpoint("ShakerSpeed", 200))))));
        }

        private static TimeSpan Seconds(int seconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        private static LadsSetpoint Setpoint(string function, double value)
        {
            return new LadsSetpoint(function, Variant.From(value));
        }
    }
}
