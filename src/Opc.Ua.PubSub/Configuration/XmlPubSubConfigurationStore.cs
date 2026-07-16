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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// File-backed implementation of
    /// <see cref="IPubSubConfigurationStore"/> that persists a
    /// <see cref="PubSubConfigurationDataType"/> as an OPC UA XML
    /// document. The format is wire-identical to the one produced by
    /// the legacy <c>UaPubSubConfigurationHelper</c>, allowing existing
    /// configuration files to be loaded without conversion.
    /// </summary>
    /// <remarks>
    /// Implements the configuration-storage surface described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6</see>. Writes are made via a sidecar
    /// <c>.tmp</c> file followed by a destructive rename to keep
    /// readers from observing torn payloads.
    /// </remarks>
    public sealed class XmlPubSubConfigurationStore : IPubSubConfigurationStore, IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="XmlPubSubConfigurationStore"/>.
        /// </summary>
        /// <param name="filePath">Backing file path.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">
        /// Optional clock used by helpers that need a deterministic
        /// timestamp. Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="watchForChanges">
        /// When <c>true</c>, the store watches the backing file and raises
        /// <see cref="Changed"/> after an external process modifies it (debounced),
        /// in addition to the in-process <see cref="SaveAsync"/> notification.
        /// Self-writes from <see cref="SaveAsync"/> are suppressed so they do not
        /// re-fire <see cref="Changed"/>. Defaults to <c>false</c>.
        /// </param>
        public XmlPubSubConfigurationStore(
            string filePath,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            bool watchForChanges = false)
        {
            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }
            if (filePath.Length == 0)
            {
                throw new ArgumentException(
                    "filePath must not be empty.",
                    nameof(filePath));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            FilePath = filePath;
            m_telemetry = telemetry;
            TimeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<XmlPubSubConfigurationStore>();
            if (watchForChanges)
            {
                SetupFileWatch();
            }
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

        /// <summary>
        /// Backing file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Clock used by helpers; exposed for diagnostics and tests.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public async ValueTask<PubSubConfigurationDataType> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(FilePath))
            {
                throw new FileNotFoundException(
                    $"PubSub configuration file '{FilePath}' does not exist.",
                    FilePath);
            }
            byte[] payload = await ReadAllBytesAsync(
                FilePath,
                cancellationToken)
                .ConfigureAwait(false);
            return DecodePayload(payload);
        }

        /// <inheritdoc/>
        public async ValueTask SaveAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            PubSubConfigurationDataType? previous = await TryLoadPreviousAsync(
                cancellationToken)
                .ConfigureAwait(false);
            byte[] payload = EncodePayload(configuration);
            string tempPath = FilePath + TempSuffix;
            try
            {
                await WriteAllBytesAsync(tempPath, payload, cancellationToken)
                    .ConfigureAwait(false);
                RecordSelfWrite(payload, configuration);
                ReplaceFile(tempPath, FilePath);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
            Changed?.Invoke(
                this,
                new PubSubConfigurationChangedEventArgs(previous, configuration));
        }

        /// <inheritdoc/>
        public ValueTask<ConfigurationVersionDataType?> GetConfigurationVersionAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            lock (m_versionGate)
            {
                return new ValueTask<ConfigurationVersionDataType?>(
                    m_configurationVersion is null
                        ? null
                        : (ConfigurationVersionDataType)m_configurationVersion.Clone());
            }
        }

        /// <inheritdoc/>
        public ValueTask SetConfigurationVersionAsync(
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }

            lock (m_versionGate)
            {
                m_configurationVersion = (ConfigurationVersionDataType)configurationVersion.Clone();
            }

            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<ConfigurationVersionDataType?> GetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            CancellationToken cancellationToken = default)
        {
            PubSubConfigurationDataType configuration = await LoadAsync(cancellationToken).ConfigureAwait(false);
            PublishedDataSetDataType? dataSet = FindPublishedDataSet(configuration, publishedDataSetName);
            return dataSet?.DataSetMetaData?.ConfigurationVersion;
        }

        /// <inheritdoc/>
        public async ValueTask SetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }

            PubSubConfigurationDataType configuration = await LoadAsync(cancellationToken).ConfigureAwait(false);
            PublishedDataSetDataType? dataSet = FindPublishedDataSet(configuration, publishedDataSetName);
            if (dataSet?.DataSetMetaData is null)
            {
                return;
            }

            dataSet.DataSetMetaData.ConfigurationVersion = configurationVersion;
            await SaveAsync(configuration, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<PubSubConfigurationDataType?> TryLoadPreviousAsync(
            CancellationToken cancellationToken)
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            try
            {
                byte[] payload = await ReadAllBytesAsync(
                    FilePath,
                    cancellationToken)
                    .ConfigureAwait(false);
                return DecodePayload(payload);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private PubSubConfigurationDataType DecodePayload(byte[] payload)
        {
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext context = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);
            return PubSubConfigurationXmlSerializer.DecodeXml(payload, context);
        }

        private static PublishedDataSetDataType? FindPublishedDataSet(
            PubSubConfigurationDataType configuration,
            string publishedDataSetName)
        {
            if (configuration.PublishedDataSets.IsNull)
            {
                return null;
            }

            foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
            {
                if (StringComparer.Ordinal.Equals(dataSet.Name, publishedDataSetName))
                {
                    return dataSet;
                }
            }

            return null;
        }

        private byte[] EncodePayload(PubSubConfigurationDataType configuration)
        {
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            IServiceMessageContext context = AmbientMessageContext.CurrentContext ??
                ServiceMessageContext.CreateEmpty(m_telemetry);
            return PubSubConfigurationXmlSerializer.EncodeXml(configuration, context);
        }

        private static async ValueTask<byte[]> ReadAllBytesAsync(
            string path,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                useAsync: true);
            using var memory = new MemoryStream(
                checked((int)Math.Min(stream.Length, int.MaxValue)));
            byte[] buffer = new byte[FileBufferSize];
            while (true)
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                int read = await stream.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken)
                    .ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken)
                    .ConfigureAwait(false);
