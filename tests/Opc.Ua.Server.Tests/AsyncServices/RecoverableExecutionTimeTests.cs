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
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Tests exact elapsed coordinates and fail-closed domain/discontinuity checks.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableExecutionTimeTests
    {
        /// <summary>
        /// Keeps exact tick differences, including zero elapsed time and the representable upper endpoint.
        /// </summary>
        [TestCase(0L, 0L)]
        [TestCase(0L, 1L)]
        [TestCase(10L, 19L)]
        [TestCase(0L, long.MaxValue)]
        [TestCase(long.MaxValue - 1, long.MaxValue)]
        public void ElapsedTimePreservesExactTicks(long start, long end)
        {
            var domain = Uuid.NewUuid();
            var origin = new RecoverableExecutionTime(domain, start);
            var current = new RecoverableExecutionTime(domain, end);

            Assert.That(origin.DomainId, Is.EqualTo(domain));
            Assert.That(origin.Ticks, Is.EqualTo(start));
            Assert.That(origin.ElapsedUntil(current).Ticks, Is.EqualTo(end - start));
        }

        /// <summary>
        /// A clock reset or rollback cannot renew a stored lifetime.
        /// </summary>
        [TestCase(1L, 0L)]
        [TestCase(20L, 19L)]
        [TestCase(long.MaxValue, 0L)]
        public void RollbackFailsClosed(long start, long end)
        {
            var domain = Uuid.NewUuid();
            var origin = new RecoverableExecutionTime(domain, start);
            var current = new RecoverableExecutionTime(domain, end);

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => origin.ElapsedUntil(current))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        /// <summary>
        /// Equal numeric coordinates do not make unrelated clock domains comparable.
        /// </summary>
        [Test]
        public void DifferentDomainsFailClosed()
        {
            var origin = new RecoverableExecutionTime(Uuid.NewUuid(), 100);
            var current = new RecoverableExecutionTime(Uuid.NewUuid(), 100);

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => origin.ElapsedUntil(current))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        /// <summary>
        /// Neither a missing origin nor a missing current coordinate is successful clock evidence.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DefaultCoordinatesFailClosed(bool missingOrigin)
        {
            var known = new RecoverableExecutionTime(Uuid.NewUuid(), 100);
            RecoverableExecutionTime origin = missingOrigin ? default : known;
            RecoverableExecutionTime current = missingOrigin ? known : default;

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => origin.ElapsedUntil(current))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        /// <summary>
        /// Two default coordinates are still missing evidence, not a valid zero elapsed interval.
        /// </summary>
        [Test]
        public void TwoDefaultCoordinatesFailClosed()
        {
            RecoverableExecutionTime origin = default;

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => origin.ElapsedUntil(default))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        /// <summary>
        /// A missing domain cannot be constructed as qualified evidence.
        /// </summary>
        [Test]
        public void ConstructorRejectsEmptyDomain()
        {
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => new RecoverableExecutionTime(default, 0))!;

            Assert.That(error.ParamName, Is.EqualTo("domainId"));
        }

        /// <summary>
        /// Negative coordinates cannot be constructed.
        /// </summary>
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void ConstructorRejectsNegativeTicks(long ticks)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
                () => new RecoverableExecutionTime(Uuid.NewUuid(), ticks))!;

            Assert.That(error.ParamName, Is.EqualTo("ticks"));
        }
    }
}
