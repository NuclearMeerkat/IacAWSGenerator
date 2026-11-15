# Observations

## Phase 2: a passing design can still be the wrong size

A prompt for an API Gateway and a Python Lambda that returns hello can pass `terraform validate` and Checkov while building a VPC, private subnets, a KMS key, flow logs, and IAM authorization on the route. The orchestrator may record "no VPC" and the developer still adds one while repairing Checkov findings.

Checkov's free JSON often leaves `severity` null. The gate treats an unrated finding as a failure, so the repair loop adds resources to silence checks instead of keeping the design small. A private VPC without a NAT gateway or a CloudWatch endpoint also leaves the Lambda with no path for logs, and IAM auth makes the hello URL return 403 to a normal caller.

The developer prompt now says to add only the resources the prompt names, and the Checkov gate no longer fails an unrated finding unless it is an open admin port. A tool that does not finish is stopped after three minutes.

The OpenAI .NET client 2.14 retries HTTP 408, 429, 500, 502, 503, and 504 up to three extra times with exponential backoff, so IacAgent adds no retry of its own. OllamaSharp talks to a local server and needs none.

The OpenAI .NET client 2.14 maps chat usage to token counts only. `ChatTokenUsage` has no cost, and `UsageDetails.AdditionalCounts` is not filled with OpenRouter's `usage.cost`. Those counts are whole numbers, so a fractional dollar amount cannot be stored there. `costUsd` uses a provider `cost` count when one is present. Otherwise `iacagent.json` `pricing` supplies USD per one million input and output tokens for the model id. The field is omitted when neither is available. The committed table uses the OpenRouter headline for DeepSeek V4.1 Flash on 15 Nov 2026, $0.0507 input and $0.2028 output per million, under the model id the suite runs. A cached-token discount is not applied.

Formatting is a deterministic normalization: `terraform fmt` rewrites the files, and the gate still decides. A syntax error remains a blocking issue. A formatting difference does not spend a repair.

Checkov findings are blocking or advisory. The host policy decides, because free Checkov leaves severity empty. `baseline` blocks only holes that are fixed by changing an attribute: open SSH or RDP, all ports open to the world, IAM admin policies, public S3 access, and encryption or public-access flags. Extra hardening stays advisory, so the developer does not add a VPC, a KMS key, or a code-signing resource to silence it. `strict` blocks every failed check except a skip list. An inline `checkov:skip` does not hide a blocking check.
