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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies RequireAuthority refusal diagnostics when no owner can be reported.
    /// </summary>
    [TestFixture]
    public class RobotIntentAuthorityNoOwnerTests
    {
        [Test]
        public void RequireAuthorityThrowsNoOwnerMessageWhenRefusedWithoutOwner()
        {
            var transport = new Mock<IRobotIntentTransport>(MockBehavior.Strict);
            transport.SetupGet(static item => item.Logger).Returns(NullLogger.Instance);
            transport.SetupGet(static item => item.ControllerId).Returns(new NodeId("controller", 2));
            transport.SetupGet(static item => item.NamespaceUris).Returns(new NamespaceTable());
            transport.SetupGet(static item => item.MessageContext)
                .Returns(ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create(true)));
            transport.Setup(static item => item.RequestControlAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CommandAuthorityOutcome>(
                    new CommandAuthorityOutcome(false, NodeId.Null)));
            transport.Setup(static item => item.ResolveChildAsync(
                    It.IsAny<NodeId>(),
                    "ControlOwner",
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(new NodeId("control-owner", 2)));
            transport.Setup(static item => item.ReadControlOwnerAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(NodeId.Null));
            transport.Setup(static item => item.SubscribeDataChangesAsync(
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeId> _, CancellationToken ct) => EmptyDataChangesAsync(ct));
            var controller = new RobotIntentControllerClient(transport.Object);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await controller.RequireAuthorityAsync())!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
                Assert.That(exception.Message, Does.Contain("No current owner"));
            });
            transport.Verify(
                static item => item.ReleaseControlAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static async IAsyncEnumerable<RobotIntentDataChange> EmptyDataChangesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            yield break;
        }
    }
}
