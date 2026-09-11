#!/usr/bin/env python3
"""MT-038 isolated packaging harness. Python stdlib only; never installs tools."""
import argparse
import base64
import functools
import hashlib
import http.server
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import subprocess
import sys
import urllib.request
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
WORK = ROOT / ".work"
VERSION = "2.8.0"
URL = f"https://github.com/sparkle-project/Sparkle/releases/download/{VERSION}/Sparkle-{VERSION}.tar.xz"
# GitHub official release asset 293691465, digest consulted 2026-09-10.
SHA256 = "fd5681ee92bf238aaac2d08214ceaf0cc8976e452d7f882d80bac1e61581f3b1"
PREFIX = "io.github.fabriciocasali.matraca.updateprobe.r"
SPARKLE_NS = "http://www.andymatuschak.org/xml-namespaces/sparkle"
ET.register_namespace("sparkle", SPARKLE_NS)


def run(*args, capture=False, env=None):
    return subprocess.run([str(a) for a in args], check=True, cwd=ROOT,
                          text=True, capture_output=capture, env=env)


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def fetch():
    vendor = WORK / "vendor"
    vendor.mkdir(parents=True, exist_ok=True)
    archive = vendor / f"Sparkle-{VERSION}.tar.xz"
    if not archive.exists():
        partial = archive.with_suffix(".partial")
        try:
            request = urllib.request.Request(URL, headers={"User-Agent": "Matraca-MT038-Probe"})
            with urllib.request.urlopen(request, timeout=60) as response, partial.open("wb") as out:
                if not response.url.startswith("https://"):
                    raise RuntimeError("Download oficial redirecionou para fora de HTTPS")
                shutil.copyfileobj(response, out)
            if digest(partial) != SHA256:
                raise RuntimeError("SHA-256 Sparkle divergente; não extrair/executar")
            partial.replace(archive)
        finally:
            partial.unlink(missing_ok=True)
    if digest(archive) != SHA256:
        raise RuntimeError("SHA-256 Sparkle divergente; cache rejeitado")
    print(f"PASS download oficial Sparkle {VERSION}: SHA-256 {SHA256}")
    return archive


def require_mac():
    if sys.platform != "darwin" or platform.machine() != "arm64":
        raise RuntimeError("Build/serve/verify exigem Mac arm64; Windows só fetch e testes portáteis")
    if int(platform.mac_ver()[0].split(".")[0]) < 15:
        raise RuntimeError("Probe alinhado ao pack.sh de produção: macOS 15+")


def plist_for(version, identifier, root, public_key, port):
    return {
        "CFBundleIdentifier": identifier, "CFBundleName": "MacUpdateProbe",
        "CFBundleExecutable": "MacUpdateProbe", "CFBundlePackageType": "APPL",
        "CFBundleVersion": version, "CFBundleShortVersionString": version,
        "LSMinimumSystemVersion": "15.0", "LSUIElement": True,
        "MPProbeRoot": str(root), "SUFeedURL": f"http://127.0.0.1:{port}/appcast.xml",
        "SUPublicEDKey": public_key, "SUEnableAutomaticChecks": True,
        "SUAutomaticallyUpdate": False, "SUAllowsAutomaticUpdates": False,
        "SUEnableSystemProfiling": False, "SUSendProfileInfo": False,
        "SUShowReleaseNotes": False, "SUVerifyUpdateBeforeExtraction": True,
        # Local fixture only. No arbitrary-loads exception or remote HTTP feed.
        "NSAppTransportSecurity": {"NSAllowsLocalNetworking": True},
    }


def appcast(signature, length, port):
    rss = ET.Element("rss", {"version": "2.0"})
    channel = ET.SubElement(rss, "channel")
    ET.SubElement(channel, "title").text = "MT-038 TEST ONLY"
    item = ET.SubElement(channel, "item")
    ET.SubElement(item, "title").text = "Probe 2.0"
    ET.SubElement(item, f"{{{SPARKLE_NS}}}version").text = "2.0"
    ET.SubElement(item, f"{{{SPARKLE_NS}}}minimumSystemVersion").text = "15.0"
    ET.SubElement(item, "enclosure", {
        "url": f"http://127.0.0.1:{port}/update.zip", "length": str(length),
        "type": "application/octet-stream", f"{{{SPARKLE_NS}}}edSignature": signature,
    })
    return ET.tostring(rss, encoding="utf-8", xml_declaration=True)


def sign(path):
    # Local self-signed identity, same family as production pack.sh. Explicitly no
    # Hardened Runtime / Library Validation / paid Apple Development identity.
    run("codesign", "--force", "--sign", "Matraca Dev", "--timestamp=none", "--options", "0", path)


