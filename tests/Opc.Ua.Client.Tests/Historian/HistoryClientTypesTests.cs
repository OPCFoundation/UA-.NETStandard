/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
