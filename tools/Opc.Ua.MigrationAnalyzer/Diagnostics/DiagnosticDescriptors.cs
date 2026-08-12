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

using Microsoft.CodeAnalysis;

namespace Opc.Ua.MigrationAnalyzer.Diagnostics
{
    /// <summary>
    /// Centralised registry of every <see cref="DiagnosticDescriptor"/> shipped
    /// by the OPC UA migration analyzer package. Descriptors are created once
    /// and shared by analyzers and tests, so message text and severity stay
    /// consistent across the codebase.
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        private static DiagnosticDescriptor Create(
            string id,
            string title,
            string messageFormat,
            DiagnosticSeverity defaultSeverity,
            string description)
        {
            return new DiagnosticDescriptor(
                id: id,
                title: title,
                messageFormat: messageFormat,
                category: DiagnosticIds.Category,
                defaultSeverity: defaultSeverity,
                isEnabledByDefault: true,
                description: description,
                helpLinkUri: DiagnosticIds.HelpLinkFor(id));
        }

        public static readonly DiagnosticDescriptor UA0001_UtilsTraceToILogger = Create(
            DiagnosticIds.UA0001,
            "Replace Utils.Trace / Utils.LogX with ILogger",
            "'{0}' is deprecated. Use an ILogger obtained from ITelemetryContext.CreateLogger<T>() instead.",
            DiagnosticSeverity.Info,
            "Opc.Ua.Utils.Trace and the Utils.LogX helpers are obsolete in 2.0. Logging is now performed through ILogger instances created from an ITelemetryContext that flows through the constructor.");

        public static readonly DiagnosticDescriptor UA0002_RemovedCollectionType = Create(
            DiagnosticIds.UA0002,
            "Removed collection wrapper type",
            "'{0}' was removed in 2.0. Use 'List<{1}>' for mutable storage or 'ArrayOf<{1}>' for read-only consumers.",
            DiagnosticSeverity.Warning,
            "Generated <Type>Collection wrappers (Int32Collection, VariantCollection, NodeIdCollection, ...) were removed in 2.0 in favour of List<T> and ArrayOf<T>.");

        public static readonly DiagnosticDescriptor UA0003_NullCheckOnStructType = Create(
            DiagnosticIds.UA0003,
            "Null comparison on now-struct built-in type",
            "'{0}' is now a value type; use IsNull instead of comparing with null",
            DiagnosticSeverity.Warning,
            "NodeId, ExpandedNodeId, QualifiedName, LocalizedText, ExtensionObject, DataValue, Variant and ByteString are readonly structs in 2.0. Comparing them with null is misleading. Use the .IsNull property (or LocalizedText.IsNullOrEmpty) instead.");

        public static readonly DiagnosticDescriptor UA0004_ConditionalAccessOnStructType = Create(
            DiagnosticIds.UA0004,
            "Null-conditional access on now-struct built-in type",
            "'?.' on '{0}' is unnecessary because '{0}' is now a value type. Use a direct access or guard with '.IsNull'.",
            DiagnosticSeverity.Warning,
            "NodeId, Variant, DataValue and the other built-in types became structs in 2.0 — the null-conditional operator is no longer meaningful on them.");

        public static readonly DiagnosticDescriptor UA0005_ByteArrayWhereByteStringExpected = Create(
            DiagnosticIds.UA0005,
            "Pass ByteString where required",
            "A 'byte[]' is being passed where 'ByteString' is now expected by '{0}'. Call '.ToByteString()' on the array.",
            DiagnosticSeverity.Warning,
            "2.0 APIs that previously took byte[] now require Opc.Ua.ByteString. Convert with the .ToByteString() extension.");

        public static readonly DiagnosticDescriptor UA0006_ObsoleteVariantCtor = Create(
            DiagnosticIds.UA0006,
            "Obsolete Variant constructor",
            "'new Variant({0})' is obsolete. Use 'Variant.From({0})' (and the matching Uuid/DateTimeUtc/ByteString wrapper if needed).",
            DiagnosticSeverity.Warning,
            "The non-generic Variant constructors accepting object/DateTime/Guid/byte[] were obsoleted in 2.0. Variant.From<T>(T) preserves the value's type information correctly.");

