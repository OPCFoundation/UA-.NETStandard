# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Actual peer-STOP evidence, including local-reset-first crossed shutdown."""
import asyncio
import time


class DatagramRelay(asyncio.DatagramProtocol):
    """Bounded test-only relay that can hold one direction of encrypted packets."""

    def __init__(self, target):
        host, port = target.rsplit(":", 1)
        self.target = (host, int(port))
        self.client = None
        self.transport = None
        self.hold_client = None
        self.held = []
        self.failure = None
        self.closed = asyncio.get_running_loop().create_future()

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, address):
        from_client = address != self.target
        if from_client:
            if self.client is None:
                self.client = address
            if self.client != address:
                self.failure = AssertionError("unexpected relay client")
                return
        destination = self.target if from_client else self.client
        if destination is None:
            self.failure = AssertionError("server datagram preceded client")
            return
        if from_client == self.hold_client:
            if len(self.held) >= 64 or len(data) > 65536:
                self.failure = AssertionError("bounded relay queue exhausted")
                return
            self.held.append((data, destination))
        else:
            self.transport.sendto(data, destination)

    def error_received(self, error):
        self.failure = error

    def connection_lost(self, error):
        if error is not None:
            self.failure = error
        if not self.closed.done():
            self.closed.set_result(None)

    def flush(self):
        self.hold_client = None
        held, self.held = self.held, []
        for data, destination in held:
            self.transport.sendto(data, destination)

    async def close(self):
        self.flush()
        self.transport.abort()
        await asyncio.wait_for(asyncio.shield(self.closed), 3)
        assert self.failure is None, self.failure


async def read_exact(driver, stream, payload):
    received = b""
    while len(received) != len(payload):
        result, data = await driver.execute(3, stream, 21, capacity=len(payload) - len(received))
        assert result.outcome == 0 and data
        received += data
    assert received == payload


def assert_pending(driver, operation):
    assert driver.pending(operation)


def assert_peer_stop(result, payload, stream_id, code):
    assert (result.status, result.native_status, result.remote, result.stream_id, result.error_code) == (
        0, 0, 1, stream_id, code)
    assert (result.written, result.final_size, result.reliable_size,
            result.reset_frame_type, result.outcome) == (0, 0, 0, 0, 0)
    assert payload == b""


