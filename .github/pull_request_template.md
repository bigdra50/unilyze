## Summary

Describe the problem and the implemented change.

## Validation

List the commands run and any relevant results.

## Checklist

- [ ] Tests pass on `net8.0`.
- [ ] Tests pass on `net10.0`.
- [ ] The self CodeHealth gate passes with `badge -p src/Unilyze --metric codehealth --fail-under 8.0`.
- [ ] `CHANGELOG.md` is updated, or this change does not require a changelog entry.
- [ ] The golden corpus is updated with `UNILYZE_GOLDEN_UPDATE=1`, or metric output is unchanged.
- [ ] Metric changes include the required `[metrics]` changelog entry and `metricsVersion` update where applicable.
