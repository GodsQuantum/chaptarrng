#!/usr/bin/env python3

from pathlib import Path
import re
import sys
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET


VERSION_PROPERTIES = {
    "runtime": ("MajorVersion", "MinorVersion", "PatchVersion"),
    "ASP.NET Core": ("AspNetCoreMajorVersion", "AspNetCoreMinorVersion", "AspNetCorePatchVersion"),
}


def read_source(source):
    if source.startswith(("https://", "http://")):
        request = Request(source, headers={"User-Agent": "ChaptarrNG-runtime-pack-preflight"})
        with urlopen(request, timeout=20) as response:
            return response.read(2 * 1024 * 1024).decode("utf-8")

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