        public static readonly DiagnosticDescriptor UA0007_ObsoleteNodeIdStringCtor = Create(
            DiagnosticIds.UA0007,
            "Obsolete NodeId(string) constructor",
            "'new {0}(string)' is obsolete. Use '{0}.Parse(s)' (or 'TryParse' for untrusted input).",
            DiagnosticSeverity.Warning,
            "new NodeId(string) and new ExpandedNodeId(string) were obsoleted in 2.0 in favour of explicit Parse / TryParse.");

        public static readonly DiagnosticDescriptor UA0008_SessionCallParamsObject = Create(
            DiagnosticIds.UA0008,
            "Wrap Session.Call arguments with Variant.From",
            "'{0}' now takes 'params Variant[]'. Wrap each argument with 'Variant.From(...)' (or 'Variant.Null' for null).",
            DiagnosticSeverity.Warning,
            "Session.Call / Session.CallAsync changed from params object[] to params Variant[] in 2.0.");

        public static readonly DiagnosticDescriptor UA0009_DataContractToDataType = Create(
            DiagnosticIds.UA0009,
            "Replace [DataContract]/[DataMember] on configuration extensions",
            "'{0}' is consumed by ParseExtension or UpdateExtension; use [DataType]/[DataTypeField] from Opc.Ua and mark the class partial",
            DiagnosticSeverity.Warning,
            "Configuration extension classes serialised through ParseExtension<T>/UpdateExtension<T> use the source-generator-driven [DataType]/[DataTypeField] attributes in 2.0.");

        public static readonly DiagnosticDescriptor UA0010_RemoveDisposable = Create(
            DiagnosticIds.UA0010,
            "Remove using/Dispose on non-IDisposable identity",
            "'{0}' is no longer IDisposable in 2.0 — remove the 'using' (or the explicit Dispose call). Lifecycle is owned by CertificateManager.",
            DiagnosticSeverity.Warning,
            "CertificateIdentifier, UserIdentity and IUserIdentityTokenHandler are no longer IDisposable in 2.0.");

        public static readonly DiagnosticDescriptor UA0011_TokenHandlerSyncToAsync = Create(
            DiagnosticIds.UA0011,
            "User identity token handler — use async members",
            "'{0}' is removed in 2.0; call the async counterpart and propagate the CancellationToken",
            DiagnosticSeverity.Info,
            "IUserIdentityTokenHandler synchronous Encrypt/Decrypt/Sign/Verify have been replaced by their *Async counterparts.");

        public static readonly DiagnosticDescriptor UA0012_CertificateFactoryStaticToInstance = Create(
            DiagnosticIds.UA0012,
            "Obsolete static CertificateFactory member",
            "'CertificateFactory.{0}' is obsolete. Use 'DefaultCertificateFactory.Instance.{0}'.",
            DiagnosticSeverity.Warning,
            "Static CertificateFactory helpers (Create, CreateCertificate, CreateSigningRequest, RevokeCertificate, ...) were obsoleted in 2.0 in favour of the singleton DefaultCertificateFactory.Instance.");

        public static readonly DiagnosticDescriptor UA0014_DataValueIsGoodStaticToInstance = Create(
            DiagnosticIds.UA0014,
            "Use DataValue.IsGood instance property",
            "'DataValue.{0}(dv)' is obsolete. Use 'dv.{0}'.",
            DiagnosticSeverity.Warning,
            "The static DataValue.IsGood/IsBad/IsUncertain helpers became instance properties in 2.0.");

        public static readonly DiagnosticDescriptor UA0015_GdsSyncToAsync = Create(
            DiagnosticIds.UA0015,
            "GDS/LDS client — use async members",
            "'{0}' is removed in 2.0; call the async counterpart and propagate the CancellationToken",
            DiagnosticSeverity.Info,
            "Synchronous and APM members on GlobalDiscoveryServerClient / LocalDiscoveryServerClient / ServerPushConfigurationClient were removed in 2.0.");

