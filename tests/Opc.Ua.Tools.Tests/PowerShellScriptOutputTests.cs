// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Pins the contract of the PowerShell output normalizer the script
    /// harnesses rely on. Without it, an assertion on a script diagnostic
    /// passes on the wide Windows agents and fails on the 80-column Linux
    /// and macOS agents purely because ConciseView wrapped the message.
    /// </summary>
    [TestFixture]
    public sealed class PowerShellScriptOutputTests
    {
        [Test]
        public void RejoinsAMessageWrappedAcrossConciseViewContinuations()
        {
            // Verbatim ConciseView output from a 80-column Linux agent.
            const string wrapped =
                "Exception: /home/runner/work/x/.azurepipelines/validate-nuget-package-set.ps1:137\n" +
                "Line |\n" +
                " 137 |      throw 'Expected at least one OPCFoundation.NetStandard.Opc.Ua.Cor \u2026\n" +
                "     |      ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n" +
                "     | Expected at least one OPCFoundation.NetStandard.Opc.Ua.Core (or\n" +
                "     | .Core.Debug) package to anchor the package version, found none.\n";

            string normalized = PowerShellScriptOutput.Normalize(wrapped);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Contain("Core (or .Core.Debug)"));
                Assert.That(
                    normalized,
                    Does.Contain(
                        "Expected at least one OPCFoundation.NetStandard.Opc.Ua.Core " +
                        "(or .Core.Debug) package to anchor the package version, found none."));
            });
        }

        [Test]
        public void RejoinsAMessageWrappedMidPhrase()
        {
            const string wrapped =
                "Exception: /home/runner/work/x/validate-nuget-package-set.ps1:141\n" +
                "Line |\n" +
                " 141 |      throw (\n" +
                "     |      ~~~~~~~\n" +
                "     | OPCFoundation.NetStandard.Opc.Ua.Core and .Core.Debug disagree on the\n" +
                "     | package version: 2.0.0, 2.0.1.\n";

            string normalized = PowerShellScriptOutput.Normalize(wrapped);

            Assert.That(normalized, Does.Contain("disagree on the package version: 2.0.0, 2.0.1."));
        }

        [Test]
        public void StripsAnsiEscapeSequences()
        {
            const string coloured = "\u001b[31;1mException: \u001b[0mit failed\u001b[0m";

            Assert.That(
                PowerShellScriptOutput.Normalize(coloured),
                Is.EqualTo("Exception: it failed"));
        }

        [Test]
        public void KeepsUnwrappedOutputUnchanged()
        {
            const string plain =
                "Validated 3 package(s).\nExpected base package version '2.0.1' | not '2.0.0'.\n";

            Assert.That(PowerShellScriptOutput.Normalize(plain), Is.EqualTo(plain));
        }

        [Test]
        public void KeepsTheSourceLineOfTheConciseViewBlock()
        {
            const string block = "Line |\n 137 |      throw 'boom'\n";

            Assert.That(PowerShellScriptOutput.Normalize(block), Is.EqualTo(block));
        }
    }
}
#endif
