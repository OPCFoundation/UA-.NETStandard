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
        /// <summary>
        /// Copy retains selected templates, all translations and deferred fallback without provider aliases.
        /// </summary>
        [Test]
        public void CopyRetainsDeferredTranslationState()
        {
            object[] args = [12.5m];
            var translations = new Dictionary<string, string>
            {
                ["en-US"] = "Value: {0:N1}",
                ["de-DE"] = "Wert: {0:N1}"
            };
            var info = new TranslationInfo("quantity", "en-US", "Fallback: {0:N1}", args);
            LocalizedText source = new LocalizedText(translations, info).FilterByPreferredLocales(["de-DE"]);
            LocalizedText copy = source.Copy();
            Assert.That(copy.Locale, Is.EqualTo(source.Locale));
            Assert.That(copy.Text, Is.EqualTo("Wert: 12,5"));
            Assert.That(copy.TranslationInfo.Key, Is.EqualTo("quantity"));
            Assert.That(copy.TranslationInfo.Locale, Is.EqualTo("en-US"));
            Assert.That(copy.TranslationInfo.Text, Is.EqualTo("Fallback: {0:N1}"));
            Assert.That(copy.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.Translations, Is.Not.SameAs(translations));
            Assert.That(copy.Translations, Is.EquivalentTo(translations));
            Assert.That(copy.FilterByPreferredLocales(["mul"]).Translations, Is.EquivalentTo(translations));
            Assert.That(copy.FilterByPreferredLocales(["qst"]).Translations, Is.EquivalentTo(translations));
            Assert.That(copy.FilterByPreferredLocales(["EN-us"]).Text, Is.EqualTo("Value: 12.5"));
            Assert.That(copy.FilterByPreferredLocales(["de-AT"]).Text, Is.EqualTo("Wert: 12,5"));
            args[0] = 99m;
            translations["de-DE"] = "Changed";
            Assert.That(source.Text, Is.EqualTo("Wert: 99,0"));
            Assert.That(copy.Text, Is.EqualTo("Wert: 12,5"));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Wert: 12,5"));
            copy.TranslationInfo.Args![0] = 14m;
            Assert.That(copy.Text, Is.EqualTo("Wert: 14,0"));
            Assert.That(copy.FilterByPreferredLocales(["en-US"]).Text, Is.EqualTo("Value: 14.0"));
        }

        /// <summary>
        /// Direct dictionary lookup retains requested locale spelling and ordered fallback behavior.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyPreservesDictionaryLookupLocale(bool ignoreCase)
        {
            var translations = new Dictionary<string, string>(
                ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                ["en-US"] = "Hello",
                ["de-DE"] = "Hallo"
            };
            var source = new LocalizedText(translations);
            LocalizedText copy = source.Copy();
            Assert.That(copy.FilterByPreferredLocales(["EN-us"]).Locale,
                Is.EqualTo(source.FilterByPreferredLocales(["EN-us"]).Locale));
            Assert.That(copy.FilterByPreferredLocales(["EN-us"]).Locale,
                Is.EqualTo(ignoreCase ? "EN-us" : "en-US"));
            Assert.That(copy.FilterByPreferredLocales(["en-GB"]).Locale, Is.EqualTo("en-US"));
            Assert.That(copy.FilterByPreferredLocales(["it-IT"]).Locale, Is.EqualTo("en-US"));
            Assert.That(copy.Translations, Is.EquivalentTo(translations));
            translations["en-US"] = "Changed";
            Assert.That(copy.FilterByPreferredLocales(["EN-us"]).Text, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// Eagerly decoded SDK-owned multi-language templates retain deferred arguments without aliasing them.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyRetainsMultiLanguageTranslationInfoArguments(bool selectLocale)
        {
            const string json = "{\"t\":[[\"en-US\",\"Hello {0}\"],[\"de-DE\",\"Hallo {0}\"]]}";
            object[] args = ["Original"];
            var source = new LocalizedText(new TranslationInfo("greeting", "mul", json, args));
            if (selectLocale)
            {
                source = source.FilterByPreferredLocales(["de-DE"]);
            }
            LocalizedText copy = source.Copy();
            Assert.That(copy.Locale, Is.EqualTo(source.Locale));
            Assert.That(copy.Text, Is.EqualTo(source.Text));
            Assert.That(copy.TranslationInfo.Key, Is.EqualTo("greeting"));
            Assert.That(copy.TranslationInfo.Locale, Is.EqualTo("mul"));
            Assert.That(copy.TranslationInfo.Text, Is.EqualTo(json));
            Assert.That(source.Translations, Is.Not.Null);
            Assert.That(copy.Translations, Is.EquivalentTo(source.Translations!));
            Assert.That(copy.Translations, Is.Not.SameAs(source.Translations));
            Assert.That(copy.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.FilterByPreferredLocales(["EN-us"]).Locale,
                Is.EqualTo(source.FilterByPreferredLocales(["EN-us"]).Locale));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Original"));
            args[0] = "Changed";
            Assert.That(source.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Changed"));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Original"));
            Assert.That(copy.Copy().FilterByPreferredLocales(["en-US"]).Text, Is.EqualTo("Hello Original"));
        }

        /// <summary>
        /// Replacing deferred information retains only the provenance of the forwarded dictionary.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyRetainsMultiLanguageWithTranslationInfoArguments(bool selectLocale)
        {
            const string json = "{\"t\":[[\"en-US\",\"Hello {0}\"],[\"de-DE\",\"Hallo {0}\"]]}";
            object[] args = ["Original"];
            var decoded = new LocalizedText("mul", json);
            if (selectLocale)
            {
                decoded = decoded.FilterByPreferredLocales(["de-DE"]);
            }
            LocalizedText source = decoded.WithTranslationInfo(
                new TranslationInfo("greeting", "en-US", "Fallback {0}", args));
            Assert.That(source.Translations, Is.SameAs(decoded.Translations));
            LocalizedText copy = source.Copy();
            Assert.That(copy.Locale, Is.EqualTo(source.Locale));
            Assert.That(copy.Text, Is.EqualTo(source.Text));
            Assert.That(copy.TranslationInfo.Key, Is.EqualTo("greeting"));
            Assert.That(copy.TranslationInfo.Locale, Is.EqualTo("en-US"));
            Assert.That(copy.TranslationInfo.Text, Is.EqualTo("Fallback {0}"));
            Assert.That(copy.Translations, Is.EquivalentTo(source.Translations));
            Assert.That(copy.Translations, Is.Not.SameAs(source.Translations));
            Assert.That(copy.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Original"));
            args[0] = "Changed";
            Assert.That(source.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Changed"));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo Original"));
            Assert.That(copy.WithTranslationInfo(copy.TranslationInfo).Copy()
                .FilterByPreferredLocales(["en-US"]).Text, Is.EqualTo("Hello Original"));
        }

        /// <summary>
        /// Unknown dictionary lookup and custom comparer behavior are rejected instead of changed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyRejectsUnsupportedDictionaryLookup(bool customComparer)
        {
            var translations = new Dictionary<string, string>(
                customComparer ? StringComparer.InvariantCultureIgnoreCase : StringComparer.Ordinal)
            {
                ["en-US"] = "Hello"
            };
            IReadOnlyDictionary<string, string> dictionary = customComparer
                ? translations
                : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(translations);
            var source = new LocalizedText(dictionary);
            Assert.That(() => source.Copy(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            LocalizedText replaced = source.WithTranslationInfo(
                new TranslationInfo("greeting", "en-US", "Hello"));
            Assert.That(() => replaced.Copy(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(source.FilterByPreferredLocales(["en-US"]).Text, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// Null, empty, encoded multi-language and malformed multi-language values retain behavior.
        /// </summary>
        [TestCase(null, null)]
        [TestCase("", "")]
        [TestCase("en-US", "")]
        [TestCase("en-US", null)]
        [TestCase("mul", "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}")]
        [TestCase("mul", "{malformed")]
        public void CopyRetainsNullEmptyAndMultiLanguage(string? locale, string? text)
        {
            var source = new LocalizedText(locale, text);
            LocalizedText copy = source.Copy();
            Assert.That(copy.IsNull, Is.EqualTo(source.IsNull));
            Assert.That(copy.Locale, Is.EqualTo(source.Locale));
            Assert.That(copy.Text, Is.EqualTo(source.Text));
            Assert.That(copy.FilterByPreferredLocales(["de-DE"]), Is.EqualTo(
                source.FilterByPreferredLocales(["de-DE"])));
            Assert.That(copy.AsMultiLanguage(), Is.EqualTo(source.AsMultiLanguage()));
            LocalizedText filtered = source.FilterByPreferredLocales(["de-DE"]);
            Assert.That(filtered.Copy(), Is.EqualTo(filtered));
        }

        /// <summary>
        /// Copy preserves invalid-format fallback rather than materializing a new representation.
        /// </summary>
        [TestCase("Value: {0}", "unknown-locale")]
        [TestCase("Value: {1}", "en-US")]
        [TestCase("", "en-US")]
        public void CopyRetainsFormattingFallback(string template, string locale)
        {
            var source = new LocalizedText(locale, template,
                new TranslationInfo("key", "en-US", "Fallback", "argument"));
            LocalizedText copy = source.Copy();
            Assert.That(copy.Text, Is.EqualTo(source.Text));
            Assert.That(copy.Locale, Is.EqualTo(source.Locale));
            Assert.That(copy.TranslationInfo.Text, Is.EqualTo("Fallback"));
            Assert.That(copy.TranslationInfo.Key, Is.EqualTo("key"));
        }

        /// <summary>
        /// Copy accepts only the documented immutable argument types without converting their values.
        /// </summary>
        [Test]
        public void CopyRetainsSupportedImmutableArguments()
        {
            object[] args =
            [
                null!, "text", true, 'a', (sbyte)1, (byte)2, (short)3, (ushort)4, 5, 6u, 7L, 8UL,
                9f, 10d, 11m, new DateTime(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                TimeSpan.FromSeconds(1), Guid.Empty
            ];
            var source = new LocalizedText("key", "en-US", "{1}", args);
            LocalizedText copy = source.Copy();
            Assert.That(copy.TranslationInfo.Args, Is.EqualTo(args));
            Assert.That(copy.TranslationInfo.Args, Is.Not.SameAs(args));
            args[1] = "changed";
            Assert.That(copy.Text, Is.EqualTo("text"));
        }

        /// <summary>
        /// Unsupported formatting arguments are rejected without executing cloning or formatting code.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyRejectsUnknownArgumentWithoutCallingUserCode(bool multiLanguage)
        {
            var argument = new Moq.Mock<ICloneable>();
            argument.Setup(value => value.Clone()).Throws(new InvalidOperationException("Clone invoked"));
            argument.Setup(value => value.ToString()).Throws(new InvalidOperationException("ToString invoked"));
            var source = multiLanguage
                ? new LocalizedText(new TranslationInfo("key", "mul",
                    "{\"t\":[[\"en-US\",\"Hello {0}\"]]}", argument.Object))
                : new LocalizedText("key", "en-US", "{0}", argument.Object);
            Assert.That(() => source.Copy(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            argument.Verify(value => value.Clone(), Moq.Times.Never);
            argument.Verify(value => value.ToString(), Moq.Times.Never);
        }

        /// <summary>
        /// Mutable built-in containers are not accepted as deferred formatting arguments.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CopyRejectsMutableFormattingContainers(bool array)
        {
            object argument = array ? new byte[] { 1, 2 } : new System.Text.StringBuilder("Original");
            var source = new LocalizedText("key", "en-US", "{0}", argument);
            Assert.That(() => source.Copy(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

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
        public void NullAndEmptyLocaleHashAlike()
        {
            var a = new LocalizedText(null, "Hi");
            var b = new LocalizedText(string.Empty, "Hi");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(new HashSet<LocalizedText> { a, b }, Has.Count.EqualTo(1));
        }

        [TestCase("{}")]
        [TestCase("{\"t\":[]}")]
        [TestCase("{\"t\":[[\"\",\"x\"]]}")]
        [TestCase("{\"t\":[[\"en\"]]}")]
        public void MulLocaleWithoutTranslationsDoesNotThrow(string text)
        {
            // Every decoder creates wire LocalizedText values this way; an
            // ArgumentException here failed the whole message.
            LocalizedText value = default;
            Assert.DoesNotThrow(() => value = new LocalizedText("mul", text));
            Assert.That(value.Locale, Is.EqualTo("mul"));
            Assert.That(value.Text, Is.EqualTo(text));
        }

        [TestCase("x")]
        [TestCase("  [\"t\"]")]
        [TestCase("{\"t\":")]
        [TestCase("{}")]
        public void MalformedMulLocaleBehavesAsPlainText(string text)
        {
            // The "mul" JSON is decoded on first use, not when the value is
            // created; malformed text must still behave as plain text.
            var value = new LocalizedText("mul", text);

            Assert.Multiple(() =>
            {
                Assert.That(value.IsNullOrEmpty, Is.False);
                Assert.That(value.Translations, Is.Null);
                Assert.That(value.TranslationInfo.IsNull, Is.True);
                Assert.That(value.AsMultiLanguage(), Is.EqualTo(value));
                Assert.That(value.FilterByPreferredLocales(["mul"]), Is.EqualTo(value));
                Assert.That(value.FilterByPreferredLocales(["de-DE"]), Is.EqualTo(value));
            });
        }

        [Test]
        public void DecodedMulLocaleResolvesTranslationsOnFirstUse()
        {
            const string text = "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}";
            var value = new LocalizedText("mul", text);
            LocalizedText copy = value;

            Assert.Multiple(() =>
            {
                Assert.That(value.Text, Is.EqualTo(text));
                Assert.That(value.Translations, Has.Count.EqualTo(2));
                Assert.That(copy.Translations, Is.SameAs(value.Translations));
                Assert.That(value.TranslationInfo.Locale, Is.EqualTo("en-US"));
                Assert.That(value.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Hallo"));
            });
        }

        [Test]
        public void FilteringSkipsNullAndEmptyLocaleIds()
        {
            var original = new LocalizedText(new Dictionary<string, string>
            {
                ["en-US"] = "Hello",
                ["de-DE"] = "Hallo"
            }, default);

            LocalizedText german = original.FilterByPreferredLocales([null!, string.Empty, "de-DE"]);
            LocalizedText first = original.FilterByPreferredLocales([null!]);
            LocalizedText multi = original.FilterByPreferredLocales(["mul", null!, "de-DE", "de-DE"]);

            Assert.Multiple(() =>
            {
                Assert.That(german.Locale, Is.EqualTo("de-DE"));
                Assert.That(first.IsNullOrEmpty, Is.False);
                Assert.That(multi.Translations, Has.Count.EqualTo(1));
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
            Assert.That(selected.Text, Is.Null);
            Assert.That(selected.TranslationInfo, Is.EqualTo(fallback));
        }

        [Test]
        public void FilterByPreferredLocalesMatchesLocaleCaseInsensitively()
        {
            // RFC 5646 language tags are case-insensitive: "DE-de" is "de-DE",
            // not just any German region.
            var text = new LocalizedText(new Dictionary<string, string>
            {
                { "de-AT", "Servus" },
                { "de-DE", "Hallo" }
            });

            LocalizedText selected = text.FilterByPreferredLocales(["DE-de"]);

            Assert.Multiple(() =>
            {
                Assert.That(selected.Locale, Is.EqualTo("de-DE"));
                Assert.That(selected.Text, Is.EqualTo("Hallo"));
            });
        }

        [Test]
        public void FilterByPreferredLocalesMulMatchesLocaleCaseInsensitively()
        {
            var text = new LocalizedText(new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" }
            });

            LocalizedText selected = text.FilterByPreferredLocales(["mul", "DE-DE", "FR-fr"]);

            Assert.That(selected.IsMultiLanguage, Is.True);
            Assert.That(selected.Translations, Is.EquivalentTo(new Dictionary<string, string>
            {
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" }
            }));
        }

        [Test]
        public void FilterByPreferredLocalesMulFallsBackToRegionOfSameLanguage()
        {
            var text = new LocalizedText(new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" }
            });

            LocalizedText selected = text.FilterByPreferredLocales(["mul", "de-AT", "fr-CA"]);

            // each text keeps the locale it is written in.
            Assert.That(selected.IsMultiLanguage, Is.True);
            Assert.That(selected.Translations, Is.EquivalentTo(new Dictionary<string, string>
            {
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" }
            }));
        }
    }
}
