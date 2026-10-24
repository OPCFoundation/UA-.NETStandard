"""Harvest independent OPC 30455 Python rules and Core binaries from pinned draft examples.

No C# output is consulted. The source tree is read-only and bytecode generation is disabled.
Run from the dedicated worktree with an explicit --spec-root; commit the resulting corpus.
"""

import argparse
import base64
import copy
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
parser = argparse.ArgumentParser()
parser.add_argument("--spec-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
tools = args.spec_root / "extras" / "endpoint-registry" / "tools"
sys.path.insert(0, str(tools))

from asyncua.ua.ua_binary import struct_to_binary
from native_pubsub import configuration_from_fixture
from native_examples import example as fixture_example, message as fixture_message
from pubsub_binding import check_publisher_binding, check_subscriber_binding, derive_endpoint
from message_binding import check_message_binding

namespaces = ["http://opcfoundation.org/UA/", "urn:example:factory", "urn:example:analytics"]
vectors = []

def example(version, mapping, *, producer):
    value = fixture_example(version, mapping, producer=producer)
    value.setdefault("message", fixture_message(version, mapping))
    return value


def add(name, record, role="group", container=None):
    producer = "producerEndpoint" in record
    endpoint = record["producerEndpoint" if producer else "consumerEndpoint"]
    connection = record["connection"]
    writer_index = 0 if role == "writer" else None
    issues = (check_publisher_binding(endpoint, connection, writer_index=writer_index) if producer
              else check_subscriber_binding(endpoint, connection))
    message = record["message"]
    message_issues = check_message_binding(message, connection, reader_index=None if producer else 0,
                                          container=container)
    native = configuration_from_fixture(connection, "PubSubConnectionDataType", namespaces)
    datasets = [configuration_from_fixture(item, "PublishedDataSetDataType", namespaces)
                for item in record.get("publishedDataSets", [])]
    vectors.append({
        "name": name, "role": role if producer else "reader", "endpoint": endpoint, "message": message,
        "container": container, "connectionBinary": base64.b64encode(struct_to_binary(native)).decode(),
        "publishedDataSetsBinary": [base64.b64encode(struct_to_binary(item)).decode() for item in datasets],
        "endpointIssues": [item["code"] for item in issues],
        "messageIssues": [item["code"] for item in message_issues],
    })


for version in ("3.1.1", "5.0"):
    for mapping in ("JSON", "UADP"):
        for producer in (True, False):
            baseline = example(version, mapping, producer=producer)
            add(f"{version}-{mapping}-{'publisher' if producer else 'reader'}", baseline)
            for guarantee in (0, 1, 2, 3, 4):
                value = copy.deepcopy(baseline)
                transport = (value["connection"]["WriterGroups"][0]["TransportSettings"] if producer else
                             value["connection"]["ReaderGroups"][0]["DataSetReaders"][0]["TransportSettings"])
                transport["RequestedDeliveryGuarantee"] = guarantee
                qos = {1: 0, 2: 1, 3: 0, 4: 2}.get(guarantee, 0)
                value["producerEndpoint" if producer else "consumerEndpoint"]["protocoloptions"]["qos"] = qos
                value["message"]["protocoloptions"]["qos"] = qos
                add(f"{version}-{mapping}-{'publisher' if producer else 'reader'}-guarantee-{guarantee}", value)
            for mutation in ("bestavailable", "missingversion", "duplicateversion", "wrongversiontype",
                             "addresspath", "wrongbroker", "unsupportedprofile", "qosmismatch",
                             "contentmismatch", "messagemapping", "envelope", "containerenvelope", "messageprotocoloptional"):
                value = copy.deepcopy(baseline)
                connection = value["connection"]
                endpoint = value["producerEndpoint" if producer else "consumerEndpoint"]
                if mutation == "bestavailable":
                    connection["ConnectionProperties"][0]["Value"]["Body"] = "BestAvailable"
                elif mutation == "missingversion":
                    del connection["ConnectionProperties"][0]
                elif mutation == "duplicateversion":
                    connection["ConnectionProperties"].append(copy.deepcopy(connection["ConnectionProperties"][0]))
                elif mutation == "wrongversiontype":
                    connection["ConnectionProperties"][0]["Value"] = {"Type": 7, "Body": 5}
                elif mutation == "addresspath":
                    connection["Address"]["Url"] += "/"
                elif mutation == "wrongbroker":
                    connection["Address"]["Url"] = "mqtts://other.example.test"
                elif mutation == "unsupportedprofile":
                    connection["TransportProfileUri"] = "http://opcfoundation.org/UA-Profile/Transport/pubsub-amqp-json"
                elif mutation == "qosmismatch":
                    endpoint["protocoloptions"]["qos"] = 2
                elif mutation == "contentmismatch":
                    value["message"]["datacontenttype"] = "application/octet-stream"
                elif mutation == "messagemapping":
                    component = (connection["WriterGroups"][0]["DataSetWriters"][0] if producer else
                                 connection["ReaderGroups"][0]["DataSetReaders"][0])
                    component["MessageSettings"] = None
                elif mutation == "envelope":
                    value["message"]["envelope"] = "CloudEvents/1.0"
                elif mutation == "messageprotocoloptional":
                    del value["message"]["protocol"]
                    del value["message"]["protocoloptions"]
                add(f"{version}-{mapping}-{'publisher' if producer else 'reader'}-{mutation}", value,
                    container={"envelope": "CloudEvents/1.0"} if mutation == "containerenvelope" else None)

for mapping in ("JSON", "UADP"):
    for single in (False, True):
        value = example("5.0", mapping, producer=True)
        group = value["connection"]["WriterGroups"][0]
        group["DataSetWriters"].append(copy.deepcopy(group["DataSetWriters"][0]))
        group["DataSetWriters"][1].update(Name="Pressure", DataSetWriterId=62542)
        topic = f"opcua/{mapping.lower()}/data/2234/Line1/Temperature"
        group["DataSetWriters"][0]["TransportSettings"]["QueueName"] = topic
        value["producerEndpoint"]["protocoloptions"]["topic"] = topic
        value["message"]["protocoloptions"]["topic_name"] = topic
        if single:
            if mapping == "JSON":
                group["MessageSettings"]["NetworkMessageContentMask"] |= 4
            else:
                group["MessageSettings"]["DataSetOrdering"] = 2
        add(f"{mapping}-writer-owned-queue-single-{single}", value, "writer")

for mutation in ("queueempty", "queuewildcard", "consumerfilter", "wrongfilter", "abstract", "subscriberonly",
                 "sessionmismatch", "writerfallback", "writerownqueue", "unusedgroup", "dollarwildcard"):
    value = example("5.0", "JSON", producer=True)
    endpoint = value["producerEndpoint"]
    group = value["connection"]["WriterGroups"][0]
    role = "group"
    if mutation == "queueempty":
        group["TransportSettings"]["QueueName"] = ""
    elif mutation == "queuewildcard":
        group["TransportSettings"]["QueueName"] = "opcua/json/+"
    elif mutation in ("consumerfilter", "wrongfilter", "dollarwildcard"):
        endpoint["usage"] = ["subscriber", "consumer"]
        del endpoint["protocoloptions"]["topic"]
        endpoint["protocoloptions"]["topicfilter"] = "opcua/json/data/+/Line1" if mutation == "consumerfilter" else "#"
        if mutation == "wrongfilter":
            endpoint["protocoloptions"]["topicfilter"] = "other/#"
        if mutation == "dollarwildcard":
            group["TransportSettings"]["QueueName"] = "$status/Line1"
    elif mutation == "abstract":
        endpoint["protocoloptions"]["deployed"] = False
    elif mutation == "subscriberonly":
        endpoint["usage"] = ["subscriber"]
        del endpoint["protocoloptions"]["topic"]
        endpoint["protocoloptions"]["topicfilter"] = "#"
    elif mutation == "sessionmismatch":
        endpoint["protocoloptions"]["sessionexpiryinterval"] = 60
    elif mutation == "writerfallback":
        role = "writer"
    elif mutation in ("writerownqueue", "unusedgroup"):
        group["DataSetWriters"][0]["TransportSettings"]["QueueName"] = group["TransportSettings"]["QueueName"]
        role = "writer" if mutation == "writerownqueue" else "group"
    add("publisher-" + mutation, value, role)

for mutation in ("sharedmatch", "sharedmismatch", "sessionmismatch", "subscriberonly", "messagetopicmismatch"):
    value = example("5.0", "JSON", producer=False)
    endpoint = value["consumerEndpoint"]
    reader = value["connection"]["ReaderGroups"][0]["DataSetReaders"][0]
    if mutation.startswith("shared"):
        endpoint["protocoloptions"]["sharedsubscriptiongroup"] = "analytics"
        if mutation == "sharedmatch":
            reader["TransportSettings"]["QueueName"] = "$share/analytics/" + reader["TransportSettings"]["QueueName"]
    elif mutation == "sessionmismatch":
        endpoint["protocoloptions"]["sessionexpiryinterval"] = 60
    elif mutation == "subscriberonly":
        endpoint["usage"] = ["subscriber"]
    else:
        value["message"]["protocoloptions"]["topic_name"] = "other/topic"
    add("reader-" + mutation, value)

sources = [tools / name for name in ("pubsub_binding.py", "message_binding.py", "native_pubsub.py", "part14_contract.py",
                                   "native_examples.py", "test_pubsub_binding.py")]
sources += [args.spec_root / "source" / "endpoint-registry" / "spec.md",
            args.spec_root / "extras" / "_common" / "part14_mqtt.py"]
result = {
    "provenance": {str(path.relative_to(args.spec_root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources},
    "namespaces": namespaces, "vectors": vectors,
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Harvested {len(vectors)} independent Python vectors and native Core binaries.")
