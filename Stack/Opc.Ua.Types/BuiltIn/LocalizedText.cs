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
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class LocalizedText : ICloneable, IFormattable
    {
        /// <summary>
        /// Initializes the object with the default values.
        /// </summary>
        private LocalizedText()
        {
            XmlEncodedLocale = null;
            XmlEncodedText = null;
        }

        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// </summary>
        public LocalizedText(string key, string locale, string text, params object[] args)
            : this(new TranslationInfo(key, locale, text, args))
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
            XmlEncodedText = translationInfo.Text;
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
                XmlEncodedText = string.Format(culture, TranslationInfo.Text, TranslationInfo.Args);
            }
            catch
            {
                XmlEncodedText = TranslationInfo.Text;
            }
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <param name="value">The text to create an instance from</param>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public LocalizedText(LocalizedText value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            XmlEncodedLocale = value.XmlEncodedLocale;
            XmlEncodedText = value.XmlEncodedText;
        }

        /// <summary>
        /// Initializes the object with a text and the default locale.
        /// </summary>
        /// <param name="text">The plain text stored within this object</param>
        public LocalizedText(string text)
        {
            XmlEncodedLocale = null;
            XmlEncodedText = text;
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        public LocalizedText(string locale, string text)
        {
            XmlEncodedLocale = locale;
            XmlEncodedText = text;
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
            XmlEncodedText = text;

            if (!string.IsNullOrEmpty(key))
            {
                TranslationInfo = new TranslationInfo(key, locale, text);
            }
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
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="translations">key = locale, value = text</param>
        public LocalizedText(string key, IReadOnlyDictionary<string, string> translations)
        {
            Translations = translations;

            if (!string.IsNullOrEmpty(key))
            {
                TranslationInfo = new TranslationInfo(key, XmlEncodedLocale, XmlEncodedText);
            }
        }

        /// <summary>
        /// The locale used to create the text.
        /// </summary>
        public string Locale => XmlEncodedLocale;

        /// <inheritdoc/>
        [DataMember(Name = "Locale", Order = 1)]
        internal string XmlEncodedLocale { get; set; }

        /// <summary>
        /// The localized text.
        /// </summary>
        public string Text => XmlEncodedText;

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
                    return new ReadOnlyDictionary<string, string>(
                        new Dictionary<string, string> { { XmlEncodedLocale, XmlEncodedText } });
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
                        XmlEncodedText = kvp.Value;
                        m_translations = null;
                        return;
                    }
                }
                m_translations = value;
            }
        }

        /// <inheritdoc/>
        [DataMember(Name = "Text", Order = 2)]
        internal string XmlEncodedText { get; set; }

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

                TranslationInfo = new TranslationInfo(value, XmlEncodedLocale, XmlEncodedText);
            }
        }

        /// <summary>
        /// The information required to translate the text into other locales.
        /// </summary>
        public TranslationInfo TranslationInfo { get; set; }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="obj">The object to compare to this</param>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is not LocalizedText ltext)
            {
                return false;
            }

            if (ltext.XmlEncodedLocale != XmlEncodedLocale &&
                !(string.IsNullOrEmpty(ltext.XmlEncodedLocale) &&
                    string.IsNullOrEmpty(XmlEncodedLocale)))
            {
                return false;
            }

            return ltext.XmlEncodedText == XmlEncodedText;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator ==(LocalizedText value1, LocalizedText value2)
        {
            if (value1 is not null)
            {
                return value1.Equals(value2);
            }

            return value2 is null;
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator !=(LocalizedText value1, LocalizedText value2)
        {
            if (value1 is not null)
            {
                return !value1.Equals(value2);
            }

            return value2 is not null;
        }

        /// <summary>
        /// Returns a suitable hash code for the object.
        /// </summary>
        public override int GetHashCode()
        {
            int hashCode = -423158783;
            hashCode = (hashCode * -1521134295) +
                EqualityComparer<string>.Default.GetHashCode(XmlEncodedLocale);
            hashCode = (hashCode * -1521134295) +
                EqualityComparer<string>.Default.GetHashCode(XmlEncodedText);
            return hashCode;
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
                return string.Format(formatProvider, "{0}", XmlEncodedText);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <param name="value">The string to store as localized text</param>
        public static LocalizedText ToLocalizedText(string value)
        {
            return new LocalizedText(value);
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
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

            return string.IsNullOrEmpty(value.XmlEncodedText);
        }

        private IReadOnlyDictionary<string, string> m_translations;
    }

    /// <summary>
    /// A collection of LocalizedText objects.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of LocalizedText objects.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfLocalizedText",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "LocalizedText")]
    public class LocalizedTextCollection : List<LocalizedText>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public LocalizedTextCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The collection to copy into this new instance</param>
        public LocalizedTextCollection(IEnumerable<LocalizedText> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max capacity of this collection</param>
        public LocalizedTextCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static LocalizedTextCollection ToLocalizedTextCollection(LocalizedText[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static implicit operator LocalizedTextCollection(LocalizedText[] values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new LocalizedTextCollection(Count);

            foreach (LocalizedText element in this)
            {
                clone.Add(Utils.Clone(element));
            }

            return clone;
        }
    }
}
