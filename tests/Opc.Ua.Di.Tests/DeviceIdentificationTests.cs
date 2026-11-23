// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
