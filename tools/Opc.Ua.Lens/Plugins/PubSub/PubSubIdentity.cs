/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Preserves UADP identity widths and the stack's canonical JSON numeric matching.
/// </summary>
internal static class PubSubIdentity
{
    public static bool TryCreate(
        PublisherIdType type,
        ulong numeric,
        string text,
        bool json,
        out Variant value)
    {
        value = Variant.Null;
        if (text is null || text.Length > 96 || PubSubConfigurationValidation.HasControlCharacters(text))
        {
            return false;
        }
        if (type == PublisherIdType.String)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                (json && (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                    Guid.TryParse(text, out _))))
            {
                return false;
            }
            value = new Variant(text);
            return true;
        }
        if (type == PublisherIdType.Guid)
        {
            if (!json || !Guid.TryParseExact(text, "D", out Guid guid) || guid == Guid.Empty)
            {
                return false;
            }
            value = new Variant(new Uuid(guid));
            return true;
        }
        if (text.Length != 0 || numeric == 0)
        {
            return false;
        }
        value = type switch
        {
            PublisherIdType.Byte when numeric <= byte.MaxValue => new Variant((byte)numeric),
            PublisherIdType.UInt16 when numeric <= ushort.MaxValue => new Variant((ushort)numeric),
            PublisherIdType.UInt32 when numeric <= uint.MaxValue => new Variant((uint)numeric),
            PublisherIdType.UInt64 => new Variant(numeric),
            _ => Variant.Null
        };
        if (value.IsNull)
        {
            return false;
        }
        if (json)
        {
            value = numeric switch
            {
                <= byte.MaxValue => new Variant((byte)numeric),
                <= ushort.MaxValue => new Variant((ushort)numeric),
                <= uint.MaxValue => new Variant((uint)numeric),
                _ => new Variant(numeric)
            };
        }
        return true;
    }

    public static Variant Local(PubSubConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Create(configuration.LocalPublisherIdType, configuration.LocalPublisherId,
            configuration.LocalPublisherName, configuration.IsJson);
    }

    public static Variant Filter(PubSubConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Create(configuration.PublisherFilterType, configuration.PublisherFilter,
            configuration.PublisherFilterName, configuration.IsJson);
    }

    public static string Describe(PubSubConfiguration configuration, bool local)
    {
        PublisherId id = PublisherId.From(local ? Local(configuration) : Filter(configuration));
        return id.Type + ": " + id.ToString();
    }

    private static Variant Create(PublisherIdType type, ulong numeric, string text, bool json)
    {
        return TryCreate(type, numeric, text, json, out Variant value)
            ? value
            : throw new ArgumentException("The publisher identity is invalid for the selected encoding.");
    }
}
