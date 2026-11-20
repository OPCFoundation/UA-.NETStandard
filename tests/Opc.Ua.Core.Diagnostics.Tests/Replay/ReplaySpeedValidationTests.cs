// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    /// <summary>
    /// Tests replay speed validation rejects non-finite and non-positive
    /// values before replay timing calculations use the parameter.
    /// </summary>
    [TestFixture]
    public sealed class ReplaySpeedValidationTests
    {
        [TestCase(double.NaN)]
        public void ReplayPcapRejectsNaNSpeed(double speed)
        {
            AssertRejectsSpeed(speed);
        }

        [TestCase(double.PositiveInfinity)]
        public void ReplayPcapRejectsPositiveInfinitySpeed(double speed)
        {
            AssertRejectsSpeed(speed);
        }

        [TestCase(double.NegativeInfinity)]
        public void ReplayPcapRejectsNegativeInfinitySpeed(double speed)
        {
            AssertRejectsSpeed(speed);
        }

        [TestCase(0d)]
        public void ReplayPcapRejectsZeroSpeed(double speed)
        {
            AssertRejectsSpeed(speed);
        }

        [TestCase(-1d)]
        [TestCase(-0.5d)]
        public void ReplayPcapRejectsNegativeSpeed(double speed)
        {
            AssertRejectsSpeed(speed);
        }

        [TestCase(0.5d)]
        [TestCase(1d)]
        [TestCase(2.5d)]
        [TestCase(100d)]
        public void ReplayPcapAcceptsPositiveFiniteSpeed(double speed)
        {
            Assert.That(() => InvokeValidateSpeed(speed), Throws.Nothing);
        }

        private static void AssertRejectsSpeed(double speed)
        {
            Assert.That(
                () => InvokeValidateSpeed(speed),
                Throws.TypeOf<ArgumentException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo("speed"));
        }

        private static void InvokeValidateSpeed(double speed)
        {
            MethodInfo? method = typeof(MockClientReplay).GetMethod(
                "ValidateSpeed",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            try
            {
                method!.Invoke(null, [speed]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
