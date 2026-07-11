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

#nullable enable

using System;
using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Additional coverage for <see cref="ResourceManager"/> targeting Add/StatusCode/SymbolicId
    /// translation, guard clauses and locale fallback paths not exercised by ResourceManagerTests.
    /// </summary>
    [TestFixture]
    [Category("ResourceManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ResourceManagerCoverageTests
    {
        private static ResourceManager CreateResourceManager()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            return new ResourceManager(appConfig);
        }

        [Test]
        public void ConstructorWithNullConfigurationThrows()
        {
            Assert.That(
                () => new ResourceManager(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddWithNullKeyThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add((string)null!, "en-US", "text"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddWithNullLocaleThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add("key", null!, "text"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddWithNullTextThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add("key", "en-US", null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddWithNeutralLocaleThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add("key", "en", "text"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddDictionaryWithNullLocaleThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add(null!, new Dictionary<string, string>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddDictionaryWithNullTranslationsThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add("en-US", null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddDictionaryWithNeutralLocaleThrows()
        {
            using ResourceManager manager = CreateResourceManager();

            Assert.That(
                () => manager.Add("de", new Dictionary<string, string> { { "k", "v" } }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddDictionaryPopulatesTranslations()
        {
            using ResourceManager manager = CreateResourceManager();
            var translations = new Dictionary<string, string>
            {
                { "greeting", "Hallo" },
                { "bye", "Tschuess" }
            };

            manager.Add("de-DE", translations);

            LocalizedText result = manager.Translate(["de-DE"], "greeting", null);
            Assert.That(result.Text, Is.EqualTo("Hallo"));
            Assert.That(result.Locale, Is.EqualTo("de-DE"));
        }

        [Test]
        public void GetAvailableLocalesReflectsAddedLocales()
        {
            using ResourceManager manager = CreateResourceManager();
            manager.Add("k", "en-US", "v");
            manager.Add("k", "de-DE", "w");

            string[] locales = manager.GetAvailableLocales();

            Assert.That(locales, Does.Contain("en-US"));
            Assert.That(locales, Does.Contain("de-DE"));
        }

        [Test]
        public void LoadDefaultTextRegistersEnglishLocale()
        {
            using ResourceManager manager = CreateResourceManager();

            manager.LoadDefaultText();

            Assert.That(manager.GetAvailableLocales(), Does.Contain("en-US"));
        }

        [Test]
        public void TranslateNullServiceResultReturnsNull()
        {
            using ResourceManager manager = CreateResourceManager();

            ServiceResult result = manager.Translate(["en-US"], null!);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TranslateServiceResultWithTextAndNoLocalesReturnsSameInstance()
        {
            using ResourceManager manager = CreateResourceManager();
            var input = new ServiceResult(StatusCodes.BadTimeout, new LocalizedText("en-US", "Boom"));

            ServiceResult result = manager.Translate(default, input);

            Assert.That(result, Is.SameAs(input));
        }

        [Test]
        public void TranslateServiceResultWithTextAndLocalesTranslatesText()
        {
            using ResourceManager manager = CreateResourceManager();
            var input = new ServiceResult(StatusCodes.BadTimeout, new LocalizedText("en-US", "Boom"));

            ServiceResult result = manager.Translate(["en-US"], input);

            Assert.That(result, Is.Not.SameAs(input));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Boom"));
            Assert.That(result.StatusCode, Is.EqualTo(input.StatusCode));
        }

        [Test]
        public void TranslateServiceResultUsesStatusCodeMapping()
        {
            using ResourceManager manager = CreateResourceManager();
            var statusCode = (StatusCode)0xABCD0000u;
            manager.Add(statusCode, "en-US", "The operation timed out.");
            var input = new ServiceResult(statusCode);

            ServiceResult result = manager.Translate(["en-US"], input);

            Assert.That(result.LocalizedText.Text, Is.EqualTo("The operation timed out."));
        }

        [Test]
        public void TranslateServiceResultWithoutStatusMappingReturnsHexCode()
        {
            using ResourceManager manager = CreateResourceManager();
            var input = new ServiceResult((StatusCode)0xABCD0000u);

            ServiceResult result = manager.Translate(["en-US"], input);

            Assert.That(
                result.LocalizedText.Text,
                Is.EqualTo(input.StatusCode.Code.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void TranslateServiceResultUsesSymbolicIdMapping()
        {
            using ResourceManager manager = CreateResourceManager();
            var symbolicId = new XmlQualifiedName("MySymbol", "urn:test");
            manager.Add(symbolicId, "en-US", "Symbolic message");
            var input = new ServiceResult(
                StatusCodes.BadTimeout,
                symbolicId,
                LocalizedText.Null);

            ServiceResult result = manager.Translate(["en-US"], input);

            Assert.That(result.LocalizedText.Text, Is.EqualTo("Symbolic message"));
        }

        [Test]
        public void TranslateServiceResultWithoutSymbolicMappingReturnsSymbolicId()
        {
            using ResourceManager manager = CreateResourceManager();
            var symbolicId = new XmlQualifiedName("UnmappedSymbol", "urn:test");
            var input = new ServiceResult(
                StatusCodes.BadTimeout,
                symbolicId,
                LocalizedText.Null);

            ServiceResult result = manager.Translate(["en-US"], input);

            Assert.That(result.LocalizedText.Text, Is.EqualTo("UnmappedSymbol"));
        }

        [Test]
        public void TranslateFallsBackToMatchingLanguageDifferentRegion()
        {
            using ResourceManager manager = CreateResourceManager();
            manager.Add("greeting", "de-DE", "Hallo");

            LocalizedText result = manager.Translate(["de-AT"], "greeting", null);

            Assert.That(result.Text, Is.EqualTo("Hallo"));
            Assert.That(result.Locale, Is.EqualTo("de-DE"));
        }

        [Test]
        public void TranslateWithNoMatchingTranslationReturnsDefault()
        {
            using ResourceManager manager = CreateResourceManager();
            manager.Add("other", "de-DE", "Hallo");

            LocalizedText result = manager.Translate(["fr-FR"], "missing", "def");

            Assert.That(result.Text, Is.EqualTo("def"));
        }
    }
}
