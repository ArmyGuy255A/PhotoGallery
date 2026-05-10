terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.10"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}
