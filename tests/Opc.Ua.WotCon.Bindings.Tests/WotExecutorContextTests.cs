// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotExecutorContext"/> default dependency handling.
    /// </summary>
    [TestFixture]
    public sealed class WotExecutorContextTests
    {
        [Test]
        public void ConstructorWithoutTelemetryFallsBackToAmbientTelemetryContext()
        {
            var context = new WotExecutorContext();

            Assert.That(context.Telemetry, Is.SameAs(AmbientMessageContext.Telemetry));
        }

        [Test]
        public void ConstructorWithTelemetryPreservesSuppliedTelemetryContext()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(_ => { });
            try
            {
                var context = new WotExecutorContext(telemetry: telemetry);

                Assert.That(context.Telemetry, Is.SameAs(telemetry));
            }
            finally
            {
                (telemetry as IDisposable)?.Dispose();
            }
        }
    }
}
