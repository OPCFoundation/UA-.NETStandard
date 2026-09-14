/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using Alarms;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the alarm simulation that drives the Quickstarts alarm node manager
    /// in CTT mode.
    /// </summary>
    [TestFixture]
    [Category("Alarms")]
    [Parallelizable]
    public class AlarmControllerTests
    {
        [Test]
        public void BooleanAndAnalogSourcesChangeStateOnTheSameStep()
        {
            // CTT A and C Enable Test_003 needs an active event from every alarm type
            // within a tenth of the Alarm Cycle Time. That only holds deterministically
            // when the boolean and analog sources cross their limits on the same step.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var analog = new SteppableAlarmController(new BaseDataVariableState(null), isBoolean: false, telemetry);
            var boolean = new SteppableAlarmController(new BaseDataVariableState(null), isBoolean: true, telemetry);
            analog.Start(uint.MaxValue);
            boolean.Start(uint.MaxValue);

            var bands = new HashSet<string>();
            for (int step = 0; step < 80; step++)
            {
                (int analogValue, bool analogActive) = analog.Step();
                (int booleanValue, bool booleanActive) = boolean.Step();

                Assert.That(booleanValue, Is.EqualTo(analogValue), $"step {step}");
                bool limitActive = analogValue >= AlarmDefines.HIGH_ALARM ||
                    analogValue <= AlarmDefines.LOW_ALARM;
                Assert.That(booleanActive, Is.EqualTo(limitActive), $"step {step} value {booleanValue}");
                Assert.That(analogActive, Is.EqualTo(limitActive), $"step {step} value {analogValue}");

                bands.Add(analogValue switch
                {
                    >= AlarmDefines.HIGHHIGH_ALARM => "HighHigh",
                    >= AlarmDefines.HIGH_ALARM => "High",
                    <= AlarmDefines.LOWLOW_ALARM => "LowLow",
                    <= AlarmDefines.LOW_ALARM => "Low",
                    _ => "Normal"
                });
            }

            Assert.That(bands, Is.EquivalentTo(new[] { "HighHigh", "High", "Normal", "Low", "LowLow" }),
                "One simulation period must visit every limit state.");
        }

        private sealed class SteppableAlarmController : AlarmController
        {
            public SteppableAlarmController(
                BaseDataVariableState variable,
                bool isBoolean,
                ITelemetryContext telemetry)
                : base(variable, 1000, isBoolean, telemetry)
            {
            }

            public (int Value, bool Active) Step()
            {
                int value = 0;
                bool active = false;
                GetValue(ref value, ref active);
                return (value, active);
            }
        }
    }
}
