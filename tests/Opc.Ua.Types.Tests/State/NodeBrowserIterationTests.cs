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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
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
