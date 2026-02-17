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
using System.Threading;
using System.Xml;
#if !NETSTANDARD2_1_OR_GREATER && !NET6_0_OR_GREATER
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
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

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
            string key,
            string text,
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
        /// Translates a service result.
        /// </summary>
        public ServiceResult Translate(ArrayOf<string> preferredLocales, ServiceResult result)
        {
            if (result == null)
            {
                return null;
            }
            // translate localized text.
            LocalizedText translatedText;
            if (result.LocalizedText.IsNullOrEmpty)
            {
                // extract any additional arguments from the translation info.
                object[] args = null;

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
                        result.SymbolicId,
                        result.NamespaceUri,
                        args);
                }
                else
                {
                    translatedText = TranslateStatusCode(preferredLocales, result.StatusCode, args);
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
                Translate(preferredLocales, result.InnerResult));
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
                    availableLocales[ii] = m_translationTables[ii].Locale.Name;
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

            var culture = new CultureInfo(locale);

            if (culture.IsNeutralCulture)
            {
                throw new ArgumentException(
                    "Cannot specify neutral locales for translation tables.",
                    nameof(locale));
            }

            lock (m_lock)
            {
                TranslationTable table = GetTable(culture.Name);
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

            var culture = new CultureInfo(locale);

            if (culture.IsNeutralCulture)
            {
                throw new ArgumentException(
                    "Cannot specify neutral locales for translation tables.",
                    nameof(locale));
            }

            lock (m_lock)
            {
                TranslationTable table = GetTable(culture.Name);

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
                Add(id, "en-US", id.SymbolicId);
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
            // check for trivial case.
            if (string.IsNullOrEmpty(info.Text) && string.IsNullOrEmpty(info.Key))
            {
                return defaultText.FilterByPreferredLocales(preferredLocales);
            }

            defaultText = defaultText.WithTranslationInfo(info);
            bool isMultilanguageRequested =
                preferredLocales.Count > 0 &&
                preferredLocales.Span[0].ToLowerInvariant() is "mul" or "qst";

            // check for exact match.
            if (preferredLocales.Count > 0)
            {
                if (!defaultText.IsNullOrEmpty &&
                    !isMultilanguageRequested &&
                    preferredLocales.Span[0] == defaultText.Locale)
                {
                    return defaultText;
                }

                // MultiLanguageText requested, specified numer of locales was found in the default text.
                if (isMultilanguageRequested &&
                    preferredLocales.Count > 1 &&
                    defaultText.Translations?.Count == preferredLocales.Count - 1)
                {
                    return defaultText.AsMultiLanguage();
                }

                if (preferredLocales.Span[0] == info.Locale)
                {
                    return new LocalizedText(info);
                }
            }

            // get translation for multiLanguage request
            if (isMultilanguageRequested)
            {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
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
                                .TryGetValue(info.Key ?? info.Text, out string translation))
                            {
                                translations[table.Locale.Name] = translation;
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
                            string translation = FindBestTranslation(
                                preferredLocales.Slice(i, 1),
                                info.Key ?? info.Text,
                                out CultureInfo culture);
                            if (translation != null)
                            {
                                translations[preferredLocales.Span[i]] = translation;
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
                string translatedText = info.Text;
                CultureInfo culture = CultureInfo.InvariantCulture;

                lock (m_lock)
                {
                    translatedText = FindBestTranslation(
                        preferredLocales,
                        info.Key ?? info.Text,
                        out culture);

                    // use the default if no translation available.
                    if (translatedText == null)
                    {
                        return defaultText.FilterByPreferredLocales(preferredLocales);
                    }
                }

                // construct translated localized text.
                return new LocalizedText(culture.Name, translatedText, info);
            }
        }

        /// <summary>
        /// Stores the translations for a locale.
        /// </summary>
        private class TranslationTable
        {
            public CultureInfo Locale;
            public SortedDictionary<string, string> Translations = [];
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

                    if (translationTable.Locale.Name == locale)
                    {
                        return translationTable;
                    }
                }

                // add table.
                var table = new TranslationTable { Locale = new CultureInfo(locale) };
                m_translationTables.Add(table);

                return table;
            }
        }

        /// <summary>
        /// Finds the best translation for the requested locales.
        /// </summary>
        private string FindBestTranslation(
            ArrayOf<string> preferredLocales,
            string key,
            out CultureInfo culture)
        {
            culture = null;
            TranslationTable match = null;

            if (preferredLocales.Count == 0)
            {
                return null;
            }

            for (int jj = 0; jj < preferredLocales.Count; jj++)
            {
                // parse the locale.
                string language = preferredLocales.Span[jj];

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
                string translatedText = null;

                for (int ii = 0; ii < m_translationTables.Count; ii++)
                {
                    TranslationTable translationTable = m_translationTables[ii];

                    // all done if exact match found.
                    if (translationTable.Locale.Name == preferredLocales.Span[jj] &&
                        translationTable.Translations.TryGetValue(key, out translatedText))
                    {
                        culture = translationTable.Locale;
                        return translatedText;
                    }

                    // check for matching language but different region.
                    if (match == null &&
                        translationTable.Locale.TwoLetterISOLanguageName == language &&
                        translationTable.Translations.TryGetValue(key, out translatedText))
                    {
                        culture = translationTable.Locale;
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
            object[] args)
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

            return LocalizedText.From(Utils.Format("{0:X8}", statusCode.Code));
        }

        /// <summary>
        /// Translates a symbolic id.
        /// </summary>
        private LocalizedText TranslateSymbolicId(
            ArrayOf<string> preferredLocales,
            string symbolicId,
            string namespaceUri,
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

            return LocalizedText.From(symbolicId);
        }

        private readonly Lock m_lock = new();
        private readonly List<TranslationTable> m_translationTables;
        private Dictionary<StatusCode, TranslationInfo> m_statusCodeMapping;
        private Dictionary<XmlQualifiedName, TranslationInfo> m_symbolicIdMapping;
    }
}
