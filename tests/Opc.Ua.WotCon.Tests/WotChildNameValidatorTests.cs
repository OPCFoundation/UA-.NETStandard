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

using NUnit.Framework;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotChildNameValidatorTests
    {
        [TestCase("temperature")]
        [TestCase("Set-Point")]
        [TestCase("value_1")]
        [TestCase("a")]
        [TestCase("A1")]
        [TestCase("Temperature_Sensor_42")]
        public void ValidNames_PassValidation(string name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"'{name}' should be accepted; got {result}");
        }

        [TestCase(null)]
        [TestCase("")]
        public void EmptyOrNull_Rejected(string? name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void NameAtMaxLength_Allowed()
        {
            string name = new('a', WotChildNameValidator.MaxLength);
            Assert.That(ServiceResult.IsGood(WotChildNameValidator.Validate(name)), Is.True);
        }

        [Test]
        public void NameOverMaxLength_Rejected()
        {
            string name = new('a', WotChildNameValidator.MaxLength + 1);
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("temp/erature")]   // path separator
        [TestCase("temp\\erature")]   // path separator
        [TestCase("with.dot")]       // browse-path token
        [TestCase("with#hash")]      // NodeId namespace tag
        [TestCase("with:colon")]     // NodeId identifier-type tag
        [TestCase("with!bang")]      // browse-path inverse token
        public void NamesWithForbiddenPunctuation_Rejected(string name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                $"'{name}' should be rejected as forbidden-punctuation");
        }

        [TestCase("a\u0000b")]    // NUL
        [TestCase("a\u0001b")]    // C0 control
        [TestCase("a\u001Fb")]    // C0 control (US)
        [TestCase("a\u007Fb")]    // DEL
        public void NamesWithControlCharacters_Rejected(string name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("\u202Eevil")]              // RTL override
        [TestCase("evil\u202D")]              // LTR override
        [TestCase("a\u200Eb")]                // LRM
        [TestCase("a\u200Fb")]                // RLM
        [TestCase("a\u2066b")]                // LRI
        [TestCase("a\u2069b")]                // PDI
        public void NamesWithBidiOrFormatChars_Rejected(string name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                $"'{name}' should be rejected as BIDI / format character");
        }

        [TestCase(" leading")]
        [TestCase("trailing ")]
        [TestCase("\tindented")]
        [TestCase("a ")]
        public void NamesWithLeadingOrTrailingWhitespace_Rejected(string name)
        {
            ServiceResult result = WotChildNameValidator.Validate(name);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void SanitiseForLog_ReplacesControlCharsWithUnicodeEscape()
        {
            string sanitised = WotChildNameValidator.SanitiseForLog("evil\u0001\u202E");
            Assert.That(sanitised, Is.EqualTo("evilU+0001U+202E"));
        }

        [Test]
        public void SanitiseForLog_TruncatesAtMaxLength()
        {
            string name = new('a', WotChildNameValidator.MaxLength + 50);
            string sanitised = WotChildNameValidator.SanitiseForLog(name);
            Assert.That(sanitised, Does.EndWith("..."));
            Assert.That(sanitised, Has.Length.EqualTo(WotChildNameValidator.MaxLength + 3));
        }

        [TestCase(null)]
        [TestCase("")]
        public void SanitiseForLog_HandlesEmptyInput(string? name)
        {
            Assert.That(WotChildNameValidator.SanitiseForLog(name), Is.EqualTo(string.Empty));
        }
    }
}
