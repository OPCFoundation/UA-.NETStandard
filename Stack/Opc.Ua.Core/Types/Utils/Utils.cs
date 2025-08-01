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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Security.Certificates;

using NewNonceImplementation = Opc.Ua.Nonce;

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static partial class Utils
    {
        #region Public Constants
        /// <summary>
        /// The URI scheme for the HTTP protocol.
        /// </summary>
        public const string UriSchemeHttp = "http";

        /// <summary>
        /// The URI scheme for the HTTPS protocol, used in some legacy https
        /// clients and servers but not compliant to spec version 1.04.
        /// </summary>
        public const string UriSchemeHttps = "https";

        /// <summary>
        /// The URI scheme for the UA HTTPS protocol.
        /// </summary>
        public const string UriSchemeOpcHttps = "opc.https";

        /// <summary>
        /// The URI scheme for the UA TCP protocol.
        /// </summary>
        public const string UriSchemeOpcTcp = "opc.tcp";

        /// <summary>
        /// The URI scheme for the UA TCP protocol over Secure WebSockets.
        /// </summary>
        public const string UriSchemeOpcWss = "opc.wss";

        /// <summary>
        /// The URI scheme for the UDP protocol.
        /// </summary>
        public const string UriSchemeOpcUdp = "opc.udp";

        /// <summary>
        /// The URI scheme for the MQTT protocol.
        /// </summary>
        public const string UriSchemeMqtt = "mqtt";

        /// <summary>
        /// The URI scheme for the MQTTS protocol.
        /// </summary>
        public const string UriSchemeMqtts = "mqtts";

        /// <summary>
        /// The URI schemes which are supported in the core server.
        /// </summary>
        public static readonly string[] DefaultUriSchemes = new string[]
        {
            Utils.UriSchemeOpcTcp,
            Utils.UriSchemeOpcHttps,
            Utils.UriSchemeHttps,
            Utils.UriSchemeOpcWss
        };

        /// <summary>
        /// The default port for the UA TCP protocol.
        /// </summary>
        public const int UaTcpDefaultPort = 4840;

        /// <summary>
        /// The default port for the UA TCP protocol over WebSockets.
        /// </summary>
        public const int UaWebSocketsDefaultPort = 4843;

        /// <summary>
        /// The default port for the MQTT protocol.
        /// </summary>
        public const int MqttDefaultPort = 1883;

        /// <summary>
        /// The urls of the discovery servers on a node.
        /// </summary>
        public static readonly string[] DiscoveryUrls = new string[]
        {
            "opc.tcp://{0}:4840",
            "https://{0}:4843",
            "http://{0}:52601/UADiscovery",
            "http://{0}/UADiscovery/Default.svc"
        };

        /// <summary>
        /// The default certificate store's type.
        /// </summary>
        public const string DefaultStoreType = CertificateStoreType.Directory;

        /// <summary>
        /// The path to the default certificate store.
        /// </summary>
#if NETFRAMEWORK
        public static readonly string DefaultStorePath = Path.Combine("%CommonApplicationData%", "OPC Foundation", "pki", "own");
#else
        public static readonly string DefaultStorePath = Path.Combine("%LocalApplicationData%", "OPC Foundation", "pki", "own");
#endif

        /// <summary>
        /// The default LocalFolder.
        /// </summary>
        public static string DefaultLocalFolder { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>
        /// The full name of the Opc.Ua.Core assembly.
        /// </summary>
        public static readonly string DefaultOpcUaCoreAssemblyFullName = typeof(Utils).Assembly.GetName().FullName;

        /// <summary>
        /// The name of the Opc.Ua.Core assembly.
        /// </summary>
        public static readonly string DefaultOpcUaCoreAssemblyName = typeof(Utils).Assembly.GetName().Name;

        /// <summary>
        /// List of known default bindings hosted in other assemblies.
        /// </summary>
        public static readonly ReadOnlyDictionary<string, string> DefaultBindings = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>() {
                { Utils.UriSchemeHttps, "Opc.Ua.Bindings.Https"},
                { Utils.UriSchemeOpcHttps, "Opc.Ua.Bindings.Https"}
            });

        /// <summary>
        /// Returns <c>true</c> if the url starts with opc.https or https.
        /// </summary>
        /// <param name="url">The url</param>
        public static bool IsUriHttpsScheme(string url)
        {
            return url.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal) ||
                url.StartsWith(Utils.UriSchemeOpcHttps, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if the url starts with http, opc.https or https.
        /// </summary>
        /// <param name="url">The url</param>
        public static bool IsUriHttpRelatedScheme(string url)
        {
            return url.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal) ||
                 IsUriHttpsScheme(url);
        }
        #endregion

        #region Trace Support
#if DEBUG
        private static int s_traceOutput = (int)TraceOutput.DebugAndFile;
        private static int s_traceMasks = (int)TraceMasks.All;
#else
        private static int s_traceOutput = (int)TraceOutput.FileOnly;
        private static int s_traceMasks = (int)TraceMasks.None;
#endif

        private static string s_traceFileName = string.Empty;
        private static readonly object s_traceFileLock = new object();

        /// <summary>
        /// The possible trace output mechanisms.
        /// </summary>
        public enum TraceOutput
        {
            /// <summary>
            /// No tracing
            /// </summary>
            Off = 0,

            /// <summary>
            /// Only write to file (if specified). Default for Release mode.
            /// </summary>
            FileOnly = 1,

            /// <summary>
            /// Write to debug trace listeners and a file (if specified). Default for Debug mode.
            /// </summary>
            DebugAndFile = 2
        }

        /// <summary>
        /// The masks used to filter trace messages.
        /// </summary>
        public static class TraceMasks
        {
            /// <summary>
            /// Do not output any messages.
            /// </summary>
            public const int None = 0x0;

            /// <summary>
            /// Output error messages.
            /// </summary>
            public const int Error = 0x1;

            /// <summary>
            /// Output informational messages.
            /// </summary>
            public const int Information = 0x2;

            /// <summary>
            /// Output stack traces.
            /// </summary>
            public const int StackTrace = 0x4;

            /// <summary>
            /// Output basic messages for service calls.
            /// </summary>
            public const int Service = 0x8;

            /// <summary>
            /// Output detailed messages for service calls.
            /// </summary>
            public const int ServiceDetail = 0x10;

            /// <summary>
            /// Output basic messages for each operation.
            /// </summary>
            public const int Operation = 0x20;

            /// <summary>
            /// Output detailed messages for each operation.
            /// </summary>
            public const int OperationDetail = 0x40;

            /// <summary>
            /// Output messages related to application initialization or shutdown
            /// </summary>
            public const int StartStop = 0x80;

            /// <summary>
            /// Output messages related to a call to an external system.
            /// </summary>
            public const int ExternalSystem = 0x100;

            /// <summary>
            /// Output messages related to security
            /// </summary>
            public const int Security = 0x200;

            /// <summary>
            /// Output all messages.
            /// </summary>
            public const int All = 0x3FF;
        }

        /// <summary>
        /// Sets the output for tracing (thread safe).
        /// </summary>
        public static void SetTraceOutput(TraceOutput output)
        {
            lock (s_traceFileLock)
            {
                s_traceOutput = (int)output;
            }
        }

        /// <summary>
        /// Gets the current trace mask settings.
        /// </summary>
        public static int TraceMask
        {
            get { return s_traceMasks; }
        }

        /// <summary>
        /// Sets the mask for tracing (thread safe).
        /// </summary>
        public static void SetTraceMask(int masks)
        {
            s_traceMasks = (int)masks;
        }

        /// <summary>
        /// Returns Tracing class instance for event attaching.
        /// </summary>
        public static Tracing Tracing
        {
            get { return Tracing.Instance; }
        }

        /// <summary>
        /// Writes a trace statement.
        /// </summary>
        private static void TraceWriteLine(string message, params object[] args)
        {
            // null strings not supported.
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // format the message if format arguments provided.
            string output = message;

            if (args != null && args.Length > 0)
            {
                try
                {
                    output = string.Format(CultureInfo.InvariantCulture, message, args);
                }
                catch (Exception)
                {
                    output = message;
                }
            }

            TraceWriteLine(output);
        }

        /// <summary>
        /// Writes a trace statement.
        /// </summary>
        private static void TraceWriteLine(string output)
        {
            // write to the log file.
            lock (s_traceFileLock)
            {
                // write to debug trace listeners.
                if (s_traceOutput == (int)TraceOutput.DebugAndFile)
                {
                    Debug.WriteLine(output);
                }

                string traceFileName = s_traceFileName;

                if (s_traceOutput != (int)TraceOutput.Off && !string.IsNullOrEmpty(traceFileName))
                {
                    try
                    {
                        FileInfo file = new FileInfo(traceFileName);

                        // limit the file size
                        bool truncated = false;

                        if (file.Exists && file.Length > 10000000)
                        {
                            file.Delete();
                            truncated = true;
                        }

                        using (StreamWriter writer = new StreamWriter(File.Open(file.FullName, FileMode.Append, FileAccess.Write, FileShare.Read)))
                        {
                            if (truncated)
                            {
                                writer.WriteLine("WARNING - LOG FILE TRUNCATED.");
                            }

                            writer.WriteLine(output);
                            writer.Flush();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Could not write to trace file. Error={0}", e.Message);
                        Debug.WriteLine("FilePath={1}", traceFileName);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the path to the log file to use for tracing.
        /// </summary>
        public static void SetTraceLog(string filePath, bool deleteExisting)
        {
            // turn tracing on.
            lock (s_traceFileLock)
            {
                // check if tracing is being turned off.
                if (string.IsNullOrEmpty(filePath))
                {
                    s_traceFileName = null;
                    return;
                }

                s_traceFileName = GetAbsoluteFilePath(filePath, true, false, true, true);

                if (s_traceOutput == (int)TraceOutput.Off)
                {
                    s_traceOutput = (int)TraceOutput.FileOnly;
                }

                try
                {
                    FileInfo file = new FileInfo(s_traceFileName);

                    if (deleteExisting && file.Exists)
                    {
                        file.Delete();
                    }

                    // write initial log message.
                    TraceWriteLine(string.Empty);
                    TraceWriteLine(
                        "{1} Logging started at {0}",
                        DateTime.Now,
                        new string('*', 25));
                }
                catch (Exception e)
                {
                    TraceWriteLine(e.Message);
                }
            }
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        public static void Trace(string message)
        {
            LogInfo(message);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        public static void Trace(string format, params object[] args)
        {
            LogInfo(format, args);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Conditional("DEBUG")]
        public static void TraceDebug(string format, params object[] args)
        {
            LogDebug(format, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        public static void Trace(Exception e, string message)
        {
            LogError(e, message);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        public static void Trace(Exception e, string format, params object[] args)
        {
            LogError(e, format, args);
        }

        /// <summary>
        /// Create an exception/error message for a log.
        /// </summary>
        internal static StringBuilder TraceExceptionMessage(Exception e, string format, params object[] args)
        {
            StringBuilder message = new StringBuilder();

            // format message.
            if (args != null && args.Length > 0)
            {
                try
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, format, args);
                    message.AppendLine();
                }
                catch (Exception)
                {
                    message.AppendLine(format);
                }
            }
            else
            {
                message.AppendLine(format);
            }

            // append exception information.
            if (e != null)
            {
                if (e is ServiceResultException sre)
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, " {0} '{1}'", StatusCodes.GetBrowseName(sre.StatusCode), sre.Message);
                }
                else
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, " {0} '{1}'", e.GetType().Name, e.Message);
                }
                message.AppendLine();

                // append stack trace.
                if ((s_traceMasks & (int)TraceMasks.StackTrace) != 0)
                {
                    message.AppendLine();
                    message.AppendLine();
                    var separator = new string('=', 40);
                    message.AppendLine(separator);
                    message.AppendLine(new ServiceResult(e).ToLongString());
                    message.AppendLine(separator);
                }
            }

            return message;
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        public static void Trace(Exception e, string format, bool handled, params object[] args)
        {
            StringBuilder message = TraceExceptionMessage(e, format, args);

            // trace message.
            Trace(e, (int)TraceMasks.Error, message.ToString(), handled, null);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Trace(int traceMask, string format, params object[] args)
        {
            const int InformationMask = (TraceMasks.Information | TraceMasks.StartStop | TraceMasks.Security);
            const int ErrorMask = (TraceMasks.Error | TraceMasks.StackTrace);
            if ((traceMask & ErrorMask) != 0)
            {
                LogError(traceMask, format, args);
            }
            else if ((traceMask & InformationMask) != 0)
            {
                LogInfo(traceMask, format, args);
            }
            else
            {
                LogTrace(traceMask, format, args);
            }
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Trace(int traceMask, string format, bool handled, params object[] args)
        {
            Trace(null, traceMask, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Trace<TState>(TState state, Exception exception, int traceMask, Func<TState, Exception, string> formatter)
        {
            // do nothing if mask not enabled.
            bool tracingEnabled = Tracing.IsEnabled();
            bool traceMaskEnabled = (s_traceMasks & traceMask) != 0;
            if (!traceMaskEnabled && !tracingEnabled)
            {
                return;
            }

            StringBuilder message = new StringBuilder();
            try
            {
                // append process and timestamp.
                message.AppendFormat(CultureInfo.InvariantCulture, "{0:d} {0:HH:mm:ss.fff} ", DateTime.UtcNow.ToLocalTime());
                message.Append(formatter(state, exception));
                if (exception != null)
                {
                    message.Append(TraceExceptionMessage(exception, string.Empty, null));
                }
            }
            catch (Exception)
            {
                return;
            }

            var output = message.ToString();
            if (tracingEnabled)
            {
                Tracing.Instance.RaiseTraceEvent(new TraceEventArgs(traceMask, output, string.Empty, exception, Array.Empty<object>()));
            }
            if (traceMaskEnabled)
            {
                TraceWriteLine(output);
            }
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Trace(Exception e, int traceMask, string format, bool handled, params object[] args)
        {
            if (!handled)
            {
                Tracing.Instance.RaiseTraceEvent(new TraceEventArgs(traceMask, format, string.Empty, e, args));
            }

            // do nothing if mask not enabled.
            if ((s_traceMasks & traceMask) == 0)
            {
                return;
            }

            StringBuilder message = new StringBuilder();

            // append process and timestamp.
            message.AppendFormat(CultureInfo.InvariantCulture, "{0:d} {0:HH:mm:ss.fff} ", DateTime.UtcNow.ToLocalTime());

            // format message.
            if (args != null && args.Length > 0)
            {
                try
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, format, args);
                }
                catch (Exception)
                {
                    message.Append(format);
                }
            }
            else
            {
                message.Append(format);
            }

            TraceWriteLine(message.ToString());
        }
        #endregion

        #region File Access
        /// <summary>
        /// Replaces a prefix enclosed in '%' with a special folder or environment variable path (e.g. %ProgramFiles%\MyCompany).
        /// </summary>
        public static bool IsPathRooted(string path)
        {
            // allow for local file locations
            return Path.IsPathRooted(path) || (path.Length >= 2 && path[0] == '.' && path[1] != '.');
        }

        /// <summary>
        /// Maps a special folder to environment variable with folder path.
        /// </summary>
        private static string ReplaceSpecialFolderWithEnvVar(string input)
        {
            switch (input)
            {
                case "CommonApplicationData": return "ProgramData";
            }

            return input;
        }

        /// <summary>
        /// Replaces a prefix enclosed in '%' with a special folder or environment variable path (e.g. %ProgramFiles%\MyCompany).
        /// </summary>
        public static string ReplaceSpecialFolderNames(string input)
        {
            // nothing to do for nulls.
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            // check for absolute path.
            if (Utils.IsPathRooted(input))
            {
                return input;
            }

            // check for special folder prefix.
            if (input[0] != '%')
            {
                return input;
            }

            // extract special folder name.
            string folder = null;
            string path = null;

            int index = input.IndexOf('%', 1);

            if (index == -1)
            {
                folder = input.Substring(1);
                path = string.Empty;
            }
            else
            {
                folder = input.Substring(1, index - 1);
                path = input.Substring(index + 1);
            }

            StringBuilder buffer = new StringBuilder();
#if !NETSTANDARD1_4 && !NETSTANDARD1_3
            // check for special folder.
            Environment.SpecialFolder specialFolder;
            if (!Enum.TryParse<Environment.SpecialFolder>(folder, out specialFolder))
            {
#endif
                folder = ReplaceSpecialFolderWithEnvVar(folder);
                string value = Environment.GetEnvironmentVariable(folder);
                if (value != null)
                {
                    buffer.Append(value);
                }
                else
                {
                    if (folder == "LocalFolder")
                    {
                        buffer.Append(DefaultLocalFolder);
                    }
                }
#if !NETSTANDARD1_4 && !NETSTANDARD1_3
            }
            else
            {
                buffer.Append(Environment.GetFolderPath(specialFolder));
            }
#endif
            // construct new path.
            buffer.Append(path);
            return buffer.ToString();
        }

        /// <summary>
        /// Finds the file by search the common file folders and then bin directories in the source tree
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>The path to the file. Null if not found.</returns>
        public static string FindInstalledFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = null;

            // check source tree.
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append(directory.FullName);
                buffer.Append(Path.DirectorySeparatorChar).Append("Bin").Append(Path.DirectorySeparatorChar);
                buffer.Append(fileName);

                path = Utils.GetAbsoluteFilePath(buffer.ToString(), false, false, false);

                if (path != null)
                {
                    break;
                }

                directory = directory.Parent;
            }

            // return what was found.
            return path;
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteFilePath(string filePath)
        {
            return GetAbsoluteFilePath(filePath, false, true, false);
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteFilePath(string filePath, bool checkCurrentDirectory, bool throwOnError, bool createAlways, bool writable = false)
        {
            filePath = Utils.ReplaceSpecialFolderNames(filePath);

            if (!string.IsNullOrEmpty(filePath))
            {
                FileInfo file = new FileInfo(filePath);

                // check for absolute path.
                bool isAbsolute = Utils.IsPathRooted(filePath);

                if (isAbsolute)
                {
                    if (file.Exists)
                    {
                        return filePath;
                    }

                    if (createAlways)
                    {
                        return CreateFile(file, filePath, throwOnError);
                    }
                }

                if (!isAbsolute)
                {
                    // look current directory.
                    if (checkCurrentDirectory)
                    {
                        // first check in local folder
                        FileInfo localFile = null;
                        if (!writable)
                        {
                            localFile = new FileInfo(Utils.Format("{0}{1}{2}", Directory.GetCurrentDirectory(), Path.DirectorySeparatorChar, filePath));
#if NETFRAMEWORK
                            if (!localFile.Exists)
                            {
                                var localFile2 = new FileInfo(Utils.Format("{0}{1}{2}",
                                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                                    Path.DirectorySeparatorChar, filePath));
                                if (localFile2.Exists)
                                {
                                    localFile = localFile2;
                                }
                            }
#endif
                        }
                        else
                        {
                            localFile = new FileInfo(Utils.Format("{0}{1}{2}", Path.GetTempPath(), Path.DirectorySeparatorChar, filePath));
                        }

                        if (localFile.Exists)
                        {
                            return localFile.FullName;
                        }

                        if (file.Exists && !writable)
                        {
                            return file.FullName;
                        }

                        if (createAlways && writable)
                        {
                            return CreateFile(localFile, localFile.FullName, throwOnError);
                        }
                    }
                }
            }

            // file does not exist.
            if (throwOnError)
            {
                var message = new StringBuilder();
                message.AppendLine("File does not exist: {0}");
                message.AppendLine("Current directory is: {1}");
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    message.ToString(),
                    filePath,
                    Directory.GetCurrentDirectory());
            }

            return null;
        }

        /// <summary>
        /// Creates an empty file.
        /// </summary>
        private static string CreateFile(FileInfo file, string filePath, bool throwOnError)
        {
            try
            {
                // create the directory as required.
                if (!file.Directory.Exists)
                {
                    Directory.CreateDirectory(file.DirectoryName);
                }

                // open and close the file.
                using (Stream ostrm = file.Open(FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    return filePath;
                }
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Could not create file: {0}", filePath);

                if (throwOnError)
                {
                    throw;
                }

                return filePath;
            }
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteDirectoryPath(string dirPath, bool checkCurrentDirectory, bool throwOnError)
        {
            return GetAbsoluteDirectoryPath(dirPath, checkCurrentDirectory, throwOnError, false);
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteDirectoryPath(string dirPath, bool checkCurrentDirectory, bool throwOnError, bool createAlways)
        {
            string originalPath = dirPath;
            dirPath = Utils.ReplaceSpecialFolderNames(dirPath);

            if (!string.IsNullOrEmpty(dirPath))
            {
                DirectoryInfo directory = new DirectoryInfo(dirPath);

                // check for absolute path.
                bool isAbsolute = Utils.IsPathRooted(dirPath);

                if (isAbsolute)
                {
                    if (directory.Exists)
                    {
                        return dirPath;
                    }

                    if (createAlways && !directory.Exists)
                    {
                        directory = Directory.CreateDirectory(dirPath);
                        return directory.FullName;
                    }
                }

                if (!isAbsolute)
                {
                    // look current directory.
                    if (checkCurrentDirectory)
                    {
                        if (!directory.Exists)
                        {
                            directory = new DirectoryInfo(Utils.Format("{0}{1}{2}", Directory.GetCurrentDirectory(), Path.DirectorySeparatorChar, dirPath));
#if NETFRAMEWORK
                            if (!directory.Exists)
                            {
                                var directory2 = new DirectoryInfo(Utils.Format("{0}{1}{2}",
                                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                                    Path.DirectorySeparatorChar, dirPath));
                                if (directory2.Exists)
                                {
                                    directory = directory2;
                                }
                            }
#endif
                        }
                    }

                    // return full path.
                    if (directory.Exists)
                    {
                        return directory.FullName;
                    }

                    // create the directory.
                    if (createAlways)
                    {
                        directory = Directory.CreateDirectory(directory.FullName);
                        return directory.FullName;
                    }
                }
            }

            // file does not exist.
            if (throwOnError)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Directory does not exist: {0}\r\nCurrent directory is: {1}",
                    originalPath,
                    Directory.GetCurrentDirectory());
            }

            return null;
        }

        /// <summary>
        /// Truncates a file path so it can be displayed in a limited width view.
        /// </summary>
        public static string GetFilePathDisplayName(string filePath, int maxLength)
        {
            // check if nothing to do.
            if (filePath == null || maxLength <= 0 || filePath.Length < maxLength)
            {
                return filePath;
            }

            // keep first path segment.
            int start = filePath.IndexOf(Path.DirectorySeparatorChar);

            if (start == -1)
            {
                return Utils.Format("{0}...", filePath.Substring(0, maxLength));
            }

            // keep file name.
            int end = filePath.LastIndexOf(Path.DirectorySeparatorChar);

            while (end > start && filePath.Length - end < maxLength)
            {
                end = filePath.LastIndexOf(Path.DirectorySeparatorChar, end - 1);

                if (filePath.Length - end > maxLength)
                {
                    end = filePath.IndexOf(Path.DirectorySeparatorChar, end + 1);
                    break;
                }
            }

            // format the result.
            return Utils.Format("{0}...{1}", filePath.Substring(0, start + 1), filePath.Substring(end));
        }
        #endregion

        #region String, Object and Data Convienence Functions
        /// <summary>
        /// Suppresses any exceptions while disposing the object.
        /// </summary>
        /// <remarks>
        /// Writes errors to trace output in DEBUG builds.
        /// </remarks>
        public static void SilentDispose(object objectToDispose)
        {
            IDisposable disposable = objectToDispose as IDisposable;
            SilentDispose(disposable);
        }

        /// <summary>
        /// Suppresses any exceptions while disposing the object.
        /// </summary>
        /// <remarks>
        /// Writes errors to trace output in DEBUG builds.
        /// </remarks>
        public static void SilentDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
#if DEBUG
            catch (Exception e)
            {
                Utils.LogError(e, "Error disposing object: {0}", disposable.GetType().Name);
            }
#else
            catch (Exception) {;}
#endif
        }

        /// <summary>
        /// The earliest time that can be represented on with UA date/time values.
        /// </summary>
        public static DateTime TimeBase
        {
            get { return s_TimeBase; }
        }

        private static readonly DateTime s_TimeBase = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Normalize a DateTime to Opc Ua UniversalTime.
        /// </summary>
        public static DateTime ToOpcUaUniversalTime(DateTime value)
        {
            if (value <= DateTime.MinValue)
            {
                return DateTime.MinValue;
            }
            if (value >= DateTime.MaxValue)
            {
                return DateTime.MaxValue;
            }
            if (value.Kind != DateTimeKind.Utc)
            {
                return value.ToUniversalTime();
            }
            return value;
        }

        /// <summary>
        /// Returns an absolute deadline for a timeout.
        /// </summary>
        public static DateTime GetDeadline(TimeSpan timeSpan)
        {
            DateTime now = DateTime.UtcNow;

            if (DateTime.MaxValue.Ticks - now.Ticks < timeSpan.Ticks)
            {
                return DateTime.MaxValue;
            }

            return now + timeSpan;
        }

        /// <summary>
        /// Returns a timeout as integer number of milliseconds
        /// </summary>
        public static int GetTimeout(TimeSpan timeSpan)
        {
            if (timeSpan.TotalMilliseconds > int.MaxValue)
            {
                return -1;
            }

            if (timeSpan.TotalMilliseconds < 0)
            {
                return 0;
            }

            return (int)timeSpan.TotalMilliseconds;
        }

        /// <inheritdoc cref="Dns.GetHostAddressesAsync(string)"/>
        public static Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress)
        {
            return Dns.GetHostAddressesAsync(hostNameOrAddress);
        }

        /// <inheritdoc cref="Dns.GetHostAddresses(string)"/>
        public static IPAddress[] GetHostAddresses(string hostNameOrAddress)
        {
            return Dns.GetHostAddresses(hostNameOrAddress);
        }

        /// <inheritdoc cref="Dns.GetHostName"/>
        /// <remarks>If the platform returns a FQDN, only the host name is returned.</remarks>
        public static string GetHostName()
        {
            return Dns.GetHostName().Split('.')[0].ToLowerInvariant();
        }

        /// <summary>
        /// Get the FQDN of the local computer.
        /// </summary>
        public static string GetFullQualifiedDomainName()
        {
            string domainName = null;
            try
            {
                domainName = Dns.GetHostEntry("localhost").HostName;
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(domainName))
            {
                return Dns.GetHostName();
            }
            return domainName;
        }

        /// <summary>
        /// Normalize ipv4/ipv6 address for comparisons.
        /// </summary>
        public static string NormalizedIPAddress(string ipAddress)
        {
            try
            {
                IPAddress normalizedAddress = IPAddress.Parse(ipAddress);
                return normalizedAddress.ToString();
            }
            catch
            {
                return ipAddress;
            }
        }

        /// <summary>
        /// Replaces the localhost domain with the current host name.
        /// </summary>
        public static string ReplaceLocalhost(string uri, string hostname = null)
        {
            // ignore nulls.
            if (string.IsNullOrEmpty(uri))
            {
                return uri;
            }

            // IPv6 address needs a surrounding []
            if (!string.IsNullOrEmpty(hostname) && hostname.Contains(':'))
            {
                hostname = "[" + hostname + "]";
            }

            // check if the string localhost is specified.
            var localhost = "localhost";
            int index = uri.IndexOf(localhost, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return uri;
            }

            // construct new uri.
            var buffer = new StringBuilder();
#if NET5_0_OR_GREATER || NETSTANDARD2_1
            buffer.Append(uri.AsSpan(0, index))
                .Append(hostname ?? GetHostName())
                .Append(uri.AsSpan(index + localhost.Length));
#else
            buffer.Append(uri.Substring(0, index))
                .Append(hostname ?? GetHostName())
                .Append(uri.Substring(index + localhost.Length));
#endif
            return buffer.ToString();
        }

        /// <summary>
        /// Replaces the cert subject name DC=localhost with the current host name.
        /// </summary>
        public static string ReplaceDCLocalhost(string subjectName, string hostname = null)
        {
            // ignore nulls.
            if (string.IsNullOrEmpty(subjectName))
            {
                return subjectName;
            }

            // IPv6 address needs a surrounding []
            if (!string.IsNullOrEmpty(hostname) && hostname.Contains(':'))
            {
                hostname = "[" + hostname + "]";
            }

            // check if the string DC=localhost is specified.
            var dclocalhost = "DC=localhost";
            int index = subjectName.IndexOf(dclocalhost, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return subjectName;
            }

            // construct new uri.
            var buffer = new StringBuilder();
#if NET5_0_OR_GREATER || NETSTANDARD2_1
            buffer.Append(subjectName.AsSpan(0, index + 3))
                .Append(hostname ?? GetHostName())
                .Append(subjectName.AsSpan(index + dclocalhost.Length));
#else
            buffer.Append(subjectName.Substring(0, index + 3))
                .Append(hostname ?? GetHostName())
                .Append(subjectName.Substring(index + dclocalhost.Length));
#endif
            return buffer.ToString();
        }

        /// <summary>
        /// Escapes a URI string using the percent encoding.
        /// </summary>
        public static string EscapeUri(string uri)
        {
            if (!string.IsNullOrWhiteSpace(uri))
            {
                // always use back compat: for not well formed Uri, fall back to legacy formatting behavior - see #2793, #2826
                // problem with Uri.TryCreate(uri.Replace(";", "%3b"), UriKind.Absolute, out Uri validUri);
                // -> uppercase letters will later be lowercase (and therefore the uri will later be non-matching)
                var buffer = new StringBuilder();
                foreach (char ch in uri)
                {
                    switch (ch)
                    {
                        case ';':
                        case '%':
                        {
                            buffer.AppendFormat(CultureInfo.InvariantCulture, "%{0:X2}", Convert.ToInt16(ch));
                            break;
                        }

                        default:
                        {
                            buffer.Append(ch);
                            break;
                        }
                    }
                }
                return buffer.ToString();
            }
            return string.Empty;
        }

#if NET9_0_OR_GREATER
        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(ReadOnlySpan<char> uri)
        {
            if (!uri.IsWhiteSpace())
            {
                return Uri.UnescapeDataString(uri);
            }

            return string.Empty;
        }
#else
        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(string uri)
        {
            if (!string.IsNullOrWhiteSpace(uri))
            {
                return Uri.UnescapeDataString(uri);
            }

            return string.Empty;
        }
#endif

        /// <summary>
        /// Parses a URI string. Returns null if it is invalid.
        /// </summary>
        public static Uri ParseUri(string uri)
        {
            try
            {
                if (string.IsNullOrEmpty(uri))
                {
                    return null;
                }

                return new Uri(uri);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Parses the URLs, returns true of they have the same domain.
        /// </summary>
        /// <param name="url1">The first URL to compare.</param>
        /// <param name="url2">The second URL to compare.</param>
        /// <returns>True if they have the same domain.</returns>
        public static bool AreDomainsEqual(Uri url1, Uri url2)
        {
            if (url1 == null || url2 == null)
            {
                return false;
            }

            try
            {
                string domain1 = url1.DnsSafeHost;
                string domain2 = url2.DnsSafeHost;

                // replace localhost with the computer name.
                if (domain1 == "localhost")
                {
                    domain1 = GetHostName();
                }

                if (domain2 == "localhost")
                {
                    domain2 = GetHostName();
                }

                if (AreDomainsEqual(domain1, domain2))
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the domains are equal.
        /// </summary>
        /// <param name="domain1">The first domain to compare.</param>
        /// <param name="domain2">The second domain to compare.</param>
        /// <returns>True if they are equal.</returns>
        public static bool AreDomainsEqual(string domain1, string domain2)
        {
            if (string.IsNullOrEmpty(domain1) || string.IsNullOrEmpty(domain2))
            {
                return false;
            }

            if (string.Equals(domain1, domain2, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Substitutes the local machine name if "localhost" is specified in the instance uri.
        /// </summary>
        public static string UpdateInstanceUri(string instanceUri)
        {
            // check for null.
            if (string.IsNullOrEmpty(instanceUri))
            {
                UriBuilder builder = new UriBuilder();

                builder.Scheme = Utils.UriSchemeHttps;
                builder.Host = GetHostName();
                builder.Port = -1;
                builder.Path = Guid.NewGuid().ToString();

                return builder.Uri.ToString();
            }

            // prefix non-urls with the hostname.
            if (!instanceUri.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
            {
                UriBuilder builder = new UriBuilder();

                builder.Scheme = Utils.UriSchemeHttps;
                builder.Host = GetHostName();
                builder.Port = -1;
                builder.Path = Uri.EscapeDataString(instanceUri);

                return builder.Uri.ToString();
            }

            // replace localhost with the current hostname.
            Uri parsedUri = Utils.ParseUri(instanceUri);

            if (parsedUri != null && parsedUri.DnsSafeHost == "localhost")
            {
                UriBuilder builder = new UriBuilder(parsedUri);
                builder.Host = GetHostName();
                return builder.Uri.ToString();
            }

            // return the original instance uri.
            return instanceUri;
        }

        /// <summary>
        /// Sets the identifier to a lower limit if smaller. Thread safe.
        /// </summary>
        /// <returns>Returns the new value.</returns>
        public static uint LowerLimitIdentifier(ref long identifier, uint lowerLimit)
        {
            long value;
            long exchangedValue;
            do
            {
                value = System.Threading.Interlocked.Read(ref identifier);
                exchangedValue = value;
                if (value < lowerLimit)
                {
                    exchangedValue = System.Threading.Interlocked.CompareExchange(ref identifier, lowerLimit, value);
                }
            } while (exchangedValue != value);
            return (uint)System.Threading.Interlocked.Read(ref identifier);
        }

        /// <summary>
        /// Increments a identifier (wraps around if max exceeded).
        /// </summary>
        public static uint IncrementIdentifier(ref long identifier)
        {
            System.Threading.Interlocked.CompareExchange(ref identifier, 0, uint.MaxValue);
            return (uint)System.Threading.Interlocked.Increment(ref identifier);
        }

        /// <summary>
        /// Increments a identifier (wraps around if max exceeded).
        /// </summary>
        public static int IncrementIdentifier(ref int identifier)
        {
            System.Threading.Interlocked.CompareExchange(ref identifier, 0, int.MaxValue);
            return System.Threading.Interlocked.Increment(ref identifier);
        }

        /// <summary>
        /// Safely converts an UInt32 identifier to a Int32 identifier.
        /// </summary>
        public static int ToInt32(uint identifier)
        {
            if (identifier <= (uint)int.MaxValue)
            {
                return (int)identifier;
            }

            return -(int)((long)uint.MaxValue - (long)identifier + 1);
        }

        /// <summary>
        /// Safely converts an Int32 identifier to a UInt32 identifier.
        /// </summary>
        public static uint ToUInt32(int identifier)
        {
            if (identifier >= 0)
            {
                return (uint)identifier;
            }

            return (uint)((long)uint.MaxValue + 1 + (long)identifier);
        }

        /// <summary>
        /// Converts a multidimension array to a flat array.
        /// </summary>
        /// <remarks>
        /// The higher rank dimensions are written first.
        /// e.g. a array with dimensions [2,2,2] is written in this order:
        /// [0,0,0], [0,0,1], [0,1,0], [0,1,1], [1,0,0], [1,0,1], [1,1,0], [1,1,1]
        /// </remarks>
        public static Array FlattenArray(Array array)
        {
            Array flatArray = Array.CreateInstance(array.GetType().GetElementType(), array.Length);

            int[] indexes = new int[array.Rank];
            int[] dimensions = new int[array.Rank];

            for (int jj = array.Rank - 1; jj >= 0; jj--)
            {
                dimensions[jj] = array.GetLength(array.Rank - jj - 1);
            }

            for (int ii = 0; ii < array.Length; ii++)
            {
                indexes[array.Rank - 1] = ii % dimensions[0];

                for (int jj = 1; jj < array.Rank; jj++)
                {
                    int multiplier = 1;

                    for (int kk = 0; kk < jj; kk++)
                    {
                        multiplier *= dimensions[kk];
                    }

                    indexes[array.Rank - jj - 1] = (ii / multiplier) % dimensions[jj];
                }

                flatArray.SetValue(array.GetValue(indexes), ii);
            }

            return flatArray;
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }

            return ToHexString(new ReadOnlySpan<byte>(buffer), invertEndian);
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
        public static string ToHexString(ReadOnlySpan<byte> buffer, bool invertEndian = false)
        {
            if (buffer.Length == 0)
            {
                return string.Empty;
            }
#else
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return String.Empty;
            }
#endif

#if NET6_0_OR_GREATER
            if (!invertEndian)
            {
                return Convert.ToHexString(buffer);
            }
            else
#endif
            {
                StringBuilder builder = new StringBuilder(buffer.Length * 2);

#if !NET6_0_OR_GREATER
                if (!invertEndian)
                {
                    for (int ii = 0; ii < buffer.Length; ii++)
                    {
                        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                    }
                }
                else
#endif
                {
                    for (int ii = buffer.Length - 1; ii >= 0; ii--)
                    {
                        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                    }
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// Converts a hexadecimal string to an array of bytes.
        /// </summary>
        public static byte[] FromHexString(string buffer)
        {
#if NET6_0_OR_GREATER
            return Convert.FromHexString(buffer);
#else
            if (buffer == null)
            {
                return null;
            }

            if (buffer.Length == 0)
            {
                return Array.Empty<byte>();
            }

            string text = buffer.ToUpperInvariant();
            const string digits = "0123456789ABCDEF";

            byte[] bytes = new byte[(buffer.Length / 2) + (buffer.Length % 2)];

            int ii = 0;

            while (ii < bytes.Length * 2)
            {
                int index = digits.IndexOf(buffer[ii]);

                if (index == -1)
                {
                    break;
                }

                byte b = (byte)index;
                b <<= 4;

                if (ii < buffer.Length - 1)
                {
                    index = digits.IndexOf(buffer[ii + 1]);

                    if (index == -1)
                    {
                        break;
                    }

                    b += (byte)index;
                }

                bytes[ii / 2] = b;
                ii += 2;
            }

            return bytes;
#endif
        }

        /// <summary>
        /// Formats an object using the invariant locale.
        /// </summary>
        public static string ToString(object source)
        {
            if (source != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}", source);
            }

            return string.Empty;
        }

        /// <summary>
        /// Formats a message using the invariant locale.
        /// </summary>
        public static string Format(string text, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, text, args);
        }

        /// <summary>
        /// Checks if a string is a valid locale identifier.
        /// </summary>
        public static bool IsValidLocaleId(string localeId)
        {
            if (string.IsNullOrEmpty(localeId))
            {
                return false;
            }

            try
            {
                CultureInfo culture = new CultureInfo(localeId);

                if (culture != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // do nothing.
            }

            return false;
        }

        /// <summary>
        /// Returns the language identifier from a locale.
        /// </summary>
        public static string GetLanguageId(string localeId)
        {
            if (localeId == null)
            {
                return string.Empty;
            }

            int index = localeId.IndexOf('-');

            if (index != -1)
            {
                return localeId.Substring(0, index);
            }

            return localeId;
        }

        /// <summary>
        /// Returns the localized text from a list of available text
        /// </summary>
        public static LocalizedText SelectLocalizedText(IList<string> localeIds, IList<LocalizedText> names, LocalizedText defaultName)
        {
            // check if no locales requested.
            if (localeIds == null || localeIds.Count == 0)
            {
                return defaultName;
            }

            // check if no names provided.
            if (names == null || names.Count == 0)
            {
                return defaultName;
            }

            // match exactly.
            for (int ii = 0; ii < localeIds.Count; ii++)
            {
                for (int jj = 0; jj < names.Count; jj++)
                {
                    if (LocalizedText.IsNullOrEmpty(names[jj]))
                    {
                        continue;
                    }

                    if (string.Equals(names[jj].Locale, localeIds[ii], StringComparison.OrdinalIgnoreCase))
                    {
                        return names[jj];
                    }
                }
            }

            // match generic language.
            for (int ii = 0; ii < localeIds.Count; ii++)
            {
                string languageId = GetLanguageId(localeIds[ii]);

                for (int jj = 0; jj < names.Count; jj++)
                {
                    if (LocalizedText.IsNullOrEmpty(names[jj]))
                    {
                        continue;
                    }

                    string actualLanguageId = GetLanguageId(names[jj].Locale);

                    if (string.Equals(languageId, actualLanguageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return names[jj];
                    }
                }
            }

            // return default.
            return defaultName;
        }

        /// <summary>
        /// Returns a deep copy of the value.
        /// </summary>
        public static object Clone(ICloneable value)
        {
            return value?.Clone();
        }

        /// <summary>
        /// Returns a deep copy of the value.
        /// </summary>
        public static object Clone(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();

            // nothing to do for value types.
            if (type.GetTypeInfo().IsPrimitive)
            {
                return value;
            }

            // strings are special a reference type that does not need to be copied.
            if (type == typeof(string))
            {
                return value;
            }

            // Guid are special a reference type that does not need to be copied.
            if (type == typeof(Guid))
            {
                return value;
            }

            // copy arrays, any dimension.
            if (value is Array array)
            {
                if (array.Rank == 1)
                {
                    Array clone = Array.CreateInstance(type.GetElementType(), array.Length);
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        clone.SetValue(Utils.Clone(array.GetValue(ii)), ii);
                    }
                    return clone;
                }
                else
                {
                    int[] arrayRanks = new int[array.Rank];
                    int[] arrayIndex = new int[array.Rank];
                    for (int ii = 0; ii < array.Rank; ii++)
                    {
                        arrayRanks[ii] = array.GetLength(ii);
                        arrayIndex[ii] = 0;
                    }
                    Array clone = Array.CreateInstance(type.GetElementType(), arrayRanks);
                    for (int ii = 0; ii < array.Length; ii++)
                    {
                        clone.SetValue(Utils.Clone(array.GetValue(arrayIndex)), arrayIndex);

                        // iterate the index array
                        for (int ix = 0; ix < array.Rank; ix++)
                        {
                            arrayIndex[ix]++;
                            if (arrayIndex[ix] < arrayRanks[ix])
                            {
                                break;
                            }
                            arrayIndex[ix] = 0;
                        }
                    }
                    return clone;
                }
            }

            // use ICloneable if supported
            // must be checked before value type due to some
            // structs implementing ICloneable
            if (value is ICloneable cloneable)
            {
                return cloneable.Clone();
            }

            // nothing to do for other value types.
            if (type.GetTypeInfo().IsValueType)
            {
                return value;
            }

            // copy XmlNode.
            if (value is XmlNode node)
            {
                return node.CloneNode(true);
            }

            //try to find the MemberwiseClone method by reflection.
            MethodInfo memberwiseCloneMethod = type.GetMethod("MemberwiseClone", BindingFlags.Public | BindingFlags.Instance);
            if (memberwiseCloneMethod != null)
            {
                object clone = memberwiseCloneMethod.Invoke(value, null);
                if (clone != null)
                {
                    Utils.LogTrace("MemberwiseClone without ICloneable in class '{0}'", type.FullName);
                    return clone;
                }
            }

            //try to find the Clone method by reflection.
            MethodInfo cloneMethod = type.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance);
            if (cloneMethod != null)
            {
                object clone = cloneMethod.Invoke(value, null);
                if (clone != null)
                {
                    Utils.LogTrace("Clone without ICloneable in class '{0}'", type.FullName);
                    return clone;
                }
            }

            // don't know how to clone object.
            throw new NotSupportedException(Utils.Format("Don't know how to clone objects of type '{0}'", type.FullName));
        }

        /// <summary>
        /// Checks if two identities are equal.
        /// </summary>
        public static bool IsEqualUserIdentity(UserIdentityToken identity1, UserIdentityToken identity2)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(identity1, identity2))
            {
                return true;
            }

            if (identity1 == null || identity2 == null)
            {
                return false;
            }

            if (identity1 is AnonymousIdentityToken &&
                identity2 is AnonymousIdentityToken)
            {
                return true;
            }

            if (identity1 is UserNameIdentityToken userName1 &&
                identity2 is UserNameIdentityToken userName2)
            {
                return string.Equals(userName1.UserName, userName2.UserName, StringComparison.Ordinal);
            }

            if (identity1 is X509IdentityToken x509Token1 &&
                identity2 is X509IdentityToken x509Token2)
            {
                return Utils.IsEqual(x509Token1.CertificateData, x509Token2.CertificateData);
            }

            if (identity1 is IssuedIdentityToken issuedToken1 &&
                identity2 is IssuedIdentityToken issuedToken2)
            {
                return Utils.IsEqual(issuedToken1.DecryptedTokenData, issuedToken2.DecryptedTokenData);
            }

            return false;
        }

        /// <summary>
        /// Checks if two DateTime values are equal.
        /// </summary>
        public static bool IsEqual(DateTime time1, DateTime time2)
        {
            var utcTime1 = Utils.ToOpcUaUniversalTime(time1);
            var utcTime2 = Utils.ToOpcUaUniversalTime(time2);

            // values smaller than Timebase can not be binary encoded and are considered equal
            if (utcTime1 <= TimeBase && utcTime2 <= TimeBase)
            {
                return true;
            }

            if (utcTime1 >= DateTime.MaxValue && utcTime2 >= DateTime.MaxValue)
            {
                return true;
            }

            return utcTime1.CompareTo(utcTime2) == 0;
        }

        /// <summary>
        /// Checks if two T values are equal based on IEquatable compare.
        /// </summary>
        public static bool IsEqual<T>(T value1, T value2) where T : IEquatable<T>
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (Object.ReferenceEquals(value1, null))
            {
                if (!Object.ReferenceEquals(value2, null))
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // use IEquatable comparer
            return value1.Equals(value2);
        }

        /// <summary>
        /// Checks if two IEnumerable T values are equal.
        /// </summary>
        public static bool IsEqual<T>(IEnumerable<T> value1, IEnumerable<T> value2) where T : IEquatable<T>
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (Object.ReferenceEquals(value1, null) || Object.ReferenceEquals(value2, null))
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }

        /// <summary>
        /// Checks if two T[] values are equal.
        /// </summary>
        public static bool IsEqual<T>(T[] value1, T[] value2) where T : unmanaged, IEquatable<T>
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (Object.ReferenceEquals(value1, null) || Object.ReferenceEquals(value2, null))
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }

#if NETFRAMEWORK
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(byte[] b1, byte[] b2, long count);

        /// <summary>
        /// Fast memcpy version of byte[] compare.
        /// </summary>
        public static bool IsEqual(byte[] value1, byte[] value2)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (Object.ReferenceEquals(value1, null) || Object.ReferenceEquals(value2, null))
            {
                return false;
            }

            // Validate buffers are the same length.
            // This also ensures that the count does not exceed the length of either buffer.
            return value1.Length == value2.Length && memcmp(value1, value2, value1.Length) == 0;
        }
#endif

        /// <summary>
        /// Checks if two values are equal.
        /// </summary>
        public static bool IsEqual(object value1, object value2)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            // check for null values.
            if (Object.ReferenceEquals(value1, null))
            {
                if (!Object.ReferenceEquals(value2, null))
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // check for null values.
            if (Object.ReferenceEquals(value2, null))
            {
                return value1.Equals(value2);
            }

            // check for encodeable objects.
            if (value1 is IEncodeable encodeable1)
            {
                if (!(value2 is IEncodeable encodeable2))
                {
                    return false;
                }

                return encodeable1.IsEqual(encodeable2);
            }

            // check that data types are not the same.
            if (value1.GetType() != value2.GetType())
            {
                return value1.Equals(value2);
            }

            // check for DateTime objects
            if (value1 is DateTime time1)
            {
                return Utils.IsEqual(time1, (DateTime)value2);
            }

            // check for compareable objects.
            if (value1 is IComparable comparable1)
            {
                return comparable1.CompareTo(value2) == 0;
            }

            // check for XmlElement objects.
            if (value1 is XmlElement element1)
            {
                if (!(value2 is XmlElement element2))
                {
                    return false;
                }

                return element1.OuterXml == element2.OuterXml;
            }

            // check for arrays.
            if (value1 is Array array1)
            {
                // arrays are greater than non-arrays.
                if (!(value2 is Array array2))
                {
                    return false;
                }

                // shorter arrays are less than longer arrays.
                if (array1.Length != array2.Length)
                {
                    return false;
                }

                // compare the array dimension
                if (array1.Rank != array2.Rank)
                {
                    return false;
                }

                // compare each rank.
                for (int ii = 0; ii < array1.Rank; ii++)
                {
                    if (array1.GetLowerBound(ii) != array2.GetLowerBound(ii) ||
                        array1.GetUpperBound(ii) != array2.GetUpperBound(ii))
                    {
                        return false;
                    }
                }

                // handle byte[] special case fast
                if (array1 is byte[] byteArray1 && array2 is byte[] byteArray2)
                {
#if NETFRAMEWORK
                    return memcmp(byteArray1, byteArray2, byteArray1.Length) == 0;
#else
                    return byteArray1.SequenceEqual(byteArray2);
#endif
                }

                IEnumerator enumerator1 = array1.GetEnumerator();
                IEnumerator enumerator2 = array2.GetEnumerator();

                // compare each element.
                while (enumerator1.MoveNext())
                {
                    // length is already checked
                    enumerator2.MoveNext();

                    bool result = Utils.IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // arrays are identical.
                return true;
            }

            // check enumerables.

            if (value1 is IEnumerable enumerable1)
            {
                // collections are greater than non-collections.
                if (!(value2 is IEnumerable enumerable2))
                {
                    return false;
                }

                IEnumerator enumerator1 = enumerable1.GetEnumerator();
                IEnumerator enumerator2 = enumerable2.GetEnumerator();

                while (enumerator1.MoveNext())
                {
                    // enumerable2 must be shorter.
                    if (!enumerator2.MoveNext())
                    {
                        return false;
                    }

                    bool result = Utils.IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // enumerable2 must be longer.
                if (enumerator2.MoveNext())
                {
                    return false;
                }

                // must be equal.
                return true;
            }

            // check for objects that override the Equals function.
            return value1.Equals(value2);
        }

        /// <summary>
        /// Tests if the specified string matches the specified pattern.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static bool Match(string target, string pattern, bool caseSensitive)
        {
            // an empty pattern always matches.
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            // an empty string never matches.
            if (string.IsNullOrEmpty(target))
            {
                return false;
            }

            // check for exact match
            if (caseSensitive)
            {
                if (target == pattern)
                {
                    return true;
                }
            }
            else
            {
                if (string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            char c;
            char p;
            char l;

            int pIndex = 0;
            int tIndex = 0;

            while (tIndex < target.Length && pIndex < pattern.Length)
            {
                p = ConvertCase(pattern[pIndex++], caseSensitive);

                if (pIndex > pattern.Length)
                {
                    return (tIndex >= target.Length); // if end of string true
                }

                switch (p)
                {
                    // match zero or more char.
                    case '*':
                    {
                        while (tIndex < target.Length)
                        {
                            if (Match(target.Substring(tIndex++), pattern.Substring(pIndex), caseSensitive))
                            {
                                return true;
                            }
                        }

                        return Match(target, pattern.Substring(pIndex), caseSensitive);
                    }

                    // match any one char.
                    case '?':
                    {
                        // check if end of string when looking for a single character.
                        if (tIndex >= target.Length)
                        {
                            return false;
                        }

                        // check if end of pattern and still string data left.
                        if (pIndex >= pattern.Length && tIndex < target.Length - 1)
                        {
                            return false;
                        }

                        tIndex++;
                        break;
                    }

                    // match char set
                    case '[':
                    {
                        c = ConvertCase(target[tIndex++], caseSensitive);

                        if (tIndex > target.Length)
                        {
                            return false; // syntax
                        }

                        l = '\0';

                        // match a char if NOT in set []
                        if (pattern[pIndex] == '!')
                        {
                            ++pIndex;

                            p = ConvertCase(pattern[pIndex++], caseSensitive);

                            while (pIndex < pattern.Length)
                            {
                                if (p == ']') // if end of char set, then
                                {
                                    break; // no match found
                                }

                                if (p == '-')
                                {
                                    // check a range of chars?
                                    p = ConvertCase(pattern[pIndex], caseSensitive);

                                    // get high limit of range
                                    if (pIndex > pattern.Length || p == ']')
                                    {
                                        return false; // syntax
                                    }

                                    if (c >= l && c <= p)
                                    {
                                        return false; // if in range, return false
                                    }
                                }

                                l = p;

                                if (c == p) // if char matches this element
                                {
                                    return false; // return false
                                }

                                p = ConvertCase(pattern[pIndex++], caseSensitive);
                            }
                        }

                        // match if char is in set []
                        else
                        {
                            p = ConvertCase(pattern[pIndex++], caseSensitive);

                            while (pIndex < pattern.Length)
                            {
                                if (p == ']') // if end of char set, then no match found
                                {
                                    return false;
                                }

                                if (p == '-')
                                {
                                    // check a range of chars?
                                    p = ConvertCase(pattern[pIndex], caseSensitive);

                                    // get high limit of range
                                    if (pIndex > pattern.Length || p == ']')
                                    {
                                        return false; // syntax
                                    }

                                    if (c >= l && c <= p)
                                    {
                                        break; // if in range, move on
                                    }
                                }

                                l = p;

                                if (c == p) // if char matches this element move on
                                {
                                    break;
                                }

                                p = ConvertCase(pattern[pIndex++], caseSensitive);
                            }

                            while (pIndex < pattern.Length && p != ']') // got a match in char set skip to end of set
                            {
                                p = pattern[pIndex++];
                            }
                        }

                        break;
                    }

                    // match digit.
                    case '#':
                    {
                        c = target[tIndex++];

                        if (!char.IsDigit(c))
                        {
                            return false; // not a digit
                        }

                        break;
                    }

                    // match exact char.
                    default:
                    {
                        c = ConvertCase(target[tIndex++], caseSensitive);

                        if (c != p) // check for exact char
                        {
                            return false; // not a match
                        }

                        // check if end of pattern and still string data left.
                        if (pIndex >= pattern.Length && tIndex < target.Length - 1)
                        {
                            return false;
                        }

                        break;
                    }
                }
            }

            if (tIndex >= target.Length)
            {
                return pIndex >= pattern.Length; // if end of pattern true
            }

            return true;
        }

        // ConvertCase
        private static char ConvertCase(char c, bool caseSensitive)
        {
            return (caseSensitive) ? c : char.ToUpperInvariant(c);
        }

        /// <summary>
        /// Returns the TimeZone information for the current local time.
        /// </summary>
        /// <returns>The TimeZone information for the current local time.</returns>
        public static TimeZoneDataType GetTimeZoneInfo()
        {
            TimeZoneDataType info = new TimeZoneDataType();

            info.Offset = (short)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
            info.DaylightSavingInOffset = true;

            return info;
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to parse it.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="extensions">The list of extensions to search.</param>
        /// <param name="elementName">Name of the element (use type name if null).</param>
        /// <returns>
        /// The deserialized extension. Null if an error occurs.
        /// </returns>
        public static T ParseExtension<T>(IList<XmlElement> extensions, XmlQualifiedName elementName)
        {
            // check if nothing to search for.
            if (extensions == null || extensions.Count == 0)
            {
                return default;
            }

            // use the type name as the default.
            if (elementName == null)
            {
                // get qualified name from the data contract attribute.
                XmlQualifiedName qname = EncodeableFactory.GetXmlName(typeof(T));

                if (qname == null)
                {
                    throw new ArgumentException("Type does not seem to support DataContract serialization");
                }

                elementName = qname;
            }

            // find the element.
            for (int ii = 0; ii < extensions.Count; ii++)
            {
                XmlElement element = extensions[ii];

                if (element.LocalName != elementName.Name || element.NamespaceURI != elementName.Namespace)
                {
                    continue;
                }

                // type found.
                XmlReader reader = XmlReader.Create(new StringReader(element.OuterXml), Utils.DefaultXmlReaderSettings());

                try
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                    return (T)serializer.ReadObject(reader);
                }
                catch (Exception ex)
                {
                    Utils.LogError("Exception parsing extension: " + ex.Message);
                    throw;
                }
                finally
                {
                    reader.Dispose();
                }
            }

            return default;
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to serializes its replacement.
        /// </summary>
        /// <typeparam name="T">The type of the extension.</typeparam>
        /// <param name="extensions">The list of extensions to update.</param>
        /// <param name="elementName">Name of the element (use type name if null).</param>
        /// <param name="value">The value.</param>
        /// <remarks>
        /// Adds a new extension if the it does not already exist.
        /// Deletes the extension if the value is null.
        /// The containing element must use the name and namespace uri specified by the DataContractAttribute for the type.
        /// </remarks>
        public static void UpdateExtension<T>(ref XmlElementCollection extensions, XmlQualifiedName elementName, object value)
        {
            XmlDocument document = new XmlDocument();

            // serialize value.
            StringBuilder buffer = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(buffer, DefaultXmlWriterSettings()))
            {
                if (value != null)
                {
                    try
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                        serializer.WriteObject(writer, value);
                    }
                    finally
                    {
                        writer.Dispose();
                    }

                    document.LoadInnerXml(buffer.ToString());
                }
            }

            // use the type name as the default.
            if (elementName == null)
            {
                // get qualified name from the data contract attribute.
                XmlQualifiedName qname = EncodeableFactory.GetXmlName(typeof(T));

                if (qname == null)
                {
                    throw new ArgumentException("Type does not seem to support DataContract serialization");
                }

                elementName = qname;
            }

            // replace existing element.
            if (extensions != null)
            {
                for (int ii = 0; ii < extensions.Count; ii++)
                {
                    if (extensions[ii] != null && extensions[ii].LocalName == elementName.Name && extensions[ii].NamespaceURI == elementName.Namespace)
                    {
                        // remove the existing value if the value is null.
                        if (value == null)
                        {
                            extensions.RemoveAt(ii);
                            return;
                        }

                        extensions[ii] = document.DocumentElement;
                        return;
                    }
                }
            }

            // add new element.
            if (value != null)
            {
                if (extensions == null)
                {
                    extensions = new XmlElementCollection();
                }

                extensions.Add(document.DocumentElement);
            }
        }
        #endregion

        #region Reflection Helper Functions
        /// <summary>
        /// Returns the public static field names for a class.
        /// </summary>
        public static string[] GetFieldNames(Type systemType)
        {
            FieldInfo[] fields = systemType.GetFields(BindingFlags.Public | BindingFlags.Static);

            int ii = 0;

            string[] names = new string[fields.Length];

            foreach (FieldInfo field in fields)
            {
                names[ii++] = field.Name;
            }

            return names;
        }

        /// <summary>
        /// Returns the data member name for a property.
        /// </summary>
        public static string GetDataMemberName(PropertyInfo property)
        {
            object[] attributes = property.GetCustomAttributes(typeof(DataMemberAttribute), true).ToArray();

            if (attributes != null)
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii] is DataMemberAttribute contract)
                    {
                        if (string.IsNullOrEmpty(contract.Name))
                        {
                            return property.Name;
                        }

                        return contract.Name;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the numeric constant associated with a name.
        /// </summary>
        public static uint GetIdentifier(string name, Type constants)
        {
            FieldInfo[] fields = constants.GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (FieldInfo field in fields)
            {
                if (field.Name == name)
                {
                    return (uint)field.GetValue(constants);
                }
            }

            return 0;
        }

        private static readonly DateTime kBaseDateTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Return the current time in milliseconds since 1/1/2000.
        /// </summary>
        /// <returns>The current time in milliseconds since 1/1/2000.</returns>
        public static uint GetVersionTime()
        {
            var ticks = (DateTime.UtcNow - kBaseDateTime).TotalMilliseconds;
            return (uint)ticks;
        }

        /// <summary>
        /// Returns the linker timestamp for an assembly.
        /// </summary>
        public static DateTime GetAssemblyTimestamp()
        {
            try
            {
#if !NETSTANDARD1_4 && !NETSTANDARD1_3
                return File.GetLastWriteTimeUtc(typeof(Utils).GetTypeInfo().Assembly.Location);
#endif
            }
            catch
            { }
            return new DateTime(1970, 1, 1, 0, 0, 0);
        }

        /// <summary>
        /// Returns the major/minor version number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblySoftwareVersion()
        {
            return typeof(Utils).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        /// <summary>
        /// Returns the build/revision number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblyBuildNumber()
        {
            return typeof(Utils).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
        }

        #endregion

        #region Security Helper Functions
        /// <summary>
        /// Returns a XmlReaderSetting with safe defaults.
        /// DtdProcessing Prohibited, XmlResolver disabled and
        /// ConformanceLevel Document.
        /// </summary>
        public static XmlReaderSettings DefaultXmlReaderSettings()
        {
            return new XmlReaderSettings() {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ConformanceLevel = ConformanceLevel.Document
            };
        }

        /// <summary>
        /// Returns a XmlWriterSetting with deterministic defaults across .NET versions.
        /// </summary>
        public static XmlWriterSettings DefaultXmlWriterSettings()
        {
            return new XmlWriterSettings() {
                Encoding = Encoding.UTF8,
                Indent = true,
                ConformanceLevel = ConformanceLevel.Document,
                IndentChars = "  ",
                CloseOutput = false,
            };
        }

        /// <summary>
        /// Safe version for assignment of InnerXml.
        /// </summary>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="xml">The Xml document string.</param>
        internal static void LoadInnerXml(this XmlDocument doc, string xml)
        {
            using (var sreader = new StringReader(xml))
            using (var reader = XmlReader.Create(sreader, DefaultXmlReaderSettings()))
            {
                doc.XmlResolver = null;
                doc.Load(reader);
            }
        }

        /// <summary>
        /// Appends a list of byte arrays.
        /// </summary>
        public static byte[] Append(params byte[][] arrays)
        {
            if (arrays == null)
            {
                return Array.Empty<byte>();
            }

            int length = 0;

            for (int ii = 0; ii < arrays.Length; ii++)
            {
                if (arrays[ii] != null)
                {
                    length += arrays[ii].Length;
                }
            }

            byte[] output = new byte[length];

            int pos = 0;

            for (int ii = 0; ii < arrays.Length; ii++)
            {
                if (arrays[ii] != null)
                {
                    Array.Copy(arrays[ii], 0, output, pos, arrays[ii].Length);
                    pos += arrays[ii].Length;
                }
            }

            return output;
        }

        /// <summary>
        /// Creates a X509 certificate object from the DER encoded bytes.
        /// </summary>
        public static X509Certificate2 ParseCertificateBlob(ReadOnlyMemory<byte> certificateData, bool useAsnParser = false)
        {
            // macOS X509Certificate2 constructor throws exception if a certchain is encoded
            // use AsnParser on macOS to parse for byteblobs,
#if !NETFRAMEWORK
            useAsnParser = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
            try
            {
#if !NETFRAMEWORK
                if (useAsnParser)
                {
                    var certBlob = AsnUtils.ParseX509Blob(certificateData);
                    return CertificateFactory.Create(certBlob, true);
                }
                else
#endif
                {
                    return CertificateFactory.Create(certificateData, true);
                }
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Could not parse DER encoded form of a X509 certificate.",
                    e);
            }
        }

        /// <summary>
        /// Creates a X509 certificate collection object from the DER encoded bytes.
        /// </summary>
        /// <param name="certificateData">The certificate data.</param>
        /// <param name="useAsnParser">Whether the ASN.1 library should be used to decode certificate blobs.</param>
        /// <returns></returns>
        public static X509Certificate2Collection ParseCertificateChainBlob(ReadOnlyMemory<byte> certificateData, bool useAsnParser = false)
        {
            X509Certificate2Collection certificateChain = new X509Certificate2Collection();

            // macOS X509Certificate2 constructor throws exception if a certchain is encoded
            // use AsnParser on macOS to parse for byteblobs,
#if !NETFRAMEWORK
            useAsnParser = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
            int offset = 0;
            int length = certificateData.Length;
            while (offset < length)
            {
                X509Certificate2 certificate;
                try
                {
#if !NETFRAMEWORK
                    if (useAsnParser)
                    {
                        var certBlob = AsnUtils.ParseX509Blob(certificateData.Slice(offset));
                        certificate = CertificateFactory.Create(certBlob, true);
                    }
                    else
#endif
                    {
                        certificate = CertificateFactory.Create(certificateData.Slice(offset), true);
                    }
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Could not parse DER encoded form of a X509 certificate.",
                        e);
                }

                certificateChain.Add(certificate);
                offset += certificate.RawData.Length;
            }

            return certificateChain;
        }


        /// <summary>
        /// Creates a DER blob from a X509Certificate2Collection.
        /// </summary>
        /// <param name="certificates">The certificates to be returned as raw data.</param>
        /// <returns>
        /// A DER blob containing zero or more certificates.
        /// </returns>
        public static byte[] CreateCertificateChainBlob(X509Certificate2Collection certificates)
        {
            if (certificates == null || certificates.Count == 0)
            {
                return Array.Empty<byte>();
            }

            int totalSize = 0;

            foreach (X509Certificate2 cert in certificates)
            {
                totalSize += cert.RawData.Length;
            }

            byte[] blobData = new byte[totalSize];
            int offset = 0;

            foreach (X509Certificate2 cert in certificates)
            {
                Array.Copy(cert.RawData, 0, blobData, offset, cert.RawData.Length);
                offset += cert.RawData.Length;
            }

            return blobData;
        }
        /// <summary>
        /// Compare Nonce for equality.
        /// </summary>
        [Obsolete("Use equivalent methods from the Opc.Ua.Nonce class")]
        public static bool CompareNonce(byte[] a, byte[] b)
        {
            return NewNonceImplementation.CompareNonce(a, b);
        }

        /// <summary>
        /// Cryptographic Nonce helper functions.
        /// </summary>
        [Obsolete("Use equivalent methods from the Opc.Ua.Nonce class")]
        public static class Nonce
        {
            /// <summary>
            /// Generates a Nonce for cryptographic functions.
            /// </summary>
            [Obsolete("Use equivalent CreateRandomNonceData method from the Opc.Ua.Nonce class")]
            public static byte[] CreateNonce(uint length)
            {
                return NewNonceImplementation.CreateRandomNonceData(length);
            }

            /// <summary>
            /// Returns the length of the symmetric encryption key for a security policy.
            /// </summary>
            [Obsolete("Use equivalent method from the Opc.Ua.Nonce class")]
            public static uint GetNonceLength(string securityPolicyUri)
            {
                return NewNonceImplementation.GetNonceLength(securityPolicyUri);
            }

            /// <summary>
            /// Validates the nonce for a message security mode and security policy.
            /// </summary>
            [Obsolete("Use equivalent method from the Opc.Ua.Nonce class")]
            public static bool ValidateNonce(byte[] nonce, MessageSecurityMode securityMode, string securityPolicyUri)
            {
                return NewNonceImplementation.ValidateNonce(nonce, securityMode, GetNonceLength(securityPolicyUri));
            }

            /// <summary>
            /// Validates the nonce for a message security mode and a minimum length.
            /// </summary>
            [Obsolete("Use equivalent method from the Opc.Ua.Nonce class")]
            public static bool ValidateNonce(byte[] nonce, MessageSecurityMode securityMode, uint minNonceLength)
            {
                return NewNonceImplementation.ValidateNonce(nonce, securityMode, minNonceLength);
            }
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA1 alhorithm.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "SHA1 is needed for deprecated security profiles.")]
        public static byte[] PSHA1(byte[] secret, string label, byte[] data, int offset, int length)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            // create the hmac.
            using (HMACSHA1 hmac = new HMACSHA1(secret))
            {
                return PSHA(hmac, label, data, offset, length);
            }
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA256 alhorithm.
        /// </summary>
        public static byte[] PSHA256(byte[] secret, string label, byte[] data, int offset, int length)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            // create the hmac.
            using (HMACSHA256 hmac = new HMACSHA256(secret))
            {
                return PSHA(hmac, label, data, offset, length);
            }
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA1 alhorithm.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "SHA1 is needed for deprecated security profiles.")]
        public static byte[] PSHA1(HMACSHA1 hmac, string label, byte[] data, int offset, int length)
        {
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA256 alhorithm.
        /// </summary>
        public static byte[] PSHA256(HMACSHA256 hmac, string label, byte[] data, int offset, int length)
        {
            return PSHA(hmac, label, data, offset, length);
        }


        /// <summary>
        /// Generates a Pseudo random sequence of bits using the HMAC algorithm.
        /// </summary>
        public static byte[] PSHA(HMAC hmac, string label, byte[] data, int offset, int length)
        {
            if (hmac == null) throw new ArgumentNullException(nameof(hmac));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            byte[] seed = null;

            // convert label to UTF-8 byte sequence.
            if (!string.IsNullOrEmpty(label))
            {
                seed = Encoding.UTF8.GetBytes(label);
            }

            // append data to label.
            if (data != null && data.Length > 0)
            {
                if (seed != null)
                {
                    byte[] seed2 = new byte[seed.Length + data.Length];
                    seed.CopyTo(seed2, 0);
                    data.CopyTo(seed2, seed.Length);
                    seed = seed2;
                }
                else
                {
                    seed = data;
                }
            }

            // check for a valid seed.
            if (seed == null)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The HMAC algorithm requires a non-null seed.");
            }

            byte[] keySeed = hmac.ComputeHash(seed);
            byte[] prfSeed = new byte[(hmac.HashSize / 8) + seed.Length];
            Array.Copy(keySeed, prfSeed, keySeed.Length);
            Array.Copy(seed, 0, prfSeed, keySeed.Length, seed.Length);

            // create buffer with requested size.
            byte[] output = new byte[length];

            int position = 0;
            do
            {
                byte[] hash = hmac.ComputeHash(prfSeed);

                if (offset < hash.Length)
                {
                    for (int ii = offset; position < length && ii < hash.Length; ii++)
                    {
                        output[position++] = hash[ii];
                    }
                }

                if (offset > hash.Length)
                {
                    offset -= hash.Length;
                }
                else
                {
                    offset = 0;
                }

                keySeed = hmac.ComputeHash(keySeed);
                Array.Copy(keySeed, prfSeed, keySeed.Length);
            }
            while (position < length);

            // return random data.
            return output;
        }


        /// <summary>
        /// Creates an HMAC.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security",
            "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "<Pending>")]
        public static HMAC CreateHMAC(HashAlgorithmName algorithmName, byte[] secret)
        {
            if (algorithmName == HashAlgorithmName.SHA256)
            {
                return new HMACSHA256(secret);
            }

            if (algorithmName == HashAlgorithmName.SHA384)
            {
                return new HMACSHA384(secret);
            }

            if (algorithmName == HashAlgorithmName.SHA1)
            {
                return new HMACSHA1(secret);
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Checks if the target is in the list. Comparisons ignore case.
        /// </summary>
        public static bool FindStringIgnoreCase(IList<string> strings, string target)
        {
            if (strings == null || strings.Count == 0)
            {
                return false;
            }

            for (int ii = 0; ii < strings.Count; ii++)
            {
                if (string.Equals(strings[ii], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns if the certificate type is supported on the platform OS.
        /// </summary>
        /// <param name="certificateType">The certificate type to check.</param>
        public static bool IsSupportedCertificateType(NodeId certificateType)
        {
            if (certificateType.Identifier is uint identifier)
            {
                switch (identifier)
                {
#if ECC_SUPPORT
                    case ObjectTypes.EccApplicationCertificateType:
                        return true;
                    case ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType:
                        return s_eccCurveSupportCache[ECCurve.NamedCurves.brainpoolP256r1.Oid.FriendlyName].Value;
                    case ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType:
                        return s_eccCurveSupportCache[ECCurve.NamedCurves.brainpoolP384r1.Oid.FriendlyName].Value;
                    case ObjectTypes.EccNistP256ApplicationCertificateType:
                        return s_eccCurveSupportCache[ECCurve.NamedCurves.nistP256.Oid.FriendlyName].Value;
                    case ObjectTypes.EccNistP384ApplicationCertificateType:
                        return s_eccCurveSupportCache[ECCurve.NamedCurves.nistP384.Oid.FriendlyName].Value;
                    //case ObjectTypes.EccCurve25519ApplicationCertificateType:
                    //case ObjectTypes.EccCurve448ApplicationCertificateType:
#endif
                    case ObjectTypes.ApplicationCertificateType:
                    case ObjectTypes.RsaMinApplicationCertificateType:
                    case ObjectTypes.RsaSha256ApplicationCertificateType:
                    case ObjectTypes.HttpsCertificateType:
                    case ObjectTypes.UserCredentialCertificateType:
                        return true;
                }
            }
            return false;
        }
#if ECC_SUPPORT
        /// <summary>
        /// Check if known curve is supported by platform
        /// </summary>
        /// <param name="eCCurve"></param>
        private static bool IsCurveSupported(ECCurve eCCurve)
        {
            try
            {
                // Create a ECDsa object and generate a new keypair on the given curve
                using (ECDsa eCDsa = ECDsa.Create(eCCurve))
                {
                    ECParameters parameters = eCDsa.ExportParameters(false);
                    return parameters.Q.X != null && parameters.Q.Y != null;
                }
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException ||
                ex is ArgumentException ||
                ex is CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// Lazy helper for checking ECC eliptic curve support for running OS
        /// </summary>
        private static readonly Dictionary<string, Lazy<bool>> s_eccCurveSupportCache = new Dictionary<string, Lazy<bool>>{
            { ECCurve.NamedCurves.nistP256.Oid.FriendlyName, new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.nistP256)) },
            { ECCurve.NamedCurves.nistP384.Oid.FriendlyName, new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.nistP384)) },
            { ECCurve.NamedCurves.brainpoolP256r1.Oid.FriendlyName, new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.brainpoolP256r1)) },
            { ECCurve.NamedCurves.brainpoolP384r1.Oid.FriendlyName, new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.brainpoolP384r1)) },
        };
#endif

        /// <summary>
        /// Lazy helper to allow runtime check for Mono.
        /// </summary>
        private static readonly Lazy<bool> s_isRunningOnMonoValue = new Lazy<bool>(() => {
            return Type.GetType("Mono.Runtime") != null;
        });

        /// <summary>
        /// Determine if assembly uses mono runtime.
        /// </summary>
        /// <returns>true if running on Mono runtime</returns>
        public static bool IsRunningOnMono()
        {
            return s_isRunningOnMonoValue.Value;
        }
        #endregion
    }
}
