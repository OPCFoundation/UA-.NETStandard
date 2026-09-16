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

using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests
{
    /// <summary>
    /// Covers selected display text that retains a different original translation fallback.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class LocalizedTextTranslationTests
    {
        [Test]
        public void SelectedTextRetainsOriginalFallback()
        {
            var fallback = new TranslationInfo("greeting", "en-US", "Hello");

            var selected = new LocalizedText("de-DE", "Hallo", fallback);

            Assert.Multiple(() =>
            {
                Assert.That(selected.Locale, Is.EqualTo("de-DE"));
                Assert.That(selected.Text, Is.EqualTo("Hallo"));
                Assert.That(selected.TranslationInfo, Is.EqualTo(fallback));
            });
        }

        [Test]
        public void SelectedTemplateUsesSelectedCultureAndRetainsFallbackArguments()
        {
            var fallback = new TranslationInfo("measurement", "en-US", "Value {0:N1}", 1234.5);

            var selected = new LocalizedText("de-DE", "Messwert {0:N1}", fallback);

            Assert.Multiple(() =>
            {
                Assert.That(selected.Locale, Is.EqualTo("de-DE"));
                Assert.That(selected.Text, Is.EqualTo("Messwert 1.234,5"));
                Assert.That(selected.TranslationInfo, Is.EqualTo(fallback));
                Assert.That(selected.TranslationInfo.Args, Is.SameAs(fallback.Args));
                Assert.That(new LocalizedText(fallback).Text, Is.EqualTo("Value 1,234.5"));
            });
        }

        [Test]
        public void FilteringRetainsFallbackAcrossLocaleSelections()
        {
            var fallback = new TranslationInfo("greeting", "en-US", "Hello {0}", "User");
            var original = new LocalizedText(new Dictionary<string, string>
            {
                ["en-US"] = "Hello {0}",
                ["de-DE"] = "Hallo {0}",
                ["fr-FR"] = "Bonjour {0}"
            }, fallback);

            LocalizedText german = original.FilterByPreferredLocales(["de-DE"]);
            LocalizedText french = german.FilterByPreferredLocales(["fr-FR"]);

            Assert.Multiple(() =>
            {
                Assert.That(german.Text, Is.EqualTo("Hallo User"));
                Assert.That(german.Locale, Is.EqualTo("de-DE"));
                Assert.That(german.TranslationInfo, Is.EqualTo(fallback));
                Assert.That(french.Text, Is.EqualTo("Bonjour User"));
                Assert.That(french.Locale, Is.EqualTo("fr-FR"));
                Assert.That(french.TranslationInfo, Is.EqualTo(fallback));
                Assert.That(original.Text, Is.EqualTo("Hello User"));
                Assert.That(original.Translations, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void SelectedTextKeepsItsDisplayWhenConvertedToMultiLanguage()
        {
            var fallback = new TranslationInfo("greeting", "en-US", "Hello {0}", "User");
            var selected = new LocalizedText("de-DE", "Hallo {0}", fallback);

            LocalizedText result = selected.AsMultiLanguage();

            Assert.Multiple(() =>
            {
                Assert.That(result.Locale, Is.EqualTo("de-DE"));
                Assert.That(result.Text, Is.EqualTo("Hallo User"));
                Assert.That(result.TranslationInfo, Is.EqualTo(fallback));
            });
        }

        [Test]
        public void SingleTranslationFormatsBracesExactlyOnce()
        {
            var fallback = new TranslationInfo("braces", "en-US", "Fallback {0}", "0");
            var original = new LocalizedText(new Dictionary<string, string>
            {
                ["de-DE"] = "{{{0}}}"
            }, fallback);

            LocalizedText result = original.AsMultiLanguage();

            Assert.That(result.Locale, Is.EqualTo("de-DE"));
            Assert.That(result.Text, Is.EqualTo("{0}"));
            Assert.That(result.TranslationInfo, Is.EqualTo(fallback));
        }

        [TestCase("de-DE", "de-DE", "Hallo")]
        [TestCase("de-AT", "de-DE", "Hallo")]
        [TestCase("ja-JP", "en-US", "Hello")]
        public void FilteringRetainsMetadataForExactRegionalAndMissingLocales(
            string requestedLocale,
            string expectedLocale,
            string expectedText)
        {
            var fallback = new TranslationInfo("greeting", "en-US", "Hello");
            var original = new LocalizedText(new Dictionary<string, string>
            {
                ["en-US"] = "Hello",
                ["de-DE"] = "Hallo"
            }, fallback);

            LocalizedText selected = original.FilterByPreferredLocales([requestedLocale]);

            Assert.That(selected.Locale, Is.EqualTo(expectedLocale));
            Assert.That(selected.Text, Is.EqualTo(expectedText));
            Assert.That(selected.TranslationInfo, Is.EqualTo(fallback));
            Assert.That(selected.Translations, Is.SameAs(original.Translations));
        }

        [TestCase("mul")]
        [TestCase("qst")]
        public void MultiLanguageSelectionKeepsFallbackAndFormatsEachLocale(string requestedLocale)
        {
            var fallback = new TranslationInfo("measurement", "en-US", "Value {0:N1}", 1234.5);
            var original = new LocalizedText(new Dictionary<string, string>
            {
                ["en-US"] = "Value {0:N1}",
                ["de-DE"] = "Messwert {0:N1}"
            }, fallback);

            LocalizedText multiple = original.FilterByPreferredLocales([requestedLocale, "de-DE", "en-US"]);
            LocalizedText german = multiple.FilterByPreferredLocales(["de-DE"]);

            Assert.Multiple(() =>
            {
                Assert.That(multiple.IsMultiLanguage, Is.True);
                Assert.That(multiple.Translations, Has.Count.EqualTo(2));
                Assert.That(multiple.Text, Does.Contain("[\"de-DE\",\"Messwert 1.234,5\"]"));
                Assert.That(multiple.Text, Does.Contain("[\"en-US\",\"Value 1,234.5\"]"));
                Assert.That(multiple.TranslationInfo, Is.EqualTo(fallback));
                Assert.That(german.Text, Is.EqualTo("Messwert 1.234,5"));
                Assert.That(german.Locale, Is.EqualTo("de-DE"));
                Assert.That(german.TranslationInfo, Is.EqualTo(fallback));
            });
        }

        [Test]
        public void EmptySelectedTextDoesNotBecomeFallbackText()
        {
            var fallback = new TranslationInfo("greeting", "en-US", "Hello");

            var selected = new LocalizedText("de-DE", string.Empty, fallback);

            Assert.That(selected.Locale, Is.EqualTo("de-DE"));
            Assert.That(selected.Text, Is.Empty);
            Assert.That(selected.TranslationInfo, Is.EqualTo(fallback));
        }
    }
}
