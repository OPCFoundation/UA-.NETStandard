/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
