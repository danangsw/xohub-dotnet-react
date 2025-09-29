# Phase 1: Environment Setup

## Prerequisites

```bash
# Check versions
dotnet --version  # Should be 8.0+
node --version    # Should be 20+
docker --version  # Should be 24+
```

## Project Structure Creation

```bash
# Create project root
mkdir xohub-dotnet-react
cd xohub-dotnet-react

# Backend structure
mkdir -p server/{Controllers,Hubs,Services,Models,Data}
mkdir -p server/Properties

# Frontend structure  
mkdir -p client/{src/{components,services,hooks,utils,pages},public}

# Documentation
mkdir -p docs examples
```
