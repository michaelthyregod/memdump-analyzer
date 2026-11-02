# Contributing

Contributions are welcome — bug reports, ideas and pull requests.

## How it works

- **Open an issue first** for anything bigger than a small fix, so we can agree on the approach before
  you spend time on it.
- **Send changes as a pull request** from a fork, on a feature branch, against `main`.
- **The maintainer decides what is merged.** A pull request may be changed, held back or declined, for
  example when it doesn't fit the direction of the project.

## Licence of contributions

By submitting a contribution you confirm that you wrote it (or have the right to submit it) and agree
that it is licensed under the project's [MIT License](LICENSE), like the rest of the code. Don't add
code, fonts or other assets under licences that are incompatible with MIT, and list any new
third-party component in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Guidelines

- `dotnet build` and `dotnet test` must pass, with no new trim/AOT warnings (see [CLAUDE.md](CLAUDE.md)
  for the project's conventions).
- Tests must be deterministic: no sleeps, timers or wall-clock time deciding the outcome.
- **Never commit memory dumps, generated reports or anything taken from them.** Dumps contain
  production data (connection strings, session tokens, customer data). Use synthetic models in tests.
- Heuristics are based on the sources listed in the [Readme](Readme.md); a new or changed rule should
  say where its threshold comes from.
- Skins: no third-party logos or brand assets without the owner's permission.
