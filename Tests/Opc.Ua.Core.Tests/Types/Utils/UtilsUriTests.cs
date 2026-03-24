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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for URI, locale and string utility methods in <see cref="Utils"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsUriTests
    {
        #region EscapeUri / UnescapeUri

        /// <summary>
        /// EscapeUri encodes semicolons to %3B.
        /// </summary>
        [Test]
        public void EscapeUriEncodesSpecialCharacters()
        {
            const string input = "opc.tcp://host:4840/path;param";
            string result = Utils.EscapeUri(input);
            Assert.That(result, Does.Contain("%3B"));
            Assert.That(result, Does.Not.Contain(";"));
        }

        /// <summary>
        /// EscapeUri encodes percent signs to %25.
        /// </summary>
        [Test]
        public void EscapeUriEncodesPercentSign()
        {
            const string input = "opc.tcp://host:4840/path%value";
            string result = Utils.EscapeUri(input);
            Assert.That(result, Does.Contain("%25"));
            Assert.That(result, Does.Not.Contain("path%v"));
        }

        /// <summary>
        /// EscapeUri with null input returns empty string.
        /// </summary>
        [Test]
        public void EscapeUriNullReturnsEmpty()
        {
            string result = Utils.EscapeUri(null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// EscapeUri with whitespace-only input returns empty string.
        /// </summary>
        [Test]
        public void EscapeUriWhitespaceReturnsEmpty()
        {
            string result = Utils.EscapeUri("   ");
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// EscapeUri with a URI that has no special characters returns it unchanged.
        /// </summary>
        [Test]
        public void EscapeUriNoSpecialCharsReturnsSameValue()
        {
            const string input = "opc.tcp://host:4840/path";
            string result = Utils.EscapeUri(input);
            Assert.That(result, Is.EqualTo(input));
        }

        /// <summary>
        /// UnescapeUri decodes percent-encoded characters.
        /// </summary>
        [Test]
        public void UnescapeUriDecodesPercentEncoding()
        {
            const string input = "opc.tcp://host:4840/path%3Bparam";
            string result = Utils.UnescapeUri(input);
            Assert.That(result, Does.Contain(";"));
            Assert.That(result, Does.Not.Contain("%3B"));
        }

        /// <summary>
        /// UnescapeUri with null input returns empty string.
        /// </summary>
        [Test]
        public void UnescapeUriNullReturnsEmpty()
        {
            string result = Utils.UnescapeUri((string)null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// UnescapeUri with whitespace-only input returns empty string.
        /// </summary>
        [Test]
        public void UnescapeUriWhitespaceReturnsEmpty()
        {
            string result = Utils.UnescapeUri("   ");
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// UnescapeUri with a URI that has no encoding returns it unchanged.
        /// </summary>
        [Test]
        public void UnescapeUriNoEncodingReturnsSameValue()
        {
            const string input = "opc.tcp://host:4840/path";
            string result = Utils.UnescapeUri(input);
            Assert.That(result, Is.EqualTo(input));
        }

        /// <summary>
        /// EscapeUri followed by UnescapeUri round-trips correctly.
        /// </summary>
        [Test]
        public void EscapeAndUnescapeUriRoundTrip()
        {
            const string original = "opc.tcp://host:4840/path;with%special";
            string escaped = Utils.EscapeUri(original);
            string unescaped = Utils.UnescapeUri(escaped);
            Assert.That(unescaped, Is.EqualTo(original));
        }

        #endregion

        #region Utf8IsNullOrEmpty

        /// <summary>
        /// Utf8IsNullOrEmpty returns true for an empty span.
        /// </summary>
        [Test]
        public void Utf8IsNullOrEmptyEmptySpanReturnsTrue()
        {
            Assert.That(Utils.Utf8IsNullOrEmpty(System.ReadOnlySpan<byte>.Empty), Is.True);
        }

        /// <summary>
        /// Utf8IsNullOrEmpty returns true for a span that contains only space bytes.
        /// </summary>
        [Test]
        public void Utf8IsNullOrEmptyOnlySpacesReturnsTrue()
        {
            byte[] spaces = System.Text.Encoding.UTF8.GetBytes("   ");
            Assert.That(Utils.Utf8IsNullOrEmpty(spaces), Is.True);
        }

        /// <summary>
        /// Utf8IsNullOrEmpty returns false when the span has at least one non-space byte.
        /// </summary>
        [Test]
        public void Utf8IsNullOrEmptyNonSpaceByteReturnsFalse()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("hello");
            Assert.That(Utils.Utf8IsNullOrEmpty(data), Is.False);
        }

        /// <summary>
        /// Utf8IsNullOrEmpty returns false for a span that starts with spaces then has content.
        /// </summary>
        [Test]
        public void Utf8IsNullOrEmptySpacePrefixedContentReturnsFalse()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("  a");
            Assert.That(Utils.Utf8IsNullOrEmpty(data), Is.False);
        }

        #endregion

        #region ReplaceLocalhost / ReplaceDCLocalhost

        /// <summary>
        /// ReplaceLocalhost replaces "localhost" in a URI with the given hostname.
        /// </summary>
        [Test]
        public void ReplaceLocalhostReplacesLocalhostWithHostname()
        {
            const string uri = "opc.tcp://localhost:4840/UA/Server";
            string result = Utils.ReplaceLocalhost(uri, "myserver");
            Assert.That(result, Does.Contain("myserver"));
            Assert.That(result, Does.Not.Contain("localhost"));
        }

        /// <summary>
        /// ReplaceLocalhost returns the original string if "localhost" is not present.
        /// </summary>
        [Test]
        public void ReplaceLocalhostNoLocalhostReturnsSameValue()
        {
            const string uri = "opc.tcp://otherhost:4840/UA/Server";
            string result = Utils.ReplaceLocalhost(uri, "myserver");
            Assert.That(result, Is.EqualTo(uri));
        }

        /// <summary>
        /// ReplaceLocalhost with null URI returns null.
        /// </summary>
        [Test]
        public void ReplaceLocalhostNullUriReturnsNull()
        {
            string result = Utils.ReplaceLocalhost(null, "myserver");
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// ReplaceLocalhost with an IPv6 hostname wraps it in brackets.
        /// </summary>
        [Test]
        public void ReplaceLocalhostIpv6HostnameIsWrappedInBrackets()
        {
            const string uri = "opc.tcp://localhost:4840/UA/Server";
            string result = Utils.ReplaceLocalhost(uri, "::1");
            Assert.That(result, Does.Contain("[::1]"));
        }

        /// <summary>
        /// ReplaceDCLocalhost replaces "DC=localhost" in a subject name with the given hostname.
        /// </summary>
        [Test]
        public void ReplaceDCLocalhostReplacesDcLocalhostWithHostname()
        {
            const string subjectName = "CN=MyServer, DC=localhost";
            string result = Utils.ReplaceDCLocalhost(subjectName, "myserver");
            Assert.That(result, Does.Contain("DC=myserver"));
            Assert.That(result, Does.Not.Contain("DC=localhost"));
        }

        /// <summary>
        /// ReplaceDCLocalhost returns the original string if "DC=localhost" is not present.
        /// </summary>
        [Test]
        public void ReplaceDCLocalhostNoDcLocalhostReturnsSameValue()
        {
            const string subjectName = "CN=MyServer, DC=otherhost";
            string result = Utils.ReplaceDCLocalhost(subjectName, "myserver");
            Assert.That(result, Is.EqualTo(subjectName));
        }

        /// <summary>
        /// ReplaceDCLocalhost with null subject name returns null.
        /// </summary>
        [Test]
        public void ReplaceDCLocalhostNullSubjectNameReturnsNull()
        {
            string result = Utils.ReplaceDCLocalhost(null, "myserver");
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// ReplaceDCLocalhost with an IPv6 hostname wraps it in brackets.
        /// </summary>
        [Test]
        public void ReplaceDCLocalhostIpv6HostnameIsWrappedInBrackets()
        {
            const string subjectName = "CN=MyServer, DC=localhost";
            string result = Utils.ReplaceDCLocalhost(subjectName, "::1");
            Assert.That(result, Does.Contain("[::1]"));
        }

        #endregion

        #region ParseUri

        /// <summary>
        /// ParseUri with a valid URI returns the parsed Uri object.
        /// </summary>
        [Test]
        public void ParseUriValidUriReturnsParsedUri()
        {
            const string uriString = "opc.tcp://host:4840/UA/Server";
            System.Uri result = Utils.ParseUri(uriString);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Scheme, Is.EqualTo("opc.tcp"));
            Assert.That(result.Port, Is.EqualTo(4840));
        }

        /// <summary>
        /// ParseUri with null input returns null.
        /// </summary>
        [Test]
        public void ParseUriNullReturnsNull()
        {
            System.Uri result = Utils.ParseUri(null);
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// ParseUri with empty string returns null.
        /// </summary>
        [Test]
        public void ParseUriEmptyReturnsNull()
        {
            System.Uri result = Utils.ParseUri(string.Empty);
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// ParseUri with an invalid URI string returns null instead of throwing.
        /// </summary>
        [Test]
        public void ParseUriInvalidUriReturnsNull()
        {
            System.Uri result = Utils.ParseUri("not a valid uri ://!!@@");
            Assert.That(result, Is.Null);
        }

        #endregion

        #region IsValidLocaleId / GetLanguageId

        /// <summary>
        /// IsValidLocaleId returns true for a well-known locale such as "en-US".
        /// </summary>
        [Test]
        public void IsValidLocaleIdValidLocaleReturnsTrue()
        {
            Assert.That(Utils.IsValidLocaleId("en-US"), Is.True);
        }

        /// <summary>
        /// IsValidLocaleId returns true for a plain language code such as "en".
        /// </summary>
        [Test]
        public void IsValidLocaleIdLanguageOnlyReturnsTrue()
        {
            Assert.That(Utils.IsValidLocaleId("en"), Is.True);
        }

        /// <summary>
        /// IsValidLocaleId returns false for a clearly malformed locale string
        /// that contains characters not valid in BCP-47 language tags.
        /// </summary>
        [Test]
        public void IsValidLocaleIdMalformedLocaleReturnsFalse()
        {
            // Strings with special characters like '@' are rejected by CultureInfo
            Assert.That(Utils.IsValidLocaleId("@invalid"), Is.False);
        }

        /// <summary>
        /// IsValidLocaleId returns false for null.
        /// </summary>
        [Test]
        public void IsValidLocaleIdNullReturnsFalse()
        {
            Assert.That(Utils.IsValidLocaleId(null), Is.False);
        }

        /// <summary>
        /// IsValidLocaleId returns false for empty string.
        /// </summary>
        [Test]
        public void IsValidLocaleIdEmptyReturnsFalse()
        {
            Assert.That(Utils.IsValidLocaleId(string.Empty), Is.False);
        }

        /// <summary>
        /// GetLanguageId extracts the language code from a locale with region.
        /// </summary>
        [Test]
        public void GetLanguageIdLocaleWithRegionReturnsLanguageOnly()
        {
            string languageId = Utils.GetLanguageId("en-US");
            Assert.That(languageId, Is.EqualTo("en"));
        }

        /// <summary>
        /// GetLanguageId returns the input unchanged when it contains no region.
        /// </summary>
        [Test]
        public void GetLanguageIdLanguageOnlyReturnsSame()
        {
            string languageId = Utils.GetLanguageId("en");
            Assert.That(languageId, Is.EqualTo("en"));
        }

        /// <summary>
        /// GetLanguageId returns empty string for null input.
        /// </summary>
        [Test]
        public void GetLanguageIdNullReturnsEmptyString()
        {
            string languageId = Utils.GetLanguageId(null);
            Assert.That(languageId, Is.EqualTo(string.Empty));
        }

        #endregion

        #region SelectLocalizedText

        /// <summary>
        /// SelectLocalizedText returns the entry that exactly matches the requested locale.
        /// </summary>
        [Test]
        public void SelectLocalizedTextExactLocaleMatchReturnsMatch()
        {
            IList<string> localeIds = ["en-US"];
            IList<LocalizedText> names =
            [
                new LocalizedText("de-DE", "Hallo"),
                new LocalizedText("en-US", "Hello"),
            ];
            LocalizedText defaultName = new LocalizedText("en", "Hi");

            LocalizedText result = Utils.SelectLocalizedText(localeIds, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// SelectLocalizedText falls back to language match when no exact locale match exists.
        /// </summary>
        [Test]
        public void SelectLocalizedTextFallsBackToLanguageMatch()
        {
            IList<string> localeIds = ["en-GB"];
            IList<LocalizedText> names =
            [
                new LocalizedText("de-DE", "Hallo"),
                new LocalizedText("en-US", "Hello"),
            ];
            LocalizedText defaultName = new LocalizedText("en", "Hi");

            LocalizedText result = Utils.SelectLocalizedText(localeIds, names, defaultName);
            Assert.That(result.Text, Is.EqualTo("Hello"));
        }

        /// <summary>
        /// SelectLocalizedText returns defaultName when no locales are requested.
        /// </summary>
        [Test]
        public void SelectLocalizedTextNoLocalesRequestedReturnsDefault()
        {
            IList<LocalizedText> names = [new LocalizedText("en-US", "Hello")];
            LocalizedText defaultName = new LocalizedText("en", "Hi");

            LocalizedText result = Utils.SelectLocalizedText(null, names, defaultName);
            Assert.That(result, Is.EqualTo(defaultName));
        }

        /// <summary>
        /// SelectLocalizedText returns defaultName when the names list is empty.
        /// </summary>
        [Test]
        public void SelectLocalizedTextEmptyNamesListReturnsDefault()
        {
            IList<string> localeIds = ["en-US"];
            LocalizedText defaultName = new LocalizedText("en", "Hi");

            LocalizedText result = Utils.SelectLocalizedText(localeIds, [], defaultName);
            Assert.That(result, Is.EqualTo(defaultName));
        }

        /// <summary>
        /// SelectLocalizedText returns defaultName when no names match the requested locale.
        /// </summary>
        [Test]
        public void SelectLocalizedTextNoMatchReturnsDefault()
        {
            IList<string> localeIds = ["fr-FR"];
            IList<LocalizedText> names = [new LocalizedText("de-DE", "Hallo")];
            LocalizedText defaultName = new LocalizedText("en", "Hi");

            LocalizedText result = Utils.SelectLocalizedText(localeIds, names, defaultName);
            Assert.That(result, Is.EqualTo(defaultName));
        }

        #endregion
    }
}
