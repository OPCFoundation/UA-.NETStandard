// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
