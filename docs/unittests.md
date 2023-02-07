# Unit tests

## Overview

Unit tests run automatically on the client and backend, as part of
the builder script. Run it with `--test` to run tests.

## Backend

The entry point is `backend/tests/Tests/Tests.fs`. Run tests from the
command line using:

`scripts/run-backend-tests`

Run `scripts/run-backend-tests --help` for options. In particular, to run only
tests with XXX in their names:

`scripts/run-backend-tests --filter-test-case XXX`

Or to run only testlists with XXX in their names:

`scripts/run-backend-tests --filter-test-list XXX`

Tests are _not_ automatically discovered; they must be added to `Tests.fs`.

We also have a number of property-based tests, which we currently keep separate
in `backend/tests/FuzzTests`. Run them with `scripts/run-backend-fuzzer`.