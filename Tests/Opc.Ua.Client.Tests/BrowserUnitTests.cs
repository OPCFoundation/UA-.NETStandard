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

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for the continuation point handling of the browser.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public class BrowserUnitTests
    {
        /// <summary>
        /// A server which signals "no more references" with an empty (zero length)
        /// continuation point must not trigger a BrowseNext call, because such a
        /// call is rejected by the server with BadContinuationPointInvalid.
        /// </summary>
        [Test]
        [TestCase(new byte[0])]
        [TestCase((byte[])null)]
        public async Task BrowseAsyncDoesNotCallBrowseNextWithoutContinuationPointAsync(
            byte[] continuationPoint)
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Strict);
            SetupBrowse(sessionMock, CreateReferences("Browsed"), continuationPoint);

            var browser = new Browser(NUnitTelemetryContext.Create())
            {
                Session = sessionMock.Object
            };

            ReferenceDescriptionCollection references = await browser
                .BrowseAsync(ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);

            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].DisplayName.Text, Is.EqualTo("Browsed"));

            sessionMock.Verify(
                x => x.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ByteStringCollection>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A non empty continuation point is followed until the server stops
        /// returning one.
        /// </summary>
        [Test]
        public async Task BrowseAsyncFollowsContinuationPointUntilExhaustedAsync()
        {
            var sessionMock = new Mock<ISessionClient>(MockBehavior.Strict);
            SetupBrowse(sessionMock, CreateReferences("First"), [1, 2, 3]);
            SetupBrowseNext(sessionMock, CreateReferences("Second"), []);

            var browser = new Browser(NUnitTelemetryContext.Create())
            {
                Session = sessionMock.Object,
                ContinueUntilDone = true
            };

            ReferenceDescriptionCollection references = await browser
                .BrowseAsync(ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);

            Assert.That(references, Has.Count.EqualTo(2));
            Assert.That(references[0].DisplayName.Text, Is.EqualTo("First"));
            Assert.That(references[1].DisplayName.Text, Is.EqualTo("Second"));

            sessionMock.Verify(
                x => x.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    false,
                    It.IsAny<ByteStringCollection>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static void SetupBrowse(
            Mock<ISessionClient> sessionMock,
            ReferenceDescriptionCollection references,
            byte[] continuationPoint)
        {
            sessionMock
                .Setup(x => x.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<BrowseDescriptionCollection>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = CreateResults(references, continuationPoint),
                    DiagnosticInfos = []
                });
        }

        private static void SetupBrowseNext(
            Mock<ISessionClient> sessionMock,
            ReferenceDescriptionCollection references,
            byte[] continuationPoint)
        {
            sessionMock
                .Setup(x => x.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ByteStringCollection>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = CreateResults(references, continuationPoint),
                    DiagnosticInfos = []
                });
        }

        private static BrowseResultCollection CreateResults(
            ReferenceDescriptionCollection references,
            byte[] continuationPoint)
        {
            return
            [
                new BrowseResult
                {
                    StatusCode = StatusCodes.Good,
                    ContinuationPoint = continuationPoint,
                    References = references
                }
            ];
        }

        private static ReferenceDescriptionCollection CreateReferences(string displayName)
        {
            return
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(new NodeId(displayName, 1)),
                    DisplayName = displayName,
                    BrowseName = new QualifiedName(displayName, 1),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IsForward = true,
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                }
            ];
        }
    }
}
