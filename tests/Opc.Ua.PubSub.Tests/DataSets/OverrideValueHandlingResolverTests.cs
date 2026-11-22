// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Validates the OverrideValueHandling resolution matrix from
    /// Part 14 §6.2.10.2.4: Disabled, LastUsableValue and
    /// OverrideValue combined with present / missing / bad incoming
    /// samples and present / absent last-good cache values.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.10.2.4",
        Summary = "OverrideValueHandlingResolver per-target write resolution")]
    public class OverrideValueHandlingResolverTests
    {
        private static readonly Variant s_override = new(42.0);
        private static readonly Variant s_incoming = new(7.0);
        private static readonly Variant s_lastGood = new(3.0);

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void Disabled_PassesIncomingThroughVerbatim()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.Disabled,
                s_override,
                new DataSetField { Value = s_incoming },
                DataValue.Null);
            Assert.That(resolved.IsNull, Is.False);
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_incoming));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void Disabled_NoIncoming_ReturnsNull()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.Disabled,
                s_override,
                null,
                new DataValue(s_lastGood));
            Assert.That(resolved.IsNull, Is.True);
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void LastUsable_GoodIncoming_PreferIncoming()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.LastUsableValue,
                s_override,
                new DataSetField { Value = s_incoming },
                new DataValue(s_lastGood));
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_incoming));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void LastUsable_BadIncoming_ReusesLastGood()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.LastUsableValue,
                s_override,
                new DataSetField
                {
                    Value = s_incoming,
                    StatusCode = StatusCodes.BadInternalError
                },
                new DataValue(s_lastGood));
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_lastGood));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void LastUsable_BadIncoming_NoLastGood_FallsBackToOverride()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.LastUsableValue,
                s_override,
                new DataSetField
                {
                    Value = s_incoming,
                    StatusCode = StatusCodes.BadInternalError
                },
                DataValue.Null);
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_override));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void LastUsable_Missing_NoOverride_ReturnsNull()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.LastUsableValue,
                Variant.Null,
                null,
                DataValue.Null);
            Assert.That(resolved.IsNull, Is.True);
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void OverrideValue_BadIncoming_UsesOverride()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.OverrideValue,
                s_override,
                new DataSetField
                {
                    Value = s_incoming,
                    StatusCode = StatusCodes.BadInternalError
                },
                new DataValue(s_lastGood));
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_override));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void OverrideValue_Missing_UsesOverride()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.OverrideValue,
                s_override,
                null,
                DataValue.Null);
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_override));
        }

        [Test]
        [TestSpec("6.2.10.2.4")]
        public void OverrideValue_GoodIncoming_PreferIncoming()
        {
            DataValue resolved = OverrideValueHandlingResolver.Resolve(
                OverrideValueHandling.OverrideValue,
                s_override,
                new DataSetField { Value = s_incoming },
                DataValue.Null);
            Assert.That(resolved.WrappedValue, Is.EqualTo(s_incoming));
        }
    }
}
