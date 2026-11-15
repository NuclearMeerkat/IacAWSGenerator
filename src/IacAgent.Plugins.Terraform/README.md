# Terraform

Runs external tools through `IProcessRunner`. Gates and `doctor` call that interface. Tests pass a fake runner.

On Windows the runner searches `PATHEXT`, so `checkov.cmd` is found when its directory is on `PATH`. A `terraform` child gets `TF_PLUGIN_CACHE_DIR` when that variable is unset, under `%LOCALAPPDATA%/iacagent/plugin-cache`.

Replace the runner by registering another `IProcessRunner`. Do not add a second cloud. This tool never runs `terraform apply` or `terraform destroy`.
