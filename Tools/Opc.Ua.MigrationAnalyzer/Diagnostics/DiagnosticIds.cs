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

namespace Opc.Ua.MigrationAnalyzer.Diagnostics
{
    /// <summary>
    /// Stable identifiers for every diagnostic shipped by the
    /// OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer analyzer package.
    /// Keep IDs immutable across releases — consumers may use them
    /// in <c>#pragma warning disable</c> or <c>.editorconfig</c>.
    /// </summary>
    internal static class DiagnosticIds
    {
        public const string UA0001 = "UA0001";
        public const string UA0002 = "UA0002";
        public const string UA0003 = "UA0003";
        public const string UA0004 = "UA0004";
        public const string UA0005 = "UA0005";
        public const string UA0006 = "UA0006";
        public const string UA0007 = "UA0007";
        public const string UA0008 = "UA0008";
        public const string UA0009 = "UA0009";
        public const string UA0010 = "UA0010";
        public const string UA0011 = "UA0011";
        public const string UA0012 = "UA0012";
        public const string UA0014 = "UA0014";
        public const string UA0015 = "UA0015";
        public const string UA0018 = "UA0018";
        public const string UA0019 = "UA0019";
        public const string UA0020 = "UA0020";
        public const string UA0021 = "UA0021";
        public const string UA0022 = "UA0022";

        /// <summary>The diagnostic category every UA00xx rule belongs to.</summary>
        public const string Category = "Migration";

        /// <summary>
        /// Base URL for per-rule help. Each rule appends its own ID.
        /// Points at the MigrationGuide.md "Automated migration" section.
        /// </summary>
        public const string HelpLinkUriBase =
            "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#";

        /// <summary>Compose a per-rule help URL anchored at the rule ID.</summary>
        public static string HelpLinkFor(string id) => HelpLinkUriBase + id.ToLowerInvariant();
    }
}
