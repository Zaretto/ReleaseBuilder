# ReleaseBuilder User Guide

## Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [Basic Concepts](#basic-concepts)
4. [Common Tasks](#common-tasks)
5. [Tutorials](#tutorials)
6. [Advanced Techniques](#advanced-techniques)
7. [Troubleshooting](#troubleshooting)
8. [Best Practices](#best-practices)

---

## Introduction

### What is ReleaseBuilder?

ReleaseBuilder is a build automation tool that helps you create reproducible, versioned releases of your software. Instead of writing complex shell scripts or maintaining multiple build configurations, you define your build process once in an XML file and ReleaseBuilder handles the rest.

### Why Use ReleaseBuilder?

- **Consistency**: Same build process works on Windows and macOS
- **Versioning**: Automatic semantic versioning with GitVersion
- **Multi-Target**: Build for different environments (dev, staging, production) from one config
- **Modular**: Build complex projects with multiple components
- **Validated**: XML schema ensures your config is correct before you run it

### Who Should Use This Guide?

This guide is for developers and release engineers who need to:
- Automate software builds and deployments
- Create versioned release packages
- Manage multi-environment deployments
- Build complex, multi-component projects

---

## Getting Started

### Installation

#### Prerequisites

1. **.NET 8.0 SDK** - Download from https://dotnet.microsoft.com/download
   - Windows: Download and run the installer
   - macOS: `brew install dotnet` or download installer
   - Linux: Follow distribution-specific instructions at https://learn.microsoft.com/en-us/dotnet/core/install/linux

2. **Git** - For GitVersion to analyze repository history
   - Windows: https://git-scm.com/download/win
   - macOS: `brew install git` or Xcode Command Line Tools
   - Linux: `sudo apt-get install git` (Ubuntu/Debian) or `sudo yum install git` (RHEL/CentOS)

3. **GitVersion.Tool** - For semantic versioning
   ```bash
   dotnet tool install --global GitVersion.Tool
   ```

#### Building ReleaseBuilder

**On Windows:**
```cmd
REM Clone or download the repository
cd ReleaseBuilder

REM Restore dependencies
dotnet restore

REM Build for Windows x64
dotnet publish -p:PublishProfile=WindowsX64

REM Output will be in: publish\release\win-x64\ReleaseBuilder.exe
```

**On macOS (Intel):**
```bash
# Clone or download the repository
cd ReleaseBuilder

# Restore dependencies
dotnet restore

# Build for macOS x64
dotnet publish -p:PublishProfile=OSX-x64

# Output will be in: publish/release/osx-x64/ReleaseBuilder
```

**On macOS (Apple Silicon/ARM):**
```bash
# Clone or download the repository
cd ReleaseBuilder

# Restore dependencies
dotnet restore

# Build for macOS ARM64
dotnet publish -p:PublishProfile=OSX-arm64

# Output will be in: publish/release/osx-arm64/ReleaseBuilder
```

**On Linux:**
```bash
# Clone or download the repository
cd ReleaseBuilder

# Restore dependencies
dotnet restore

# Build for Linux (creates self-contained executable)
dotnet publish -c Release -r linux-x64 --self-contained true

# Or for ARM64 (Raspberry Pi, ARM servers)
dotnet publish -c Release -r linux-arm64 --self-contained true

# Output will be in: bin/Release/net8.0/linux-x64/publish/
```

**Build Options:**
- `--self-contained true` - Includes .NET runtime (no need to install .NET on target)
- `--self-contained false` - Requires .NET 8.0 runtime on target (smaller output)
- `-c Release` - Optimized release build
- `-r <runtime>` - Target runtime identifier (win-x64, osx-x64, linux-x64, etc.)

#### Installing to Your System

**Windows:**
```cmd
REM Copy to a directory in your PATH
copy publish\release\win-x64\ReleaseBuilder.exe C:\tools\

REM Add C:\tools to PATH if not already there (PowerShell as Administrator):
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\tools", [EnvironmentVariableTarget]::Machine)
```

**macOS:**
```bash
# Copy to user bin directory
sudo cp publish/release/osx-x64/ReleaseBuilder /usr/local/bin/
sudo chmod +x /usr/local/bin/ReleaseBuilder

# Or for current user only
mkdir -p ~/bin
cp publish/release/osx-x64/ReleaseBuilder ~/bin/
chmod +x ~/bin/ReleaseBuilder
# Add ~/bin to PATH in ~/.zshrc or ~/.bash_profile
echo 'export PATH="$HOME/bin:$PATH"' >> ~/.zshrc
```

**Linux:**
```bash
# Copy to system bin directory
sudo cp bin/Release/net8.0/linux-x64/publish/ReleaseBuilder /usr/local/bin/
sudo chmod +x /usr/local/bin/ReleaseBuilder

# Or for current user only
mkdir -p ~/bin
cp bin/Release/net8.0/linux-x64/publish/ReleaseBuilder ~/bin/
chmod +x ~/bin/ReleaseBuilder
# Add ~/bin to PATH in ~/.bashrc
echo 'export PATH="$HOME/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

### Installing GitVersion.Tool

GitVersion.Tool is a .NET Global Tool that analyzes your Git repository and generates semantic version numbers based on your commit history and tags.

**Installation:**
```bash
dotnet tool install --global GitVersion.Tool
```

**Verify Installation:**
```bash
dotnet-gitversion --version
# Should output version information like: GitVersion 5.x.x

# Test in a Git repository
cd /path/to/your/git/repo
dotnet-gitversion
# Should output JSON with version information
```

**If Installation Fails:**
```bash
# Update to latest version
dotnet tool update --global GitVersion.Tool

# If you get "command not found", ensure .NET tools are in PATH:
# Linux/macOS: Add to ~/.bashrc or ~/.zshrc
export PATH="$PATH:$HOME/.dotnet/tools"

# Windows: Usually automatic, but verify PATH includes:
# %USERPROFILE%\.dotnet\tools
```

**Basic Usage:**
```bash
# Run in your Git repository
cd /path/to/your/repo
dotnet-gitversion

# Output JSON with version information
dotnet-gitversion /showvariable SemVer
```

**How GitVersion Works:**

GitVersion determines your version number from:
1. **Git tags** - e.g., `v1.2.3` or `1.2.3`
2. **Branch name** - Different strategies for main/develop/feature branches
3. **Commit history** - Commits since last tag

**Example:**
```bash
# Create a version tag
git tag v1.0.0
git push --tags

# GitVersion will now report version 1.0.0
dotnet-gitversion /showvariable SemVer
# Output: 1.0.0

# After 5 more commits
dotnet-gitversion /showvariable SemVer
# Output: 1.0.1+5 (or 1.0.0-alpha.5 depending on branch)
```

**See Also:**
- GitVersion Documentation: https://gitversion.net/
- Semantic Versioning: https://semver.org/

### Verify Installation

```bash
# Test ReleaseBuilder
ReleaseBuilder --help

# Test GitVersion (if installed)
dotnet-gitversion --version
```

You should see the help message with available options.

---

## Basic Concepts

### The ReleaseConfig.xml File

Your build process is defined in `ReleaseConfig.xml`. This file contains:

1. **Project name** - What you're building
2. **Targets** - Where and how to output (dev, staging, production)
3. **Artifacts** - What files/folders to include
4. **Build actions** - Steps to create the artifacts

### Variables

Variables make your config reusable across different environments:

- **Built-in variables**: `~VERSION~`, `~TYPE~`, `~PUBLISHROOT~`
- **GitVersion variables**: `~SemVer~`, `~Major~`, `~Minor~`, `~Patch~`, etc.
- **Environment variables**: `$HOME`, `$DEPLOY_PATH`
- **Custom variables**: Define your own with `<Folder>` or `<Set>`

Variables are referenced with tilde syntax: `~VARIABLE_NAME~`

### Version Management with GitVersion

ReleaseBuilder automatically integrates with GitVersion.Tool to provide semantic versioning.

**How It Works:**

1. When ReleaseBuilder starts, it executes `dotnet-gitversion` in your project directory
2. GitVersion analyzes your Git repository (tags, branches, commits)
3. It returns JSON with version information
4. ReleaseBuilder extracts version data and creates variables

**Version Extraction:**

ReleaseBuilder creates the `VERSION` variable using this logic:
```
if (NuGetVersionV2 is not empty):
    VERSION = NuGetVersionV2
else:
    VERSION = MajorMinorPatch
```

Also creates `SemVer` as an alias for easier use.

**Available GitVersion Variables:**

All GitVersion JSON properties become variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `~SemVer~` | Semantic version | `1.2.3` |
| `~Major~` | Major version number | `1` |
| `~Minor~` | Minor version number | `2` |
| `~Patch~` | Patch version number | `3` |
| `~MajorMinorPatch~` | Full version | `1.2.3` |
| `~PreReleaseTag~` | Pre-release label | `alpha.1` |
| `~FullSemVer~` | Full semantic version with metadata | `1.2.3-alpha.1+5` |
| `~BranchName~` | Git branch name | `main`, `develop` |
| `~Sha~` | Git commit SHA (short) | `abc1234` |
| `~CommitsSinceVersionSource~` | Commits since last tag | `5` |
| `~CommitDate~` | Date of commit | `2024-01-15` |
| `~NuGetVersion~` | NuGet-compatible version | `1.2.3-alpha0001` |
| `~GITVERSION.JSON~` | Complete JSON output | Full JSON object |

**Example Usage:**
```xml
<!-- Use semantic version in target -->
<Target name="production"
        path="releases"
        type="zip"
        archive-version="~SemVer~" />

<!-- Create version file -->
<create file="VERSION.txt">
Version: ~SemVer~
Branch: ~BranchName~
Commit: ~Sha~
Commits Since Tag: ~CommitsSinceVersionSource~
Build Date: ~CommitDate~
</create>

<!-- Use in filename -->
<copy from="app.exe" name="MyApp-~Major~.~Minor~.~Patch~.exe" />
```

**Without GitVersion:**

If `dotnet-gitversion` is not installed or fails:
- **ReleaseBuilder will fail to start**
- Error message: "Failed to get git version" or similar
- **Build will NOT continue** - GitVersion is required

**To Fix:**
```bash
# Install GitVersion.Tool
dotnet tool install --global GitVersion.Tool

# Verify it's accessible
dotnet-gitversion --version
```

### Targets

Targets define *where* and *how* your release is packaged:

- **dev** - Local folder for development
- **staging** - ZIP file for staging server
- **production** - Versioned ZIP for production

You select a target with the `--target` flag.

### Artifacts

Artifacts are the *what* - the files and folders that make up your release.

---

## Common Tasks

### Task 1: Create Your First Config

Let's create a simple config for a .NET console application.

**Create `ReleaseConfig.xml`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyConsoleApp</Name>

  <Target name="dev" path="builds" type="folder" />

  <Artefacts>
    <build>
      <exec app="dotnet" args="build -c Release" />
    </build>
    <folder>bin/Release/net8.0</folder>
  </Artefacts>
</ReleaseConfig>
```

**Run it:**

```bash
ReleaseBuilder --target dev
```

**What happens:**
1. Builds your app with `dotnet build -c Release`
2. Copies everything from `bin/Release/net8.0`
3. Outputs to `builds/` folder

### Task 2: Add Multiple Targets

Extend the config to support dev and production:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyConsoleApp</Name>

  <!-- Dev: copy to local folder -->
  <Target name="dev" path="builds" type="folder" />

  <!-- Production: create versioned ZIP -->
  <Target name="production" path="releases" type="zip" />

  <Artefacts>
    <build>
      <exec app="dotnet" args="build -c Release" />
    </build>
    <folder>bin/Release/net8.0</folder>
  </Artefacts>
</ReleaseConfig>
```

**Build for production:**

```bash
ReleaseBuilder --target production
```

Creates `releases/MyConsoleApp-{version}.zip`

### Task 3: Add Versioning

Let ReleaseBuilder automatically version your releases using Git tags:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyConsoleApp</Name>

  <Target name="production"
          path="releases"
          type="zip"
          archive-version="~SemVer~" />

  <Artefacts>
    <build>
      <exec app="dotnet" args="build -c Release" />

      <!-- Create version file -->
      <create file="bin/Release/net8.0/VERSION.txt">~SemVer~</create>
    </build>
    <folder>bin/Release/net8.0</folder>
  </Artefacts>
</ReleaseConfig>
```

Now your ZIP will be named like `MyConsoleApp-1.2.3.zip` based on your Git tags.

### Task 4: Clean Before Build

Ensure a clean build every time:

```xml
<Artefacts>
  <build>
    <clean folder="bin" include-folders="true" />
    <clean folder="obj" include-folders="true" />
    <exec app="dotnet" args="build -c Release" />
  </build>
  <folder>bin/Release/net8.0</folder>
</Artefacts>
```

### Task 5: Copy Selective Files

Instead of copying everything, choose specific files:

```xml
<Artefacts folder="output">
  <build>
    <clean folder="output" include-folders="true" />
    <exec app="dotnet" args="build -c Release" />

    <!-- Copy only executables and DLLs -->
    <copy from="bin/Release/net8.0" to="output" match="*.exe" />
    <copy from="bin/Release/net8.0" to="output" match="*.dll" />

    <!-- Copy config files -->
    <copy from="configs" to="output" match="*.json" />
  </build>
  <folder>output</folder>
</Artefacts>
```

### Task 6: Use Environment-Specific Settings

Different settings for dev vs. production:

```xml
<Target name="dev" path="builds" type="folder">
  <Set name="API_URL" value="http://localhost:5000" />
  <Set name="LOG_LEVEL" value="Debug" />
</Target>

<Target name="production" path="releases" type="zip">
  <Set name="API_URL" value="https://api.example.com" />
  <Set name="LOG_LEVEL" value="Warning" />
</Target>

<Artefacts>
  <build>
    <exec app="dotnet" args="build -c Release" />

    <!-- Create config with environment-specific values -->
    <create file="bin/Release/net8.0/appsettings.json">
{
  "ApiUrl": "~API_URL~",
  "Logging": {
    "LogLevel": {
      "Default": "~LOG_LEVEL~"
    }
  }
}
    </create>
  </build>
  <folder>bin/Release/net8.0</folder>
</Artefacts>
```

### Task 7: Copy Environment-Specific Configuration Files

Instead of using `<create>` to generate config files, you can maintain pre-built configuration files in a folder structure and copy the appropriate ones based on the target:

```xml
<Target name="production" path="releases" type="zip">
  <Set name="ENV" value="production" />
</Target>

<Target name="staging" path="releases/staging" type="folder">
  <Set name="ENV" value="staging" />
</Target>

<Target name="dev" path="builds" type="folder">
  <Set name="ENV" value="dev" />
</Target>

<Artefacts>
  <build>
    <!-- Build the application -->
    <exec app="dotnet" args="publish -c Release -o publish" />

    <!-- Copy environment-specific configs from predefined folder -->
    <copy from="configs/~ENV~/appsettings.json" to="publish" />
    <copy from="configs/~ENV~/web.config" to="publish" />
    <copy from="configs/~ENV~" to="publish/config" match="*.xml" />
  </build>
  <folder>publish</folder>
</Artefacts>
```

**Directory structure:**
```
project/
  configs/
    production/
      appsettings.json
      web.config
      database.xml
      logging.xml
    staging/
      appsettings.json
      web.config
      database.xml
      logging.xml
    dev/
      appsettings.json
      web.config
      database.xml
      logging.xml
  ReleaseConfig.xml
```

When you run `ReleaseBuilder --target production`, it copies files from `configs/production/`.
When you run `ReleaseBuilder --target dev`, it copies files from `configs/dev/`.

This approach works well when:
- You have complex configuration files that are easier to maintain as separate files
- Multiple developers need to edit configurations
- Configurations contain binary or non-text data
- You want version control for each environment's config

---

## Tutorials

### Tutorial 1: Building a .NET Web Application

**Scenario**: You have an ASP.NET Core web application that needs different configurations for development, staging, and production.

**Step 1: Create the basic structure**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyWebApp</Name>

  <Target name="dev" path="$HOME/deploys/dev" type="folder">
    <Set name="ENV" value="Development" />
    <Set name="DB_CONNECTION" value="Server=localhost;Database=MyAppDev" />
  </Target>

  <Target name="staging" path="$HOME/deploys/staging" type="folder">
    <Set name="ENV" value="Staging" />
    <Set name="DB_CONNECTION" value="Server=staging-db;Database=MyApp" />
  </Target>

  <Target name="production" path="releases" type="zip" archive-version="~SemVer~">
    <Set name="ENV" value="Production" />
    <Set name="DB_CONNECTION" value="Server=prod-db;Database=MyApp" />
  </Target>
</ReleaseConfig>
```

**Step 2: Add build steps**

```xml
  <Artefacts>
    <build>
      <!-- Clean -->
      <clean folder="bin" include-folders="true" />
      <clean folder="publish" include-folders="true" />

      <!-- Publish the app -->
      <exec app="dotnet" args="publish -c Release -o publish" />

      <!-- Create environment-specific appsettings -->
      <create file="publish/appsettings.Production.json">
{
  "Environment": "~ENV~",
  "ConnectionStrings": {
    "DefaultConnection": "~DB_CONNECTION~"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
      </create>

      <!-- Add version info -->
      <create file="publish/version.txt">
Version: ~SemVer~
Environment: ~ENV~
Build Date: ~BuildDate~
      </create>
    </build>

    <folder>publish</folder>
  </Artefacts>
```

**Step 3: Test each target**

```bash
# Test dev build
ReleaseBuilder --target dev -v

# Test staging
ReleaseBuilder --target staging -v

# Create production release
ReleaseBuilder --target production
```

### Tutorial 2: Building an Angular Application

**Scenario**: Angular app with environment-specific configurations.

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>AngularApp</Name>

  <Target name="dev" path="dist-dev" type="folder" />
  <Target name="production" path="releases" type="zip" archive-version="~SemVer~" />

  <Artefacts>
    <build>
      <!-- Create version details file -->
      <create file="src/app/version.ts">
export const version = {
  number: '~SemVer~',
  buildDate: '~BuildDate~',
  target: '~TYPE~'
};
      </create>

      <!-- Copy environment config -->
      <copy from="environments/environment.~TYPE~.ts"
            to="src/environments"
            name="environment.ts" />

      <!-- Build Angular app -->
      <exec app="npm" args="run build -- --configuration production" log-stdout="true" />
    </build>

    <folder>dist/angular-app</folder>
  </Artefacts>
</ReleaseConfig>
```

### Tutorial 3: Multi-Component Application with Chaining

**Scenario**: Application with separate backend API, frontend, and background worker. Each component has its own build process, but you want to orchestrate them all from a single command.

**Project Structure:**
```
MyEnterpriseSolution/
├── ReleaseConfig.xml          (Main orchestrator)
├── API/
│   ├── ReleaseConfig.xml      (API build config)
│   ├── API.csproj
│   └── ...
├── Frontend/
│   ├── ReleaseConfig.xml      (Frontend build config)
│   ├── package.json
│   └── ...
└── Worker/
    ├── ReleaseConfig.xml      (Worker build config)
    ├── Worker.csproj
    └── ...
```

**How Config Chaining Works:**

The `<ReleaseBuilder>` element tells ReleaseBuilder to:
1. **Change directory** to the specified `folder`
2. **Load** the `ReleaseConfig.xml` in that folder
3. **Execute** that configuration (recursive invocation)
4. **Collect artifacts** if `process="true"`
5. **Return** to the parent directory
6. **Continue** with next element

Think of it as calling ReleaseBuilder from within ReleaseBuilder.

**Main `ReleaseConfig.xml`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyEnterpriseSolution</Name>

  <Target name="dev" path="$DEPLOY_DEV" type="folder" />
  <Target name="production" path="releases" type="zip" archive-version="~SemVer~" />

  <!--
    Chain to component configs
    Each <ReleaseBuilder> element:
    - Changes to the specified folder
    - Loads ReleaseConfig.xml in that folder
    - Executes the nested build
    - Collects artifacts into parent if process="true"
  -->
  <ReleaseBuilder folder="API" process="true" />
  <ReleaseBuilder folder="Frontend" process="true" />
  <ReleaseBuilder folder="Worker" process="true" />
</ReleaseConfig>
```

**API/ReleaseConfig.xml:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>API</Name>

  <!--
    This config inherits the target from parent.
    When parent runs with --target production,
    this also uses production target.
  -->
  <Target name="dev" path="$DEPLOY_DEV" />
  <Target name="production" path="$DEPLOY_DEV" />

  <Artefacts>
    <build>
      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
  </Artefacts>

  <!--
    This folder's contents will be collected by parent
    because parent has process="true"
  -->
  <Artefacts>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>
```

**Frontend/ReleaseConfig.xml:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>Frontend</Name>

  <Target name="dev" path="$DEPLOY_DEV" />
  <Target name="production" path="$DEPLOY_DEV" />

  <Artefacts>
    <build>
      <exec app="npm" args="install" />
      <exec app="npm" args="run build -- --configuration production" />
    </build>
  </Artefacts>

  <Artefacts>
    <folder>dist</folder>
  </Artefacts>
</ReleaseConfig>
```

**Worker/ReleaseConfig.xml:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>Worker</Name>

  <Target name="dev" path="$DEPLOY_DEV" />
  <Target name="production" path="$DEPLOY_DEV" />

  <Artefacts>
    <build>
      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
  </Artefacts>

  <Artefacts>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>
```

**Build Commands:**

```bash
# Build everything
ReleaseBuilder --target production
# Executes: Main -> API -> Frontend -> Worker
# Output: releases/MyEnterpriseSolution-1.2.3.zip
#   Contains: API/*, Frontend/*, Worker/*

# Build only API and Frontend (skip Worker)
ReleaseBuilder --module API --module Frontend --target dev
# Executes: Main -> API -> Frontend (Worker skipped)
# Output: $DEPLOY_DEV/MyEnterpriseSolution/
#   Contains: API/*, Frontend/*

# Build only Worker
ReleaseBuilder --module Worker --target dev
# Executes: Main -> Worker (API and Frontend skipped)
```

**How Module Filtering Works:**

The `--module` flag filters which components get built:

1. **Checks `<Name>` element** - If module name is substring of Name, include it
2. **Checks `<ReleaseBuilder name="...">` attribute** - If specified, match against this
3. **Case-insensitive** - `--module api` matches `<Name>API</Name>`
4. **Substring matching** - `--module Front` matches `<Name>Frontend</Name>`

**Example with Named Modules:**

```xml
<!-- Main config with explicit module names -->
<ReleaseConfig>
  <Name>MyEnterpriseSolution</Name>

  <ReleaseBuilder name="BackendAPI" folder="API" process="true" />
  <ReleaseBuilder name="WebUI" folder="Frontend" process="true" />
  <ReleaseBuilder name="BackgroundWorker" folder="Worker" process="true" />
</ReleaseConfig>
```

```bash
# Build only backend components
ReleaseBuilder --module Backend
# Matches: BackendAPI, BackgroundWorker (both contain "Backend")

# Build only API
ReleaseBuilder --module BackendAPI
# Matches: BackendAPI only

# Build UI
ReleaseBuilder --module WebUI
# Matches: WebUI only
```

**Execution Flow Example:**

When you run: `ReleaseBuilder --module API --target production`

```
1. Load main ReleaseConfig.xml
2. Check <Name>MyEnterpriseSolution</Name>
   - Does NOT contain "API", but continue (parent always runs)
3. Process <ReleaseBuilder folder="API" process="true" />
   - Check <Name>API</Name> in API/ReleaseConfig.xml
   - CONTAINS "API" → INCLUDE
   - cd API/
   - Load API/ReleaseConfig.xml
   - Execute build actions
   - Collect artifacts
   - cd ../
4. Process <ReleaseBuilder folder="Frontend" process="true" />
   - Check <Name>Frontend</Name>
   - Does NOT contain "API" → SKIP
5. Process <ReleaseBuilder folder="Worker" process="true" />
   - Check <Name>Worker</Name>
   - Does NOT contain "API" → SKIP
6. Create final package with only API artifacts
```

**Advanced: Nested Chaining**

You can chain multiple levels deep:

```xml
<!-- Level 1: Main -->
<ReleaseConfig>
  <Name>EnterpriseSuite</Name>
  <ReleaseBuilder folder="ProductA" process="true" />
  <ReleaseBuilder folder="ProductB" process="true" />
</ReleaseConfig>

<!-- Level 2: ProductA/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>ProductA</Name>
  <ReleaseBuilder folder="API" process="true" />
  <ReleaseBuilder folder="UI" process="true" />
</ReleaseConfig>

<!-- Level 3: ProductA/API/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>ProductA.API</Name>
  <Artefacts>
    <build>...</build>
  </Artefacts>
</ReleaseConfig>
```

**Key Points:**

1. **Target inheritance**: Nested configs use the same `--target` as parent (unless overridden)
2. **Variable inheritance**: **Variables ARE inherited** - All parent variables (including `<Set>` from active Target) are passed to children
3. **Target override**: Child configs can define their own `<Target>` elements to override parent's output path
4. **Module filtering**: Applied at each level based on `<Name>` element
5. **Working directory**: Each nested config runs in its own folder
6. **Error propagation**: If any nested build fails, entire build fails

**Variable Inheritance Details:**

When a parent config chains to a child:
- All variables from parent are **inherited by child**
- This includes `<Set>` variables from the active `<Target>`
- Children can use parent variables like `~API_URL~`, `~ENV~`, etc.
- GitVersion variables (`~SemVer~`, etc.) are available to all configs
- Built-in variables (`~TYPE~`, `~PUBLISHROOT~`) are available to all configs

**Target Override Example:**

Children can override the parent's Target to use different output paths:

```xml
<!-- Parent: Main ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>MobileAppSolution</Name>

  <Target name="production" path="releases/api" type="zip">
    <Set name="ENV" value="production" />
    <Set name="API_URL" value="https://api.example.com" />
  </Target>

  <ReleaseBuilder folder="WebAPI" process="true" />
  <ReleaseBuilder folder="AndroidApp" process="true" />
</ReleaseConfig>

<!-- Child: WebAPI/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>WebAPI</Name>

  <!-- Uses parent's Target (path="releases/api") -->
  <!-- Inherits ENV and API_URL variables from parent -->

  <Artefacts>
    <build>
      <create file="config.json">
{
  "environment": "~ENV~",
  "apiUrl": "~API_URL~"
}
      </create>
      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>

<!-- Child: AndroidApp/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>AndroidApp</Name>

  <!-- Override parent's Target - Android APK goes to different folder -->
  <Target name="production" path="releases/android" type="folder" />

  <!-- Still inherits ENV and API_URL variables from parent! -->

  <Artefacts>
    <build>
      <!-- Can use parent's variables -->
      <create file="config.properties">
environment=~ENV~
apiUrl=~API_URL~
      </create>
      <copy from="../mobile/Android/app/release/app-release.apk"
            to="."
            name="MyApp-~SemVer~.apk" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

**Result:**
- WebAPI artifacts → `releases/api/MobileAppSolution-1.2.3.zip` (uses parent Target)
- AndroidApp artifacts → `releases/android/` folder (overrides parent Target)
- Both have access to `~ENV~` and `~API_URL~` from parent's `<Set>` variables

### Tutorial 4: Publishing a NuGet Package

**Scenario**: Library that you want to publish to NuGet.

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyAwesomeLibrary</Name>

  <Artefacts>
    <build>
      <!-- Build -->
      <exec app="dotnet" args="build -c Release" folder="~PUBLISHROOT~" />

      <!-- Run tests -->
      <exec app="dotnet" args="test -c Release --no-build" folder="~PUBLISHROOT~" />

      <!-- Pack -->
      <exec app="dotnet"
            args="pack MyAwesomeLibrary.csproj -c Release"
            folder="~PUBLISHROOT~/MyAwesomeLibrary" />

      <!-- Push to NuGet (requires NUGET_API_KEY environment variable) -->
      <exec app="dotnet"
            args="nuget push MyAwesomeLibrary.~SemVer~.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json"
            folder="~PUBLISHROOT~/MyAwesomeLibrary/bin/Release" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

**Usage:**

```bash
# Set your API key
export NUGET_API_KEY=your-key-here

# Build and publish
ReleaseBuilder -v
```

---

## Advanced Techniques

### Working with Configuration Files

#### Technique 1: Transform JSON/XML Config Files

Instead of maintaining separate config files for each environment, transform a template:

```xml
<build>
  <!-- Copy and transform web.config -->
  <copy from="web.config.template" to="publish" name="web.config">
    <transform-content transform="replace,{{DB_SERVER}},~DB_SERVER~" />
    <transform-content transform="replace,{{API_KEY}},~API_KEY~" />
  </copy>
</build>
```

#### Technique 2: Edit XML Files with XPath

Modify existing XML files:

```xml
<build>
  <exec app="dotnet" args="publish -c Release -o publish" />

  <!-- Update connection string in web.config -->
  <xml-edit file="publish/web.config">
    <node path="//connectionStrings/add[@name='DefaultConnection']/@connectionString"
          action="set,~DB_CONNECTION~" />
    <node path="//appSettings/add[@key='Environment']/@value"
          action="set,~ENV~" />
  </xml-edit>
</build>
```

### Dynamic File Selection

#### Find Latest Build Folder

Automatically locate the most recent build:

```xml
<!-- Find newest build folder -->
<Folder name="LATEST_BUILD" path="builds/Build_*" version="latest" />

<!-- Use it in artifacts -->
<Artefacts>
  <build>
    <copy from="~LATEST_BUILD~" to="release" match="*.*" recursive="true" />
  </build>
  <folder>release</folder>
</Artefacts>
```

#### Extract Version from Folder Name

```xml
<Folder name="APPPATH"
        path="MyApp_*"
        version="last-name"
        name-version="APP_VERSION" />

<!-- Now APP_VERSION contains the version number extracted from folder name -->
```

### Conditional Processing

#### Build Only for Specific Targets

```xml
<!-- Only include documentation in production builds -->
<Artefacts active="when,~TYPE~,==,production">
  <folder>docs</folder>
</Artefacts>

<!-- Development-only files -->
<Artefacts active="when,~TYPE~,==,dev">
  <folder>debug-tools</folder>
</Artefacts>
```

### Working with External Tools

#### Custom Build Scripts

```xml
<build>
  <!-- Run custom PowerShell script (Windows) -->
  <exec app="powershell"
        args="-ExecutionPolicy Bypass -File build-assets.ps1"
        folder="scripts" />

  <!-- Run bash script (macOS/Linux) -->
  <exec app="bash" args="build-assets.sh" folder="scripts" />
</build>
```

#### Add Custom Tool Directories

```bash
# Add directories containing custom build tools
ReleaseBuilder --toolsdir /opt/custom-tools --toolsdir ~/bin
```

---

## Troubleshooting

### Problem: "Could not locate config file"

**Symptoms**: Error message when running ReleaseBuilder

**Solutions**:
1. Verify the file is named `ReleaseConfig.xml` exactly
2. Specify the path explicitly: `ReleaseBuilder --config path/to/ReleaseConfig.xml`
3. Check you're in the correct directory

### Problem: Variables Not Expanding

**Symptoms**: Seeing `~VERSION~` in output instead of actual version

**Solutions**:
1. Check variable name spelling
2. Ensure Git repository has tags (for `~SemVer~`)
3. Use `-vv` for extra verbose output to see variable values
4. Verify variables are defined before use

**Debug variables:**
```bash
ReleaseBuilder -vv | grep "SemVer"
```

### Problem: "Folder to clean required"

**Symptoms**: Error during clean operation

**Solutions**:
1. Ensure `folder` attribute is specified: `<clean folder="bin" />`
2. Check the folder path is correct
3. Use `~PUBLISHROOT~` for absolute paths

### Problem: Build Succeeds But Files Missing

**Symptoms**: Build completes but output is empty or missing files

**Solutions**:
1. Check your `<folder>` or `<file>` elements point to correct paths
2. Verify files exist before trying to include them
3. Use `--verbose` to see what files are being added
4. Check `match` patterns are correct (wildcards)

**Debug output:**
```bash
ReleaseBuilder --target dev -vv
```

### Problem: Executable Not Found

**Symptoms**: "Could not find executable" error

**Solutions**:
1. Specify full path: `<exec app="c:\tools\msbuild.exe" ... />`
2. Add to PATH or use `--toolsdir`
3. Check executable name (include `.exe` on Windows)

### Problem: Module Not Building

**Symptoms**: Using `--module` but module not found

**Solutions**:
1. Check module name matches the `name` attribute or `<Name>` element
2. Module names are case-insensitive
3. Use `-v` to see which modules are being processed

---

## Best Practices

### 1. Use Version Control for Configs

Store your `ReleaseConfig.xml` in Git alongside your code:

```bash
git add ReleaseConfig.xml
git commit -m "Add release configuration"
```

### 2. Use Variables for Paths

Don't hardcode paths:

**Bad:**
```xml
<Target name="production" path="C:\deploys\production" />
```

**Good:**
```xml
<Target name="production" path="$DEPLOY_PATH_PROD" />
```

### 3. Clean Before Building

Always start with a clean slate:

```xml
<build>
  <clean folder="bin" include-folders="true" />
  <clean folder="obj" include-folders="true" />
  <exec app="dotnet" args="build -c Release" />
</build>
```

### 4. Test Locally Before Production

Test your config with dev target first:

```bash
# Test locally
ReleaseBuilder --target dev -v

# Verify output
ls builds/

# Then build production
ReleaseBuilder --target production
```

### 5. Use Semantic Versioning

Let GitVersion manage your versions:

```bash
# Tag releases
git tag v1.0.0
git push --tags

# Build uses the tag automatically
ReleaseBuilder --target production
```

### 6. Document Your Config

Add XML comments to explain complex sections:

```xml
<Artefacts>
  <build>
    <!-- Clean previous builds to ensure reproducibility -->
    <clean folder="bin" include-folders="true" />

    <!-- Build for Release configuration
         Note: This uses .NET 8.0 target framework -->
    <exec app="dotnet" args="build -c Release" />
  </build>
</Artefacts>
```

### 7. Keep Secrets in Environment Variables

Never hardcode passwords or API keys:

**Bad:**
```xml
<exec args="push --api-key abc123xyz" />
```

**Good:**
```xml
<exec args="push --api-key $NUGET_API_KEY" />
```

### 8. Use Descriptive Target Names

Make it clear what each target is for:

```xml
<Target name="local-dev" path="builds" type="folder" />
<Target name="staging-server" path="$STAGING_PATH" type="folder" />
<Target name="production-release" path="releases" type="zip" />
```

### 9. Modularize Large Projects

Break complex builds into modules:

```
project/
  ReleaseConfig.xml           (main - orchestrates modules)
  API/ReleaseConfig.xml       (API module)
  Frontend/ReleaseConfig.xml  (Frontend module)
  Worker/ReleaseConfig.xml    (Worker module)
```

### 10. Validate Configs Before Committing

Use an XML editor with schema validation to catch errors early. Point it to `ReleaseConfig.xsd`.

---

## Next Steps

Now that you've learned the basics:

1. **Create your first config** - Start with a simple project
2. **Add targets** - Set up dev, staging, and production
3. **Explore the [User Reference Manual](User-Reference.md)** - Complete details on all features
4. **Check the [Format Reference](ReleaseConfig-Format.md)** - Detailed XML element documentation

## Getting Help

- **Verbose output**: Use `-v` or `-vv` flags to see what's happening
- **Help command**: `ReleaseBuilder --help`
- **Schema validation**: Use `ReleaseConfig.xsd` in your XML editor

Happy building!
