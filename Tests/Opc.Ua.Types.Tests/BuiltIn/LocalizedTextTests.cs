using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the LocalizedText Class.
    /// </summary>
    [TestFixture]
    [Category("BuiltIn")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class LocalizedTextTests
    {
        [Test]
        public void MultiLanguageDictionaryCreatesCorrectMulLocale()
        {
            // Arrange
            var translations = new Dictionary<string, string> {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" } };

            // Act
            LocalizedText localizedText = new LocalizedText(translations).AsMultiLanguage();

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(localizedText.Locale, Is.EqualTo("mul"), "Locale should be 'mul'");
            Assert.That(localizedText.Text, Is.Not.Null, "Text should not be null");
            Assert.That(
                localizedText.Translations,
                Has.Count.EqualTo(2),
                "Translations should have 2 entries");
            Assert.That(localizedText.Translations["en-US"], Is.EqualTo("Hello"));
            Assert.That(localizedText.Translations["de-DE"], Is.EqualTo("Hallo"));

            const string expectedJson = /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}";
            Assert.That(localizedText.Text, Is.EqualTo(expectedJson));
        }

        [Test]
        public void EmptyDictionaryCreatesNullText()
        {
            // Arrange
            var translations = new Dictionary<string, string>();

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.False, "Should not be mul locale");
            Assert.That(
                localizedText.Translations,
                Is.Null,
                "Translations should be null for empty dictionary");
            Assert.That(localizedText.Text, Is.Null, "Text should be null for empty dictionary");
            Assert.That(localizedText.Locale, Is.Null, "Locale should be null for empty dictionary");
        }

        [Test]
        public void SingleLocaleDictionaryCreatesLocaleAndTextDirectly()
        {
            // Arrange
            var translations = new Dictionary<string, string> { { "fr-FR", "Bonjour" } };

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.False, "Should not be mul locale for single entry");
            Assert.That(localizedText.Locale, Is.EqualTo("fr-FR"), "Locale should be 'fr-FR'");
            Assert.That(localizedText.Text, Is.EqualTo("Bonjour"), "Text should be 'Bonjour'");
            Assert.That(
                localizedText.Translations,
                Has.Count.EqualTo(1),
                "Translations should have 1 entry");
            Assert.That(localizedText.Translations["fr-FR"], Is.EqualTo("Bonjour"));
        }

        [Test]
        public void LocalizedText_MulLocale_WithThreeTranslations_ParsesCorrectly()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(localizedText.Locale, Is.EqualTo("mul"), "Locale should be 'mul'");
            Assert.That(
                localizedText.Text,
                Is.EqualTo(jsonText),
                "Text should match the input JSON exactly");
            Assert.That(localizedText.Translations, Is.Not.Null, "Translations should not be null");
            Assert.That(
                localizedText.Translations,
                Has.Count.EqualTo(3),
                "Translations should have 3 entries");
            Assert.That(localizedText.Translations["en-US"], Is.EqualTo("Hello"));
            Assert.That(localizedText.Translations["de-DE"], Is.EqualTo("Hallo"));
            Assert.That(localizedText.Translations["fr-FR"], Is.EqualTo("Bonjour"));
        }

        private static readonly string[] s_preferredLocales = ["en-US", "de-DE"];
        private static readonly string[] s_preferredLocalesArray = ["en-GB", "de-DE"];
        private static readonly string[] s_preferredLocalesArray0 = ["en-GB"];
        private static readonly string[] s_preferredLocalesArray1 = ["mul", "en-GB"];
        private static readonly string[] s_preferredLocalesArray2 = ["mul"];
        private static readonly string[] s_preferredLocalesArray3 = ["fr-FR"];
        private static readonly string[] s_preferredLocalesArray4 = ["en-GB", "en-US"];
        private static readonly string[] s_preferredLocalesArray5 = ["mul", "fr-FR"];

        [Test]
        public void LocalizedText_MulLocale_ReturnsPreferredTranslations()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.True, "Should be mul locale");

            //found locale returned
            LocalizedText singleUS = localizedText.FilterByPreferredLocales(s_preferredLocales);
            Assert.That(singleUS.Locale, Is.EqualTo("en-US"), "Locale should be 'en-US'");
            Assert.That(singleUS.Text, Is.EqualTo("Hello"), "Text should be 'Hello'");

            //found locale returned
            LocalizedText singleDE = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray);
            Assert.That(singleDE.Locale, Is.EqualTo("de-DE"), "Locale should be 'de-DE'");
            Assert.That(singleDE.Text, Is.EqualTo("Hallo"), "Text should be 'Hallo'");

            //first locale returned
            LocalizedText singleFR = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray0);
            Assert.That(singleFR.Locale, Is.EqualTo("en-US"), "Locale should be 'en-US'");
            Assert.That(singleFR.Text, Is.EqualTo("Hello"), "Text should be 'Hello'");

            // All locales returned
            LocalizedText mul = localizedText.FilterByPreferredLocales(s_preferredLocalesArray2);
            Assert.That(mul.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(mul.Translations, Has.Count.EqualTo(3), "Translations should have 3 entries");
            Assert.That(mul.Translations["en-US"], Is.EqualTo("Hello"));
            Assert.That(mul.Translations["de-DE"], Is.EqualTo("Hallo"));
            Assert.That(mul.Translations["fr-FR"], Is.EqualTo("Bonjour"));

            //matching locale returned
            LocalizedText fr = localizedText.FilterByPreferredLocales(s_preferredLocalesArray3);
            Assert.That(fr.Locale, Is.EqualTo("fr-FR"), "Locale should be 'fr-FR'");
            Assert.That(fr.Text, Is.EqualTo("Bonjour"), "Text should be 'Bonjour'");

            // Default locale returned
            LocalizedText gb = localizedText.FilterByPreferredLocales(s_preferredLocalesArray4);
            Assert.That(gb.Locale, Is.EqualTo("en-US"), "Locale should be 'en-US'");
            Assert.That(gb.Text, Is.EqualTo("Hello"), "Text should be 'Hello'");

            // Filtered returned only fr locale as mul
            LocalizedText mulFr = localizedText.FilterByPreferredLocales(s_preferredLocalesArray5);
            Assert.That(mulFr.IsMultiLanguage, Is.False, "Should not be mul locale because only one locale matched.");
            Assert.That(mulFr.Translations, Has.Count.EqualTo(1), "Translations should have 1 entries");
            Assert.That(mulFr.Translations["fr-FR"], Is.EqualTo("Bonjour"));
        }

        [Test]
        public void LocalizedText_SingleLocale_ReturnsPreferredTranslations()
        {
            // Act
            var localizedText = new LocalizedText("de-DE", "Hallo");

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.False, "Should not be mul locale");

            //found locale returned
            LocalizedText singleDE = localizedText.FilterByPreferredLocales(s_preferredLocales);
            Assert.That(singleDE.Locale, Is.EqualTo("de-DE"), "Locale should be 'de-DE'");
            Assert.That(singleDE.Text, Is.EqualTo("Hallo"), "Text should be 'Hallo'");

            //nonexisting locale, default locale returned
            LocalizedText singleUS = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray4);
            Assert.That(singleUS.Locale, Is.EqualTo("de-DE"), "Locale should be 'de-DE'");
            Assert.That(singleUS.Text, Is.EqualTo("Hallo"), "Text should be 'Hallo'");

            // Default locale returned
            LocalizedText mulGB = localizedText.FilterByPreferredLocales(s_preferredLocalesArray1);
            Assert.That(mulGB.Locale, Is.EqualTo("de-DE"), "Locale should be 'de-DE'");
            Assert.That(mulGB.Text, Is.EqualTo("Hallo"), "Text should be 'Hallo'");
        }

        [Test]
        public void LocalizedText_MulLocale_InvalidJson()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = /*lang=json,strict*/
                "{\"t\":[[\"en-US\"],[\"de-DE\",\"Hallo\", \"fr-FR\"],[]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.That(localizedText.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(localizedText.Text, Is.EqualTo(jsonText));
            Assert.That(localizedText.Translations, Is.Not.Null, "Translations should not be null");
        }

        [Test]
        public void LocalizedText_MulLocale_DeepCopy()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);
            LocalizedText deepCopy = CoreUtils.Clone(in localizedText);

            //Assert
            Assert.That(localizedText.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(deepCopy.IsMultiLanguage, Is.True, "Should be mul locale");
            Assert.That(deepCopy.Locale, Is.EqualTo(localizedText.Locale), "Locale should be the same");
            Assert.That(deepCopy.Text, Is.EqualTo(localizedText.Text), "Text should be the same");
            Assert.That(
                deepCopy.Translations,
                Has.Count.EqualTo(localizedText.Translations.Count),
                "Translations count should be the same");
            Assert.That(
                deepCopy.Translations["en-US"],
                Is.EqualTo(localizedText.Translations["en-US"]),
                "English translation should be the same");
            Assert.That(
                deepCopy.Translations["de-DE"],
                Is.EqualTo(localizedText.Translations["de-DE"]),
                "German translation should be the same");
            Assert.That(
                deepCopy.Translations["fr-FR"],
                Is.EqualTo(localizedText.Translations["fr-FR"]),
                "French translation should be the same");
        }
    }
}
