# Spec: publish 0.1.0-preview.1 to NuGet

Acceptance checks, run by hand or CI:

1. `dotnet pack Handrail.slnx -c Release -p:Version=0.1.0-preview.1 -o <feed>` yields `Handrail` and
   `Handrail.AspNetCore` nupkgs with MIT licence expression, README, repository URL and commit, and embedded symbols.
2. A copy of `samples/NotesApp` that references the two packages from that feed (not project references) builds and
   passes a smoke request to `/_webmcp/runtime.js`.
3. `ws readme check` passes.
4. A `v*` tag on main runs `release.yml`; nuget.org then returns 200 for `handrail`.
