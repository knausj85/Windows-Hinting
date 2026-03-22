# GitHub Actions CI/CD Pipelines

## Overview

This project uses GitHub Actions for continuous integration and deployment.

## Workflows

### 1. CI/CD Pipeline (`ci-cd.yml`)
- **Triggers**: Push to main/develop, PRs, releases
- **Jobs**:
  - **Build and Test**: Compiles code, runs tests, performs code analysis
  - **Publish**: Creates self-contained executables for win-x64 and win-x86
  - **Release**: Uploads artifacts to GitHub releases
  - **Code Quality**: Runs additional analysis tools
  - **Security Scan**: Checks for vulnerable dependencies

### 2. Nightly Build (`nightly.yml`)
- **Triggers**: Daily at 2 AM UTC, manual dispatch
- **Purpose**: Create nightly builds for testing
- **Output**: Dated build artifacts

### 3. CodeQL Analysis (`codeql.yml`)
- **Triggers**: Push, PR, weekly schedule
- **Purpose**: Security vulnerability detection
- **Language**: C#

## Required Secrets

No additional secrets are required for basic functionality. The following are optional:

- `GITHUB_TOKEN`: Automatically provided by GitHub Actions
- Add custom secrets if deploying to external services

## Artifact Retention

- Build artifacts: 7 days
- Published releases: 30 days
- Nightly builds: 7 days

## Manual Workflow Dispatch

The nightly build can be triggered manually from the Actions tab.

## Customization

### Change .NET Version
Update the `DOTNET_VERSION` environment variable in each workflow file.

### Add Custom Steps
Add additional steps in the respective jobs as needed for your deployment process.

### Modify Build Configuration
Update the `BUILD_CONFIGURATION` environment variable to change between Debug/Release builds.
