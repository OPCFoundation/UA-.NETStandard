/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Configuration;

namespace Opc.Ua.AMB.Server.Structure
{
    /// <summary>
    /// Converts the value of a writable property to the text it is persisted
    /// as, and back.
    /// </summary>
    internal interface IPersistedValueCodec
    {
        /// <summary>
        /// Converts a written value; <see langword="false"/> when it has the
        /// wrong type.
        /// </summary>
        bool TryEncode(Variant value, out string? text);

        /// <summary>
        /// Converts a persisted text back.
        /// </summary>
        Variant Decode(string text);
    }

    /// <summary>
    /// Persists a <c>String</c> property as it is.
    /// </summary>
    internal sealed class StringCodec : IPersistedValueCodec
    {
        public static readonly StringCodec Instance = new();

        /// <summary>A codec for fields whose null values cannot be retained as absent store entries.</summary>
        public static readonly StringCodec RequiredInstance = new(allowNull: false);

        private StringCodec(bool allowNull = true)
        {
            m_allowNull = allowNull;
        }

        public bool TryEncode(Variant value, out string? text)
        {
            if (value.IsNull)
            {
                text = null;
                return m_allowNull;
            }
            return value.TryGetValue(out text) && (text is not null || m_allowNull);
        }

        public Variant Decode(string text)
        {
            return Variant.From(text);
        }

        /// <summary>Whether a null write removes the store entry.</summary>
        private readonly bool m_allowNull;
    }

    /// <summary>
    /// Stores the locale length, locale and text of a LocalizedText without a lossy string conversion.
    /// </summary>
    internal sealed class LocalizedTextCodec : IPersistedValueCodec
    {
        /// <summary>The stateless shared codec.</summary>
        public static readonly LocalizedTextCodec Instance = new();

