"""
asyncua (FreeOpcUa opcua-asyncio) interop peer.

Speaks the same CLI / stdout contract as tests/Opc.Ua.Interop.LegacyPeer so the
2.0 interop fixtures can drive it as a child process:

  server --port <port> --pki <dir> [--kind interop] [--autoaccept true|false]
         [--init-only true]
      prints "PEER-SERVER-READY <url>" (and the legacy alias
      "LEGACY-SERVER-READY <url>"), runs until stdin closes or reads "stop".
  client --url <url> --pki <dir> [--policy <uri>] [--mode None|Sign|SignAndEncrypt]
         [--user <name> --password <pw>] [--checks a,b,c]
         [--expect-connect-error A,B] [--timeout-seconds N]
      prints one "RESULT {json}" line per check and "SUMMARY passed=N failed=M".

PKI layout (shared with the .NET peers): <pki>/own/{certs,private},
<pki>/trusted/{certs,crl}, <pki>/issuer/{certs,crl}, <pki>/rejected/certs.
"""

from __future__ import annotations

import asyncio
import datetime
import json
import logging
import math
import random
import socket
import sys
import threading
import time
import uuid
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import serialization
from cryptography.x509.oid import ExtendedKeyUsageOID

from asyncua import Client, Server, ua
from asyncua.crypto import cert_gen, security_policies
from asyncua.crypto.permission_rules import User, UserRole
from asyncua.crypto.truststore import TrustStore
from asyncua.client.ua_client import UaClientState
from asyncua.common.connection import TransportLimits
from asyncua.common.subscription import Subscription
from asyncua.ua.ua_binary import struct_from_binary
from asyncua.crypto.validator import CertificateValidator, CertificateValidatorOptions
from asyncua.server.user_managers import UserManager

EXIT_SUCCESS, EXIT_CHECKS_FAILED, EXIT_USAGE, EXIT_FATAL = 0, 1, 2, 3

INTEROP_NAMESPACE = "urn:opcfoundation.org:interop:legacy"
REFERENCE_NAMESPACE = "http://opcfoundation.org/Quickstarts/ReferenceServer"
USER_NAME, PASSWORD = "interop", "interop-password"

POLICIES = {
    "http://opcfoundation.org/UA/SecurityPolicy#None": None,
    "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256": security_policies.SecurityPolicyBasic256Sha256,
    "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep": security_policies.SecurityPolicyAes128Sha256RsaOaep,
    "http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss": security_policies.SecurityPolicyAes256Sha256RsaPss,
}


# --------------------------------------------------------------------------- options / PKI


def parse_options(args: list[str]) -> dict[str, str]:
    options: dict[str, str] = {}
    ii = 0
    while ii < len(args):
        name = args[ii]
        if not name.startswith("--") or ii + 1 >= len(args):
            raise ValueError("expected --name value, got " + name)
        options[name[2:].lower()] = args[ii + 1]
        ii += 2
    return options


def flag(options: dict[str, str], name: str, default: bool) -> bool:
    return options[name].lower() == "true" if name in options else default


def pki_dirs(root: Path) -> dict[str, Path]:
    dirs = {
        "own_certs": root / "own" / "certs",
        "own_private": root / "own" / "private",
        "trusted": root / "trusted" / "certs",
        "trusted_crl": root / "trusted" / "crl",
        "issuer": root / "issuer" / "certs",
        "issuer_crl": root / "issuer" / "crl",
        "rejected": root / "rejected" / "certs",
    }
    for d in dirs.values():
        d.mkdir(parents=True, exist_ok=True)
    return dirs


def ensure_certificate(root: Path, name: str, application_uri: str, server: bool) -> tuple[Path, Path]:
    """Creates (once) a self-signed RSA 2048 application certificate in own/."""
    dirs = pki_dirs(root)
    cert_path = dirs["own_certs"] / f"{name}.der"
    key_path = dirs["own_private"] / f"{name}.pem"
    if cert_path.exists() and key_path.exists():
        return cert_path, key_path
    key = cert_gen.generate_private_key()
    usage = [ExtendedKeyUsageOID.SERVER_AUTH, ExtendedKeyUsageOID.CLIENT_AUTH] if server else [ExtendedKeyUsageOID.CLIENT_AUTH]
    cert = cert_gen.generate_self_signed_app_certificate(
        key,
        name,
        {"organizationName": "OPC Foundation", "domainComponent": "localhost"},
        [x509.UniformResourceIdentifier(application_uri), x509.DNSName("localhost"), x509.DNSName(socket.gethostname())],
        extended=usage,
        days=365,
    )
    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.DER))
    key_path.write_bytes(cert_gen.dump_private_key_as_pem(key))
    return cert_path, key_path


def make_validator(root: Path, auto_accept: bool, peer_is_server: bool):
    """Validates peer certificates against trusted/ + issuer/ unless auto-accepting.
    Rejected certificates are copied to rejected/certs like the .NET stacks do."""
    dirs = pki_dirs(root)
    store = TrustStore([dirs["trusted"], dirs["issuer"]], [dirs["trusted_crl"], dirs["issuer_crl"]])
    validator = CertificateValidator(CertificateValidatorOptions.TRUSTED_VALIDATION | (CertificateValidatorOptions.PEER_SERVER if peer_is_server else CertificateValidatorOptions.PEER_CLIENT), store)
    loaded = False

    async def validate(cert: x509.Certificate, app_description: ua.ApplicationDescription):
        nonlocal loaded
        if auto_accept:
            return
        if not loaded:
            await store.load()
            loaded = True
        try:
            await validator.validate(cert, app_description)
        except Exception:
            thumb = cert.fingerprint(cert.signature_hash_algorithm).hex()
            (dirs["rejected"] / f"{thumb}.der").write_bytes(cert.public_bytes(serialization.Encoding.DER))
            raise

    return validate


# --------------------------------------------------------------------------- server


class InteropUserManager(UserManager):
    def get_user(self, iserver, username=None, password=None, certificate=None):
        if not username:
            # Anonymous (asyncua passes the channel certificate) or an X509 user token:
            # asyncua verified the token signature; any user certificate is accepted
            # (auto-accept, like the application certificates).
            # Admin: asyncua's SimpleRoleRuleset allows AddNodes/DeleteNodes to admins only,
            # and the interop tests exercise node management with the anonymous session.
            return User(role=UserRole.Admin)
        if username == USER_NAME and password == PASSWORD:
            return User(role=UserRole.Admin)
        return None  # asyncua answers BadUserAccessDenied


