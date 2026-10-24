# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Apply only the accepted, hash-verified QUIC observation and policy overlays."""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError(f"Non-unique QUIC overlay anchor: {before!r}")
    return text.replace(before, after, 1)


def prepare(source, output, verified_module):
    pins = json.loads((Path(__file__).parent / "native-pins.json").read_text("utf-8"))
    # Go forbids overlaying GOMODCACHE. Reuse a supplied source checkout/copy,
    # but first compare EVERY file with the Go checksum-verified module.
    upstream = {p.relative_to(verified_module): p for p in verified_module.rglob("*") if p.is_file()}
    supplied = {p.relative_to(source): p for p in source.rglob("*")
                if p.is_file() and ".git" not in p.relative_to(source).parts}
    if upstream.keys() != supplied.keys():
        raise ValueError("Supplied QUIC source file inventory differs from the verified module")
    for name, original in upstream.items():
        if original.read_bytes() != supplied[name].read_bytes():
            raise ValueError(f"Supplied QUIC source differs from verified module: {name}")
    output.mkdir(parents=True, exist_ok=True)
    replacements, patch = {}, []
    for item in pins["overlays"]:
        name = item["source"]
        original = source.joinpath(*name.split("/")).read_bytes()
        if hashlib.sha256(original).hexdigest() != item["upstream_sha256"]:
            raise ValueError(f"Unpinned upstream source: {name}")
        text = original.decode("utf-8")
        if name == "internal/wire/transport_parameters.go":
            text = replace_once(text, "\tMinAckDelay          *time.Duration",
                                "\tResetStreamAt09     bool // Observation only: exact draft09 parameter received.\n"
                                "\tMinAckDelay          *time.Duration")
            text = replace_once(text, "\t\t\tp.EnableResetStreamAt = true",
                                "\t\t\tp.EnableResetStreamAt = true\n"
                                "\t\t\tp.ResetStreamAt09 = p.ResetStreamAt09 || paramID == resetStreamAtParameterID")
        elif name == "qlog/event.go":
            text = replace_once(text, "\tEnableResetStreamAt             bool\n",
                                "\tEnableResetStreamAt             bool\n"
                                "\tResetStreamAt09                 bool // Exact peer TP0x1d, not legacy07.\n")
            anchor = '\tif e.EnableResetStreamAt {\n\t\th.WriteToken(jsontext.String("reset_stream_at"))\n'
            text = replace_once(text, anchor,
                                '\tif e.ResetStreamAt09 {\n'
                                '\t\th.WriteToken(jsontext.String("reset_stream_at_09"))\n'
                                '\t\th.WriteToken(jsontext.True)\n\t}\n' + anchor)
        elif name == "connection_logging.go":
            text = replace_once(text, "\t\tEnableResetStreamAt:             tp.EnableResetStreamAt,\n",
                                "\t\tEnableResetStreamAt:             tp.EnableResetStreamAt,\n"
                                "\t\tResetStreamAt09:                 tp.ResetStreamAt09,\n")
        elif name == "send_stream.go":
            text = replace_once(
                text,
                "\tif s.resetErr != nil && s.reliableOffset() == 0 {\n"
                "\t\ts.mutex.Unlock()\n"
                "\t\treturn\n"
                "\t}\n"
                "\t// if the peer stopped reading from the stream, there's no need to transmit any data reliably\n"
                "\ts.reliableSize = 0\n"
                "\ts.numOutstandingFrames = 0\n"
                "\tfinalSize := s.writeOffset\n"
                "\tif s.nextFrameReserved && s.nextFrame != nil {\n"
                "\t\tfinalSize = max(finalSize, s.nextFrame.Offset+s.nextFrame.DataLen())\n"
                "\t}\n"
                "\ts.returnFramesToPool()\n",
                "\tif s.resetErr != nil {\n"
                "\t\ts.mutex.Unlock()\n"
                "\t\treturn\n"
                "\t}\n"
                "\treliableOffset := s.reliableOffset()\n"
                "\tfinalSize := max(s.writeOffset, reliableOffset)\n"
                "\tif s.nextFrameReserved && s.nextFrame != nil {\n"
                "\t\tfinalSize = max(finalSize, s.nextFrame.Offset+s.nextFrame.DataLen())\n"
                "\t}\n"
                "\tif reliableOffset == 0 {\n"
                "\t\ts.numOutstandingFrames = 0\n"
                "\t\ts.returnFramesToPool()\n"
                "\t} else {\n"
                "\t\tif s.nextFrame != nil {\n"
                "\t\t\tif s.nextFrame.Offset >= reliableOffset {\n"
                "\t\t\t\ts.nextFrame.PutBack()\n"
                "\t\t\t\ts.nextFrame = nil\n"
                "\t\t\t\ts.nextFrameReserved = false\n"
                "\t\t\t} else if s.nextFrame.Offset+s.nextFrame.DataLen() > reliableOffset {\n"
                "\t\t\t\ts.nextFrame.Data = s.nextFrame.Data[:reliableOffset-s.nextFrame.Offset]\n"
                "\t\t\t}\n"
                "\t\t}\n"
                "\t\tif len(s.retransmissionQueue) > 0 {\n"
                "\t\t\tretransmissionQueue := make([]*wire.StreamFrame, 0, len(s.retransmissionQueue))\n"
                "\t\t\tfor _, frame := range s.retransmissionQueue {\n"
                "\t\t\t\tif frame.Offset >= reliableOffset {\n"
                "\t\t\t\t\tframe.PutBack()\n"
                "\t\t\t\t\tcontinue\n"
                "\t\t\t\t}\n"
                "\t\t\t\tif frame.Offset+frame.DataLen() > reliableOffset {\n"
                "\t\t\t\t\tframe.Data = frame.Data[:reliableOffset-frame.Offset]\n"
                "\t\t\t\t}\n"
                "\t\t\t\tretransmissionQueue = append(retransmissionQueue, frame)\n"
                "\t\t\t}\n"
                "\t\t\ts.retransmissionQueue = retransmissionQueue\n"
                "\t\t}\n"
                "\t}\n")
            text = replace_once(
                text,
                "\t\tErrorCode: s.resetErr.ErrorCode,\n"
                "\t}\n"
                "\ts.mutex.Unlock()\n",
                "\t\tErrorCode:    s.resetErr.ErrorCode,\n"
                "\t\tReliableSize: reliableOffset,\n"
                "\t}\n"
                "\ts.mutex.Unlock()\n")
        if hashlib.sha256(text.encode()).hexdigest() != item["overlay_sha256"]:
            raise ValueError(f"Accepted QUIC overlay changed: {name}")
        destination = output / name.replace("/", "-")
        destination.write_bytes(text.encode())
        source_path = str(source.joinpath(*name.split("/")).absolute())
        destination_path = str(destination.absolute())
        replacements[source_path] = destination_path
        if os.name == "nt":
            replacements[source_path[:2] + source_path[2:].lower()] = destination_path
        patch.extend(difflib.unified_diff(original.decode().splitlines(True), text.splitlines(True),
                                         "upstream/" + name, "observed/" + name))
    (output / "overlay.json").write_text(json.dumps({"Replace": replacements}, indent=2), encoding="utf-8")
    (output / "quic-overlay.patch").write_text("".join(patch), encoding="utf-8")
    (output / "source-hashes.json").write_text(json.dumps(pins["overlays"], indent=2), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verified-module", type=Path, required=True)
    args = parser.parse_args()
    # Go keys overlays by its lexical module path, not the junction's target.
    prepare(args.source.absolute(), args.output.absolute(), args.verified_module.absolute())
