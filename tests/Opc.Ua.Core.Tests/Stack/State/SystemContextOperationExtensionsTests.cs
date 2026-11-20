// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for <see cref="SystemContextOperationExtensions"/>, which is how a callback hands
    /// the operation it is serving to an API that has to know which operation invoked it.
    /// </summary>
    [TestFixture]
    [Category("SystemContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SystemContextOperationExtensionsTests
    {
        [Test]
        public void GetOperationContextThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(
                () => SystemContextOperationExtensions.GetOperationContext(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void GetOperationContextReturnsNullWhenTheSystemContextHasNoOperation()
        {
            var context = new SystemContext(NUnitTelemetryContext.Create());

            Assert.That(context.GetOperationContext(), Is.Null);
        }

        [Test]
        public void GetOperationContextReturnsNullWhenTheSessionSystemContextHasNoOperation()
        {
            var context = new SessionSystemContext(NUnitTelemetryContext.Create());

            Assert.That(context.GetOperationContext(), Is.Null);
        }

        [Test]
        public void GetOperationContextReturnsTheOperationASystemContextWasCopiedFor()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var operation = new SystemContext(telemetry);
            var context = new SystemContext(telemetry);

            ISystemContext copy = context.Copy(operation);

            Assert.That(copy.GetOperationContext(), Is.SameAs(operation));
        }

        [Test]
        public void GetOperationContextReturnsTheOperationASessionSystemContextWasCopiedFor()
        {
            // ServerSystemContext derives from SessionSystemContext, so this is the shape a
            // NodeManager or Method callback actually receives.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var operation = new SystemContext(telemetry);
            var context = new SessionSystemContext(telemetry);

            ISystemContext copy = context.Copy(operation);

            Assert.That(copy.GetOperationContext(), Is.SameAs(operation));
        }

        [Test]
        public void GetOperationContextReturnsTheOperationASessionSystemContextWasBuiltWith()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var operation = new SystemContext(telemetry);

            var context = new SessionSystemContext(operation, telemetry);

            Assert.That(context.GetOperationContext(), Is.SameAs(operation));
        }
    }
}
