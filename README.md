# Garden Engine CI

This repository is a CI mirror. The code it runs is a generated snapshot of a private repository, with only the files CI needs. Each snapshot is pushed to a `ci/*` branch as one commit with no history, and CI results go back to the private commit as commit statuses.

`main` holds only the sync and sweeper workflows. Issues and pull requests belong in the private repository.
