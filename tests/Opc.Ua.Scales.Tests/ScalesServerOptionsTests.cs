// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Scales.Server;

namespace Opc.Ua.Scales.Tests
{
    [TestFixture]
    [Category("Scales")]
    public sealed class ScalesServerOptionsTests
    {
        [Test]
        public void ValidateAcceptsDefaultOptionsAndLeavesOptionalNamespacesEmpty()
        {
            var options = new ScalesServerOptions();

            ScalesServerOptions validated = options.Validate();

            Assert.That(validated, Is.SameAs(options));
            Assert.That(validated.ZeroSettingRange, Is.EqualTo(0.04));
            Assert.That(validated.AdditionalNamespaceUris.Count, Is.Zero);
        }

        [TestCase(0d)]
        [TestCase(1d)]
        public void ValidateAcceptsInclusiveZeroSettingRangeBoundaries(double zeroSettingRange)
        {
            var options = new ScalesServerOptions
            {
                ZeroSettingRange = zeroSettingRange,
                AdditionalNamespaceUris = ["urn:example:scales:vendor"]
            };

            ScalesServerOptions validated = options.Validate();

            Assert.That(validated, Is.SameAs(options));
            Assert.That(validated.ZeroSettingRange, Is.EqualTo(zeroSettingRange));
            Assert.That(validated.AdditionalNamespaceUris.Count, Is.EqualTo(1));
            Assert.That(validated.AdditionalNamespaceUris[0], Is.EqualTo("urn:example:scales:vendor"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void ValidateRejectsNullOrEmptyAdditionalNamespaceUri(string? uri)
        {
            var options = new ScalesServerOptions
            {
                AdditionalNamespaceUris = [uri!]
            };

            ArgumentException? error = Assert.Throws<ArgumentException>(() => options.Validate());

            Assert.That(error!.ParamName, Is.EqualTo(nameof(ScalesServerOptions.AdditionalNamespaceUris)));
        }

        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-0.001)]
        [TestCase(1.001)]
        [TestCase(double.PositiveInfinity)]
        public void ValidateRejectsNonFiniteOrOutOfRangeZeroSettingRange(double zeroSettingRange)
        {
            var options = new ScalesServerOptions { ZeroSettingRange = zeroSettingRange };

            ArgumentOutOfRangeException? error =
                Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.That(error!.ParamName, Is.EqualTo(nameof(ScalesServerOptions.ZeroSettingRange)));
            Assert.That(error.ActualValue, Is.EqualTo(zeroSettingRange));
        }
    }
}
