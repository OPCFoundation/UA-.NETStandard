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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The work of one TranslateBrowsePathsToNodeIds request has to stay bounded,
    /// whatever relative path the Client sent.
    /// </summary>
    /// <remarks>
    /// AuditConditionEnableEventType (i=2803) is the target of an AlwaysGeneratesEvent
    /// reference from every Method named Enable, and each of those Methods points back
    /// to it. A path that alternates between the inverse and the forward reference
    /// multiplies the branches of the walk with every pair of elements, unless a node
    /// is followed only once for each path element.
    /// </remarks>
    [TestFixture]
    [Category("MasterNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MasterNodeManagerTranslateBrowsePathTests
    {
        private static readonly TimeSpan s_timeLimit = TimeSpan.FromSeconds(30);

        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private int m_enableMethods;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>();
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);

            // one element has no way back, so it lists every Enable Method once.
            BrowsePathResultCollection results = await TranslateAsync(CreateEnableFanOutPath(1))
                .ConfigureAwait(false);
            m_enableMethods = results[0].Targets.Count;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The path ends on the Enable Methods, however often it went back and forth
        /// before. Each of them is returned once.
        /// </summary>
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(21)]
        public async Task TranslateBrowsePathReturnsEachTargetOnceAsync(int elements)
        {
            Assert.That(m_enableMethods, Is.GreaterThan(1), "the path needs a fan-out");

            BrowsePathResultCollection results = await TranslateAsync(CreateEnableFanOutPath(elements))
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            var targets = new HashSet<ExpandedNodeId>();
            foreach (BrowsePathTarget target in results[0].Targets)
            {
                Assert.That(targets.Add(target.TargetId), Is.True, $"{target.TargetId} is returned twice");
                Assert.That(target.RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            }
            Assert.That(targets, Has.Count.EqualTo(m_enableMethods));
        }

        /// <summary>
        /// A path that still costs too much when no node is followed twice ends with
        /// BadQueryTooComplex. The next path of the request is translated.
        /// </summary>
        [Test]
        public async Task TranslateBrowsePathOverTheWorkLimitReturnsBadQueryTooComplexAsync()
        {
            BrowsePathResultCollection results = await TranslateAsync(
                CreateEnableFanOutPath(20001),
                CreateEnableFanOutPath(3)).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadQueryTooComplex));
            Assert.That(results[0].Targets, Is.Empty);
            Assert.That(results[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(results[1].Targets, Has.Count.EqualTo(m_enableMethods));
        }

        /// <summary>
        /// A long path without fan-out goes from the Objects folder to the Server object
        /// and back. It is cheap and is translated.
        /// </summary>
        [Test]
        public async Task TranslateBrowsePathFollowsALongPathWithoutFanOutAsync()
        {
            const int elements = 30001;
            var path = new RelativePathElementCollection(elements);
            for (int ii = 0; ii < elements; ii++)
            {
                bool inverse = ii % 2 != 0;
                path.Add(new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    IsInverse = inverse,
                    IncludeSubtypes = false,
                    TargetName = new QualifiedName(inverse ? BrowseNames.ObjectsFolder : BrowseNames.Server)
                });
            }

            BrowsePathResultCollection results = await TranslateAsync(
                new BrowsePath
                {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath { Elements = path }
                }).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(results[0].Targets, Has.Count.EqualTo(1));
            Assert.That(results[0].Targets[0].TargetId, Is.EqualTo((ExpandedNodeId)ObjectIds.Server));
        }

        /// <summary>
        /// Translates the paths and fails the test if that takes longer than the time limit.
        /// </summary>
        private async Task<BrowsePathResultCollection> TranslateAsync(params BrowsePath[] browsePaths)
        {
            using var timeout = new CancellationTokenSource(s_timeLimit);

            Task<(BrowsePathResultCollection, DiagnosticInfoCollection)> translate = Task.Run(
                () => m_server.CurrentInstance.NodeManager.TranslateBrowsePathsToNodeIdsAsync(
                    CreateContext(),
                    [.. browsePaths],
                    timeout.Token).AsTask());

            Task completed = await Task.WhenAny(translate, Task.Delay(s_timeLimit)).ConfigureAwait(false);
            if (completed != translate)
            {
                Assert.Fail($"The paths were still being translated after {s_timeLimit.TotalSeconds} s.");
            }

            (BrowsePathResultCollection results, _) = await translate.ConfigureAwait(false);
            return results;
        }

        private static BrowsePath CreateEnableFanOutPath(int elements)
        {
            var path = new RelativePathElementCollection(elements);
            for (int ii = 0; ii < elements; ii++)
            {
                bool inverse = ii % 2 == 0;
                path.Add(new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.AlwaysGeneratesEvent,
                    IsInverse = inverse,
                    IncludeSubtypes = false,
                    TargetName = new QualifiedName(
                        inverse ? BrowseNames.Enable : BrowseNames.AuditConditionEnableEventType)
                });
            }

            return new BrowsePath
            {
                StartingNode = ObjectTypeIds.AuditConditionEnableEventType,
                RelativePath = new RelativePath { Elements = path }
            };
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.TranslateBrowsePathsToNodeIds,
                session.Object);
        }
    }
}
