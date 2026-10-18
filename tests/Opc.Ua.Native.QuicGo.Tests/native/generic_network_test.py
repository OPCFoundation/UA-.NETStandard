# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Real-socket acceptance of the production ABI, with independent native endpoints."""
import argparse
import asyncio
import base64
import ctypes as c
import hashlib
import json
import subprocess
import time
from pathlib import Path

U8, U32, U64, I32 = c.c_uint8, c.c_uint32, c.c_uint64, c.c_int32
P8 = c.POINTER(U8)


class Handle(c.Structure):
    _fields_ = [("value", U64)]


class Span(c.Structure):
    _fields_ = [("data", P8), ("length", U32), ("reserved", U32)]


class Identity(c.Structure):
    _fields_ = [(name, Span) for name in
                ("certificate_der", "issuer_chain_der", "private_key_pkcs8",
                 "trust_roots_der", "local_uri", "server_name")]


class Peer(c.Structure):
    _fields_ = [("application_uri", Span), ("spki_sha256", Span)]


class Config(c.Structure):
    _fields_ = [("size", U32), ("timeout_ms", U32), ("address", Span), ("alpn", Span),
                ("identity", Identity), ("peers", c.POINTER(Peer)), ("peer_count", U32),
                ("stream_limit", U32), ("idle_timeout_ms", U32), ("reserved", U32)]


class Request(c.Structure):
    _fields_ = [(n, U32) for n in ("size", "kind", "timeout_ms", "flags")] + [
        ("error_code", U64), ("payload", Span), ("capacity", U32), ("index", U32)]


class Result(c.Structure):
    _fields_ = [(n, U32) for n in ("size", "state", "kind", "cancel_requested")] + [
        ("status", I32), ("native_status", I32), ("written", U32), ("payload_length", U32)] + [
        (n, Handle) for n in ("listener", "connection", "stream")] + [
        (n, U64) for n in ("stream_id", "error_code", "final_size", "reliable_size", "reset_frame_type")] + [
        (n, U32) for n in ("maximum_datagram_payload", "remote", "outcome", "reserved")]


class Info(c.Structure):
    _fields_ = [(n, U32) for n in (
        "size", "tls_version", "is_client", "verified_chain_length", "handshake_complete",
        "used_0rtt", "resumed", "datagrams_local", "datagrams_remote", "reset09_local",
        "reset09_remote", "maximum_datagram_payload")] + [
        ("local_max_datagram_frame_size", U64), ("peer_max_datagram_frame_size", U64),
        ("alpn_length", U32), ("application_uri_length", U32),
        ("certificate_sha256", U8 * 32), ("spki_sha256", U8 * 32),
        ("alpn", U8 * 256), ("application_uri", U8 * 512)]
    _fields_ += [("handshake_confirmed", U32), ("mutual_tls_authenticated", U32),
                 ("local_certificate_used", U32), ("reserved", U32)]


class Counts(c.Structure):
    _fields_ = [(n, U32) for n in ("size", "operations", "terminal", "listeners", "connections",
                                  "streams", "native_connections", "transports")] + [("retained_bytes", U64)]


class Abi(c.Structure):
    _fields_ = [(n, U32) for n in ("size", "version", "pointer_size", "config_size",
                                  "request_size", "result_size", "info_size", "counts_size")]


def array(value):
    return (U8 * len(value)).from_buffer_copy(value)


def fixture(path, rsa=False):
    # The executable explicitly refuses terminal/regular-file stdout.
    process = subprocess.run([str(path.absolute())] + (["-rsa"] if rsa else []),
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True, timeout=30)
    value = json.loads(process.stdout)
    value["root"] = base64.b64decode(value["root"])
    for identity in value["identities"]:
        for field in ("cert", "key", "spki"):
            identity[field] = base64.b64decode(identity[field])
    return value