async def run_server(options: dict[str, str]) -> int:
    port = int(options["port"])
    kind = options.get("kind", "interop")
    if kind != "interop":
        raise ValueError(f"--kind {kind} is not supported by the asyncua peer")
    root = Path(options["pki"])
    name = "AsyncuaInteropServer"
    application_uri = f"urn:localhost:opcfoundation.org:{name}"
    cert_path, key_path = ensure_certificate(root, name, application_uri, server=True)
    if flag(options, "init-only", False):
        print("PEER-PKI-READY " + application_uri, flush=True)
        return EXIT_SUCCESS

    server = Server(user_manager=InteropUserManager())
    # asyncua binds ConditionType.ConditionRefresh(2) (RefreshStart/RefreshEnd events) only
    # on request; the value is the Severity of those events.
    server.iserver.bind_condition_methods = 100
    await server.init()
    # The interop server's 4 MB MaxMessageSize (InteropLimits of the 1.5.378 peer).
    # asyncua has no MaxArrayLength / MaxByteStringLength to configure.
    server.limits = TransportLimits(
        max_recv_buffer=65535, max_send_buffer=65535,
        max_chunk_count=math.ceil(4 * 1024 * 1024 / 65535), max_message_size=4 * 1024 * 1024)
    url = f"opc.tcp://localhost:{port}/{name}"
    server.set_endpoint(f"opc.tcp://0.0.0.0:{port}/{name}")
    server.set_server_name(name)
    await server.set_application_uri(application_uri)
    await server.set_build_info(
        product_uri="http://opcfoundation.org/UA/Interop/AsyncuaServer",
        manufacturer_name="FreeOpcUa",
        product_name="asyncua Interop Server",
        software_version=_asyncua_version(),
        build_number="0",
        build_date=datetime.datetime.now(datetime.timezone.utc),
    )
    server.set_security_policy([
        ua.SecurityPolicyType.NoSecurity,
        ua.SecurityPolicyType.Basic256Sha256_Sign,
        ua.SecurityPolicyType.Basic256Sha256_SignAndEncrypt,
        ua.SecurityPolicyType.Aes128Sha256RsaOaep_Sign,
        ua.SecurityPolicyType.Aes128Sha256RsaOaep_SignAndEncrypt,
        ua.SecurityPolicyType.Aes256Sha256RsaPss_Sign,
        ua.SecurityPolicyType.Aes256Sha256RsaPss_SignAndEncrypt,
    ])
    server.set_identity_tokens([ua.AnonymousIdentityToken, ua.UserNameIdentityToken, ua.X509IdentityToken])
    await server.load_certificate(str(cert_path))
    await server.load_private_key(str(key_path))
    server.set_certificate_validator(make_validator(root, flag(options, "autoaccept", True), peer_is_server=False))

    # The operation limit the 2.0 tests cross (InteropLimits.MaxNodesPerOperation).
    await server.get_node(ua.ObjectIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead).write_value(
        ua.Variant(100, ua.VariantType.UInt32))
    ns = await server.register_namespace(INTEROP_NAMESPACE)
    objects = server.nodes.objects
    folder = await objects.add_folder(ua.NodeId("Interop", ns), ua.QualifiedName("Interop", ns))

    async def var(name: str, value, vtype: ua.VariantType, writable: bool = True, datatype=None):
        node = await folder.add_variable(ua.NodeId(name, ns), ua.QualifiedName(name, ns), ua.Variant(value, vtype), datatype=datatype)
        # The 1.5.378 interop server organizes its variables; asyncua adds HasComponent.
        await folder.delete_reference(node, ua.ObjectIds.HasComponent)
        await folder.add_reference(node, ua.ObjectIds.Organizes)
        if writable:
            await node.set_writable()
        return node

    await var("Boolean", True, ua.VariantType.Boolean)
    await var("Int32", 42, ua.VariantType.Int32)
    await var("UInt64", 2**64 - 1, ua.VariantType.UInt64)
    await var("Double", 3.25, ua.VariantType.Double)
    await var("String", "legacy", ua.VariantType.String)
    await var("DateTime", datetime.datetime(2024, 1, 2, 3, 4, 5, tzinfo=datetime.timezone.utc), ua.VariantType.DateTime)
    await var("Guid", uuid.UUID("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01"), ua.VariantType.Guid)
    await var("ByteString", bytes([1, 2, 3, 4, 5]), ua.VariantType.ByteString)
    await var("LocalizedText", ua.LocalizedText("Hallo", "de"), ua.VariantType.LocalizedText)
    await var("QualifiedName", ua.QualifiedName("Name", ns), ua.VariantType.QualifiedName)
    await var("NodeId", ua.NodeId("Interop", ns), ua.VariantType.NodeId)
    arr = await var("Int32Array", [1, 2, 3], ua.VariantType.Int32)
    await arr.write_value_rank(ua.ValueRank.OneDimension)
    sarr = await var("StringArray", ["a", "b", "c"], ua.VariantType.String)
    await sarr.write_value_rank(ua.ValueRank.OneDimension)
    await var("Range", ua.Range(Low=0, High=100), ua.VariantType.ExtensionObject, datatype=ua.NodeId(ua.ObjectIds.Range))
    counter = await var("Counter", 0, ua.VariantType.Int32, writable=False)

    async def add(parent, a, b):
        return [ua.Variant(a.Value + b.Value, ua.VariantType.Int32)]

    await folder.add_method(
        ua.NodeId("Add", ns), ua.QualifiedName("Add", ns), add,
        [_argument("a", ua.ObjectIds.Int32), _argument("b", ua.ObjectIds.Int32)],
        [_argument("sum", ua.ObjectIds.Int32)],
    )

    # RaiseEvent(): one BaseEventType event through the Server object.
    event_generator = await server.get_event_generator(ua.ObjectIds.BaseEventType, ua.ObjectIds.Server)

    async def raise_event(parent):
        event_generator.event.SourceNode = folder.nodeid
        event_generator.event.SourceName = "Interop"
        event_generator.event.Severity = 500
        await event_generator.trigger(message="interop event")
        return []

    await folder.add_method(ua.NodeId("RaiseEvent", ns), ua.QualifiedName("RaiseEvent", ns), raise_event, [], [])

    async with server:
        print("PEER-INFO " + json.dumps({
            "stack": "asyncua", "version": _asyncua_version().split(" ")[1],
            "applicationUri": application_uri, "softwareVersion": _asyncua_version(),
        }), flush=True)
        print("PEER-SERVER-READY " + url, flush=True)
        print("LEGACY-SERVER-READY " + url, flush=True)
        stop = asyncio.Event()
        loop = asyncio.get_running_loop()
        threading.Thread(target=_watch_stdin, args=(loop, stop), daemon=True).start()
        value = 0
        while not stop.is_set():
            try:
                await asyncio.wait_for(stop.wait(), 0.1)
            except asyncio.TimeoutError:
                value += 1
                await counter.write_value(ua.Variant(value, ua.VariantType.Int32))
    print("PEER-SERVER-STOPPED", flush=True)
    return EXIT_SUCCESS


def _argument(name: str, datatype: int) -> ua.Argument:
    arg = ua.Argument()
    arg.Name = name
    arg.DataType = ua.NodeId(datatype)
    arg.ValueRank = -1
    arg.Description = ua.LocalizedText(name)
    return arg


def _watch_stdin(loop: asyncio.AbstractEventLoop, stop: asyncio.Event) -> None:
    # A thread, because asyncio cannot read a pipe on Windows' proactor loop portably.
    for line in sys.stdin:
        if line.strip().lower() == "stop":
            break
    loop.call_soon_threadsafe(stop.set)


def _asyncua_version() -> str:
    from importlib.metadata import version
    return "asyncua " + version("asyncua")


# --------------------------------------------------------------------------- client


class CheckRunner:
    def __init__(self) -> None:
        self.passed = 0
        self.failed = 0

    async def run(self, name: str, check) -> bool:
        started = time.monotonic()
        outcome, message = "Passed", ""
        try:
            await check()
            self.passed += 1
        except Exception as e:  # noqa: BLE001 - every failure is a check result
            self.failed += 1
            outcome = "Failed"
            message = f"{type(e).__name__} {e}"
        print("RESULT " + json.dumps({
            "check": name, "outcome": outcome, "message": message,
            "milliseconds": int((time.monotonic() - started) * 1000),
        }), flush=True)
        return outcome == "Passed"

    def finish(self) -> int:
        print(f"SUMMARY passed={self.passed} failed={self.failed}", flush=True)
        return EXIT_SUCCESS if self.failed == 0 else EXIT_CHECKS_FAILED


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


