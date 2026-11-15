# Observations

## Phase 2: a passing design can still be the wrong size

A prompt for an API Gateway and a Python Lambda that returns hello can pass `terraform validate` and Checkov while building a VPC, private subnets, a KMS key, flow logs, and IAM authorization on the route. The orchestrator may record "no VPC" and the developer still adds one while repairing Checkov findings.

Checkov's free JSON often leaves `severity` null. The gate treats an unrated finding as a failure, so the repair loop adds resources to silence checks instead of keeping the design small. A private VPC without a NAT gateway or a CloudWatch endpoint also leaves the Lambda with no path for logs, and IAM auth makes the hello URL return 403 to a normal caller.

The developer prompt now says to add only the resources the prompt names, and the Checkov gate no longer fails an unrated finding unless it is an open admin port. A tool that does not finish is stopped after three minutes.

The OpenAI .NET client 2.14 retries HTTP 408, 429, 500, 502, 503, and 504 up to three extra times with exponential backoff, so IacAgent adds no retry of its own. OllamaSharp talks to a local server and needs none.

Formatting is a deterministic normalization: `terraform fmt` rewrites the files, and the gate still decides. A syntax error remains a blocking issue. A formatting difference does not spend a repair.
