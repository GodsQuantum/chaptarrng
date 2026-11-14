import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


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
    commit = "8e0f8ed2312954727f8919d7d1c2f78b483b4ee7"

    def source_url(self, component, commit=None):
        commit = commit or self.commit
        return f"https://raw.githubusercontent.com/dotnet/dotnet/{commit}/src/{component}/eng/Versions.props"

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

    def test_rejects_non_https_and_non_pinned_remote_sources(self):
        for source in (
            "file:///etc/passwd",
            self.source_url("runtime").replace("https://", "http://"),
            self.source_url("runtime").replace("raw.githubusercontent.com", "example.com"),
            self.source_url("runtime") + "?token=secret",
            f"https://raw.githubusercontent.com/dotnet/dotnet/{self.commit}/README.md",
        ):
            with self.subTest(source=source), self.assertRaises(ValueError):
                VERIFY.validate_remote_source(source)

    @patch.object(VERIFY.subprocess, "run")
    def test_fetches_remote_props_with_curl_and_https_only(self, run):
        run.return_value.stdout = "<Project />"
        source = self.source_url("runtime")

        result = VERIFY.read_source(source)

        self.assertEqual(result, "<Project />")
        args, kwargs = run.call_args
        self.assertEqual(args[0][0], "curl")
        self.assertIn("=https", args[0])
        self.assertNotIn("--location", args[0])
        self.assertEqual(args[0][-1], source)
        self.assertTrue(kwargs["check"])
        self.assertNotIn("shell", kwargs)

    def test_main_requires_both_remote_components_to_use_the_same_commit(self):
        with self.assertRaisesRegex(SystemExit, "same pinned .NET VMR commit"):
            VERIFY.main(
                [
                    "verify",
                    "10.0.15",
                    self.source_url("runtime"),
                    self.source_url("aspnetcore", commit="0" * 40),
                ]
            )


if __name__ == "__main__":
    unittest.main()