def package(version, root, sdk, public, identifier, port, env):
    app = root / f"v{version}" / "MacUpdateProbe.app"
    macos = app / "Contents" / "MacOS"
    frameworks = app / "Contents" / "Frameworks"
    macos.mkdir(parents=True)
    frameworks.mkdir()
    run("dotnet", "publish", ROOT / "MacUpdateProbe.csproj", "-c", "Release",
        "-r", "osx-arm64", "--self-contained", "true", "-p:Version=" + version,
        "-o", macos, env=env)
    framework = frameworks / "Sparkle.framework"
    # ditto preserves executable modes and versioned-framework symlinks.
    run("ditto", sdk / "Sparkle.framework", framework)
    run("xcrun", "clang", "-fobjc-arc", "-fblocks", "-Wall", "-Wextra",
        "-Wno-unused-parameter", "-Werror=protocol", "-Werror=implicit-function-declaration",
        "-dynamiclib", "-arch", "arm64", "-mmacosx-version-min=15.0",
        "-F" + str(frameworks), "-framework", "AppKit", "-framework", "Sparkle",
        "-Wl,-rpath,@loader_path/../Frameworks",
        "-Wl,-install_name,@rpath/libMacUpdateProbe.dylib",
        ROOT / "native" / "ProbeDriver.m", "-o", macos / "libMacUpdateProbe.dylib")
    with (app / "Contents" / "Info.plist").open("wb") as stream:
        plistlib.dump(plist_for(version, identifier, root, public, port), stream)
    run("plutil", "-lint", app / "Contents" / "Info.plist")
    # Sign Mach-O files only, not managed PE assemblies / configuration JSON.
    for path in sorted(macos.rglob("*")):
        if path.is_file() and not path.is_symlink() and path.name != "MacUpdateProbe":
            if "Mach-O" in run("file", "-b", path, capture=True).stdout:
                sign(path)
    # Official Sparkle nested-signing order; no --deep signing.
    for relative in ("Versions/B/XPCServices/Installer.xpc",
                     "Versions/B/XPCServices/Downloader.xpc",
                     "Versions/B/Autoupdate", "Versions/B/Updater.app"):
        sign(framework / relative)
    sign(framework)
    sign(app)
    run("codesign", "--verify", "--deep", "--strict", app)
    run("otool", "-L", macos / "libMacUpdateProbe.dylib")
    archive = root / "packages" / f"probe-{version}.zip"
    run("ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive)
    return app, archive


def build(port):
    require_mac()
    for tool in ("dotnet", "xcrun", "codesign", "security", "ditto", "tar", "file", "otool", "plutil"):
        if not shutil.which(tool):
            raise RuntimeError(f"Pré-requisito ausente: {tool}; nenhuma instalação automática")
    identities = run("security", "find-identity", "-v", "-p", "codesigning", capture=True).stdout
    if '"Matraca Dev"' not in identities:
        raise RuntimeError("Identidade local Matraca Dev ausente; não usar fallback ad-hoc/Apple Development")
    archive = fetch()
    identifier = PREFIX + uuid.uuid4().hex
    root = WORK / "runs" / identifier
    root.mkdir(parents=True)
    (root / "packages").mkdir()
    sdk = root / "sparkle"
    sdk.mkdir()
    run("tar", "-xf", archive, "-C", sdk)
    # Refuse changed archive layout instead of guessing a different framework.
    if not (sdk / "Sparkle.framework").is_dir() or not (sdk / "bin" / "sign_update").is_file():
        raise RuntimeError("Layout da distribuição Sparkle inesperado")
    signer = root / "ephemeral-signer"
    run("xcrun", "swiftc", "-target", "arm64-apple-macosx15.0",
        ROOT / "native" / "EphemeralSigner.swift", "-o", signer)
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               DOTNET_CLI_HOME=str(WORK / "dotnet-home"), NUGET_PACKAGES=str(WORK / "nuget"))
    with subprocess.Popen([str(signer), str(sdk / "bin" / "sign_update")],
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, cwd=ROOT) as keys:
        try:
            public = keys.stdout.readline().strip()
            if len(base64.b64decode(public, validate=True)) != 32:
                raise RuntimeError("Assinador não retornou chave pública Ed25519")
            first, first_zip = package("1.0", root, sdk, public, identifier, port, env)
            _, second_zip = package("2.0", root, sdk, public, identifier, port, env)
            signatures = []
            for path in (first_zip, second_zip):
                keys.stdin.write(str(path) + "\n")
                keys.stdin.flush()
                signature = keys.stdout.readline().strip()
                if len(base64.b64decode(signature, validate=True)) != 64:
                    raise RuntimeError("Assinatura de teste ausente ou inválida")
                signatures.append(signature)
        finally:
            keys.stdin.close()  # EOF destroys the ephemeral seed with the process.
            try:
                keys.wait(timeout=10)
            except subprocess.TimeoutExpired:
                keys.kill()
                keys.wait()
    if keys.returncode != 0:
        raise RuntimeError("Assinador efêmero falhou")
    installed = root / "installed" / "MacUpdateProbe.app"
    installed.parent.mkdir()
    run("ditto", first, installed)
    data = root / "data"
    data.mkdir()
    # Synthetic data lives outside the replaced bundle, within this unique run.
    (data / "settings.json").write_text('{"fixture":true,"theme":"system"}\n', encoding="utf-8")
    (data / "history.txt").write_text("Ditado sintético: ação, café, 日本語.\n", encoding="utf-8")
    manifest = {
        "id": identifier, "port": port, "sparkle": VERSION, "sparkleSha256": SHA256,
        "publicKey": public, "signatures": signatures,
        "archives": {p.name: digest(p) for p in (first_zip, second_zip)},
        "data": {p.name: digest(p) for p in data.iterdir()},
    }
    (root / "run.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    good = root / "good"
    bad = root / "invalid"
    good.mkdir()
    bad.mkdir()
    shutil.copyfile(second_zip, good / "update.zip")
    shutil.copyfile(second_zip, bad / "update.zip")
    # Same length, same signature, one changed byte: signature failure, not 404.
    with (bad / "update.zip").open("r+b") as stream:
        stream.seek(second_zip.stat().st_size // 2)
        byte = stream.read(1)
        stream.seek(-1, 1)
        stream.write(bytes([byte[0] ^ 1]))
    feed = appcast(signatures[1], second_zip.stat().st_size, port)
    for target in (good, bad):
        (target / "appcast.xml").write_bytes(feed)
    print(f"Bundles preparados, NÃO lançados. RUN={root}")
    print(f"Instalação isolada: {installed}")
    print("Assinaturas Ed25519 geradas pelo sign_update e verificadas pelo CryptoKit; chave efêmera encerrada.")


def load_run(path):
    root = Path(path).resolve()
    if root.parent != (WORK / "runs").resolve() or not root.name.startswith(PREFIX):
        raise RuntimeError("RUN deve ser um diretório gerado em .work/runs")
    manifest = json.loads((root / "run.json").read_text(encoding="utf-8"))
    if manifest["id"] != root.name:
        raise RuntimeError("Identidade do RUN divergente")
    return root, manifest


def serve(path, invalid):
    require_mac()
    root, manifest = load_run(path)
    directory = root / ("invalid" if invalid else "good")
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(directory))
    # Only public fixture files are exposed, never the run root or signer.
    with http.server.ThreadingHTTPServer(("127.0.0.1", manifest["port"]), handler) as server:
        print(f"Servindo {directory} em http://127.0.0.1:{manifest['port']}; Ctrl+C encerra", flush=True)
        server.serve_forever()


def verify(path, version):
    require_mac()
    root, manifest = load_run(path)
    app = root / "installed" / "MacUpdateProbe.app"
    with (app / "Contents" / "Info.plist").open("rb") as stream:
        info = plistlib.load(stream)
    if info["CFBundleIdentifier"] != manifest["id"] or info["CFBundleVersion"] != version:
        raise RuntimeError("Versão/identidade instalada diferente da esperada")
    actual = {p.name: digest(p) for p in (root / "data").iterdir() if p.is_file()}
    if actual != manifest["data"]:
        raise RuntimeError("Dados sintéticos alterados/perdidos")
    run("codesign", "--verify", "--deep", "--strict", app)
    print(f"PASS bundle {version}, assinatura local verificável, dados sintéticos idênticos.")
    print("Isso NÃO comprova relaunch automático, Gatekeeper, TCC ou drenagem real; conferir roteiro e events.log.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("fetch", help="Download oficial + hash apenas; também executável no Windows")
    build_parser = commands.add_parser("build")
    build_parser.add_argument("--port", type=int, default=18738)
    serve_parser = commands.add_parser("serve")
    serve_parser.add_argument("run")
    serve_parser.add_argument("--invalid", action="store_true")
    verify_parser = commands.add_parser("verify")
    verify_parser.add_argument("run")
    verify_parser.add_argument("--version", required=True, choices=["1.0", "2.0"])
    args = parser.parse_args()
    if args.command == "fetch": fetch()
    elif args.command == "build":
        if not 1024 <= args.port <= 65535: parser.error("porta deve estar entre 1024 e 65535")
        build(args.port)
    elif args.command == "serve": serve(args.run, args.invalid)
    else: verify(args.run, args.version)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(130)
    except (RuntimeError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"ERRO: {error}", file=sys.stderr)
        sys.exit(1)
