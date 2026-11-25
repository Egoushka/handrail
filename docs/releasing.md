# Releasing

The git tag is the version: `git tag v0.1.0-preview.1 && git push origin v0.1.0-preview.1` runs
`.github/workflows/release.yml`, which checks the tag is on main and CHANGELOG.md has the entry, builds, tests, packs
both packages (symbols embedded, SourceLink via `PublishRepositoryUrl`), pushes to nuget.org and creates a GitHub
release (marked prerelease when the version has a suffix).

## One-time setup (nuget.org, needs the owner's login)

Publishing uses trusted publishing, so no API key is stored. On nuget.org, account menu, Trusted Publishing, add a
policy:

| Field | Value |
|---|---|
| Repository owner | `Egoushka` |
| Repository | `handrail` |
| Workflow file | `release.yml` |
| Environment | empty |
| Scope | may publish new packages, glob `Handrail*` |

The repository variable `NUGET_USER` (the nuget.org profile name, not a secret) is already set to the same account the
other repos use.
