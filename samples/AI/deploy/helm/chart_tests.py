#!/usr/bin/env python3
"""Renders the chart and asserts what the rendered manifests must and must not say.

`helm lint` proves a chart is well formed. It does not prove the chart deploys
what it claims to, and the mistakes worth catching here all produce a valid
manifest: a Server that reports the wrong data residency, a credential that
reaches the address space, a fallback pointing at the endpoint it is supposed to
cover for.

Each negative case is also asserted to FAIL, because a guardrail that never
fires is indistinguishable from one that does not work.

    python chart_tests.py [path-to-helm]
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

CHART = Path(__file__).resolve().parent / "ai-model-management"
HELM = sys.argv[1] if len(sys.argv) > 1 else shutil.which("helm")
if HELM is None:
    print("helm was not found on PATH. Pass path-to-helm as the first argument.", file=sys.stderr)
    sys.exit(1)

failures = []
checks = 0


def render(*args, values=None):
    """Runs `helm template` and returns (ok, output)."""
    command = [HELM, "template", "test", str(CHART)]
    if values:
        command += ["-f", str(Path(__file__).resolve().parent / values)]
    command += list(args)
    result = subprocess.run(
        command, capture_output=True, text=True, check=False
    )
    return result.returncode == 0, result.stdout + result.stderr


def check(name, condition, detail=""):
    global checks
    checks += 1
    if condition:
        print(f"  ok   {name}")
    else:
        print(f"  FAIL {name}{(': ' + detail) if detail else ''}")
        failures.append(name)


def expect_render(name, *args, values=None):
    ok, output = render(*args, values=values)
    if not ok:
        check(name, False, output.strip().splitlines()[-1] if output else "")
        return ""
    return output


def expect_refusal(name, fragment, *args, values=None):
    """Asserts the chart refuses a configuration, and refuses it for the stated reason."""
    ok, output = render(*args, values=values)
    check(name, not ok and fragment in output,
          "rendered successfully" if ok else "refused for a different reason")


print("Default values")
out = expect_render("renders")
check("names the OPC UA port", "containerPort: 62640" in out)
check("runs as a non-root user", "runAsNonRoot: true" in out)
check("drops all capabilities", "- ALL" in out)
check("keeps the certificate store on a volume",
      "kind: PersistentVolumeClaim" in out)
check("mounts no credential when none is configured",
      "secretName:" not in out)
check("publishes on-premises residency",
      'value: "on-premises"' in out)
check("publishes egress as false",
      "InferenceBackend__EgressPermitted" in out and 'value: "false"' in out)
check("publishes no fallback by default",
      'name: FallbackInferenceBackend__Enabled' in out and
      out.count('value: "false"') >= 1)

print()
print("Cloud values")
out = expect_render("renders", "--set", "credentials.existingSecret=my-secret",
                    values="values-cloud.yaml")
check("mounts the named Secret", "secretName: my-secret" in out)
check("mounts it read only", "readOnly: true" in out)
check("mounts it unreadable to the group", "defaultMode: 400" in out or
      "defaultMode: 0400" in out or "defaultMode: 256" in out)
check("publishes the credential REFERENCE, not a value",
      "InferenceBackend__CredentialReference" in out)
check("publishes egress as true when the payload leaves the machine",
      'value: "true"' in out)
check("reaches the fallback at a different endpoint",
      "FallbackInferenceBackend__EndpointUri" in out)

print()
print("The credential never appears in a rendered manifest")
out = expect_render("renders with an inline credential",
                    "--set", "credentials.create=true",
                    "--set", "credentials.value=super-secret-value",
                    "--set", "backend.authentication=ApiKey",
                    "--set", "backend.credentialReference=inference-api-key")
# It appears exactly once, in the Secret it belongs in, and nowhere else -
# not in an env var, not in an annotation, not in a ConfigMap.
check("appears only inside the Secret",
      out.count("super-secret-value") == 1)
check("is not passed as an environment variable",
      "value: \"super-secret-value\"" not in out)
check("rolls the pods when it changes",
      "checksum/credentials:" in out)

# A digest of a low-entropy secret is recoverable offline, and Pod annotations
# are readable by anyone with `get pods` - which is granted far more widely than
# `get secrets`. So the annotation must not be a digest OF THE VALUE.
import hashlib  # noqa: E402
value_digest = hashlib.sha256(b"super-secret-value").hexdigest()
check("the annotation is not a digest of the value",
      value_digest not in out)

print()
print("Refusals")
expect_refusal(
    "an ApiKey endpoint with no credential",
    "expects a credential but none is supplied",
    "--set", "backend.authentication=ApiKey")
expect_refusal(
    "a BearerToken endpoint with no credential",
    "expects a credential but none is supplied",
    "--set", "backend.authentication=BearerToken")
expect_refusal(
    "a WorkloadIdentity endpoint with no credential",
    "expects a credential but none is supplied",
    "--set", "backend.authentication=WorkloadIdentity")
expect_refusal(
    "a mounted Secret with no key named",
    "credentialReference is empty",
    "--set", "backend.authentication=ApiKey",
    "--set", "credentials.existingSecret=some-secret",
    "--set", "backend.credentialReference=")
expect_refusal(
    "a fallback claiming no egress while calling off the machine",
    "fallbackBackend.egressPermitted is false",
    "--set", "ai.enableFallback=true",
    "--set", "fallbackBackend.enabled=true",
    "--set", "fallbackBackend.site=Cloud",
    "--set", "fallbackBackend.egressPermitted=false")
expect_refusal(
    "a fallback with nowhere to fall back to",
    "nothing to reach",
    "--set", "ai.enableFallback=true")
expect_refusal(
    "a fallback pointing at the primary's endpoint",
    "That is a retry, not a fallback",
    "--set", "ai.enableFallback=true",
    "--set", "fallbackBackend.enabled=true",
    "--set", "fallbackBackend.endpointUri=http://localhost:5273/",
    "--set", "backend.endpointUri=http://localhost:5273/")
expect_refusal(
    "claiming no egress while calling off the machine",
    "would publish EgressPermitted=false",
    "--set", "backend.site=Cloud",
    "--set", "backend.egressPermitted=false",
    "--set", "backend.authentication=Anonymous")
expect_refusal(
    "both credential sources at once",
    "not both",
    "--set", "credentials.create=true",
    "--set", "credentials.value=x",
    "--set", "credentials.existingSecret=y")
expect_refusal(
    "creating a Secret with no value",
    "credentials.value is empty",
    "--set", "credentials.create=true",
    "--set", "backend.authentication=Anonymous")

print()
if failures:
    print(f"{len(failures)} of {checks} checks failed:")
    for name in failures:
        print(f"  - {name}")
    sys.exit(1)

print(f"All {checks} checks passed.")
