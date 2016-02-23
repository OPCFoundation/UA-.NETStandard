/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
using System.Runtime.InteropServices;
using Windows.Storage;
using System.Linq;
using Windows.ApplicationModel;
using Windows.Networking;
using Windows.Networking.Sockets;
using System.Net;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.Networking.Connectivity;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static class Utils
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
        /// The URI scheme for using HTTP protocol without any security. 
        /// </summary>
        public const string UriSchemeNoSecurityHttp = "nosecurityhttp";

        /// <summary>
        /// The URI scheme for the UA TCP protocol. 
        /// </summary>
        public const string UriSchemeOpcTcp = "opc.tcp";
        
        /// <summary>
        /// The URI scheme for the .NET TCP protocol. 
        /// </summary>
        public const string UriSchemeNetTcp = "net.tcp";

        /// <summary>
        /// The URI scheme for the .NET Named Pipes protocol. 
        /// </summary>
        public const string UriSchemeNetPipe = "net.pipe";

        /// <summary>
        /// The default port for the UA TCP protocol.
        /// </summary>
        public const int UaTcpDefaultPort = 4840;
        
        /// <summary>
		/// The urls of the discovery servers on a node.
		/// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        public static readonly string[] DiscoveryUrls = new string[]
		{ 
            "opc.tcp://{0}:4840",
            "https://{0}:4843",
            "http://{0}:52601/UADiscovery",
            "http://{0}/UADiscovery/Default.svc"
		};
        
        /// <summary>
        /// The class that provides the default implementation for the UA TCP protocol.
        /// </summary>
        public const string UaTcpBindingDefault = "Opc.Ua.Bindings.UaTcpBinding";

        /// <summary>
        /// The class that wraps the ANSI C implementation of the UA TCP protocol.
        /// </summary>
        public const string UaTcpBindingNativeStack = "Opc.Ua.NativeStack.NativeStackBinding,Opc.Ua.NativeStackWrapper";

        /// <summary>
        /// The default certificate store's type.
        /// </summary>
        public const string DefaultStoreType = CertificateStoreType.Directory;
        #endregion
                
        #region Trace Support
        #if DEBUG
        private static int s_traceOutput = (int)TraceOutput.DebugAndFile;
        private static int s_traceMasks = (int)TraceMasks.All;
        #else
        private static int s_traceOutput = (int)TraceOutput.FileOnly;
        private static int s_traceMasks = (int)TraceMasks.None;
        #endif

        private static string s_traceFileName = null;
        private static long s_BaseLineTicks = DateTime.UtcNow.Ticks;
        private static object s_traceFileLock = new object();

        /// <summary>
        /// The possible trace output mechanisms.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
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
            DebugAndFile = 2,

            /// <summary>
            /// Write to trace listeners and a file (if specified).
            /// </summary>
            StdOutAndFile = 3
        }

        /// <summary>
        /// The masks used to filter trace messages.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
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
            public const int All = 0x7FFFFFFF;
        }


        /// <summary>
        /// Sets the output for tracing (thead safe).
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
        /// Sets the mask for tracing (thead safe).
        /// </summary>
        public static void SetTraceMask(int masks)
        {
            s_traceMasks = (int)masks;
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

            // write to the log file.
            lock (s_traceFileLock)
            {
                // write to debug trace listeners.
                if (s_traceOutput == (int)TraceOutput.DebugAndFile)
                {
                    System.Diagnostics.Debug.WriteLine(output);
                }

                string traceFileName = s_traceFileName;

                if (s_traceOutput != (int)TraceOutput.Off && !String.IsNullOrEmpty(traceFileName))
                {
                    try
                    {
                        FileInfo file = new FileInfo(traceFileName);

                        // limit the file size. hard coded for now - fix later.
                        bool truncated = false;

                        if (file.Exists && file.Length > 10000000)
                        {
                            file.Delete();
                            truncated = true;
                        }

                        using (StreamWriter writer = new StreamWriter(File.Open(traceFileName, FileMode.Append)))
                        {
                            if (truncated)
                            {
                                writer.WriteLine("WARNING - LOG FILE TRUNCATED.");
                            }

                            writer.WriteLine(output);
                            writer.Flush();
                            writer.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Could not write to trace file. Error={0}\r\nFilePath={1}", e.Message, traceFileName);
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
                    TraceWriteLine(
                        "\r\nPID:{2} {1} Logging started at {0}",
                        DateTime.Now,
                        new String('*', 25));
                }
                catch (Exception e)
                {
                    TraceWriteLine(e.Message, null);
                }
            }
        }
        
        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        public static void Trace(string format, params object[] args)
        {
            Trace((int)TraceMasks.Information, format, args);
        }
        
        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        public static void Trace(Exception e, string format, params object[] args)
        {
            StringBuilder message = new StringBuilder();
                        
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
                
                // append stack trace.
                if ((s_traceMasks & (int)TraceMasks.StackTrace) != 0)
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, "\r\n\r\n{0}\r\n", new String('=', 40));              
                    message.Append(new ServiceResult(e).ToLongString());
                    message.AppendFormat(CultureInfo.InvariantCulture, "\r\n{0}\r\n", new String('=', 40));
                }
            }
            
            // trace message.
            Trace((int)TraceMasks.Error, message.ToString(), null);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Trace(int traceMask, string format, params object[] args)
        {
            // do nothing if mask not enabled.
            if ((s_traceMasks & traceMask) == 0)
            {
                return;
            }

            double seconds = ((double)(HiResClock.UtcNow.Ticks - s_BaseLineTicks))/TimeSpan.TicksPerSecond;
            
            StringBuilder message = new StringBuilder();

            // append process and timestamp.
            message.AppendFormat("{0:HH:mm:ss.fff} ", HiResClock.UtcNow.ToLocalTime());

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

            TraceWriteLine(message.ToString(), null);
        }
        #endregion

        #region File Access
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
            if (input.Length > 1 && ((input[0] == '\\' && input[1] == '\\') || input[1] == ':'))
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
                folder = input.Substring(1, index-1);
                path = input.Substring(index+1);
            }

            StringBuilder buffer = new StringBuilder();

            string value = Environment.GetEnvironmentVariable(folder);
            if (value != null)
            {
                buffer.Append(value);
            }
                       
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

            // check installation folder.
            StringBuilder buffer = new StringBuilder();
            buffer.Append("%CommonProgramFiles%\\OPC Foundation\\UA\\v1.0\\Bin\\");
            buffer.Append(fileName);

            string path = Utils.GetAbsoluteFilePath(buffer.ToString(), false, false, false);

            if (path == null)
            {
                // check x86 installation folder.
                path = Utils.GetAbsoluteDirectoryPath("%CommonProgramFiles%", false, false, false);

                if (path != null)
                {
                    DirectoryInfo directory = new DirectoryInfo(path);

                    buffer = new StringBuilder();

                    buffer.Append(directory.Parent.FullName);
                    buffer.Append(" (x86)\\");
                    buffer.Append(directory.Name);
                    buffer.Append("\\OPC Foundation\\UA\\v1.0\\Bin\\");
                    buffer.Append(fileName);

                    path = Utils.GetAbsoluteFilePath(buffer.ToString(), false, false, false);
                }
            }

            if (path == null)
            {
                // check source tree.
                DirectoryInfo directory = new DirectoryInfo(ApplicationData.Current.LocalFolder.Path);

                while (directory != null)
                {
                    buffer = new StringBuilder();
                    buffer.Append(directory.FullName);
                    buffer.Append("\\Bin\\");
                    buffer.Append(fileName);

                    path = Utils.GetAbsoluteFilePath(buffer.ToString(), false, false, false);

                    if (path != null)
                    {
                        break;
                    }

                    directory = directory.Parent;
                }
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
        public static string GetAbsoluteFilePath(string filePath, bool checkCurrentDirectory, bool throwOnError, bool createAlways, bool writable=false)
        {
            filePath = Utils.ReplaceSpecialFolderNames(filePath);

            if (!String.IsNullOrEmpty(filePath))
            {            
                FileInfo file = new FileInfo(filePath);

                // check for absolute path.
                bool isAbsolute = filePath.StartsWith("\\\\", StringComparison.Ordinal) || filePath.IndexOf(':') == 1;
                
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
                        FileInfo localFile = new FileInfo(Utils.Format("{0}\\{1}", ApplicationData.Current.LocalFolder.Path, filePath));
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
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "File does not exist: {0}\r\nCurrent directory is: {1}",
                    filePath,
                    ApplicationData.Current.LocalFolder.Path);
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
                Utils.Trace(e, "Could not create file: {0}", filePath);

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
                bool isAbsolute = dirPath.StartsWith("\\\\", StringComparison.Ordinal) || dirPath.IndexOf(':') == 1;
                
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
                            directory = new DirectoryInfo(Utils.Format("{0}\\{1}", ApplicationData.Current.LocalFolder.Path, dirPath));
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
                    ApplicationData.Current.LocalFolder.Path);
            }

            return null;
        }

        /// <summary>
        /// Returns the value of the user setting.
        /// </summary>
        public static string GetUserSetting(string applicationName, string keyName)
        {
            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings.CreateContainer(applicationName, ApplicationDataCreateDisposition.Always);
            return settings.Values[keyName].ToString();
        }

        /// <summary>
        /// Sets the value of the user setting.
        /// </summary>
        public static void SetUserSetting(string applicationName, string keyName, string value)
        {
            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings.CreateContainer(applicationName, ApplicationDataCreateDisposition.Always);
            settings.Values[keyName] = value;
        }

        /// <summary>
        /// Returns the contents of the recent file list for the application.
        /// </summary>
        public static List<string> GetRecentFileList(string applicationName)
        {
            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings.CreateContainer(applicationName, ApplicationDataCreateDisposition.Always);
            return (List<string>) settings.Values["Recent File List"];
        }
        
        /// <summary>
        /// Updates the contents of the recent file list for the application.
        /// </summary>
        public static void UpdateRecentFileList(string applicationName, string filePath, int maxEntries)
        {
            if (String.IsNullOrEmpty(applicationName)) throw new ArgumentNullException("applicationName");

            // update existing list.
            List<string> files = GetRecentFileList(applicationName);

            if (maxEntries > 0)
            {
                if (String.IsNullOrEmpty(filePath))
                {
                    return;
                }

                for (int ii = 0; ii < files.Count; ii++)
                {
                    if (String.Compare(files[ii], filePath, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        files.RemoveAt(ii);
                        break;
                    }
                }

                files.Insert(0, filePath);
            }

            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings.CreateContainer(applicationName, ApplicationDataCreateDisposition.Always);
            settings.Values["Recent File List"] = files;
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
            int start = filePath.IndexOf('\\');

            if (start == -1)
            {
                return Utils.Format("{0}...", filePath.Substring(0, maxLength));
            }
            
            // keep file name.
            int end = filePath.LastIndexOf('\\');
            
            while (end > start && filePath.Length - end < maxLength)
            {
                end = filePath.LastIndexOf('\\', end-1);

                if (filePath.Length - end > maxLength)
                {
                    end = filePath.IndexOf('\\', end+1);
                    break;
                }
            }
            
            // format the result.
            return Utils.Format("{0}...{1}", filePath.Substring(0, start+1), filePath.Substring(end));
        }
        #endregion
        #region String, Object and Data Convienence Functions
        private const int MAX_MESSAGE_LENGTH = 1024;

		private const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;
		private const uint FORMAT_MESSAGE_FROM_SYSTEM    = 0x00001000;

        private static class NativeMethods
        {
            [DllImport("Kernel32.dll")]
            public static extern int FormatMessageW(
                int dwFlags,
                IntPtr lpSource,
                int dwMessageId,
                int dwLanguageId,
                IntPtr lpBuffer,
                int nSize,
                IntPtr Arguments);
        }
        
        /// <summary>
        /// Supresses any exceptions while disposing the object.
        /// </summary>
        /// <remarks>
        /// Writes errors to trace output in DEBUG builds.
        /// </remarks>
        public static void SilentDispose(object objectToDispose)
        {
            IDisposable disposable = objectToDispose as IDisposable;

            if (disposable != null)
            {
                try
                {
                    disposable.Dispose();
                }
                #if DEBUG
                catch (Exception e)
                {
                    Utils.Trace(e, "Error disposing object: {0}", disposable.GetType().Name);
                }
                #else
                catch (Exception)
                {
                }
                #endif
            }
        }
        
        /// <summary>
        /// The earliest time that can be represented on with UA date/time values.
        /// </summary>
        public static DateTime TimeBase
        {
            get { return s_TimeBase; }
        }
        
        private static readonly DateTime s_TimeBase = new DateTime(1601, 1, 1);
        
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

        public static IPAddress[] GetIPAddresses()
        {
            IPAddress[] addresses = null;

            if (NetworkInformation.GetHostNames().Count > 0)
            {
                int count = 0;
                addresses = new IPAddress[NetworkInformation.GetHostNames().Count];
                foreach (HostName localHostInfo in NetworkInformation.GetHostNames())
                {
                    if (localHostInfo.Type == HostNameType.Ipv4 ||
                        localHostInfo.Type == HostNameType.Ipv6)
                    {
                        addresses[count++] = IPAddress.Parse(localHostInfo.DisplayName);
                    }
                }
                if (count > 0)
                {
                    Array.Resize(ref addresses, count);
                    return addresses;
                }
            }

            return null;
        }

        public static bool HasIPAddresses(HostNameType hnt)
        {
            foreach (HostName localHostInfo in NetworkInformation.GetHostNames())
            {
                if (localHostInfo.Type == hnt)
                {
                    return true;
                }
            }
            return false;
        }


        public static async Task<IPAddress[]> GetHostAddresses(string remoteHostName)
        {
            IPAddress[] addresses = null;
            IReadOnlyList<EndpointPair> data = null;

            if (remoteHostName == GetHostName() && NetworkInformation.GetHostNames().Count > 0)
            {
                addresses = GetIPAddresses();
                if (addresses != null)
                {
                    return addresses;
                }
            }

            try
            {
                data = await DatagramSocket.GetEndpointPairsAsync(new HostName(remoteHostName), "0");
            }
            catch (Exception ex) // For debugging
            {
                Utils.Trace("GetEndpointPairsAsync({0}) failed. {1}", remoteHostName, ex);
            }

            if (data != null && data.Count > 0)
            {
                addresses = new IPAddress[data.Count];
                for (int ii = 0; ii < data.Count; ii++)
                {
                    if (data[ii] != null && data[ii].RemoteHostName != null)
                    {
                        addresses[ii] = IPAddress.Parse(data[ii].RemoteHostName.CanonicalName);
                    }
                }
            }

            return addresses;
        }


        static HostName hostName;
        public static string GetHostName()
        {
            if (hostName == null)
            {
                var hostNames = NetworkInformation.GetHostNames();
                hostName = hostNames.FirstOrDefault(name => name.Type == HostNameType.DomainName);
            }
            return hostName.CanonicalName.Split('.')[0];
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

            // check if the string localhost is specified.
            int index = uri.IndexOf("localhost", StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return uri;
            }

            // construct new uri.
            StringBuilder buffer = new StringBuilder();

            buffer.Append(uri.Substring(0, index));
            buffer.Append((hostname == null)?GetHostName():hostname);
            buffer.Append(uri.Substring(index + "localhost".Length));

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

            if (String.Compare(domain1, domain2, StringComparison.OrdinalIgnoreCase) == 0)
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
                
                builder.Scheme = Utils.UriSchemeHttp;
                builder.Host   = GetHostName();
                builder.Port   = -1;
                builder.Path   = Guid.NewGuid().ToString();
                
                return builder.Uri.ToString();;
            }

            // prefix non-urls with the hostname.
            if (!instanceUri.StartsWith(Utils.UriSchemeHttp, StringComparison.Ordinal))
            {                
                UriBuilder builder = new UriBuilder();
                
                builder.Scheme = Utils.UriSchemeHttp;
                builder.Host   = GetHostName();
                builder.Port   = -1;
                builder.Path   = Uri.EscapeDataString(instanceUri);
                
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
        
            for (int jj = array.Rank-1; jj >= 0; jj--)
            {
                dimensions[jj] = array.GetLength(array.Rank-jj-1);
            }

            for (int ii = 0; ii < array.Length; ii++)
            {
                indexes[array.Rank-1] = ii % dimensions[0];

                for (int jj = 1; jj < array.Rank; jj++)
                {
                    int multiplier = 1;

                    for (int kk = 0; kk < jj; kk++)
                    {
                        multiplier *= dimensions[kk];
                    }

                    indexes[array.Rank-jj-1] = (ii/multiplier) % dimensions[jj];
                }
                
                flatArray.SetValue(array.GetValue(indexes), ii);
            }
            
            return flatArray;
        }
        
        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
        public static string ToHexString(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(buffer.Length*2);
                
            for (int ii = 0; ii < buffer.Length; ii++)
            {
                builder.AppendFormat("{0:X2}", buffer[ii]);
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
                return new byte[0];
            }

            string text = buffer.ToUpperInvariant();
            const string digits = "0123456789ABCDEF";

            byte[] bytes = new byte[(buffer.Length / 2) + (buffer.Length % 2)];

            int ii = 0;

            while (ii < bytes.Length*2)
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

                    if (String.Compare(names[jj].Locale, localeIds[ii], StringComparison.OrdinalIgnoreCase) == 0)
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

                    if (String.Compare(languageId, actualLanguageId, StringComparison.OrdinalIgnoreCase) == 0)
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
            
            // copy arrays.
            Array array = value as Array;
            if (array != null)
            {
                Array clone = Array.CreateInstance(type.GetElementType(), array.Length);

                for (int ii = 0; ii < array.Length; ii++)
                {
                    clone.SetValue(Utils.Clone(array.GetValue(ii)), ii);
                }

                return clone;
            }

            // copy XmlNode.
            XmlNode node = value as XmlNode;
            if (node != null)
            {
                return node.CloneNode(true);
            }

            // copy ExtensionObject.
            {
                ExtensionObject castedObject = value as ExtensionObject;
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

            // don't know how to clone object.
            throw new NotSupportedException(Utils.Format("Don't know how to clone objects of type '{0}'", type.FullName));
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
                IEncodeable encodeable2 = value2 as IEncodeable;

                if (encodeable2 == null)
                {
                    return false;
                }

                return encodeable1.IsEqual(encodeable2);
            }
                        
            // check for XmlElement objects.
            XmlElement element1 = value1 as XmlElement;

            if (element1 != null)
            {
                XmlElement element2 = value2 as XmlElement;

                if (element2 == null)
                {
                    return false;
                }

                return element1.OuterXml == element2.OuterXml;
            }

            // check for arrays.
            Array array1 = value1 as Array;

            if (array1 != null)
            {
                Array array2 = value2 as Array;

                // arrays are greater than non-arrays.
                if (array2 == null)
                {
                    return false;
                }

                // shorter arrays are less than longer arrays.
                if (array1.Length != array2.Length) 
                {
                    return false;
                }

                // compare each element.
                for (int ii = 0; ii < array1.Length; ii++)
                {
                    bool result = Utils.IsEqual(array1.GetValue(ii), array2.GetValue(ii));

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
                IEnumerable enumerable2 = value2 as IEnumerable;

                // collections are greater than non-collections.
                if (enumerable2 == null)
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
			if (pattern == null || pattern.Length == 0)
			{
				return true;
			}

			// an empty string never matches.
			if (target == null || target.Length == 0)
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
                if (target.ToUpperInvariant() == pattern.ToUpperInvariant())
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
						if (pIndex >= pattern.Length && tIndex < target.Length-1)
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

									if (c >= l  &&  c <= p) 
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
						if (pIndex >= pattern.Length && tIndex < target.Length-1)
						{
							return false;
						}

						break;
					}
				} 
			}

            if (tIndex >= target.Length)
			{
				return (pIndex >= pattern.Length); // if end of pattern true
			}
	
			return true;
		} 
		
		// ConvertCase
		private static char ConvertCase(char c, bool caseSensitive)
		{
			return (caseSensitive)?c:Char.ToUpperInvariant(c);
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
        public static T ParseExtension<T>(IList<XElement> extensions, XmlQualifiedName elementName)
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
                if (extensions[ii].Name.LocalName != elementName.Name || extensions[ii].Name.NamespaceName != elementName.Namespace)
                {
                    continue;
                }

                // type found.
                XmlReader reader = XmlReader.Create(new StringReader(extensions[ii].ToString()));

                try
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                    return (T)serializer.ReadObject(reader);
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
        public static void UpdateExtension<T>(ref ExtensionCollection extensions, XmlQualifiedName elementName, object value)
        {
            XmlDocument document = new XmlDocument();

            // serialize value.
            StringBuilder buffer = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(buffer))
            {
                if (value != null)
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                    serializer.WriteObject(writer, value);

                    document.InnerXml = buffer.ToString();
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
                for (int ii =  0; ii < extensions.Count; ii++)
                {
                    if (extensions[ii] != null && extensions[ii].Name.LocalName == elementName.Name && extensions[ii].Name.NamespaceName == elementName.Namespace)
                    {
                        // remove the existing value if the value is null.
                        if (value == null)
                        {
                            extensions.RemoveAt(ii);
                            return;
                        }

                        extensions[ii] = new XElement(document.ToString());
                        return;
                    }
                }
            }
            
            // add new element.
            if (value != null)
            {
                if (extensions == null)
                {
                    extensions = new ExtensionCollection();
                }

                extensions.Add(new XElement(document.ToString()));
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
            return DateTime.Now;
        }

        /// <summary>
        /// Returns the major/minor version number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblySoftwareVersion()
        {
            PackageVersion version = Package.Current.Id.Version;
            return Utils.Format("{0}.{1}", version.Major, version.Minor);
        }

        /// <summary>
        /// Returns the build/revision number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblyBuildNumber()
        {
            PackageVersion version = Package.Current.Id.Version;
            return Utils.Format("{0}.{1}", version.Build, (version.Revision << 16) + version.Build);
        }
#endregion
        
#region Security Helper Functions
        /// <summary>
        /// Appends a list of byte arrays.
        /// </summary>
        public static byte[] Append(params byte[][] arrays)
        {
            if (arrays == null)
            {
                return new byte[0];
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
            try
            {
                return CertificateFactory.Create(certificateData, true);
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid, 
                    "Could not parse DER encoded form of an X509 certificate.",
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
            X509Certificate2 certificate = null;

            while (certificatesBytes.Count > 0)
	        {
                try
                {
                    certificate = CertificateFactory.Create(certificatesBytes.ToArray(), true);
                }
                catch(Exception e)
                {
                    throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Could not parse DER encoded form of an X509 certificate.",
                    e);
                }

                certificateChain.Add(certificate);
                certificatesBytes.RemoveRange(0, certificate.RawData.Length);
            }
            

            return certificateChain;
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA1 alhorithm.
        /// </summary>
        public static byte[] PSHA1(byte[] secret, string label, byte[] data, int offset, int length)
        {
            if (secret == null) throw new ArgumentNullException("secret");
            if (offset < 0)     throw new ArgumentOutOfRangeException("offset");
            if (length < 0)     throw new ArgumentOutOfRangeException("offset");

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
                    byte[] seed2 = new byte[seed.Length+data.Length];
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
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The PSHA1 algorithm requires a non-null seed.");
            }

            // create the hmac.
            HMACSHA1 hmac = new HMACSHA1(secret); 
           
            byte[] keySeed = hmac.ComputeHash(seed);
            byte[] prfSeed = new byte[hmac.HashSize/8 + seed.Length];
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
        /// Parses a distingushed name.
        /// </summary>
        public static List<string> ParseDistinguishedName(string name)
        {
            List<string> fields = new List<string>();

            if (String.IsNullOrEmpty(name))
            {
                return fields;
            }

            // determine the delimiter used.
            char delimiter = ',';
            bool found = false;
            bool quoted = false;

            for (int ii = name.Length - 1; ii >= 0; ii--)
            {
                char ch = name[ii];

                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && ch == '=')
                {
                    ii--;

                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;
                    while (ii >= 0 && (Char.IsLetterOrDigit(name[ii]) || name[ii] == '.')) ii--;
                    while (ii >= 0 && Char.IsWhiteSpace(name[ii])) ii--;

                    if (ii >= 0)
                    {
                        delimiter = name[ii];
                    }

                    break;
                }
            }
            
            StringBuilder buffer = new StringBuilder();

            string key = null;
            string value = null;
            found = false;

            for (int ii = 0; ii < name.Length; ii++)
            {
                while (ii < name.Length && Char.IsWhiteSpace(name[ii])) ii++;

                if (ii >= name.Length)
                {
                    break;
                }

                char ch = name[ii];

                if (found)
                {
                    char end = delimiter;

                    if (ii < name.Length && name[ii] == '"')
                    {
                        ii++;
                        end = '"';
                    }

                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == end)
                        {
                            while (ii < name.Length && name[ii] != delimiter) ii++;
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    value = buffer.ToString().TrimEnd();
                    found = false;

                    buffer.Length = 0;
                    buffer.Append(key);
                    buffer.Append('=');

                    if (value.IndexOfAny(new char[] { '/', ',', '=' }) != -1)
                    {
                        if (value.Length > 0 && value[0] != '"')
                        {
                            buffer.Append('"');
                        }

                        buffer.Append(value);

                        if (value.Length > 0 && value[value.Length-1] != '"')
                        {
                            buffer.Append('"');
                        }
                    }
                    else
                    {
                        buffer.Append(value);
                    }

                    fields.Add(buffer.ToString());
                    buffer.Length = 0;
                }

                else
                {
                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == '=')
                        {
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    key = buffer.ToString().TrimEnd().ToUpperInvariant();
                    buffer.Length = 0;
                    found = true;
                }
            }

            return fields;
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
                if (String.Compare(strings[ii], target, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts the the DNS names specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The DNS names.</returns>
        public static IList<string> GetDomainsFromCertficate(X509Certificate2 certificate)
        {
            List<string> dnsNames = new List<string>();

            // extracts the domain from the subject name.
            List<string> fields = Utils.ParseDistinguishedName(certificate.Subject);

            StringBuilder builder = new StringBuilder();

            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (fields[ii].StartsWith("DC="))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(fields[ii].Substring(3));
                }
            }

            if (builder.Length > 0)
            {
                dnsNames.Add(builder.ToString().ToUpperInvariant());
            }
            
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = null;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid || extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    alternateName = new X509SubjectAltNameExtension(extension, extension.Critical);
                    break;
                }
            }

            if (alternateName != null)
            {
                for (int ii = 0; ii < alternateName.DomainNames.Count; ii++)
                {
                    string hostname = alternateName.DomainNames[ii];

                    // do not add duplicates to the list.
                    bool found = false;

                    for (int jj = 0; jj < dnsNames.Count; jj++)
                    {
                        if (String.Compare(dnsNames[jj], hostname, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        dnsNames.Add(hostname.ToUpperInvariant());
                    }
                }

                for (int ii = 0; ii < alternateName.IPAddresses.Count; ii++)
                {
                    string ipAddress = alternateName.IPAddresses[ii];

                    if (!dnsNames.Contains(ipAddress))
                    {
                        dnsNames.Add(ipAddress);
                    }
                }
            }
            // return the list.
            return dnsNames;
        }

        /// <summary>
        /// Extracts the the application URI specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The application URI.</returns>
        public static string GetApplicationUriFromCertficate(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = null;

            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid || extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    alternateName = new X509SubjectAltNameExtension(extension, extension.Critical);
                    break;
                }
            }

            // get the application uri.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                return alternateName.Uris[0];
            }
            // return the list.
            return null;
        }

        /// <summary>
        /// Checks that the domain in the URL provided matches one of the domains in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="endpointUrl">The endpoint url to verify.</param>
        /// <returns>True if the certificate matches the url.</returns>
        public static bool DoesUrlMatchCertificate(X509Certificate2 certificate, Uri endpointUrl)
        {
            if (endpointUrl == null || certificate == null)
            {
                return false;
            }

            IList<string> domainNames = GetDomainsFromCertficate(certificate);

            for (int jj = 0; jj < domainNames.Count; jj++)
            {
                if (String.Compare(domainNames[jj], endpointUrl.DnsSafeHost, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(string name1, string name2)
        {
            // check for simple equality.
            if (String.Compare(name1, name2, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
            
            // parse the names.
            List<string> fields1 = ParseDistinguishedName(name1);
            List<string> fields2 = ParseDistinguishedName(name2);

            // can't be equal if the number of fields is different.
            if (fields1.Count != fields2.Count)
            {
                return false;
            }

            // compare each.
            for (int ii = 0; ii < fields1.Count; ii++)
            {
                if (String.Compare(fields1[ii], fields2[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(X509Certificate2 certificate, List<string> parsedName)
        {
            // parse the names.
            List<string> certificateName = ParseDistinguishedName(certificate.Subject);

            // can't be equal if the number of fields is different.
            if (parsedName.Count != certificateName.Count)
            {
                return false;
            }

            // compare each.
            for (int ii = 0; ii < parsedName.Count; ii++)
            {
                if (String.Compare(parsedName[ii], certificateName[ii], StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }
            }

            return true;
        }
#endregion
	}
}
