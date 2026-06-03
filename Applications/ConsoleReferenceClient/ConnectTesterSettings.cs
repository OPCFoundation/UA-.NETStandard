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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quickstarts
{
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(ConnectTesterSettings))]
    internal sealed partial class ConnectTesterSettingsJsonContext : JsonSerializerContext
    {
    }


    /// <summary>
    /// Externalised settings for <see cref="ConnectTester"/>.
    /// </summary>
    /// <remarks>
    /// Replaces the previously hard-coded constants in ConnectTester.cs. By default the
    /// loader looks for "ConnectTester.Settings.json" next to the executable; the path
    /// may be overridden with the REFCLIENT_CONNECTTESTER_SETTINGS_FILE environment
    /// variable. A missing or unparseable file falls back to the built-in defaults.
    /// </remarks>
    internal sealed class ConnectTesterSettings
    {
        public string ServerUrl { get; set; }
            = "opc.tcp://whitecat:62541/Quickstarts/ReferenceServer";

        public string UserName { get; set; } = "sysadmin";

        public string Password { get; set; } = "demo";

        public string SecurityPolicyFilter { get; set; } = "";

        public bool SupportsX509 { get; set; } = true;

        public bool EnableCryptoLogging { get; set; } = true;

        public int ReconnectPeriod { get; set; } = 1000;

        public int ReconnectPeriodExponentialBackoff { get; set; } = 15000;

        /// <summary>
        /// Directory that contains the user PKCS#12 certificates (relative paths are
        /// resolved against the current working directory).
        /// </summary>
        public string UserCertificatePath { get; set; }
            = Path.Combine("..", "..", "pki", "trustedUser", "private");

        public string UserCertificatePassword { get; set; } = "password";

        public static ConnectTesterSettings Load()
        {
            string? overridePath
                = Environment.GetEnvironmentVariable("REFCLIENT_CONNECTTESTER_SETTINGS_FILE");
            string path = !string.IsNullOrWhiteSpace(overridePath)
                ? overridePath
                : Path.Combine(AppContext.BaseDirectory, "ConnectTester.Settings.json");

            if (!File.Exists(path))
            {
                return new ConnectTesterSettings();
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(
                           stream,
                           ConnectTesterSettingsJsonContext.Default.ConnectTesterSettings)
                       ?? new ConnectTesterSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Console.WriteLine(
                    "Failed to load ConnectTester settings from '{0}': {1}",
                    path,
                    ex.Message);
                return new ConnectTesterSettings();
            }
        }
    }
}
