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
        public void ValueChangedValueNullThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => MonitoredItem.ValueChanged(null, null, null, null, null, 0));
        }

        [Test]
        public void ValueChangedLastValueNullReturnsTrue()
        {
            var value = new DataValue(new Variant(1));
            Assert.That(
                MonitoredItem.ValueChanged(value, null, null, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedStatusChangedReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var value = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Bad };

            // Status different
            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedErrorOverridesStatusReturnsTrueIfChanged()
        {
            var lastValue = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var value = new DataValue(new Variant(1)) { StatusCode = StatusCodes.Good };
            var error = new ServiceResult(StatusCodes.Bad);

            // Error makes new status Bad, last was Good -> Changed
            Assert.That(
                MonitoredItem.ValueChanged(value, error, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedSameValueReturnsFalse()
        {
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(1));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.False);
        }

        [Test]
        public void ValueChangedDifferentValueReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(2));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedSameReferenceReturnsFalse()
        {
            const string obj = "test";
            var lastValue = new DataValue(new Variant(obj));
            var value = new DataValue(new Variant(obj));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.False);
        }

        [Test]
        public void ValueChangedTriggerStatusChangeIgnoresValueChange()
        {
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.Status };
            var lastValue = new DataValue(new Variant(1));
            var value = new DataValue(new Variant(2)); // Value changed

            // Status is Good for both, so no status change.
            // Trigger is Status, so value change should be ignored.
            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0),
                Is.False);
        }

        [Test]
        public void ValueChangedTriggerStatusValueTimestampTimestampChanged()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp
            };
            DateTime now = DateTime.UtcNow;
            var lastValue = new DataValue(new Variant(1)) { SourceTimestamp = now };
            var value = new DataValue(new Variant(1)) { SourceTimestamp = now.AddMilliseconds(1) };

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedTriggerStatusValueTimestampTimestampSame()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp
            };
            DateTime now = DateTime.UtcNow;
            var lastValue = new DataValue(new Variant(1)) { SourceTimestamp = now };
            var value = new DataValue(new Variant(1)) { SourceTimestamp = now };

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0),
                Is.False);
        }

        [Test]
        public void ValueChangedTypeMismatchReturnsTrue()
        {
            var lastValue = new DataValue(new Variant(1)); // int
            var value = new DataValue(new Variant(1.0)); // double

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChangedDoubleNaNReturnsFalse()
        {
            var lastValue = new DataValue(Variant.From(double.NaN));
            var value = new DataValue(Variant.From(double.NaN));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.False);
        }

        [Test]
        public void ValueChangedFloatNaNReturnsFalse()
        {
            // NaN != NaN, so ValueChanged returns True
            var lastValue = new DataValue(Variant.From(float.NaN));
            var value = new DataValue(Variant.From(float.NaN));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.False);
        }

        [Test]
        public void ValueChanged_Double_DeadbandAbsolute_Inside()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.0
            };
            var lastValue = new DataValue(new Variant(10.0));
            var value = new DataValue(new Variant(10.5)); // Diff 0.5 <= 1.0

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0),
                Is.False);
        }

        [Test]
        public void ValueChanged_Double_DeadbandAbsolute_Outside()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.0
            };
            var lastValue = new DataValue(new Variant(10.0));
            var value = new DataValue(new Variant(11.1)); // Diff 1.1 > 1.0

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0),
                Is.True);
        }

        [Test]
        public void ValueChanged_Double_DeadbandPercent_Inside()
        {
            // Range = 100. Deadband = 10%. Threshold = 100 * 0.1 = 10.
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50.0));
            var value = new DataValue(new Variant(55.0)); // Diff 5 <= 10

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, range),
                Is.False);
        }

        [Test]
        public void ValueChanged_Double_DeadbandPercent_Outside()
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50.0));
            var value = new DataValue(new Variant(61.0)); // Diff 11 > 10

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, filter, range),
                Is.True);
        }

        [Test]
        public void ValueChanged_Float_DeadbandAbsolute()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 0.5
            };
            var lastValue = new DataValue(new Variant(1.0f));
            var valueInside = new DataValue(new Variant(1.4f));
            var valueOutside = new DataValue(new Variant(1.6f));

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False);
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True);
        }

        [Test]
        public void ValueChanged_Int_DeadbandAbsolute()
        {
            // Initializes implicit conversion to decimal in ExceedsDeadband
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 5.0
            };
            var lastValue = new DataValue(new Variant(10));
            var valueInside = new DataValue(new Variant(14));
            var valueOutside = new DataValue(new Variant(16));

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False,
                "Inside deadband");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True,
                "Outside deadband");
        }

        [Test]
        public void ValueChanged_Int_DeadbandPercent()
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };
            const double range = 100.0;
            var lastValue = new DataValue(new Variant(50));
            var valueInside = new DataValue(new Variant(55));
            var valueOutside = new DataValue(new Variant(61));

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, range),
                Is.False,
                "Inside percent deadband");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, range),
                Is.True,
                "Outside percent deadband");
        }

        [Test]
        public void ValueChanged_XmlElement_Compare()
        {
            var doc = new XmlDocument();
            System.Xml.XmlElement elem1 = doc.CreateElement("Root");
            elem1.InnerXml = "<Child>A</Child>";

            System.Xml.XmlElement elem2 = doc.CreateElement("Root");
            elem2.InnerXml = "<Child>A</Child>";

            System.Xml.XmlElement elem3 = doc.CreateElement("Root");
            elem3.InnerXml = "<Child>B</Child>";

            var lastValue = new DataValue(new Variant(XmlElement.From(elem1)));
            var valueSame = new DataValue(new Variant(XmlElement.From(elem2)));
            var valueDiff = new DataValue(new Variant(XmlElement.From(elem3)));

            Assert.That(
                MonitoredItem.ValueChanged(valueSame, null, lastValue, null, null, 0),
                Is.False);
            Assert.That(
                MonitoredItem.ValueChanged(valueDiff, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChanged_Array_LengthMismatch()
        {
            var lastValue = new DataValue(new Variant([1, 2]));
            var value = new DataValue(new Variant([1, 2, 3]));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChanged_DoubleArray_Deadband()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 0.5
            };
            var lastValue = new DataValue(new Variant([1.0, 2.0]));
            var valueInside = new DataValue(new Variant([1.4, 2.4]));
            var valueOutside = new DataValue(new Variant([1.4, 2.6]));

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False);
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True);
        }

        [Test]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.SByte)]
        public void ValueChanged_DeadbandPercent(BuiltInType builtInType)
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };
            const double range = 100.0;
            DataValue lastValue;
            DataValue valueInside;
            DataValue valueOutside;

            switch (builtInType)
            {
                case BuiltInType.Double:
                    lastValue = new DataValue(new Variant(50.0));
                    valueInside = new DataValue(new Variant(55.0));
                    valueOutside = new DataValue(new Variant(61.0));
                    break;
                case BuiltInType.Float:
                    lastValue = new DataValue(new Variant(50.0f));
                    valueInside = new DataValue(new Variant(55.0f));
                    valueOutside = new DataValue(new Variant(61.0f));
                    break;
                case BuiltInType.Int16:
                    lastValue = new DataValue(new Variant((short)50));
                    valueInside = new DataValue(new Variant((short)55));
                    valueOutside = new DataValue(new Variant((short)61));
                    break;
                case BuiltInType.UInt16:
                    lastValue = new DataValue(new Variant((ushort)50));
                    valueInside = new DataValue(new Variant((ushort)55));
                    valueOutside = new DataValue(new Variant((ushort)61));
                    break;
                case BuiltInType.Int32:
                    lastValue = new DataValue(new Variant(50));
                    valueInside = new DataValue(new Variant(55));
                    valueOutside = new DataValue(new Variant(61));
                    break;
                case BuiltInType.UInt32:
                    lastValue = new DataValue(new Variant((uint)50));
                    valueInside = new DataValue(new Variant((uint)55));
                    valueOutside = new DataValue(new Variant((uint)61));
                    break;
                case BuiltInType.Int64:
                    lastValue = new DataValue(new Variant((long)50));
                    valueInside = new DataValue(new Variant((long)55));
                    valueOutside = new DataValue(new Variant((long)61));
                    break;
                case BuiltInType.UInt64:
                    lastValue = new DataValue(new Variant((ulong)50));
                    valueInside = new DataValue(new Variant((ulong)55));
                    valueOutside = new DataValue(new Variant((ulong)61));
                    break;
                case BuiltInType.Byte:
                    lastValue = new DataValue(new Variant((byte)50));
                    valueInside = new DataValue(new Variant((byte)55));
                    valueOutside = new DataValue(new Variant((byte)61));
                    break;
                case BuiltInType.SByte:
                    lastValue = new DataValue(new Variant((sbyte)50));
                    valueInside = new DataValue(new Variant((sbyte)55));
                    valueOutside = new DataValue(new Variant((sbyte)61));
                    break;
                default:
                    throw new NotImplementedException();
            }

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, range),
                Is.False,
                $"Inside percent deadband for {builtInType}");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, range),
                Is.True,
                $"Outside percent deadband for {builtInType}");
        }

        [Test]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.SByte)]
        public void ValueChanged_Array_DeadbandPercent(BuiltInType builtInType)
        {
            // Range = 100. Deadband = 10%. Threshold = 10.
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };
            const double range = 100.0;
            DataValue lastValue;
            DataValue valueInside;
            DataValue valueOutside;

            switch (builtInType)
            {
                case BuiltInType.Double:
                    lastValue = new DataValue(new Variant((double[])[50.0, 50.0]));
                    valueInside = new DataValue(new Variant((double[])[55.0, 55.0]));
                    valueOutside = new DataValue(new Variant((double[])[55.0, 61.0]));
                    break;
                case BuiltInType.Float:
                    lastValue = new DataValue(new Variant((float[])[50.0f, 50.0f]));
                    valueInside = new DataValue(new Variant((float[])[55.0f, 55.0f]));
                    valueOutside = new DataValue(new Variant((float[])[55.0f, 61.0f]));
                    break;
                case BuiltInType.Int16:
                    lastValue = new DataValue(new Variant((short[])[50, 50]));
                    valueInside = new DataValue(new Variant((short[])[55, 55]));
                    valueOutside = new DataValue(new Variant((short[])[55, 61]));
                    break;
                case BuiltInType.UInt16:
                    lastValue = new DataValue(new Variant((ushort[])[50, 50]));
                    valueInside = new DataValue(new Variant((ushort[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ushort[])[55, 61]));
                    break;
                case BuiltInType.Int32:
                    lastValue = new DataValue(new Variant((int[])[50, 50]));
                    valueInside = new DataValue(new Variant((int[])[55, 55]));
                    valueOutside = new DataValue(new Variant((int[])[55, 61]));
                    break;
                case BuiltInType.UInt32:
                    lastValue = new DataValue(new Variant((uint[])[50, 50]));
                    valueInside = new DataValue(new Variant((uint[])[55, 55]));
                    valueOutside = new DataValue(new Variant((uint[])[55, 61]));
                    break;
                case BuiltInType.Int64:
                    lastValue = new DataValue(new Variant((long[])[50, 50]));
                    valueInside = new DataValue(new Variant((long[])[55, 55]));
                    valueOutside = new DataValue(new Variant((long[])[55, 61]));
                    break;
                case BuiltInType.UInt64:
                    lastValue = new DataValue(new Variant((ulong[])[50, 50]));
                    valueInside = new DataValue(new Variant((ulong[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ulong[])[55, 61]));
                    break;
                case BuiltInType.Variant:
                    lastValue = new DataValue(new Variant((Variant[])[new Variant(50), new Variant(50)]));
                    valueInside = new DataValue(new Variant((Variant[])[new Variant(55), new Variant(55)]));
                    valueOutside = new DataValue(new Variant((Variant[])[new Variant(55), new Variant(61)]));
                    break;
                case BuiltInType.Byte:
                    lastValue = new DataValue(new Variant((ArrayOf<byte>)(byte[])[50, 50]));
                    valueInside = new DataValue(new Variant((ArrayOf<byte>)(byte[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ArrayOf<byte>)(byte[])[55, 61]));
                    break;
                case BuiltInType.SByte:
                    lastValue = new DataValue(new Variant((ArrayOf<sbyte>)(sbyte[])[50, 50]));
                    valueInside = new DataValue(new Variant((ArrayOf<sbyte>)(sbyte[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ArrayOf<sbyte>)(sbyte[])[55, 61]));
                    break;
                default:
                    throw new NotImplementedException();
            }

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, range),
                Is.False,
                "Inside percent deadband");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, range),
                Is.True,
                "Outside percent deadband");
        }

        [Test]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Byte)]
        public void ValueChanged_DeadbandAbsolute(BuiltInType builtInType)
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 10.0
            };
            DataValue lastValue;
            DataValue valueInside;
            DataValue valueOutside;

            switch (builtInType)
            {
                case BuiltInType.Double:
                    lastValue = new DataValue(new Variant(50.0));
                    valueInside = new DataValue(new Variant(55.0));
                    valueOutside = new DataValue(new Variant(61.0));
                    break;
                case BuiltInType.Float:
                    lastValue = new DataValue(new Variant(50.0f));
                    valueInside = new DataValue(new Variant(55.0f));
                    valueOutside = new DataValue(new Variant(61.0f));
                    break;
                case BuiltInType.Int16:
                    lastValue = new DataValue(new Variant((short)50));
                    valueInside = new DataValue(new Variant((short)55));
                    valueOutside = new DataValue(new Variant((short)61));
                    break;
                case BuiltInType.UInt16:
                    lastValue = new DataValue(new Variant((ushort)50));
                    valueInside = new DataValue(new Variant((ushort)55));
                    valueOutside = new DataValue(new Variant((ushort)61));
                    break;
                case BuiltInType.Int32:
                    lastValue = new DataValue(new Variant(50));
                    valueInside = new DataValue(new Variant(55));
                    valueOutside = new DataValue(new Variant(61));
                    break;
                case BuiltInType.UInt32:
                    lastValue = new DataValue(new Variant((uint)50));
                    valueInside = new DataValue(new Variant((uint)55));
                    valueOutside = new DataValue(new Variant((uint)61));
                    break;
                case BuiltInType.Int64:
                    lastValue = new DataValue(new Variant((long)50));
                    valueInside = new DataValue(new Variant((long)55));
                    valueOutside = new DataValue(new Variant((long)61));
                    break;
                case BuiltInType.UInt64:
                    lastValue = new DataValue(new Variant((ulong)50));
                    valueInside = new DataValue(new Variant((ulong)55));
                    valueOutside = new DataValue(new Variant((ulong)61));
                    break;
                case BuiltInType.Byte:
                    lastValue = new DataValue(new Variant((byte)50));
                    valueInside = new DataValue(new Variant((byte)55));
                    valueOutside = new DataValue(new Variant((byte)61));
                    break;
                default:
                    throw new NotImplementedException();
            }

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False,
                $"Inside absolute deadband for {builtInType}");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True,
                $"Outside absolute deadband for {builtInType}");
        }

        [Test]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Byte)]
        public void ValueChanged_Array_DeadbandAbsolute(BuiltInType builtInType)
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 10.0
            };
            DataValue lastValue;
            DataValue valueInside;
            DataValue valueOutside;

            switch (builtInType)
            {
                case BuiltInType.Double:
                    lastValue = new DataValue(new Variant((double[])[50.0, 50.0]));
                    valueInside = new DataValue(new Variant((double[])[55.0, 55.0]));
                    valueOutside = new DataValue(new Variant((double[])[55.0, 61.0]));
                    break;
                case BuiltInType.Float:
                    lastValue = new DataValue(new Variant((float[])[50.0f, 50.0f]));
                    valueInside = new DataValue(new Variant((float[])[55.0f, 55.0f]));
                    valueOutside = new DataValue(new Variant((float[])[55.0f, 61.0f]));
                    break;
                case BuiltInType.Int16:
                    lastValue = new DataValue(new Variant((short[])[50, 50]));
                    valueInside = new DataValue(new Variant((short[])[55, 55]));
                    valueOutside = new DataValue(new Variant((short[])[55, 61]));
                    break;
                case BuiltInType.UInt16:
                    lastValue = new DataValue(new Variant((ushort[])[50, 50]));
                    valueInside = new DataValue(new Variant((ushort[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ushort[])[55, 61]));
                    break;
                case BuiltInType.Int32:
                    lastValue = new DataValue(new Variant((int[])[50, 50]));
                    valueInside = new DataValue(new Variant((int[])[55, 55]));
                    valueOutside = new DataValue(new Variant((int[])[55, 61]));
                    break;
                case BuiltInType.UInt32:
                    lastValue = new DataValue(new Variant((uint[])[50, 50]));
                    valueInside = new DataValue(new Variant((uint[])[55, 55]));
                    valueOutside = new DataValue(new Variant((uint[])[55, 61]));
                    break;
                case BuiltInType.Int64:
                    lastValue = new DataValue(new Variant((long[])[50, 50]));
                    valueInside = new DataValue(new Variant((long[])[55, 55]));
                    valueOutside = new DataValue(new Variant((long[])[55, 61]));
                    break;
                case BuiltInType.UInt64:
                    lastValue = new DataValue(new Variant((ulong[])[50, 50]));
                    valueInside = new DataValue(new Variant((ulong[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ulong[])[55, 61]));
                    break;
                case BuiltInType.Variant:
                    lastValue = new DataValue(new Variant((Variant[])[new Variant(50), new Variant(50)]));
                    valueInside = new DataValue(new Variant((Variant[])[new Variant(55), new Variant(55)]));
                    valueOutside = new DataValue(new Variant((Variant[])[new Variant(55), new Variant(61)]));
                    break;
                case BuiltInType.Byte:
                    lastValue = new DataValue(new Variant((ArrayOf<byte>)(byte[])[50, 50]));
                    valueInside = new DataValue(new Variant((ArrayOf<byte>)(byte[])[55, 55]));
                    valueOutside = new DataValue(new Variant((ArrayOf<byte>)(byte[])[55, 61]));
                    break;
                default:
                    throw new NotImplementedException();
            }

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False,
                $"Inside absolute deadband for array of {builtInType}");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True,
                $"Outside absolute deadband for array of {builtInType}");
        }

        [Test]
        public void ValueChanged_FloatArray_Deadband()
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 0.5
            };
            // Note: Use values that definitely exceed deadband plus epsilon
            // 2.6 - 2.0 = 0.6. 0.6 > 0.5.
            var lastValue = new DataValue(new Variant([1.0f, 2.0f]));
            var valueInside = new DataValue(new Variant([1.4f, 2.4f]));
            var valueOutside = new DataValue(new Variant([1.4f, 2.6f]));

            Assert.That(
                MonitoredItem.ValueChanged(valueInside, null, lastValue, null, filter, 0),
                Is.False,
                "Inside");
            Assert.That(
                MonitoredItem.ValueChanged(valueOutside, null, lastValue, null, filter, 0),
                Is.True,
                "Outside");
        }

        [Test]
        public void ValueChanged_VariantArray_Recursive()
        {
            var val1 = new Variant(1);
            var val2 = new Variant(2);

            var lastValue = new DataValue(new Variant([val1, val1]));
            var value = new DataValue(new Variant([val1, val2]));

            Assert.That(
                MonitoredItem.ValueChanged(value, null, lastValue, null, null, 0),
                Is.True);
        }

        [Test]
        public void ValueChanged_Deadband_ConversionError_ReturnsTrue()
        {
            // Test exception path in ExceedsDeadband (object overload)
            // Use Strings which are valid in Variant but fail Convert.ToDecimal

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.0
            };

            const string val1 = "A";
            const string val2 = "B";

            var lastValue = new DataValue(new Variant(val1));
            var value = new DataValue(new Variant(val2));

            // ExceedsDeadband should catch FormatException and return true
            Assert.That(MonitoredItem.ValueChanged(value, null, lastValue, null, filter, 0), Is.True);
        }
    }
}
