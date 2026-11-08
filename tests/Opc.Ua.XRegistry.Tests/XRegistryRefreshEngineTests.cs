/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies selection behavior of the generic xRegistry refresh engine.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryRefreshEngineTests
    {
        /// <summary>
        /// A selected resource does not prepare or commit unrelated dependency closures.
        /// </summary>
        [Test]
        public async Task RefreshSelectionDoesNotProcessUnselectedClosuresAsync()
        {
            XRegistryRefreshMember selectedMember = new("selected");
            XRegistryRefreshMember dependencyMember = new("dependency");
            XRegistryRefreshMember unselectedMember = new("unselected");
            XRegistryDependencyClosure selectedClosure = CreateClosure(
                "selected-closure", selectedMember, dependencyMember);
            XRegistryDependencyClosure unselectedClosure = CreateClosure("unselected-closure", unselectedMember);
            var preparedClosures = new List<string>();
            var committedClosures = new List<(string Key, int MemberCount)>();
            var strategy = new Mock<IXRegistryRefreshStrategy>();
            strategy.Setup(value => value.EnumerateClosuresAsync(
                    It.IsAny<XRegistryRefreshRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<XRegistryDependencyClosure>>(
                    new[] { selectedClosure, unselectedClosure }.ToArrayOf()));
            strategy.Setup(value => value.Matches(
                    It.IsAny<XRegistryRefreshMember>(), It.IsAny<XRegistryResourceSelector>()))
                .Returns((XRegistryRefreshMember member, XRegistryResourceSelector selector) =>
                    member.Xid == selector.Xid);
            strategy.Setup(value => value.PrepareClosureAsync(
                    It.IsAny<XRegistryClosurePreparationContext>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryClosurePreparationContext context, CancellationToken _) =>
                {
                    preparedClosures.Add(context.Closure.Key);
                    return new ValueTask<XRegistryClosurePreparation>(XRegistryClosurePreparation.Ready(null));
                });
            strategy.Setup(value => value.CommitClosureAsync(
                    It.IsAny<XRegistryClosureCommitContext>(), It.IsAny<CancellationToken>()))
                .Returns((XRegistryClosureCommitContext context, CancellationToken _) =>
                {
                    committedClosures.Add((context.Closure.Key, context.Closure.Members.Count));
                    return new ValueTask<XRegistryClosureCommitResult>(new XRegistryClosureCommitResult());
                });
            strategy.Setup(value => value.ApplyResultsAsync(
                    It.IsAny<ArrayOf<XRegistryRefreshItemResult>>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            var host = new Mock<IXRegistryProjectionHost>();
            using var engine = new XRegistryRefreshEngine(strategy.Object, host.Object);
            var request = new XRegistryRefreshRequest
            {
                Selection = new[] { new XRegistryResourceSelector { Xid = selectedMember.Xid } }.ToArrayOf()
            };

            XRegistryRefreshResult result = await engine.RefreshAsync(request).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(preparedClosures, Is.EqualTo(new[] { selectedClosure.Key }));
                Assert.That(committedClosures, Is.EqualTo(new[] { (selectedClosure.Key, 2) }));
                Assert.That(result.Results.Count, Is.EqualTo(1));
                Assert.That(result.Results[0].Xid, Is.EqualTo(selectedMember.Xid));
                Assert.That(result.Summary.Total, Is.EqualTo(1));
                Assert.That(engine.Generation, Is.EqualTo(1));
            }
        }

        private static XRegistryDependencyClosure CreateClosure(
            string key,
            params XRegistryRefreshMember[] members)
        {
            return new XRegistryDependencyClosure(
                key,
                members.ToArrayOf(),
                members.ToArrayOf(),
                [],
                [],
                hasCycle: false,
                hasMissingDependency: false);
        }
    }
}