async def run_client(options: dict[str, str]) -> int:
    url = options["url"]
    policy = options.get("policy", "http://opcfoundation.org/UA/SecurityPolicy#None")
    mode = options.get("mode", "None")
    root = Path(options["pki"])
    name = "AsyncuaInteropClient"
    application_uri = f"urn:localhost:opcfoundation.org:{name}"
    cert_path, key_path = ensure_certificate(root, name, application_uri, server=False)
    if flag(options, "init-only", False):
        print("PEER-PKI-READY " + application_uri, flush=True)
        return EXIT_SUCCESS

    selected = [c for c in options.get("checks", ",".join(DEFAULT_CHECKS)).split(",") if c]
    unknown = [c for c in selected if c not in CHECKS and c not in ("Connect", "CloseSession")]
    if unknown:
        raise ValueError("unknown checks: " + ", ".join(unknown))
    expected_errors = [e for e in options.get("expect-connect-error", "").split(",") if e]
    deadline = float(options.get("timeout-seconds", "300"))

    if POLICIES.get(policy, "missing") == "missing":
        raise ValueError("unsupported --policy " + policy)
    validator = make_validator(root, flag(options, "autoaccept", True), peer_is_server=True)

    async def new_client(user: str | None = options.get("user"), password: str | None = options.get("password", "")) -> Client:
        """A client configured like the main one (endpoint, security, PKI); not connected."""
        client = Client(url, timeout=60)
        # Mirrors the 2.0 reference server fixture: the large-message checks are
        # bounded by the server, not by this client.
        client.max_messagesize = 16 * 1024 * 1024
        if options.get("token-lifetime"):
            client.secure_channel_timeout = int(options["token-lifetime"])
        client.application_uri = application_uri
        client.name = name
        if user:
            client.set_user(user)
            client.set_password(password or "")
        if POLICIES[policy] is not None:
            await client.set_security(
                POLICIES[policy], str(cert_path), str(key_path),
                mode=getattr(ua.MessageSecurityMode, mode),
            )
        client.certificate_validator = validator
        return client

    client = await new_client()
    checks = CheckRunner()
    connected = False

    async def connect():
        nonlocal connected
        try:
            await client.connect()
        except Exception as e:  # noqa: BLE001
            if expected_errors:
                actual = _status_code_name(e)
                require(actual in expected_errors, f"expected {','.join(expected_errors)}, the connect failed with {actual}: {e}")
                return
            raise
        connected = True
        require(not expected_errors, f"expected the connect to fail with {','.join(expected_errors)}, but it succeeded")

    async def body() -> None:
        await checks.run("Connect", connect)
        if not connected:
            return
        ctx = Context(client, options, new_client)
        try:
            for check_name, check in CHECKS.items():
                if check_name in selected:
                    await ctx.ensure_connected()
                    await checks.run(check_name, lambda c=check: c(ctx))
        finally:
            await checks.run("CloseSession", lambda: ctx.client.disconnect())

    try:
        await asyncio.wait_for(body(), deadline)
    except asyncio.TimeoutError:
        print(f"INFO the checks did not complete within {deadline} s", flush=True)
    return checks.finish()


def _status_code_name(e: Exception) -> str:
    code = getattr(e, "code", None)
    if code is not None:
        for key, value in vars(ua.StatusCodes).items():
            if value == code:
                return key
    return type(e).__name__


class Context:
    def __init__(self, client: Client, options: dict[str, str], new_client) -> None:
        self.client = client
        self.options = options
        self.new_client = new_client
        self.ns: int | None = None

    async def ensure_connected(self) -> None:
        """A failed check must not break the next ones: if the connection was lost,
        connect a new session for the remaining checks."""
        if self.client.uaclient.state is UaClientState.CONNECTED:
            return
        print(f"INFO the connection was lost (state {self.client.uaclient.state.value}); reconnecting", flush=True)
        try:
            await self.client.disconnect()
        except Exception:  # noqa: BLE001
            pass
        client = await self.new_client()
        await client.connect()
        self.client = client

    async def nsidx(self) -> int:
        if self.ns is None:
            self.ns = (await self.client.get_namespace_array()).index(REFERENCE_NAMESPACE)
        return self.ns

    async def node(self, name: str):
        return self.client.get_node(ua.NodeId(name, await self.nsidx()))

    async def id(self, name: str) -> ua.NodeId:
        return ua.NodeId(name, await self.nsidx())


async def check_namespace_array(c: Context) -> None:
    array = await c.client.get_namespace_array()
    require(REFERENCE_NAMESPACE in array, "reference server namespace is missing: " + ", ".join(array))


async def check_server_status(c: Context) -> None:
    status = await c.client.get_node(ua.ObjectIds.Server_ServerStatus).read_value()
    require(isinstance(status, ua.ServerStatusDataType), f"ServerStatus did not decode: {status!r}")
    require(status.State == ua.ServerState.Running, f"server state {status.State}")
    require(bool(status.BuildInfo.ProductUri), "BuildInfo is empty")


async def check_browse_objects(c: Context) -> None:
    names = [(await n.read_browse_name()).Name for n in await c.client.nodes.objects.get_children()]
    require("Server" in names, "Server object not found")
    require("CTT" in names, "CTT folder not found; children: " + ", ".join(names))


async def check_translate(c: Context) -> None:
    node = await c.client.nodes.objects.get_child(["0:Server", "0:ServerStatus", "0:CurrentTime"])
    require(node.nodeid == ua.NodeId(ua.ObjectIds.Server_ServerStatus_CurrentTime), f"unexpected target {node.nodeid}")


async def check_read_scalars(c: Context) -> None:
    names = ["Boolean", "Int32", "Double", "String", "DateTime", "Guid", "ByteString", "LocalizedText",
             "QualifiedName", "NodeId", "Arrays_Int32", "Arrays_String"]
    nodes = [await c.node("Scalar_Static_" + n) for n in names]
    values = await c.client.read_values(nodes)
    require(len(values) == len(nodes), f"result count {len(values)}")


async def check_read_attributes(c: Context) -> None:
    node = await c.node("Scalar_Static_Int32")
    require(await node.read_node_class() == ua.NodeClass.Variable, "NodeClass")
    require((await node.read_browse_name()).Name == "Scalar_Static_Int32", "BrowseName")
    require(await node.read_data_type() == ua.NodeId(ua.ObjectIds.Int32), "DataType")


async def check_write_read_back(c: Context) -> None:
    i32, s, darr = (await c.node("Scalar_Static_Int32"), await c.node("Scalar_Static_String"),
                    await c.node("Scalar_Static_Arrays_Double"))
    text = "written by asyncua äöü"
    await i32.write_value(ua.Variant(1234567, ua.VariantType.Int32))
    await s.write_value(ua.Variant(text, ua.VariantType.String))
    await darr.write_value(ua.Variant([1.5, -2.25, 1e300], ua.VariantType.Double))
    require(await i32.read_value() == 1234567, "Int32 read back")
    require(await s.read_value() == text, "String read back")
    require(await darr.read_value() == [1.5, -2.25, 1e300], "Double[] read back")


async def check_call_methods(c: Context) -> None:
    methods = await c.node("Methods")
    hello = await methods.call_method(await c.node("Methods_Hello"), ua.Variant("asyncua", ua.VariantType.String))
    require(hello == "hello asyncua", f"Hello returned {hello!r}")
    result = await methods.call_method(await c.node("Methods_Add"),
                                       ua.Variant(1.5, ua.VariantType.Float), ua.Variant(2, ua.VariantType.UInt32))
    require(result == 3.5, f"Add returned {result!r}")


async def check_subscription(c: Context) -> None:
    received = asyncio.Event()
    count = 0

    class Handler:
        def datachange_notification(self, node, val, data):
            nonlocal count
            count += 1
            if count >= 3:
                received.set()

    sub = await c.client.create_subscription(100, Handler())
    await sub.subscribe_data_change(c.client.get_node(ua.ObjectIds.Server_ServerStatus_CurrentTime), sampling_interval=100)
    try:
        await asyncio.wait_for(received.wait(), 15)
    except asyncio.TimeoutError:
        raise AssertionError(f"only {count} data change notifications in 15 s") from None
    finally:
        await sub.delete()


