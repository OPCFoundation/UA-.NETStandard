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

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture]
    public class ConfigurationVersionUtilsTests
    {
        [Test]
        public void CalculateConfigurationVersionThrowsOnNullNewMetaData()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData(1);

            Assert.That(
                () => ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CalculateConfigurationVersionMajorChangeWhenOldIsNull()
        {
            DataSetMetaDataType newMetaData = CreateMetaData(2);

            ConfigurationVersionDataType result = ConfigurationVersionUtils.CalculateConfigurationVersion(null, newMetaData);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.MajorVersion, Is.GreaterThan(0));
            Assert.That(result.MajorVersion, Is.EqualTo(result.MinorVersion));
        }

        [Test]
        public void CalculateConfigurationVersionMajorChangeWhenFieldsRemoved()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData(3);
            DataSetMetaDataType newMetaData = CreateMetaData(1);

            ConfigurationVersionDataType result = ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, newMetaData);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.MajorVersion, Is.GreaterThan(0));
            Assert.That(result.MajorVersion, Is.EqualTo(result.MinorVersion));
        }

        [Test]
        public void CalculateConfigurationVersionMinorChangeWhenFieldsAppended()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData(2, majorVersion: 10, minorVersion: 5);
            DataSetMetaDataType newMetaData = CreateMetaData(4, majorVersion: 10, minorVersion: 5);

            ConfigurationVersionDataType result = ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, newMetaData);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.MajorVersion, Is.EqualTo(10));
            Assert.That(result.MinorVersion, Is.GreaterThan(0));
            Assert.That(result.MinorVersion, Is.Not.EqualTo(5));
        }

        [Test]
        public void CalculateConfigurationVersionNoChangeWhenFieldsSame()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData(2, majorVersion: 10, minorVersion: 5);
            DataSetMetaDataType newMetaData = CreateMetaData(2, majorVersion: 10, minorVersion: 5);

            ConfigurationVersionDataType result = ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, newMetaData);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.MajorVersion, Is.EqualTo(10));
            Assert.That(result.MinorVersion, Is.EqualTo(5));
        }

        [Test]
        public void CalculateVersionTimeReturnsZeroAtEpoch()
        {
            var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            uint result = ConfigurationVersionUtils.CalculateVersionTime(epoch);

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void CalculateVersionTimeReturnsCorrectValue()
        {
            var time = new DateTime(2000, 1, 1, 0, 1, 0, DateTimeKind.Utc);

            uint result = ConfigurationVersionUtils.CalculateVersionTime(time);

            Assert.That(result, Is.EqualTo(60));
        }

        [Test]
        public void IsUsableReturnsFalseForNull()
        {
            Assert.That(ConfigurationVersionUtils.IsUsable(null), Is.False);
        }

        [Test]
        public void IsUsableReturnsFalseForEmptyFields()
        {
            DataSetMetaDataType metaData = CreateMetaData(0);

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.False);
        }

        [Test]
        public void IsUsableReturnsFalseForNullConfigVersion()
        {
            DataSetMetaDataType metaData = CreateMetaData(1);
            metaData.ConfigurationVersion = null;

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.False);
        }

        [Test]
        public void IsUsableReturnsFalseForZeroMajorVersion()
        {
            DataSetMetaDataType metaData = CreateMetaData(1, majorVersion: 0, minorVersion: 1);

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.False);
        }

        [Test]
        public void IsUsableReturnsFalseForZeroMinorVersion()
        {
            DataSetMetaDataType metaData = CreateMetaData(1, majorVersion: 1, minorVersion: 0);

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.False);
        }

        [Test]
        public void IsUsableReturnsTrueForValidMetaData()
        {
            DataSetMetaDataType metaData = CreateMetaData(2, majorVersion: 1, minorVersion: 1);

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.True);
        }

        private static DataSetMetaDataType CreateMetaData(int fieldCount, uint majorVersion = 1, uint minorVersion = 1)
        {
            var fields = new FieldMetaData[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = new FieldMetaData { Name = $"Field{i}" };
            }
            return new DataSetMetaDataType
            {
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                },
                Fields = fields
            };
        }
    }
}
