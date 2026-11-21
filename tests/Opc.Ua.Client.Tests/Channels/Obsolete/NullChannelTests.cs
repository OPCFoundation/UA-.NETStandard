// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// TODO: NullChannel is internal in Opc.Ua.Core and does not grant
// InternalsVisibleTo to Opc.Ua.Client.V2.Tests.  Additionally, the
// legacy synchronous ITransportChannel methods tested here have been
// removed in the v1.6 API.  These tests are now covered by the
// Opc.Ua.Core.Tests project which has access to the internal type.
// See: tests\Opc.Ua.Core.Tests for equivalent coverage.

using NUnit.Framework;

namespace Opc.Ua.Client.Obsolete
{
    [TestFixture]
    public sealed class NullChannelTests
    {
        [Test]
        public void Placeholder()
        {
            // NullChannel tests moved to Opc.Ua.Core.Tests
            Assert.Pass("NullChannel is internal to Opc.Ua.Core; tests live in Core.Tests.");
        }
    }
}