def _browse_description(node_id: ua.NodeId, reference_type: int = ua.ObjectIds.HierarchicalReferences) -> ua.BrowseDescription:
    desc = ua.BrowseDescription()
    desc.NodeId = node_id
    desc.BrowseDirection = ua.BrowseDirection.Forward
    desc.ReferenceTypeId = ua.NodeId(reference_type)
    desc.IncludeSubtypes = True
    desc.ResultMask = ua.BrowseResultMask.All
    return desc


async def _browse_first(c: Context, node_id: ua.NodeId, max_references: int) -> ua.BrowseResult:
    params = ua.BrowseParameters()
    # A null view timestamp, as asyncua's own browse sends; its default is "now".
    params.View.Timestamp = ua.get_win_epoch()
    params.NodesToBrowse = [_browse_description(node_id)]
    params.RequestedMaxReferencesPerNode = max_references
    result = (await c.client.uaclient.browse(params))[0]
    require(result.StatusCode.is_good(), f"browse of {node_id} returned {result.StatusCode}")
    return result


async def _browse_next(c: Context, continuation_point: bytes, release: bool) -> ua.BrowseResult:
    params = ua.BrowseNextParameters()
    params.ContinuationPoints = [continuation_point]
    params.ReleaseContinuationPoints = release
    return (await c.client.uaclient.browse_next(params))[0]


async def _browse(c: Context, node_id: ua.NodeId, max_references: int) -> list[ua.ReferenceDescription]:
    """Browses one node, following continuation points."""
    result = await _browse_first(c, node_id, max_references)
    references = list(result.References or [])
    while result.ContinuationPoint:
        result = await _browse_next(c, result.ContinuationPoint, False)
        require(result.StatusCode.is_good(), f"browse next of {node_id} returned {result.StatusCode}")
        references.extend(result.References or [])
    return references


async def _read(c: Context, node_ids: list[ua.NodeId], attribute: ua.AttributeIds) -> list[ua.DataValue]:
    results: list[ua.DataValue] = []
    for offset in range(0, len(node_ids), 500):
        params = ua.ReadParameters()
        for node_id in node_ids[offset:offset + 500]:
            rv = ua.ReadValueId()
            rv.NodeId = node_id
            rv.AttributeId = attribute
            params.NodesToRead.append(rv)
        results.extend(await c.client.uaclient.read(params))
    return results


async def _write(c: Context, node_id: ua.NodeId, variant: ua.Variant) -> ua.StatusCode:
    params = ua.WriteParameters()
    wv = ua.WriteValue()
    wv.NodeId = node_id
    wv.AttributeId = ua.AttributeIds.Value
    wv.Value = ua.DataValue(variant)
    params.NodesToWrite = [wv]
    return (await c.client.uaclient.write(params))[0]


async def check_complex_types(c: Context) -> None:
    """Loads the server's DataTypeDefinitions, decodes every variable below Objects whose
    data type is a custom type, and writes up to 50 of the structures back re-encoded."""
    created = await c.client.load_data_type_definitions()
    names = set(created.keys()) if isinstance(created, dict) else set()
    for required in ("ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields"):
        require(required in names, f"type {required} was not created; created {len(names)} types")

    variables: list[ua.NodeId] = []
    visited = {ua.NodeId(ua.ObjectIds.ObjectsFolder)}
    queue = [ua.NodeId(ua.ObjectIds.ObjectsFolder)]
    while queue and len(visited) < 30_000:
        node_id = queue.pop(0)
        if node_id == ua.NodeId(ua.ObjectIds.Server):
            continue
        for r in await _browse(c, node_id, 0):
            if r.NodeId.NamespaceUri or r.NodeId.ServerIndex:
                continue
            target = ua.NodeId(r.NodeId.Identifier, r.NodeId.NamespaceIndex, r.NodeId.NodeIdType)
            if target not in visited:
                visited.add(target)
                queue.append(target)
                if r.NodeClass == ua.NodeClass.Variable and target.NamespaceIndex != 0:
                    variables.append(target)
    data_types = await _read(c, variables, ua.AttributeIds.DataType)
    custom = [v for v, dt in zip(variables, data_types)
              if isinstance(dt.Value.Value, ua.NodeId) and dt.Value.Value.NamespaceIndex != 0]
    require(len(custom) >= 10, f"only {len(custom)} variables with a custom data type found")
    values = await _read(c, custom, ua.AttributeIds.Value)
    access = await _read(c, custom, ua.AttributeIds.UserAccessLevel)
    structures, undecoded, writes = 0, [], []
    for node_id, dv, level in zip(custom, values, access):
        if not dv.StatusCode.is_good() or dv.Value is None or dv.Value.VariantType != ua.VariantType.ExtensionObject:
            continue
        items = dv.Value.Value if isinstance(dv.Value.Value, list) else [dv.Value.Value]
        for item in [i for i in items if i is not None]:
            structures += 1
            if isinstance(item, ua.ExtensionObject):  # still opaque: not decoded
                undecoded.append(f"{node_id} ({item.TypeId})")
        if len(writes) < 50 and (level.Value.Value or 0) & 2 and items:
            writes.append((node_id, dv.Value))
    require(structures > 0, "no structure values were read")
    require(not undecoded, f"{len(undecoded)} of {structures} structures were not decoded: {'; '.join(undecoded[:10])}")
    require(writes, "no writable structure variable found")
    rejected = []
    for node_id, variant in writes:
        try:
            status = await _write(c, node_id, variant)
            if not status.is_good():
                rejected.append(f"{node_id}: {status.name}")
        except ua.UaStatusCodeError as e:
            rejected.append(f"{node_id}: {_status_code_name(e)} for the request")
    require(not rejected, f"{len(rejected)} of {len(writes)} structure writes were rejected: {'; '.join(rejected[:10])}")
    read_back = await _read(c, [w[0] for w in writes], ua.AttributeIds.Value)
    changed = [str(w[0]) for w, dv in zip(writes, read_back) if dv.Value.Value != w[1].Value]
    require(not changed, f"{len(changed)} structures changed in a write and read round trip: {'; '.join(changed[:10])}")


async def _write_and_compare(c: Context, node_id: ua.NodeId, variant: ua.Variant) -> None:
    status = await _write(c, node_id, variant)
    require(status.is_good(), f"write of {node_id} returned {status.name}")
    dv = (await _read(c, [node_id], ua.AttributeIds.Value))[0]
    require(dv.StatusCode.is_good(), f"read of {node_id} returned {dv.StatusCode}")
    require(dv.Value.Value == variant.Value, f"{node_id} read back a different value")


async def check_large_array(c: Context) -> None:
    # 200 000 Int32 = 800 kB, far more than one 64 kB message chunk.
    values = [((i * 7919) ^ 0x5A5A) & 0x7FFFFFFF for i in range(200_000)]
    await _write_and_compare(c, ua.NodeId("Scalar_Static_Arrays_Int32", await c.nsidx()), ua.Variant(values, ua.VariantType.Int32))


async def check_large_bytestring(c: Context) -> None:
    # 3 MB, below the 4 MB ByteString limit of the reference server fixture.
    value = random.Random(4711).randbytes(3 * 1024 * 1024)
    await _write_and_compare(c, ua.NodeId("Scalar_Static_ByteString", await c.nsidx()), ua.Variant(value, ua.VariantType.ByteString))


