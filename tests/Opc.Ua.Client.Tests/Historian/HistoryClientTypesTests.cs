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

#nullable enable

using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Historian;

namespace Opc.Ua.Client.Tests.Historian
{
    /// <summary>
    /// Tests for client-side historian DTO and option types.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistoryClientTypesTests
    {
        [Test]
        public void HistoryClientOptionsDefaultTimestampsIsSource()
        {
            var options = new HistoryClientOptions();

            Assert.That(options.DefaultTimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(options.DefaultMaxValuesPerNode, Is.Zero);
        }

        [Test]
        public void SessionHistorianExtensionReturnsBoundClient()
        {
            var mockSession = new Mock<ISession>();
            ISession session = mockSession.Object;

            HistoryClient client = session.Historian();

            Assert.That(client, Is.Not.Null);
            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(client.Options, Is.Not.Null);
            Assert.That(client.Options.DefaultTimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
        }
    }
}
