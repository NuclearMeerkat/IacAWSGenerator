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
}
