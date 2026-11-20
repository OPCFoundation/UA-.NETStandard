/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Verifies the stored-reference iteration contract for node browsers.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class NodeBrowserIterationTests
    {
        /// <summary>
        /// Pushback preserves the order of references already stored in a browser.
        /// </summary>
        [Test]
        public void PushbackReturnsReferenceBeforeRemainingStoredReferences()
        {
            using var browser = new NodeBrowser(
                CreateContext(), null, ReferenceTypeIds.HasComponent, false,
                BrowseDirection.Forward, default, null, false);
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(100u, 1));
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(200u, 2));
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(300u, 3));

            IReference first = browser.Next() ??
                throw new AssertionException("The browser must return its first stored reference.");
            IReference second = browser.Next() ??
                throw new AssertionException("The browser must return its second stored reference.");
            browser.Push(second);

            Assert.That(first.TargetId, Is.EqualTo(new ExpandedNodeId(100u, 1)));
            Assert.That(browser.Next(), Is.SameAs(second));
            Assert.That(browser.Next()?.TargetId, Is.EqualTo(new ExpandedNodeId(300u, 3)));
            Assert.That(browser.Next(), Is.Null);
        }

        /// <summary>
        /// The default asynchronous iterator produces the same sequence as the synchronous iterator.
        /// </summary>
        [Test]
        public async Task DefaultAsyncIterationDrainsStoredReferencesInOrderAsync()
        {
            using var browser = new NodeBrowser(
                CreateContext(), null, ReferenceTypeIds.HasComponent, false,
                BrowseDirection.Forward, default, null, false);
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(100u, 1));
            browser.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(200u, 2));

            Assert.That((await browser.NextAsync().ConfigureAwait(false))?.TargetId,
                Is.EqualTo(new ExpandedNodeId(100u, 1)));
            Assert.That((await browser.NextAsync().ConfigureAwait(false))?.TargetId,
                Is.EqualTo(new ExpandedNodeId(200u, 2)));
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }
    }
}
