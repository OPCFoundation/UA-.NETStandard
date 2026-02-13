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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that provides translations.
    /// </summary>
    public interface ITranslationManager
    {
        /// <summary>
        /// Translates the text and then formats it with the arguments provided.
        /// </summary>
        /// <param name="preferredLocales">The list of preferred locales</param>
        /// <param name="key">The key used to look up the translation</param>
        /// <param name="text">The text to translate</param>
        /// <param name="args">The format argumente</param>
        /// <returns>The translated text</returns>
        /// <remarks>
        /// If any error occur during format the unformatted text is used instead.
        /// </remarks>
        LocalizedText Translate(
            IList<string> preferredLocales,
            string key,
            string text,
            params object[] args);

        /// <summary>
        /// Translates the LocalizedText.
        /// </summary>
        /// <seealso cref="Translate(IList{string},string,string,object[])" />
        LocalizedText Translate(
            IList<string> preferredLocales,
            LocalizedText text);

        /// <summary>
        /// Translates a service result.
        /// </summary>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="result">The result.</param>
        /// <returns>The result with all localized text translated.</returns>
        /// <remarks>Recurssively translates text in inner results.</remarks>
        ServiceResult Translate(
            IList<string> preferredLocales,
            ServiceResult result);
    }

    /// <summary>
    /// Stores the information requires to translate a string.
    /// </summary>
    public readonly record struct TranslationInfo
    {
        /// <summary>
        /// Stores the arguments for uses with a SymbolicId that is used to look up default text.
        /// </summary>
        public TranslationInfo(
            System.Xml.XmlQualifiedName symbolicId,
            params object[] args)
            : this(symbolicId?.ToString(), null, null, args)
        {
        }

        /// <summary>
        /// Creates from a key and a text.
        /// </summary>
        public TranslationInfo(
            string key,
            string locale,
            string text)
            : this(key, locale, text, null)
        {
        }

        /// <summary>
        /// Creates from a key with text and format arguements.
        /// </summary>
        public TranslationInfo(
            string key,
            string locale,
            string format,
            params object[] args)
        {
            Key = string.IsNullOrEmpty(key) ? null : key;
            Locale = string.IsNullOrEmpty(locale) ? null : locale;
            Text = string.IsNullOrEmpty(format) ? null : format;
            Args = args != null && args.Length == 0 ? null : args;
        }

        /// <summary>
        /// Null translation info.
        /// </summary>
        public static readonly TranslationInfo Null;

        /// <summary>
        /// Returns true if this is the default
        /// </summary>
        public bool IsNull =>
            Key == null &&
            Locale == null &&
            Text == null &&
            Args == null;

        /// <summary>
        /// The key used to look up translations in the translation
        /// manager.
        /// </summary>
        public string Key { get; init; }

        /// <summary>
        /// The default locale for the text should nothing be found.
        /// </summary>
        public string Locale { get; init; }

        /// <summary>
        /// The text to translate which acts as fallback for a
        /// missing text.
        /// The text can be a format string if Args are provided.
        /// </summary>
        public string Text { get; init; }

        /// <summary>
        /// The arguments that are used when formatting the text
        /// after translation.
        /// </summary>
        public object[] Args { get; init; }
    }
}