        public static readonly DiagnosticDescriptor UA0018_CertificateIdentifierCertificateGetter = Create(
            DiagnosticIds.UA0018,
            "Use CertificateIdentifierResolver.ResolveAsync",
            "'CertificateIdentifier.Certificate' is removed in 2.0; call CertificateIdentifierResolver.ResolveAsync",
            DiagnosticSeverity.Info,
            "The synchronous CertificateIdentifier.Certificate getter was removed in 2.0. Use the async resolver.");

        public static readonly DiagnosticDescriptor UA0019_ObsoleteDataValueStatusCodeCtor = Create(
            DiagnosticIds.UA0019,
            "Obsolete DataValue(StatusCode) constructor",
            "'new DataValue(StatusCode{0})' silently lost the value semantics. Use 'DataValue.FromStatusCode(...)'.",
            DiagnosticSeverity.Warning,
            "new DataValue(StatusCode) / new DataValue(StatusCode, DateTimeUtc) were obsoleted in 2.0 because they silently resolved to the StatusCode overload and lost the value. DataValue.FromStatusCode is explicit.");

        public static readonly DiagnosticDescriptor UA0020_EncodeableFactoryRename = Create(
            DiagnosticIds.UA0020,
            "EncodeableFactory member renamed",
            "'{0}' was replaced in 2.0. Use '{1}' instead.",
            DiagnosticSeverity.Warning,
            "EncodeableFactory.GlobalFactory was removed (consumers now obtain the factory from ServiceMessageContext.Factory) and EncodeableFactory.Create was renamed to Fork.");

        public static readonly DiagnosticDescriptor UA0021_CertificateValidatorRename = Create(
            DiagnosticIds.UA0021,
            "CertificateValidator / CertificateValidationEventArgs renamed in 1.6",
            "'{0}' was replaced in 1.6 by the new CertificateManager pipeline (ICertificateManager / ICertificateValidatorEx / CertificateValidationResult). The migration is structural (event-based -> async result + AcceptError callback) — see docs/migrate/2.0.x/certificates.md.",
            DiagnosticSeverity.Info,
            "The CertificateValidator class and CertificateValidationEventArgs were removed in 1.6. The new ICertificateManager (composed of ICertificateValidatorEx, ICertificateRegistry, ICertificateTrustListManager, ICertificateLifecycle) replaces them; per-error accept logic moves from the CertificateValidation event to CertificateValidationOptions.AcceptError. This rule is diagnostic-only because the migration changes the API shape (no mechanical rename).");

        public static readonly DiagnosticDescriptor UA0022_CertificateValidatorPropertyRename = Create(
            DiagnosticIds.UA0022,
            "ApplicationConfiguration.CertificateValidator / ServerBase.CertificateValidator renamed in 2.0",
            "'{0}.CertificateValidator' was removed in 2.0 — use '{0}.CertificateManager' (type ICertificateManager)",
            DiagnosticSeverity.Warning,
            "Configure via CertificateManagerFactory.Create(securityConfiguration, telemetry, ...). See docs/migrate/2.0.x/certificates.md.");

        public static readonly DiagnosticDescriptor UA0023_PubSubTopLevelObsolete = Create(
            DiagnosticIds.UA0023,
            "PubSub top-level types replaced in 2.0",
            "'{0}' was replaced in 2.0 — use the new IPubSubApplication / PubSubApplicationBuilder surface (or AddPubSub() / AddUdpTransport() / AddMqttTransport() on IOpcUaBuilder)",
            DiagnosticSeverity.Warning,
            "The 1.04-era PubSub top-level types (UaPubSubApplication, IUaPubSubConnection, IUaPublisher, UaPubSubDataStore, UaPubSubConfigurator) ship as obsolete shims in 2.0; the new top-level surface uses provider-model abstractions wired via PubSubApplicationBuilder or Microsoft.Extensions.DependencyInjection extensions (docs/migrate/2.0.x/pubsub.md).");

