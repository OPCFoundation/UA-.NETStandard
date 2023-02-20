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
using System.Text;
using System.Xml;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Runtime.Serialization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;
using System.Collections.ObjectModel;
using Opc.Ua.Security.Certificates;
using System.Runtime.InteropServices;

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
        /// The URI scheme for the HTTPS protocol.
        /// </summary>
        public const string UriSchemeHttps = "https";

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
            Utils.UriSchemeHttps
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
        public static readonly string DefaultLocalFolder = Directory.GetCurrentDirectory();

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
                { Utils.UriSchemeHttps, "Opc.Ua.Bindings.Https"}
            });
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
        private static object s_traceFileLock = new object();

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
            if (String.IsNullOrEmpty(message))
            {
                return;
            }

            // format the message if format arguments provided.
            string output = message;

            if (args != null && args.Length > 0)
            {
                try
                {
                    output = String.Format(CultureInfo.InvariantCulture, message, args);
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

                if (s_traceOutput != (int)TraceOutput.Off && !String.IsNullOrEmpty(traceFileName))
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
                if (String.IsNullOrEmpty(filePath))
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
                        new String('*', 25));
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
                ServiceResultException sre = e as ServiceResultException;

                if (sre != null)
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
                    var separator = new String('=', 40);
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
                    message.Append(TraceExceptionMessage(exception, String.Empty, null));
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
            if (String.IsNullOrEmpty(input))
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
                path = String.Empty;
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
            if (String.IsNullOrEmpty(fileName))
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

            if (!String.IsNullOrEmpty(filePath))
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

            if (!String.IsNullOrEmpty(dirPath))
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
        /// Supresses any exceptions while disposing the object.
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
        /// Supresses any exceptions while disposing the object.
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
            if (timeSpan.TotalMilliseconds > Int32.MaxValue)
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
            if (String.IsNullOrEmpty(domainName))
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
            if (String.IsNullOrEmpty(uri))
            {
                return uri;
            }

            // IPv6 address needs a surrounding [] 
            if (!String.IsNullOrEmpty(hostname) && hostname.Contains(':'))
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
            if (String.IsNullOrEmpty(subjectName))
            {
                return subjectName;
            }

            // IPv6 address needs a surrounding [] 
            if (!String.IsNullOrEmpty(hostname) && hostname.Contains(':'))
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
        /// Parses a URI string. Returns null if it is invalid.
        /// </summary>
        public static Uri ParseUri(string uri)
        {
            try
            {
                if (String.IsNullOrEmpty(uri))
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
            if (String.IsNullOrEmpty(domain1) || String.IsNullOrEmpty(domain2))
            {
                return false;
            }

            if (String.Equals(domain1, domain2, StringComparison.OrdinalIgnoreCase))
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
            if (String.IsNullOrEmpty(instanceUri))
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
            System.Threading.Interlocked.CompareExchange(ref identifier, 0, UInt32.MaxValue);
            return (uint)System.Threading.Interlocked.Increment(ref identifier);
        }

        /// <summary>
        /// Increments a identifier (wraps around if max exceeded).
        /// </summary>
        public static int IncrementIdentifier(ref int identifier)
        {
            System.Threading.Interlocked.CompareExchange(ref identifier, 0, Int32.MaxValue);
            return System.Threading.Interlocked.Increment(ref identifier);
        }

        /// <summary>
        /// Safely converts an UInt32 identifier to a Int32 identifier.
        /// </summary>
        public static int ToInt32(uint identifier)
        {
            if (identifier <= (uint)Int32.MaxValue)
            {
                return (int)identifier;
            }

            return -(int)((long)UInt32.MaxValue - (long)identifier + 1);
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

            return (uint)((long)UInt32.MaxValue + 1 + (long)identifier);
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
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(buffer.Length * 2);

            if (invertEndian)
            {
                for (int ii = buffer.Length - 1; ii >= 0; ii--)
                {
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                }
            }
            else
            {
                for (int ii = 0; ii < buffer.Length; ii++)
                {
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", buffer[ii]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Converts a hexadecimal string to an array of bytes.
        /// </summary>
        public static byte[] FromHexString(string buffer)
        {
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
        }

        /// <summary>
        /// Formats an object using the invariant locale.
        /// </summary>
        public static string ToString(object source)
        {
            if (source != null)
            {
                return String.Format(CultureInfo.InvariantCulture, "{0}", source);
            }

            return String.Empty;
        }

        /// <summary>
        /// Formats a message using the invariant locale.
        /// </summary>
        public static string Format(string text, params object[] args)
        {
            return String.Format(CultureInfo.InvariantCulture, text, args);
        }

        /// <summary>
        /// Checks if a string is a valid locale identifier.
        /// </summary>
        public static bool IsValidLocaleId(string localeId)
        {
            if (String.IsNullOrEmpty(localeId))
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
                return String.Empty;
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

                    if (String.Equals(names[jj].Locale, localeIds[ii], StringComparison.OrdinalIgnoreCase))
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

                    if (String.Equals(languageId, actualLanguageId, StringComparison.OrdinalIgnoreCase))
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
        public static object Clone(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();

            // nothing to do for value types.
            if (type.GetTypeInfo().IsValueType)
            {
                return value;
            }

            // strings are special a reference type that does not need to be copied.
            if (type == typeof(string))
            {
                return value;
            }

            // copy arrays, any dimension.
            Array array = value as Array;
            if (array != null)
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

            // copy XmlNode.
            XmlNode node = value as XmlNode;
            if (node != null)
            {
                return node.CloneNode(true);
            }

            // use ICloneable if supported
            ICloneable cloneable = value as ICloneable;
            if (cloneable != null)
            {
                return cloneable.Clone();
            }

            // copy ExtensionObject.
            {
                ExtensionObject castedObject = value as ExtensionObject;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy ExtensionObjectCollection.
            {
                ExtensionObjectCollection castedObject = value as ExtensionObjectCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy EnumValueType.
            {
                EnumValueType castedObject = value as EnumValueType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy LocalizedText.
            {
                LocalizedText castedObject = value as LocalizedText;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy Argument.
            {
                Argument castedObject = value as Argument;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy NodeId.
            {
                NodeId castedObject = value as NodeId;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy UInt32Collection.
            {
                UInt32Collection castedObject = value as UInt32Collection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy QualifiedName.
            {
                QualifiedName castedObject = value as QualifiedName;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy ServerDiagnosticsSummaryDataType.
            {
                ServerDiagnosticsSummaryDataType castedObject = value as ServerDiagnosticsSummaryDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy ApplicationDescription.
            {
                ApplicationDescription castedObject = value as ApplicationDescription;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy StringCollection.
            {
                StringCollection castedObject = value as StringCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy UserTokenPolicyCollection.
            {
                UserTokenPolicyCollection castedObject = value as UserTokenPolicyCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy UserTokenPolicy
            {
                UserTokenPolicy castedObject = value as UserTokenPolicy;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy SessionDiagnosticsDataType
            {
                SessionDiagnosticsDataType castedObject = value as SessionDiagnosticsDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy ServiceCounterDataType
            {
                ServiceCounterDataType castedObject = value as ServiceCounterDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy SessionSecurityDiagnosticsDataType
            {
                SessionSecurityDiagnosticsDataType castedObject = value as SessionSecurityDiagnosticsDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy AnonymousIdentityToken
            {
                AnonymousIdentityToken castedObject = value as AnonymousIdentityToken;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy EventFilter.
            {
                EventFilter castedObject = value as EventFilter;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy DataChangeFilter.
            {
                DataChangeFilter castedObject = value as DataChangeFilter;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy SimpleAttributeOperandCollection.
            {
                SimpleAttributeOperandCollection castedObject = value as SimpleAttributeOperandCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy SimpleAttributeOperand.
            {
                SimpleAttributeOperand castedObject = value as SimpleAttributeOperand;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy QualifiedNameCollection.
            {
                QualifiedNameCollection castedObject = value as QualifiedNameCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy ContentFilter.
            {
                ContentFilter castedObject = value as ContentFilter;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy ContentFilterElement.
            {
                ContentFilterElement castedObject = value as ContentFilterElement;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            // copy ContentFilterElementCollection.
            {
                ContentFilterElementCollection castedObject = value as ContentFilterElementCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy SubscriptionDiagnosticsDataType.
            {
                SubscriptionDiagnosticsDataType castedObject = value as SubscriptionDiagnosticsDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy UserNameIdentityToken.
            {
                UserNameIdentityToken castedObject = value as UserNameIdentityToken;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy ServerStatusDataType.
            {
                ServerStatusDataType castedObject = value as ServerStatusDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy BuildInfo.
            {
                BuildInfo castedObject = value as BuildInfo;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy X509IdentityToken.
            {
                X509IdentityToken castedObject = value as X509IdentityToken;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.Range.
            {
                Opc.Ua.Range castedObject = value as Opc.Ua.Range;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.EUInformation
            {
                Opc.Ua.EUInformation castedObject = value as Opc.Ua.EUInformation;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.WriteValueCollection
            {
                Opc.Ua.WriteValueCollection castedObject = value as Opc.Ua.WriteValueCollection;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.WriteValue
            {
                Opc.Ua.WriteValue castedObject = value as Opc.Ua.WriteValue;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.DataValue
            {
                Opc.Ua.DataValue castedObject = value as Opc.Ua.DataValue;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.ExpandedNodeId
            {
                ExpandedNodeId castedObject = value as ExpandedNodeId;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.TimeZoneDataType
            {
                TimeZoneDataType castedObject = value as TimeZoneDataType;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }
            // copy Opc.Ua.LiteralOperand
            {
                LiteralOperand castedObject = value as LiteralOperand;
                if (castedObject != null)
                {
                    return castedObject.MemberwiseClone();
                }
            }

            //try to find the MemberwiseClone method by reflection.
            MethodInfo memberwiseCloneMethod = type.GetMethod("MemberwiseClone", BindingFlags.Public | BindingFlags.Instance);
            if (memberwiseCloneMethod != null)
            {
                object clone = memberwiseCloneMethod.Invoke(value, null);
                if (clone != null)
                {
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
            if (value1 == null)
            {
                if (value2 != null)
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // check for null values.
            if (value2 == null)
            {
                return value1.Equals(value2);
            }

            // check that data types are the same.
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
            IComparable comparable1 = value1 as IComparable;

            if (comparable1 != null)
            {
                return comparable1.CompareTo(value2) == 0;
            }

            // check for encodeable objects.
            IEncodeable encodeable1 = value1 as IEncodeable;

            if (encodeable1 != null)
            {
                if (!(value2 is IEncodeable encodeable2))
                {
                    return false;
                }

                return encodeable1.IsEqual(encodeable2);
            }

            // check for XmlElement objects.
            XmlElement element1 = value1 as XmlElement;

            if (element1 != null)
            {
                if (!(value2 is XmlElement element2))
                {
                    return false;
                }

                return element1.OuterXml == element2.OuterXml;
            }

            // check for arrays.
            Array array1 = value1 as Array;

            if (array1 != null)
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
            IEnumerable enumerable1 = value1 as IEnumerable;

            if (enumerable1 != null)
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
                if (String.Equals(target, pattern, StringComparison.OrdinalIgnoreCase))
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

                        if (!Char.IsDigit(c))
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
            return (caseSensitive) ? c : Char.ToUpperInvariant(c);
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
                return default(T);
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

            return default(T);
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
            using (XmlWriter writer = XmlWriter.Create(buffer))
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
                    DataMemberAttribute contract = attributes[ii] as DataMemberAttribute;

                    if (contract != null)
                    {
                        if (String.IsNullOrEmpty(contract.Name))
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
        public static X509Certificate2 ParseCertificateBlob(byte[] certificateData)
        {
            // macOS X509Certificate2 constructor throws exception if a certchain is encoded
            // use AsnParser on macOS to parse for byteblobs,
            bool useAsnParser = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            try
            {
                if (useAsnParser)
                {
                    var certBlob = AsnUtils.ParseX509Blob(certificateData);
                    return CertificateFactory.Create(certBlob, true);
                }
                else
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
        /// <returns></returns>
        public static X509Certificate2Collection ParseCertificateChainBlob(byte[] certificateData)
        {
            X509Certificate2Collection certificateChain = new X509Certificate2Collection();
            List<byte> certificatesBytes = new List<byte>(certificateData);
            // macOS X509Certificate2 constructor throws exception if a certchain is encoded
            // use AsnParser on macOS to parse for byteblobs,
            bool useAsnParser = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            while (certificatesBytes.Count > 0)
            {
                X509Certificate2 certificate;
                try
                {
                    if (useAsnParser)
                    {
                        var certBlob = AsnUtils.ParseX509Blob(certificatesBytes.ToArray());
                        certificate = CertificateFactory.Create(certBlob, true);
                    }
                    else
                    {
                        certificate = CertificateFactory.Create(certificatesBytes.ToArray(), true);
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
                certificatesBytes.RemoveRange(0, certificate.RawData.Length);
            }

            return certificateChain;
        }

        /// <summary>
        /// Compare Nonce for equality.
        /// </summary>
        public static bool CompareNonce(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            byte result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= (byte)(a[i] ^ b[i]);

            return result == 0;
        }

        /// <summary>
        /// Cryptographic Nonce helper functions.
        /// </summary>
        public static class Nonce
        {
            static readonly RandomNumberGenerator m_rng = RandomNumberGenerator.Create();

            /// <summary>
            /// Generates a Nonce for cryptographic functions.
            /// </summary>
            public static byte[] CreateNonce(uint length)
            {
                byte[] randomBytes = new byte[length];
                m_rng.GetBytes(randomBytes);
                return randomBytes;
            }

            /// <summary>
            /// Returns the length of the symmetric encryption key for a security policy.
            /// </summary>
            public static uint GetNonceLength(string securityPolicyUri)
            {
                switch (securityPolicyUri)
                {
                    case SecurityPolicies.Basic128Rsa15:
                    {
                        return 16;
                    }

                    case SecurityPolicies.Basic256:
                    case SecurityPolicies.Basic256Sha256:
                    case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return 32;
                    }

                    default:
                    case SecurityPolicies.None:
                    {
                        return 0;
                    }
                }
            }

            /// <summary>
            /// Validates the nonce for a message security mode and security policy.
            /// </summary>
            public static bool ValidateNonce(byte[] nonce, MessageSecurityMode securityMode, string securityPolicyUri)
            {
                return ValidateNonce(nonce, securityMode, GetNonceLength(securityPolicyUri));
            }

            /// <summary>
            /// Validates the nonce for a message security mode and a minimum length.
            /// </summary>
            public static bool ValidateNonce(byte[] nonce, MessageSecurityMode securityMode, uint minNonceLength)
            {
                // no nonce needed for no security.
                if (securityMode == MessageSecurityMode.None)
                {
                    return true;
                }

                // check the length.
                if (nonce == null || nonce.Length < minNonceLength)
                {
                    return false;
                }

                // try to catch programming errors by rejecting nonces with all zeros.
                for (int ii = 0; ii < nonce.Length; ii++)
                {
                    if (nonce[ii] != 0)
                    {
                        return true;
                    }
                }

                return false;
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
            HMACSHA1 hmac = new HMACSHA1(secret);
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA256 alhorithm.
        /// </summary>
        public static byte[] PSHA256(byte[] secret, string label, byte[] data, int offset, int length)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            // create the hmac.
            HMACSHA256 hmac = new HMACSHA256(secret);
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the HMAC algorithm.
        /// </summary>
        private static byte[] PSHA(HMAC hmac, string label, byte[] data, int offset, int length)
        {
            if (hmac == null) throw new ArgumentNullException(nameof(hmac));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            byte[] seed = null;

            // convert label to UTF-8 byte sequence.
            if (!String.IsNullOrEmpty(label))
            {
                seed = new UTF8Encoding().GetBytes(label);
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
                if (String.Equals(strings[ii], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

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
