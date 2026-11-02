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
                    id.SoftwareRevision = "1.0.0";
                })
                .Location("Lab 1", "Site/Building A/Lab 1")
                .MaintenanceTask("Calibration", task =>
                {
                    task.Message = new LocalizedText("Calibrate the CO2 sensor.");
                    task.RecurrencePeriod = TimeSpan.FromDays(30);
                })
                .FunctionalUnit(UnitName, unit => unit
                    .AnalogControl("Temperature", options =>
                    {
                        options.EngineeringUnits = LadsUnits.Celsius;
                        options.Range = new Opc.Ua.Range(60, 4);
                        options.InitialTargetValue = AmbientTemperature;
                        options.InitialCurrentValue = AmbientTemperature;
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
                    .Programs(programs => programs
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
