# Dependency security

ChaptarrNG tracks frontend and .NET dependencies in their lock and central
version files. Run the audits after dependency restore so results cover the
versions that CI and release builds will consume:

```sh
yarn audit
dotnet list src/Chaptarr.sln package --vulnerable --include-transitive
```

CI also runs Trivy dependency and secret scans and Semgrep Community Edition;
CodeQL analysis is disabled. `yarn audit` checks
the frontend build and lint dependency tree; the .NET command checks resolved
packages across the solution's target frameworks.

## Frontend parser depth limit

GitHub withdrew
[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) on
October 23, 2026, stating that it was not practically exploitable. ChaptarrNG
keeps a local MIT-licensed copy of `braces` 3.0.3 at
`patches/braces`, resolved through `package.json`. Its parser rejects nested
brace and parenthesis input beyond 256 levels before building a deeply nested
AST as defense in depth. The package is used by frontend build tooling; the
change does not alter the Chaptarr application runtime. The withdrawn advisory
is not an open vulnerability finding.

`patches/braces/PATCH.md` records the exact local modification. Revisit the
local bound if the package changes or its callers change, then regenerate
`yarn.lock` and rerun the full audit and frontend build.

## .NET cryptography package

The solution centrally pins `System.Security.Cryptography.Pkcs` to `11.0.0`
in `src/Directory.Packages.props`. That is the current .NET 11 patch for
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) and
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).
Keep the central pin current when updating packages that bring Pkcs
transitively, and verify all target frameworks after restoring the solution.


## Frontend glob matching — November 6, 2026

The modern minimatch consumers resolve to 11.0.1 for
[GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249).
Legacy callable-API consumers retain explicit 3.1.4 or 5.x resolutions;
`babel-plugin-lodash` and `filelist` need these exceptions before the general
resolution. Verify the actual installed graph as well as the lockfile: a
successful install alone does not establish that every intended override applied.
The production frontend build, all 193 CJS tests, TypeScript checks and focused
application lint pass against the resolved graph. No minimatch 7–10 remains.


## Picomatch ReDoS — November 17, 2026

Both JavaScript lockfiles resolve Picomatch to the integrity-pinned
`@ryotsu/picomatch@4.0.7-a` fork for
[CVE-2026-112366](https://github.com/advisories/GHSA-fc5q-39xc-vjpf).
The package has no runtime dependencies or install scripts. The reviewed
parser restricts a repeated wildcard branch to a single equivalent match
or treats it literally if it cannot safely simplify it. The frontend's Yarn
Classic lockfile describes the dependency by its alias, so Trivy attributes
the fork to upstream Picomatch. The scanner exception applies only to that
alias and exact version in `yarn.lock`. Remove the overrides and scanner
exception together after validating a compatible upstream fix.

The Docker builders also replace the separate copy bundled inside npm
12.2.2 with the same verified tarball. Their browser stages inherit that
installation so installing Chromium cannot restore the bundled copy.

## Dependency audit — November 13, 2026

The current `yarn.lock` audit reported zero informational, low, moderate, high,
or critical advisories across 1,571 dependencies. A restore followed by
`dotnet list src/Chaptarr.sln package --vulnerable --include-transitive` also
reported no vulnerable .NET packages. These checks used the current public
advisory data; rerun them after dependency changes and review Trivy's exact-head
filesystem and image scans before release.
