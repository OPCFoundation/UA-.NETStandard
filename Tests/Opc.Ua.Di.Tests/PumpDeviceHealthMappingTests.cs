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

using NUnit.Framework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Pure-function tests for the NAMUR NE 107 supervision-to-health
    /// mapping exposed by <see cref="PumpNodeManager.MapSupervisionToDeviceHealth"/>.
    /// FAILURE outranks MAINTENANCE_REQUIRED outranks NORMAL.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpDeviceHealthMappingTests
    {
        [Test]
        public void BothFlagsClearMapsToNormal()
        {
            DeviceHealthEnumeration result =
                PumpNodeManager.MapSupervisionToDeviceHealth(
                    cavitation: false, motorOverheat: false);
            Assert.That(result, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
        }

        [Test]
        public void CavitationOnlyMapsToMaintenanceRequired()
        {
            DeviceHealthEnumeration result =
                PumpNodeManager.MapSupervisionToDeviceHealth(
                    cavitation: true, motorOverheat: false);
            Assert.That(
                result, Is.EqualTo(DeviceHealthEnumeration.MAINTENANCE_REQUIRED));
        }

        [Test]
        public void MotorOverheatOnlyMapsToFailure()
        {
            DeviceHealthEnumeration result =
                PumpNodeManager.MapSupervisionToDeviceHealth(
                    cavitation: false, motorOverheat: true);
            Assert.That(result, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
        }

        [Test]
        public void BothFlagsSetFailureWinsOverCavitation()
        {
            // Severity ordering per NAMUR NE 107: a motor failure
            // dominates a cavitation maintenance-required event.
            DeviceHealthEnumeration result =
                PumpNodeManager.MapSupervisionToDeviceHealth(
                    cavitation: true, motorOverheat: true);
            Assert.That(result, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
        }
    }
}
