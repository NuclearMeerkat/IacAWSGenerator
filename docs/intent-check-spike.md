# Intent check spike

Research only. No gate was added, and OPA and conftest were not installed.

The question is whether IacAgent can check that generated Terraform builds what the prompt asked for, on top of the checks it already has: `terraform validate`, Checkov, and the optional Infracost budget.

## 1. Can `terraform plan` run offline?

The committed fixture `tests/fixtures/valid/` is one `aws_s3_bucket` with `provider "aws" { region = "eu-central-1" }` and no skip flags. With fake credentials in the environment (`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` set to `testing`, no session token, no profile), `terraform plan` exits 1:

```text
Error: Retrieving AWS account details: validating provider credentials: retrieving caller identity from STS
```

That is a network call to STS. The three provider settings `skip_credentials_validation`, `skip_requesting_account_id`, and `skip_metadata_api_check` are not in the fixture, so they do not apply.

A copy of that configuration, plus static fake keys on the provider and those three settings, was planned with Terraform and AWS provider 6.73.0. `TF_LOG=trace` on the S3 plan contained no `https://` URL and no `169.254.169.254`. The plan completed locally:

| Configuration | Result |
|---|---|
| `aws_s3_bucket` | Exit 0. Plan: 1 to add. |
| `aws_lambda_function` (`python3.12`, local zip path, fake role ARN) | Exit 0. |
| `aws_apigatewayv2_api` | Exit 0. |
| `aws_sqs_queue` | Exit 0. |
| `aws_dynamodb_table` | Exit 0. |
| `aws_vpc` plus one public and one private `aws_subnet` | Exit 0. Plan: 3 to add. |

`terraform init` still needs the AWS provider. Later runs used the existing plugin cache and did not download it again. Data sources that read AWS, such as `aws_availability_zones` or `aws_ami`, were not part of this set. A plan that reads remote state or refreshes existing objects would call AWS even with the skip flags.

## 2. Can the plan JSON be checked with a few rules?

`terraform show -json` exposes planned resources under `planned_values.root_module.resources`, each with `type`, `name`, and `values`. A rule such as "exactly one `aws_lambda_function` whose `runtime` matches `python3.*`" and "one `aws_apigatewayv2_api`" is a short walk of that tree in C#. The same counts can be read from the generated `.tf` files for top-level `resource` blocks. The attribute check (runtime, protocol type) needs either the plan JSON or a slightly richer read of the HCL than the resource inventory, which only records `type.name`.

IaC-Eval encodes the same idea in Rego and evaluates it with OPA against that plan graph. A published rule looks up `input.configuration.root_module.resources`, checks `resource.type`, and then checks attributes. Those specs average 37.5 lines and the longest is 205 lines, because they allow optional attributes and resource references, not only a count. OPA and conftest were not installed, so this comparison is from the published rule shape, not from a local OPA run. For the short rules above, C# is enough and adds no tool. Rego pays off only if the benchmark reuses the IaC-Eval specs as they are.

## 3. IaC-Eval (Kon et al., NeurIPS 2024 Datasets and Benchmarks)

The dataset is 458 human-curated scenarios, all AWS. Each row is a natural-language prompt, a natural-language intent, a Rego intent, a difficulty from 1 to 6, the resource types involved, and one reference Terraform configuration. The dataset is CC-BY-4.0. The benchmark code is MIT. The persistent DOI is `10.57967/hf/2400`. The files are at [huggingface.co/datasets/autoiac-project/iac-eval](https://huggingface.co/datasets/autoiac-project/iac-eval) and [github.com/autoiac-project/iac-eval](https://github.com/autoiac-project/iac-eval).

Their evaluator feeds the prompt to a model, runs `terraform plan`, and matches the plan graph with the Rego intent in OPA. A configuration that does not plan is counted wrong. GPT-4's reported pass@1 on this benchmark is 19.36%.

The prompts and the list of resource types are usable as ideas for the benchmark suite, with CC-BY-4.0 attribution. The reference Terraform in the dataset includes account-specific provider profiles. Those files should not be copied into this repository. The Rego could be reused only together with a successful plan and OPA, which this spike did not add.

## 4. Credentials

The product rule is that no gate requires real cloud credentials, and the tool never runs `terraform apply` or `terraform destroy`. The keys used here were the literal value `testing` on the provider and in the environment. They are not an AWS account. The successful plans did not show an AWS HTTP call. The fixture as committed did call STS and failed. Fake credentials therefore do not by themselves make `terraform plan` safe to run inside a gate. The skip flags do, for the resource creates tried above.

## Recommendation

No intent gate.

A blocking check that runs `terraform plan` would make every generate depend on provider settings the model may not emit, and it would fail the run for a network or credential problem that is not a wrong design. The Rego specs that would make the check meaningful are a separate language and a new tool. The short rules that would fit this project are a C# walk of the generated files, and `run.json` already records resource types for that comparison. If the benchmark later wants "did the prompt's resources appear", do it as a column computed from the generated `.tf` files, not as a gate.