class Driver:
    def __init__(self, library):
        self.library = c.CDLL(str(library.absolute()))
        self.jobs, self.listeners, self.connections, self.streams = set(), set(), set(), set()
        self.checks, self.latencies = [], []

        def bind(name, parameters):
            fn = getattr(self.library, "opcua_quic_" + name)
            fn.argtypes, fn.restype = parameters, I32
            return fn

        self.abi = bind("abi", [c.POINTER(Abi)])
        self.listen = bind("listen_begin", [c.POINTER(Config), c.POINTER(Handle)])
        self.connect = bind("connect_begin", [c.POINTER(Config), c.POINTER(Handle)])
        self.begin_fns = [bind(k + "_begin", [Handle, c.POINTER(Request), c.POINTER(Handle)])
                          for k in ("listener", "connection", "stream")]
        self.poll = bind("poll", [Handle, c.POINTER(Result), P8, U32])
        self.cancel = bind("cancel", [Handle, U64])
        self.release = bind("release", [Handle])
        self.get_counts = bind("counts", [c.POINTER(Counts)])

    def check(self, name, **details):
        self.checks.append({"name": name, "passed": True, "details": details})
        print("PASS", name, flush=True)

    def counts(self):
        value = Counts(size=c.sizeof(Counts))
        assert self.get_counts(c.byref(value)) == 0
        return value

    def pending(self, operation):
        result = Result(size=c.sizeof(Result))
        return self.poll(operation, c.byref(result), None, 0) == 1

    def configuration(self, pki, local, peers, address, overrides=None, timeout=3000, alpn=b"h3"):
        storage = []

        def span(value):
            buf = array(value)
            storage.append(buf)
            return Span(buf if len(buf) else None, len(buf), 0)

        identity = pki["identities"][local]
        values = dict(zip((f[0] for f in Identity._fields_), [
            identity["cert"], b"", identity["key"], pki["root"], identity["uri"].encode(),
            b"" if local == 1 else b"localhost"]))
        values.update(overrides or {})
        material = Identity(**{k: span(v) for k, v in values.items()})
        rules = (Peer * len(peers))(*[
            Peer(span(pki["identities"][i]["uri"].encode()), span(pki["identities"][i]["spki"])) for i in peers])
        storage.append(rules)
        config = Config(c.sizeof(Config), timeout, span(address.encode()), span(alpn),
                        material, rules, len(peers), 32, 60000, 0)
        return config, storage

    def create(self, listening, config, storage, wipe=True):
        handle = Handle()
        start = time.monotonic()
        status = (self.listen if listening else self.connect)(c.byref(config), c.byref(handle))
        self.latencies.append(time.monotonic() - start)
        if wipe:
            for buf in storage:
                c.memset(buf, 0, c.sizeof(buf))
        if status == 0:
            assert handle.value
            self.jobs.add(handle.value)
        else:
            assert handle.value == 0
        return status, handle

    def begin(self, category, handle, kind, data=b"", flags=0, capacity=0, code=0, index=0, timeout=3000):
        buf, operation = array(data), Handle()
        request = Request(c.sizeof(Request), kind, timeout, flags, code,
                          Span(buf if len(buf) else None, len(buf), 0), capacity, index)
        start = time.monotonic()
        status = self.begin_fns[category - 1](Handle(handle), c.byref(request), c.byref(operation))
        self.latencies.append(time.monotonic() - start)
        c.memset(buf, 0xa5, len(buf))
        if status == 0:
            self.jobs.add(operation.value)
        else:
            assert operation.value == 0
        return status, operation

    async def finish(self, operation, expected=0, release=True):
        deadline = time.monotonic() + 65
        buf = (U8 * 65536)()
        while True:
            result = Result(size=c.sizeof(Result))
            start = time.monotonic()
            status = self.poll(operation, c.byref(result), buf, len(buf))
            self.latencies.append(time.monotonic() - start)
            if status != 1:
                break
            assert time.monotonic() < deadline, "native operation never terminated"
            await asyncio.sleep(0.001)
        assert status == 0, ("poll", status)
        if result.listener.value:
            self.listeners.add(result.listener.value)
        if result.connection.value:
            self.connections.add(result.connection.value)
        if result.stream.value:
            self.streams.add(result.stream.value)
        if release:
            assert self.release(operation) == 0
            self.jobs.remove(operation.value)
        assert expected is None or result.status == expected, (
            "terminal", result.kind, result.status, result.native_status)
        return result, bytes(buf[:result.payload_length])

    async def execute(self, category, handle, kind, expected=0, **kwargs):
        status, operation = self.begin(category, handle, kind, **kwargs)
        assert status == 0, ("begin", kind, status)
        result = await self.finish(operation, expected)
        if kind == 5:
            self.listeners.remove(handle)
        elif kind == 18:
            self.connections.remove(handle)
        elif kind == 27:
            self.streams.remove(handle)
        return result

    async def start_listener(self, pki, peers=(0, 2), **kwargs):
        config, storage = self.configuration(pki, 1, peers, "127.0.0.1:0", **kwargs)
        status, operation = self.create(True, config, storage)
        assert status == 0
        result, _ = await self.finish(operation)
        listener = result.listener.value
        _, address = await self.execute(1, listener, 4)
        return listener, address.decode()

    async def connect_client(self, pki, listener, address, identity=0, **kwargs):
        status, accept = self.begin(1, listener, 3)
        assert status == 0
        config, storage = self.configuration(pki, identity, (1,), address, **kwargs)
        status, operation = self.create(False, config, storage)
        assert status == 0
        client, _ = await self.finish(operation)
        server, _ = await self.finish(accept)
        return client.connection.value, server.connection.value

    async def open_stream(self, client, server, uni=False, prefix=b"HEAD"):
        opened, _ = await self.execute(2, client, 12, flags=int(uni))
        status, accept = self.begin(2, server, 13, flags=int(uni))
        assert status == 0
        await self.execute(3, opened.stream.value, 20, data=prefix, flags=2)
        accepted, _ = await self.finish(accept)
        assert accepted.stream_id == opened.stream_id
        return opened.stream.value, accepted.stream.value, opened.stream_id

    async def cleanup(self):
        for job in list(self.jobs):
            self.cancel(Handle(job), 0)
        for job in list(self.jobs):
            await self.finish(Handle(job), None)
        for stream in list(self.streams):
            await self.execute(3, stream, 27)
        for conn in list(self.connections):
            await self.execute(2, conn, 18)
        for listener in list(self.listeners):
            await self.execute(1, listener, 5)
        for _ in range(5000):
            value = self.counts()
            if value.native_connections == 0 and value.transports == 0:
                break
            await asyncio.sleep(0.001)
        assert not any(getattr(value, n) for n, _ in Counts._fields_ if n != "size"), (
            "leaks", [(n, getattr(value, n)) for n, _ in Counts._fields_])


