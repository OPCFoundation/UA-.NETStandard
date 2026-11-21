// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public const string UA0023 = "UA0023";
        public const string UA0024 = "UA0024";
        public const string UA0025 = "UA0025";
        public const string UA0026 = "UA0026";

        /// <summary>
        /// NodeBrowser.DataLock was removed: a browser is single-consumer in 2.0.
        /// </summary>
        public const string UA0027 = "UA0027";

        /// <summary>
        /// ApplicationConfiguration.PropertiesLock was removed: the properties
        /// dictionary synchronizes itself in 2.0.
        /// </summary>
        public const string UA0028 = "UA0028";

        /// <summary>
        /// The SecurityPolicies lookup and cryptography statics moved onto
        /// ISecurityPolicyRegistry: they operate on the set of registered
        /// policies rather than on constants.
        /// </summary>
        public const string UA0029 = "UA0029";

        /// <summary>
        /// The ISubscription publish-pipeline members and SessionPublishQueue
        /// are server-internal in 2.0: only SubscriptionManager and the publish
        /// queue may drive the publishing state machine.
        /// </summary>
        public const string UA0030 = "UA0030";

        // UA0031 is reserved: it is the DiagnosticId of the [Obsolete] attributes that
        // deprecate the synchronous node manager API in 2.0.3 (docs/migrate/2.0.3/node-managers.md).
        // It is reported by the compiler, not by this analyzer package.

        /// <summary>The diagnostic category every UA00xx rule belongs to.</summary>
        public const string Category = "Migration";

        /// <summary>
        /// Base URL for per-rule help. Each rule appends its own ID.
        /// Points at the 2.0 migration index (`docs/migrate/2.0.x/README.md`)
        /// which contains the symptom → sub-doc table that maps every UA00xx
        /// rule to the sub-doc that documents it in detail.
        /// </summary>
        public const string HelpLinkUriBase =
            "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/README.md#";

        /// <summary>Compose a per-rule help URL anchored at the rule ID.</summary>
        public static string HelpLinkFor(string id)
        {
            return HelpLinkUriBase + id.ToLowerInvariant();
        }
    }
}
