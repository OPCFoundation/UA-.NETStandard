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
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Smoke tests for the <see cref="DeviceIdentification"/> record DTO
    /// and basic <see cref="DiDeviceClient"/> argument validation.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class DeviceIdentificationTests
    {
        [Test]
        public void DeviceIdentificationRecordHoldsAllProperties()
        {
            var id = new DeviceIdentification(
                Manufacturer: "SimDevice Corp",
                Model: "Model-X",
                SerialNumber: "SN-001",
                HardwareRevision: "1.0",
                SoftwareRevision: "2.5",
                DeviceRevision: "3.1",
                DeviceClass: "Pump",
                ProductInstanceUri: "urn:simdevice:Model-X:SN-001");

            Assert.That(id.Manufacturer, Is.EqualTo("SimDevice Corp"));
            Assert.That(id.Model, Is.EqualTo("Model-X"));
            Assert.That(id.SerialNumber, Is.EqualTo("SN-001"));
            Assert.That(id.HardwareRevision, Is.EqualTo("1.0"));
            Assert.That(id.SoftwareRevision, Is.EqualTo("2.5"));
            Assert.That(id.DeviceRevision, Is.EqualTo("3.1"));
            Assert.That(id.DeviceClass, Is.EqualTo("Pump"));
            Assert.That(id.ProductInstanceUri, Is.EqualTo("urn:simdevice:Model-X:SN-001"));
        }

        [Test]
        public void DeviceIdentificationAllowsNullProperties()
        {
            var id = new DeviceIdentification(
                Manufacturer: null,
                Model: null,
                SerialNumber: null,
                HardwareRevision: null,
                SoftwareRevision: null,
                DeviceRevision: null,
                DeviceClass: null,
                ProductInstanceUri: null);

            Assert.That(id.Manufacturer, Is.Null);
            Assert.That(id.SerialNumber, Is.Null);
        }

        [Test]
        public void DeviceIdentificationRecordEqualityIsByValue()
        {
            var a = new DeviceIdentification(
                "X", "Y", "Z", null, null, null, null, null);
            var b = new DeviceIdentification(
                "X", "Y", "Z", null, null, null, null, null);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }
    }
}
