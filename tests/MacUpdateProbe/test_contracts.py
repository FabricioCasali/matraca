"""Portable checks of generated artifacts. Not a native compilation/execution."""
import base64
import importlib.util
from pathlib import Path
import plistlib
import re
import tarfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("probe", ROOT / "probe.py")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class Contracts(unittest.TestCase):
    def test_bundle_identity_and_install_policy(self):
        identifier = probe.PREFIX + "fixture"
        one = probe.plist_for("1.0", identifier, ROOT, "PUBLIC-TEST-ONLY", 18738)
        two = probe.plist_for("2.0", identifier, ROOT, "PUBLIC-TEST-ONLY", 18738)
        for value in (one, two):
            value = plistlib.loads(plistlib.dumps(value))
            self.assertEqual(value["CFBundleIdentifier"], identifier)
            self.assertTrue(value["SUEnableAutomaticChecks"])
            self.assertTrue(value["SUVerifyUpdateBeforeExtraction"])
            self.assertFalse(value["SUAutomaticallyUpdate"])
            self.assertFalse(value["SUAllowsAutomaticUpdates"])
            self.assertFalse(value["SUEnableSystemProfiling"])
            self.assertNotIn("NSAllowsArbitraryLoads", value["NSAppTransportSecurity"])
        self.assertNotEqual(one["CFBundleVersion"], two["CFBundleVersion"])

    def test_feed_roundtrip(self):
        # Public signature-shaped fixture; not cryptographic verification.
        signature = base64.b64encode(bytes(64)).decode()
        feed = ET.fromstring(probe.appcast(signature, 12345, 18738))
        enclosure = feed.find("channel/item/enclosure")
        self.assertEqual(enclosure.attrib["url"], "http://127.0.0.1:18738/update.zip")
        self.assertEqual(enclosure.attrib["length"], "12345")
        self.assertEqual(enclosure.attrib[f"{{{probe.SPARKLE_NS}}}edSignature"], signature)
        self.assertEqual(feed.find(f"channel/item/{{{probe.SPARKLE_NS}}}version").text, "2.0")

    def test_outside_run_refused(self):
        with self.assertRaises(RuntimeError):
            probe.load_run(ROOT.parent.parent)

    def test_official_archive_layout_when_downloaded(self):
        archive = ROOT / ".work" / "vendor" / f"Sparkle-{probe.VERSION}.tar.xz"
        if not archive.exists():
            self.skipTest("Run python probe.py fetch to check the pinned distribution")
        self.assertEqual(probe.digest(archive), probe.SHA256)
        with tarfile.open(archive) as package:
            names = {member.name.removeprefix("./") for member in package.getmembers()}
            for path in ("bin/sign_update", "Sparkle.framework/Versions/B/Sparkle",
                         "Sparkle.framework/Versions/B/Autoupdate",
                         "Sparkle.framework/Versions/B/Updater.app/Contents/MacOS/Updater",
                         "Sparkle.framework/Versions/B/XPCServices/Installer.xpc/Contents/Info.plist",
                         "Sparkle.framework/Versions/B/XPCServices/Downloader.xpc/Contents/Info.plist"):
                self.assertIn(path, names)
            member = next(m for m in package.getmembers()
                          if m.name.removeprefix("./") == "Sparkle.framework/Versions/B/Headers/SPUUserDriver.h")
            header = package.extractfile(member).read().decode()
            header = re.sub(r"/\*.*?\*/|//[^\n]*", "", header, flags=re.S).split("@optional")[0]
            required = re.findall(r"-\s*\([^)]*\)\s*(.*?);", header, flags=re.S)
            source = (ROOT / "native" / "ProbeDriver.m").read_text(encoding="utf-8")
            implemented = re.findall(r"^-\s*\([^)]*\)\s*(.*?)\{", source, flags=re.S | re.M)

            def selector(declaration):
                labels = re.findall(r"\b(\w+)\s*:", declaration)
                return ":".join(labels) + ":" if labels else declaration.strip().split()[0]

            actual = {selector(declaration) for declaration in implemented}
            for declaration in required:
                self.assertIn(selector(declaration), actual)


if __name__ == "__main__":
    unittest.main()
