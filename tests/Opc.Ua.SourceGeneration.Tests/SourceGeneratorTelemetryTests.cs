/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            Assert.That(result.Description, Is.EqualTo(source.Description));
            Assert.That(result.HelpLinkUri, Is.EqualTo(source.HelpLinkUri));
            Assert.That(result.IsEnabledByDefault, Is.EqualTo(source.IsEnabledByDefault));
            Assert.That(result.CustomTags, Is.EquivalentTo(source.CustomTags.ToArray()));
        }

        /// <summary>
        /// Regression: the generator's logger reports Critical as enabled, but
        /// the level was not mapped to any descriptor, so every LogCritical
        /// message - including "Could not parse NodeSet" - was silently dropped.
        /// </summary>
        [Test]
        public void CriticalLogLevelMapsToADiagnostic()
        {
            bool mapped = SourceGenerator.TryGetDiagnostic(
                Microsoft.Extensions.Logging.LogLevel.Critical,
                default,
                out DiagnosticDescriptor descriptor);

            Assert.That(mapped, Is.True, "Critical must reach the build as a diagnostic");
            Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
        }

        /// <summary>
        /// Levels below Warning still map to nothing - they are diagnostics-only
        /// tracing, and the logger reports them as disabled.
        /// </summary>
        [TestCase(Microsoft.Extensions.Logging.LogLevel.Trace)]
        [TestCase(Microsoft.Extensions.Logging.LogLevel.Debug)]
        [TestCase(Microsoft.Extensions.Logging.LogLevel.Information)]
        public void SubWarningLogLevelsMapToNothing(
            Microsoft.Extensions.Logging.LogLevel level)
        {
            Assert.That(
                SourceGenerator.TryGetDiagnostic(level, default, out _),
                Is.False);
        }

        /// <summary>
        /// Regression: MODELGEN003 declares two placeholders, so reporting it
        /// with a single argument rendered the raw template. The [DataType]
        /// paths now pass the annotated type name and the message separately.
        /// </summary>
        [Test]
        public void ExceptionDescriptorWithBothArgumentsRendersTheMessage()
        {
            Diagnostic diagnostic = Diagnostic.Create(
                SourceGenerator.Exception,
                Location.None,
                "TestApp.Config.ServerConfig",
                "InvalidOperationException: boom");

            string rendered = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            Assert.That(rendered, Does.Contain("TestApp.Config.ServerConfig"));
            Assert.That(rendered, Does.Contain("boom"));
            Assert.That(rendered, Does.Not.Contain("{0}"));
            Assert.That(rendered, Does.Not.Contain("{1}"));
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
