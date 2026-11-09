# Gates

`TerraformFmtValidateGate` runs fmt, init, and validate. `CheckovGate` fails on medium and above. An unrated finding fails only when it is an open admin port. Other unrated findings stay for the reviewer. `InfracostGate` compares `infracost scan --json` with `--budget` and does nothing when no budget is set.

Register another gate with `services.AddSingleton<IGate, MyGate>()` and a unique `Order`. Do not edit Host or Core.
