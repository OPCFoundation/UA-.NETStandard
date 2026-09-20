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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Parallelizable]
    public sealed class NodeBrowserDependencyTests
    {
        [Test]
        public void StoredDependenciesIncludePushbackWithoutAdvancingOrRepeatingConsumedTargets()
        {
            using var browser = new NodeBrowser(CreateContext(), null, ReferenceTypeIds.HasComponent, false,
                BrowseDirection.Forward, default, null, false);
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(100u, 1));
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(200u, 2));
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(300u, 3));
            Assert.That(browser.Next().TargetId, Is.EqualTo(new ExpandedNodeId(100u, 1)));
            IReference pushed = browser.Next();
            browser.Push(pushed);
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> first), Is.True);
            Assert.That(first.ToArray(),
                Is.EquivalentTo(new[] { new ExpandedNodeId(200u, 2), new ExpandedNodeId(300u, 3) }));
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> second), Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(browser.Next(), Is.SameAs(pushed));
            Assert.That(browser.Next().TargetId, Is.EqualTo(new ExpandedNodeId(300u, 3)));
            Assert.That(browser.Next(), Is.Null);
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> drained), Is.True);
            Assert.That(drained.IsEmpty, Is.True);
        }

        [Test]
        public void EmptyStoredBrowserDeclaresNoDependencies()
        {
            using var browser = new NodeBrowser(CreateContext(), null, default, true,
                BrowseDirection.Both, default, null, false);
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> dependencies), Is.True);
            Assert.That(dependencies.IsEmpty, Is.True);
        }

        [Test]
        public void DerivedBrowserCannotInheritAnIncompleteDependencyPromise()
        {
            using var browser = new OpaqueBrowser();
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(100u, 1));
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> dependencies), Is.False);
            Assert.That(dependencies.IsEmpty, Is.True);
            Assert.That(browser.Next().TargetId, Is.EqualTo(new ExpandedNodeId(100u, 1)),
                "An unsupported dependency query does not consume the browser.");
        }

        [Test]
        public void ExplicitDerivedContractCombinesStoredAndFutureLazyTargets()
        {
            using var browser = new DeclaredBrowser();
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(100u, 1));
            Assert.That(browser.TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> dependencies), Is.True);
            Assert.That(dependencies.ToArray(), Is.EquivalentTo(new[]
            {
                new ExpandedNodeId(100u, 1),
                new ExpandedNodeId(900u, 9)
            }));
            Assert.That(browser.Next().TargetId, Is.EqualTo(new ExpandedNodeId(100u, 1)));
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }

        private sealed class OpaqueBrowser() : NodeBrowser(
            CreateContext(), null, ReferenceTypeIds.HasComponent, false,
            BrowseDirection.Forward, default, null, false)
        {
        }

        private sealed class DeclaredBrowser() : NodeBrowser(
            CreateContext(), null, ReferenceTypeIds.HasComponent, false,
            BrowseDirection.Forward, default, null, false)
        {
            public override bool TryGetContinuationDependencies(out ArrayOf<ExpandedNodeId> targetIds)
            {
                targetIds = [.. GetRemainingReferenceTargets(), new ExpandedNodeId(900u, 9)];
                return true;
            }
        }
    }
}
