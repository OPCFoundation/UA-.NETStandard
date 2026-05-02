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

// TODO: NullChannel is internal in Opc.Ua.Core and does not grant
// InternalsVisibleTo to Opc.Ua.Client.V2.Tests.  Additionally, the
// legacy synchronous ITransportChannel methods tested here have been
// removed in the v1.6 API.  These tests are now covered by the
// Opc.Ua.Core.Tests project which has access to the internal type.
// See: Tests\Opc.Ua.Core.Tests for equivalent coverage.

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
