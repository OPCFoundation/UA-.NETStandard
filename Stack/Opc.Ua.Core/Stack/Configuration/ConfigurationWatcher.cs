/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_logger = telemetry.CreateLogger<ConfigurationWatcher>();

            var fileInfo = new FileInfo(configuration.SourceFilePath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(
                    "Could not load configuration file",
                    configuration.SourceFilePath);
            }

            m_configuration = configuration;
            m_lastWriteTime = fileInfo.LastWriteTimeUtc;
            m_watcher = new System.Threading.Timer(Watcher_Changed, null, 5000, 5000);
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
        public event EventHandler<ConfigurationWatcherEventArgs> Changed
        {
            add => m_Changed += value;
            remove => m_Changed -= value;
        }

        /// <summary>
        /// Handles a file changed event.
        /// </summary>
        private void Watcher_Changed(object state)
        {
            try
            {
                var fileInfo = new FileInfo(m_configuration.SourceFilePath);

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
                        m_configuration.SourceFilePath));
            }
            catch (Exception exception)
            {
                m_logger.LogError(
                    exception,
                    "Unexpected error raising configuration file changed event.");
            }
        }

        private readonly ILogger m_logger;
        private readonly ApplicationConfiguration m_configuration;
        private System.Threading.Timer m_watcher;
        private DateTime m_lastWriteTime;
        private event EventHandler<ConfigurationWatcherEventArgs> m_Changed;
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
}
