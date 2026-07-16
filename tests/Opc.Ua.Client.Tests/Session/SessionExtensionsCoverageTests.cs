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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionExtensionsCoverageTests
    {
        [Test]
        public async Task FindComponentIdsAsyncClassifiesTranslateResultsAsync()
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.TypeTree).Returns(Mock.Of<ITypeTable>());
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader { StringTable = ["diagnostic"] },
                        Results =
                        [
                            new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch },
                            new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = [] },
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    Target(new NodeId(1), uint.MaxValue),
                                    Target(new NodeId(2), uint.MaxValue)
                                ]
                            },
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = [Target(new NodeId(3), 0)]
                            },
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = NodeId.Null,
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            },
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(10u, "urn:remote"),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            },
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = [Target(new NodeId(42), uint.MaxValue)]
                            }
                        ],
                        DiagnosticInfos = []
                    }));

            (ArrayOf<NodeId> ids, ArrayOf<ServiceResult> errors) = await session.Object
                .FindComponentIdsAsync(new NodeId(100), ["A", "B", "C", "D", "E", "F", "G"])
                .ConfigureAwait(false);

            Assert.That(ids, Has.Count.EqualTo(7));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadTargetNodeIdInvalid));
            Assert.That(errors[2].StatusCode, Is.EqualTo(StatusCodes.BadTooManyMatches));
            Assert.That(errors[3].StatusCode, Is.EqualTo(StatusCodes.BadTargetNodeIdInvalid));
            Assert.That(errors[4].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(errors[5].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(ServiceResult.IsGood(errors[6]), Is.True);
            Assert.That(ids[6], Is.EqualTo(new NodeId(42)));
        }

        private static BrowsePathTarget Target(NodeId nodeId, uint remainingPathIndex)
        {
            return new BrowsePathTarget
            {
                TargetId = nodeId,
                RemainingPathIndex = remainingPathIndex
            };
        }
    }
}
