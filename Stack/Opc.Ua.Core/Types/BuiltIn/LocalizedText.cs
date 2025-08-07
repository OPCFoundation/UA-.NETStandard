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
using Newtonsoft.Json;

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
    /// This class provides a wrapper for posting text that is qualified with the locale that it is
    /// intended for.
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
    /// Utils.LogInfo( welcomeUS.ToString() );
    /// Utils.LogInfo( welcomeGB.ToString() );
    /// Utils.LogInfo( welcomeNoLocale.ToString() );
    /// </code>
    /// <code lang="Visual Basic">
    /// Dim welcomeUS As LocalizedText = New LocalizedText( "Hi Everyone", "EN-GB" )
    /// Dim welcomeGB As LocalizedText = New LocalizedText( "Hello Everyone", "EN-GB" )
    /// Dim welcomeNoLocale As LocalizedText = New LocalizedText( "Welcome" )
    ///
    /// Utils.LogInfo( welcomeUS.ToString() )
    /// Utils.LogInfo( welcomeGB.ToString() )
    /// Utils.LogInfo( welcomeNoLocale.ToString() )
    /// </code>
    /// <para>
    /// This produces the following output:<br/>
    /// [EN-US]:Hi Everyone<br/>
    /// [EN-GB]:Hello Everyone<br/>
    /// Welcome<br/>
    /// </para>
    /// </example>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial class LocalizedText : ICloneable, IFormattable
    {
        private const string kMulLocale = "mul";
        private const string kMulLocaleDictionaryKey = "t";

        #region Constructors
        /// <summary>
        /// Initializes the object with the default values.
        /// </summary>
        /// <remarks>
        /// Initializes the object with the default values.
        /// </remarks>
        private LocalizedText()
        {
            XmlEncodedLocale = null;
            m_text = null;
        }


        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// </summary>
        public LocalizedText(string key, string locale, string text, params object[] args)
        :
            this(new TranslationInfo(key, locale, text, args))
        {
        }

        /// <summary>
        /// Creates text from a TranslationInfo object.
        /// </summary>
        public LocalizedText(TranslationInfo translationInfo)
        {
            if (translationInfo == null)
            {
                throw new ArgumentNullException(nameof(translationInfo));
            }

            XmlEncodedLocale = translationInfo.Locale;
            m_text = translationInfo.Text;
            TranslationInfo = translationInfo;

            if (TranslationInfo.Args == null || TranslationInfo.Args.Length == 0)
            {
                return;
            }

            CultureInfo culture = CultureInfo.InvariantCulture;

            if (!string.IsNullOrEmpty(XmlEncodedLocale))
            {
                try
                {
                    culture = new CultureInfo(XmlEncodedLocale);
                }
                catch
                {
                    culture = CultureInfo.InvariantCulture;
                }
            }

            try
            {
                m_text = string.Format(culture, TranslationInfo.Text, TranslationInfo.Args);
            }
            catch
            {
                m_text = TranslationInfo.Text;
            }
            m_translations = DecodeMulLocale();
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the value.
        /// </remarks>
        /// <param name="value">The text to create an instance from</param>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public LocalizedText(LocalizedText value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            XmlEncodedLocale = value.XmlEncodedLocale;
            m_text = value.m_text;
            m_translations = DecodeMulLocale();
        }

        /// <summary>
        /// Initializes the object with a text and the default locale.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a text and the default locale.
        /// </remarks>
        /// <param name="text">The plain text stored within this object</param>
        public LocalizedText(string text)
        {
            XmlEncodedLocale = null;
            m_text = text;
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a locale and text.
        /// </remarks>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        public LocalizedText(string locale, string text)
        {
            XmlEncodedLocale = locale;
            m_text = text;
            m_translations = DecodeMulLocale();
        }

        /// <summary>
        /// Initializes the locale with a key, locale and text.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="locale">The locale for the text provides</param>
        /// <param name="text">The localized text</param>
        public LocalizedText(string key, string locale, string text)
        {
            XmlEncodedLocale = locale;
            m_text = text;

            if (!string.IsNullOrEmpty(key))
            {
                TranslationInfo = new TranslationInfo(key, locale, text);
            }
            m_translations = DecodeMulLocale();
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="translations">key = locale, value = text</param>
        public LocalizedText(IReadOnlyDictionary<string, string> translations)
        {
            Translations = translations;
        }

        /// <summary>
        /// Creates a LocalizedText object from a dictionary of translations.
        /// The dictionary must contain at least one entry.
        /// Results in a localized text using the "mul" locale.
        /// </summary>
        /// <param name="translations">key = locale, value = text</param>
        /// <param name="key">A key used to look up the text for different locales</param>
        public LocalizedText(string key, IReadOnlyDictionary<string, string> translations)
        {
            Translations = translations;

            if (!string.IsNullOrEmpty(key))
            {
                TranslationInfo = new TranslationInfo(key, XmlEncodedLocale, m_text);
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The locale used to create the text.
        /// </summary>
        /// <remarks>
        /// The locale used to create the text.
        /// </remarks>
        public string Locale => XmlEncodedLocale;

        /// <inheritdoc/>
        [DataMember(Name = "Locale", Order = 1)]
        internal string XmlEncodedLocale { get; set; }

        /// <summary>
        /// The localized text.
        /// </summary>
        /// <remarks>
        /// The localized text.
        /// </remarks>
        public string Text => m_text;

        /// <summary>
        /// The decoded translations if the Localized Text is a mul locale.
        /// If the LocalizedText is not a mul locale, this property will return a dictionary with one entry from the Text and Locale properties.
        /// If the translations property is set a mul locale will be created.
        /// Key = locale, value = text.
        /// </summary>
        public IReadOnlyDictionary<string, string> Translations
        {
            get
            {
                if (m_translations == null && XmlEncodedLocale != null)
                {
                    return new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { { XmlEncodedLocale, m_text } });
                }
                return m_translations;
            }

            set
            {
                if (value == null || value.Count == 0)
                {
                    m_translations = null;
                    return;
                }
                // if the dictionary contains only one entry, use the first entry as the locale and text.
                if (value.Count == 1)
                {
                    foreach (KeyValuePair<string, string> kvp in value)
                    {
                        XmlEncodedLocale = kvp.Key;
                        m_text = kvp.Value;
                        m_translations = null;
                        return;
                    }
                }
                //Encode the dictionary to a mul locale.
                m_translations = value;
                XmlEncodedLocale = kMulLocale;
                m_text = EncodeMulLocale(m_translations);
            }
        }

        /// <inheritdoc/>
        [DataMember(Name = "Text", Order = 2)]
        internal string XmlEncodedText
        {
            get { return m_text; }
            set { m_text = value; }
        }

        /// <summary>
        /// A key that can be used to look to the localized text in different locales.
        /// </summary>
        /// <remarks>
        /// This value is used within a process to facilite localization. It is not transmitted on the wire.
        /// </remarks>
        public string Key
        {
            get
            {
                if (TranslationInfo != null)
                {
                    return TranslationInfo.Key;
                }

                return null;
            }

            set
            {
                if (TranslationInfo != null)
                {
                    TranslationInfo.Key = value;
                    return;
                }

                TranslationInfo = new TranslationInfo(value, XmlEncodedLocale, m_text);
            }
        }

        /// <summary>
        /// The information required to translate the text into other locales.
        /// </summary>
        public TranslationInfo TranslationInfo { get; set; }

        /// <summary>
        /// Returns true if this LocalizedText uses the "mul" special locale.
        /// </summary>
        public bool IsMultiLanguage => string.Equals(XmlEncodedLocale, kMulLocale, StringComparison.OrdinalIgnoreCase);
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="obj">The object to compare to this</param>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            var ltext = obj as LocalizedText;

            if (ltext == null)
            {
                return false;
            }

            if (ltext.XmlEncodedLocale != XmlEncodedLocale)
            {
                if (!(string.IsNullOrEmpty(ltext.XmlEncodedLocale) && string.IsNullOrEmpty(XmlEncodedLocale)))
                {
                    return false;
                }
            }

            return ltext.m_text == m_text;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the two objects are equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator ==(LocalizedText value1, LocalizedText value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.Equals(value2);
            }

            return Object.ReferenceEquals(value2, null);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the two objects are not equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator !=(LocalizedText value1, LocalizedText value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return !value1.Equals(value2);
            }

            return !Object.ReferenceEquals(value2, null);
        }

        /// <summary>
        /// Returns a suitable hash code for the object.
        /// </summary>
        /// <remarks>
        /// Returns a suitable hash code for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            if (m_text != null)
            {
                hash.Add(m_text);
            }

            if (XmlEncodedLocale != null)
            {
                hash.Add(XmlEncodedLocale);
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <param name="format">(Unused). Always pass NULL/NOTHING</param>
        /// <param name="formatProvider">(Unused). Always pass NULL/NOTHING</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are used</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", this.m_text);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Makes a deep copy of the object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <remarks>
        /// Converts a string to a localized text.
        /// </remarks>
        /// <param name="value">The string to store as localized text</param>
        public static LocalizedText ToLocalizedText(string value)
        {
            return new LocalizedText(value);
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <remarks>
        /// Converts a string to a localized text.
        /// </remarks>
        /// <param name="value">The string to store as localized text</param>
        public static implicit operator LocalizedText(string value)
        {
            return new LocalizedText(value);
        }

        /// <summary>
        /// Returns an instance of a null LocalizedText.
        /// </summary>
        public static LocalizedText Null { get; } = new LocalizedText();

        /// <summary>
        /// Returns true if the text is a null or empty string.
        /// </summary>
        public static bool IsNullOrEmpty(LocalizedText value)
        {
            if (value == null)
            {
                return true;
            }

            return string.IsNullOrEmpty(value.m_text);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns a LocalizedText filtered by the preferred locales according to OPC UA Part 4 rules for 'mul' and 'qst'. (https://reference.opcfoundation.org/Core/Part4/v105/docs/5.4)
        /// </summary>
        /// <param name="preferredLocales">The list of preferred locales, possibly including 'mul' or 'qst' as the first entry.</param>
        /// <returns>A LocalizedText containing translations as specified by the rules.</returns>
        public LocalizedText FilterByPreferredLocales(IList<string> preferredLocales)
        {
            if (preferredLocales == null || preferredLocales.Count == 0 || XmlEncodedLocale == null)
            {
                return this;
            }

            bool isMultilanguageRequested = preferredLocales[0].ToLowerInvariant() is "mul" or "qst";

            // If not a multi-language request, return the best match or fallback
            if (!isMultilanguageRequested)
            {
                if (!IsMultiLanguage)
                {
                    // nothing to do for single locale text
                    return this;
                }

                // Try to find the first matching locale
                foreach (string locale in preferredLocales)
                {
                    if (Translations.TryGetValue(locale, out string text))
                    {
                        return new LocalizedText(locale, text);
                    }
                }
                // return the first available locale
                KeyValuePair<string, string> defaultKVP = Translations.First();
                return new LocalizedText(defaultKVP.Key, defaultKVP.Value);
            }

            // Multi-language request: 'mul' or 'qst'
            if (preferredLocales.Count == 1)
            {
                return this;
            }
            // 'mul' or 'qst' + specific locales: return only those translations
            else
            {
                if (!IsMultiLanguage)
                {
                    // nothing to do for single locale text
                    return this;
                }

                var translations = new ReadOnlyDictionary<string, string>(Translations
                    .Where(t => preferredLocales.Contains(t.Key))
                    .ToDictionary(s => s.Key, s => s.Value));

                // If matching locales are found return those
                if (translations.Count > 0)
                {
                    return new LocalizedText(translations);
                }
                // else return the first available locale
                else
                {
                    KeyValuePair<string, string> defaultKVP = Translations.First();
                    return new LocalizedText(defaultKVP.Key, defaultKVP.Value);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Ecodes the translations to a JSON string according to the format specified in https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
        /// </summary>
        private static string EncodeMulLocale(IReadOnlyDictionary<string, string> translations)
        {
            if (translations == null)
            {
                throw new ArgumentNullException(nameof(translations));
            }

            if (translations.Count == 0)
            {
                throw new ArgumentException("The translations dictionary must not be empty.", nameof(translations));
            }

            var t = new List<object[]>();
            foreach (KeyValuePair<string, string> kvp in translations)
            {
                t.Add(new object[] { kvp.Key, kvp.Value });
            }

            return JsonConvert.SerializeObject(new Dictionary<string, object> { { kMulLocaleDictionaryKey, t } });
        }

        /// <summary>
        /// If this is a "mul" locale, returns a dictionary of locale/text pairs from the JSON Text.
        /// Otherwise, returns null.
        /// </summary>
        private IReadOnlyDictionary<string, string> DecodeMulLocale()
        {
            if (!IsMultiLanguage || string.IsNullOrWhiteSpace(m_text))
            {
                return null;
            }

            var result = new Dictionary<string, string>();
            try
            {
                // The expected JSON structure is defined in https://reference.opcfoundation.org/Core/Part3/v105/docs/8.5
                Dictionary<string, object> json = JsonConvert.DeserializeObject<Dictionary<string, object>>(m_text);
                if (json != null && json.TryGetValue(kMulLocaleDictionaryKey, out object tValue) && tValue is Newtonsoft.Json.Linq.JArray tArray)
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
                Utils.Trace("Failed to parse mul locale JSON text: {0}", m_text);
                return null; // Return null if parsing fails
            }
            return new ReadOnlyDictionary<string, string>(result);
        }

#endregion
#region Private Fields
        private string m_text;
        private IReadOnlyDictionary<string, string> m_translations;
        #endregion
    }

    #region LocalizedTextCollection Class
    /// <summary>
    /// A collection of LocalizedText objects.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of LocalizedText objects.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfLocalizedText", Namespace = Namespaces.OpcUaXsd, ItemName = "LocalizedText")]
    public partial class LocalizedTextCollection : List<LocalizedText>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public LocalizedTextCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection to copy into this new instance</param>
        public LocalizedTextCollection(IEnumerable<LocalizedText> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity of this collection</param>
        public LocalizedTextCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static LocalizedTextCollection ToLocalizedTextCollection(LocalizedText[] values)
        {
            if (values != null)
            {
                return new LocalizedTextCollection(values);
            }

            return new LocalizedTextCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static implicit operator LocalizedTextCollection(LocalizedText[] values)
        {
            return ToLocalizedTextCollection(values);
        }
        #endregion

        #region ICloneable
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            var clone = new LocalizedTextCollection(this.Count);

            foreach (LocalizedText element in this)
            {
                clone.Add((LocalizedText)Utils.Clone(element));
            }

            return clone;
        }
        #endregion
    }//class
}//namespace
