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

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using Opc.Ua.Pcap.Replay;

using Opc.Ua.Bindings;

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
