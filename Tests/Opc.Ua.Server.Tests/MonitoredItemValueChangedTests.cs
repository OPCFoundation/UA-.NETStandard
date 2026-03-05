using System;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemValueChangedTests
    {
        [Test]
        public void ValueChanged_ValueNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => MonitoredItem.ValueChanged(null, null, null, null, null, 0));
        }

        [Test]
        public void ValueChanged_LastValueNull_ReturnsTrue()
        {
            var value = new DataValue(new Variant(1));
            Assert.That(MonitoredItem.ValueChanged(value, null, null, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_StatusChanged_ReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var value = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Bad };

            // Status different
            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_ErrorOverridesStatus_ReturnsTrueIfChanged()
        {
            var lastValue = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var value = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var error = new ServiceResult(StatusCodes.Bad);

            // Error makes new status Bad, last was Good -> Changed
            Assert.That(MonitoredItem.ValueChanged(value, error, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_SameValue_ReturnsFalse()
        {
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(1));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.False);
        }

        [Test]
        public void ValueChanged_DifferentValue_ReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(2));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_SameReference_ReturnsFalse()
        {
            const string obj = "test";
            var lastValue = new DataValue(new Variant(obj));
            var value = new DataValue(new Variant(obj));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.False);
        }

        [Test]
        public void ValueChanged_TriggerStatusChange_IgnoresValueChange()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.Status };
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(2)); // Value changed

            // Status is Good for both, so no status change.
            // Trigger is Status, so value change should be ignored.
            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.False);
        }

        [Test]
        public void ValueChanged_TriggerStatusValueTimestamp_TimestampChanged()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp };
            DateTime now = DateTime.UtcNow;
            var lastValue = new DataValue(new Variant(1)) { SourceTimestamp = now };
            var value = new DataValue(new Variant(1)) { SourceTimestamp = now.AddMilliseconds(1) };

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.True);
        }

        [Test]
        public void ValueChanged_TriggerStatusValueTimestamp_TimestampSame()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp };
            DateTime now = DateTime.UtcNow;
            var lastValue = new DataValue(new Variant(1)) { SourceTimestamp = now };
            var value = new DataValue(new Variant(1)) { SourceTimestamp = now };

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.False);
        }

        [Test]
        public void ValueChanged_TypeMismatch_ReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1)); // int
            var value = new DataValue(new Variant(1.0)); // double

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Double_NaN_ReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(double.NaN));
            var value = new DataValue(new Variant(double.NaN));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Float_NaN_ReturnsTrue()
        {
            // NaN != NaN, so ValueChanged returns True
            var lastValue = new DataValue(new Variant(float.NaN));
            var value = new DataValue(new Variant(float.NaN));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Double_DeadbandAbsolute_Inside()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 };
            var lastValue = new DataValue(new Variant(10.0));
            var value = new DataValue(new Variant(10.5)); // Diff 0.5 <= 1.0

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.False);
        }

        [Test]
        public void ValueChanged_Double_DeadbandAbsolute_Outside()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 };
            var lastValue = new DataValue(new Variant(10.0));
            var value = new DataValue(new Variant(11.1)); // Diff 1.1 > 1.0

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Double_DeadbandPercent_Inside()
        {
            // Range = 100. Deadband = 10%. Threshold = 100 * 0.1 = 10.
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50.0));
            var value = new DataValue(new Variant(55.0)); // Diff 5 <= 10

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, range), Is.False);
        }

        [Test]
        public void ValueChanged_Double_DeadbandPercent_Outside()
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50.0));
            var value = new DataValue(new Variant(61.0)); // Diff 11 > 10

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, range), Is.True);
        }

        [Test]
        public void ValueChanged_Float_DeadbandAbsolute()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 0.5 };
            var lastValue = new DataValue(new Variant(1.0f));
            var valueInside = new DataValue(new Variant(1.4f));
            var valueOutside = new DataValue(new Variant(1.6f));

            Assert.That(MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0), Is.False);
            Assert.That(MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Int_DeadbandAbsolute()
        {
            // Initializes implicit conversion to decimal in ExceedsDeadband
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 5.0 };
            var lastValue = new DataValue(new Variant(10));
            var valueInside = new DataValue(new Variant(14));
            var valueOutside = new DataValue(new Variant(16));

            Assert.That(MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0), Is.False, "Inside deadband");
            Assert.That(MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0), Is.True, "Outside deadband");
        }

        [Test]
        public void ValueChanged_Int_DeadbandPercent()
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Percent, DeadbandValue = 10.0 };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50));
            var valueInside = new DataValue(new Variant(55));
            var valueOutside = new DataValue(new Variant(61));

            Assert.That(MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, range), Is.False, "Inside percent deadband");
            Assert.That(MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, range), Is.True, "Outside percent deadband");
        }

        [Test]
        public void ValueChanged_XmlElement_Compare()
        {
            var doc = new XmlDocument();
            XmlElement elem1 = doc.CreateElement("Root");
            elem1.InnerXml = "<Child>A</Child>";

            XmlElement elem2 = doc.CreateElement("Root");
            elem2.InnerXml = "<Child>A</Child>";

            XmlElement elem3 = doc.CreateElement("Root");
            elem3.InnerXml = "<Child>B</Child>";

            var lastValue = new DataValue(new Variant(elem1));
            var valueSame = new DataValue(new Variant(elem2));
            var valueDiff = new DataValue(new Variant(elem3));

            Assert.That(MonitoredItem.ValueChanged(valueSame, null, lastValue, null, null, 0), Is.False);
            Assert.That(MonitoredItem.ValueChanged(valueDiff, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Array_LengthMismatch()
        {
            var lastValue = new DataValue(new Variant([1, 2]));
            var value = new DataValue(new Variant([1, 2, 3]));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_DoubleArray_Deadband()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 0.5 };
            var lastValue = new DataValue(new Variant([1.0, 2.0]));
            var valueInside = new DataValue(new Variant([1.4, 2.4]));
            var valueOutside = new DataValue(new Variant([1.4, 2.6]));

            Assert.That(MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0), Is.False);
            Assert.That(MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0), Is.True);
        }

        [Test]
        public void ValueChanged_FloatArray_Deadband()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 0.5 };
            // Note: Use values that definitely exceed deadband plus epsilon
            // 2.6 - 2.0 = 0.6. 0.6 > 0.5.
            var lastValue = new DataValue(new Variant([1.0f, 2.0f]));
            var valueInside = new DataValue(new Variant([1.4f, 2.4f]));
            var valueOutside = new DataValue(new Variant([1.4f, 2.6f]));

            Assert.That(MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0), Is.False, "Inside");
            Assert.That(MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0), Is.True, "Outside");
        }

        [Test]
        public void ValueChanged_VariantArray_Recursive()
        {
            var val1 = new Variant(1);
            var val2 = new Variant(2);

            var lastValue = new DataValue(new Variant([val1, val1]));
            var value = new DataValue(new Variant([val1, val2]));

            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0), Is.True);
        }

        [Test]
        public void ValueChanged_Deadband_ConversionError_ReturnsTrue()
        {
            // Test exception path in ExceedsDeadband (object overload)
            // Use Strings which are valid in Variant but fail Convert.ToDecimal

            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 1.0 };

            const string val1 = "A";
            const string val2 = "B";

            var lastValue = new DataValue(new Variant(val1));
            var value = new DataValue(new Variant(val2));

            // ExceedsDeadband should catch FormatException and return true
            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.True);
        }
    }
}
