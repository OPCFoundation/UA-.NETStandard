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

using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Tests Part 14 §6.2.3 ConfigurationVersion rules.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.3", Summary = "ConfigurationVersion MajorVersion and MinorVersion update rules")]
    public class ConfigurationVersionUtilsTests
    {
        [Test]
        public void CalculateConfigurationVersion_WhenSingleFieldPropertiesUnchanged_DoesNotThrow()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData("A", DataTypeIds.Int32, ValueRanks.Scalar);
            DataSetMetaDataType newMetaData = CreateMetaData("A", DataTypeIds.Int32, ValueRanks.Scalar);

            ConfigurationVersionDataType version =
                ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, newMetaData);

            Assert.That(version.MajorVersion, Is.EqualTo(1u));
        }

        [Test]
        public void CalculateConfigurationVersion_WhenFieldShapeChanges_BumpsMajorVersion()
        {
            DataSetMetaDataType oldMetaData = CreateMetaData("A", DataTypeIds.Int32, ValueRanks.Scalar);
            DataSetMetaDataType newMetaData = CreateMetaData("B", DataTypeIds.Int32, ValueRanks.Scalar);

            ConfigurationVersionDataType version =
                ConfigurationVersionUtils.CalculateConfigurationVersion(oldMetaData, newMetaData);

            Assert.That(version.MajorVersion, Is.GreaterThan(1u));
            Assert.That(version.MinorVersion, Is.EqualTo(version.MajorVersion));
        }

        [Test]
        public void IsUsable_WhenMinorVersionIsZero_ReturnsTrue()
        {
            DataSetMetaDataType metaData = CreateMetaData("A", DataTypeIds.Int32, ValueRanks.Scalar);
            metaData.ConfigurationVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 0
            };

            Assert.That(ConfigurationVersionUtils.IsUsable(metaData), Is.True);
        }

        private static DataSetMetaDataType CreateMetaData(
            string fieldName,
            NodeId dataType,
            int valueRank)
        {
            return new DataSetMetaDataType
            {
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 1
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = fieldName,
                        DataType = dataType,
                        ValueRank = valueRank,
                        Properties = []
                    }
                ]
            };
        }
    }
}
