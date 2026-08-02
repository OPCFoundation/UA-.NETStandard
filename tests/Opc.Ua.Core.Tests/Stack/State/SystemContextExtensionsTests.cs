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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for <see cref="SystemContextExtensions"/>, which is how a callback hands
    /// the operation it is serving to an API that has to know which operation invoked it.
    /// </summary>
    [TestFixture]
    [Category("SystemContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SystemContextExtensionsTests
    {
        [Test]
        public void GetOperationContextThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(
                () => SystemContextExtensions.GetOperationContext(null),
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