async def check_oversized_request(c: Context) -> None:
    """A ByteString above the server's MaxByteStringLength but inside its MaxMessageSize must be
    rejected with a status code; the session stays usable."""
    node_id = ua.NodeId("Scalar_Static_ByteString", await c.nsidx())
    try:
        status = await _write(c, node_id, ua.Variant(bytes(5 * 1024 * 1024), ua.VariantType.ByteString))
        require(not status.is_good(), "a 5 MB ByteString write was accepted")
        result = status.name
    except ua.UaStatusCodeError as e:
        result = _status_code_name(e)
    state = (await _read(c, [ua.NodeId(ua.ObjectIds.Server_ServerStatus_State)], ua.AttributeIds.Value))[0]
    require(state.StatusCode.is_good(), f"the session is unusable after the rejected write ({result}): {state.StatusCode}")
    print("INFO OversizedRequestRejected: " + result, flush=True)


async def check_browse_continuation(c: Context) -> None:
    """Browses a folder three references at a time with BrowseNext, compares with a browse without
    limit, then releases an open continuation point."""
    folder = ua.NodeId("Scalar_Static", await c.nsidx())
    full = [r.NodeId for r in await _browse(c, folder, 0)]
    require(len(full) > 10, f"only {len(full)} references below {folder}")
    paged = [r.NodeId for r in await _browse(c, folder, 3)]
    require(paged == full, f"paged browse returned {len(paged)} of {len(full)} references or a different order")
    first = await _browse_first(c, folder, 3)
    require(bool(first.ContinuationPoint), "no continuation point returned")
    released = await _browse_next(c, first.ContinuationPoint, True)
    require(released.StatusCode.is_good(), f"releasing the continuation point returned {released.StatusCode}")
    reused = await _browse_next(c, first.ContinuationPoint, False)
    require(reused.StatusCode.value == ua.StatusCodes.BadContinuationPointInvalid,
            f"a released continuation point returned {reused.StatusCode}")


async def check_read_many_nodes(c: Context) -> None:
    """Reads 2 * MaxNodesPerRead + 1 nodes split by the server's limit, then in one request, which
    the server must reject with BadTooManyOperations."""
    limit_dv = (await _read(c, [ua.NodeId(ua.ObjectIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead)],
                            ua.AttributeIds.Value))[0]
    limit = int(limit_dv.Value.Value or 0) if limit_dv.Value else 0
    require(limit > 0, "the server published no MaxNodesPerRead")
    nodes = [ua.NodeId(ua.ObjectIds.Server_ServerStatus_State)] * (limit * 2 + 1)
    good = 0
    for offset in range(0, len(nodes), limit):
        params = ua.ReadParameters()
        for node_id in nodes[offset:offset + limit]:
            rv = ua.ReadValueId()
            rv.NodeId = node_id
            rv.AttributeId = ua.AttributeIds.Value
            params.NodesToRead.append(rv)
        good += sum(1 for dv in await c.client.uaclient.read(params) if dv.StatusCode.is_good())
    require(good == len(nodes), f"{good} of {len(nodes)} reads split by MaxNodesPerRead {limit} succeeded")
    params = ua.ReadParameters()
    for node_id in nodes:
        rv = ua.ReadValueId()
        rv.NodeId = node_id
        rv.AttributeId = ua.AttributeIds.Value
        params.NodesToRead.append(rv)
    try:
        await c.client.uaclient.read(params)
        too_many = "Good"
    except ua.UaStatusCodeError as e:
        too_many = _status_code_name(e)
    require(too_many == "BadTooManyOperations",
            f"reading {len(nodes)} nodes in one request with MaxNodesPerRead {limit} returned {too_many}")


async def check_token_renewal(c: Context) -> None:
    """Keeps reading for longer than the revised token lifetime (renewal at 75 %)."""
    seconds = int(c.options.get("token-test-seconds", "65"))
    started = time.monotonic()
    reads = 0
    while time.monotonic() - started < seconds:
        dv = (await _read(c, [ua.NodeId(ua.ObjectIds.Server_ServerStatus_CurrentTime)], ua.AttributeIds.Value))[0]
        require(dv.StatusCode.is_good(), f"read {reads} after {time.monotonic() - started:.0f} s returned {dv.StatusCode}")
        reads += 1
        await asyncio.sleep(0.5)


# --------------------------------------------------------------------------- feature checks (wave 2)

ALARMS_NAMESPACE = "http://test.org/UA/Alarms/"
EVENT_WAIT = 10.0
BASE_EVENT_FIELDS = ("EventId", "EventType", "SourceNode", "Time", "Message", "Severity")


class Collector:
    """Subscription handler that records data changes (client handle, DataValue) and
    the raw event field lists."""

    def __init__(self) -> None:
        self.values: list[tuple[int, ua.DataValue]] = []
        self.events: list[list] = []

    def datachange_notification(self, node, val, data) -> None:
        self.values.append((data.monitored_item.ClientHandle, data.monitored_item.Value))

    def event_notification(self, event) -> None:
        self.events.append([f.Value for f in event.event_fields])

    def status_change_notification(self, status) -> None:
        pass

    def of(self, handle: int) -> list[ua.DataValue]:
        return [dv for h, dv in self.values if h == handle]


_client_handles = iter(range(50_000, 1_000_000))


async def _subscription(c: Context, handler: Collector, publishing_interval: float = 100) -> Subscription:
    params = ua.CreateSubscriptionParameters()
    params.RequestedPublishingInterval = publishing_interval
    params.RequestedLifetimeCount = 100
    params.RequestedMaxKeepAliveCount = 10
    params.MaxNotificationsPerPublish = 0
    params.PublishingEnabled = True
    return await c.client.create_subscription(params, handler)


async def _data_item(sub: Subscription, node_id: ua.NodeId, sampling: float = 50, queue: int = 10,
                     discard_oldest: bool = True, mfilter=None,
                     mode: ua.MonitoringMode = ua.MonitoringMode.Reporting) -> tuple[int, int]:
    """Creates one data monitored item; returns (server id, client handle)."""
    rv = ua.ReadValueId()
    rv.NodeId = node_id
    rv.AttributeId = ua.AttributeIds.Value
    mp = ua.MonitoringParameters()
    mp.ClientHandle = next(_client_handles)
    mp.SamplingInterval = sampling
    mp.QueueSize = queue
    mp.DiscardOldest = discard_oldest
    if mfilter is not None:
        mp.Filter = mfilter
    mir = ua.MonitoredItemCreateRequest()
    mir.ItemToMonitor = rv
    mir.MonitoringMode = mode
    mir.RequestedParameters = mp
    result = (await sub.create_monitored_items([mir]))[0]
    require(isinstance(result, int), f"monitored item on {node_id} returned {result}")
    return result, mp.ClientHandle


async def _delete_subscription(sub: Subscription) -> None:
    try:
        await sub.delete()
    except Exception:  # noqa: BLE001 - the check already reported its outcome
        pass


def _sao(type_id: int, *path: str, attribute: ua.AttributeIds = ua.AttributeIds.Value) -> ua.SimpleAttributeOperand:
    op = ua.SimpleAttributeOperand()
    op.TypeDefinitionId = ua.NodeId(type_id)
    op.BrowsePath = [ua.QualifiedName(p, 0) for p in path]
    op.AttributeId = attribute
    return op


def _event_filter(of_type: int, condition: bool = False) -> ua.EventFilter:
    evfilter = ua.EventFilter()
    evfilter.SelectClauses = [_sao(ua.ObjectIds.BaseEventType, f) for f in BASE_EVENT_FIELDS]
    if condition:
        evfilter.SelectClauses += [
            _sao(ua.ObjectIds.ConditionType, attribute=ua.AttributeIds.NodeId),
            _sao(ua.ObjectIds.AcknowledgeableConditionType, "AckedState", "Id"),
            _sao(ua.ObjectIds.ConditionType, "Retain"),
        ]
    element = ua.ContentFilterElement()
    element.FilterOperator = ua.FilterOperator.OfType
    element.FilterOperands = [ua.LiteralOperand(Value=ua.Variant(ua.NodeId(of_type), ua.VariantType.NodeId))]
    evfilter.WhereClause = ua.ContentFilter(Elements=[element])
    return evfilter