async def check_peer_stop_semantics(driver, credentials):
    local_code, peer_code = 0x52E4A40FA8DB, 0x52E4A40FA8E3
    prefix, body = b"\x40\x41\x00", b"B" * 2138
    for client_sender in (True, False):
        role = "Client" if client_sender else "Server"
        listener, address = await driver.start_listener(credentials)
        client, server = await driver.connect_client(credentials, listener, address)
        sending, receiving = (client, server) if client_sender else (server, client)
        sender, receiver, stream_id = await driver.open_stream(sending, receiving, prefix=prefix)
        await read_exact(driver, receiver, prefix)
        await driver.execute(3, sender, 20, data=body)
        await read_exact(driver, receiver, body)
        await driver.execute(3, receiver, 20, data=b"roundtrip")
        await read_exact(driver, sender, b"roundtrip")
        # This is a deliberate delayed-prefix case, not a claim to expose ACK counters.
        await asyncio.sleep(0.25)
        status, observed = driver.begin(3, sender, 29)
        assert status == 0
        assert driver.begin(3, sender, 29)[0] == -11
        assert driver.begin(3, sender, 27)[0] == -11
        await driver.execute(3, receiver, 24, code=peer_code)
        stopped, payload = await driver.finish(observed)
        assert_peer_stop(stopped, payload, stream_id, peer_code)
        reset, _ = await driver.execute(3, sender, 23)
        assert (reset.remote, reset.stream_id, reset.error_code, reset.final_size,
                reset.reliable_size, reset.reset_frame_type) == (0, stream_id, peer_code, 2141, 0, 0x04)
        await driver.execute(3, sender, 22, code=local_code)
        repeated, _ = await driver.execute(3, sender, 23)
        assert (repeated.error_code, repeated.final_size, repeated.reliable_size,
                repeated.reset_frame_type) == (peer_code, 2141, 0, 0x04)
        await driver.execute(3, receiver, 20, data=b"reverse-after-stop", flags=1)
        received = b""
        while True:
            read, data = await driver.execute(3, sender, 21, capacity=64)
            received += data
            if read.outcome == 1:
                break
            assert read.outcome == 0
        assert received == b"reverse-after-stop"
        await driver.cleanup()
        driver.check(role + "DelayedPrefixPeerStopProducesActualOrdinaryReset",
                     error=peer_code, final=2141, reliable=0, frame=4)

        listener, address = await driver.start_listener(credentials)
        relay = DatagramRelay(address)
        transport, _ = await asyncio.get_running_loop().create_datagram_endpoint(
            lambda: relay, local_addr=("127.0.0.1", 0))
        try:
            relay_address = "127.0.0.1:" + str(transport.get_extra_info("sockname")[1])
            client, server = await driver.connect_client(credentials, listener, relay_address)
            sending, receiving = (client, server) if client_sender else (server, client)
            sender, receiver, stream_id = await driver.open_stream(sending, receiving, prefix=prefix)
            await read_exact(driver, receiver, prefix)
            await driver.execute(3, sender, 20, data=body)
            await read_exact(driver, receiver, body)
            relay.hold_client = client_sender
            status, observed = driver.begin(3, sender, 29)
            assert status == 0
            await driver.execute(3, sender, 22, code=local_code)
            context, _ = await driver.execute(3, sender, 25)
            assert (context.remote, context.error_code) == (0, local_code)
            before, _ = await driver.execute(3, sender, 23)
            assert (before.remote, before.error_code, before.final_size,
                    before.reliable_size, before.reset_frame_type) == (0, local_code, 2141, 3, 0x24)
            assert_pending(driver, observed)
            await driver.execute(3, receiver, 24, code=peer_code)
            stopped, payload = await driver.finish(observed)
            assert_peer_stop(stopped, payload, stream_id, peer_code)
            deadline = time.monotonic() + 3
            while True:
                after, _ = await driver.execute(3, sender, 23)
                if after.reset_frame_type == 0x04:
                    break
                assert time.monotonic() < deadline, "actual STOP did not produce ordinary RESET"
                await asyncio.sleep(0.001)
            assert (after.remote, after.error_code, after.final_size,
                    after.reliable_size, after.reset_frame_type) == (0, local_code, 2141, 0, 0x04)
            repeated, payload = await driver.execute(3, sender, 29)
            assert_peer_stop(repeated, payload, stream_id, peer_code)
            legacy, _ = await driver.execute(3, sender, 25)
            assert (legacy.remote, legacy.error_code) == (0, local_code)
            assert relay.failure is None, relay.failure
            relay.flush()
        finally:
            relay.flush()
            try:
                await relay.close()
            finally:
                await driver.cleanup()
        driver.check(role + "ActualPeerStopSurvivesEarlierLocalResetCause",
                     local_error=local_code, peer_error=peer_code, final=2141)

    listener, address = await driver.start_listener(credentials)
    client, server = await driver.connect_client(credentials, listener, address)
    sender, receiver, stream_id = await driver.open_stream(client, server, uni=True, prefix=prefix)
    assert driver.begin(3, receiver, 29)[0] == -1
    status, observed = driver.begin(3, sender, 29)
    assert status == 0
    assert driver.cancel(observed, 9) == 0
    cancelled, payload = await driver.finish(observed, -10)
    assert cancelled.remote == 0 and cancelled.error_code == 0 and payload == b""
    timed_out, payload = await driver.execute(3, sender, 29, timeout=20, expected=-8)
    assert timed_out.remote == 0 and timed_out.error_code == 0 and payload == b""
    await read_exact(driver, receiver, prefix)
    await driver.execute(3, sender, 20, data=b"still-live")
    await read_exact(driver, receiver, b"still-live")
    await driver.execute(3, receiver, 24, code=0)
    stopped, payload = await driver.execute(3, sender, 29)
    assert_peer_stop(stopped, payload, stream_id, 0)
    await driver.cleanup()
    driver.check("PeerStopZeroErrorCancellationTimeoutAndDirectionValidation")

    listener, address = await driver.start_listener(credentials)
    client, server = await driver.connect_client(credentials, listener, address)
    sender, _, _ = await driver.open_stream(client, server, prefix=prefix)
    status, observed = driver.begin(3, sender, 29)
    assert status == 0
    await driver.execute(2, client, 16, code=7)
    closed, payload = await driver.finish(observed, -5)
    assert closed.remote == 0 and closed.error_code == 0 and payload == b""
    await driver.cleanup()
    driver.check("ConnectionCloseDoesNotInventPeerStopEvidence")
