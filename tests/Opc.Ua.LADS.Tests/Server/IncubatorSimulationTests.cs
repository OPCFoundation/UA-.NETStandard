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

using System.Threading.Tasks;
using LadsSample.Server;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the shaking incubator sample with its hosted LADS functions.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class IncubatorSimulationTests
    {
        /// <summary>
        /// Stopped and aborted controls return to rest and resume their retained targets when restarted.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ControlsReturnToRestAfterStopOrAbortAsync(bool abortTemperature)
        {
            await using LadsHostedServer server = await StartSampleAsync(
                nameof(ControlsReturnToRestAfterStopOrAbortAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit chamber = device.GetFunctionalUnit(ShakingIncubator.UnitName);
            IAnalogControlFunction temperature = chamber.GetFunction<IAnalogControlFunction>("Temperature");
            IAnalogControlFunction shaker = chamber.GetFunction<IAnalogControlFunction>("ShakerSpeed");
            IAnalogScalarSensorFunction co2 = chamber.GetFunction<IAnalogScalarSensorFunction>("CO2");
            ITimerFunction timer = chamber.GetFunction<ITimerFunction>("Timer");

            await temperature.StartWithTargetValueAsync(37).ConfigureAwait(false);
            await shaker.StartWithTargetValueAsync(400).ConfigureAwait(false);
            string? runId = IncubatorSimulation.Advance(temperature, shaker, co2, timer, "run-1", null);
            runId = IncubatorSimulation.Advance(temperature, shaker, co2, timer, "run-1", runId);
            Assert.That(temperature.CurrentValue, Is.EqualTo(26));
            Assert.That(shaker.CurrentValue, Is.EqualTo(100));
            Assert.That(timer.CurrentValue, Is.EqualTo(500));

            if (abortTemperature)
            {
                await temperature.AbortAsync().ConfigureAwait(false);
                await shaker.StopAsync().ConfigureAwait(false);
            }
            else
            {
                await temperature.StopAsync().ConfigureAwait(false);
                await shaker.AbortAsync().ConfigureAwait(false);
            }
            runId = IncubatorSimulation.Advance(temperature, shaker, co2, timer, "run-1", runId);
            Assert.That(temperature.CurrentValue, Is.EqualTo(24));
            Assert.That(shaker.CurrentValue, Is.EqualTo(50));
            Assert.That(timer.CurrentValue, Is.EqualTo(1000));
            Assert.That(temperature.TargetValue, Is.EqualTo(37));
            Assert.That(shaker.TargetValue, Is.EqualTo(400));

            runId = IncubatorSimulation.Advance(temperature, shaker, co2, timer, "run-1", runId);
            Assert.That(temperature.CurrentValue, Is.EqualTo(22));
            Assert.That(shaker.CurrentValue, Is.Zero);
            Assert.That(timer.CurrentValue, Is.EqualTo(1500));

            if (abortTemperature)
            {
                await temperature.ClearAsync().ConfigureAwait(false);
            }
            else
            {
                await shaker.ClearAsync().ConfigureAwait(false);
            }
            await temperature.StartAsync().ConfigureAwait(false);
            await shaker.StartAsync().ConfigureAwait(false);
            _ = IncubatorSimulation.Advance(temperature, shaker, co2, timer, "run-1", runId);
            Assert.That(temperature.CurrentValue, Is.EqualTo(24));
            Assert.That(shaker.CurrentValue, Is.EqualTo(50));
        }

        /// <summary>
        /// The sample timer publishes the Duration data type with millisecond units.
        /// </summary>
        [Test]
        public async Task TimerPublishesDurationAndMillisecondUnitsAsync()
        {
            await using LadsHostedServer server = await StartSampleAsync(
                nameof(TimerPublishesDurationAndMillisecondUnitsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ITimerFunction timer = device.GetFunctionalUnit(ShakingIncubator.UnitName)
                .GetFunction<ITimerFunction>("Timer");
            NodeId current = await server.ChildAsync(timer.NodeId, "CurrentValue").ConfigureAwait(false);
            NodeId engineeringUnits = await server.ChildAsync(current, "EngineeringUnits").ConfigureAwait(false);

            DataValue dataType = await server.ReadAsync(current, Attributes.DataType).ConfigureAwait(false);
            DataValue units = await server.ReadAsync(engineeringUnits).ConfigureAwait(false);

            Assert.That(dataType.WrappedValue.TryGetValue(out NodeId actualType), Is.True);
            Assert.That(actualType, Is.EqualTo(global::Opc.Ua.DataTypeIds.Duration));
            Assert.That(units.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out EUInformation? information), Is.True);
            Assert.That(information?.UnitId, Is.EqualTo(LadsUnits.Millisecond.UnitId));
        }

        /// <summary>
        /// Starts the sample configuration with its hardware provider.
        /// </summary>
        private static Task<LadsHostedServer> StartSampleAsync(string name)
        {
            return LadsHostedServer.StartAsync(name, builder =>
            {
                builder.Services.AddSingleton<ILadsFunctionProvider, IncubatorHardware>();
                builder.AddLads().ConfigureLads(ShakingIncubator.Configure);
            });
        }
    }
}
