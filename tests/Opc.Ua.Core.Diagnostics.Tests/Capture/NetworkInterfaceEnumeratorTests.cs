/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Pcap.Capture.Sources;

namespace Opc.Ua.Pcap.Tests.Capture
{
    [TestFixture]
    public sealed class NetworkInterfaceEnumeratorTests
    {
        [Test]
        public void GetLinkTypeDoesNotReadClosedDevice()
        {
            bool accessed = false;

            string? linkType = NetworkInterfaceEnumerator.GetLinkType(
                opened: false,
                () =>
                {
                    accessed = true;
                    return "Ethernet";
                });

            Assert.That(linkType, Is.Null);
            Assert.That(accessed, Is.False);
        }
    }
}