async def _event_subscription(c: Context, notifier: ua.NodeId, condition: bool = False) -> tuple[Subscription, Collector]:
    handler = Collector()
    sub = await _subscription(c, handler)
    try:
        of_type = ua.ObjectIds.AcknowledgeableConditionType if condition else ua.ObjectIds.BaseEventType
        await sub.subscribe_events(notifier, evfilter=_event_filter(of_type, condition), queuesize=100)
    except Exception:
        await _delete_subscription(sub)
        raise
    return sub, handler


async def _wait_for(predicate, timeout: float = EVENT_WAIT):
    deadline = time.monotonic() + timeout
    while True:
        found = predicate()
        if found or time.monotonic() >= deadline:
            return found
        await asyncio.sleep(0.1)


async def _call(c: Context, object_id: ua.NodeId, method_id: ua.NodeId, *arguments: ua.Variant) -> ua.CallMethodResult:
    request = ua.CallMethodRequest()
    request.ObjectId = object_id
    request.MethodId = method_id
    request.InputArguments = list(arguments)
    return (await c.client.uaclient.call([request]))[0]


async def _write_value(c: Context, node_id: ua.NodeId, variant: ua.Variant) -> None:
    status = await _write(c, node_id, variant)
    require(status.is_good(), f"write of {node_id} returned {status.name}")


async def check_event_subscription(c: Context) -> None:
    sub, handler = await _event_subscription(c, ua.NodeId(ua.ObjectIds.Server))
    try:
        await _write_value(c, await c.id("NodeIds_Events_TriggerNode01"), ua.Variant(1, ua.VariantType.Int32))
        received = await _wait_for(lambda: next((e for e in handler.events
                                                 if isinstance(e[4], ua.LocalizedText) and "Trigger event" in (e[4].Text or "")), None))
        require(received is not None, f"no trigger event in {EVENT_WAIT} s; received {len(handler.events)} events")
        require(isinstance(received[0], bytes) and len(received[0]) > 0, "the event has no EventId")
        require(isinstance(received[5], int) and received[5] > 0, "the event has no Severity")
    finally:
        await _delete_subscription(sub)


async def check_condition_refresh(c: Context) -> None:
    sub, handler = await _event_subscription(c, ua.NodeId(ua.ObjectIds.Server))
    try:
        result = await _call(c, ua.NodeId(ua.ObjectIds.ConditionType), ua.NodeId(ua.ObjectIds.ConditionType_ConditionRefresh),
                             ua.Variant(sub.subscription_id, ua.VariantType.UInt32))
        require(result.StatusCode.is_good(), f"ConditionRefresh returned {result.StatusCode.name}")
        end = await _wait_for(lambda: any(e[1] == ua.NodeId(ua.ObjectIds.RefreshEndEventType) for e in handler.events))
        require(end, "no RefreshEndEvent received")
        require(any(e[1] == ua.NodeId(ua.ObjectIds.RefreshStartEventType) for e in handler.events),
                "no RefreshStartEvent received")
    finally:
        await _delete_subscription(sub)


async def check_alarm_acknowledge(c: Context) -> None:
    array = await c.client.get_namespace_array()
    require(ALARMS_NAMESPACE in array, "the Alarms namespace is missing")
    alarms_ns = array.index(ALARMS_NAMESPACE)
    folder = ua.NodeId("Alarms", alarms_ns)
    sub, handler = await _event_subscription(c, folder, condition=True)
    try:
        started = await _call(c, folder, ua.NodeId("Alarms.Start", alarms_ns), ua.Variant(30, ua.VariantType.UInt32))
        require(started.StatusCode.is_good(), f"Alarms.Start returned {started.StatusCode.name}")
        # Fields: 0 EventId, 1 EventType, 2 SourceNode, 3 Time, 4 Message, 5 Severity,
        # 6 ConditionId, 7 AckedState/Id, 8 Retain.
        unacked = await _wait_for(lambda: next((e for e in handler.events
                                                if isinstance(e[6], ua.NodeId) and not e[6].is_null()
                                                and e[7] is False and e[8] is True), None), 20)
        if unacked is None:
            last = handler.events[-1] if handler.events else None
            raise AssertionError(f"no unacknowledged condition in 20 s; received {len(handler.events)} events, last: {last!r}")
        ack = await _call(c, unacked[6], ua.NodeId(ua.ObjectIds.AcknowledgeableConditionType_Acknowledge),
                          ua.Variant(unacked[0], ua.VariantType.ByteString),
                          ua.Variant(ua.LocalizedText("acknowledged by the interop test"), ua.VariantType.LocalizedText))
        require(ack.StatusCode.is_good(), f"Acknowledge returned {ack.StatusCode.name}")
    finally:
        try:
            await _call(c, folder, ua.NodeId("Alarms.End", alarms_ns))
        except Exception:  # noqa: BLE001
            pass
        await _delete_subscription(sub)


async def check_deadband_filter(c: Context) -> None:
    analog = await c.id("DataAccess_AnalogType_Double")
    for deadband_type, label in ((ua.DeadbandType.Absolute, "Absolute"), (ua.DeadbandType.Percent, "Percent")):
        await _write_value(c, analog, ua.Variant(0.0, ua.VariantType.Double))
        handler = Collector()
        sub = await _subscription(c, handler)
        try:
            dcf = ua.DataChangeFilter()
            dcf.Trigger = ua.DataChangeTrigger.StatusValue
            dcf.DeadbandType = int(deadband_type)
            dcf.DeadbandValue = 10.0  # 10 units, or 10 % of the EURange 0..100
            _, handle = await _data_item(sub, analog, mfilter=dcf)
            await asyncio.sleep(0.5)
            for v in (5.0, 20.0, 25.0, 40.0):
                await _write_value(c, analog, ua.Variant(v, ua.VariantType.Double))
                await asyncio.sleep(0.4)
            await asyncio.sleep(0.8)
            seen = [dv.Value.Value for dv in handler.of(handle) if dv.Value is not None]
            require(20.0 in seen and 40.0 in seen, f"{label}: 20 and 40 not reported ({seen})")
            require(5.0 not in seen and 25.0 not in seen, f"{label}: changes inside the deadband reported ({seen})")
        finally:
            await _delete_subscription(sub)


async def check_queue_overflow(c: Context) -> None:
    node = await c.id("Scalar_Static_Int32")
    await _write_value(c, node, ua.Variant(0, ua.VariantType.Int32))
    handler = Collector()
    sub = await _subscription(c, handler, publishing_interval=2000)
    try:
        _, handle = await _data_item(sub, node, sampling=0, queue=2, discard_oldest=True)
        await asyncio.sleep(2.5)  # let the initial value go out first
        handler.values.clear()
        for ii in range(1, 6):
            await _write_value(c, node, ua.Variant(ii, ua.VariantType.Int32))
        await asyncio.sleep(3.0)
        seen = handler.of(handle)
        text = ", ".join(f"{dv.Value.Value if dv.Value else None}:0x{dv.StatusCode.value:08X}" for dv in seen)
        require(len(seen) == 2, f"expected the last 2 of 5 values, got {len(seen)} ({text})")
        require(seen[0].Value.Value == 4 and seen[1].Value.Value == 5, f"expected 4 and 5, got {text}")
        require(any(dv.StatusCode.value & 0x0480 == 0x0480 for dv in seen), f"no value carries the Overflow bit ({text})")
    finally:
        await _delete_subscription(sub)


