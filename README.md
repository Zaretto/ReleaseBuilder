# ReleaseBuilder

A cross-platform .NET 8.0 build automation tool that uses XML configuration files to orchestrate complex release processes. ReleaseBuilder provides a structured, validated approach to building, packaging, and deploying software artifacts with version management and multi-target support.

## Features

- **XML-based Configuration**: Strongly-typed XML configuration with XSD schema validation for reliable build definitions
- **Cross-Platform**: Runs on Windows (x64), macOS (x64 and ARM64) with .NET 8.0
- **Version Management**: Integrated GitVersion.Tool support for semantic versioning
- **Multi-Target Publishing**: Support for multiple build targets (e.g., live, staging, test)
- **Flexible Build Operations**:
  - File/folder copying with pattern matching
  - XML content transformation
  - Directory cleaning
  - External process execution
  - Archive creation (ZIP)
- **Modular Builds**: Selective module building with `--module` flag
- **Variable Substitution**: Dynamic variable expansion in configuration files
- **Verbose Logging**: Multiple verbosity levels for debugging

## Quick Start

### Prerequisites

1. **.NET 8.0 SDK** - https://dotnet.microsoft.com/download
2. **Git** - For GitVersion to analyze repository
3. **GitVersion.Tool** - `dotnet tool install --global GitVersion.Tool`

### Building ReleaseBuilder

**Windows:**
```cmd
dotnet restore
dotnet publish -p:PublishProfile=WindowsX64
```
Output: `publish\release\win-x64\ReleaseBuilder.exe`

**macOS Intel:**
```bash
dotnet restore
dotnet publish -p:PublishProfile=OSX-x64
```
Output: `publish/release/osx-x64/ReleaseBuilder`

**macOS Apple Silicon:**
```bash
dotnet restore
dotnet publish -p:PublishProfile=OSX-arm64
```
Output: `publish/release/osx-arm64/ReleaseBuilder`

**Linux:**
```bash
dotnet restore
dotnet publish -c Release -r linux-x64 --self-contained true
```
Output: `bin/Release/net8.0/linux-x64/publish/ReleaseBuilder`

See [User Guide: Installation](documentation/User-Guide.md#installation) for detailed platform-specific instructions.

## Usage

```bash
ReleaseBuilder [options]
```

### Command-Line Options

- `-r, --root <directory>` - Root folder for the build (defaults to current directory)
- `-c, --config <file>` - Path to ReleaseConfig.xml file
- `-t, --target <name>` - Target to build (default: "live")
- `-p, --toolsdir <directory>` - Add path to search for tools (can be specified multiple times)
- `-m, --module <name>` - Build only specific modules (can be specified multiple times)
- `-n, --nobuild` - Skip building artifacts
- `-s, --shell-exec` - Use ShellExecute for process execution
- `-v, --verbose` - Increase verbosity (use twice for extra verbose)
- `-h, --help` - Show help information

### Example

```bash
# Build release target with verbose output
ReleaseBuilder --root /path/to/project --config ReleaseConfig.xml --target release -v

# Build only specific modules
ReleaseBuilder --module ModuleA --module ModuleB --nobuild
```

## Configuration

ReleaseBuilder looks for `ReleaseConfig.xml` in the following order:
1. File specified with `--config` option
2. `ReleaseConfig.xml` in the root directory
3. `ReleaseConfig.xml` in the current directory

## Documentation

ReleaseBuilder includes comprehensive documentation to help you get started and master all features:

### Getting Started
- **[User Guide](documentation/User-Guide.md)** - Step-by-step tutorials, common tasks, and best practices. **Start here if you're new to ReleaseBuilder.**

### Reference Documentation
- **[User Reference Manual](documentation/User-Reference.md)** - Complete command-line and configuration reference with detailed specifications
- **[ReleaseConfig.xml Format Reference](documentation/ReleaseConfig-Format.md)** - Detailed XML format documentation with extensive examples
- **[ReleaseConfig.xsd](ReleaseConfig.xsd)** - XML Schema Definition for validation in your editor

### Quick Links by Task
- **First time user?** → [User Guide: Getting Started](documentation/User-Guide.md#getting-started)
- **Need a specific example?** → [User Guide: Tutorials](documentation/User-Guide.md#tutorials)
- **Looking for command-line options?** → [User Reference: Command-Line Reference](documentation/User-Reference.md#command-line-reference)
- **Need XML element details?** → [Format Reference: Elements](documentation/ReleaseConfig-Format.md#elements-reference)
- **Troubleshooting an issue?** → [User Guide: Troubleshooting](documentation/User-Guide.md#troubleshooting)

### Quick Start Example

```xml
<ReleaseConfig>
  <Name>MyProject</Name>

  <Target name="live" path="releases" type="zip">
    <Set name="ENV" value="production" />
  </Target>

  <Artefacts>
    <build>
      <clean folder="bin" />
      <exec app="dotnet" args="build -c Release" />
      <copy from="bin/Release" to="output" match="*.dll" />
    </build>
    <folder>output</folder>
  </Artefacts>
</ReleaseConfig>
```

See the [User Guide](documentation/User-Guide.md) for tutorials and [User Reference Manual](documentation/User-Reference.md) for complete details.

## Version Management with GitVersion

ReleaseBuilder integrates seamlessly with GitVersion for automatic semantic versioning:

```bash
# Install GitVersion.Tool
dotnet tool install --global GitVersion.Tool

# Initialize in your repository
dotnet-gitversion init

# Tag a release
git tag v1.0.0
git push --tags
```

**How It Works:**
- ReleaseBuilder automatically executes `dotnet-gitversion` at startup
- GitVersion analyzes your Git tags, branches, and commit history
- Version information becomes available as variables: `~SemVer~`, `~Major~`, `~Minor~`, `~Patch~`, etc.
- Use in configs: `<Target archive-version="~SemVer~" />`

**Available Version Variables:**
- `~SemVer~` - Semantic version (e.g., `1.2.3`)
- `~Major~`, `~Minor~`, `~Patch~` - Individual version components
- `~BranchName~` - Current Git branch
- `~Sha~` - Commit SHA
- `~CommitsSinceVersionSource~` - Commits since last tag
- And many more...

See [User Guide: Version Management](documentation/User-Guide.md#version-management-with-gitversion) for complete details.

## Platform Support

ReleaseBuilder runs on:
- **Windows** - x64 (Windows 10/11, Windows Server 2016+)
- **macOS** - Intel (x64) and Apple Silicon (ARM64)
- **Linux** - x64 and ARM64 (Ubuntu, Debian, RHEL, CentOS, etc.)

**Requirements:**
- .NET 8.0 SDK (for building) or Runtime (for running)
- Git
- GitVersion.Tool (`dotnet tool install --global GitVersion.Tool`)

## License

This project is licensed under the GNU General Public License v2.0 (GPL-2.0).
