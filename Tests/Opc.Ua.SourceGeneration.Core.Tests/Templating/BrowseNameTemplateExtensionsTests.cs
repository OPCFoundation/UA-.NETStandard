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

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Templating.Tests
{
    /// <summary>
    /// Integration tests covering the
    /// <see cref="BrowseNameTemplateExtensions.AddBrowseNameReplacement"/>
    /// helper end-to-end: the helper populates both the raw and the
    /// literal token, the literal token is safely escaped, and a
    /// <c>UASG_BROWSENAME_UNSAFE</c> warning surfaces with the expected
    /// <c>EventId</c> when the raw value contains characters that would
    /// have produced an unbalanced C# string literal.
    /// </summary>
    [TestFixture]
    [Category("Templating")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BrowseNameTemplateExtensionsTests
    {
        [Test]
        public void Helper_SafeValue_PopulatesBothTokensWithoutWarning()
        {
            // Arrange — represents what a stock OPC UA design XML would
            // produce: a plain identifier-shaped BrowseName.
            const string browseName = "Temperature";
            string output = RenderWithBrowseName(
                browseName,
                out List<LogEntry> entries);

            // Assert — both tokens substituted identically, no warning.
            Assert.That(output, Does.Contain("raw=Temperature"));
            Assert.That(output, Does.Contain("literal=\"Temperature\""));
            Assert.That(entries, Is.Empty,
                "No BrowseNameUnsafe warning is expected for safe inputs.");
        }

        [Test]
        public void Helper_QuoteValue_EscapesLiteralAndEmitsWarning()
        {
            // Arrange — third-party design XML BrowseName attribute that
            // contains an embedded double quote. Without the helper the
            // generated `case "..."` arm would be unbalanced and the
            // consuming build would fail with CS1010 / CS1003.
            const string hostile = "Set\"Point";
            string output = RenderWithBrowseName(
                hostile,
                out List<LogEntry> entries);

            // Raw token holds the verbatim value (used in identifier
            // contexts which are validated upstream).
            Assert.That(output, Does.Contain("raw=Set\"Point"));

            // Literal token has the embedded quote escaped so the
            // generated `"..."` literal stays balanced.
            Assert.That(output, Does.Contain("literal=\"Set\\\"Point\""));

            // A single MODELGEN020 / STACKGEN020 warning fired.
            Assert.That(entries, Has.Count.EqualTo(1));
            LogEntry entry = entries[0];
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.EventId.Id, Is.EqualTo(20));
            Assert.That(entry.EventId.Name, Is.EqualTo("BrowseNameUnsafe"));
            Assert.That(entry.Message, Does.Contain("Set\"Point"));
        }

        [Test]
        public void Helper_ControlCharValue_EmitsUnicodeEscape()
        {
            const string hostile = "Bad\u0001Name";
            string output = RenderWithBrowseName(
                hostile,
                out List<LogEntry> entries);

            // The control character is \u0001 in the C# literal.
            Assert.That(output, Does.Contain("literal=\"Bad\\u0001Name\""));
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].EventId.Id, Is.EqualTo(20));
        }

        [Test]
        public void Helper_BackslashValue_EmitsDoubleBackslash()
        {
            const string hostile = "weird\\path";
            string output = RenderWithBrowseName(
                hostile,
                out List<LogEntry> entries);

            Assert.That(output, Does.Contain("literal=\"weird\\\\path\""));
            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void Helper_NullValue_PopulatesEmptyTokens()
        {
            string output = RenderWithBrowseName(
                null,
                out List<LogEntry> entries);

            Assert.That(output, Does.Contain("raw="));
            Assert.That(output, Does.Contain("literal=\"\""));
            Assert.That(entries, Is.Empty);
        }

        [Test]
        public void Helper_NullLogger_DoesNotThrow()
        {
            // Ensures the helper is callable from generators that haven't
            // initialised a logger field (e.g. static TypeSourceGenerator).
            using var writer = new System.IO.StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(
                templateWriter,
                TemplateString.Parse($"[{Tokens.BrowseNameLiteral}]"));
            template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                "weird\"name",
                logger: null);
            Assert.DoesNotThrow(() => template.Render());
        }

        [Test]
        public void Generated_CaseLabel_CompilesCleanly_ForHostileBrowseName()
        {
            // End-to-end: render the actual FindChildCase template that
            // emits `case "{{Tokens.ChildBrowseNameLiteral}}":` and
            // confirm Roslyn accepts the result wrapped in a synthetic
            // switch statement. Before the fix a `"`-bearing BrowseName
            // would produce CS1010 / CS1003 here.
            const string hostile = "Embedded\"Quote";

            using var writer = new System.IO.StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(
                templateWriter,
                NodeStateTemplates.FindChildCase);
            template.AddReplacement(Tokens.ChildName, "EmbeddedQuoteChild");
            template.AddBrowseNameReplacement(
                Tokens.ChildBrowseName,
                Tokens.ChildBrowseNameLiteral,
                hostile);
            Assert.That(template.Render(), Is.True);
            string caseArm = writer.ToString();

            string program = $$"""
                class C
                {
                    public object instance;
                    public object EmbeddedQuoteChild;
                    public object CreateOrReplaceEmbeddedQuoteChild(
                        object context, object replacement)
                        => null;
                    public void Find(
                        string browseName,
                        bool createOrReplace,
                        object replacement)
                    {
                        object context = null;
                        switch (browseName)
                        {
                            {{caseArm}}
                        }
                    }
                }
                """;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(program);
            var errors = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.That(errors, Is.Empty,
                "Generated case arm did not parse cleanly: " +
                string.Join("; ", errors.Select(d => d.GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture))));

            // The switch arm must contain the escaped literal and the
            // raw value must NOT appear inside the case label as bare
            // quotes (that would indicate unbalanced emission).
            var caseLabels = tree.GetRoot().DescendantNodes()
                .OfType<CaseSwitchLabelSyntax>()
                .ToList();
            Assert.That(caseLabels, Has.Count.EqualTo(1));
            Assert.That(caseLabels[0].Value, Is.InstanceOf<LiteralExpressionSyntax>());
            var literal = (LiteralExpressionSyntax)caseLabels[0].Value;
            Assert.That(literal.Token.Value, Is.EqualTo(hostile));
        }

        private static string RenderWithBrowseName(
            string browseName,
            out List<LogEntry> entries)
        {
            entries = [];
            ILogger logger = new CapturingLogger(entries);

            using var writer = new System.IO.StringWriter();
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(
                templateWriter,
                TemplateString.Parse(
                    $"raw={Tokens.BrowseName}\nliteral=\"{Tokens.BrowseNameLiteral}\""));
            template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                browseName,
                logger);
            Assert.That(template.Render(), Is.True);
            return writer.ToString();
        }

        private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(List<LogEntry> entries)
            {
                m_entries = entries;
            }

            public System.IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                System.Exception exception,
                System.Func<TState, System.Exception, string> formatter)
            {
                m_entries.Add(new LogEntry(
                    logLevel, eventId, formatter(state, exception)));
            }

            private readonly List<LogEntry> m_entries;
        }
    }
}
