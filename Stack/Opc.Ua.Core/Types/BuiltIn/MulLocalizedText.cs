/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static System.Net.Mime.MediaTypeNames;

namespace Opc.Ua
{
    /// <summary>
    /// Localized text extensions to support multi-language text according to OPC UA Part 3.
    /// </summary>
    public static class MulLocalizedText
    {
        private const string kMulLocale = "mul";
        private const string kMulLocaleDictionaryKey = "t";

        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// </summary>
        public static LocalizedText Create(
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
        public static LocalizedText Create(TranslationInfo translationInfo)
        {
            var localizedText = new LocalizedText(translationInfo);
            return localizedText.DecodeMulLocale();
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        public static LocalizedText Create(string locale, string text)
        {
            var localizedText = new LocalizedText(locale, text);
            return localizedText.DecodeMulLocale();
        }

        /// <summary>
        /// Initializes the locale with a key, locale and text.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="locale">The locale for the text provides</param>
        /// <param name="text">The localized text</param>
        public static LocalizedText Create(string key, string locale, string text)
        {
            var localizedText = new LocalizedText(locale, text);
            if (!string.IsNullOrEmpty(key))
            {
                localizedText.TranslationInfo = new TranslationInfo(key, locale, text);
            }
            localizedText.DecodeMulLocale();
            return localizedText;
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="translations">key = locale, value = text</param>
        public static LocalizedText Create(IReadOnlyDictionary<string, string> translations)
        {
            var localizedText = new LocalizedText
            {
                Translations = translations
            };
            return localizedText.EncodeMulLocale(translations);
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="translations">key = locale, value = text</param>
        public static LocalizedText Create(string key, IReadOnlyDictionary<string, string> translations)
        {
            var localizedText = new LocalizedText()
            {
                Translations = translations
            };
            if (!string.IsNullOrEmpty(key))
            {
                localizedText.TranslationInfo = new TranslationInfo(
                    key,
                    localizedText.XmlEncodedLocale,
                    localizedText.XmlEncodedText);
            }
            return localizedText.EncodeMulLocale(translations);
        }

        /// <summary>
        /// Copy localized text and decode the "mul" locale if applicable.
        /// </summary>
        /// <param name="text">The text to copy</param>
        public static LocalizedText Copy(this LocalizedText text)
        {
            return text.DecodeMulLocale();
        }

        /// <summary>
        /// Returns true if this LocalizedText uses the "mul" special locale.
        /// </summary>
        public static bool IsMultiLanguage(this LocalizedText text)
        {
            return string.Equals(text.XmlEncodedLocale, kMulLocale, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a LocalizedText filtered by the preferred locales according
        /// to OPC UA Part 4 rules for 'mul' and 'qst'.
        /// (https://reference.opcfoundation.org/Core/Part4/v105/docs/5.4)
        /// </summary>
        /// <param name="localizedText">Text to filter</param>
        /// <param name="preferredLocales">The list of preferred locales, possibly
        /// including 'mul' or 'qst' as the first entry.</param>
        /// <returns>A LocalizedText containing translations as specified by the rules.</returns>
        public static LocalizedText FilterByPreferredLocales(
            this LocalizedText localizedText,
            IList<string> preferredLocales)
        {
            if (localizedText == null ||
                preferredLocales == null ||
                preferredLocales.Count == 0 ||
                localizedText.XmlEncodedLocale == null)
            {
                return localizedText;
            }

            KeyValuePair<string, string> defaultKVP;
            bool isMultilanguageRequested = preferredLocales[0]
                .ToLowerInvariant() is "mul" or "qst";

            // If not a multi-language request, return the best match or fallback
            if (!isMultilanguageRequested)
            {
                if (!localizedText.IsMultiLanguage())
                {
                    // nothing to do for single locale text
                    return localizedText;
                }

                // Try to find the first matching locale
                foreach (string locale in preferredLocales)
                {
                    if (localizedText.Translations.TryGetValue(locale, out string text))
                    {
                        return new LocalizedText(locale, text);
                    }
                }
                // return the first available locale
                defaultKVP = localizedText.Translations.First();
                return new LocalizedText(defaultKVP.Key, defaultKVP.Value);
            }

            // Multi-language request: 'mul' or 'qst'
            if (preferredLocales.Count == 1)
            {
                return localizedText;
            }
            if (!localizedText.IsMultiLanguage())
            {
                // nothing to do for single locale text
                return localizedText;
            }

            var translations = new ReadOnlyDictionary<string, string>(
                localizedText.Translations.Where(t => preferredLocales.Contains(t.Key))
                    .ToDictionary(s => s.Key, s => s.Value));

            // If matching locales are found return those
            if (translations.Count > 0)
            {
                return Create(translations);
            }
            defaultKVP = localizedText.Translations.First();

            return new LocalizedText(defaultKVP.Key, defaultKVP.Value);
        }

        /// <summary>
        /// Encodes the translations to a JSON string according to the format
        /// specified in https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="translations"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static LocalizedText EncodeMulLocale(
            this LocalizedText text,
            IReadOnlyDictionary<string, string> translations)
        {
            if (translations == null)
            {
                throw new ArgumentNullException(nameof(translations));
            }

            if (translations.Count == 0)
            {
                throw new ArgumentException(
                    "The translations dictionary must not be empty.",
                    nameof(translations));
            }

            var t = new List<object[]>();
            foreach (KeyValuePair<string, string> kvp in translations)
            {
                t.Add([kvp.Key, kvp.Value]);
            }

            return new LocalizedText(
                kMulLocale,
                JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    { kMulLocaleDictionaryKey, t }
                }));
        }

        /// <summary>
        /// If this is a "mul" locale, returns a dictionary of locale/text pairs
        /// from the JSON Text.
        /// Otherwise, returns null.
        /// </summary>
        public static LocalizedText DecodeMulLocale(this LocalizedText localizedText)
        {
            if (!localizedText.IsMultiLanguage() ||
                string.IsNullOrWhiteSpace(localizedText.XmlEncodedText))
            {
                return null;
            }

            var result = new Dictionary<string, string>();
            try
            {
                // The expected JSON structure is defined in
                // https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
                Dictionary<string, object> json =
                    JsonConvert.DeserializeObject<Dictionary<string, object>>(localizedText.XmlEncodedText);
                if (json != null &&
                    json.TryGetValue(kMulLocaleDictionaryKey, out object tValue) &&
                    tValue is Newtonsoft.Json.Linq.JArray tArray)
                {
                    foreach (Newtonsoft.Json.Linq.JToken pairToken in tArray)
                    {
                        if (pairToken is Newtonsoft.Json.Linq.JArray pair && pair.Count == 2)
                        {
                            string locale = pair[0]?.ToString();
                            string text = pair[1]?.ToString();
                            if (!string.IsNullOrEmpty(locale) && text != null)
                            {
                                result[locale] = text;
                            }
                        }
                    }
                }
            }
            catch
            {
                // TODO: Need to wire a logger here
                ITelemetryContext telemetry = AmbientMessageContext.Telemetry;
                ILogger logger = telemetry != null ?
                     telemetry.CreateLogger<LocalizedText>() : LoggerUtils.Fallback.Logger;
                logger.LogDebug("Failed to parse mul locale JSON text: {Text}", localizedText.XmlEncodedText);
                return null; // Return null if parsing fails
            }
            return new LocalizedText(localizedText)
            {
                Translations = new ReadOnlyDictionary<string, string>(result)
            };
        }
    }
}
