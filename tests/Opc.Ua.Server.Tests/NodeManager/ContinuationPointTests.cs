/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic unit tests for <see cref="ContinuationPoint"/> covering the
    /// result-mask driven flag properties and the disposal behavior.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("ContinuationPoint")]
    [Parallelizable(ParallelScope.All)]
    public class ContinuationPointTests
    {
        [Test]
        public void RequiredFlagsAreFalseWhenResultMaskIsNone()
        {
            using var cp = new ContinuationPoint { ResultMask = BrowseResultMask.None };

            Assert.That(cp.ReferenceTypeIdRequired, Is.False);
            Assert.That(cp.IsForwardRequired, Is.False);
            Assert.That(cp.NodeClassRequired, Is.False);
            Assert.That(cp.BrowseNameRequired, Is.False);
            Assert.That(cp.DisplayNameRequired, Is.False);
            Assert.That(cp.TypeDefinitionRequired, Is.False);
        }

        [Test]
        public void RequiredFlagsAreTrueWhenResultMaskIsAll()
        {
            using var cp = new ContinuationPoint { ResultMask = BrowseResultMask.All };

            Assert.That(cp.ReferenceTypeIdRequired, Is.True);
            Assert.That(cp.IsForwardRequired, Is.True);
            Assert.That(cp.NodeClassRequired, Is.True);
            Assert.That(cp.BrowseNameRequired, Is.True);
            Assert.That(cp.DisplayNameRequired, Is.True);
            Assert.That(cp.TypeDefinitionRequired, Is.True);
        }

        [Test]
        public void ReferenceTypeIdRequiredReflectsOnlyThatBit()
        {
            using var cp = new ContinuationPoint
            {
                ResultMask = BrowseResultMask.ReferenceTypeId
            };

            Assert.That(cp.ReferenceTypeIdRequired, Is.True);
            Assert.That(cp.IsForwardRequired, Is.False);
            Assert.That(cp.BrowseNameRequired, Is.False);
        }

        [Test]
        public void TargetAttributesRequiredIsTrueWhenNodeClassMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = (uint)NodeClass.Variable,
                ResultMask = BrowseResultMask.None
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void TargetAttributesRequiredIsFalseWhenNoRelevantMaskSet()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.ReferenceTypeId | BrowseResultMask.IsForward
            };

            Assert.That(cp.TargetAttributesRequired, Is.False);
        }

        [Test]
        public void TargetAttributesRequiredIsTrueWhenBrowseNameRequested()
        {
            using var cp = new ContinuationPoint
            {
                NodeClassMask = 0,
                ResultMask = BrowseResultMask.BrowseName
            };

            Assert.That(cp.TargetAttributesRequired, Is.True);
        }

        [Test]
        public void DisposeDisposesDisposableData()
        {
            var disposable = new TrackingDisposable();
            var cp = new ContinuationPoint { Data = disposable };

            cp.Dispose();

            Assert.That(disposable.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeWithNonDisposableDataDoesNotThrow()
        {
            var cp = new ContinuationPoint { Data = "not-disposable" };

            Assert.DoesNotThrow(cp.Dispose);
        }

        private sealed class TrackingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
