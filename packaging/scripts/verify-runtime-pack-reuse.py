#!/usr/bin/env python3
"""Permit runtime pack reuse only from a successful trusted matching producer."""
import base64
import json
import os
from pathlib import Path
import re
import subprocess
import sys

def api(repository, path):
    return json.loads(subprocess.check_output(['gh', 'api', f'repos/{repository}/{path}'], text=True))

def normalize_runtime_pack_builder(source):
    source, function_count = re.subn(
        r'(?ms)^verify_pinned_freebsd_source_versions\(\) \{\n.*?^\}\n',
        '<PINNED_SOURCE_VERSION_PREFLIGHT_FUNCTION>\n',
        source,
    )
    source, call_count = re.subn(
        r'(?m)^[ \t]*verify_pinned_freebsd_source_versions[ \t]*\n',
        '<PINNED_SOURCE_VERSION_PREFLIGHT_CALL>\n',
        source,
    )
    source, inline_count = re.subn(
        r'(?ms)^[ \t]*python3 - "\$runtime_repo/eng/Versions\.props".*?^[ \t]*PY\n',
        '<POST_CLONE_VERSION_PREFLIGHT>\n',
        source,
    )
    source, helper_count = re.subn(
        r'(?m)^[ \t]*python3 "\$repo_root/packaging/scripts/verify-pinned-dotnet-runtime-versions\.py" \\\n[ \t]*[^\n]*\n',
        '<POST_CLONE_VERSION_PREFLIGHT>\n',
        source,
    )
    if function_count != 1 or call_count != 1 or inline_count + helper_count != 1:
        raise SystemExit('Runtime pack builder version-preflight structure changed unexpectedly')
    return source

def verify_runtime_pack_reuse(run_id, output, repository):
    run = api(repository, f'actions/runs/{run_id}')
    trusted_producers = {
        '.github/workflows/standalone-asset-validation.yml': 'Build Linux x86 and FreeBSD runtime packs',
    }
    if not (
        run['head_repository']['full_name'] == repository
        and run['event'] == 'workflow_dispatch'
        and run['head_branch'] == 'main'
        and run['path'] in trusted_producers
    ):
        raise SystemExit('Runtime pack source must be this repository\'s trusted main standalone validation workflow')
    jobs = api(repository, f'actions/runs/{run_id}/jobs?per_page=100')['jobs']
    producer = [job for job in jobs if job['name'] == trusted_producers[run['path']]]
    if len(producer) != 1 or producer[0]['conclusion'] != 'success':
        raise SystemExit('The source runtime pack producer did not complete successfully')

    producer_inputs = {}
    current_inputs = {}
    for file in [
        'packaging/scripts/build-extra-platform-runtime-packs.sh',
        'packaging/scripts/dotnet-linux-x86.Dockerfile',
        'packaging/scripts/copy-dotnet-sdk-reference-packs.sh',
        'packaging/scripts/install-extra-platform-runtime-packs.sh',
    ]:
        current = subprocess.check_output(['git', 'hash-object', file], text=True).strip()
        original = api(repository, f'contents/{file}?ref={run["head_sha"]}')
        if file == 'packaging/scripts/build-extra-platform-runtime-packs.sh':
            source_builder = base64.b64decode(original['content']).decode('utf-8')
            current_builder = Path(file).read_text(encoding='utf-8')
            if normalize_runtime_pack_builder(current_builder) != normalize_runtime_pack_builder(source_builder):
                raise SystemExit('Runtime pack builder behavior changed outside source-version preflight')
        elif current != original['sha']:
            raise SystemExit(f'Runtime pack producer input changed: {file}')
        producer_inputs[file] = original['sha']
        current_inputs[file] = current
    artifacts = api(repository, f'actions/runs/{run_id}/artifacts?per_page=100')['artifacts']
    packs = [artifact for artifact in artifacts if artifact['name'] == 'chaptarrng-platform-runtime-packs' and not artifact['expired']]
    if len(packs) != 1:
        raise SystemExit('Exactly one unexpired runtime pack artifact is required')
    Path(output).write_text(json.dumps({
        'sourceRunId': run_id, 'sourceCommit': run['head_sha'],
        'artifactId': packs[0]['id'], 'artifactDigest': packs[0].get('digest'),
        'producerInputBlobs': producer_inputs,
        'currentInputBlobs': current_inputs,
    }, indent=2) + '\n')
    print('Verified successful trusted runtime producer and unchanged runtime-pack build behavior:', run_id)


def main(argv):
    if len(argv) != 3:
        raise SystemExit('Usage: verify-runtime-pack-reuse.py <run-id> <provenance-output>')
    run_id, output = argv[1:]
    if not re.fullmatch(r'[1-9][0-9]*', run_id):
        raise SystemExit('Runtime pack run ID must be a positive integer')
    repository = os.environ['GITHUB_REPOSITORY']
    verify_runtime_pack_reuse(run_id, output, repository)


if __name__ == '__main__':
    main(sys.argv)
