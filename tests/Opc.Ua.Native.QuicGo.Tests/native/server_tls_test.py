# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Actual native server-authenticated TLS, not UASC or browser conformance evidence."""
import argparse
import asyncio
import ctypes as c
import hashlib
import json
from pathlib import Path

from generic_network_test import Abi, Driver, Info, fixture


async def refused_handshake(driver, pki, listener, address, overrides, policy=1):
    status, accept = driver.begin(1, listener, 3, timeout=1500)
    assert status == 0
    config, storage = driver.configuration(
        pki, 0, (), address, overrides=overrides, authentication_policy=policy)
    status, operation = driver.create(False, config, storage)
    assert status == 0
    client, _ = await driver.finish(operation, expected=-3)
    server, _ = await driver.finish(accept, expected=None)
    assert client.connection.value == server.connection.value == 0
    assert server.status in (-3, -8), server.status


async def main(args):
    driver = Driver(args.library)
    try:
        abi = Abi(size=c.sizeof(Abi))
        assert driver.abi(c.byref(abi)) == 0 and abi.version == 7
        pki = fixture(args.fixture)
        for local, missing in ((0, "trust_roots_der"), (0, "server_name"),
                               (1, "certificate_der"), (1, "private_key_pkcs8")):
            config, storage = driver.configuration(
                pki, local, (), "127.0.0.1:1", overrides={missing: b""}, authentication_policy=1)
            assert driver.create(local == 1, config, storage)[0] == -1
        for local, field, value in (
            (0, "certificate_der", pki["identities"][0]["cert"]),
            (0, "private_key_pkcs8", pki["identities"][0]["key"]),
            (0, "issuer_chain_der", pki["root"]),
            (0, "local_uri", b"urn:forbidden"),
            (1, "local_uri", b"urn:forbidden"),
            (1, "trust_roots_der", pki["root"]),
            (1, "server_name", b"localhost"),
        ):
            config, storage = driver.configuration(
                pki, local, (), "127.0.0.1:1", overrides={field: value}, authentication_policy=1)
            assert driver.create(local == 1, config, storage)[0] == -1, field
        config, storage = driver.configuration(
            pki, 1, (0,), "127.0.0.1:0", authentication_policy=1)
        assert driver.create(True, config, storage)[0] == -1
        driver.check("TransportOnlyTlsRequiresRoleSpecificCredentialsWithoutApplicationPolicy")

        listener, address = await driver.start_listener(pki, peers=(), authentication_policy=1)
        client, server = await driver.connect_client(
            pki, listener, address, peers=(), authentication_policy=1)
        for handle, is_client in ((client, True), (server, False)):
            _, payload = await driver.execute(2, handle, 10)
            evidence = Info.from_buffer_copy(payload)
            assert evidence.authentication_policy == 1
            assert evidence.tls_version == 0x304 and evidence.is_client == int(is_client)
            assert evidence.handshake_complete == evidence.handshake_confirmed == 1
            assert evidence.mutual_tls_authenticated == evidence.used_0rtt == evidence.resumed == 0
            assert evidence.local_certificate_used == int(not is_client)
            assert evidence.verified_chain_length == (2 if is_client else 0)
            assert evidence.application_uri_length == 0 and not any(evidence.application_uri)
            assert bytes(evidence.alpn[:evidence.alpn_length]) == b"h3"
            assert (evidence.datagrams_local, evidence.datagrams_remote,
                    evidence.reset09_local, evidence.reset09_remote) == (1, 1, 1, 1)
            assert evidence.maximum_datagram_payload > 0
            assert bytes(evidence.certificate_sha256) == (
                hashlib.sha256(pki["identities"][1]["cert"]).digest() if is_client else bytes(32))
            assert bytes(evidence.spki_sha256) == (pki["identities"][1]["spki"] if is_client else bytes(32))
        await driver.execute(2, server, 11, expected=-1)
        _, leaf = await driver.execute(2, client, 11)
        assert leaf == pki["identities"][1]["cert"]
        driver.check("ServerAuthenticatedTlsReportsNoClientCertificateOrApplicationIdentity")

        outgoing, incoming, _ = await driver.open_stream(client, server, prefix=b"transport-prefix")
        _, received = await driver.execute(3, incoming, 21, capacity=64)
        assert received == b"transport-prefix"
        await driver.execute(3, incoming, 20, data=b"server-reply")
        _, received = await driver.execute(3, outgoing, 21, capacity=64)
        assert received == b"server-reply"
        status, receive = driver.begin(2, server, 15)
        assert status == 0
        await driver.execute(2, client, 14, data=b"native-datagram")
        _, received = await driver.finish(receive)
        assert received == b"native-datagram"
        driver.check("ServerAuthenticatedTlsRetainsActualBidirectionalStreamsAndDatagrams")

        await refused_handshake(driver, pki, listener, address, {"server_name": b"wrong.invalid"})
        other = fixture(args.fixture)
        await refused_handshake(driver, pki, listener, address, {"trust_roots_der": other["root"]})
        driver.check("ServerAuthenticatedTlsRejectsWrongHostnameAndUntrustedServer")

        strict_listener, strict_address = await driver.start_listener(pki)
        await refused_handshake(driver, pki, strict_listener, strict_address, {})
        driver.check("TransportOnlyClientCannotDowngradeApplicationMutualTlsListener")
    finally:
        await driver.cleanup()
    driver.check("ServerAuthenticatedTlsReleasesAllNativeOwnership")
    args.output.write_text(json.dumps({"checks": driver.checks}, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    asyncio.run(main(parser.parse_args()))
