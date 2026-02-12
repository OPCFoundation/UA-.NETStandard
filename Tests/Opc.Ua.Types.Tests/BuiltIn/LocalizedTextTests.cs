using System.Collections.Generic;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual("mul", localizedText.Locale, "Locale should be 'mul'");
            Assert.IsNotNull(localizedText.Text, "Text should not be null");
            Assert.AreEqual(
                2,
                localizedText.Translations.Count,
                "Translations should have 2 entries");
            Assert.AreEqual("Hello", localizedText.Translations["en-US"]);
            Assert.AreEqual("Hallo", localizedText.Translations["de-DE"]);

            const string expectedJson = /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}";
            Assert.AreEqual(expectedJson, localizedText.Text);
        }

        [Test]
        public void EmptyDictionaryCreatesNullText()
        {
            // Arrange
            var translations = new Dictionary<string, string>();

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should not be mul locale");
            Assert.IsNull(
                localizedText.Translations,
                "Translations should be null for empty dictionary");
            Assert.IsNull(localizedText.Text, "Text should be null for empty dictionary");
            Assert.IsNull(localizedText.Locale, "Locale should be null for empty dictionary");
        }

        [Test]
        public void SingleLocaleDictionaryCreatesLocaleAndTextDirectly()
        {
            // Arrange
            var translations = new Dictionary<string, string> { { "fr-FR", "Bonjour" } };

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should not be mul locale for single entry");
            Assert.AreEqual("fr-FR", localizedText.Locale, "Locale should be 'fr-FR'");
            Assert.AreEqual("Bonjour", localizedText.Text, "Text should be 'Bonjour'");
            Assert.AreEqual(
                1,
                localizedText.Translations.Count,
                "Translations should have 1 entry");
            Assert.AreEqual("Bonjour", localizedText.Translations["fr-FR"]);
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
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual("mul", localizedText.Locale, "Locale should be 'mul'");
            Assert.AreEqual(
                jsonText,
                localizedText.Text,
                "Text should match the input JSON exactly");
            Assert.IsNotNull(localizedText.Translations, "Translations should not be null");
            Assert.AreEqual(
                3,
                localizedText.Translations.Count,
                "Translations should have 3 entries");
            Assert.AreEqual("Hello", localizedText.Translations["en-US"]);
            Assert.AreEqual("Hallo", localizedText.Translations["de-DE"]);
            Assert.AreEqual("Bonjour", localizedText.Translations["fr-FR"]);
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
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");

            //found locale returned
            LocalizedText singleUS = localizedText.FilterByPreferredLocales(s_preferredLocales);
            Assert.AreEqual("en-US", singleUS.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", singleUS.Text, "Text should be 'Hello'");

            //found locale returned
            LocalizedText singleDE = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray);
            Assert.AreEqual("de-DE", singleDE.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleDE.Text, "Text should be 'Hallo'");

            //first locale returned
            LocalizedText singleFR = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray0);
            Assert.AreEqual("en-US", singleFR.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", singleFR.Text, "Text should be 'Hello'");

            // All locales returned
            LocalizedText mul = localizedText.FilterByPreferredLocales(s_preferredLocalesArray2);
            Assert.IsTrue(mul.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(3, mul.Translations.Count, "Translations should have 3 entries");
            Assert.AreEqual("Hello", mul.Translations["en-US"]);
            Assert.AreEqual("Hallo", mul.Translations["de-DE"]);
            Assert.AreEqual("Bonjour", mul.Translations["fr-FR"]);

            //matching locale returned
            LocalizedText fr = localizedText.FilterByPreferredLocales(s_preferredLocalesArray3);
            Assert.AreEqual("fr-FR", fr.Locale, "Locale should be 'fr-FR'");
            Assert.AreEqual("Bonjour", fr.Text, "Text should be 'Bonjour'");

            // Default locale returned
            LocalizedText gb = localizedText.FilterByPreferredLocales(s_preferredLocalesArray4);
            Assert.AreEqual("en-US", gb.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", gb.Text, "Text should be 'Hello'");

            // Filtered returned only fr locale as mul
            LocalizedText mulFr = localizedText.FilterByPreferredLocales(s_preferredLocalesArray5);
            Assert.IsFalse(mulFr.IsMultiLanguage, "Should not be mul locale because only one locale matched.");
            Assert.AreEqual(1, mulFr.Translations.Count, "Translations should have 1 entries");
            Assert.AreEqual("Bonjour", mulFr.Translations["fr-FR"]);
        }

        [Test]
        public void LocalizedText_SingleLocale_ReturnsPreferredTranslations()
        {
            // Act
            var localizedText = new LocalizedText("de-DE", "Hallo");

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should not be mul locale");

            //found locale returned
            LocalizedText singleDE = localizedText.FilterByPreferredLocales(s_preferredLocales);
            Assert.AreEqual("de-DE", singleDE.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleDE.Text, "Text should be 'Hallo'");

            //nonexisting locale, default locale returned
            LocalizedText singleUS = localizedText.FilterByPreferredLocales(
                s_preferredLocalesArray4);
            Assert.AreEqual("de-DE", singleUS.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleUS.Text, "Text should be 'Hallo'");

            // Default locale returned
            LocalizedText mulGB = localizedText.FilterByPreferredLocales(s_preferredLocalesArray1);
            Assert.AreEqual("de-DE", mulGB.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", mulGB.Text, "Text should be 'Hallo'");
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
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(jsonText, localizedText.Text);
            Assert.IsNotNull(localizedText.Translations, "Translations should not be null");
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
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.IsTrue(deepCopy.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(localizedText.Locale, deepCopy.Locale, "Locale should be the same");
            Assert.AreEqual(localizedText.Text, deepCopy.Text, "Text should be the same");
            Assert.AreEqual(
                localizedText.Translations.Count,
                deepCopy.Translations.Count,
                "Translations count should be the same");
            Assert.AreEqual(
                localizedText.Translations["en-US"],
                deepCopy.Translations["en-US"],
                "English translation should be the same");
            Assert.AreEqual(
                localizedText.Translations["de-DE"],
                deepCopy.Translations["de-DE"],
                "German translation should be the same");
            Assert.AreEqual(
                localizedText.Translations["fr-FR"],
                deepCopy.Translations["fr-FR"],
                "French translation should be the same");
        }
    }
}
