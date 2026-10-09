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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Xml;
#if !NET6_0_OR_GREATER
using System.Linq;
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages access to localized resources.
    /// </summary>
    public class ResourceManager : IDisposable, ITranslationManager
    {
        /// <summary>
        /// Initializes the resource manager with the server instance that owns it.
        /// </summary>
        public ResourceManager(ApplicationConfiguration configuration)
            : this(configuration, static locale => new CultureInfo(locale))
        {
        }

        /// <summary>
        /// Initializes the resource manager with the factory that creates the
        /// culture of a locale id.
        /// </summary>
        /// <param name="configuration">The configuration of the server.</param>
        /// <param name="createCulture">Creates the culture of a locale id, or throws
        /// <see cref="CultureNotFoundException"/> when the runtime cannot create it.</param>
        internal ResourceManager(
            ApplicationConfiguration configuration,
            Func<string, CultureInfo> createCulture)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_createCulture = createCulture ?? throw new ArgumentNullException(nameof(createCulture));
            m_translationTables = [];
        }

        /// <summary>
        /// May be called by the application to clean up resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Cleans up all resources held by the object.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // nothing to do at this time.
            }
        }

        /// <inheritdoc/>
        public virtual LocalizedText Translate(
            ArrayOf<string> preferredLocales,
            string? key,
            string? text,
            params object[] args)
        {
            return Translate(
                preferredLocales,
                default,
                new TranslationInfo(key, string.Empty, text, args));
        }

        /// <inheritdoc/>
        public LocalizedText Translate(ArrayOf<string> preferredLocales, LocalizedText text)
        {
            return Translate(preferredLocales, text, text.TranslationInfo);
        }

        /// <summary>
        /// Selects, for every <see cref="LocalizedText"/> held by the value (a scalar
        /// or a one-dimensional array), the translation that is the most preferred
        /// for the requested locales, or all translations when "mul" is requested
        /// (OPC 10000-4 5.4, OPC 10000-3 8.5). Any other value is returned unchanged.
        /// </summary>
        /// <remarks>
        /// The value is never modified in place, so a value stored in the address
        /// space keeps all its translations. Nothing is selected when no locale is
        /// requested, the stored value is returned as is.
        /// </remarks>
        /// <param name="preferredLocales">The locales of the session, most preferred first.</param>
        /// <param name="value">The value to translate.</param>
        /// <returns>The value with every localized text translated.</returns>
        public virtual Variant TranslateValue(ArrayOf<string> preferredLocales, Variant value)
        {
            if (preferredLocales.Count == 0 || value.IsNull)
            {
                return value;
            }

            if (value.TryGetValue(out LocalizedText text))
            {
                return Translate(preferredLocales, text);
            }

            if (value.TryGetValue(out ArrayOf<LocalizedText> texts) && texts.Count > 0)
            {
                // copy, the array may be shared with the value stored in the node.
                var translated = new LocalizedText[texts.Count];
                for (int ii = 0; ii < translated.Length; ii++)
                {
                    translated[ii] = Translate(preferredLocales, texts[ii]);
                }
                return Variant.From(translated.ToArrayOf());
            }

            return value;
        }

        /// <summary>
        /// Translates a service result.
        /// </summary>
        public ServiceResult Translate(ArrayOf<string> preferredLocales, ServiceResult result)
        {
            if (result == null)
            {
                return null!;
            }
            // translate localized text.
            LocalizedText translatedText;
            if (result.LocalizedText.IsNullOrEmpty)
            {
                // extract any additional arguments from the translation info.
                object[]? args = null;

                if (!result.LocalizedText.TranslationInfo.IsNull)
                {
                    TranslationInfo info = result.LocalizedText.TranslationInfo;

                    if (info.Args != null && info.Args.Length > 0)
                    {
                        args = info.Args;
                    }
                }

                if (!string.IsNullOrEmpty(result.SymbolicId))
                {
                    translatedText = TranslateSymbolicId(
                        preferredLocales,
                        result.SymbolicId!,
                        result.NamespaceUri!,
                        result.StatusCode,
                        args!);
                }
                else
                {
                    translatedText = TranslateStatusCode(preferredLocales, result.StatusCode, args!);
                }
            }
            else
            {
                if (preferredLocales.Count == 0)
                {
                    return result;
                }

                translatedText = Translate(preferredLocales, result.LocalizedText);
            }

            // construct new service result.
            return new ServiceResult(
                result.NamespaceUri,
                result.StatusCode,
                translatedText,
                result.AdditionalInfo,
                Translate(preferredLocales, result.InnerResult!));
        }

        /// <summary>
        /// Returns the locales supported by the resource manager.
        /// </summary>
        public virtual string[] GetAvailableLocales()
        {
            lock (m_lock)
            {
                string[] availableLocales = new string[m_translationTables.Count];

                for (int ii = 0; ii < m_translationTables.Count; ii++)
                {
                    availableLocales[ii] = m_translationTables[ii].Locale;
                }

                return availableLocales;
            }
        }

        /// <summary>
        /// Adds a translation to the resource manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public void Add(string key, string locale, string text)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (locale == null)
            {
                throw new ArgumentNullException(nameof(locale));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string localeId = GetLocaleId(locale, out bool isNeutral);

            if (isNeutral)
            {
                throw new ArgumentException(
                    "Cannot specify neutral locales for translation tables.",
                    nameof(locale));
            }

            lock (m_lock)
            {
                TranslationTable table = GetTable(localeId);
                table.Translations[key] = text;
            }
        }

        /// <summary>
        /// Adds the translations to the resource manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public void Add(string locale, IDictionary<string, string> translations)
        {
            if (locale == null)
            {
                throw new ArgumentNullException(nameof(locale));
            }

            if (translations == null)
            {
                throw new ArgumentNullException(nameof(translations));
            }

            string localeId = GetLocaleId(locale, out bool isNeutral);

            if (isNeutral)
            {
                throw new ArgumentException(
                    "Cannot specify neutral locales for translation tables.",
                    nameof(locale));
            }

            lock (m_lock)
            {
                TranslationTable table = GetTable(localeId);

                foreach (KeyValuePair<string, string> translation in translations)
                {
                    table.Translations[translation.Key] = translation.Value;
                }
            }
        }

        /// <summary>
        /// Adds the translations to the resource manager.
        /// </summary>
        public void Add(StatusCode statusCode, string locale, string text)
        {
            lock (m_lock)
            {
                string key = statusCode.ToString(null, CultureInfo.InvariantCulture);

                Add(key, locale, text);

                m_statusCodeMapping ??= [];

                if (string.IsNullOrEmpty(locale) || locale == "en-US")
                {
                    m_statusCodeMapping[statusCode] = new TranslationInfo(key, locale, text);
                }
            }
        }

        /// <summary>
        /// Adds the translations to the resource manager.
        /// </summary>
        public void Add(XmlQualifiedName symbolicId, string locale, string text)
        {
            lock (m_lock)
            {
                if (symbolicId != null)
                {
                    string key = symbolicId.ToString();

                    Add(key, locale, text);

                    m_symbolicIdMapping ??= [];

                    if (string.IsNullOrEmpty(locale) || locale == "en-US")
                    {
                        m_symbolicIdMapping[symbolicId] = new TranslationInfo(key, locale, text);
                    }
                }
            }
        }

        /// <summary>
        /// Uses reflection to load default text for standard StatusCodes.
        /// </summary>
        public void LoadDefaultText()
        {
            foreach (StatusCode id in StatusCode.InternedStatusCodes)
            {
                Add(id, "en-US", id.SymbolicId!);
            }
        }

        /// <summary>
        /// Translates the text provided.
        /// </summary>
        protected virtual LocalizedText Translate(
            ArrayOf<string> preferredLocales,
            LocalizedText defaultText,
            TranslationInfo info)
        {
            // a null LocaleId is legal wire input (Part 3 8.4) and means unknown.
            preferredLocales = RemoveUnknownLocales(preferredLocales);

            // check for trivial case.
            if (string.IsNullOrEmpty(info.Text) && string.IsNullOrEmpty(info.Key))
            {
                return defaultText.FilterByPreferredLocales(preferredLocales);
            }

            defaultText = defaultText.WithTranslationInfo(info);
            bool isMultilanguageRequested =
                preferredLocales.Count > 0 &&
                preferredLocales[0]?.ToLowerInvariant() is "mul" or "qst";

            // check for exact match.
            if (preferredLocales.Count > 0)
            {
                // locale ids are compared case-insensitively (RFC 5646).
                if (!defaultText.IsNullOrEmpty &&
                    !isMultilanguageRequested &&
                    string.Equals(preferredLocales[0], defaultText.Locale, StringComparison.OrdinalIgnoreCase))
                {
                    return defaultText;
                }

                // MultiLanguageText requested and the default text holds exactly the
                // requested locales.
                if (isMultilanguageRequested &&
                    preferredLocales.Count > 1 &&
                    defaultText.Translations?.Count == preferredLocales.Count - 1 &&
                    ContainsAllLocales(defaultText.Translations, preferredLocales))
                {
                    return defaultText.AsMultiLanguage();
                }

                if (string.Equals(preferredLocales[0], info.Locale, StringComparison.OrdinalIgnoreCase))
                {
                    return new LocalizedText(info);
                }
            }

            // get translation for multiLanguage request
            if (isMultilanguageRequested)
            {
#if NET6_0_OR_GREATER
                Dictionary<string, string> translations =
                    defaultText.Translations != null
                        ? new Dictionary<string, string>(defaultText.Translations)
                        : [];
#else
                Dictionary<string, string> translations =
                    defaultText.Translations != null
                        ? new Dictionary<string, string>(
                            defaultText.Translations.ToDictionary(s => s.Key, s => s.Value))
                        : [];
#endif
                // If only mul/qst is requested, return all available translations for the key.
                if (preferredLocales.Count == 1)
                {
                    lock (m_lock)
                    {
                        foreach (TranslationTable table in m_translationTables)
                        {
                            if (table.Translations
                                .TryGetValue((info.Key ?? info.Text)!, out string? translation))
                            {
                                translations[table.Locale] = translation!;
                            }
                        }
                    }
                }
                else
                {
                    // mul/qst + specific locales: return only those translations
                    lock (m_lock)
                    {
                        for (int i = 1; i < preferredLocales.Count; i++)
                        {
                            string? translation = FindBestTranslation(
                                preferredLocales.Slice(i, 1),
                                (info.Key ?? info.Text)!,
                                out string locale);
                            if (translation != null)
                            {
                                // label the text with the locale of the table it came
                                // from, which may be another region of the requested
                                // language.
                                translations[locale] = translation;
                            }
                        }
                    }
                }
                return defaultText
                    .WithTranslations(translations)
                    .FilterByPreferredLocales(preferredLocales)
                    .AsMultiLanguage();
            }
            // single locale requested.
            else
            {
                // find the best translation.
                string? translatedText;
                string locale;

                lock (m_lock)
                {
                    translatedText = FindBestTranslation(
                        preferredLocales,
                        (info.Key ?? info.Text)!,
                        out locale);

                    // use the default if no translation available.
                    if (translatedText == null)
                    {
                        return preferredLocales.Count > 0 && defaultText.Translations == null && info.Text != null
                            ? new LocalizedText(info)
                            : defaultText.FilterByPreferredLocales(preferredLocales);
                    }
                }

                // construct translated localized text.
                return new LocalizedText(locale, translatedText, info);
            }
        }

        /// <summary>
        /// Stores the translations for a locale.
        /// </summary>
        private sealed class TranslationTable
        {
            /// <summary>
            /// Creates an empty table for a locale id.
            /// </summary>
            public TranslationTable(string locale)
            {
                Locale = locale;
                Language = GetLanguage(locale);
            }

            /// <summary>
            /// The locale id in the casing of its culture.
            /// </summary>
            public string Locale { get; }

            /// <summary>
            /// The language of the locale id, used to fall back to another region.
            /// </summary>
            public string Language { get; }

            /// <summary>
            /// The translations by key.
            /// </summary>
            public SortedDictionary<string, string> Translations { get; } = [];
        }

        /// <summary>
        /// Finds the translation table for the locale. Creates a new table if it does not exist.
        /// </summary>
        private TranslationTable GetTable(string locale)
        {
            lock (m_lock)
            {
                // search for table.
                for (int ii = 0; ii < m_translationTables.Count; ii++)
                {
                    TranslationTable translationTable = m_translationTables[ii];

                    if (translationTable.Locale == locale)
                    {
                        return translationTable;
                    }
                }

                // add table.
                var table = new TranslationTable(locale);
                m_translationTables.Add(table);

                return table;
            }
        }

        /// <summary>
        /// Returns the locale id of a translation table in the casing of its culture,
        /// and whether the locale id names a language without a region.
        /// </summary>
        /// <remarks>
        /// When the runtime cannot create the culture, as in globalization-invariant
        /// mode, where it creates no culture other than the invariant culture, a
        /// well-formed locale id is parsed instead.
        /// </remarks>
        /// <exception cref="CultureNotFoundException">The locale id is malformed.</exception>
        private string GetLocaleId(string locale, out bool isNeutral)
        {
            lock (m_lock)
            {
                if (m_localeIds.TryGetValue(locale, out (string LocaleId, bool IsNeutral) cached))
                {
                    isNeutral = cached.IsNeutral;
                    return cached.LocaleId;
                }

                string localeId;
                try
                {
                    CultureInfo culture = m_createCulture(locale);
                    localeId = culture.Name;
                    isNeutral = culture.IsNeutralCulture;
                }
                catch (CultureNotFoundException) when (TryParseLocaleId(locale, out localeId, out isNeutral))
                {
                    // the runtime cannot create the culture of a well-formed locale id.
                }

                m_localeIds[locale] = (localeId, isNeutral);
                return localeId;
            }
        }

        /// <summary>
        /// Parses a well-formed locale id (an RFC 5646 language tag) into the casing
        /// of its culture, and returns whether it names a language without a region.
        /// </summary>
        private static bool TryParseLocaleId(string locale, out string localeId, out bool isNeutral)
        {
            localeId = string.Empty;
            isNeutral = false;

            string[] subtags = locale.Split('-');
            string language = subtags[0];
            if (language.Length is < 2 or > 8 || !IsAsciiLetters(language))
            {
                return false;
            }

            var builder = new StringBuilder(locale.Length);
            builder.Append(language.ToLowerInvariant());

            // the script and the region can only follow the language, in this order.
            int regionIndex = 1;
            bool hasRegion = false;
            bool isExtension = false;
            for (int ii = 1; ii < subtags.Length; ii++)
            {
                string subtag = subtags[ii];
                if (subtag.Length is < 1 or > 8 || !IsAsciiLettersOrDigits(subtag))
                {
                    return false;
                }

                builder.Append('-');

                // a singleton starts an extension or a private use sequence.
                isExtension |= subtag.Length == 1;

                if (!isExtension && ii == 1 && subtag.Length == 4 && IsAsciiLetters(subtag))
                {
                    builder.Append(char.ToUpperInvariant(subtag[0]))
                        .Append(subtag[1..].ToLowerInvariant());
                    regionIndex = 2;
                }
                else if (!isExtension &&
                    ii == regionIndex &&
                    ((subtag.Length == 2 && IsAsciiLetters(subtag)) ||
                        (subtag.Length == 3 && IsAsciiDigits(subtag))))
                {
                    builder.Append(subtag.ToUpperInvariant());
                    hasRegion = true;
                }
                else
                {
                    builder.Append(subtag.ToLowerInvariant());
                }
            }

            localeId = builder.ToString();
            isNeutral = !hasRegion;
            return true;
        }

        /// <summary>
        /// Returns the language subtag of a locale id.
        /// </summary>
        private static string GetLanguage(string locale)
        {
            int index = locale.IndexOf('-', StringComparison.Ordinal);
            return index == -1 ? locale : locale[..index];
        }

        /// <summary>
        /// Returns true if the value contains only ASCII letters.
        /// </summary>
        private static bool IsAsciiLetters(string value)
        {
            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z')))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if the value contains only ASCII digits.
        /// </summary>
        private static bool IsAsciiDigits(string value)
        {
            foreach (char c in value)
            {
                if (c is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if the value contains only ASCII letters and digits.
        /// </summary>
        private static bool IsAsciiLettersOrDigits(string value)
        {
            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Removes the null (unknown) entries from the requested locales.
        /// </summary>
        private static ArrayOf<string> RemoveUnknownLocales(ArrayOf<string> preferredLocales)
        {
            for (int ii = 0; ii < preferredLocales.Count; ii++)
            {
                if (preferredLocales[ii] != null)
                {
                    continue;
                }

                var locales = new List<string>(preferredLocales.Count);
                for (int jj = 0; jj < preferredLocales.Count; jj++)
                {
                    if (preferredLocales[jj] != null)
                    {
                        locales.Add(preferredLocales[jj]);
                    }
                }
                return locales.ToArrayOf();
            }
            return preferredLocales;
        }

        /// <summary>
        /// Returns true if every locale requested after "mul" or "qst" has a
        /// translation (compared case-insensitively).
        /// </summary>
        private static bool ContainsAllLocales(
            IReadOnlyDictionary<string, string> translations,
            ArrayOf<string> preferredLocales)
        {
            for (int ii = 1; ii < preferredLocales.Count; ii++)
            {
                string requested = preferredLocales[ii];
                if (translations.ContainsKey(requested))
                {
                    continue;
                }
                bool found = false;
                foreach (string locale in translations.Keys)
                {
                    if (string.Equals(locale, requested, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Finds the best translation for the requested locales.
        /// </summary>
        private string? FindBestTranslation(
            ArrayOf<string> preferredLocales,
            string key,
            out string locale)
        {
            locale = string.Empty;
            TranslationTable? match = null;

            if (preferredLocales.Count == 0)
            {
                return null;
            }

            for (int jj = 0; jj < preferredLocales.Count; jj++)
            {
                // parse the locale.
                string language = preferredLocales[jj];

                if (language == null)
                {
                    continue;
                }

                int index = language.IndexOf('-', StringComparison.Ordinal);

                if (index != -1)
                {
                    language = language[..index];
                }

                // search for translation.
                string? translatedText = null;

                for (int ii = 0; ii < m_translationTables.Count; ii++)
                {
                    TranslationTable translationTable = m_translationTables[ii];

                    // all done if exact match found (locale ids are case-insensitive, RFC 5646).
                    if (string.Equals(
                            translationTable.Locale,
                            preferredLocales[jj],
                            StringComparison.OrdinalIgnoreCase) &&
                        translationTable.Translations.TryGetValue(key, out string? exactMatch))
                    {
                        locale = translationTable.Locale;
                        return exactMatch;
                    }

                    // check for matching language but different region.
                    if (match == null &&
                        string.Equals(
                            translationTable.Language,
                            language,
                            StringComparison.OrdinalIgnoreCase) &&
                        translationTable.Translations.TryGetValue(key, out translatedText))
                    {
                        locale = translationTable.Locale;
                        match = translationTable;
                    }
                }

                // take a partial match if one found.
                if (match != null)
                {
                    return translatedText;
                }
            }

            // no translations available.
            return null;
        }

        /// <summary>
        /// Translates a status code.
        /// </summary>
        private LocalizedText TranslateStatusCode(
            ArrayOf<string> preferredLocales,
            StatusCode statusCode,
            object[] args,
            string? symbolicId = null)
        {
            lock (m_lock)
            {
                if (m_statusCodeMapping != null &&
                    m_statusCodeMapping.TryGetValue(statusCode.Code, out TranslationInfo info))
                {
                    // merge the argument list with the translation info cached for the status code.
                    if (args != null)
                    {
                        info = new TranslationInfo(info.Key, info.Locale, info.Text, args);
                    }

                    return Translate(preferredLocales, default, info);
                }
            }

            return LocalizedText.From(symbolicId ?? Utils.Format("{0:X8}", statusCode.Code));
        }

        /// <summary>
        /// Translates a symbolic id.
        /// </summary>
        private LocalizedText TranslateSymbolicId(
            ArrayOf<string> preferredLocales,
            string symbolicId,
            string namespaceUri,
            StatusCode statusCode,
            object[] args)
        {
            lock (m_lock)
            {
                if (m_symbolicIdMapping != null &&
                    m_symbolicIdMapping.TryGetValue(
                        new XmlQualifiedName(symbolicId, namespaceUri),
                        out TranslationInfo info))
                {
                    // merge the argument list with the translation info cached for the symbolic id.
                    if (args != null)
                    {
                        info = new TranslationInfo(info.Key, info.Locale, info.Text, args);
                    }

                    return Translate(preferredLocales, default, info);
                }
            }

            if ((string.IsNullOrEmpty(namespaceUri) || namespaceUri == Ua.Namespaces.OpcUa) &&
                symbolicId == new StatusCode(statusCode.Code).SymbolicId)
            {
                return TranslateStatusCode(preferredLocales, statusCode, args, symbolicId);
            }

            return LocalizedText.From(symbolicId);
        }

        private readonly Lock m_lock = new();
        private readonly Func<string, CultureInfo> m_createCulture;
        private readonly Dictionary<string, (string LocaleId, bool IsNeutral)> m_localeIds =
            new(StringComparer.Ordinal);
        private readonly List<TranslationTable> m_translationTables;
        private Dictionary<StatusCode, TranslationInfo>? m_statusCodeMapping;
        private Dictionary<XmlQualifiedName, TranslationInfo>? m_symbolicIdMapping;
    }
}
