// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Tests the independent Call status and device-output contract.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmControlResultTests
    {
        /// <summary>
        /// Proves accepted or rejected device outputs retain every independent field.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NonBadResultRetainsIndependentOutputs(bool clamped)
        {
            StatusCode status = clamped ? StatusCodes.GoodClamped : StatusCodes.Good;
            const byte code = 0;
            var result = new EcmControlResult(status, code, 2, 100, 200, 300, 400);

            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.ReturnCode, Is.EqualTo(code));
            Assert.That(result.ModeId, Is.EqualTo(2));
            Assert.That(result.CurrentTimeToDestination, Is.EqualTo(100));
            Assert.That(result.RegularTimeToOperate, Is.EqualTo(200));
            Assert.That(result.TimeMinLengthOfStay, Is.EqualTo(300));
            Assert.That(result.CurrentTimeToOperate, Is.EqualTo(400));
        }

        /// <summary>
        /// Proves Call status cannot contradict the device return code.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InconsistentDeviceCodeIsRejected(bool uncertain)
        {
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.Good;
            byte code = uncertain ? (byte)0 : (byte)0x50;
            Assert.That(() => new EcmControlResult(status, code), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Proves a device rejection retains the effective mode but cannot contain accepted-operation timings.
        /// </summary>
        [Test]
        public void RejectedResultRetainsEffectiveModeAndRequiresZeroTimings()
        {
            var result = new EcmControlResult(StatusCodes.Uncertain, 0x52, 2);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(result.ReturnCode, Is.EqualTo(0x52));
            Assert.That(result.ModeId, Is.EqualTo(2));
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
            Assert.That(result.RegularTimeToOperate, Is.Zero);
            Assert.That(result.TimeMinLengthOfStay, Is.Zero);
            Assert.That(result.CurrentTimeToOperate, Is.Zero);
            Assert.That(() => new EcmControlResult(StatusCodes.Uncertain, 0x52, currentTimeToDestination: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Uncertain, 0x52, regularTimeToOperate: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Uncertain, 0x52, timeMinLengthOfStay: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Uncertain, 0x52, currentTimeToOperate: 1),
                Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Proves a bad Call status contains no device outputs to interpret.
        /// </summary>
        [Test]
        public void BadResultHasAbsentDeviceCodeAndRejectsEachOutput()
        {
            var result = new EcmControlResult(StatusCodes.BadUserAccessDenied, null);
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, 0),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, null, modeId: 2),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, null, currentTimeToDestination: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, null, regularTimeToOperate: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, null, timeMinLengthOfStay: 1),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => new EcmControlResult(StatusCodes.BadUnexpectedError, null, currentTimeToOperate: 1),
                Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Proves both non-bad status classes require a device return code.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NonBadResultRequiresDeviceCode(bool uncertain)
        {
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.Good;
            Assert.That(() => new EcmControlResult(status, null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// Proves every timing output rejects negative and nonfinite values independently.
        /// </summary>
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidTimingCannotEnterControlResult(double value)
        {
            Assert.That(() => new EcmControlResult(StatusCodes.Good, 0, currentTimeToDestination: value),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Good, 0, regularTimeToOperate: value),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Good, 0, timeMinLengthOfStay: value),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EcmControlResult(StatusCodes.Good, 0, currentTimeToOperate: value),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
