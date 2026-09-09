/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    [Parallelizable]
    public sealed class WotInvokeResultTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void InvocationCopiesRetainContextOperationAndOwnedInputDetails(bool contextFirst)
        {
            var context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            var operation = new ServiceResult(
                "urn:source", new StatusCode(StatusCodes.BadInvalidArgument.Code, "CannotRun"),
                new LocalizedText("en", "Cannot run"), "operation detail", innerResult: null);
            var inputError = new ServiceResult(
                "urn:source", new StatusCode(StatusCodes.BadOutOfRange.Code, "OutsideRange"),
                new LocalizedText("de", "Zu gross"), "input detail", innerResult: null);
            ServiceResult[] inputs = [ServiceResult.Good, inputError];
            var original = new WotInvokeResult(StatusCodes.BadInvalidArgument, error: "call detail");

            WotInvokeResult copy = contextFirst
                ? original.WithContext(context).WithResultDetails(operation, inputs)
                : original.WithResultDetails(operation, inputs).WithContext(context);
            inputs[1] = null!;

            Assert.That(copy.Context, Is.SameAs(context));
            Assert.That(copy.OperationResult, Is.SameAs(operation));
            Assert.That(copy.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(copy.Error, Is.EqualTo("call detail"));
            Assert.That(copy.InputArgumentResults.Count, Is.EqualTo(2));
            Assert.That(copy.InputArgumentResults[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(copy.InputArgumentResults[1], Is.SameAs(inputError));
            Assert.That(original.Context, Is.Null);
            Assert.That(original.InputArgumentResults.IsEmpty, Is.True);
        }

        [Test]
        public void ExistingInvocationErrorsRemainAvailableToMethodDiagnosticEncoding()
        {
            var result = new WotInvokeResult(StatusCodes.BadNotSupported, error: "Selected source cannot run.");

            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.OperationResult.LocalizedText.Text, Is.EqualTo("Selected source cannot run."));
        }

        [Test]
        public void InvalidInvocationDetailsCannotReplaceTheDeclaredStatusOrLosePositions()
        {
            var result = new WotInvokeResult(StatusCodes.BadInvalidArgument);
            Assert.Throws<ArgumentNullException>(() => result.WithResultDetails(null!, []));
            Assert.Throws<ArgumentException>(() => result.WithResultDetails(ServiceResult.Good, []));
            Assert.Throws<ArgumentException>(() => result.WithResultDetails(
                result.OperationResult, [ServiceResult.Good, null!]));
            Assert.Throws<ArgumentNullException>(() => result.WithContext(null!));
            Assert.That(result.OperationResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.InputArgumentResults.IsEmpty, Is.True);
        }

        [Test]
        public void InvocationDetailsMustMatchTheCompleteStatusIncludingInformationBits()
        {
            var result = new WotInvokeResult(StatusCodes.Good.SetSemanticsChanged(true));

            Assert.Throws<ArgumentException>(() => result.WithResultDetails(ServiceResult.Good, []));
        }
    }
}
