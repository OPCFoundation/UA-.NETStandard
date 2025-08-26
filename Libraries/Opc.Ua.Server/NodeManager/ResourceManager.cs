/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
            IList<string> preferredLocales,
            string key,
            string text,
            params object[] args)
        {
            return Translate(
                preferredLocales,
                null,
                new TranslationInfo(key, string.Empty, text, args));
        }

        /// <inheritdoc/>
        public LocalizedText Translate(IList<string> preferredLocales, LocalizedText text)
        {
            return Translate(preferredLocales, text, text.TranslationInfo);
        }

        /// <summary>
        /// Translates a service result.
        /// </summary>
        public ServiceResult Translate(IList<string> preferredLocales, ServiceResult result)
        {
            if (result == null)
            {
                return null;
            }
            // translate localized text.
            LocalizedText translatedText;
            if (LocalizedText.IsNullOrEmpty(result.LocalizedText))
            {
                // extract any additional arguments from the translation info.
                object[] args = null;

                if (result.LocalizedText != null && result.LocalizedText.TranslationInfo != null)
                {
                    TranslationInfo info = result.LocalizedText.TranslationInfo;

                    if (info != null && info.Args != null && info.Args.Length > 0)
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
                if (preferredLocales == null || preferredLocales.Count == 0)
                {
                    return result;
                }

                translatedText = Translate(preferredLocales, result.LocalizedText);
            }

            // construct new service result.
            return new ServiceResult(
                result.StatusCode,
                result.SymbolicId,
                result.NamespaceUri,
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
        public void Add(uint statusCode, string locale, string text)
        {
            lock (m_lock)
            {
                string key = statusCode.ToString(CultureInfo.InvariantCulture);

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
            foreach (
                System.Reflection.FieldInfo field in typeof(StatusCodes).GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                uint? id = field.GetValue(typeof(StatusCodes)) as uint?;

                if (id != null)
                {
                    Add(id.Value, "en-US", field.Name);
                }
            }
        }

        /// <summary>
        /// Translates the text provided.
        /// </summary>
        protected virtual LocalizedText Translate(
            IList<string> preferredLocales,
            LocalizedText defaultText,
            TranslationInfo info)
        {
            defaultText = defaultText?.FilterByPreferredLocales(preferredLocales);

            bool isMultilanguageRequested =
                preferredLocales?.Count > 0 &&
                preferredLocales[0].ToLowerInvariant() is "mul" or "qst";

            // check for trivial case.
            if (info == null || (string.IsNullOrEmpty(info.Text) && string.IsNullOrEmpty(info.Key)))
            {
                return defaultText;
            }

            // check for exact match.
            if (preferredLocales != null && preferredLocales.Count > 0)
            {
                if (defaultText != null &&
                    !isMultilanguageRequested &&
                    preferredLocales[0] == defaultText?.Locale)
                {
                    return defaultText;
                }

                // MultiLanguageText requested, specified numer of locales was found in the default text.
                if (isMultilanguageRequested &&
                    preferredLocales.Count > 1 &&
                    defaultText?.Translations?.Count == preferredLocales.Count - 1)
                {
                    return defaultText;
                }

                if (preferredLocales[0] == info.Locale)
                {
                    return new LocalizedText(info);
                }
            }

            // get translation for multiLanguage request
            if (isMultilanguageRequested)
            {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                Dictionary<string, string> translations =
                    defaultText?.Translations != null
                        ? new Dictionary<string, string>(defaultText.Translations)
                        : [];
#else
                Dictionary<string, string> translations =
                    defaultText?.Translations != null
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
                                // format translated text.
                                if (info.Args?.Length > 0)
                                {
                                    try
                                    {
                                        translation = string.Format(
                                            table.Locale,
                                            translation,
                                            info.Args);
                                    }
                                    catch
                                    {
                                    }
                                }

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
                                [preferredLocales[i]],
                                info.Key ?? info.Text,
                                out CultureInfo culture);

                            if (translation != null)
                            {
                                // format translated text.
                                if (info.Args?.Length > 0)
                                {
                                    try
                                    {
                                        translation = string.Format(
                                            culture,
                                            translation,
                                            info.Args);
                                    }
                                    catch
                                    {
                                    }
                                }

                                translations[preferredLocales[i]] = translation;
                            }
                        }
                    }
                }
                return new LocalizedText(translations);
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
                        return defaultText;
                    }

                    // get a culture to use for formatting
                    if (culture == null &&
                        info.Args?.Length > 0 &&
                        !string.IsNullOrEmpty(info.Locale))
                    {
                        try
                        {
                            culture = new CultureInfo(info.Locale);
                        }
                        catch
                        {
                            culture = CultureInfo.InvariantCulture;
                        }
                    }
                }

                // format translated text.
                string formattedText = translatedText;

                if (info.Args?.Length > 0)
                {
                    try
                    {
                        formattedText = string.Format(culture, translatedText, info.Args);
                    }
                    catch
                    {
                        formattedText = translatedText;
                    }
                }

                // construct translated localized text.
                return new LocalizedText(culture.Name, formattedText) { TranslationInfo = info };
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
            IList<string> preferredLocales,
            string key,
            out CultureInfo culture)
        {
            culture = null;
            TranslationTable match = null;

            if (preferredLocales == null || preferredLocales.Count == 0)
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
                string translatedText = null;

                for (int ii = 0; ii < m_translationTables.Count; ii++)
                {
                    TranslationTable translationTable = m_translationTables[ii];

                    // all done if exact match found.
                    if (translationTable.Locale.Name == preferredLocales[jj] &&
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
            IList<string> preferredLocales,
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

                    return Translate(preferredLocales, null, info);
                }
            }

            return Utils.Format("{0:X8}", statusCode.Code);
        }

        /// <summary>
        /// Translates a symbolic id.
        /// </summary>
        private LocalizedText TranslateSymbolicId(
            IList<string> preferredLocales,
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

                    return Translate(preferredLocales, null, info);
                }
            }

            return symbolicId;
        }

        private readonly Lock m_lock = new();
        private readonly List<TranslationTable> m_translationTables;
        private Dictionary<uint, TranslationInfo> m_statusCodeMapping;
        private Dictionary<XmlQualifiedName, TranslationInfo> m_symbolicIdMapping;
    }
}
