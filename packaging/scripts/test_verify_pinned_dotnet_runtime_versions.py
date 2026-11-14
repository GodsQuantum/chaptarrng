import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).with_name("verify-pinned-dotnet-runtime-versions.py")
SPEC = importlib.util.spec_from_file_location("pinned_runtime_versions", SCRIPT)
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


def versions_props(major, minor, patch, aspnet=False):
    prefix = "AspNetCore" if aspnet else ""
    properties = ("MajorVersion", "MinorVersion", "PatchVersion")
    values = (major, minor, patch)
    entries = "".join(
        f"<{prefix}{name}>{value}</{prefix}{name}>"
        for name, value in zip(properties, values)
    )
    return f"<Project><PropertyGroup>{entries}</PropertyGroup></Project>"


class PinnedDotnetRuntimeVersionsTest(unittest.TestCase):
    def test_accepts_matching_runtime_and_aspnet_major_minor_patch(self):
        VERIFY.verify_source_versions(
            versions_props(10, 0, 15),
            versions_props(10, 0, 15, aspnet=True),
            "10.0.15",
        )

    def test_rejects_runtime_with_wrong_major_and_matching_patch(self):
        with self.assertRaisesRegex(SystemExit, "runtime source has version 9.0.15"):
            VERIFY.verify_source_versions(
                versions_props(9, 0, 15),
                versions_props(10, 0, 15, aspnet=True),
                "10.0.15",
            )

    def test_rejects_runtime_with_wrong_minor_and_matching_patch(self):
        with self.assertRaisesRegex(SystemExit, "runtime source has version 10.1.15"):
            VERIFY.verify_source_versions(
                versions_props(10, 1, 15),
                versions_props(10, 0, 15, aspnet=True),
                "10.0.15",
            )

    def test_rejects_aspnet_with_wrong_major_and_matching_patch(self):
        with self.assertRaisesRegex(SystemExit, "ASP.NET Core source has version 9.0.15"):
            VERIFY.verify_source_versions(
                versions_props(10, 0, 15),
                versions_props(9, 0, 15, aspnet=True),
                "10.0.15",
            )

    def test_rejects_aspnet_with_wrong_minor_and_matching_patch(self):
        with self.assertRaisesRegex(SystemExit, "ASP.NET Core source has version 10.1.15"):
            VERIFY.verify_source_versions(
                versions_props(10, 0, 15),
                versions_props(10, 1, 15, aspnet=True),
                "10.0.15",
            )


if __name__ == "__main__":
    unittest.main()
