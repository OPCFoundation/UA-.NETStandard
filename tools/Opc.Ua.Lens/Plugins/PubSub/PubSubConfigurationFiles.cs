/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UaLens.Storage;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Bounded configuration-only import and atomic local-file replacement. Input
/// streams remain caller-owned; parsing never mutates a workspace or acquires providers.
/// </summary>
internal static class PubSubConfigurationFiles
{
    public static async Task<PubSubConfiguration> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[MaxConfigurationBytes + 1];
        int total = 0;
        while (true)
        {
            int count = await stream.ReadAsync(
                buffer.AsMemory(total, Math.Min(4096, buffer.Length - total)), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            total += count;
            if (total > MaxConfigurationBytes)
            {
                throw new JsonException("The PubSub configuration file exceeds the byte limit.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        int offset = total >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf ? 3 : 0;
        string json = s_utf8.GetString(buffer, offset, total - offset);
        if (json.Length > PubSubStateCodec.MaxConfigurationCharacters)
        {
            throw new JsonException("The PubSub configuration exceeds the document limit.");
        }
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        PubSubConfiguration configuration = document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("version", out _)
            ? PubSubStateCodec.Restore(document.RootElement)
            : PubSubStateCodec.Parse(json);
        cancellationToken.ThrowIfCancellationRequested();
        return configuration;
    }

    public static Task WriteAsync(
        string localPath,
        PubSubConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        cancellationToken.ThrowIfCancellationRequested();
        _ = PubSubStateCodec.Capture(configuration);
        return JsonFileStore.WriteAsync(localPath, new PubSubDocumentState(1, configuration),
            PubSubJsonContext.Default.PubSubDocumentState, cancellationToken);
    }

    public const int MaxConfigurationBytes = PubSubStateCodec.MaxConfigurationCharacters * 3;
    private static readonly UTF8Encoding s_utf8 = new(false, true);
}
