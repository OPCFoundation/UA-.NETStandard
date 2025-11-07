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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
#if !NETFRAMEWORK
using Opc.Ua.Security.Certificates;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static partial class Utils
    {
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
        public static readonly string[] DefaultUriSchemes =
        [
            UriSchemeOpcTcp,
            UriSchemeOpcHttps,
            UriSchemeHttps,
            UriSchemeOpcWss
        ];

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
        public static readonly string[] DiscoveryUrls =
        [
            "opc.tcp://{0}:4840",
            "https://{0}:4843",
            "http://{0}:52601/UADiscovery",
            "http://{0}/UADiscovery/Default.svc"
        ];

        /// <summary>
        /// The default certificate store's type.
        /// </summary>
        public const string DefaultStoreType = CertificateStoreType.Directory;

        /// <summary>
        /// The path to the default certificate store.
        /// </summary>
#if NETFRAMEWORK
        public static readonly string DefaultStorePath = Path.Combine(
            "%CommonApplicationData%",
            "OPC Foundation",
            "pki",
            "own");
#else
        public static readonly string DefaultStorePath = Path.Combine(
            "%LocalApplicationData%",
            "OPC Foundation",
            "pki",
            "own");
#endif

        /// <summary>
        /// The default LocalFolder.
        /// </summary>
        public static string DefaultLocalFolder { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>
        /// The full name of the Opc.Ua.Core assembly.
        /// </summary>
        public static readonly string DefaultOpcUaCoreAssemblyFullName = typeof(Utils).Assembly
            .GetName()
            .FullName;

        /// <summary>
        /// The name of the Opc.Ua.Core assembly.
        /// </summary>
        public static readonly string DefaultOpcUaCoreAssemblyName = typeof(Utils).Assembly
            .GetName()
            .Name;

        /// <summary>
        /// Helper to get the name of the Opc.Ua.Bindings.Https assembly.
        /// </summary>
        private static string OpcUaHttpsAssemblyName()
        {
            string assemblyName = DefaultOpcUaCoreAssemblyName;
            int offset = assemblyName.IndexOf("Core", StringComparison.Ordinal);
            Debug.Assert(offset != -1);
            return $"{assemblyName[0..offset]}Bindings.Https";
        }

        /// <summary>
        /// List of known default bindings hosted in other assemblies.
        /// </summary>
        public static readonly ReadOnlyDictionary<string, string> DefaultBindings = new(
            new Dictionary<string, string>
            {
                { UriSchemeHttps, OpcUaHttpsAssemblyName() },
                { UriSchemeOpcHttps, OpcUaHttpsAssemblyName() }
            });

        /// <summary>
        /// Returns <c>true</c> if the url starts with opc.https or https.
        /// </summary>
        /// <param name="url">The url</param>
        public static bool IsUriHttpsScheme(string url)
        {
            return url.StartsWith(UriSchemeHttps, StringComparison.Ordinal) ||
                url.StartsWith(UriSchemeOpcHttps, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if the url starts with http, opc.https or https.
        /// </summary>
        /// <param name="url">The url</param>
        public static bool IsUriHttpRelatedScheme(string url)
        {
            return url.StartsWith(UriSchemeHttps, StringComparison.Ordinal) ||
                IsUriHttpsScheme(url);
        }

        /// <summary>
        /// Replaces a prefix enclosed in '%' with a special folder or environment variable path (e.g. %ProgramFiles%\MyCompany).
        /// </summary>
        public static bool IsPathRooted(string path)
        {
            // allow for local file locations
            return Path.IsPathRooted(path) ||
                (path.Length >= 2 && path[0] == '.' && path[1] != '.');
        }

        /// <summary>
        /// Maps a special folder to environment variable with folder path.
        /// </summary>
        private static string ReplaceSpecialFolderWithEnvVar(string input)
        {
            switch (input)
            {
                case "CommonApplicationData":
                    return "ProgramData";
                default:
                    return input;
            }
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
            if (IsPathRooted(input))
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
                folder = input[1..];
                path = string.Empty;
            }
            else
            {
                folder = input[1..index];
                path = input[(index + 1)..];
            }

            var buffer = new StringBuilder();
#if !NETSTANDARD1_4 && !NETSTANDARD1_3
            // check for special folder.
            if (!Enum.TryParse(folder, out Environment.SpecialFolder specialFolder))
            {
#endif
                folder = ReplaceSpecialFolderWithEnvVar(folder);
                string value = Environment.GetEnvironmentVariable(folder);
                if (value != null)
                {
                    buffer.Append(value);
                }
                else if (folder == "LocalFolder")
                {
                    buffer.Append(DefaultLocalFolder);
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
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteFilePath(string filePath)
        {
            return GetAbsoluteFilePath(
                filePath,
                checkCurrentDirectory: false,
                createAlways: false);
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static string GetAbsoluteFilePath(
            string filePath,
            bool checkCurrentDirectory,
            bool createAlways,
            bool writable = false)
        {
            filePath = ReplaceSpecialFolderNames(filePath);

            if (!string.IsNullOrEmpty(filePath))
            {
                var file = new FileInfo(filePath);

                // check for absolute path.
                bool isAbsolute = IsPathRooted(filePath);

                if (isAbsolute)
                {
                    if (file.Exists)
                    {
                        return filePath;
                    }

                    if (createAlways)
                    {
                        return CreateFile(file, filePath);
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
                            localFile = new FileInfo(
                                Format(
                                    "{0}{1}{2}",
                                    Directory.GetCurrentDirectory(),
                                    Path.DirectorySeparatorChar,
                                    filePath));
#if NETFRAMEWORK
                            if (!localFile.Exists)
                            {
                                var localFile2 = new FileInfo(
                                    Format(
                                        "{0}{1}{2}",
                                        Path.GetDirectoryName(
                                            Assembly.GetExecutingAssembly().Location),
                                        Path.DirectorySeparatorChar,
                                        filePath));
                                if (localFile2.Exists)
                                {
                                    localFile = localFile2;
                                }
                            }
#endif
                        }
                        else
                        {
                            localFile = new FileInfo(
                                Format(
                                    "{0}{1}{2}",
                                    Path.GetTempPath(),
                                    Path.DirectorySeparatorChar,
                                    filePath));
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
                            return CreateFile(localFile, localFile.FullName);
                        }
                    }
                }
            }

            // file does not exist.
            var message = new StringBuilder();
            message.AppendLine("File does not exist: {0}")
                .AppendLine("Current directory is: {1}");
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                message.ToString(),
                filePath,
                Directory.GetCurrentDirectory());
        }

        /// <summary>
        /// Creates an empty file.
        /// </summary>
        private static string CreateFile(FileInfo file, string filePath)
        {
            // create the directory as required.
            if (!file.Directory.Exists)
            {
                Directory.CreateDirectory(file.DirectoryName);
            }

            // open and close the file.
            using Stream ostrm = file.Open(FileMode.CreateNew, FileAccess.ReadWrite);
            return filePath;
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        public static string GetAbsoluteDirectoryPath(
            string dirPath,
            bool checkCurrentDirectory,
            bool throwOnError)
        {
            return GetAbsoluteDirectoryPath(dirPath, checkCurrentDirectory, throwOnError, false);
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static string GetAbsoluteDirectoryPath(
            string dirPath,
            bool checkCurrentDirectory,
            bool throwOnError,
            bool createAlways)
        {
            string originalPath = dirPath;
            dirPath = ReplaceSpecialFolderNames(dirPath);

            if (!string.IsNullOrEmpty(dirPath))
            {
                var directory = new DirectoryInfo(dirPath);

                // check for absolute path.
                bool isAbsolute = IsPathRooted(dirPath);

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
                            directory = new DirectoryInfo(
                                Format(
                                    "{0}{1}{2}",
                                    Directory.GetCurrentDirectory(),
                                    Path.DirectorySeparatorChar,
                                    dirPath));
#if NETFRAMEWORK
                            if (!directory.Exists)
                            {
                                var directory2 = new DirectoryInfo(
                                    Format(
                                        "{0}{1}{2}",
                                        Path.GetDirectoryName(
                                            Assembly.GetExecutingAssembly().Location),
                                        Path.DirectorySeparatorChar,
                                        dirPath));
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
            int start = filePath.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);

            if (start == -1)
            {
                return Format("{0}...", filePath[..maxLength]);
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
            return Format("{0}...{1}", filePath[..(start + 1)], filePath[end..]);
        }

        /// <summary>
        /// Suppresses any exceptions while disposing the object.
        /// </summary>
        /// <remarks>
        /// Writes errors to trace output in DEBUG builds.
        /// </remarks>
        public static void SilentDispose(object objectToDispose)
        {
            var disposable = objectToDispose as IDisposable;
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
            CoreUtils.SilentDispose(disposable);
        }

        /// <summary>
        /// The earliest time that can be represented on with UA date/time values.
        /// </summary>
        public static DateTime TimeBase => CoreUtils.TimeBase;

        /// <summary>
        /// Normalize a DateTime to Opc Ua UniversalTime.
        /// </summary>
        public static DateTime ToOpcUaUniversalTime(DateTime value)
        {
            return CoreUtils.ToOpcUaUniversalTime(value);
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
        public static Task<IPAddress[]> GetHostAddressesAsync(
            string hostNameOrAddress,
            CancellationToken ct = default)
        {
#if NET8_0_OR_GREATER
            return Dns.GetHostAddressesAsync(hostNameOrAddress, ct);
#else
            ct.ThrowIfCancellationRequested();
            return Dns.GetHostAddressesAsync(hostNameOrAddress);
#endif
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
                var normalizedAddress = IPAddress.Parse(ipAddress);
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
            if (!string.IsNullOrEmpty(hostname) && hostname.Contains(':', StringComparison.Ordinal))
            {
                hostname = "[" + hostname + "]";
            }

            // check if the string localhost is specified.
            const string localhost = "localhost";
            int index = uri.IndexOf(localhost, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return uri;
            }

            // construct new uri.
            var buffer = new StringBuilder();
#if NET5_0_OR_GREATER || NETSTANDARD2_1
            buffer
                .Append(uri.AsSpan(0, index))
                .Append(hostname ?? GetHostName())
                .Append(uri.AsSpan(index + localhost.Length));
#else
            buffer.Append(uri[..index]).Append(hostname ?? GetHostName())
                .Append(uri[(index + localhost.Length)..]);
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
            if (!string.IsNullOrEmpty(hostname) && hostname.Contains(':', StringComparison.Ordinal))
            {
                hostname = "[" + hostname + "]";
            }

            // check if the string DC=localhost is specified.
            const string dclocalhost = "DC=localhost";
            int index = subjectName.IndexOf(dclocalhost, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return subjectName;
            }

            // construct new uri.
            var buffer = new StringBuilder();
#if NET5_0_OR_GREATER || NETSTANDARD2_1
            buffer
                .Append(subjectName.AsSpan(0, index + 3))
                .Append(hostname ?? GetHostName())
                .Append(subjectName.AsSpan(index + dclocalhost.Length));
#else
            buffer
                .Append(subjectName[..(index + 3)])
                .Append(hostname ?? GetHostName())
                .Append(subjectName[(index + dclocalhost.Length)..]);
#endif
            return buffer.ToString();
        }

        /// <summary>
        /// Escapes a URI string using the percent encoding.
        /// </summary>
        public static string EscapeUri(string uri)
        {
            return CoreUtils.EscapeUri(uri);
        }

        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(ReadOnlySpan<char> uri)
        {
            return CoreUtils.UnescapeUri(uri);
        }

        /// <summary>
        /// Corresponds to <see cref="string.IsNullOrEmpty(string?)"/> for byte[].
        /// </summary>
        public static bool Utf8IsNullOrEmpty(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return true;
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != ' ')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Unescapes a URI string using the percent encoding.
        /// </summary>
        public static string UnescapeUri(string uri)
        {
            return CoreUtils.UnescapeUri(uri);
        }

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
                string domain1 = url1.IdnHost;
                string domain2 = url2.IdnHost;

                // replace localhost with the computer name.
                if (domain1 == "localhost")
                {
                    domain1 = GetHostName();
                }

                if (domain2 == "localhost")
                {
                    domain2 = GetHostName();
                }

                return AreDomainsEqual(domain1, domain2);
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

            return string.Equals(domain1, domain2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Substitutes the local machine name if "localhost" is specified in the instance uri.
        /// </summary>
        public static string UpdateInstanceUri(string instanceUri)
        {
            // check for null.
            if (string.IsNullOrEmpty(instanceUri))
            {
                var builder = new UriBuilder
                {
                    Scheme = UriSchemeHttps,
                    Host = GetHostName(),
                    Port = -1,
                    Path = Guid.NewGuid().ToString()
                };

                return builder.Uri.ToString();
            }

            // prefix non-urls with the hostname.
            if (!instanceUri.StartsWith(UriSchemeHttps, StringComparison.Ordinal))
            {
                var builder = new UriBuilder
                {
                    Scheme = UriSchemeHttps,
                    Host = GetHostName(),
                    Port = -1,
                    Path = Uri.EscapeDataString(instanceUri)
                };

                return builder.Uri.ToString();
            }

            // replace localhost with the current hostname.
            Uri parsedUri = ParseUri(instanceUri);

            if (parsedUri != null && parsedUri.IdnHost == "localhost")
            {
                var builder = new UriBuilder(parsedUri) { Host = GetHostName() };
                return builder.Uri.ToString();
            }

            // return the original instance uri.
            return instanceUri;
        }

        /// <summary>
        /// Sets the identifier to a lower limit if smaller. Thread safe.
        /// </summary>
        /// <returns>Returns the new value.</returns>
        public static uint SetIdentifierToAtLeast(ref uint identifier, uint lowerLimit)
        {
            uint value;
            uint exchangedValue;
            do
            {
                value = identifier;
                exchangedValue = value;
                if (value < lowerLimit)
                {
                    ref int id = ref Unsafe.As<uint, int>(ref identifier);
                    exchangedValue = (uint)Interlocked.CompareExchange(ref id, (int)lowerLimit, (int)value);
                }
            } while (exchangedValue != value);
            return value;
        }

        /// <summary>
        /// Sets the identifier to a new value. Thread safe.
        /// </summary>
        /// <returns>Returns the new value.</returns>
        public static uint SetIdentifier(ref uint identifier, uint newIdentifier)
        {
            Debug.Assert(newIdentifier != 0);
#if NET8_0_OR_GREATER
            return Interlocked.Exchange(ref identifier, newIdentifier);
#else
            ref int id = ref Unsafe.As<uint, int>(ref identifier);
            return (uint)Interlocked.Exchange(ref id, (int)newIdentifier);
#endif
        }

        /// <summary>
        /// Increments a identifier (prohibits 0).
        /// </summary>
        public static uint IncrementIdentifier(ref uint identifier)
        {
            uint result;
            do
            {
#if NET8_0_OR_GREATER
                result = Interlocked.Increment(ref identifier);
#else
                ref int id = ref Unsafe.As<uint, int>(ref identifier);
                result = (uint)Interlocked.Increment(ref id);
#endif
            }
            while (result == 0);
            return result;
        }

        /// <summary>
        /// Increments a identifier (prohibits 0).
        /// </summary>
        public static int IncrementIdentifier(ref int identifier)
        {
            int result;
            do
            {
                result = Interlocked.Increment(ref identifier);
            }
            while (result == 0);
            return result;
        }

        /// <summary>
        /// Safely converts an UInt32 identifier to a Int32 identifier.
        /// </summary>
        public static int ToInt32(uint identifier)
        {
            if (identifier <= int.MaxValue)
            {
                return (int)identifier;
            }

            return -(int)(uint.MaxValue - (long)identifier + 1);
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

            return (uint)((long)uint.MaxValue + 1 + identifier);
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
            return CoreUtils.FlattenArray(array);
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
        {
            return CoreUtils.ToHexString(buffer, invertEndian);
        }

        /// <summary>
        /// Converts a buffer to a hexadecimal string.
        /// </summary>
        public static string ToHexString(ReadOnlySpan<byte> buffer, bool invertEndian = false)
#else
        public static string ToHexString(byte[] buffer, bool invertEndian = false)
#endif
        {
            return CoreUtils.ToHexString(buffer, invertEndian);
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
                return [];
            }

            string text = buffer.ToUpperInvariant();
            const string digits = "0123456789ABCDEF";

            byte[] bytes = new byte[(buffer.Length / 2) + (buffer.Length % 2)];

            int ii = 0;

            while (ii < bytes.Length * 2)
            {
                int index = digits.IndexOf(buffer[ii], StringComparison.Ordinal);

                if (index == -1)
                {
                    break;
                }

                byte b = (byte)index;
                b <<= 4;

                if (ii < buffer.Length - 1)
                {
                    index = digits.IndexOf(buffer[ii + 1], StringComparison.Ordinal);

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
            return CoreUtils.Format(text, args);
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
                var culture = new CultureInfo(localeId);

                if (culture != null)
                {
                    return true;
                }
            }
            catch
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

            int index = localeId.IndexOf('-', StringComparison.Ordinal);

            if (index != -1)
            {
                return localeId[..index];
            }

            return localeId;
        }

        /// <summary>
        /// Returns the localized text from a list of available text
        /// </summary>
        public static LocalizedText SelectLocalizedText(
            IList<string> localeIds,
            IList<LocalizedText> names,
            LocalizedText defaultName)
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

                    if (string.Equals(
                        names[jj].Locale,
                        localeIds[ii],
                        StringComparison.OrdinalIgnoreCase))
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

                    if (string.Equals(
                        languageId,
                        actualLanguageId,
                        StringComparison.OrdinalIgnoreCase))
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
        /// <typeparam name="T"></typeparam>
        public static T Clone<T>(T value)
            where T : class
        {
            return CoreUtils.Clone<T>(value);
        }

        /// <summary>
        /// Returns a deep copy of the value.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public static object Clone(object value)
        {
            return CoreUtils.Clone(value);
        }

        /// <summary>
        /// Checks if two identities are equal.
        /// </summary>
        public static bool IsEqualUserIdentity(
            UserIdentityToken identity1,
            UserIdentityToken identity2)
        {
            // check for reference equality.
            if (ReferenceEquals(identity1, identity2))
            {
                return true;
            }

            if (identity1 == null || identity2 == null)
            {
                return false;
            }

            if (identity1 is AnonymousIdentityToken && identity2 is AnonymousIdentityToken)
            {
                return true;
            }

            if (identity1 is UserNameIdentityToken userName1 &&
                identity2 is UserNameIdentityToken userName2)
            {
                return string.Equals(
                    userName1.UserName,
                    userName2.UserName,
                    StringComparison.Ordinal);
            }

            if (identity1 is X509IdentityToken x509Token1 &&
                identity2 is X509IdentityToken x509Token2)
            {
                return IsEqual(x509Token1.CertificateData, x509Token2.CertificateData);
            }

            if (identity1 is IssuedIdentityToken issuedToken1 &&
                identity2 is IssuedIdentityToken issuedToken2)
            {
                return IsEqual(issuedToken1.DecryptedTokenData, issuedToken2.DecryptedTokenData);
            }

            return false;
        }

        /// <summary>
        /// Checks if two DateTime values are equal.
        /// </summary>
        public static bool IsEqual(DateTime time1, DateTime time2)
        {
            return CoreUtils.IsEqual(time1, time2);
        }

        /// <summary>
        /// Checks if two T values are equal based on IEquatable compare.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(T value1, T value2)
            where T : IEquatable<T>
        {
            return CoreUtils.IsEqual(value1, value2);
        }

        /// <summary>
        /// Checks if two IEnumerable T values are equal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(IEnumerable<T> value1, IEnumerable<T> value2)
            where T : IEquatable<T>
        {
            return CoreUtils.IsEqual<T>(value1, value2);
        }

        /// <summary>
        /// Checks if two T[] values are equal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(ReadOnlySpan<T> value1, ReadOnlySpan<T> value2) where T : unmanaged, IEquatable<T>
        {
            if (value1.IsEmpty && value2.IsEmpty)
            {
                return true;
            }

            if (value1.Length != value2.Length)
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }

        /// <summary>
        /// Checks if two T[] values are equal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static bool IsEqual<T>(T[] value1, T[] value2)
            where T : unmanaged, IEquatable<T>
        {
            return CoreUtils.IsEqual(value1, value2);
        }

#if NETFRAMEWORK
        /// <summary>
        /// Fast memcpy version of byte[] compare.
        /// </summary>
        public static bool IsEqual(byte[] value1, byte[] value2)
        {
            return CoreUtils.IsEqual(value1, value2);
        }
#endif

        /// <summary>
        /// Checks if two values are equal.
        /// </summary>
        public static bool IsEqual(object value1, object value2)
        {
            return CoreUtils.IsEqual(value1, value2);
        }

        /// <summary>
        /// Returns the TimeZone information for the current local time.
        /// </summary>
        /// <returns>The TimeZone information for the current local time.</returns>
        public static TimeZoneDataType GetTimeZoneInfo()
        {
            return new TimeZoneDataType
            {
                Offset = (short)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
                DaylightSavingInOffset = true
            };
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to parse it.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        /// <param name="extensions">The list of extensions to search.</param>
        /// <param name="elementName">Name of the element (use type name if null).</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>
        /// The deserialized extension. Null if an error occurs.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="elementName"/></exception>
        public static T ParseExtension<T>(
            IList<XmlElement> extensions,
            XmlQualifiedName elementName,
            ITelemetryContext telemetry)
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
                XmlQualifiedName qname = TypeInfo.GetXmlName(typeof(T));

                elementName =
                    qname ??
                    throw new ArgumentException(
                        "Type does not seem to support DataContract serialization");
            }

            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);

            // find the element.
            for (int ii = 0; ii < extensions.Count; ii++)
            {
                XmlElement element = extensions[ii];

                if (element.LocalName != elementName.Name ||
                    element.NamespaceURI != elementName.Namespace)
                {
                    continue;
                }

                // type found.
                var reader = XmlReader.Create(
                    new StringReader(element.OuterXml),
                    DefaultXmlReaderSettings());

                try
                {
                    var serializer = new DataContractSerializer(typeof(T));
                    return (T)serializer.ReadObject(reader);
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
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <remarks>
        /// Adds a new extension if the it does not already exist.
        /// Deletes the extension if the value is null.
        /// The containing element must use the name and namespace uri specified by the DataContractAttribute for the type.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="elementName"/></exception>
        public static void UpdateExtension<T>(
            ref XmlElementCollection extensions,
            XmlQualifiedName elementName,
            object value,
            ITelemetryContext telemetry)
        {
            var document = new XmlDocument();

            // serialize value.
            var buffer = new StringBuilder();
            using (var writer = XmlWriter.Create(buffer, DefaultXmlWriterSettings()))
            {
                if (value != null)
                {
                    try
                    {
                        var serializer = new DataContractSerializer(typeof(T));
                        using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
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
                XmlQualifiedName qname = TypeInfo.GetXmlName(typeof(T));

                elementName =
                    qname ??
                    throw new ArgumentException(
                        "Type does not seem to support DataContract serialization");
            }

            // replace existing element.
            if (extensions != null)
            {
                for (int ii = 0; ii < extensions.Count; ii++)
                {
                    if (extensions[ii] != null &&
                        extensions[ii].LocalName == elementName.Name &&
                        extensions[ii].NamespaceURI == elementName.Namespace)
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
                (extensions ??= []).Add(document.DocumentElement);
            }
        }

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
            object[] attributes = [.. property.GetCustomAttributes(
                typeof(DataMemberAttribute),
                true)];

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
            foreach (FieldInfo field in constants.GetFields(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (field.Name == name)
                {
                    return Convert.ToUInt32(
                        field.GetValue(constants),
                        CultureInfo.InvariantCulture);
                }
            }

            return 0;
        }

        private static readonly DateTime s_baseDateTime = new(
            2000,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);

        /// <summary>
        /// Return the current time in milliseconds since 1/1/2000.
        /// </summary>
        /// <returns>The current time in milliseconds since 1/1/2000.</returns>
        public static uint GetVersionTime()
        {
            double ticks = (DateTime.UtcNow - s_baseDateTime).TotalMilliseconds;
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
            {
            }
            return new DateTime(1970, 1, 1, 0, 0, 0);
        }

        /// <summary>
        /// Returns the major/minor version number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblySoftwareVersion()
        {
            return typeof(Utils)
                .GetTypeInfo()
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
        }

        /// <summary>
        /// Returns the build/revision number for an assembly formatted as a string.
        /// </summary>
        public static string GetAssemblyBuildNumber()
        {
            return typeof(Utils).GetTypeInfo().Assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                .Version;
        }

        /// <summary>
        /// Returns a XmlReaderSetting with safe defaults.
        /// DtdProcessing Prohibited, XmlResolver disabled and
        /// ConformanceLevel Document.
        /// </summary>
        public static XmlReaderSettings DefaultXmlReaderSettings()
        {
            return CoreUtils.DefaultXmlReaderSettings();
        }

        /// <summary>
        /// Returns a XmlWriterSetting with deterministic defaults across .NET versions.
        /// </summary>
        public static XmlWriterSettings DefaultXmlWriterSettings()
        {
            return CoreUtils.DefaultXmlWriterSettings();
        }

        /// <summary>
        /// Safe version for assignment of InnerXml.
        /// </summary>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="xml">The Xml document string.</param>
        internal static void LoadInnerXml(XmlDocument doc, string xml)
        {
            doc.LoadInnerXml(xml);
        }

        /// <summary>
        /// Appends a list of byte arrays.
        /// </summary>
        public static byte[] Append(params byte[][] arrays)
        {
            if (arrays == null)
            {
                return [];
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
        /// <exception cref="ServiceResultException"></exception>
        public static X509Certificate2 ParseCertificateBlob(
            ReadOnlyMemory<byte> certificateData,
            ITelemetryContext telemetry,
            bool useAsnParser = false)
        {
            try
            {
#if !NETFRAMEWORK
                // macOS X509Certificate2 constructor throws exception if a certchain is encoded
                // use AsnParser on macOS to parse for byteblobs,
                if (useAsnParser || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    certificateData = AsnUtils.ParseX509Blob(certificateData);
                }
#endif
                return CertificateFactory.Create(certificateData);
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
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="useAsnParser">Whether the ASN.1 library should be used to decode certificate blobs.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static X509Certificate2Collection ParseCertificateChainBlob(
            ReadOnlyMemory<byte> certificateData,
            ITelemetryContext telemetry,
            bool useAsnParser = false)
        {
            var certificateChain = new X509Certificate2Collection();
            int offset = 0;
            int length = certificateData.Length;
            while (offset < length)
            {
                X509Certificate2 certificate;
                try
                {
                    ReadOnlyMemory<byte> certBlob = certificateData[offset..];
#if !NETFRAMEWORK
                    // macOS X509Certificate2 constructor throws exception if a certchain is encoded
                    // use AsnParser on macOS to parse for byteblobs,
                    if (useAsnParser || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        certBlob = AsnUtils.ParseX509Blob(certBlob);
                    }
#endif
                    certificate = CertificateFactory.Create(certBlob);
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
                return [];
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
        /// Generates a Pseudo random sequence of bits using the P_SHA1 alhorithm.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="secret"/> is <c>null</c>.</exception>
        public static byte[] PSHA1(byte[] secret, string label, byte[] data, int offset, int length)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }
            // create the hmac.
            using var hmac = new HMACSHA1(secret);
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA256 alhorithm.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="secret"/> is <c>null</c>.</exception>
        public static byte[] PSHA256(
            byte[] secret,
            string label,
            byte[] data,
            int offset,
            int length)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }
            // create the hmac.
            using var hmac = new HMACSHA256(secret);
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA1 alhorithm.
        /// </summary>
        public static byte[] PSHA1(HMACSHA1 hmac, string label, byte[] data, int offset, int length)
        {
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the P_SHA256 alhorithm.
        /// </summary>
        public static byte[] PSHA256(
            HMACSHA256 hmac,
            string label,
            byte[] data,
            int offset,
            int length)
        {
            return PSHA(hmac, label, data, offset, length);
        }

        /// <summary>
        /// Generates a Pseudo random sequence of bits using the HMAC algorithm.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="hmac"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static byte[] PSHA(HMAC hmac, string label, byte[] data, int offset, int length)
        {
            if (hmac == null)
            {
                throw new ArgumentNullException(nameof(hmac));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

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
                throw ServiceResultException.Unexpected(
                    "The HMAC algorithm requires a non-null seed.");
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
            } while (position < length);

            // return random data.
            return output;
        }

        /// <summary>
        /// Creates an HMAC.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
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
            if (certificateType.Identifier is not uint identifier)
            {
                return false;
            }
            switch (identifier)
            {
#if ECC_SUPPORT
                case ObjectTypes.EccApplicationCertificateType:
                    return true;
                case ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType:
                    return s_eccCurveSupportCache[
                        ECCurve.NamedCurves.brainpoolP256r1.Oid.FriendlyName].Value;
                case ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType:
                    return s_eccCurveSupportCache[
                        ECCurve.NamedCurves.brainpoolP384r1.Oid.FriendlyName].Value;
                case ObjectTypes.EccNistP256ApplicationCertificateType:
                    return s_eccCurveSupportCache[ECCurve.NamedCurves.nistP256.Oid.FriendlyName]
                        .Value;
                case ObjectTypes.EccNistP384ApplicationCertificateType:
                    return s_eccCurveSupportCache[ECCurve.NamedCurves.nistP384.Oid.FriendlyName]
                        .Value;
                // case ObjectTypes.EccCurve25519ApplicationCertificateType:
                // case ObjectTypes.EccCurve448ApplicationCertificateType:
#endif
                case ObjectTypes.ApplicationCertificateType:
                case ObjectTypes.RsaMinApplicationCertificateType:
                case ObjectTypes.RsaSha256ApplicationCertificateType:
                case ObjectTypes.HttpsCertificateType:
                case ObjectTypes.UserCertificateType:
                    return true;
                default:
                    return false;
            }
        }

#if ECC_SUPPORT
        /// <summary>
        /// Check if known curve is supported by platform
        /// </summary>
        private static bool IsCurveSupported(ECCurve eCCurve)
        {
            try
            {
                // Create a ECDsa object and generate a new keypair on the given curve
                using var eCDsa = ECDsa.Create(eCCurve);
                ECParameters parameters = eCDsa.ExportParameters(false);
                return parameters.Q.X != null && parameters.Q.Y != null;
            }
            catch (Exception ex)
                when (ex is PlatformNotSupportedException or ArgumentException or CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// Lazy helper for checking ECC eliptic curve support for running OS
        /// </summary>
        private static readonly Dictionary<string, Lazy<bool>> s_eccCurveSupportCache = new()
        {
            {
                ECCurve.NamedCurves.nistP256.Oid.FriendlyName,
                new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.nistP256))
            },
            {
                ECCurve.NamedCurves.nistP384.Oid.FriendlyName,
                new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.nistP384))
            },
            {
                ECCurve.NamedCurves.brainpoolP256r1.Oid.FriendlyName,
                new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.brainpoolP256r1))
            },
            {
                ECCurve.NamedCurves.brainpoolP384r1.Oid.FriendlyName,
                new Lazy<bool>(() => IsCurveSupported(ECCurve.NamedCurves.brainpoolP384r1))
            }
        };
#endif

        /// <summary>
        /// Lazy helper to allow runtime check for Mono.
        /// </summary>
        private static readonly Lazy<bool> s_isRunningOnMonoValue = new(
            () => Type.GetType("Mono.Runtime") != null);

        /// <summary>
        /// Determine if assembly uses mono runtime.
        /// </summary>
        /// <returns>true if running on Mono runtime</returns>
        public static bool IsRunningOnMono()
        {
            return s_isRunningOnMonoValue.Value;
        }
    }
}
