# Gates

The host runs every registered gate, in `Order`. A profile cannot skip one.

`TerraformFmtValidateGate` (10) runs `terraform fmt -recursive`, then init and validate. `CheckovGate` (20) scans Terraform. `baseline` blocks only the host's check list. `strict` blocks every failed check except a skip list. An inline `checkov:skip` does not hide a blocking check. `InfracostGate` (30) compares the monthly estimate with `--budget` and does not run when no budget is set.

Add a gate by implementing `IGate` with a unique `Order` and registering `services.AddSingleton<IGate, MyGate>()`. Do not edit Host, Core, or the other gates.
