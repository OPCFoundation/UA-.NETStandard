/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Pcap.Capture.Sources;
using SharpPcap;

namespace Opc.Ua.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class NicCaptureSourceTests
    {
        [TestCase(PcapError.PermissionDenied)]
        [TestCase(PcapError.PromiscuousPermissionDenied)]
        public void CreateOpenFailureMessageExplainsPermissionRequirement(PcapError error)
        {
            string message = NicCaptureSource.CreateOpenFailureMessage("eth0", error);

            Assert.That(message, Does.Contain("eth0"));
            Assert.That(message, Does.Contain("Permission denied"));
            Assert.That(message, Does.Contain("CAP_NET_RAW"));
            Assert.That(message, Does.Contain("elevated"));
        }
    }
}