        /// <inheritdoc/>
        public bool TryEncode(Variant value, out string? text)
        {
            if (value.IsNull)
            {
                text = "null";
                return true;
            }
            if (!value.TryGetValue(out LocalizedText localized))
            {
                text = null;
                return false;
            }
            text = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}{3}",
                localized.Locale?.Length ?? -1, localized.Text?.Length ?? -1, localized.Locale, localized.Text);
            return true;
        }

        /// <inheritdoc/>
        public Variant Decode(string text)
        {
            if (text == "null")
            {
                return Variant.From(LocalizedText.Null);
            }
            int delimiter = text.IndexOf(':', StringComparison.Ordinal);
            int second = delimiter < 0 ? -1 : text.IndexOf(':', delimiter + 1);
            if (delimiter < 1 || second < 0 ||
                !TryParseLength(text, 0, delimiter, out int localeLength) ||
                !TryParseLength(text, delimiter + 1, second - delimiter - 1, out int textLength) ||
                localeLength < -1 || textLength < -1 ||
                (long)Math.Max(0, localeLength) + Math.Max(0, textLength) != text.Length - second - 1)
            {
                throw new FormatException("The persisted LocalizedText has an invalid locale length.");
            }
            return Variant.From(new LocalizedText(
                localeLength < 0 ? null : text.Substring(second + 1, localeLength),
                textLength < 0 ? null : text.Substring(second + 1 + Math.Max(0, localeLength), textLength)));
        }

        /// <summary>
        /// Parses an invariant length without allocating a substring on platforms with span-based parsing.
        /// </summary>
        private static bool TryParseLength(string text, int start, int length, out int value)
        {
#if NET8_0_OR_GREATER
            return int.TryParse(text.AsSpan(start, length),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
#else
            return int.TryParse(text.Substring(start, length),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
#endif
        }
    }

    /// <summary>
    /// Persists a <c>TimeZoneDataType</c> as its offset in minutes and the
    /// daylight saving flag.
    /// </summary>
    internal sealed class TimeZoneCodec : IPersistedValueCodec
    {
        public static readonly TimeZoneCodec Instance = new();

        public bool TryEncode(Variant value, out string? text)
        {
            if (value.IsNull)
            {
                text = null;
                return true;
            }
            TimeZoneDataType timeZone;
            if (!value.TryGetStructure(out timeZone!))
            {
                text = null;
                return false;
            }
            text = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}",
                timeZone.Offset,
                timeZone.DaylightSavingInOffset);
            return true;
        }

        public Variant Decode(string text)
        {
            string[] parts = text.Split('|');
            if (parts.Length != 2 ||
                !short.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out short offset) ||
                !bool.TryParse(parts[1], out bool daylightSaving))
            {
                return Variant.Null;
            }
            return Variant.FromStructure(new TimeZoneDataType
            {
                Offset = offset,
                DaylightSavingInOffset = daylightSaving
            });
        }
    }

    /// <summary>
    /// Makes a property of an asset writable and persists what clients write
    /// under the asset's <c>ProductInstanceUri</c>, restoring it on start.
    /// </summary>
    internal sealed class PersistentProperty
    {
        private PersistentProperty(
            BaseVariableState variable,
            IAssetConfigurationStore store,
            string productInstanceUri,
            string name,
            IPersistedValueCodec codec,
            int? maxLength,
            ILogger logger)
        {
            m_variable = variable;
            m_store = store;
            m_productInstanceUri = productInstanceUri;
            m_name = name;
            m_codec = codec;
            m_maxLength = maxLength;
            m_logger = logger;
        }

        /// <summary>
        /// Restores a persisted value and makes the property writable.
        /// </summary>
        /// <param name="variable">The property.</param>
        /// <param name="store">The store.</param>
        /// <param name="productInstanceUri">The key of the asset in the store.</param>
        /// <param name="name">The name of the value in the store.</param>
        /// <param name="codec">Converts the value to text and back.</param>
        /// <param name="maxLength">
        /// The number of Unicode characters a written text may have at most;
        /// null when the codec bounds it.
        /// </param>
        /// <param name="logger">The logger.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="writable">Enables write access after restoration when true.</param>
        public static async ValueTask<PersistentProperty> BindAsync(
            BaseVariableState variable,
            IAssetConfigurationStore store,
            string productInstanceUri,
            string name,
            IPersistedValueCodec codec,
            int? maxLength,
            ILogger logger,
            CancellationToken cancellationToken,
            bool writable = true)
        {
            string? stored = await store.GetValueAsync(productInstanceUri, name, cancellationToken)
                .ConfigureAwait(false);
            if (stored != null && maxLength is int limit && UnicodeText.Length(stored) > limit)
            {
                // A limit lowered since the value was stored.
                stored = UnicodeText.Truncate(stored, limit);
                logger.PropertyClamped(variable.BrowseName, productInstanceUri);
            }
            if (stored != null)
            {
                variable.WrappedValue = codec.Decode(stored);
                variable.Timestamp = DateTimeUtc.Now;
            }
            if (writable)
            {
                variable.AccessLevel |= AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel |= AccessLevels.CurrentReadOrWrite;
            }
            var binding = new PersistentProperty(variable, store, productInstanceUri, name, codec, maxLength, logger);
            variable.OnSimpleWriteValueAsync = binding.OnWriteAsync;
            return binding;
        }

        private async ValueTask<AttributeWriteResult> OnWriteAsync(
            ISystemContext context,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!m_codec.TryEncode(value, out string? text))
            {
                return new AttributeWriteResult(StatusCodes.BadTypeMismatch);
            }
            if (text != null && m_maxLength is int limit && UnicodeText.Length(text) > limit)
            {
                return new AttributeWriteResult(ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "{0} must not be longer than {1} characters.",
                    m_variable.BrowseName.Name ?? string.Empty,
                    limit));
            }
            try
            {
                await m_store.SetValueAsync(m_productInstanceUri, m_name, text, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.PropertyNotPersisted(ex, m_variable.BrowseName, m_productInstanceUri);
                return new AttributeWriteResult(StatusCodes.BadResourceUnavailable);
            }
            m_logger.PropertyChanged(m_variable.BrowseName, m_productInstanceUri);
            return new AttributeWriteResult(ServiceResult.Good);
        }

        private readonly BaseVariableState m_variable;
        private readonly IAssetConfigurationStore m_store;
        private readonly string m_productInstanceUri;
        private readonly string m_name;
        private readonly IPersistedValueCodec m_codec;
        private readonly int? m_maxLength;
        private readonly ILogger m_logger;
    }

    internal static partial class PersistentPropertyLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AssetStructure + 0,
            Level = LogLevel.Information,
            Message = "Property {BrowseName} of asset {ProductInstanceUri} changed.")]
        public static partial void PropertyChanged(
            this ILogger logger,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetStructure + 1,
            Level = LogLevel.Error,
            Message = "Property {BrowseName} of asset {ProductInstanceUri} could not be persisted; " +
                "the write is rejected.")]
        public static partial void PropertyNotPersisted(
            this ILogger logger,
            Exception exception,
            QualifiedName browseName,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetStructure + 2,
            Level = LogLevel.Warning,
            Message = "The persisted value of property {BrowseName} of asset {ProductInstanceUri} is longer than " +
                "the limit allows now and is cut to it.")]
        public static partial void PropertyClamped(
            this ILogger logger,
            QualifiedName browseName,
            string productInstanceUri);
    }
}
