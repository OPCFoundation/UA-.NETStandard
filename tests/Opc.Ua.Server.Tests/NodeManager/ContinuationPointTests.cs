// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
