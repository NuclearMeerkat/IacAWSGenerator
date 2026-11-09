# Terraform

Runs external tools through `IProcessRunner`. On Windows it resolves `PATHEXT`, so a pip-installed `checkov.cmd` is found when its directory is on PATH.

Doctor uses this runner for `terraform`, `checkov`, and `infracost`. Gate parsers will live beside the gates, because plugins do not reference each other.
