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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Scales.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// The error handling of the <see cref="ScalesClient"/> browse helper
    /// against a mocked channel.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScalesClientTests
    {
        [Test]
        public async Task BrowseOfANodeWithABadStatusYieldsNothingAsync()
        {
            using SessionMock session = SessionMock.Create();
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }],
                    DiagnosticInfos = []
                }));
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());

            var children = new List<ReferenceDescription>();
            await foreach (ReferenceDescription reference in client
                .BrowseChildrenAsync(new NodeId("Scale", 2), NodeClass.Object, CancellationToken.None)
                .ConfigureAwait(false))
            {
                children.Add(reference);
            }

            Assert.That(children, Is.Empty);
        }

        [Test]
        public void BrowseThatFailsAsAServiceCallPropagates()
        {
            // A timeout or a lost session is not an empty folder.
            using SessionMock session = SessionMock.Create();
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<BrowseRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(
                    Task.FromException<IServiceResponse>(new ServiceResultException(StatusCodes.BadTimeout))));
            var client = new ScalesClient(session, NUnitTelemetryContext.Create());

            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (ReferenceDescription reference in client
                    .BrowseChildrenAsync(new NodeId("Scale", 2), NodeClass.Object, CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    Assert.Fail("No reference was expected, but got " + reference.BrowseName);
                }
            });
            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
        }
    }
}
