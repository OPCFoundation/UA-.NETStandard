// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Alarms
{
    /// <summary>
    /// Tests for <see cref="AlarmClientSessionExtensions.GetAlarmClient(ISession, ITelemetryContext)"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class AlarmClientSessionExtensionsTests
    {
        [Test]
        public void GetAlarmClientReturnsNonNullAlarmClient()
        {
            ISession session = new Mock<ISession>(MockBehavior.Loose).Object;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            AlarmClient client = session.GetAlarmClient(telemetry);

            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void GetAlarmClientWithNullSessionThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.That(
                () => AlarmClientSessionExtensions.GetAlarmClient(null!, telemetry),
                Throws.ArgumentNullException);
        }

        [Test]
        public void GetAlarmClientWithNullTelemetryThrowsArgumentNullException()
        {
            ISession session = new Mock<ISession>(MockBehavior.Loose).Object;

            Assert.That(
                () => session.GetAlarmClient(null!),
                Throws.ArgumentNullException);
        }
    }
}
