resource "aws_security_group" "open" {
  name        = "open-ssh"
  description = "Spike fixture with SSH open to the world"

  ingress {
    description = "ssh"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
}
