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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Translates a declarative <see cref="TranscodeRouteOptions"/> into a
    /// <see cref="TranscodingBridgeDescriptor"/> by driving the fluent
    /// <see cref="PubSubTranscoderBuilder"/>, and computes a change signature
    /// used by the reload coordinator to reconfigure only modified routes.
    /// </summary>
    internal static class TranscodeRouteOptionsFactory
    {
        public static TranscodingBridgeDescriptor Create(TranscodeRouteOptions route)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }
            if (string.IsNullOrEmpty(route.Name))
            {
                throw new InvalidOperationException(
                    "A transcoding route requires a Name.");
            }
            if (string.IsNullOrEmpty(route.Source))
            {
                throw new InvalidOperationException(
                    $"Transcoding route '{route.Name}' requires a Source connection.");
            }
            if (string.IsNullOrEmpty(route.Target))
            {
                throw new InvalidOperationException(
                    $"Transcoding route '{route.Name}' requires a Target connection.");
            }

            PubSubTranscoderBuilder builder = new PubSubTranscoderBuilder()
                .From(route.Source!)
                .To(route.Target!, route.TargetEncoding)
                .PreserveMetaDataVersion(route.PreserveMetaDataVersion);

            if (!string.IsNullOrEmpty(route.Topic))
            {
                builder.ToTopic(route.Topic!);
            }
            if (route.FieldEncoding is PubSubFieldEncoding fieldEncoding)
            {
                builder.WithFieldEncoding(fieldEncoding);
            }
            if (route.JsonSingleMessage)
            {
                builder.AsJsonSingleMessage();
            }
            if (route.AllowInsecureCrossEncoding)
            {
                builder.AllowInsecureCrossEncoding();
            }

            ApplyRemap(builder, route.RemapIds);

            if (route.RenameFields is { Count: > 0 })
            {
                foreach (KeyValuePair<string, string> rename in route.RenameFields)
                {
                    builder.RenameField(rename.Key, rename.Value);
                }
            }
            if (route.SelectFields is { Count: > 0 })
            {
                builder.SelectFields([.. route.SelectFields]);
            }
            if (route.ExcludeFields is { Count: > 0 })
            {
                builder.ExcludeFields([.. route.ExcludeFields]);
            }
            if (route.KeepMessageTypes is { Count: > 0 })
            {
                var keep = new HashSet<PubSubDataSetMessageType>(route.KeepMessageTypes);
                builder.FilterMessageTypes(keep.Contains);
            }
            if (route.DropKeepAlive)
            {
                builder.DropKeepAlive();
            }
            if (route.PromoteFields is { Count: > 0 })
            {
                builder.PromoteFields([.. route.PromoteFields]);
                if (!string.IsNullOrEmpty(route.PromotedFieldPrefix))
                {
                    builder.WithPromotedFieldPrefix(route.PromotedFieldPrefix!);
                }
            }

            return builder.Build();
        }

        private static void ApplyRemap(
            PubSubTranscoderBuilder builder,
            TranscodeIdRemapOptions? remap)
        {
            if (remap is null)
            {
                return;
            }
            PublisherId publisherId = default;
            if (!string.IsNullOrEmpty(remap.PublisherId))
            {
                publisherId = PublisherId.FromString(remap.PublisherId!);
            }
            else if (remap.PublisherIdNumber is ulong number)
            {
                publisherId = PublisherId.FromUInt64(number);
            }
            Uuid? dataSetClassId = null;
            if (!string.IsNullOrEmpty(remap.DataSetClassId))
            {
                dataSetClassId = new Uuid(Guid.Parse(remap.DataSetClassId!));
            }
            builder.RemapIds(
                publisherId,
                remap.WriterGroupId,
                dataSetClassId,
                remap.DataSetWriterIds is { Count: > 0 }
                    ? new Dictionary<ushort, ushort>(remap.DataSetWriterIds)
                    : null);
        }

        /// <summary>
        /// Builds a deterministic signature of a route's declarative content
        /// so the reload coordinator can detect whether a route changed.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string ComputeSignature(TranscodeRouteOptions route)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }
            var sb = new StringBuilder();
            Append(sb, "src", route.Source);
            Append(sb, "tgt", route.Target);
            Append(sb, "enc", route.TargetEncoding.ToString());
            Append(sb, "topic", route.Topic);
            Append(sb, "fe", route.FieldEncoding?.ToString());
            Append(sb, "json", route.JsonSingleMessage);
            Append(sb, "pmv", route.PreserveMetaDataVersion);
            Append(sb, "insec", route.AllowInsecureCrossEncoding);
            Append(sb, "dka", route.DropKeepAlive);
            AppendRemap(sb, route.RemapIds);
            AppendMap(sb, "rename", route.RenameFields);
            AppendList(sb, "select", route.SelectFields);
            AppendList(sb, "exclude", route.ExcludeFields);
            AppendTypes(sb, "keeptypes", route.KeepMessageTypes);
            AppendList(sb, "promote", route.PromoteFields);
            Append(sb, "prefix", route.PromotedFieldPrefix);
            return sb.ToString();
        }

        private static void AppendRemap(StringBuilder sb, TranscodeIdRemapOptions? remap)
        {
            if (remap is null)
            {
                sb.Append("remap=;");
                return;
            }
            Append(sb, "remap.pid", remap.PublisherId);
            Append(sb, "remap.pidn", remap.PublisherIdNumber?.ToString(CultureInfo.InvariantCulture));
            Append(sb, "remap.wg", remap.WriterGroupId?.ToString(CultureInfo.InvariantCulture));
            Append(sb, "remap.dsc", remap.DataSetClassId);
            if (remap.DataSetWriterIds is { Count: > 0 })
            {
                var keys = new List<ushort>(remap.DataSetWriterIds.Keys);
                keys.Sort();
                sb.Append("remap.dsw=");
                foreach (ushort key in keys)
                {
                    sb.Append(key.ToString(CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(remap.DataSetWriterIds[key].ToString(CultureInfo.InvariantCulture))
                        .Append(',');
                }
                sb.Append(';');
            }
        }

        private static void AppendMap(
            StringBuilder sb,
            string key,
            IDictionary<string, string>? map)
        {
            if (map is not { Count: > 0 })
            {
                sb.Append(key).Append("=;");
                return;
            }
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            sb.Append(key).Append('=');
            foreach (string entry in keys)
            {
                sb.Append(entry).Append(':').Append(map[entry]).Append(',');
            }
            sb.Append(';');
        }

        private static void AppendList(StringBuilder sb, string key, IList<string>? list)
        {
            if (list is not { Count: > 0 })
            {
                sb.Append(key).Append("=;");
                return;
            }
            sb.Append(key).Append('=');
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(list[i]).Append(',');
            }
            sb.Append(';');
        }

        private static void AppendTypes(
            StringBuilder sb,
            string key,
            IList<PubSubDataSetMessageType>? list)
        {
            if (list is not { Count: > 0 })
            {
                sb.Append(key).Append("=;");
                return;
            }
            sb.Append(key).Append('=');
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(list[i].ToString()).Append(',');
            }
            sb.Append(';');
        }

        private static void Append(StringBuilder sb, string key, string? value)
        {
            sb.Append(key).Append('=').Append(value ?? string.Empty).Append(';');
        }

        private static void Append(StringBuilder sb, string key, bool value)
        {
            sb.Append(key).Append('=').Append(value ? '1' : '0').Append(';');
        }
    }
}
