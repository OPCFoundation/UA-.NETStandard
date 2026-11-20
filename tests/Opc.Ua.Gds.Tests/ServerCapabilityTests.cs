// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerCapabilityTests
    {
        [Test]
        public void ToStringReturnsFormattedString()
        {
            var capability = new ServerCapabilityInfo { Id = "DA", Description = "Live Data" };
            string result = capability.ToString();
            Assert.That(result, Is.EqualTo("[DA] Live Data"));
        }

        [Test]
        public void ToStringWithNullFormatReturnsFormattedString()
        {
            var capability = new ServerCapabilityInfo { Id = "AC", Description = "Alarms" };
            string result = capability.ToString(null, null);
            Assert.That(result, Is.EqualTo("[AC] Alarms"));
        }

        [Test]
        public void ToStringWithNonNullFormatThrowsFormatException()
        {
            var capability = new ServerCapabilityInfo { Id = "DA", Description = "Live Data" };
            Assert.Throws<FormatException>(() => capability.ToString("G", null));
        }

        [TestCase(ServerCapability.DA, "DA")]
        [TestCase(ServerCapability.NA, "NA")]
        [TestCase(ServerCapability.AC, "AC")]
        [TestCase(ServerCapability.HD, "HD")]
        [TestCase(ServerCapability.HE, "HE")]
        [TestCase(ServerCapability.GDS, "GDS")]
        [TestCase(ServerCapability.LDS, "LDS")]
        [TestCase(ServerCapability.DI, "DI")]
        public void ConstantsHaveExpectedValues(string actual, string expected)
        {
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
