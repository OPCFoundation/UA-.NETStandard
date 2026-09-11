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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that verify <see cref="MasterNodeManager"/> browse operations
    /// return a null continuation point when the browse is finished,
    /// aligning with the existing 1.5.xxx behavior.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MasterNodeManagerBrowseTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>();
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// A completed browse (all references returned, no continuation point
        /// required) must leave the continuation point null rather than an
        /// empty array.
        /// </summary>
        [Test]
        public async Task BrowseAsyncCompletedBrowseReturnsNullContinuationPointAsync()
        {
            MasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = ObjectIds.ViewsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };

            (BrowseResultCollection results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescriptionCollection { nodeToBrowse },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(results[0].ContinuationPoint, Is.Null);
        }

        /// <summary>
        /// A browse of an unknown node returns a bad status code and a null
        /// continuation point.
        /// </summary>
        [Test]
        public async Task BrowseAsyncUnknownNodeReturnsNullContinuationPointAsync()
        {
            MasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = new NodeId(99999u),
                BrowseDirection = BrowseDirection.Forward
            };

            (BrowseResultCollection results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescriptionCollection { nodeToBrowse },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(results[0].ContinuationPoint, Is.Null);
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                session.Object);
        }
    }
}
