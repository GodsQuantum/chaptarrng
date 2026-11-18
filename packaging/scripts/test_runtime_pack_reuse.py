import contextlib
import copy
import base64
import importlib.util
import io
import json
import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).with_name('verify-runtime-pack-reuse.py')
SPEC = importlib.util.spec_from_file_location('runtime_pack_reuse', SCRIPT)
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class RuntimePackReuseTest(unittest.TestCase):
    def setUp(self):
        self.responses = {
            'actions/runs/123': {'head_repository': {'full_name': 'owner/repo'}, 'event': 'workflow_dispatch', 'head_branch': 'main', 'path': '.github/workflows/standalone-asset-validation.yml', 'head_sha': 'source-sha'},
            'actions/runs/123/jobs?per_page=100': {'jobs': [{'name': 'Build Linux x86 and FreeBSD runtime packs', 'conclusion': 'success'}]},
            'actions/runs/123/artifacts?per_page=100': {'artifacts': [{'name': 'chaptarrng-platform-runtime-packs', 'expired': False, 'id': 42, 'digest': 'sha256:fixture'}]},
        }

    def run_verifier(self, run_id='123'):
        def response(argv, **kwargs):
            if argv[:2] == ['git', 'hash-object']:
                return 'matching-blob\n'
            if argv[:2] == ['git', 'show']:
                return Path(argv[2].split(':', 1)[1]).read_text()
            self.assertEqual(argv[:2], ['gh', 'api'])
            endpoint = argv[2].removeprefix('repos/owner/repo/')
            if endpoint.startswith('contents/'):
                file = endpoint.split('?', 1)[0].removeprefix('contents/')
                return json.dumps({
                    'sha': 'matching-blob',
                    'content': base64.b64encode(Path(file).read_bytes()).decode('ascii'),
                })
            return json.dumps(self.responses[endpoint])

        script = Path(__file__).with_name('verify-runtime-pack-reuse.py')
        with tempfile.TemporaryDirectory(prefix='chaptarr-runtime-reuse-') as directory:
            output = Path(directory) / 'provenance.json'
            with patch.dict(os.environ, {'GITHUB_REPOSITORY': 'owner/repo'}), patch('sys.argv', [str(script), run_id, str(output)]), patch('subprocess.check_output', side_effect=response), contextlib.redirect_stdout(io.StringIO()):
                runpy.run_path(str(script), run_name='__main__')
            return json.loads(output.read_text())

    def test_matching_successful_producer_records_original_identity(self):
        result = self.run_verifier()
        self.assertEqual(result['artifactId'], 42)
        self.assertEqual(result['sourceCommit'], 'source-sha')
        self.assertIn('packaging/scripts/dotnet-linux-x86.Dockerfile', result['producerInputBlobs'])

    def test_rejects_release_workflow_run_as_unapproved_producer(self):
        self.responses['actions/runs/123']['path'] = '.github/workflows/release.yml'
        self.responses['actions/runs/123/jobs?per_page=100']['jobs'][0]['name'] = 'Build Linux x86 and FreeBSD .NET 11 runtime packs'
        with self.assertRaisesRegex(SystemExit, 'trusted main'):
            self.run_verifier()

    def test_rejects_untrusted_or_different_workflow_sources(self):
        original = copy.deepcopy(self.responses)
        for key, value in [('head_repository', {'full_name': 'fork/repo'}), ('event', 'pull_request'), ('head_branch', 'feature'), ('path', '.github/workflows/build.yml')]:
            with self.subTest(key=key):
                self.responses = copy.deepcopy(original)
                self.responses['actions/runs/123'][key] = value
                with self.assertRaisesRegex(SystemExit, 'trusted main'):
                    self.run_verifier()

    def test_rejects_failed_producer_even_when_other_jobs_pass(self):
        self.responses['actions/runs/123/jobs?per_page=100']['jobs'][0]['conclusion'] = 'failure'
        with self.assertRaisesRegex(SystemExit, 'successfully'):
            self.run_verifier()

    def test_rejects_changed_producer_input(self):
        def response(argv, **kwargs):
            if argv[:2] == ['git', 'hash-object']:
                return 'changed-blob'
            endpoint = argv[2].removeprefix('repos/owner/repo/')
            if endpoint.startswith('contents/'):
                file = endpoint.split('?', 1)[0].removeprefix('contents/')
                return json.dumps({
                    'sha': 'old-blob',
                    'content': base64.b64encode(Path(file).read_bytes()).decode('ascii'),
                })
            return json.dumps(self.responses[endpoint])

        with patch('subprocess.check_output', side_effect=response):
            with tempfile.TemporaryDirectory(prefix='chaptarr-runtime-reuse-') as directory:
                script = Path(__file__).with_name('verify-runtime-pack-reuse.py')
                with patch.dict(os.environ, {'GITHUB_REPOSITORY': 'owner/repo'}), patch('sys.argv', [str(script), '123', str(Path(directory) / 'result.json')]), self.assertRaisesRegex(SystemExit, 'input changed'):
                    runpy.run_path(str(script), run_name='__main__')

    def test_rejects_expired_artifact(self):
        self.responses['actions/runs/123/artifacts?per_page=100']['artifacts'][0]['expired'] = True
        with self.assertRaisesRegex(SystemExit, 'unexpired'):
            self.run_verifier()

    def test_rejects_non_numeric_id_before_network_access(self):
        with self.assertRaisesRegex(SystemExit, 'positive integer'):
            self.run_verifier('123;echo injected')

    def test_reuses_when_only_pinned_version_preflight_changes(self):
        common = 'readonly freebsd_vmr_commit="297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f"\n'
        build = '    dotnet build runtime\n'
        old = (
            common
            + 'verify_pinned_freebsd_source_versions() {\n    check_patch_version\n}\n'
            + 'clone_sources() {\n'
            + '    python3 - "$runtime_repo/eng/Versions.props" <<\'PY\'\ncheck_patch_version()\nPY\n'
            + '}\nmain() {\n    verify_pinned_freebsd_source_versions\n}\n'
            + build
        )
        current = (
            common
            + 'verify_pinned_freebsd_source_versions() {\n    check_full_version\n}\n'
            + 'clone_sources() {\n'
            + '    python3 "$repo_root/packaging/scripts/verify-pinned-dotnet-runtime-versions.py" \\\n'
            + '        "$runtime_version" "$runtime_repo/eng/Versions.props"\n'
            + '}\nmain() {\n    verify_pinned_freebsd_source_versions\n}\n'
            + build
        )

        self.assertEqual(
            VERIFY.normalize_runtime_pack_builder(current),
            VERIFY.normalize_runtime_pack_builder(old),
        )

    def test_does_not_reuse_when_runtime_pack_build_commands_change(self):
        common = 'readonly freebsd_vmr_commit="297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f"\n'
        preflight = (
            'verify_pinned_freebsd_source_versions() {\n    check_version\n}\n'
            + 'clone_sources() {\n'
            + '    python3 - "$runtime_repo/eng/Versions.props" <<\'PY\'\ncheck_version()\nPY\n'
            + '}\nmain() {\n    verify_pinned_freebsd_source_versions\n}\n'
        )

        source = VERIFY.normalize_runtime_pack_builder(common + preflight + '    dotnet build runtime\n')
        changed = VERIFY.normalize_runtime_pack_builder(common + preflight + '    dotnet build different-runtime\n')

        self.assertNotEqual(source, changed)


if __name__ == '__main__':
    unittest.main()