#endif
                if (read <= 0)
                {
                    break;
                }
                memory.Write(buffer, 0, read);
            }
            return memory.ToArray();
        }

        private static async ValueTask WriteAllBytesAsync(
            string path,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                useAsync: true);
#if NETSTANDARD2_1_OR_GREATER || NET
            await stream.WriteAsync(
                payload.AsMemory(),
                cancellationToken)
                .ConfigureAwait(false);
#else
            await stream.WriteAsync(
                payload,
                0,
                payload.Length,
                cancellationToken)
                .ConfigureAwait(false);
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            File.Move(source, destination);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void SetupFileWatch()
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = ".";
            }
            string fileName = Path.GetFileName(FilePath);
            try
            {
                m_debounceTimer = TimeProvider.CreateTimer(
                    _ => OnDebounceElapsed(),
                    null,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                var watcher = new FileSystemWatcher(directory!, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.FileName |
                        NotifyFilters.CreationTime
                };
                watcher.Changed += OnFileSystemChange;
                watcher.Created += OnFileSystemChange;
                watcher.Renamed += OnFileSystemChange;
                watcher.EnableRaisingEvents = true;
                m_watcher = watcher;
            }
            catch (Exception ex)
            {
                m_logger.PubSubConfigurationFileWatchCouldNotBeStarted(ex, FilePath);
            }
        }

        private void OnFileSystemChange(object sender, FileSystemEventArgs e)
        {
            lock (m_watchGate)
            {
                if (m_disposed)
                {
                    return;
                }
                // Coalesce the burst of events an editor produces into one reload.
                m_debounceTimer?.Change(
                    TimeSpan.FromMilliseconds(WatchDebounceMs),
                    Timeout.InfiniteTimeSpan);
            }
        }

        private void OnDebounceElapsed()
        {
            _ = ReloadFromFileAsync();
        }

        private async Task ReloadFromFileAsync()
        {
            byte[] payload;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }
                payload = await ReadAllBytesAsync(FilePath, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.PubSubConfigurationReloadFileChangeFailedToRead(ex, FilePath);
                return;
            }

            PubSubConfigurationDataType? previous;
            lock (m_watchGate)
            {
                if (m_disposed)
                {
                    return;
                }
                // Ignore the file event our own SaveAsync produced.
                if (m_lastWrittenPayload is not null &&
                    payload.AsSpan().SequenceEqual(m_lastWrittenPayload))
                {
                    return;
                }
                previous = m_lastKnownConfig;
            }

            PubSubConfigurationDataType configuration;
            try
            {
                configuration = DecodePayload(payload);
            }
            catch (Exception ex)
            {
                m_logger.PubSubConfigurationReloadFileChangeCouldNotDecode(ex, FilePath);
                return;
            }

            lock (m_watchGate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_lastWrittenPayload = payload;
                m_lastKnownConfig = configuration;
            }

            m_logger.PubSubConfigurationFileChangedExternally(FilePath);
            Changed?.Invoke(
                this,
                new PubSubConfigurationChangedEventArgs(previous, configuration));
        }

        private void RecordSelfWrite(byte[] payload, PubSubConfigurationDataType configuration)
        {
            lock (m_watchGate)
            {
                m_lastWrittenPayload = payload;
                m_lastKnownConfig = configuration;
            }
        }

        /// <summary>
        /// Stops watching the backing file and releases the watcher resources.
        /// </summary>
        public void Dispose()
        {
            FileSystemWatcher? watcher;
            ITimer? timer;
            lock (m_watchGate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                watcher = m_watcher;
                timer = m_debounceTimer;
                m_watcher = null;
                m_debounceTimer = null;
            }
            if (watcher is not null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileSystemChange;
                watcher.Created -= OnFileSystemChange;
                watcher.Renamed -= OnFileSystemChange;
                watcher.Dispose();
            }
            timer?.Dispose();
        }

        private const int FileBufferSize = 4096;
        private const string TempSuffix = ".tmp";
        private const int WatchDebounceMs = 250;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly Lock m_versionGate = new();
        private readonly Lock m_watchGate = new();
        private ConfigurationVersionDataType? m_configurationVersion;
        private FileSystemWatcher? m_watcher;
        private ITimer? m_debounceTimer;
        private byte[]? m_lastWrittenPayload;
        private PubSubConfigurationDataType? m_lastKnownConfig;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="XmlPubSubConfigurationStore"/>.
    /// </summary>
    internal static partial class XmlPubSubConfigurationStoreLog
    {
        [LoggerMessage(EventId = PubSubEventIds.XmlPubSubConfigurationStore + 0, Level = LogLevel.Information,
            Message = "PubSub configuration file watch could not be started for '{Path}'; " +
                "external changes will not raise Changed.")]
        public static partial void PubSubConfigurationFileWatchCouldNotBeStarted(
            this ILogger logger,
            Exception exception,
            string path);

        [LoggerMessage(EventId = PubSubEventIds.XmlPubSubConfigurationStore + 1, Level = LogLevel.Information,
            Message = "PubSub configuration reload after a file change failed to read '{Path}'.")]
        public static partial void PubSubConfigurationReloadFileChangeFailedToRead(
            this ILogger logger,
            Exception exception,
            string path);

        [LoggerMessage(EventId = PubSubEventIds.XmlPubSubConfigurationStore + 2, Level = LogLevel.Information,
            Message = "PubSub configuration reload after a file change could not decode '{Path}'; " +
                "keeping the previous configuration.")]
        public static partial void PubSubConfigurationReloadFileChangeCouldNotDecode(
            this ILogger logger,
            Exception exception,
            string path);

        [LoggerMessage(EventId = PubSubEventIds.XmlPubSubConfigurationStore + 3, Level = LogLevel.Information,
            Message = "PubSub configuration file '{Path}' changed externally; raising Changed.")]
        public static partial void PubSubConfigurationFileChangedExternally(this ILogger logger, string path);
    }

}