async def main(args):
    d = Driver(args.library)
    try:
        abi = Abi(size=c.sizeof(Abi))
        assert d.abi(c.byref(abi)) == 0 and abi.version == 4 and abi.pointer_size == 8
        for name, structure in (("config", Config), ("request", Request), ("result", Result),
                                ("info", Info), ("counts", Counts)):
            assert getattr(abi, name + "_size") == c.sizeof(structure)
        d.check("TypedAbiLayoutsAndProductionExports", config=c.sizeof(Config), result=c.sizeof(Result))
        pki = fixture(args.fixture)
        for missing in ("certificate_der", "private_key_pkcs8", "trust_roots_der", "local_uri", "server_name"):
            config, storage = d.configuration(pki, 0, (1,), "127.0.0.1:1", {missing: b""})
            assert d.create(False, config, storage)[0] == -1
        for peers in ((),):
            config, storage = d.configuration(pki, 1, peers, "127.0.0.1:0")
            assert d.create(True, config, storage)[0] == -1
        for field, value in (("timeout_ms", 0), ("timeout_ms", 60001), ("stream_limit", 0),
                             ("stream_limit", 33), ("idle_timeout_ms", 999), ("idle_timeout_ms", 600001),
                             ("peer_count", 33), ("reserved", 1), ("size", 0)):
            config, storage = d.configuration(pki, 1, (0,), "127.0.0.1:0")
            setattr(config, field, value)
            assert d.create(True, config, storage)[0] == -1, field
        assert d.counts().operations == 0
        d.check("MissingCredentialsAndAllConfigurationBoundsRefusedWithoutJobs")

        listener, address = await d.start_listener(pki)
        client, server = await d.connect_client(pki, listener, address)
        second_client, second_server = await d.connect_client(pki, listener, address, identity=2)
        for handle, peer, role in ((client, 1, 1), (server, 0, 0), (second_server, 2, 0)):
            _, payload = await d.execute(2, handle, 10)
            info = Info.from_buffer_copy(payload)
            assert info.tls_version == 0x304 and info.is_client == role and info.handshake_complete == 1
            assert info.used_0rtt == info.resumed == 0 and info.verified_chain_length == 2
            assert info.handshake_confirmed == info.mutual_tls_authenticated == 1
            assert info.local_certificate_used == 1 and info.reserved == 0
            assert (info.datagrams_local, info.datagrams_remote, info.reset09_local, info.reset09_remote) == (1, 1, 1, 1)
            assert info.local_max_datagram_frame_size == info.peer_max_datagram_frame_size == 16383
            assert bytes(info.alpn[:info.alpn_length]) == b"h3"
            identity = pki["identities"][peer]
            assert bytes(info.application_uri[:info.application_uri_length]).decode() == identity["uri"]
            assert bytes(info.certificate_sha256) == hashlib.sha256(identity["cert"]).digest()
            assert bytes(info.spki_sha256) == identity["spki"]
            _, leaf = await d.execute(2, handle, 11)
            _, root = await d.execute(2, handle, 11, index=1)
            assert leaf == identity["cert"] and root == pki["root"]
            await d.execute(2, handle, 11, index=2, expected=-1)
        d.check("IndependentListenerTwoAuthorizedClientsAndVerifiedTlsEvidence")
        d.check("CallerBuffersCopiedBeforeBeginReturns")

        assert d.begin(2, listener, 10)[0] == -1
        assert d.begin(1, client, 4)[0] == -1
        for kwargs in ({"flags": 1}, {"capacity": 1}, {"code": 1}, {"index": 1},
                       {"timeout": 0}, {"timeout": 60001}, {"data": b"wrong"}):
            assert d.begin(2, client, 10, **kwargs)[0] == -1
        d.check("TypedStaleHandlesAndIrrelevantRequestFieldsRejected")

        status, info_job = d.begin(2, client, 10)
        assert status == 0
        while True:
            result = Result(size=c.sizeof(Result))
            status = d.poll(info_job, c.byref(result), None, 0)
            if status != 1:
                break
            await asyncio.sleep(0.001)
        assert status == -7 and result.payload_length == c.sizeof(Info)
        assert d.release(info_job) == -11
        first, payload = await d.finish(info_job, release=False)
        assert d.cancel(info_job, 17) == 0
        second, again = await d.finish(info_job)
        assert bytes(first) == bytes(second) and payload == again
        assert d.release(info_job) == -1
        d.check("NonConsumingShortPollObservationAndLateCancellation")

        _, waiting = d.begin(1, listener, 3, timeout=10000)
        assert d.release(waiting) == 1 and d.begin(1, listener, 3)[0] == -11
        assert d.cancel(waiting, 7) == 0
        cancelled, _ = await d.finish(waiting, -10)
        assert cancelled.cancel_requested == 1
        await d.execute(1, listener, 3, timeout=20, expected=-8)
        d.check("AcceptCancellationTimeoutAndConflictingAdmission")

        sender, receiver, native_id = await d.open_stream(client, server)
        assert native_id == 0
        assert d.begin(2, client, 18)[0] == -11
        _, prefix = await d.execute(3, receiver, 21, capacity=4)
        assert prefix == b"HEAD"
        _, pending_read = d.begin(3, receiver, 21, capacity=8)
        assert d.begin(3, receiver, 21, capacity=1)[0] == -11
        assert d.begin(3, receiver, 27)[0] == -11
        assert d.cancel(pending_read, 9) == 0
        await d.finish(pending_read, -10)
        sent, _ = await d.execute(3, sender, 20, data=b"after-cancel", flags=1)
        assert sent.written == len(b"after-cancel")
        received = b""
        while True:
            read, data = await d.execute(3, receiver, 21, capacity=100)
            received += data
            if read.outcome == 1:
                break
        assert received == b"after-cancel"
        await d.execute(3, receiver, 20, data=b"reverse", flags=1)
        _, data = await d.execute(3, sender, 21, capacity=100)
        assert data == b"reverse"
        d.check("ReadCancellationFinHalfCloseAndBidirectionalIsolation")

        for expected_id in (2, 6, 10):
            s, r, native_id = await d.open_stream(client, server, uni=True)
            assert native_id == expected_id
            assert d.begin(3, s, 21, capacity=1)[0] == -1
            assert d.begin(3, r, 20, data=b"wrong-direction")[0] == -1
            await d.execute(3, s, 20, data=b"uni", flags=1)
            got = b""
            while True:
                result, data = await d.execute(3, r, 21, capacity=64)
                got += data
                if result.outcome == 1:
                    break
            assert got == b"HEADuni"
        d.check("NativeUnidirectionalIdsAndDirectionEnforcement")

        large, _ = await d.execute(2, client, 14, data=bytes(65536), expected=-4)
        maximum = large.maximum_datagram_payload
        assert 0 < maximum < 65536
        # PMTU may increase. The 65536 rejection above is authoritative; use the
        # returned current limit, never a hardcoded stream-framing substitute.
        await d.execute(2, client, 14, data=b"D" * maximum)
        _, datagram = await d.execute(2, server, 15)
        assert datagram == b"D" * maximum
        adjacent, _ = await d.execute(2, client, 14, data=b"A" * (maximum + 1), expected=None)
        if adjacent.status == 0:
            # A successful adjacent send is only legitimate if native PMTU grew.
            current, _ = await d.execute(2, client, 14, data=bytes(65536), expected=-4)
            assert current.maximum_datagram_payload > maximum
            _, delivered = await d.execute(2, server, 15)
            assert delivered == b"A" * (maximum + 1)
        else:
            assert adjacent.status == -4 and adjacent.maximum_datagram_payload <= maximum
        await d.execute(2, server, 14, data=b"")
        received, empty = await d.execute(2, client, 15)
        assert received.written == 0 and empty == b""
        _, pending = d.begin(2, server, 15)
        assert d.cancel(pending, 5) == 0
        await d.finish(pending, -10)
        d.check("RealDatagramNegotiatedSizeEmptyPayloadAndReceiveCancellation", maximum=maximum)

        s, r, _ = await d.open_stream(client, server)
        await d.execute(3, s, 20, data=bytes(60000))
        _, write = d.begin(3, s, 20, data=bytes(65536), timeout=10000)
        assert d.begin(3, s, 22, code=91)[0] == -11
        for _ in range(3000):
            offset, _ = await d.execute(3, s, 26)
            if offset.final_size == 65536:
                break
            await asyncio.sleep(0.001)
        assert offset.final_size == 65536
        assert d.cancel(write, 91) == 0
        partial, _ = await d.finish(write, -10)
        assert 0 < partial.written < 65536 and partial.native_status == -5
        assert partial.written + 60004 == 65536 and partial.error_code == 91
        reset, _ = await d.execute(3, s, 23)
        assert (reset.final_size, reset.reliable_size, reset.reset_frame_type, reset.error_code) == (65536, 4, 0x24, 91)
        got = b""
        while True:
            result, data = await d.execute(3, r, 21, capacity=65536)
            got += data
            if result.outcome == 2:
                assert result.final_size == 65536 and result.reliable_size == 4 and result.reset_frame_type == 0x24
                break
        assert got.startswith(b"HEAD") and len(got) >= 4
        d.check("PartialWriteCancellationPreservesNativeFinalSizeAndReset09", written=partial.written, final=reset.final_size)

        for _ in range(4):
            opened, _ = await d.execute(2, client, 12)
            _, accepting = d.begin(2, server, 13)
            _, prefix_job = d.begin(3, opened.stream.value, 20, data=b"BOUNDARY", flags=2)
            assert d.cancel(prefix_job, 77) == 0
            prefix_result, _ = await d.finish(prefix_job, None)
            assert prefix_result.written == 8 and prefix_result.status in (0, -10)
            assert prefix_result.native_status == 0
            if prefix_result.status == 0:
                await d.execute(3, opened.stream.value, 22, code=77)
            accepted, _ = await d.finish(accepting)
            reset, _ = await d.execute(3, opened.stream.value, 23)
            assert reset.reliable_size == 8 and reset.reset_frame_type == 0x24 and reset.final_size == 8
            got = b""
            while True:
                result, data = await d.execute(3, accepted.stream.value, 21, capacity=32)
                got += data
                if result.outcome == 2:
                    break
            assert got == b"BOUNDARY"
        d.check("ImmediateCancellationCannotOvertakeReliablePrefix")

        s, r, _ = await d.open_stream(client, server)
        await d.execute(3, r, 24, code=55)
        stopped, _ = await d.execute(3, s, 25)
        assert stopped.error_code == 55 and stopped.remote == 1
        await d.execute(3, r, 20, data=b"opposite-still-live", flags=1)
        _, reverse = await d.execute(3, s, 21, capacity=64)
        assert reverse == b"opposite-still-live"
        await d.execute(2, second_client, 14, data=b"other-connection")
        _, data = await d.execute(2, second_server, 15)
        assert data == b"other-connection"
        d.check("StopSendingPreservesOppositeDirectionAndOtherConnection")

        s, r, native_id = await d.open_stream(client, server)
        status, peer_reset = d.begin(3, r, 28)
        assert status == 0
        status, peer_stop = d.begin(3, r, 25)
        assert status == 0
        await d.execute(3, s, 20, data=bytes(128))
        await d.execute(3, s, 22, code=92)
        reset, payload = await d.finish(peer_reset)
        assert (reset.remote, reset.outcome, reset.error_code, reset.final_size,
                reset.reliable_size, reset.reset_frame_type, reset.stream_id) == (1, 2, 92, 132, 4, 0x24, native_id)
        assert reset.written == 0 and payload == b""
        still_waiting = Result(size=c.sizeof(Result))
        assert d.poll(peer_stop, c.byref(still_waiting), None, 0) == 1
        assert d.cancel(peer_stop, 0) == 0
        await d.finish(peer_stop, -10)
        received = b""
        while True:
            read, data = await d.execute(3, r, 21, capacity=256)
            received += data
            if read.outcome == 2:
                assert (read.error_code, read.final_size, read.reliable_size,
                        read.reset_frame_type) == (92, 132, 4, 0x24)
                break
        assert received.startswith(b"HEAD")
        await d.execute(3, r, 20, data=b"watcher-keeps-send", flags=1)
        _, reply = await d.execute(3, s, 21, capacity=64)
        assert reply == b"watcher-keeps-send"
        d.check("ResetReceivedWithoutReadPreservesBufferedPrefixAndDiffersFromStop")

        s, r, _ = await d.open_stream(client, server)
        _, prefix = await d.execute(3, r, 21, capacity=4)
        assert prefix == b"HEAD"
        status, blocked_read = d.begin(3, r, 21, capacity=64)
        assert status == 0
        status, peer_reset = d.begin(3, r, 28)
        assert status == 0
        assert d.begin(3, r, 27)[0] == -11
        await d.execute(3, s, 22, code=93)
        watched, watched_data = await d.finish(peer_reset)
        read, read_data = await d.finish(blocked_read)
        assert watched_data == read_data == b""
        assert watched.remote == read.remote == 1 and watched.outcome == read.outcome == 2
        assert watched.error_code == read.error_code == 93
        assert watched.final_size == read.final_size == 4
        assert watched.reliable_size == read.reliable_size == 4
        assert watched.reset_frame_type == read.reset_frame_type == 0x24
        d.check("ResetReceivedWatcherAndReadCanObserveTheSameActualReset")

        s, r, _ = await d.open_stream(client, server)
        status, reverse_reset = d.begin(3, s, 28)
        assert status == 0
        await d.execute(3, r, 22, code=37)
        reset, data = await d.finish(reverse_reset)
        assert (reset.remote, reset.outcome, reset.error_code, reset.final_size,
                reset.reliable_size, reset.reset_frame_type) == (1, 2, 37, 0, 0, 0x04)
        assert data == b"" and reset.written == 0
        await d.execute(3, s, 20, data=b"original-direction", flags=1)
        received = b""
        while True:
            read, data = await d.execute(3, r, 21, capacity=64)
            received += data
            if read.outcome == 1:
                break
        assert received == b"HEADoriginal-direction"
        d.check("ResetReceivedObservesOrdinaryReverseResetWithoutClosingOtherHalf")

        s, r, _ = await d.open_stream(client, server)
        status, watcher = d.begin(3, r, 28)
        assert status == 0 and d.begin(3, r, 28)[0] == -11
        assert d.cancel(watcher, 7) == 0
        cancelled, _ = await d.finish(watcher, -10)
        assert cancelled.remote == 0 and cancelled.outcome == 0
        timed_out, _ = await d.execute(3, r, 28, timeout=20, expected=-8)
        assert timed_out.remote == 0 and timed_out.outcome == 0
        await d.execute(3, s, 20, data=b"after-watcher", flags=1)
        received = b""
        while True:
            read, data = await d.execute(3, r, 21, capacity=64)
            received += data
            if read.outcome == 1:
                break
        assert received == b"HEADafter-watcher"
        d.check("ResetReceivedCancellationAndTimeoutDoNotConsumeOrStopReceive")

        retained = []
        for _ in range(128):
            _, job = d.begin(2, client, 11, index=2)
            await d.finish(job, -1, release=False)
            retained.append(job)
        assert d.counts().operations == 128 and d.begin(2, client, 10)[0] == -6
        for job in retained:
            await d.finish(job, -1)
        assert d.counts().retained_bytes == 0
        d.check("OutstandingLimitIncludesUnreleasedTerminalResults")

        retained = []
        for _ in range(32):
            config, storage = d.configuration(pki, 1, (0,) * 32, "127.0.0.1:0", {
                "certificate_der": bytes(16384), "issuer_chain_der": bytes(16384),
                "private_key_pkcs8": bytes(16384), "trust_roots_der": bytes(16384)})
            status, job = d.create(True, config, storage)
            if status == -6:
                break
            assert status == 0
            await d.finish(job, -3, release=False)
            retained.append(job)
        assert 0 < len(retained) < 32 and d.counts().retained_bytes <= 4194304
        for job in retained:
            await d.finish(job, -3)
        d.check("IndependentRetainedByteAdmissionBudget", admitted=len(retained))

        await d.execute(1, listener, 5)
        assert d.begin(1, listener, 4)[0] == -1
        await d.execute(2, client, 14, data=b"listener-closed")
        _, data = await d.execute(2, server, 15)
        assert data == b"listener-closed"
        await d.execute(2, second_client, 16, code=4321)
        closed, _ = await d.execute(2, second_server, 17)
        assert closed.remote == 1 and closed.error_code == 4321
        await d.execute(2, client, 14, data=b"unrelated")
        _, data = await d.execute(2, server, 15)
        assert data == b"unrelated"
        d.check("ListenerAndConnectionCloseIsolation")
        await d.cleanup()

        # Resource caps are enforced on real endpoints/streams, not just fake IDs.
        listeners = []
        for _ in range(8):
            handle, _ = await d.start_listener(pki)
            listeners.append(handle)
        config, storage = d.configuration(pki, 1, (0,), "127.0.0.1:0")
        assert d.create(True, config, storage)[0] == -6 and d.counts().listeners == 8
        await d.execute(1, listeners.pop(), 5)
        restored, _ = await d.start_listener(pki)
        assert restored not in listeners and d.counts().listeners == 8
        d.check("ListenerHandleLimitAndAdmissionRecovery")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        connected = []
        for _ in range(16):
            connected.append(await d.connect_client(pki, listener, address))
        assert d.counts().connections == d.counts().native_connections == 32
        config, storage = d.configuration(pki, 0, (1,), address)
        status, job = d.create(False, config, storage)
        assert status == 0
        limited, _ = await d.finish(job, -6)
        assert limited.connection.value == 0 and d.counts().native_connections == 32
        d.check("NativeConnectionAndHandshakeAdmissionLimit")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        connected = [await d.connect_client(pki, listener, address) for _ in range(4)]
        all_streams = []
        for client, server in connected:
            for _ in range(32):
                all_streams.append(await d.open_stream(client, server))
        assert d.counts().streams == 256
        assert d.begin(2, connected[0][0], 12)[0] == -6
        old_sender, old_receiver, old_id = all_streams.pop(0)
        await d.execute(3, old_sender, 27)
        await d.execute(3, old_receiver, 27)
        assert d.begin(3, old_sender, 26)[0] == -1
        new_sender, new_receiver, new_id = await d.open_stream(*connected[0])
        assert new_sender != old_sender and new_receiver != old_receiver and new_id > old_id
        assert d.counts().streams == 256
        d.check("GlobalStreamHandleLimitAndNeverRecycledOwnership")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        client, server = await d.connect_client(pki, listener, address)
        for index in range(270):
            s, r, native_id = await d.open_stream(client, server)
            assert native_id == index * 4
            await d.execute(3, s, 27)
            await d.execute(3, r, 27)
        assert d.counts().streams == 0
        d.check("ReleasedStreamEvidenceDoesNotAccumulateOverConnectionLifetime")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        _, accepting = d.begin(1, listener, 3)
        await d.execute(1, listener, 5)
        await d.finish(accepting, -5)
        assert d.counts().listeners == 0
        d.check("ListenerCloseWakesPendingAccept")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        config, storage = d.configuration(pki, 0, (1,), address)
        status, job = d.create(False, config, storage)
        assert status == 0
        connected, _ = await d.finish(job)
        # No server Accept is ever issued: listener shutdown must drain its queue.
        await d.execute(1, listener, 5)
        closed, _ = await d.execute(2, connected.connection.value, 17, expected=None)
        assert closed.status in (0, -5)
        if closed.status == 0:
            assert closed.remote == 1
        d.check("ListenerCloseDrainsUnacceptedConnectionsOrHandshakes")
        await d.cleanup()

        for _ in range(32):
            config, storage = d.configuration(pki, 1, (0,), "127.0.0.1:0")
            status, job = d.create(True, config, storage)
            assert status == 0 and d.cancel(job, 3) == 0
            raced, _ = await d.finish(job, expected=None)
            assert raced.status in (0, -10)
            if raced.status == 0:
                assert raced.listener.value != 0
                await d.execute(1, raced.listener.value, 5)
            else:
                assert raced.listener.value == 0
        d.check("ListenerCreationCancellationRaceTransfersOrCleansOwnership")
        await d.cleanup()

        listener, address = await d.start_listener(pki)
        client, server = await d.connect_client(pki, listener, address)
        s, r, _ = await d.open_stream(client, server)
        await d.execute(3, s, 20, data=bytes(60000))
        partial, _ = await d.execute(3, s, 20, data=bytes(65536), flags=2, timeout=500, expected=-8)
        assert 0 < partial.written < 65536 and partial.native_status == -8
        closed, _ = await d.execute(2, server, 17)
        assert closed.remote == 1 and closed.error_code == 1
        d.check("IncompleteReliablePrefixClosesCarrierInsteadOfBareReset", written=partial.written)
        await d.cleanup()

        for missing in ("datagrams", "reset", "client-auth", "confirm"):
            # The independent Go peer takes only test fixture credentials through
            # its pipe. It is never linked into or exported by the shipping DLL.
            process = await asyncio.create_subprocess_exec(
                str(args.peer_server.absolute()), f"-{missing}=false",
                stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE)
            try:
                encoded = {"root": base64.b64encode(pki["root"]).decode(), "identities": [
                    {"cert": base64.b64encode(i["cert"]).decode(), "key": base64.b64encode(i["key"]).decode()}
                    for i in pki["identities"]]}
                process.stdin.write(json.dumps(encoded).encode())
                await process.stdin.drain()
                process.stdin.close()
                address = json.loads(await asyncio.wait_for(process.stdout.readline(), 10))
                config, storage = d.configuration(pki, 0, (1,), address,
                                                  timeout=500 if missing == "confirm" else 3000)
                status, job = d.create(False, config, storage)
                assert status == 0
                expected = -3 if missing == "client-auth" else -8 if missing == "confirm" else -12
                refused, _ = await d.finish(job, expected)
                assert refused.connection.value == 0
                if missing == "confirm":
                    peer_state = json.loads(await asyncio.wait_for(process.stdout.readline(), 10))
                    assert peer_state["handshake_complete"] and peer_state["peer_certificates"] > 0
                    assert peer_state["verified_chains"] > 0
                assert await asyncio.wait_for(process.wait(), 10) == 0
            finally:
                if process.returncode is None:
                    process.kill()
                    await process.wait()
            names = {
                "datagrams": "MissingNativeCapabilityRefusedWithoutFallbackDatagrams",
                "reset": "MissingNativeCapabilityRefusedWithoutFallbackReset",
                "client-auth": "TrustedPinnedServerWithoutClientCertificateRequestIsRefused",
                "confirm": "CompletedMutualTlsWithoutHandshakeDoneIsNotAdmitted",
            }
            d.check(names[missing])
            await d.cleanup()

        # Refusals use real new endpoints and explicit policy, not a mock TLS verifier.
        for name, override in (
            ("MalformedCertificate", {"certificate_der": b"not DER"}),
            ("MalformedPrivateKey", {"private_key_pkcs8": b"not PKCS8"}),
            ("KeyCertificateMismatch", {"private_key_pkcs8": pki["identities"][1]["key"]}),
            ("WrongLocalUri", {"local_uri": b"urn:wrong"}),
            ("MalformedTrust", {"trust_roots_der": b"invalid trust"})):
            config, storage = d.configuration(pki, 0, (1,), "127.0.0.1:1", override)
            status, job = d.create(False, config, storage)
            assert status == 0
            result, _ = await d.finish(job, -3)
            assert result.connection.value == 0
            d.check(name + "Refused")
        config, storage = d.configuration(pki, 5, (1,), "127.0.0.1:1")
        status, job = d.create(False, config, storage)
        assert status == 0
        await d.finish(job, -3)
        d.check("UnsupportedSigningCapabilityRefused")

        for name in ("WrongHostname", "WrongRoot", "WrongPeerUri", "WrongPeerKey", "AlpnMismatch"):
            listener, address = await d.start_listener(pki)
            override = {}
            if name == "WrongHostname":
                override["server_name"] = b"not-localhost.example"
            elif name == "WrongRoot":
                override["trust_roots_der"] = fixture(args.fixture)["root"]
            config, storage = d.configuration(pki, 0, (1,), address, override,
                                              alpn=b"mismatch" if name == "AlpnMismatch" else b"h3")
            if name == "WrongPeerUri":
                buf = array(b"urn:not-the-peer")
                storage.append(buf)
                config.peers[0].application_uri = Span(buf, len(buf), 0)
            elif name == "WrongPeerKey":
                buf = array(pki["identities"][0]["spki"])
                storage.append(buf)
                config.peers[0].spki_sha256 = Span(buf, len(buf), 0)
            status, job = d.create(False, config, storage)
            assert status == 0
            result, _ = await d.finish(job, -3)
            assert result.connection.value == 0
            d.check(name + "RefusedOnRealHandshake")
            await d.cleanup()

        for name, local, peers in (("UnlistedClient", 2, (0,)), ("ExpiredClient", 3, (3,)),
                                   ("WrongClientEku", 4, (4,))):
            listener, address = await d.start_listener(pki, peers=peers)
            _, accepting = d.begin(1, listener, 3, timeout=500)
            config, storage = d.configuration(pki, local, (1,), address)
            status, job = d.create(False, config, storage)
            assert status == 0
            result, _ = await d.finish(job, None)
            if result.connection.value:
                closed, _ = await d.execute(2, result.connection.value, 17, expected=-5)
            accepted, _ = await d.finish(accepting, -8)
            assert accepted.connection.value == 0
            d.check(name + "NeverAcceptedByListener")
            await d.cleanup()

        rsa_pki = fixture(args.fixture, rsa=True)
        listener, address = await d.start_listener(rsa_pki)
        client, server = await d.connect_client(rsa_pki, listener, address, alpn=b"h3")
        await d.execute(2, client, 14, data=b"RSA2048")
        _, data = await d.execute(2, server, 15)
        assert data == b"RSA2048"
        d.check("Rsa2048AndEcdsaP256SuppliedIdentities")
        await d.cleanup()
        from peer_stop_regression import check_peer_stop_semantics
        await check_peer_stop_semantics(d, pki)
        d.check("ZeroJobsHandlesTransportsAndRetainedBytes", max_entry_ms=round(max(d.latencies) * 1000, 3))
    finally:
        await d.cleanup()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({
        "passed": True, "library_sha256": hashlib.sha256(args.library.read_bytes()).hexdigest(),
        "checks": d.checks}, indent=2), encoding="utf-8")
    print(f"PASS {len(d.checks)} production native checks")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--peer-server", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    asyncio.run(main(parser.parse_args()))
