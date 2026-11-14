#!/usr/bin/env python3

from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET


VERSION_PROPERTIES = {
    "runtime": ("MajorVersion", "MinorVersion", "PatchVersion"),
    "ASP.NET Core": ("AspNetCoreMajorVersion", "AspNetCoreMinorVersion", "AspNetCorePatchVersion"),
}
REMOTE_SOURCE = re.compile(
    r"/dotnet/dotnet/(?P<commit>[0-9a-f]{40})/src/(?P<component>runtime|aspnetcore)/eng/Versions\.props"
)


def validate_remote_source(source):
    parsed = urlsplit(source)
    match = REMOTE_SOURCE.fullmatch(parsed.path)
    if (
        parsed.scheme != "https"
        or parsed.netloc != "raw.githubusercontent.com"
        or parsed.query
        or parsed.fragment
        or match is None
    ):
        raise ValueError("remote source must be a pinned .NET VMR Versions.props URL on raw.githubusercontent.com")

    return match.group("commit"), match.group("component")


def read_source(source):
    parsed = urlsplit(source)
    if parsed.scheme:
        validate_remote_source(source)
        response = subprocess.run(
            [
                "curl",
                "--fail",
                "--silent",
                "--show-error",
                "--proto",
                "=https",
                "--max-time",
                "20",
                "--max-filesize",
                str(2 * 1024 * 1024),
                source,
            ],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        return response.stdout

    return Path(source).read_text(encoding="utf-8")


def parse_expected_version(value):
    if not re.fullmatch(r"\d+\.\d+\.\d+", value):
        raise SystemExit(f"Expected runtime version must be major.minor.patch, got {value!r}")
    return tuple(int(part) for part in value.split("."))


def read_component_version(content, component):
    try:
        root = ET.fromstring(content)
    except ET.ParseError as exc:
        raise ValueError("invalid Versions.props XML") from exc

    names = VERSION_PROPERTIES[component]
    parts = []
    for name in names:
        values = [element.text for element in root.iter(name)]
        if len(values) != 1 or values[0] is None or not re.fullmatch(r"\d+", values[0].strip()):
            raise ValueError(f"missing or invalid {name}")
        parts.append(int(values[0].strip()))

    return tuple(parts)


def verify_source_versions(runtime_content, aspnet_content, expected):
    expected_parts = parse_expected_version(expected)
    for component, content in (("runtime", runtime_content), ("ASP.NET Core", aspnet_content)):
        try:
            actual = read_component_version(content, component)
        except ValueError as exc:
            raise SystemExit(f"Pinned .NET VMR {component} source has {exc}; expected .NET {expected} before cloning") from exc

        if actual != expected_parts:
            actual_version = ".".join(str(part) for part in actual)
            raise SystemExit(
                f"Pinned .NET VMR {component} source has version {actual_version}; "
                f"expected .NET {expected} before cloning"
            )


def main(argv):
    if len(argv) != 4:
        raise SystemExit("Usage: verify-pinned-dotnet-runtime-versions.py <expected-version> <runtime-versions-props> <aspnet-versions-props>")

    expected, runtime_source, aspnet_source = argv[1:]
    remote_sources = []
    for source in (runtime_source, aspnet_source):
        if urlsplit(source).scheme:
            remote_sources.append(validate_remote_source(source))
        else:
            remote_sources.append(None)

    if remote_sources[0] is not None or remote_sources[1] is not None:
        if (
            remote_sources[0] is None
            or remote_sources[1] is None
            or remote_sources[0][1] != "runtime"
            or remote_sources[1][1] != "aspnetcore"
            or remote_sources[0][0] != remote_sources[1][0]
        ):
            raise SystemExit("Remote runtime sources must use the same pinned .NET VMR commit and matching component paths")

    contents = []
    for component, source in (("runtime", runtime_source), ("ASP.NET Core", aspnet_source)):
        try:
            contents.append(read_source(source))
        except Exception as exc:
            raise SystemExit(
                f"Could not verify pinned .NET VMR {component} version before cloning: {type(exc).__name__}"
            ) from None

    verify_source_versions(contents[0], contents[1], expected)


if __name__ == "__main__":
    main(sys.argv)
