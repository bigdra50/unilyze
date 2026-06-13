# Contributing to unilyze

Thank you for contributing to `unilyze`.
Detailed setup, validation, metric compatibility, and release procedures are documented in [README.dev.md](README.dev.md).

## Development workflow

1. Create a dedicated Git worktree and branch for one logical change.
2. Make the smallest focused change that solves the issue.
3. Run the tests for both supported target frameworks:

   ```bash
   dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0 -v minimal
   dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 -v minimal
   ```

4. Run the same CodeHealth self-gate used by CI:

   ```bash
   dotnet run --project src/Unilyze -c Release --framework net10.0 -- \
     badge -p src/Unilyze --metric codehealth --fail-under 8.0
   ```

5. Review the pull request checklist before requesting review.

See [Local Validation](README.dev.md#local-validation) for restore options, pack smoke tests, and local environment notes.

## Metric compatibility and golden corpus

Changes that intentionally affect metric output must update the golden corpus explicitly:

```bash
UNILYZE_GOLDEN_UPDATE=1 dotnet test tests/Unilyze.Tests -f net10.0 --filter GoldenCorpus
```

Review the generated `tests/fixtures/golden/expected.json` diff.
Do not regenerate the corpus automatically in CI.

When measured values or metric definitions change, follow the [Metric Compatibility Policy](docs/metrics.md#metric-compatibility-policy), including the required `metricsVersion` update where applicable.
Add a `CHANGELOG.md` entry under `[Unreleased]` whose text starts with `[metrics]`.

Further details are in [Golden corpus (metrics compatibility)](README.dev.md#golden-corpus-metrics-compatibility).

## Pull requests

Keep each pull request focused on one logical change.
Describe the observed behavior, the intended behavior, and the validation performed.
Complete every applicable item in the pull request template.
