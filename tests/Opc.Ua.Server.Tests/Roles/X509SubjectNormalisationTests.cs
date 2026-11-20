/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the canonical form used to match certificate subjects against
    /// X509Subject identity mapping rules (Part 18 §4.4.3).
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class X509SubjectNormalisationTests
    {
        [TestCase("CN=alice, O=Contoso", "CN=\"alice\"/O=\"Contoso\"")]
        [TestCase("O=Contoso, CN=\"alice, bob\"", "CN=\"alice, bob\"/O=\"Contoso\"")]
        [TestCase("CN=\"alice\", O=Contoso", "CN=\"alice\"/O=\"Contoso\"")]
        public void NormaliseX509SubjectBuildsCanonicalForm(string subject, string expected)
        {
            Assert.That(IdentityRuleValidator.NormaliseX509Subject(subject), Is.EqualTo(expected));
        }

        /// <summary>
        /// A value with an embedded (doubled) quote must not be truncated at the
        /// quote into the subject of another user.
        /// </summary>
        [TestCase("CN=\"alice\"\"x\"")]
        [TestCase("CN=\"alice\"\"x\", O=Contoso")]
        [TestCase("CN=\"alice\"x, O=Contoso")]
        [TestCase("CN=\"alice")]
        [TestCase("CN=al\"ice")]
        public void NormaliseX509SubjectRejectsValuesContainingQuotes(string subject)
        {
            Assert.That(IdentityRuleValidator.NormaliseX509Subject(subject), Is.Empty);
        }
    }
}
