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
using System.Globalization;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that the resource manager behaves the same when the runtime runs in
    /// globalization-invariant mode, which is common for Native AOT and container
    /// deployments.
    /// </summary>
    /// <remarks>
    /// The globalization mode of a process is fixed when it starts, so the tests
    /// reproduce each mode with the factory that creates the culture of a locale id.
    /// </remarks>
    [TestFixture]
    [Category("ResourceManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ResourceManagerInvariantGlobalizationTests
    {
        /// <summary>
        /// The ways the runtime can create the culture of a locale id.
        /// </summary>
        public enum CultureMode
        {
            /// <summary>
            /// The runtime creates every culture with its data.
            /// </summary>
            Normal,

            /// <summary>
            /// Globalization-invariant mode: the runtime creates only the invariant
            /// culture and throws <see cref="CultureNotFoundException"/> for any
            /// other locale id.
            /// </summary>
            Invariant,

            /// <summary>
            /// Globalization-invariant mode with predefined cultures turned off: the
            /// runtime creates every culture, but with the data of the invariant
            /// culture, for example the language "iv".
            /// </summary>
            InvariantData
        }

        [TestCase(CultureMode.Normal)]
        [TestCase(CultureMode.Invariant)]
        [TestCase(CultureMode.InvariantData)]
        public void LoadDefaultTextRegistersEnglishStatusTexts(CultureMode mode)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);

            resourceManager.LoadDefaultText();

            var input = new ServiceResult(
                StatusCodes.BadTimeout,
                new XmlQualifiedName("BadTimeout"),
                LocalizedText.Null);
            LocalizedText translated = resourceManager.Translate(["en-US"], input).LocalizedText;
            string[] locales = resourceManager.GetAvailableLocales();
            Assert.That(locales, Has.Length.EqualTo(1));
            Assert.That(locales[0], Is.EqualTo("en-US"));
            Assert.That(translated.Locale, Is.EqualTo("en-US"));
            Assert.That(translated.Text, Is.EqualTo("BadTimeout"));
        }

        [TestCase(CultureMode.Normal, "de-de", "de-DE")]
        [TestCase(CultureMode.Invariant, "de-de", "de-DE")]
        [TestCase(CultureMode.Normal, "sr-latn-rs", "sr-Latn-RS")]
        [TestCase(CultureMode.Invariant, "sr-latn-rs", "sr-Latn-RS")]
        [TestCase(CultureMode.Normal, "es-419", "es-419")]
        [TestCase(CultureMode.Invariant, "es-419", "es-419")]
        public void AddUsesTheCanonicalCasingOfTheLocaleId(
            CultureMode mode,
            string locale,
            string expected)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);

            resourceManager.Add("greeting", locale, "Hello");

            LocalizedText translated = resourceManager.Translate([expected], "greeting", "Hi");
            string[] locales = resourceManager.GetAvailableLocales();
            Assert.That(locales, Has.Length.EqualTo(1));
            Assert.That(locales[0], Is.EqualTo(expected));
            Assert.That(translated.Locale, Is.EqualTo(expected));
            Assert.That(translated.Text, Is.EqualTo("Hello"));
        }

        [TestCase(CultureMode.Normal)]
        [TestCase(CultureMode.Invariant)]
        [TestCase(CultureMode.InvariantData)]
        public void TranslateFallsBackToAnotherRegionOfTheLanguage(CultureMode mode)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);
            resourceManager.Add("greeting", "de-DE", "Hallo");

            LocalizedText translated = resourceManager.Translate(["de-AT"], "greeting", "Hello");

            Assert.That(translated.Locale, Is.EqualTo("de-DE"));
            Assert.That(translated.Text, Is.EqualTo("Hallo"));
        }

        [TestCase(CultureMode.Normal)]
        [TestCase(CultureMode.Invariant)]
        [TestCase(CultureMode.InvariantData)]
        public void TranslateMulLabelsRegionFallbackWithItsOwnLocale(CultureMode mode)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);
            resourceManager.Add("greeting", "de-DE", "Hallo");

            LocalizedText translated = resourceManager.Translate(
                ["mul", "de-AT"],
                new LocalizedText("greeting", "en-US", "Hello"));

            Assert.That(translated.Locale, Is.EqualTo("de-DE"));
            Assert.That(translated.Text, Is.EqualTo("Hallo"));
        }

        [TestCase(CultureMode.Normal, "de")]
        [TestCase(CultureMode.Invariant, "de")]
        [TestCase(CultureMode.Normal, "zh-Hans")]
        [TestCase(CultureMode.Invariant, "zh-Hans")]
        public void AddRejectsNeutralLocale(CultureMode mode, string locale)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);

            Assert.That(
                () => resourceManager.Add("greeting", locale, "Hallo"),
                Throws.ArgumentException);
            Assert.That(resourceManager.GetAvailableLocales(), Is.Empty);
        }

        [TestCase(CultureMode.Normal)]
        [TestCase(CultureMode.Invariant)]
        [TestCase(CultureMode.InvariantData)]
        public void AddRejectsMalformedLocale(CultureMode mode)
        {
            using ResourceManager resourceManager = CreateResourceManager(mode);

            Assert.That(
                () => resourceManager.Add("greeting", "!!", "Hallo"),
                Throws.TypeOf<CultureNotFoundException>());
            Assert.That(resourceManager.GetAvailableLocales(), Is.Empty);
        }

        private static ResourceManager CreateResourceManager(CultureMode mode)
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            return mode switch
            {
                CultureMode.Normal => new ResourceManager(configuration),
                CultureMode.Invariant => new ResourceManager(configuration, CreateInvariantModeCulture),
                CultureMode.InvariantData => new ResourceManager(configuration, CreateInvariantDataCulture),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        private static CultureInfo CreateInvariantModeCulture(string name)
        {
            if (name.Length == 0)
            {
                return CultureInfo.InvariantCulture;
            }
            throw new CultureNotFoundException(
                nameof(name),
                name,
                "Only the invariant culture is supported in globalization-invariant mode.");
        }

        private static CultureInfo CreateInvariantDataCulture(string name)
        {
            return name.Length == 0 ? CultureInfo.InvariantCulture : new InvariantDataCulture(name);
        }

        /// <summary>
        /// A culture that carries the data of the invariant culture, as the runtime
        /// creates it in globalization-invariant mode when predefined cultures are
        /// turned off.
        /// </summary>
        private sealed class InvariantDataCulture : CultureInfo
        {
            /// <summary>
            /// Creates the culture of a locale id.
            /// </summary>
            public InvariantDataCulture(string name)
                : base(name)
            {
            }

            /// <inheritdoc/>
            public override string TwoLetterISOLanguageName => "iv";
        }
    }
}
