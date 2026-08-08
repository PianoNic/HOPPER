"""Launches a real Minecraft client per locator adapter and checks HOPPER did its job.

Needs Prism Launcher, a signed-in Minecraft account and a running HOPPER. See README.md next to
this file. Standard library only.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

RUNNING = re.compile(r"Sound engine started|Forge Mod Loader has successfully loaded")
BROKEN = re.compile(r"A fatal error|ModLoadingException|has crashed|Failed to start")
SYNCED = re.compile(r"\[HOPPER\].*mod\(s\) ready")
TROUBLE = re.compile(r"^.*(?:ERROR|FATAL|Caused by).*$", re.M)

# No identity pattern means the loader cannot report one - see docs/locator.md.
TARGETS = [
    # adapter        loader      mc         loader version   prism component               facet       mod            identity
    ("Forge1122",    "Forge",    "1.12.2",  "14.23.5.2864",  "net.minecraftforge",         "forge",    "appleskin",   ""),
    ("Forge1165",    "Forge",    "1.16.5",  "36.2.42",       "net.minecraftforge",         "forge",    "ferrite-core", ""),
    ("Forge1182",    "Forge",    "1.18.2",  "40.3.12",       "net.minecraftforge",         "forge",    "ferrite-core", ""),
    ("ForgeModern",  "Forge",    "1.20.1",  "47.4.10",       "net.minecraftforge",         "forge",    "ferrite-core", ""),
    ("NeoForge",     "NeoForge", "1.21.1",  "21.1.248",      "net.neoforged",              "neoforge", "ferrite-core", ""),
    ("Fabric",       "Fabric",   "1.21.1",  "0.16.14",       "net.fabricmc.fabric-loader", "fabric",   "ferrite-core", r"[-|]\s*hopper[\s|]"),
    ("Quilt",        "Quilt",    "1.20.1",  "0.29.2",        "org.quiltmc.quilt-loader",   "quilt",    "ferrite-core", r"[-|]\s*hopper[\s|]"),
]

FIELDS = ("adapter", "loader", "mc", "loader_version", "component", "facet", "mod", "identity")

INSTANCE_CFG = """[General]
ConfigVersion=1.3
InstanceType=OneSix
name={instance}
AutomaticJava=true
JoinServerOnLaunch=false
OverrideMemory=true
MinMemAlloc=1024
MaxMemAlloc=4096
notes=Created by tools/locator-e2e. Safe to delete.
"""


class Api:
    def __init__(self, base, token):
        self.base = base.rstrip("/")
        self.token = token

    def __call__(self, method, path, body=None, raw=False):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Authorization", "Bearer " + self.token)
        if data:
            request.add_header("Content-Type", "application/json")

        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                payload = response.read()
                return payload if raw else json.loads(payload or b"{}")
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"{error.code} from {method} {path}: {error.read().decode()[:300]}") from None


def instances_root():
    return os.environ.get("HOPPER_E2E_PRISM_INSTANCES") or os.path.expandvars(
        r"%APPDATA%\PrismLauncher\instances")


def prism_exe():
    return os.environ.get("HOPPER_E2E_PRISM_EXE") or os.path.expandvars(
        r"%LOCALAPPDATA%\Programs\PrismLauncher\prismlauncher.exe")


def create_instance(target):
    root = os.path.join(instances_root(), target["instance"])
    os.makedirs(os.path.join(root, ".minecraft", "mods"), exist_ok=True)

    with open(os.path.join(root, "instance.cfg"), "w", encoding="utf-8") as handle:
        handle.write(INSTANCE_CFG.format(instance=target["instance"]))

    pack = {
        "components": [
            {"important": True, "uid": "net.minecraft", "version": target["mc"]},
            {"uid": target["component"], "version": target["loader_version"]},
        ],
        "formatVersion": 1,
    }

    with open(os.path.join(root, "mmc-pack.json"), "w", encoding="utf-8") as handle:
        json.dump(pack, handle, indent=4)


def close_everything():
    for image in ("javaw.exe", "java.exe", "prismlauncher.exe"):
        subprocess.run(["taskkill", "/F", "/IM", image, "/T"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)


def read(path):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as handle:
            return handle.read()
    except OSError:
        return ""


def run_client(target, timeout):
    game = os.path.join(instances_root(), target["instance"], ".minecraft")
    log = os.path.join(game, "logs", "latest.log")

    if os.path.exists(log):
        os.remove(log)

    subprocess.Popen([prism_exe(), "--launch", target["instance"]])

    try:
        deadline = time.monotonic() + timeout

        while time.monotonic() < deadline:
            time.sleep(5)
            text = read(log)

            if RUNNING.search(text) or BROKEN.search(text):
                break

        time.sleep(3)

        return read(log)
    finally:
        close_everything()


def judge(target, log):
    if not log:
        return False, False, False, "the client wrote no log at all"

    started = bool(RUNNING.search(log))
    synced = bool(SYNCED.search(log))
    identity = not target["identity"] or bool(re.search(target["identity"], log, re.I))

    if not started:
        trouble = TROUBLE.search(log)
        note = trouble.group(0).strip()[:110] if trouble else "never reached a running state"
    elif not target["identity"]:
        note = "loader does not list service-layer jars, by design"
    else:
        version = re.search(r"hopper[\s|]+(\d+\.\d+\.\d+)", log, re.I)
        note = f"loader reports hopper {version.group(1)}" if version else "started"

    return started, synced, identity, note


def existing_server(api, name):
    servers = api("GET", "/api/servers")
    rows = servers if isinstance(servers, list) else servers.get("items", [])

    for row in rows:
        if row["name"] == name:
            return row["id"]

    return None


def verify(api, target, timeout, keep):
    print(f"== {target['adapter']}: {target['loader']} {target['mc']}")

    create_instance(target)

    server = existing_server(api, target["server"]) or api("POST", "/api/servers", {
        "name": target["server"],
        "loader": target["loader"],
        "minecraftVersion": target["mc"],
        "loaderVersion": target["loader_version"],
    })["id"]

    versions = api("GET", f"/api/modrinth/projects/{target['mod']}/versions"
                          f"?gameVersion={target['mc']}&loader={target['facet']}")

    if not versions:
        raise RuntimeError(f"Modrinth publishes no {target['mod']} for {target['mc']}/{target['facet']}")

    installed = api("POST", f"/api/servers/{server}/modrinth/install",
                    {"items": [{"versionId": versions[0]["id"], "replace": False}]})
    print(f"   server {server[:8]}, {len(installed.get('installed') or [])} mod(s) installed")

    jar = api("GET", f"/api/servers/{server}/jar", raw=True)
    mods = os.path.join(instances_root(), target["instance"], ".minecraft", "mods")
    with open(os.path.join(mods, "hopper.jar"), "wb") as handle:
        handle.write(jar)
    print(f"   jar {len(jar)} bytes -> {target['instance']}")

    result = judge(target, run_client(target, timeout))

    if not keep:
        shutil.rmtree(os.path.join(instances_root(), target["instance"]), ignore_errors=True)
        api("DELETE", f"/api/servers/{server}")

    return result


def mark(value):
    return "yes" if value else "NO"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("adapter", nargs="?", help="run one adapter instead of all of them")
    parser.add_argument("--keep", action="store_true",
                        help="leave the Prism instances and HOPPER servers behind")
    parser.add_argument("--timeout", type=int, default=300, help="seconds to wait for a client to start")
    parser.add_argument("--api", default=os.environ.get("HOPPER_E2E_API", "http://localhost:5170"))
    arguments = parser.parse_args()

    token = os.environ.get("HOPPER_E2E_TOKEN")

    if not token:
        print("Set HOPPER_E2E_TOKEN to a bearer token for an account in the admin role.", file=sys.stderr)
        print("See tools/locator-e2e/README.md.", file=sys.stderr)
        return 2

    targets = [dict(zip(FIELDS, row)) for row in TARGETS]
    for target in targets:
        target["instance"] = f"HOPPER-V-{target['adapter']}"
        target["server"] = f"Verify {target['loader']} {target['mc']} ({target['adapter']})"

    if arguments.adapter:
        targets = [t for t in targets if t["adapter"].lower() == arguments.adapter.lower()]

        if not targets:
            known = ", ".join(row[0] for row in TARGETS)
            print(f"No adapter called '{arguments.adapter}'. Known: {known}", file=sys.stderr)
            return 2

    api = Api(arguments.api, token)
    results = []

    for target in targets:
        try:
            results.append((target["adapter"],) + verify(api, target, arguments.timeout, arguments.keep))
        except Exception as error:
            close_everything()
            results.append((target["adapter"], False, False, False, str(error)[:110]))

    print()
    print(f"{'adapter':<14} {'started':<8} {'synced':<8} {'identity':<9} note")
    print("-" * 78)

    for adapter, started, synced, identity, note in results:
        print(f"{adapter:<14} {mark(started):<8} {mark(synced):<8} {mark(identity):<9} {note}")

    failed = sum(1 for _, s, y, i, _ in results if not (s and y and i))
    print()
    print(f"All {len(results)} adapter(s) started, synced and identified themselves."
          if not failed else f"{failed} of {len(results)} adapter(s) failed.")

    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
