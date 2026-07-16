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
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="DeviceIdentificationData"/> mutable
    /// property bag used by the DI device builder to populate nameplate
    /// properties.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceIdentificationDataTests
    {
        [Test]
        public void DefaultInstanceHasNullLocalizedTextsAndNullStrings()
        {
            var data = new DeviceIdentificationData();

            Assert.That(data.Manufacturer.IsNull, Is.True);
            Assert.That(data.Model.IsNull, Is.True);
            Assert.That(data.ManufacturerUri, Is.Null);
            Assert.That(data.HardwareRevision, Is.Null);
            Assert.That(data.SoftwareRevision, Is.Null);
            Assert.That(data.DeviceRevision, Is.Null);
            Assert.That(data.ProductCode, Is.Null);
            Assert.That(data.DeviceManual, Is.Null);
            Assert.That(data.DeviceClass, Is.Null);
            Assert.That(data.SerialNumber, Is.Null);
            Assert.That(data.ProductInstanceUri, Is.Null);
            Assert.That(data.RevisionCounter, Is.Null);
        }

        [Test]
        public void SettingManufacturerMakesLocalizedTextNonNull()
        {
            var data = new DeviceIdentificationData
            {
                Manufacturer = new LocalizedText("Acme")
            };

            Assert.That(data.Manufacturer.IsNull, Is.False);
            Assert.That(data.Manufacturer.Text, Is.EqualTo("Acme"));
        }

        [Test]
        public void SettingModelMakesLocalizedTextNonNull()
        {
            var data = new DeviceIdentificationData
            {
                Model = new LocalizedText("ModelX")
            };

            Assert.That(data.Model.IsNull, Is.False);
            Assert.That(data.Model.Text, Is.EqualTo("ModelX"));
        }

        [Test]
        public void AllStringPropertiesRoundTrip()
        {
            var data = new DeviceIdentificationData
            {
                ManufacturerUri = "urn:acme",
                HardwareRevision = "HW-1",
                SoftwareRevision = "SW-2",
                DeviceRevision = "DR-3",
                ProductCode = "PC-4",
                DeviceManual = "https://example.com/manual",
                DeviceClass = "Pump",
                SerialNumber = "SN-42",
                ProductInstanceUri = "urn:acme:instance:1"
            };

            Assert.That(data.ManufacturerUri, Is.EqualTo("urn:acme"));
            Assert.That(data.HardwareRevision, Is.EqualTo("HW-1"));
            Assert.That(data.SoftwareRevision, Is.EqualTo("SW-2"));
            Assert.That(data.DeviceRevision, Is.EqualTo("DR-3"));
            Assert.That(data.ProductCode, Is.EqualTo("PC-4"));
            Assert.That(data.DeviceManual, Is.EqualTo("https://example.com/manual"));
            Assert.That(data.DeviceClass, Is.EqualTo("Pump"));
            Assert.That(data.SerialNumber, Is.EqualTo("SN-42"));
            Assert.That(data.ProductInstanceUri, Is.EqualTo("urn:acme:instance:1"));
        }

        [Test]
        public void RevisionCounterRoundTrips()
        {
            var data = new DeviceIdentificationData { RevisionCounter = 7 };

            Assert.That(data.RevisionCounter, Is.Not.Null);
            Assert.That(data.RevisionCounter!.Value, Is.EqualTo(7));
        }

        [Test]
        public void AllTwelvePropertiesRoundTripInOneObject()
        {
            var data = new DeviceIdentificationData
            {
                Manufacturer = new LocalizedText("en", "Acme"),
                ManufacturerUri = "urn:acme",
                Model = new LocalizedText("en", "Model-1"),
                HardwareRevision = "HW",
                SoftwareRevision = "SW",
                DeviceRevision = "DR",
                ProductCode = "PC",
                DeviceManual = "manual",
                DeviceClass = "Pump",
                SerialNumber = "SN",
                ProductInstanceUri = "urn:instance",
                RevisionCounter = 42
            };

            Assert.That(data.Manufacturer.Text, Is.EqualTo("Acme"));
            Assert.That(data.ManufacturerUri, Is.EqualTo("urn:acme"));
            Assert.That(data.Model.Text, Is.EqualTo("Model-1"));
            Assert.That(data.HardwareRevision, Is.EqualTo("HW"));
            Assert.That(data.SoftwareRevision, Is.EqualTo("SW"));
            Assert.That(data.DeviceRevision, Is.EqualTo("DR"));
            Assert.That(data.ProductCode, Is.EqualTo("PC"));
            Assert.That(data.DeviceManual, Is.EqualTo("manual"));
            Assert.That(data.DeviceClass, Is.EqualTo("Pump"));
            Assert.That(data.SerialNumber, Is.EqualTo("SN"));
            Assert.That(data.ProductInstanceUri, Is.EqualTo("urn:instance"));
            Assert.That(data.RevisionCounter, Is.EqualTo(42));
        }
    }
}