        public static readonly DiagnosticDescriptor UA0024_RemovedDiagnosticsLock = Create(
            DiagnosticIds.UA0024,
            "Diagnostics locks are no longer exposed",
            "'{0}.{1}' was removed in 2.0 — apply the change through '{0}.{2}' instead of taking the lock yourself",
            DiagnosticSeverity.Warning,
            "IServerInternal, ISession and ISubscription no longer expose DiagnosticsLock / DiagnosticsWriteLock. A caller could not see what else took those locks, in what order, or for how long, and holding one across a call back into the stack could deadlock. Each owner now applies the mutation itself through UpdateDiagnostics / UpdateServerDiagnostics, with ISession.ReadDiagnostics / ISubscription.ReadDiagnostics for projections. Do not let the diagnostics object escape the callback: once it returns the lock is released. This rule reports rather than fixes, because turning a lock statement body into a lambda depends on what the body captures and returns.");

                    public static readonly DiagnosticDescriptor UA0025_RemovedNodeDataLock = Create(
            DiagnosticIds.UA0025,
            "ILocalNode.DataLock is no longer exposed",
            "'{0}.DataLock' was removed in 2.0 — the node synchronizes its own state, so take a lock you own if the surrounding operation still needs to be atomic",
            DiagnosticSeverity.Warning,
            "ILocalNode.DataLock returned the node instance itself, so every caller that took it shared one lock with the stack and with every other caller, in an order none of them could see, and holding it across a call back into the stack could deadlock. The node now guards its own state. Code that needs a wider critical section should use a lock it owns rather than one reachable from a shared node.");

        public static readonly DiagnosticDescriptor UA0026_RemovedVariableValueLock = Create(
            DiagnosticIds.UA0026,
            "BaseVariableValue.Lock is no longer exposed",
            "'{0}.Lock' was removed in 2.0 — construct the value with a lock you own and take that one instead",
            DiagnosticSeverity.Warning,
            "BaseVariableValue handed out the synchronization primitive that guards its value, so a caller could hold it across a callback into the stack. The value now owns it: derived value classes synchronize through the protected EnterLock / ExitLock pair, and a component that has to make its own state atomic with the value passes a lock it already owns to the BaseVariableValue constructor - which is how the server keeps its status and its diagnostics mutually exclusive - and takes that lock directly.");

        public static readonly DiagnosticDescriptor UA0027_RemovedNodeBrowserDataLock = Create(
            DiagnosticIds.UA0027,
            "NodeBrowser no longer exposes a synchronization lock",
            "'NodeBrowser.DataLock' was removed in 2.0 — a browser is single-consumer, so drop the lock instead of replacing it",
            DiagnosticSeverity.Warning,
            "NodeBrowser no longer exposes a protected DataLock. A browser is single-consumer: it belongs to whoever created it, and both server browse paths already serialize access on the owning side (BrowserContext for the async node manager, a lock around the continuation point's browser for CustomNodeManager2). The exposed lock therefore only added an inheritance-level locking contract that a derived browser could not reason about — it could be held across a call into another module with nothing in the type system saying for how long. A derived browser that took DataLock inside its Next() override should simply remove the lock statement and keep the body. If a browser really is shared between threads, serialize it where it is owned, not inside the browser. See docs/migrate/2.0.x/node-states.md.");

        public static readonly DiagnosticDescriptor UA0028_RemovedPropertiesLock = Create(
            DiagnosticIds.UA0028,
            "ApplicationConfiguration.PropertiesLock is no longer exposed",
            "'ApplicationConfiguration.PropertiesLock' was removed in 2.0 — Properties synchronizes itself, so drop the lock and use GetOrAddProperty(...) if a read and a write have to be atomic",
            DiagnosticSeverity.Warning,
            "ApplicationConfiguration.PropertiesLock returned the properties dictionary itself, so the lock was the data it guarded: every caller that took it shared one monitor with the dictionary and with every other caller, in an order none of them could see. Properties is now a concurrent dictionary, so each individual operation is atomic without a lock. The only combination that needs more than one operation is get-or-add, which GetOrAddProperty covers; it deliberately does not invoke the factory under a lock, so a caller cannot hold a critical section across a callback. This rule reports rather than fixes, because whether a lock body becomes a plain call or a GetOrAddProperty depends on what the body does.");
    }
}
