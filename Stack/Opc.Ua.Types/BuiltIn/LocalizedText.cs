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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Human readable qualified with a locale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The LocalizedText is defined in <b>Part 3 - Address Space Model, Section 7.5</b>, titled
    /// <b>LocalizedText</b>.
    /// <br/></para>
    /// <para>
    /// This class provides a wrapper for posting text that is qualified with the locale
    /// that it is intended for.
    /// <br/></para>
    /// </remarks>
    /// <example>
    /// <para>The following example shows a very simple use of this class to localize a
    /// welcome message</para>
    /// <code lang="C#">
    /// LocalizedText welcomeUS = new LocalizedText( "Hi Everyone", "EN-US" );
    /// LocalizedText welcomeGB = new LocalizedText( "Hello Everyone", "EN-GB" );
    /// LocalizedText welcomeNoLocale = new LocalizedText( "Welcome" );
    ///
    /// Console.WriteLine( welcomeUS.ToString() );
    /// Console.WriteLine( welcomeGB.ToString() );
    /// Console.WriteLine( welcomeNoLocale.ToString() );
    /// </code>
    /// <code lang="Visual Basic">
    /// Dim welcomeUS As LocalizedText = New LocalizedText( "Hi Everyone", "EN-GB" )
    /// Dim welcomeGB As LocalizedText = New LocalizedText( "Hello Everyone", "EN-GB" )
    /// Dim welcomeNoLocale As LocalizedText = New LocalizedText( "Welcome" )
    ///
    /// Console.WriteLine( welcomeUS.ToString() )
    /// Console.WriteLine( welcomeGB.ToString() )
    /// Console.WriteLine( welcomeNoLocale.ToString() )
    /// </code>
    /// <para>
    /// This produces the following output:<br/>
    /// [EN-US]:Hi Everyone<br/>
    /// [EN-GB]:Hello Everyone<br/>
    /// Welcome<br/>
    /// </para>
    /// </example>
    public readonly struct LocalizedText :
        IEquatable<LocalizedText>,
        IFormattable
    {
        /// <summary>
        /// Returns an instance of a null LocalizedText.
        /// </summary>
        public static readonly LocalizedText Null;

        /// <summary>
        /// Returns true if the text is a null or empty string.
        /// </summary>
        public bool IsNullOrEmpty
            => m_translation == null && string.IsNullOrEmpty(m_text);

        /// <summary>
        /// Default constructor
        /// </summary>
        public LocalizedText()
        {
            m_translation = null;
            m_locale = null;
            m_text = null;
        }

        /// <summary>
        /// Create a very simple localized text with default locale
        /// </summary>
        /// <param name="text">The plain text stored within this object</param>
        public LocalizedText(string text)
        {
            m_translation = null;
            m_locale = null;
            m_text = text;
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        [JsonConstructor]
        public LocalizedText(string locale, string text)
        {
            m_text = text;
            m_locale = locale;
            m_translation = LocalizedTextFormatAndTranslation.Create(locale, text);
        }

        /// <summary>
        /// Initializes the locale with a key, locale and text.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="locale">The locale for the text provides</param>
        /// <param name="text">The localized text</param>
        public LocalizedText(string key, string locale, string text)
        {
            m_text = text;
            m_locale = locale;
            m_translation = LocalizedTextFormatAndTranslation.Create(key, locale, text);
        }

        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// </summary>
        public LocalizedText(string key, string locale, string text, params object[] args)
            : this(locale, text, LocalizedTextFormatAndTranslation.Create(key, locale, text, args))
        {
        }

        /// <summary>
        /// Creates text from a TranslationInfo object.
        /// </summary>
        public LocalizedText(TranslationInfo translationInfo)
            : this(LocalizedTextFormatAndTranslation.Create(translationInfo))
        {
        }

        /// <summary>
        /// Creates text from a TranslationInfo object.
        /// </summary>
        public LocalizedText(string locale, string text, TranslationInfo translationInfo)
            : this(locale, text, LocalizedTextFormatAndTranslation.Create(translationInfo))
        {
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="translations">key = locale, value = text</param>
        /// <param name="translationInfo"></param>
        public LocalizedText(
            IReadOnlyDictionary<string, string> translations,
            TranslationInfo translationInfo = default)
            : this(LocalizedTextFormatAndTranslation.Create(translations, translationInfo))
        {
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="translations">key = locale, value = text</param>
        public LocalizedText(string key, IReadOnlyDictionary<string, string> translations)
            : this(LocalizedTextFormatAndTranslation.Create(key, translations))
        {
        }

        /// <summary>
        /// Create localized text
        /// </summary>
        /// <param name="translation"></param>
        /// <exception cref="ArgumentNullException"></exception>
        internal LocalizedText(LocalizedTextFormatAndTranslation translation)
        {
            m_translation = translation;
            m_locale = translation?.GetLocale();
            m_text = translation?.FormatText();
        }

        /// <summary>
        /// Initializes the object with a locale and text and translation object.
        /// </summary>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        /// <param name="translation">The translation information</param>
        internal LocalizedText(string locale, string text, LocalizedTextFormatAndTranslation translation)
        {
            m_text = text;
            m_locale = locale;
            m_translation = translation;
        }

        /// <summary>
        /// The locale used to create the text.
        /// </summary>
        public string Locale
            => IsMultiLanguage ? m_locale : m_translation?.GetLocale() ?? m_locale;

        /// <summary>
        /// The localized text.
        /// </summary>
        public string Text
            => IsMultiLanguage ? m_text : m_translation?.FormatText() ?? m_text;

        /// <summary>
        /// Translations
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, string> Translations
            => m_translation?.Translations;

        /// <summary>
        /// The information required to translate the text into other locales.
        /// </summary>
        [JsonIgnore]
        public TranslationInfo TranslationInfo
            => m_translation?.TranslationInfo ?? default;

        /// <summary>
        /// Returns true if this LocalizedText uses the "mul" special locale.
        /// </summary>
        [JsonIgnore]
        public bool IsMultiLanguage
            => LocalizedTextFormatAndTranslation.IsMultiLanguage(m_locale);

        /// <summary>
        /// Convert this to multi-language format
        /// </summary>
        public LocalizedText AsMultiLanguage()
        {
            return
                m_translation?.AsMultiLanguage(false) ??
                LocalizedTextFormatAndTranslation.EncodeAsMulLocale(this);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                LocalizedText ltext => Equals(ltext),
                string str => Equals(str),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(LocalizedText ltext)
        {
            if (ltext.Locale != Locale &&
                !(string.IsNullOrEmpty(ltext.Locale) &&
                    string.IsNullOrEmpty(Locale)))
            {
                return false;
            }
            return ltext.Text == Text;
        }

        /// <inheritdoc/>
        public bool Equals(string ltext)
        {
            if (!string.IsNullOrEmpty(Locale))
            {
                return false;
            }
            return ltext == Text;
        }

        /// <inheritdoc/>
        public static bool operator ==(LocalizedText value1, LocalizedText value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(LocalizedText value1, LocalizedText value2)
        {
            return !value1.Equals(value2);
        }

        /// <summary>
        /// Returns a suitable hash code for the object.
        /// </summary>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            if (Text != null)
            {
                hash.Add(Text);
            }
            if (Locale != null)
            {
                hash.Add(Locale);
            }
            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused). Always pass NULL/NOTHING</param>
        /// <param name="formatProvider">(Unused). Always pass NULL/NOTHING</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are used</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", Text);
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <param name="value">The string to store as localized text</param>
        public static LocalizedText From(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Null;
            }
            return new LocalizedText(value);
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <param name="value">The string to store as localized text</param>
        public static explicit operator LocalizedText(string value)
        {
            return From(value);
        }

        /// <summary>
        /// Returns a LocalizedText filtered by the preferred locales according to OPC UA Part 4
        /// rules for 'mul' and 'qst'. (https://reference.opcfoundation.org/Core/Part4/v105/docs/5.4)
        /// </summary>
        /// <param name="preferredLocales">The list of preferred locales, possibly including 'mul'
        /// or 'qst' as the first entry.</param>
        /// <returns>A LocalizedText containing translations as specified by the rules.</returns>
        [Pure]
        public LocalizedText FilterByPreferredLocales(ArrayOf<string> preferredLocales)
        {
            return m_translation == null
                ? this
                : m_translation.FilterByPreferredLocales(this, preferredLocales);
        }

        /// <summary>
        /// Add translations to the localized text
        /// </summary>
        [Pure]
        public LocalizedText WithTranslations(IReadOnlyDictionary<string, string> translations)
        {
            if (translations == null || translations.Count == 0)
            {
                return this;
            }
            Dictionary<string, string> merged = m_translation?.Translations?
                .ToDictionary(k => k.Key, v => v.Value) ??
                [];
            foreach (KeyValuePair<string, string> kvp in translations)
            {
                merged[kvp.Key] = kvp.Value;
            }
            if (m_locale != null && m_text != null)
            {
                merged[m_locale] = m_text;
            }
            return new LocalizedText(
                 m_locale,
                 m_text,
                 LocalizedTextFormatAndTranslation.Create(
                     merged,
                     m_translation?.TranslationInfo ?? default));
        }

        /// <summary>
        /// Replace translation information
        /// </summary>
        [Pure]
        public LocalizedText WithTranslationInfo(TranslationInfo info)
        {
            if (info.IsNull)
            {
                return this;
            }
            return new LocalizedText(
                m_locale,
                m_text,
                LocalizedTextFormatAndTranslation.Create(
                    m_translation?.Translations,
                    info));
        }

        private readonly string m_text;
        private readonly string m_locale; // TODO: make union with m_translation?
        private readonly LocalizedTextFormatAndTranslation m_translation;
    }

    /// <summary>
    /// Translation and formatting information for a LocalizedText.
    /// </summary>
    internal sealed class LocalizedTextFormatAndTranslation
    {
        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// Creates a translation info object. Translations are empty
        /// </summary>
        public static LocalizedTextFormatAndTranslation Create(
            string key,
            string locale,
            string text,
            params object[] args)
        {
            return Create(new TranslationInfo(key, locale, text, args));
        }

        /// <summary>
        /// Creates text from a TranslationInfo object.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="translationInfo"/>
        /// is <c>null</c>.</exception>
        public static LocalizedTextFormatAndTranslation Create(
            TranslationInfo translationInfo)
        {
            if (translationInfo.IsNull)
            {
                throw new ArgumentNullException(nameof(translationInfo));
            }

            // If the translation info uses the "mul" locale, decode it into a
            // translations dictionary to use during formatting.
            string locale = translationInfo.Locale;
            string text = translationInfo.Text;
            return new LocalizedTextFormatAndTranslation(
                DecodeMulLocale(locale, text),
                translationInfo);
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        public static LocalizedTextFormatAndTranslation Create(
            string locale,
            string text)
        {
            ReadOnlyDictionary<string, string> translations = DecodeMulLocale(locale, text);
            return translations == null ?
                null :
                new LocalizedTextFormatAndTranslation(translations);
        }

        /// <summary>
        /// Initializes the locale with a key, locale and text.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="locale">The locale for the text provides</param>
        /// <param name="text">The localized text</param>
        public static LocalizedTextFormatAndTranslation Create(
            string key,
            string locale,
            string text)
        {
            return new LocalizedTextFormatAndTranslation(
                DecodeMulLocale(locale, text),
                new TranslationInfo(key, locale, text));
        }

        /// <summary>
        /// Create a LocalizedText object from a dictionary of translations.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="translations"></param>
        /// <returns></returns>
        public static LocalizedTextFormatAndTranslation Create(
            string key,
            IReadOnlyDictionary<string, string> translations)
        {
            if ((translations == null || translations.Count == 0) && string.IsNullOrEmpty(key))
            {
                return null;
            }
            if (!string.IsNullOrEmpty(key))
            {
                return new LocalizedTextFormatAndTranslation(
                    translations, new TranslationInfo(key, null, null));
            }
            return new LocalizedTextFormatAndTranslation(translations);
        }

        /// <summary>
        /// Create a LocalizedText object from a dictionary of translations.
        /// </summary>
        /// <param name="translations"></param>
        /// <param name="translationInfo"></param>
        /// <returns></returns>
        public static LocalizedTextFormatAndTranslation Create(
            IReadOnlyDictionary<string, string> translations,
            TranslationInfo translationInfo)
        {
            if ((translations == null || translations.Count == 0) && translationInfo.IsNull)
            {
                return null;
            }
            return new LocalizedTextFormatAndTranslation(
                translations,
                translationInfo);
        }

        /// <summary>
        /// Creates a translation object from a dictionary of translations.
        /// Either one of the arguments must not be null or empty.
        /// </summary>
        /// <param name="translations">key = locale, value = text</param>
        /// <param name="translationInfo">The optional translation info></param>
        /// <exception cref="ArgumentException">Thrown if both arguments are null
        /// or empty.</exception>"
        private LocalizedTextFormatAndTranslation(
            IReadOnlyDictionary<string, string> translations,
            TranslationInfo translationInfo = default)
        {
            if (translationInfo.IsNull &&
                (translations == null || translations.Count == 0))
            {
                throw new ArgumentException(
                    "At least one translation must be provided.",
                    nameof(translations));
            }
            KeyValuePair<string, string> first = translations?.FirstOrDefault() ?? default;
            TranslationInfo = translationInfo with
            {
                Text = translationInfo.Text ?? first.Value,
                Locale = translationInfo.Locale ?? first.Key
            };
            Translations = translations;
        }

        /// <summary>
        /// The information required to format the text.
        /// </summary>
        public TranslationInfo TranslationInfo { get; }

        /// <summary>
        /// Translations
        /// </summary>
        public IReadOnlyDictionary<string, string> Translations { get; }

        /// <summary>
        /// Get locale
        /// </summary>
        public string GetLocale()
        {
            return TranslationInfo.Locale;
        }

        /// <summary>
        /// Format the translation info text with args and locale
        /// </summary>
        /// <returns></returns>
        public string FormatText(string locale = null, string fallbackText = null)
        {
            string text = TranslationInfo.Text;
            locale ??= TranslationInfo.Locale;
            if (Translations != null &&
                locale != null &&
                Translations.TryGetValue(locale, out string localizedText))
            {
                text = localizedText;
            }

            if (string.IsNullOrWhiteSpace(text) ||
                TranslationInfo.Args == null ||
                TranslationInfo.Args.Length == 0)
            {
                return text ?? fallbackText;
            }

            CultureInfo culture = CultureInfo.InvariantCulture;
            if (!string.IsNullOrEmpty(TranslationInfo.Locale))
            {
                try
                {
                    culture = new CultureInfo(TranslationInfo.Locale);
                }
                catch
                {
                    culture = CultureInfo.InvariantCulture;
                }
            }
            try
            {
                return string.Format(culture, text, TranslationInfo.Args);
            }
            catch
            {
                return text;
            }
        }

        /// <summary>
        /// Returns a LocalizedText filtered by the preferred locales according
        /// to OPC UA Part 4 rules for 'mul' and 'qst'.
        /// (https://reference.opcfoundation.org/Core/Part4/v105/docs/5.4)
        /// </summary>
        /// <param name="localizedText">The text to filter</param>
        /// <param name="preferredLocales">The list of preferred locales, possibly
        /// including 'mul' or 'qst' as the first entry.</param>
        /// <returns>A LocalizedText containing translations as specified by the
        /// rules.</returns>
        [Pure]
        public LocalizedText FilterByPreferredLocales(
            LocalizedText localizedText,
            ArrayOf<string> preferredLocales)
        {
            if (preferredLocales.Count == 0)
            {
                return localizedText;
            }

            // TODO: Match case insensitive

            // Handle if mul or qst are requested as per Part 4 rules
            if (preferredLocales[0].ToLowerInvariant() is kMulLocale or kQstLocale)
            {
                // If there are no further entries, return all languages available.
                // If there are more languages included after ‘mul’ or ‘qst’, return
                // only those languages from that list.
                if (preferredLocales.Count > 1 && Translations != null)
                {
                    var filtered = new Dictionary<string, string>();
                    for (int i = 1; i < preferredLocales.Count; i++)
                    {
                        if (Translations.TryGetValue(preferredLocales[i], out string t))
                        {
                            filtered.Add(preferredLocales[i], t);
                        }
                    }
                    if (filtered.Count > 0)
                    {
                        localizedText = new LocalizedText(filtered, TranslationInfo);
                    }
                }
                return localizedText.AsMultiLanguage();
            }

            if (Translations == null || Translations.Count == 0)
            {
                // No translations - return what we have
                return localizedText;
            }

            // Try to find the first matching locale and then return a formatted text or the raw text
            foreach (string locale in preferredLocales)
            {
                if (Translations.TryGetValue(locale, out string text))
                {
                    return new LocalizedText(locale, FormatText(locale, text));
                }
            }

            // Match language only e.g. en matches en-US and en-GB
            foreach (string locale in preferredLocales)
            {
                string language = locale.Split('-')[0];
                foreach (KeyValuePair<string, string> kvp in Translations)
                {
                    if (kvp.Key.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kvp.Key, language, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LocalizedText(kvp.Key, FormatText(kvp.Key, kvp.Value));
                    }
                }
            }

            // Return the first entry instead
            KeyValuePair<string, string> first = Translations.First();
            return new LocalizedText(first.Key, FormatText(first.Key, first.Value));
        }

        /// <summary>
        /// Check for multi language locale
        /// </summary>
        /// <param name="locale"></param>
        /// <returns></returns>
        public static bool IsMultiLanguage(string locale)
        {
            return string.Equals(locale, kMulLocale, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Encodes the translations to a JSON string according to the format specified
        /// in https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
        /// </summary>
        [Pure]
        public LocalizedText AsMultiLanguage(bool force = false)
        {
            var t = new List<string[]>();
            if (Translations == null || Translations.Count == 0)
            {
                // Get from translation info
                if (TranslationInfo.IsNull)
                {
                    return LocalizedText.Null;
                }
                t.Add([
                    TranslationInfo.Locale ?? "en-US",
                    FormatText(TranslationInfo.Text, string.Empty)]);
            }
            else
            {
                foreach (KeyValuePair<string, string> kvp in Translations)
                {
                    t.Add([kvp.Key, FormatText(kvp.Key, kvp.Value)]);
                }
            }
            if (t.Count == 1 && !force)
            {
                // No need to encode as mul locale if only one entry
                return new LocalizedText(t[0][0], t[0][1], this);
            }
            return new LocalizedText(kMulLocale, JsonSerializer.Serialize(
                new Dictionary<string, List<string[]>> { { kMulLocaleDictionaryKey, t } }), this);
        }

        /// <summary>
        /// Encodes the translations to a JSON string according to the format specified
        /// in https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
        /// </summary>
        public static LocalizedText EncodeAsMulLocale(LocalizedText localizedText)
        {
            if (localizedText.IsMultiLanguage)
            {
                return localizedText;
            }
            var t = new List<string[]>
            {
                new string[] { localizedText.Locale ?? "en-US", localizedText.Text ?? string.Empty }
            };
            return new LocalizedText(kMulLocale, JsonSerializer.Serialize(
                new Dictionary<string, List<string[]>> { { kMulLocaleDictionaryKey, t } }));
        }

        /// <summary>
        /// If this is a "mul" locale, returns a dictionary of locale/text pairs from the
        /// JSON Text. Otherwise, returns null. The expected JSON structure is defined in
        /// https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
        /// </summary>
        private static ReadOnlyDictionary<string, string> DecodeMulLocale(
            string encodedLocale,
            string encodedText)
        {
            if (!IsMultiLanguage(encodedLocale) || string.IsNullOrWhiteSpace(encodedText))
            {
                return null;
            }
            var result = new Dictionary<string, string>();
            try
            {
                Dictionary<string, List<string[]>> json =
                    JsonSerializer.Deserialize<Dictionary<string, List<string[]>>>(encodedText);
                if (json.TryGetValue(kMulLocaleDictionaryKey, out List<string[]> tValue))
                {
                    foreach (string[] pair in tValue)
                    {
                        if (pair.Length < 2)
                        {
                            continue;
                        }
                        string locale = pair[0];
                        string text = pair[1];
                        if (!string.IsNullOrEmpty(locale) && text != null)
                        {
                            result[locale] = text;
                        }
                    }
                }
            }
            catch
            {
                // TODO: Need to wire a logger here
                ILogger logger = AmbientMessageContext.Telemetry.CreateLogger<LocalizedText>();
                logger.LogDebug("Failed to parse mul locale JSON text: {Text}", encodedText);
                return null; // Return null if parsing fails
            }
            return new ReadOnlyDictionary<string, string>(result);
        }

        private const string kMulLocale = "mul";
        private const string kQstLocale = "qst";
        private const string kMulLocaleDictionaryKey = "t";
    }

    /// <summary>
    /// Helper to allow data contract serialization of LocalizedText
    /// </summary>
    [DataContract(
        Name = "LocalizedText",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableLocalizedText :
        IEquatable<LocalizedText>,
        IEquatable<SerializableLocalizedText>,
        ISurrogateFor<LocalizedText>
    {
        /// <summary>
        /// Create initialized localized text
        /// </summary>
        public SerializableLocalizedText()
        {
            Value = default;
        }

        /// <summary>
        /// Create initialized localized text
        /// </summary>
        public SerializableLocalizedText(LocalizedText value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public LocalizedText Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <inheritdoc/>
        [DataMember(Name = "Locale", Order = 1)]
        internal string XmlEncodedLocale
        {
            get => Value.Locale;
            set => Value = new LocalizedText(value, XmlEncodedText);
        }

        /// <inheritdoc/>
        [DataMember(Name = "Text", Order = 2)]
        internal string XmlEncodedText
        {
            get => Value.Text;
            set => Value = new LocalizedText(XmlEncodedLocale, value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableLocalizedText s => Equals(s),
                LocalizedText n => Equals(n),
                _ => Value.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(LocalizedText obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableLocalizedText obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(SerializableLocalizedText left, SerializableLocalizedText right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(SerializableLocalizedText left, SerializableLocalizedText right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(SerializableLocalizedText left, LocalizedText right)
        {
            return left is null ? right.IsNullOrEmpty : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(SerializableLocalizedText left, LocalizedText right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableLocalizedText(
            LocalizedText value)
        {
            return new SerializableLocalizedText(value);
        }

        /// <inheritdoc/>
        public static implicit operator LocalizedText(
            SerializableLocalizedText value)
        {
            return value.Value;
        }
    }
}
