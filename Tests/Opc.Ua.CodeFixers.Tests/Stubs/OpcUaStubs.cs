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

namespace Opc.Ua.CodeFixers.Tests
{
    /// <summary>
    /// Minimal hand-written OPC UA 2.0 stubs used by the analyzer tests.
    /// Stubs are kept narrow on purpose - they reproduce just the public
    /// surface that the analyzers key off (struct vs class, [Obsolete],
    /// member signatures). Anything beyond that risks teaching tests to
    /// pass against the wrong shape.
    /// </summary>
    public static class OpcUaStubs
    {
        public const string Source =
"""
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Opc.Ua
{
    public readonly struct NodeId
    {
        public NodeId(uint id) { Identifier = id; NamespaceIndex = 0; }
        public NodeId(uint id, ushort ns) { Identifier = id; NamespaceIndex = ns; }
        [Obsolete("Use NodeId.Parse(string) instead.")]
        public NodeId(string s) { Identifier = s; NamespaceIndex = 0; }
        public object Identifier { get; }
        public ushort NamespaceIndex { get; }
        public bool IsNull => Identifier is null;
        public static NodeId Parse(string s) => default;
        public static bool TryParse(string s, out NodeId id) { id = default; return true; }
        public override string ToString() => Identifier?.ToString() ?? "";
        public override bool Equals(object? obj) => obj is NodeId n && Equals(Identifier, n.Identifier);
        public override int GetHashCode() => Identifier?.GetHashCode() ?? 0;
        public static bool operator ==(NodeId left, object? right) => right is null ? left.IsNull : left.Equals(right);
        public static bool operator !=(NodeId left, object? right) => !(left == right);
        public static bool operator ==(object? left, NodeId right) => right == left;
        public static bool operator !=(object? left, NodeId right) => right != left;
    }

    public readonly struct ExpandedNodeId
    {
        public ExpandedNodeId(uint id) { Identifier = id; }
        [Obsolete("Use ExpandedNodeId.Parse(string) instead.")]
        public ExpandedNodeId(string s) { Identifier = s; }
        public object Identifier { get; }
        public bool IsNull => Identifier is null;
        public static ExpandedNodeId Parse(string s) => default;
    }

    public readonly struct QualifiedName
    {
        public QualifiedName(string name) { Name = name; }
        public string Name { get; }
        public bool IsNull => string.IsNullOrEmpty(Name);
    }

    public readonly struct LocalizedText
    {
        public LocalizedText(string text) { Text = text; }
        public string Text { get; }
        public bool IsNullOrEmpty => string.IsNullOrEmpty(Text);
        public override bool Equals(object? obj) => obj is LocalizedText lt && Text == lt.Text;
        public override int GetHashCode() => Text?.GetHashCode() ?? 0;
        public static bool operator ==(LocalizedText left, object? right) => right is null ? left.IsNullOrEmpty : left.Equals(right);
        public static bool operator !=(LocalizedText left, object? right) => !(left == right);
        public static bool operator ==(object? left, LocalizedText right) => right == left;
        public static bool operator !=(object? left, LocalizedText right) => right != left;
    }

    public readonly struct ByteString
    {
        public ByteString(byte[] data) { Data = data; }
        public byte[]? Data { get; }
        public bool IsNull => Data is null;
        public Span<byte> Span => Data;
    }

    public static class ByteStringExtensions
    {
        public static ByteString ToByteString(this byte[] data) => new ByteString(data);
    }

    public readonly struct StatusCode
    {
        public StatusCode(uint code) { Code = code; }
        public uint Code { get; }
    }

    public readonly struct DateTimeUtc
    {
        public DateTimeUtc(DateTime dt) { UtcDateTime = dt; }
        public DateTime UtcDateTime { get; }
    }

    public readonly struct Uuid
    {
        public Uuid(Guid g) { Value = g; }
        public Guid Value { get; }
    }

    public readonly struct Variant
    {
        public Variant(int i) { _value = i; }
        public Variant(uint i) { _value = i; }
        public Variant(string s) { _value = s; }
        public Variant(NodeId n) { _value = n; }
        [Obsolete("Use Variant.From(object) instead.")]
        public Variant(object o) { _value = o; }
        [Obsolete("Use Variant.From(new DateTimeUtc(value)) instead.")]
        public Variant(DateTime dt) { _value = dt; }
        [Obsolete("Use Variant.From(new Uuid(value)) instead.")]
        public Variant(Guid g) { _value = g; }
        [Obsolete("Use Variant.From(value.ToByteString()) instead.")]
        public Variant(byte[] b) { _value = b; }
        private readonly object? _value;
        public bool IsNull => _value is null;
        public static Variant Null => default;
        public static Variant From<T>(T value) => default;
    }

    public readonly struct ExtensionObject
    {
        public ExtensionObject(object body) { Body = body; }
        public object Body { get; }
        public bool IsNull => Body is null;
    }

    public readonly struct DiagnosticInfo
    {
    }

    public readonly struct DataValue
    {
        public DataValue(Variant v) { Value = v; StatusCode = default; SourceTimestamp = default; }
        [Obsolete("Use DataValue.FromStatusCode(StatusCode) instead.")]
        public DataValue(StatusCode sc) { Value = default; StatusCode = sc; SourceTimestamp = default; }
        [Obsolete("Use DataValue.FromStatusCode(StatusCode, DateTimeUtc) instead.")]
        public DataValue(StatusCode sc, DateTimeUtc ts) { Value = default; StatusCode = sc; SourceTimestamp = ts; }
        public Variant Value { get; }
        public StatusCode StatusCode { get; }
        public DateTimeUtc SourceTimestamp { get; }
        public bool IsGood => StatusCode.Code == 0;
        public bool IsBad => (StatusCode.Code & 0x80000000) != 0;
        public bool IsUncertain => (StatusCode.Code & 0x40000000) != 0;
        public bool IsNull => false;
        public static DataValue Null => default;
        public static DataValue FromStatusCode(StatusCode sc) => default;
        public static DataValue FromStatusCode(StatusCode sc, DateTimeUtc ts) => default;
        [Obsolete("Use the dv.IsGood instance property.")]
        public static bool IsGood(DataValue dv) => dv.IsGood;
        [Obsolete("Use the dv.IsBad instance property.")]
        public static bool IsBad(DataValue dv) => dv.IsBad;
        [Obsolete("Use the dv.IsUncertain instance property.")]
        public static bool IsUncertain(DataValue dv) => dv.IsUncertain;
        [Obsolete("Use the !dv.IsGood instance property.")]
        public static bool IsNotGood(DataValue dv) => !dv.IsGood;
        [Obsolete("Use the !dv.IsBad instance property.")]
        public static bool IsNotBad(DataValue dv) => !dv.IsBad;
        [Obsolete("Use the !dv.IsUncertain instance property.")]
        public static bool IsNotUncertain(DataValue dv) => !dv.IsUncertain;
    }

    public static class DataValueExtensions
    {
        [Obsolete("Use the dv.IsGood instance property.")]
        public static bool IsGood(DataValue dv) => dv.IsGood;
        [Obsolete("Use the dv.IsBad instance property.")]
        public static bool IsBad(DataValue dv) => dv.IsBad;
        [Obsolete("Use the dv.IsUncertain instance property.")]
        public static bool IsUncertain(DataValue dv) => dv.IsUncertain;
        [Obsolete("Use the !dv.IsGood instance property.")]
        public static bool IsNotGood(DataValue dv) => !dv.IsGood;
        [Obsolete("Use the !dv.IsBad instance property.")]
        public static bool IsNotBad(DataValue dv) => !dv.IsBad;
        [Obsolete("Use the !dv.IsUncertain instance property.")]
        public static bool IsNotUncertain(DataValue dv) => !dv.IsUncertain;
    }

    public static class CertificateFactory
    {
        [Obsolete("Use DefaultCertificateFactory.Instance.Create.")]
        public static object Create(string subject) => null!;
        [Obsolete("Use DefaultCertificateFactory.Instance.CreateCertificate.")]
        public static object CreateCertificate(string subject) => null!;
        [Obsolete("Use DefaultCertificateFactory.Instance.CreateSigningRequest.")]
        public static object CreateSigningRequest(string subject) => null!;
        [Obsolete("Use DefaultCertificateFactory.Instance.RevokeCertificate.")]
        public static object RevokeCertificate(string subject) => null!;
        [Obsolete("Use DefaultCertificateFactory.Instance.CreateCertificateWithPEMPrivateKey.")]
        public static object CreateCertificateWithPEMPrivateKey(string subject) => null!;
        [Obsolete("Use DefaultCertificateFactory.Instance.CreateCertificateWithPrivateKey.")]
        public static object CreateCertificateWithPrivateKey(string subject) => null!;
    }

    public sealed class DefaultCertificateFactory
    {
        public static DefaultCertificateFactory Instance { get; } = new();
        public object Create(string subject) => null!;
        public object CreateCertificate(string subject) => null!;
        public object CreateSigningRequest(string subject) => null!;
        public object RevokeCertificate(string subject) => null!;
        public object CreateCertificateWithPEMPrivateKey(string subject) => null!;
        public object CreateCertificateWithPrivateKey(string subject) => null!;
    }

    // ─── Stubs for UA0001 (Utils.Trace/LogX → ILogger) ───
    public interface ITelemetryContext
    {
        Microsoft.Extensions.Logging.ILogger CreateLogger<T>();
    }
    public static partial class Utils
    {
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void Trace(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void Trace(string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void Trace(Exception e, string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void Trace(int traceMask, string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogError(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogError(string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogError(Exception e, string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogWarning(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogWarning(string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogInformation(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogInformation(string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogDebug(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogDebug(string format, params object[] args) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogTrace(string message) { }
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void LogCritical(string message) { }
    }
    public static class TraceMasks
    {
        public const int Error = 1;
        public const int Information = 2;
    }

    // ─── Stubs for UA0002 (Collection types removed) ───
    public class Int32Collection : List<int> { }
    public class UInt32Collection : List<uint> { }
    public class StringCollection : List<string> { }
    public class NodeIdCollection : List<NodeId> { }
    public class VariantCollection : List<Variant> { }
    public class DataValueCollection : List<DataValue> { }
    public class ByteStringCollection : List<ByteString> { }
    public class ArgumentCollection : List<Argument> { }
    public class ServerSecurityPolicyCollection : List<ServerSecurityPolicy> { }
    public class TransportConfigurationCollection : List<TransportConfiguration> { }
    public class ReverseConnectClientCollection : List<ReverseConnectClient> { }
    public class Argument { }
    public class ServerSecurityPolicy { }
    public class TransportConfiguration { }
    public class ReverseConnectClient { }
    public readonly struct ArrayOf<T>
    {
        public ArrayOf(T[] data) { Data = data; }
        public T[]? Data { get; }
    }

    // ─── Stubs for UA0005 (byte[] → ByteString) ───
    // ByteString is already defined above; expose an API surface that takes ByteString.
    public static class ByteStringApi
    {
        public static void Process(ByteString data) { }
        public static void Process(byte[] data) { }
    }

    // ─── Stubs for UA0008 (Session.Call params object[] → params Variant[]) ───
    public interface ISession
    {
        object Call(NodeId objectId, NodeId methodId, params Variant[] args);
        Task<object> CallAsync(NodeId objectId, NodeId methodId, CancellationToken ct, params Variant[] args);
    }
    public class Session : ISession
    {
        public object Call(NodeId objectId, NodeId methodId, params Variant[] args) => null!;
        public Task<object> CallAsync(NodeId objectId, NodeId methodId, CancellationToken ct, params Variant[] args) => Task.FromResult<object>(null!);
    }

    // ─── Stubs for UA0009 ([DataContract]/[DataMember] → [DataType]/[DataTypeField]) ───
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DataTypeAttribute : Attribute
    {
        public string? TypeId { get; set; }
    }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class DataTypeFieldAttribute : Attribute
    {
        public int Order { get; set; }
    }
    public class ApplicationConfiguration
    {
        public static T ParseExtension<T>() where T : new() => new();
        public static void UpdateExtension<T>(T value) { }

        // ─── Stubs for UA0022 (CertificateValidator → CertificateManager property rename) ───
        [Obsolete("Use CertificateManager instead.")]
        public object? CertificateValidator { get; set; }
        public object? CertificateManager { get; set; }
    }

    public class ServerBase
    {
        [Obsolete("Use CertificateManager instead.")]
        public object? CertificateValidator { get; set; }
        public object? CertificateManager { get; set; }
    }

    // ─── Stubs for UA0010 (Remove IDisposable on cert/identity types) ───
    // CertificateIdentifier intentionally does NOT implement IDisposable in 2.0.
    public class CertificateIdentifier
    {
        public string? SubjectName { get; set; }
    }
    public class UserIdentity
    {
        public string? Name { get; set; }
    }

    // ─── Stubs for UA0011 (TokenHandler sync → async) ───
    public interface IUserIdentityTokenHandler
    {
        [Obsolete("Use EncryptAsync instead.")]
        byte[] Encrypt(byte[] data);
        [Obsolete("Use DecryptAsync instead.")]
        byte[] Decrypt(byte[] data);
        [Obsolete("Use SignAsync instead.")]
        byte[] Sign(byte[] data);
        [Obsolete("Use VerifyAsync instead.")]
        bool Verify(byte[] data, byte[] signature);
        Task<byte[]> EncryptAsync(byte[] data, CancellationToken ct);
        Task<byte[]> DecryptAsync(byte[] data, CancellationToken ct);
        Task<byte[]> SignAsync(byte[] data, CancellationToken ct);
        Task<bool> VerifyAsync(byte[] data, byte[] signature, CancellationToken ct);
    }

    // ─── Stubs for UA0015 (GDS sync → async) ───
    public class GlobalDiscoveryServerClient
    {
        [Obsolete("Use RegisterApplicationAsync instead.")]
        public void RegisterApplication(string applicationUri) { }
        [Obsolete("Use UnregisterApplicationAsync instead.")]
        public void UnregisterApplication(string applicationUri) { }
        public Task RegisterApplicationAsync(string applicationUri, CancellationToken ct) => Task.CompletedTask;
        public Task UnregisterApplicationAsync(string applicationUri, CancellationToken ct) => Task.CompletedTask;
    }
    public class ServerPushConfigurationClient
    {
        [Obsolete("Use ApplyChangesAsync instead.")]
        public void ApplyChanges() { }
        public Task ApplyChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
    public class LocalDiscoveryServerClient
    {
        [Obsolete("Use FindServersAsync instead.")]
        public IAsyncResult BeginFindServers(string endpoint, AsyncCallback? callback, object? state) => null!;
        [Obsolete("Use FindServersAsync instead.")]
        public string[] EndFindServers(IAsyncResult result) => System.Array.Empty<string>();
        public Task<string[]> FindServersAsync(string endpoint, CancellationToken ct) => Task.FromResult(System.Array.Empty<string>());
    }

    // ─── Stubs for UA0018 (CertificateIdentifier.Certificate → ResolveAsync) ───
    // Add a getter that's marked obsolete on the CertificateIdentifier defined above
    // (we provide a separate type with the obsolete getter for test isolation).
    public class CertificateIdentifierWithObsoleteCertificate
    {
        [Obsolete("Use CertificateIdentifierResolver.ResolveAsync instead.")]
        public object? Certificate => null;
    }
    public static class CertificateIdentifierResolver
    {
        public static Task<object> ResolveAsync(
            CertificateIdentifier id,
            object registry,
            bool needPrivateKey,
            string applicationUri,
            ITelemetryContext telemetry,
            CancellationToken ct) => Task.FromResult<object>(null!);
    }

    // ─── Stubs for UA0020 (EncodeableFactory renames) ───
    public class EncodeableFactory
    {
        [Obsolete("Use ServiceMessageContext.Factory instead.")]
        public static EncodeableFactory GlobalFactory { get; } = new EncodeableFactory();
        [Obsolete("Use Fork() instead.")]
        public EncodeableFactory Create() => new EncodeableFactory();
        public EncodeableFactory Fork() => new EncodeableFactory();
    }
    public class ServiceMessageContext
    {
        public EncodeableFactory Factory { get; } = new EncodeableFactory();
    }

    // ─── Stubs for UA0021 (CertificateValidator / CertificateValidationEventArgs rename) ───
    // The legacy types are kept here so the analyzer's "symbol-present + [Obsolete]" branch
    // can be exercised. The 1.6 replacements (ICertificateManager, ICertificateValidatorEx,
    // CertificateValidationResult) are stubbed to verify the analyzer does NOT fire on them.
    [Obsolete("Use ICertificateManager (via CertificateManagerFactory.Create) instead. See MigrationGuide.md#ua0021.")]
    public class CertificateValidator { }
    [Obsolete("Use CertificateValidationResult returned from ICertificateValidatorEx.ValidateAsync instead. See MigrationGuide.md#ua0021.")]
    public class CertificateValidationEventArgs : EventArgs { }
    public interface ICertificateManager { }
    public interface ICertificateValidatorEx { }
    public class CertificateValidationResult { }

    // ─── OpcUaShim marker attribute and shim wrappers used by analyzer tests ───
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    public sealed class OpcUaShimAttribute : Attribute
    {
        public string RuleId { get; }
        public OpcUaShimAttribute(string ruleId) { RuleId = ruleId; }
    }

    // Shim for UA0008: Call/CallAsync with raw object args on ISession-like receiver.
    public static class SessionShim
    {
        [Obsolete("Use ISession.Call(params Variant[]) instead.")]
        [OpcUaShim("UA0008")]
        public static object Call(this ISession session, NodeId objectId, NodeId methodId, params object[] args)
            => null!;

        [Obsolete("Use ISession.CallAsync(params Variant[]) instead.")]
        [OpcUaShim("UA0008")]
        public static Task<object> CallAsync(
            this ISession session, NodeId objectId, NodeId methodId, CancellationToken ct, params object[] args)
            => Task.FromResult<object>(null!);
    }

    // Shim for UA0011: synchronous Encrypt/Decrypt/Sign/Verify on token handler.
    public static class UserIdentityTokenHandlerShim
    {
        [Obsolete("Use EncryptAsync instead.")]
        [OpcUaShim("UA0011")]
        public static byte[] Encrypt(this IUserIdentityTokenHandler handler, byte[] data) => null!;

        [Obsolete("Use DecryptAsync instead.")]
        [OpcUaShim("UA0011")]
        public static byte[] Decrypt(this IUserIdentityTokenHandler handler, byte[] data) => null!;

        // Same shape, different RuleId — used to verify rule-id filtering.
        [Obsolete("Different-rule shim used for negative test.")]
        [OpcUaShim("UA9999")]
        public static byte[] EncryptUnrelated(this IUserIdentityTokenHandler handler, byte[] data) => null!;
    }

    // Shim for UA0015: synchronous GDS/LDS members.
    public static class GdsClientShim
    {
        [Obsolete("Use RegisterApplicationAsync instead.")]
        [OpcUaShim("UA0015")]
        public static void RegisterApplicationLegacy(this GlobalDiscoveryServerClient client, string applicationUri) { }

        [Obsolete("Use FindServersAsync instead.")]
        [OpcUaShim("UA0015")]
        public static string[] FindServersLegacy(this LocalDiscoveryServerClient client, string endpoint)
            => System.Array.Empty<string>();
    }

    // Shim for UA0018: CertificateIdentifier.Certificate getter relocated to shim.
    public class CertificateIdentifierShimHost
    {
        [Obsolete("Use CertificateIdentifierResolver.ResolveAsync instead.")]
        [OpcUaShim("UA0018")]
        public object? Certificate => null;
    }

    // Shim for UA0020: EncodeableFactory.GlobalFactory / Create relocated to shim.
    public static class EncodeableFactoryShim
    {
        [Obsolete("Use ServiceMessageContext.Factory instead.")]
        [OpcUaShim("UA0020")]
        public static EncodeableFactory GlobalFactory => new EncodeableFactory();

        [Obsolete("Use Fork() instead.")]
        [OpcUaShim("UA0020")]
        public static EncodeableFactory Create(this EncodeableFactory factory) => new EncodeableFactory();
    }
}

namespace Microsoft.Extensions.Logging
{
    public interface ILogger
    {
        void LogError(string message);
        void LogWarning(string message);
        void LogInformation(string message);
        void LogDebug(string message);
        void LogTrace(string message);
        void LogCritical(string message);
    }
}
""";
    }
}
