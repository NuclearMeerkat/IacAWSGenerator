using IacAgent.Core;
using IacAgent.Host.Recording;

namespace IacAgent.Host.Tests.Recording;

public sealed class TerraformResourceInventoryTests
{
    [Fact]
    public void Count_IgnoresCommentsAndDataBlocks()
    {
        var files = new[]
        {
            new GeneratedFile("main.tf", """
                resource "aws_s3_bucket" "logs" {}
                # resource "aws_vpc" "commented" {}
                // resource "aws_subnet" "line" {}
                /*
                resource "aws_kms_key" "block" {}
                */
                data "aws_ami" "ubuntu" {}
                resource "aws_lambda_function" "hello" {}
                """),
            new GeneratedFile("lambda.py", "resource \"aws_s3_bucket\" \"ignored\" {}"),
        };

        var inventory = TerraformResourceInventory.Count(files);

        Assert.Equal(2, inventory.Count);
        Assert.Equal(1, inventory.Types["aws_s3_bucket"]);
        Assert.Equal(1, inventory.Types["aws_lambda_function"]);
        Assert.False(inventory.Types.ContainsKey("aws_vpc"));
        Assert.False(inventory.Types.ContainsKey("aws_ami"));
    }

    [Fact]
    public void Describe_ListsResourceNamesAndSkipsComments()
    {
        var file = new GeneratedFile("variables.tf", """
            resource "aws_s3_bucket" "data" {}
            # resource "aws_vpc" "commented" {}
            data "aws_ami" "ubuntu" {}
            resource "aws_iam_role" "reader" {}
            """);

        Assert.Equal("variables.tf: aws_s3_bucket.data, aws_iam_role.reader", TerraformResourceInventory.Describe(file));
        Assert.Equal("lambda.py", TerraformResourceInventory.Describe(new GeneratedFile("lambda.py", "resource \"aws_s3_bucket\" \"ignored\" {}")));
    }

    [Fact]
    public void Count_IncludesModuleBlocks()
    {
        var files = new[]
        {
            new GeneratedFile("main.tf", """
                module "vpc" {
                  source = "terraform-aws-modules/vpc/aws"
                }
                # module "commented" {}
                resource "aws_s3_bucket" "logs" {}
                """),
        };

        var inventory = TerraformResourceInventory.Count(files);

        Assert.Equal(2, inventory.Count);
        Assert.Equal(1, inventory.Types["module"]);
        Assert.Equal(1, inventory.Types["aws_s3_bucket"]);
        Assert.Contains("module.vpc", TerraformResourceInventory.Describe(files[0]), StringComparison.Ordinal);
    }
}