async def _set_triggering(c: Context, subscription_id: int, triggering_id: int, links_to_add: list[int]) -> ua.SetTriggeringResult:
    # asyncua 2.0.1 has the SetTriggering request types but no client method: send the
    # stack-encoded request through the session.
    request = ua.SetTriggeringRequest()
    request.Parameters.SubscriptionId = subscription_id
    request.Parameters.TriggeringItemId = triggering_id
    request.Parameters.LinksToAdd = links_to_add
    request.Parameters.LinksToRemove = []
    data = await c.client.uaclient.session._send_request(request)
    response = struct_from_binary(ua.SetTriggeringResponse, data)
    response.ResponseHeader.ServiceResult.check()
    return response.Parameters


async def check_triggering(c: Context) -> None:
    handler = Collector()
    sub = await _subscription(c, handler, publishing_interval=100)
    try:
        int_node, str_node = await c.id("Scalar_Static_Int32"), await c.id("Scalar_Static_String")
        trigger_id, _ = await _data_item(sub, int_node, sampling=50, mode=ua.MonitoringMode.Reporting)
        linked_id, linked_handle = await _data_item(sub, str_node, sampling=50, mode=ua.MonitoringMode.Sampling)
        result = await _set_triggering(c, sub.subscription_id, trigger_id, [linked_id])
        require(len(result.AddResults) == 1 and result.AddResults[0].is_good(),
                f"SetTriggering returned {[s.name for s in result.AddResults]}")
        await asyncio.sleep(0.5)
        before = len(handler.of(linked_handle))
        await _write_value(c, str_node, ua.Variant("linked " + uuid.uuid4().hex, ua.VariantType.String))
        await asyncio.sleep(0.5)
        require(len(handler.of(linked_handle)) == before, "the sampling item reported without its trigger")
        await _write_value(c, int_node, ua.Variant(random.randint(1, 2**31 - 1), ua.VariantType.Int32))
        await asyncio.sleep(1.0)
        require(len(handler.of(linked_handle)) > before, "the triggered item did not report")
    finally:
        await _delete_subscription(sub)


async def _republish_status(c: Context, subscription_id: int, sequence: int) -> str:
    try:
        await c.client.uaclient.session.republish(subscription_id, sequence)
        return "Good"
    except ua.UaStatusCodeError as e:
        return _status_code_name(e)


async def check_republish(c: Context) -> None:
    handler = Collector()
    sub = await _subscription(c, handler)
    try:
        await _data_item(sub, await c.id("Scalar_Static_Int32"))
        not_sent = await _republish_status(c, sub.subscription_id, (sub.last_sequence_number or 0) + 1000)
        require(not_sent == "BadMessageNotAvailable", f"Republish of an unsent message returned {not_sent}")
        unknown = await _republish_status(c, sub.subscription_id + 100_000, 1)
        require(unknown == "BadSubscriptionIdInvalid", f"Republish of an unknown subscription returned {unknown}")
    finally:
        await _delete_subscription(sub)


async def check_transfer_subscription(c: Context) -> None:
    node = await c.id("Scalar_Static_Int32")
    handler = Collector()
    sub = await _subscription(c, handler)
    target = await c.new_client(None, None)
    try:
        await _data_item(sub, node)
        await target.connect()
        # The receiving Subscription object of session B: same id and monitored items.
        handler_b = Collector()
        transferred = Subscription(target.uaclient.session, sub.parameters, handler_b)
        transferred.subscription_id = sub.subscription_id
        transferred._monitored_items = dict(sub._monitored_items)
        params = ua.TransferSubscriptionsParameters()
        params.SubscriptionIds = [sub.subscription_id]
        params.SendInitialValues = True
        results = await target.uaclient.transfer_subscriptions(params)
        require(len(results) == 1 and results[0].StatusCode.is_good(),
                f"TransferSubscriptions returned {[r.StatusCode.name for r in results]}")
        # Session A no longer owns it: keep asyncua's stale watchdog from recreating it there.
        sub._deleted = True
        target.uaclient.session._subscription_callbacks[sub.subscription_id] = transferred.publish_callback
        target.uaclient.session.ensure_publish_loop()
        await asyncio.sleep(0.3)
        before = len(handler_b.values)
        status = await target.uaclient.write(_write_params(node, ua.Variant(random.randint(1, 2**31 - 1), ua.VariantType.Int32)))
        require(status[0].is_good(), f"write returned {status[0].name}")
        reported = await _wait_for(lambda: len(handler_b.values) > before)
        require(reported, "the transferred subscription reported nothing")
    finally:
        try:
            await target.disconnect()  # CloseSession(deleteSubscriptions=true)
        except Exception:  # noqa: BLE001
            pass
        await _delete_subscription(sub)


def _write_params(node_id: ua.NodeId, variant: ua.Variant, index_range: str | None = None) -> ua.WriteParameters:
    params = ua.WriteParameters()
    wv = ua.WriteValue()
    wv.NodeId = node_id
    wv.AttributeId = ua.AttributeIds.Value
    wv.IndexRange = index_range
    wv.Value = ua.DataValue(variant)
    params.NodesToWrite = [wv]
    return params


async def check_wrong_password(c: Context) -> None:
    session = await c.new_client("user1", "wrong password")
    try:
        await session.connect()
    except ua.UaStatusCodeError as e:
        name = _status_code_name(e)
        require(name in ("BadUserAccessDenied", "BadIdentityTokenRejected"), f"the wrong password was rejected with {name}")
        return
    try:
        await session.disconnect()
    except Exception:  # noqa: BLE001
        pass
    raise AssertionError("a session with a wrong password was activated")


def _user_certificate():
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "InteropUser"),
                         x509.NameAttribute(NameOID.ORGANIZATION_NAME, "OPC Foundation")])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (x509.CertificateBuilder()
            .subject_name(subject).issuer_name(subject).public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - datetime.timedelta(days=1)).not_valid_after(now + datetime.timedelta(days=365))
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
            .add_extension(x509.KeyUsage(digital_signature=True, content_commitment=True, key_encipherment=True,
                                         data_encipherment=True, key_agreement=False, key_cert_sign=False,
                                         crl_sign=False, encipher_only=False, decipher_only=False), critical=True)
            .add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.CLIENT_AUTH]), critical=False)
            .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
            .sign(key, hashes.SHA256()))
    return cert, key


async def check_x509_user_token(c: Context) -> None:
    cert, key = _user_certificate()
    session = await c.new_client(None, None)
    session.user_certificate = cert
    session.user_private_key = key
    await session.connect()
    try:
        dv = (await session.uaclient.read(_read_params([ua.NodeId(ua.ObjectIds.Server_ServerStatus_State)])))[0]
        require(dv.StatusCode.is_good(), f"reading with the X509 user returned {dv.StatusCode.name}")
    finally:
        await session.disconnect()


def _read_params(node_ids: list[ua.NodeId], index_range: str | None = None) -> ua.ReadParameters:
    params = ua.ReadParameters()
    for node_id in node_ids:
        rv = ua.ReadValueId()
        rv.NodeId = node_id
        rv.AttributeId = ua.AttributeIds.Value
        rv.IndexRange = index_range
        params.NodesToRead.append(rv)
    return params


async def check_register_nodes(c: Context) -> None:
    registered = await c.client.uaclient.register_nodes([await c.id("Scalar_Static_Int32"), await c.id("Scalar_Static_String")])
    require(len(registered) == 2, f"RegisterNodes returned {len(registered)} ids")
    values = await c.client.uaclient.read(_read_params(registered))
    require(all(dv.StatusCode.is_good() for dv in values),
            "reading registered nodes returned " + ", ".join(dv.StatusCode.name for dv in values))
    await c.client.uaclient.unregister_nodes(registered)


