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

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Keeps exhaustive browse diagnostics out of NUnit's retained output buffers.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class CommonTestWorkersOutputTests
    {
        /// <summary>
        /// Preserves all browse results and optional diagnostic lines with bounded inline output.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(4096, false)]
        [TestCase(4096, true)]
        public async Task BrowseFullAddressSpaceKeepsInlineOutputBounded(int referenceCount, bool outputResult)
        {
            ReferenceDescription[] expected = CreateReferences(referenceCount);
            Mock<IServerTestServices> services = CreateServices(expected);

            using var isolatedContext = new TestExecutionContext.IsolatedContext();
            TestResult result = TestExecutionContext.CurrentContext.CurrentResult;
            try
            {
                ArrayOf<ReferenceDescription> references =
                    await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                        services.Object,
                        new RequestHeader(),
                        outputResult: outputResult).ConfigureAwait(false);

                Assert.That(references.ToArray(), Is.EqualTo(expected));
                Assert.That(result.Output, Does.StartWith($"Found {referenceCount} references on server."));
                Assert.That(result.Output, Has.Length.LessThan(1024),
                    "NUnit and VSTest retain and serialize inline output; exhaustive listings belong in attachments.");
                Assert.That(result.TestAttachments, Has.Count.EqualTo(outputResult ? 1 : 0));

                if (outputResult)
                {
                    await AssertAttachmentAsync(result.TestAttachments.Single(), expected).ConfigureAwait(false);
                }
                services.Verify(service => service.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(referenceCount == 0 ? 2 : 3));
            }
            finally
            {
                foreach (TestAttachment attachment in result.TestAttachments)
                {
                    File.Delete(attachment.FilePath);
                }
            }
        }

        /// <summary>
        /// Repeated dumps from one test must not overwrite earlier attached diagnostics.
        /// </summary>
        [Test]
        public async Task BrowseFullAddressSpaceCreatesDistinctAttachments()
        {
            using var isolatedContext = new TestExecutionContext.IsolatedContext();
            TestResult result = TestExecutionContext.CurrentContext.CurrentResult;
            try
            {
                for (int count = 1; count <= 2; count++)
                {
                    await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                        CreateServices(CreateReferences(count)).Object,
                        new RequestHeader(),
                        outputResult: true).ConfigureAwait(false);
                }

                TestAttachment[] attachments = [.. result.TestAttachments];
                Assert.That(attachments, Has.Length.EqualTo(2));
                Assert.That(attachments[0].FilePath, Is.Not.EqualTo(attachments[1].FilePath));
                await AssertAttachmentAsync(attachments[0], CreateReferences(1)).ConfigureAwait(false);
                await AssertAttachmentAsync(attachments[1], CreateReferences(2)).ConfigureAwait(false);
            }
            finally
            {
                foreach (TestAttachment attachment in result.TestAttachments)
                {
                    File.Delete(attachment.FilePath);
                }
            }
        }

        private static ReferenceDescription[] CreateReferences(int count)
        {
            return [.. Enumerable.Range(0, count).Select(index => new ReferenceDescription
            {
                NodeId = new ExpandedNodeId($"leaf-{index:D4}-{new string('x', 256)}", 1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName($"Node{index:D4}\u00b5", 1)
            })];
        }

        private static Mock<IServerTestServices> CreateServices(ReferenceDescription[] references)
        {
            var services = new Mock<IServerTestServices>(MockBehavior.Strict);
            var messageContext = new Mock<IServiceMessageContext>();
            messageContext.SetupGet(context => context.NamespaceUris).Returns(new NamespaceTable());
            services.SetupGet(service => service.MessageContext).Returns(messageContext.Object);
            services.SetupGet(service => service.Logger).Returns(NullLogger.Instance);
            services.SetupSequence(service => service.BrowseAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ViewDescription>(),
                It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNothingToDo))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { References = references }]
                })
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = references.Select(_ => new BrowseResult()).ToArray()
                });
            return services;
        }

        private static async Task AssertAttachmentAsync(
            TestAttachment attachment,
            ReferenceDescription[] expected)
        {
            Assert.That(attachment.Description, Is.EqualTo("Full address-space browse results"));
            using var reader = new StreamReader(attachment.FilePath);
            foreach (ReferenceDescription reference in expected)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                Assert.That(line,
                    Is.EqualTo($"NodeId {reference.NodeId} {reference.NodeClass} {reference.BrowseName}"));
            }
            Assert.That(await reader.ReadLineAsync().ConfigureAwait(false), Is.Null);
        }
    }
}
