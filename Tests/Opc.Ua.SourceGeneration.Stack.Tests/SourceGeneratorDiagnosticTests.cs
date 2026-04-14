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

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SourceGeneratorDiagnosticTests
    {
        [Test]
        public void TryGetDiagnosticWithEventId1ReturnsGenericError()
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                LogLevel.Information,
                new EventId(1),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.True);
            Assert.That(descriptor, Is.EqualTo(SourceGenerator.GenericError));
        }

        [Test]
        public void TryGetDiagnosticWithEventId2ReturnsGenericWarning()
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                LogLevel.Information,
                new EventId(2),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.True);
            Assert.That(descriptor, Is.EqualTo(SourceGenerator.GenericWarning));
        }

        [Test]
        public void TryGetDiagnosticWithEventId3ReturnsException()
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                LogLevel.Information,
                new EventId(3),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.True);
            Assert.That(descriptor, Is.EqualTo(SourceGenerator.Exception));
        }

        [Test]
        public void TryGetDiagnosticWithUnknownEventIdAndErrorLogLevelReturnsGenericError()
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                LogLevel.Error,
                new EventId(99),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.True);
            Assert.That(descriptor, Is.EqualTo(SourceGenerator.GenericError));
        }

        [Test]
        public void TryGetDiagnosticWithUnknownEventIdAndWarningLogLevelReturnsGenericWarning()
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                LogLevel.Warning,
                new EventId(99),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.True);
            Assert.That(descriptor, Is.EqualTo(SourceGenerator.GenericWarning));
        }

        [TestCase(Microsoft.Extensions.Logging.LogLevel.Information)]
        [TestCase(Microsoft.Extensions.Logging.LogLevel.Debug)]
        [TestCase(Microsoft.Extensions.Logging.LogLevel.Trace)]
        [TestCase(Microsoft.Extensions.Logging.LogLevel.None)]
        public void TryGetDiagnosticWithUnknownEventIdAndNonErrorLogLevelReturnsFalse(
            Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            bool result = SourceGenerator.TryGetDiagnostic(
                logLevel,
                new EventId(99),
                out DiagnosticDescriptor descriptor);

            Assert.That(result, Is.False);
            Assert.That(descriptor, Is.Null);
        }

        [Test]
        public void GenericErrorDescriptorHasExpectedProperties()
        {
            Assert.That(SourceGenerator.GenericError.Id, Is.EqualTo("STACKGEN001"));
            Assert.That(SourceGenerator.GenericError.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(SourceGenerator.GenericError.IsEnabledByDefault, Is.True);
            Assert.That(SourceGenerator.GenericError.Category, Is.EqualTo(SourceGenerator.Name));
        }

        [Test]
        public void GenericWarningDescriptorHasExpectedProperties()
        {
            Assert.That(SourceGenerator.GenericWarning.Id, Is.EqualTo("STACKGEN002"));
            Assert.That(SourceGenerator.GenericWarning.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(SourceGenerator.GenericWarning.IsEnabledByDefault, Is.True);
        }

        [Test]
        public void ExceptionDescriptorHasExpectedProperties()
        {
            Assert.That(SourceGenerator.Exception.Id, Is.EqualTo("STACKGEN003"));
            Assert.That(SourceGenerator.Exception.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(SourceGenerator.Exception.IsEnabledByDefault, Is.True);
        }

        [Test]
        public void SourceGeneratorNameIsStackSourceGenerator()
        {
            Assert.That(SourceGenerator.Name, Is.EqualTo(nameof(StackSourceGenerator)));
        }
    }
}
