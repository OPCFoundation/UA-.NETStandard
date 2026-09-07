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

using System;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Covers the guard that refuses to run a twin whose §5.13 reconciliation is dead.
    /// </summary>
    /// <remarks>
    /// A connector whose model-change item was rejected keeps whatever composition it
    /// happened to resolve at start-up and never reconciles again, so a Dynamic component
    /// silently stops matching the server. That has to be loud: the same class of silent
    /// staleness took a long time to find the last time it went unreported.
    /// </remarks>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorModelChangeGuardTests
    {
        [Test]
        public void ARejectedModelChangeItemIsRefused()
        {
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(
                () => OpenUsdConnector.ThrowIfModelChangeRejected(error, Opc.Ua.ObjectIds.Server));

            Assert.That(thrown!.Message, Does.Contain("model-change"));
            Assert.That(thrown.Message, Does.Contain("i=2253"),
                "The message must name the node the subscription was refused on.");
        }

        [Test]
        public void EveryBadStatusIsRefused()
        {
            foreach (StatusCode status in new[]
            {
                StatusCodes.BadEventFilterInvalid,
                StatusCodes.BadAttributeIdInvalid,
                StatusCodes.BadMonitoredItemFilterUnsupported
            })
            {
                Assert.Throws<InvalidOperationException>(
                    () => OpenUsdConnector.ThrowIfModelChangeRejected(
                        new ServiceResult(status), Opc.Ua.ObjectIds.Server),
                    $"{status} should have been refused.");
            }
        }

        [Test]
        public void AnAcceptedModelChangeItemIsAllowedThrough()
        {
            Assert.DoesNotThrow(
                () => OpenUsdConnector.ThrowIfModelChangeRejected(
                    ServiceResult.Good, Opc.Ua.ObjectIds.Server));
        }

        [Test]
        public void AnUnreportedStatusIsAllowedThrough()
        {
            // A monitored item that was never given a status is not a rejected one; the
            // client stack leaves Error null until the server has answered for it.
            Assert.DoesNotThrow(
                () => OpenUsdConnector.ThrowIfModelChangeRejected(null, Opc.Ua.ObjectIds.Server));
        }
    }
}
