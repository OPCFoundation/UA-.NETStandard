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

using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for the logger-to-diagnostic bridge in
    /// <see cref="SourceGeneratorTelemetry"/> / <see cref="SourceGenerator"/>.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SourceGeneratorTelemetryTests
    {
        /// <summary>
        /// Documents the underlying bug: reporting an already-formatted message
        /// directly against a descriptor whose format declares more placeholders
        /// than arguments supplied yields the raw, unsubstituted template.
        /// </summary>
        [Test]
        public void DirectReportAgainstMultiPlaceholderDescriptorLosesMessage()
        {
            Diagnostic diagnostic = Diagnostic.Create(
                SourceGenerator.Exception, Location.None, "boom");

            string rendered = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            // The single argument cannot satisfy the '{0}': '{1}' format, so the
            // real message is dropped and the placeholders leak through verbatim.
            Assert.That(rendered, Does.Not.Contain("boom"));
            Assert.That(rendered, Does.Contain("{1}"));
        }

        /// <summary>
        /// Verifies the fix: an already-formatted message is rendered verbatim
        /// under the correct diagnostic identity regardless of the looked-up
        /// descriptor's placeholder arity.
        /// </summary>
        [Test]
        public void CreateFormattedDiagnosticRendersMessageVerbatim()
        {
            Diagnostic diagnostic = SourceGenerator.CreateFormattedDiagnostic(
                SourceGenerator.Exception, "boom");

            string rendered = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            Assert.That(rendered, Is.EqualTo("boom"));
            Assert.That(rendered, Does.Not.Contain("{0}"));
            Assert.That(rendered, Does.Not.Contain("{1}"));
        }

        /// <summary>
        /// The passthrough descriptor preserves the looked-up descriptor's
        /// identity and metadata (id, severity, category, help link, tags).
        /// </summary>
        [Test]
        public void CreateFormattedDiagnosticPreservesDescriptorMetadata()
        {
            DiagnosticDescriptor source = SourceGenerator.Exception;

            Diagnostic diagnostic = SourceGenerator.CreateFormattedDiagnostic(
                source, "boom");
            DiagnosticDescriptor result = diagnostic.Descriptor;

            Assert.That(result.Id, Is.EqualTo(source.Id));
            Assert.That(result.Id, Is.EqualTo("MODELGEN003"));
            Assert.That(diagnostic.Severity, Is.EqualTo(source.DefaultSeverity));
            Assert.That(result.Category, Is.EqualTo(source.Category));
            Assert.That(result.HelpLinkUri, Is.EqualTo(source.HelpLinkUri));
            Assert.That(result.IsEnabledByDefault, Is.EqualTo(source.IsEnabledByDefault));
            Assert.That(result.CustomTags, Is.EquivalentTo(source.CustomTags.ToArray()));
        }

        /// <summary>
        /// The formatted message is emitted safely even when it contains brace
        /// characters that would otherwise be interpreted as format placeholders.
        /// </summary>
        [Test]
        public void CreateFormattedDiagnosticIsSafeForBracesInMessage()
        {
            const string message = "value {0} was not {closed";

            Diagnostic diagnostic = SourceGenerator.CreateFormattedDiagnostic(
                SourceGenerator.GenericError, message);

            string rendered = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            Assert.That(rendered, Is.EqualTo(message));
        }
    }
}