async def check_history_read_raw(c: Context) -> None:
    node = await c.id("Scalar_Static_Double")
    now = datetime.datetime.now(datetime.timezone.utc)
    details = ua.ReadRawModifiedDetails()
    details.IsReadModified = False
    details.StartTime = now - datetime.timedelta(hours=3)
    details.EndTime = now
    details.NumValuesPerNode = 10
    details.ReturnBounds = False
    params = ua.HistoryReadParameters()
    params.HistoryReadDetails = details
    params.TimestampsToReturn = ua.TimestampsToReturn.Source
    params.ReleaseContinuationPoints = False
    params.NodesToRead = [ua.HistoryReadValueId(NodeId=node)]
    result = (await c.client.uaclient.history_read(params))[0]
    require(result.StatusCode.is_good(), f"HistoryRead returned {result.StatusCode.name}")
    data = result.HistoryData
    require(isinstance(data, ua.HistoryData) and data.DataValues, f"HistoryRead returned no values ({data!r})")
    require(len(data.DataValues) <= 10, f"{len(data.DataValues)} values despite NumValuesPerNode 10")
    if result.ContinuationPoint:
        params.ReleaseContinuationPoints = True
        params.NodesToRead = [ua.HistoryReadValueId(NodeId=node, ContinuationPoint=result.ContinuationPoint)]
        await c.client.uaclient.history_read(params)


async def check_node_management(c: Context) -> None:
    ns = await c.nsidx()
    name = "InteropAdded_" + uuid.uuid4().hex[:8]
    attributes = ua.ObjectAttributes()
    attributes.DisplayName = ua.LocalizedText(name)
    attributes.SpecifiedAttributes = int(ua.NodeAttributesMask.DisplayName)
    item = ua.AddNodesItem()
    item.ParentNodeId = ua.NodeId(ua.ObjectIds.ObjectsFolder)
    item.ReferenceTypeId = ua.NodeId(ua.ObjectIds.Organizes)
    item.RequestedNewNodeId = ua.NodeId(name, ns)
    item.BrowseName = ua.QualifiedName(name, ns)
    item.NodeClass = ua.NodeClass.Object
    item.NodeAttributes = attributes
    item.TypeDefinition = ua.NodeId(ua.ObjectIds.BaseObjectType)
    added = (await c.client.uaclient.add_nodes([item]))[0]
    require(added.StatusCode.is_good(), f"AddNodes returned {added.StatusCode.name}")
    refs = await _browse(c, ua.NodeId(ua.ObjectIds.ObjectsFolder), 0)
    require(any(r.BrowseName.Name == name for r in refs), "the added node is not organized by the Objects folder")
    params = ua.DeleteNodesParameters()
    params.NodesToDelete = [ua.DeleteNodesItem(NodeId=added.AddedNodeId, DeleteTargetReferences=True)]
    deleted = (await c.client.uaclient.delete_nodes(params))[0]
    require(deleted.is_good(), f"DeleteNodes returned {deleted.name}")


async def check_index_range(c: Context) -> None:
    node = await c.id("Scalar_Static_Arrays_Int32")
    await _write_value(c, node, ua.Variant(list(range(10)), ua.VariantType.Int32))
    status = (await c.client.uaclient.write(_write_params(node, ua.Variant([20, 30], ua.VariantType.Int32), "2:3")))[0]
    require(status.is_good(), f"writing the index range 2:3 returned {status.name}")
    dv = (await c.client.uaclient.read(_read_params([node], "1:4")))[0]
    require(dv.StatusCode.is_good(), f"reading the index range 1:4 returned {dv.StatusCode.name}")
    part = dv.Value.Value if dv.Value else None
    require(part == [1, 20, 30, 4], f"index range 1:4 read {part!r}")


async def check_find_servers(c: Context) -> None:
    server_uri = (await c.client.get_node(ua.ObjectIds.Server_ServerArray).read_value())[0]
    discovery = Client(c.client.server_url.geturl(), timeout=30)
    servers = await discovery.connect_and_find_servers()
    uris = [s.ApplicationUri for s in servers]
    require(server_uri in uris, f"FindServers does not list {server_uri}: {', '.join(uris)}")
    endpoints = await discovery.connect_and_get_server_endpoints()
    policy = c.client.security_policy
    require(any(e.SecurityPolicyUri == policy.URI and e.SecurityMode == policy.Mode for e in endpoints),
            f"GetEndpoints does not offer {policy.URI}/{policy.Mode.name}")


async def check_session_reconnect(c: Context) -> None:
    """ActivateSession of the existing session on a new secure channel, by asyncua's own
    reconnect (the auto-reconnect supervisor reactivates the session before creating one)."""
    client = c.client
    token = client.uaclient.session.authentication_token
    protocol = client.uaclient.protocol
    client._auto_reconnect = True
    try:
        async with client.uaclient.subscribe_state() as states:
            client.uaclient.notify_transport_lost()
            await states.wait_for_state(UaClientState.CONNECTED, timeout=30)
    except asyncio.TimeoutError:
        raise AssertionError(f"no reconnect in 30 s (state {client.uaclient.state.value})") from None
    finally:
        client._auto_reconnect = False
    require(client.uaclient.protocol is not protocol, "the reconnect did not open a new secure channel")
    require(client.uaclient.session.authentication_token == token,
            "the session was recreated instead of reactivated on the new channel")
    dv = (await client.uaclient.read(_read_params([ua.NodeId(ua.ObjectIds.Server_ServerStatus_State)])))[0]
    require(dv.StatusCode.is_good(), f"reading after the reconnect returned {dv.StatusCode.name}")


CHECKS = {
    "NamespaceArray": check_namespace_array,
    "ReadServerStatusStructure": check_server_status,
    "BrowseObjectsFolder": check_browse_objects,
    "TranslateBrowsePath": check_translate,
    "ReadScalars": check_read_scalars,
    "ReadAttributes": check_read_attributes,
    "WriteAndReadBack": check_write_read_back,
    "CallMethods": check_call_methods,
    "Subscription": check_subscription,
    "ComplexTypes": check_complex_types,
    "LargeArrayRoundTrip": check_large_array,
    "LargeByteStringRoundTrip": check_large_bytestring,
    "OversizedRequestRejected": check_oversized_request,
    "BrowseContinuationPoints": check_browse_continuation,
    "ReadManyNodes": check_read_many_nodes,
    "TokenRenewal": check_token_renewal,
    "EventSubscription": check_event_subscription,
    "ConditionRefresh": check_condition_refresh,
    "AlarmAcknowledge": check_alarm_acknowledge,
    "DeadbandFilter": check_deadband_filter,
    "QueueOverflow": check_queue_overflow,
    "Triggering": check_triggering,
    "Republish": check_republish,
    "TransferSubscription": check_transfer_subscription,
    "WrongPasswordRejected": check_wrong_password,
    "X509UserToken": check_x509_user_token,
    "RegisterNodes": check_register_nodes,
    "HistoryReadRaw": check_history_read_raw,
    "NodeManagement": check_node_management,
    "IndexRange": check_index_range,
    "FindServers": check_find_servers,
    "SessionReconnect": check_session_reconnect,  # last: it reconnects the session
}
DEFAULT_CHECKS = list(CHECKS)[:9]


# --------------------------------------------------------------------------- main


def main(argv: list[str]) -> int:
    # stdout carries the protocol; asyncua logs go to stderr.
    logging.basicConfig(stream=sys.stderr, level=logging.WARNING)
    if not argv:
        print("error: missing command", file=sys.stderr)
        return EXIT_USAGE
    try:
        options = parse_options(argv[1:])
    except ValueError as e:
        print("error: " + str(e), file=sys.stderr)
        return EXIT_USAGE
    try:
        if argv[0] == "server":
            return asyncio.run(run_server(options))
        if argv[0] == "client":
            return asyncio.run(run_client(options))
        print("error: unknown command " + argv[0], file=sys.stderr)
        return EXIT_USAGE
    except Exception as e:  # noqa: BLE001
        print(f"FATAL {type(e).__name__}: {e}", file=sys.stderr)
        return EXIT_FATAL


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
