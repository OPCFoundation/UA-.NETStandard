// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ValueRanksTests
    {
        [Test]
        [TestCase(ValueRanks.ScalarOrOneDimension)]
        [TestCase(ValueRanks.Scalar)]
        [TestCase(ValueRanks.OneOrMoreDimensions)]
        [TestCase(ValueRanks.OneDimension)]
        [TestCase(ValueRanks.TwoDimensions)]
        public void TestValueRanks(int actualValueRank)
        {
            Assert.That(ValueRanks.IsValid(actualValueRank, actualValueRank), Is.True);
            Assert.That(ValueRanks.IsValid(actualValueRank, ValueRanks.Any), Is.True);
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.ScalarOrOneDimension),
                Is.EqualTo(actualValueRank is ValueRanks.Scalar or ValueRanks.OneDimension or ValueRanks
                    .ScalarOrOneDimension));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions),
                Is.EqualTo(actualValueRank >= 0));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.TwoDimensions),
                Is.EqualTo(actualValueRank == ValueRanks.TwoDimensions));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneDimension),
                Is.EqualTo(actualValueRank == ValueRanks.OneDimension));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions),
                Is.EqualTo(actualValueRank >= 0));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.Scalar),
                Is.EqualTo(actualValueRank == ValueRanks.Scalar));
        }
    }
}
