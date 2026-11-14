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
 *
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
