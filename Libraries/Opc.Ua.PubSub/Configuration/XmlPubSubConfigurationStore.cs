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
    public sealed class XmlPubSubConfigurationStore : IPubSubConfigurationStore
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
        public XmlPubSubConfigurationStore(
            string filePath,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
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
            m_filePath = filePath;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

        /// <summary>
        /// Backing file path.
        /// </summary>
        public string FilePath => m_filePath;

        /// <summary>
        /// Clock used by helpers; exposed for diagnostics and tests.
        /// </summary>
        public TimeProvider TimeProvider => m_timeProvider;

        /// <inheritdoc/>
        public async ValueTask<PubSubConfigurationDataType> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(m_filePath))
            {
                throw new FileNotFoundException(
                    $"PubSub configuration file '{m_filePath}' does not exist.",
                    m_filePath);
            }
            byte[] payload = await ReadAllBytesAsync(
                m_filePath,
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
            string tempPath = m_filePath + TempSuffix;
            try
            {
                await WriteAllBytesAsync(tempPath, payload, cancellationToken)
                    .ConfigureAwait(false);
                ReplaceFile(tempPath, m_filePath);
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
            if (!File.Exists(m_filePath))
            {
                return null;
            }
            try
            {
                byte[] payload = await ReadAllBytesAsync(
                    m_filePath,
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

        private const int FileBufferSize = 4096;
        private const string TempSuffix = ".tmp";

        private readonly string m_filePath;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
    }
}
