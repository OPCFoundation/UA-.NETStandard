/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Watches the configuration file and reports any changes.
    /// </summary>
    public class ConfigurationWatcher : IDisposable
    {
        /// <summary>
        /// Creates the watcher for the configuration.
        /// </summary>
        public ConfigurationWatcher(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
            : this(configuration, telemetry, null)
        {
        }

        /// <summary>
        /// Creates the watcher for the configuration using the supplied
        /// <see cref="TimeProvider"/> for polling.
        /// </summary>
        public ConfigurationWatcher(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_logger = telemetry.CreateLogger<ConfigurationWatcher>();
            m_timeProvider = timeProvider ?? TimeProvider.System;

            var fileInfo = new FileInfo(configuration.SourceFilePath!);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(
                    "Could not load configuration file",
                    configuration.SourceFilePath);
            }

            m_configuration = configuration;
            m_lastWriteTime = fileInfo.LastWriteTimeUtc;
            m_watcher = m_timeProvider.CreateTimer(
                Watcher_Changed,
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && m_watcher != null)
            {
                m_watcher.Dispose();
                m_watcher = null;
            }
        }

        /// <summary>
        /// Raised when the configuration file changes.
        /// </summary>
        public event EventHandler<ConfigurationWatcherEventArgs>? Changed
        {
            add => m_Changed += value;
            remove => m_Changed -= value;
        }

        /// <summary>
        /// Handles a file changed event.
        /// </summary>
        private void Watcher_Changed(object? state)
        {
            try
            {
                var fileInfo = new FileInfo(m_configuration.SourceFilePath!);

                if (!fileInfo.Exists)
                {
                    return;
                }

                if (fileInfo.LastWriteTimeUtc <= m_lastWriteTime)
                {
                    return;
                }

                m_lastWriteTime = fileInfo.LastWriteTimeUtc;

                m_Changed?.Invoke(
                    this,
                    new ConfigurationWatcherEventArgs(
                        m_configuration,
                        m_configuration.SourceFilePath!));
            }
            catch (Exception exception)
            {
                m_logger.ConfigurationWatcherLogMessage0(exception);
            }
        }

        private readonly ILogger m_logger;
        private readonly ApplicationConfiguration m_configuration;
        private readonly TimeProvider m_timeProvider;
        private ITimer? m_watcher;
        private DateTime m_lastWriteTime;
        private event EventHandler<ConfigurationWatcherEventArgs>? m_Changed;
    }

    /// <summary>
    /// Stores the arguments passed when the configuration file changes.
    /// </summary>
    public class ConfigurationWatcherEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes the object with a configuration and a file path.
        /// </summary>
        public ConfigurationWatcherEventArgs(
            ApplicationConfiguration configuration,
            string filePath)
        {
            Configuration = configuration;
            FilePath = filePath;
        }

        /// <summary>
        /// The application configuration which changed.
        /// </summary>
        public ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// The path to the application configuration file.
        /// </summary>
        public string FilePath { get; }
    }

    /// <summary>
    /// Source-generated log messages for ConfigurationWatcher.
    /// </summary>
    internal static partial class ConfigurationWatcherLog
    {
        [LoggerMessage(EventId = CoreEventIds.ConfigurationWatcher + 0, Level = LogLevel.Error,
            Message = "Unexpected error raising configuration file changed event.")]
        public static partial void ConfigurationWatcherLogMessage0(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
