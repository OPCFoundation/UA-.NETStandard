// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Unit tests for the value semantics of <see cref="UsdTimeSample"/>. Samples are compared
    /// by (time code, value) so a recorded timeline can be diffed against an expected one, and
    /// the hash is keyed on the time code alone so samples that compare equal always hash equal.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdTimeSampleEqualityTests
    {
        [Test]
        public void SamplesWithTheSameTimeCodeAndValueAreEqual()
        {
            var left = new UsdTimeSample(1.5, UsdValue.From(42.0));
            var right = new UsdTimeSample(1.5, UsdValue.From(42.0));
            bool viaOperator = left == right;
            bool viaInequality = left != right;

            Assert.That(left, Is.EqualTo(right));
            Assert.That(viaOperator, Is.True);
            Assert.That(viaInequality, Is.False);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void SamplesWithADifferentTimeCodeAreNotEqual()
        {
            var left = new UsdTimeSample(1.5, UsdValue.From(42.0));
            var right = new UsdTimeSample(2.5, UsdValue.From(42.0));
            bool viaOperator = left == right;
            bool viaInequality = left != right;

            Assert.That(left, Is.Not.EqualTo(right));
            Assert.That(viaOperator, Is.False);
            Assert.That(viaInequality, Is.True);
        }

        [Test]
        public void SamplesWithADifferentValueAreNotEqual()
        {
            var left = new UsdTimeSample(1.5, UsdValue.From(42.0));
            var right = new UsdTimeSample(1.5, UsdValue.FromString("42"));
            bool viaEquatable = left.Equals(right);

            Assert.That(viaEquatable, Is.False);
            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void ASampleWithoutAValueEqualsAnotherWithoutAValue()
        {
            var left = new UsdTimeSample(-3.25, UsdValue.Null);
            var right = new UsdTimeSample(-3.25, UsdValue.Null);
            bool viaEquatable = left.Equals(right);

            Assert.That(viaEquatable, Is.True);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void ASampleEqualsABoxedSampleWithTheSameContent()
        {
            var sample = new UsdTimeSample(0.5, UsdValue.From(7L));
            object boxed = new UsdTimeSample(0.5, UsdValue.From(7L));
            bool equal = sample.Equals(boxed);

            Assert.That(equal, Is.True);
        }

        [Test]
        public void ASampleDoesNotEqualAnObjectOfAnotherType()
        {
            var sample = new UsdTimeSample(0.5, UsdValue.From(7L));
            bool equalsText = sample.Equals("0.5");
            bool equalsNothing = sample.Equals(null);

            Assert.That(equalsText, Is.False);
            Assert.That(equalsNothing, Is.False);
        }
    }
}
