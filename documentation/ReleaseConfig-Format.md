# ReleaseConfig.xml Format Documentation

This document provides comprehensive documentation for the ReleaseConfig.xml format used by ReleaseBuilder.

## Table of Contents

1. [Overview](#overview)
2. [Root Element](#root-element)
3. [Variable Substitution](#variable-substitution)
4. [Built-in Variables](#built-in-variables)
5. [Elements Reference](#elements-reference)
   - [Name](#name)
   - [Folder](#folder)
   - [Target](#target)
   - [Artefacts](#artefacts)
   - [Artefact](#artefact)
   - [ReleaseBuilder](#releasebuilder)
6. [Build Actions](#build-actions)
7. [Transform Functions](#transform-functions)
8. [Examples](#examples)

## Overview

ReleaseConfig.xml is an XML-based configuration file that defines how to build, package, and deploy software artifacts. It supports:

- Multiple build targets (live, dev, test, etc.)
- Version management with GitVersion integration
- Complex build operations (copy, transform, execute)
- Nested/modular builds
- Variable substitution and transformations

## Root Element

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig xmlns:p1="http://www.w3.org/2001/XMLSchema-instance"
               p1:noNamespaceSchemaLocation="file:///c:/xml-schemas/ReleaseConfig.xsd">
  <!-- Configuration elements here -->
</ReleaseConfig>
```

## Variable Substitution

Variables can be referenced using the tilde syntax: `~VARIABLE_NAME~`

Variables are expanded in:
- File paths
- Folder paths
- Arguments
- Text content
- Attribute values

**Example:**
```xml
<exec app="dotnet.exe" args="build -c Release" folder="~PUBLISHROOT~"/>
<copy from="bin\Release" to="output-~TYPE~"/>
```

Environment variables can be referenced using `$VARIABLE_NAME` syntax:
```xml
<exec args="push package.nupkg --api-key $NUGET_API_KEY"/>
<Target name="live" path="$PATH_52"/>
```

## Built-in Variables

ReleaseBuilder automatically provides these variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `TYPE` | Current target name | `live`, `dev`, `test` |
| `PUBLISHROOT` | Root directory specified with -r or current directory | `/path/to/project` |
| `SemVer` | Semantic version from GitVersion | `1.2.3` |
| `VERSION` | NuGetVersionV2 or MajorMinorPatch from GitVersion | `1.2.3` |
| `TARGETPATH` | Path of the active target | `/output/releases` |
| `GITVERSION.JSON` | Full GitVersion JSON output | JSON object with all version info |

### GitVersion Integration

ReleaseBuilder requires GitVersion.Tool to function.

**Install GitVersion:**
```bash
dotnet tool install --global GitVersion.Tool
```

**How It Works:**
1. ReleaseBuilder executes `dotnet-gitversion` in your project directory at startup
2. GitVersion analyzes Git tags, branches, and commit history
3. Returns JSON with comprehensive version information
4. All string properties become available as variables

**All GitVersion Variables:**

| Variable | Description | Example |
|----------|-------------|---------|
| `SemVer` | Semantic version | `1.2.3`, `1.2.3-alpha.1` |
| `Major` | Major version number | `1` |
| `Minor` | Minor version number | `2` |
| `Patch` | Patch version number | `3` |
| `MajorMinorPatch` | Standard version format | `1.2.3` |
| `PreReleaseTag` | Pre-release identifier | `alpha.1`, `beta.5` |
| `FullSemVer` | Full semantic version with metadata | `1.2.3-alpha.1+5` |
| `BranchName` | Current Git branch | `main`, `develop` |
| `Sha` | Git commit SHA (short) | `abc1234` |
| `CommitsSinceVersionSource` | Commits since last version tag | `5` |
| `CommitDate` | Date of commit | `2024-01-15` |
| `NuGetVersion` | NuGet-compatible version | `1.2.3-alpha.1` |
| `NuGetVersionV2` | NuGet v2 version | `1.2.3-alpha0001` |
| `AssemblySemVer` | Assembly version | `1.2.0.0` |
| `AssemblySemFileVer` | Assembly file version | `1.2.3.0` |
| `InformationalVersion` | Full version with all metadata | `1.2.3-alpha.1+Branch.main.Sha.abc` |

See [User Reference Manual](User-Reference.md#variable-system) for complete list.

**Setup GitVersion for Your Project:**

```bash
# Initialize GitVersion in your repository
dotnet-gitversion init

# Tag your first release
git tag v1.0.0
git push --tags

# ReleaseBuilder will now use version 1.0.0
```

**Without GitVersion:**
If GitVersion is not installed, **ReleaseBuilder will fail to start** with an error. GitVersion.Tool is a required dependency.

**Further Reading:**
- [GitVersion Documentation](https://gitversion.net/)
- [User Guide: Version Management](User-Guide.md#version-management-with-gitversion)

## Elements Reference

### Name

Specifies the name of the release configuration.

**Syntax:**
```xml
<Name>ProjectName</Name>
```

**Attributes:** None

**Example:**
```xml
<Name>MyApplication</Name>
```

---

### Folder

Defines a variable that points to a folder selected from a search pattern. Used to locate folders with version numbers or timestamps.

**Syntax:**
```xml
<Folder name="VARIABLE_NAME"
        path="search_pattern"
        version="selection_method"
        name-version="VERSION_VAR" />
```

**Attributes:**

| Attribute | Required | Description | Values |
|-----------|----------|-------------|--------|
| `name` | Yes | Variable name to create | Any string |
| `path` | Yes | Search pattern for folder | Wildcard pattern (e.g., `MyApp_*`) |
| `version` | No | How to select from multiple matches | `latest`, `oldest`, `name`, `last-name` (default: `latest`) |
| `name-version` | No | Variable to store extracted version | Any string |

**Selection Methods:**
- `latest` - Most recently created folder
- `oldest` - Oldest created folder
- `name` - First alphabetically
- `last-name` - Last alphabetically

**Example:**
```xml
<!-- Find the most recent build folder -->
<Folder name="APPPATH" path="MyApp.UWP_*" version="latest" />

<!-- Extract version number from folder name -->
<Folder name="BUILDPATH"
        path="Build_*"
        version="last-name"
        name-version="BUILD_VERSION" />
```

---

### Target

Defines a deployment target with output configuration.

**Syntax:**
```xml
<Target name="target_name"
        path="output_path"
        type="output_type"
        archive-version="version_string">
  <Set name="VAR_NAME" value="value" />
</Target>
```

**Attributes:**

| Attribute | Required | Description | Values |
|-----------|----------|-------------|--------|
| `name` | Yes | Target identifier (used with -t flag) | Any string (e.g., `live`, `dev`, `test`) |
| `path` | Yes | Output directory or zip file path | Path (supports variables) |
| `type` | No | Output type | `zip` (default), `folder` |
| `archive-version` | No | Version string for archive name | String with variables/transforms |

**Child Elements:**
- `<Set>` - Define target-specific variables (only active when this target is selected)

**Example:**
```xml
<!-- Create a ZIP file -->
<Target name="live" path="releases" type="zip" archive-version="~SemVer~">
  <Set name="ENV" value="production" />
  <Set name="API_URL" value="https://api.prod.example.com" />
</Target>

<!-- Copy to a folder -->
<Target name="dev" path="$DEV_DEPLOY_PATH" type="folder">
  <Set name="ENV" value="development" />
  <Set name="API_URL" value="https://api.dev.example.com" />
</Target>
```

---

### Artefacts

Defines a collection of artifacts to build and/or include in the release.

**Syntax:**
```xml
<Artefacts folder="working_directory" active="condition">
  <build>
    <!-- Build actions -->
  </build>
  <file>path/to/file.ext</file>
  <folder>path/to/folder</folder>
</Artefacts>
```

**Attributes:**

| Attribute | Required | Description |
|-----------|----------|-------------|
| `folder` | No | Working directory for build actions (relative to root) |
| `active` | No | Conditional expression to enable/disable this artifact group |

**Child Elements:**
- `<build>` - Build actions to execute
- `<file>` - Single file to include
- `<folder>` - Folder to include

**Example:**
```xml
<!-- Build and package application -->
<Artefacts>
  <build>
    <exec app="msbuild.exe" args="MyApp.csproj /p:Configuration=Release" />
  </build>
</Artefacts>

<!-- Include files from a specific folder -->
<Artefacts folder="release-~TYPE~">
  <build>
    <clean folder="." />
    <copy from="bin\Release" match="*.exe" />
    <copy from="bin\Release" match="*.dll" />
  </build>
  <folder>release-~TYPE~</folder>
</Artefacts>

<!-- Conditional artifact (only when variable is set) -->
<Artefacts active="~INCLUDE_DOCS~">
  <folder>documentation</folder>
</Artefacts>
```

**file Element Attributes:**

| Attribute | Description |
|-----------|-------------|
| `folder` | Subdirectory containing the file |
| `newname` | Rename the file in the output |
| `skip-directories-front` | Number of leading directories to skip in output path |
| `name` | Original filename |

**folder Element Attributes:**

| Attribute | Description |
|-----------|-------------|
| `skip-directories-front` | Number of leading directories to skip in output path |

---

### Artefact

Alternative element for defining individual artifacts (legacy/alternative syntax).

**Syntax:**
```xml
<Artefact folder="source_folder"
          directory="directory_path"
          file="file_path" />
```

**Attributes:**

| Attribute | Description |
|-----------|-------------|
| `folder` | Source folder |
| `directory` | Directory to include |
| `file` | File to include |

**Example:**
```xml
<Artefact file="readme.txt" />
<Artefact directory="config" folder="settings" />
```

---

### ReleaseBuilder

Recursively invokes ReleaseBuilder on a nested configuration file, enabling modular builds.

**Syntax:**
```xml
<ReleaseBuilder name="module_name"
                folder="subfolder"
                file="config_file.xml"
                process="true|false" />
```

**Attributes:**

| Attribute | Required | Description |
|-----------|----------|-------------|
| `name` | No | Module name (for filtering with --module) |
| `folder` | Yes | Subfolder containing the nested config |
| `file` | No | Config filename (default: `ReleaseConfig.xml` or `ReleaseConfig{name}.xml`) |
| `process` | No | Whether to process artifacts (default: `false`) |

**How Configuration Chaining Works:**

The `<ReleaseBuilder>` element enables building complex multi-component projects by chaining configurations together:

1. **Changes directory** to the specified `folder`
2. **Loads** the ReleaseConfig.xml in that folder (or specified `file`)
3. **Executes** that configuration completely (recursive invocation)
4. **Collects artifacts** if `process="true"`
5. **Returns** to parent directory and continues

**Module Filtering:**

When `--module` is specified on the command line:
- Checks `name` attribute (if present) or `<Name>` element in nested config
- Case-insensitive substring matching
- Only builds modules that match

**Examples:**

**Example 1: Basic Multi-Module Build**
```xml
<!-- Main: ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>MyEnterpriseSolution</Name>

  <Target name="production" path="releases" type="zip" />

  <!-- Chain to component configs -->
  <ReleaseBuilder folder="Dashboard" process="true" />
  <ReleaseBuilder folder="Background" process="true" />
  <ReleaseBuilder folder="API" process="true" />
</ReleaseConfig>

<!-- Dashboard/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>Dashboard</Name>
  <Artefacts>
    <build>
      <exec app="msbuild.exe" args="dashboard.csproj /p:Configuration=Release" />
    </build>
  </Artefacts>
</ReleaseConfig>

<!-- Background/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>Background</Name>
  <Artefacts>
    <build>
      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>
```

Usage:
```bash
# Build all components
ReleaseBuilder --target production

# Build only Dashboard and Background
ReleaseBuilder --module Dashboard --module Background --target production

# Build only API
ReleaseBuilder --module API --target production
```

**Example 2: Named Modules for Better Control**
```xml
<ReleaseConfig>
  <Name>MyProduct</Name>

  <Target name="dev" path="builds" type="folder" />

  <!-- Use 'name' attribute for explicit module names -->
  <ReleaseBuilder name="BackendAPI" folder="api" process="true" />
  <ReleaseBuilder name="WebUI" folder="frontend" process="true" />
  <ReleaseBuilder name="BackgroundWorker" folder="worker" process="true" />
  <ReleaseBuilder name="DatabaseMigrations" folder="migrations" process="false" />
</ReleaseConfig>
```

```bash
# Build only backend components
ReleaseBuilder --module Backend
# Matches: BackendAPI, BackgroundWorker (both contain "Backend")

# Build only UI
ReleaseBuilder --module WebUI

# Run migrations without including artifacts
ReleaseBuilder --module DatabaseMigrations
# Executes but doesn't collect artifacts (process="false")
```

**Example 3: Nested Chaining (Multi-Level)**
```xml
<!-- Level 1: Main ReleaseConfig.xml -->
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
  <ReleaseBuilder folder="Worker" process="true" />
</ReleaseConfig>

<!-- Level 3: ProductA/API/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>ProductA.API</Name>
  <Artefacts>
    <build>
      <exec app="dotnet" args="build -c Release" />
    </build>
    <folder>bin/Release</folder>
  </Artefacts>
</ReleaseConfig>
```

Execution hierarchy: `Main → ProductA → (API, UI, Worker)`

**Example 4: Custom Config Filenames**
```xml
<ReleaseConfig>
  <Name>MyProject</Name>

  <!-- Use custom config filename -->
  <ReleaseBuilder folder="component-a"
                  file="BuildConfig.xml"
                  process="true" />

  <!-- Auto-detect: tries ReleaseConfigAPI.xml first -->
  <ReleaseBuilder name="API"
                  folder="backend"
                  process="true" />
</ReleaseConfig>
```

**Key Behaviors:**

| Behavior | Description |
|----------|-------------|
| **Target Inheritance** | Nested configs use the same `--target` name as parent |
| **Variable Inheritance** | **All variables ARE inherited** from parent to child |
| **Set Variables Inherited** | `<Set>` variables from parent's active Target are passed to children |
| **Target Override** | Child can define its own `<Target>` to change output path/type |
| **Module Filtering** | Applied at each nesting level |
| **Working Directory** | Each config executes in its own folder |
| **Error Propagation** | If any nested build fails, entire build fails |

**Variable Inheritance:**

When parent chains to child, the child inherits:
- All `<Folder>` variables from parent
- All `<Set>` variables from parent's active `<Target>`
- All GitVersion variables (`~SemVer~`, `~Major~`, etc.)
- All built-in variables (`~TYPE~`, `~PUBLISHROOT~`, etc.)

This allows you to define common configuration in the parent (like environment URLs, database servers, feature flags) and have all children use those same values.

**Target Override:**

Children can override the parent's `<Target>` to use different output paths. This is common when building multiple platform artifacts:

```xml
<!-- Parent sets default target -->
<Target name="production" path="releases/api" type="zip">
  <Set name="ENV" value="production" />
</Target>

<!-- Child can override to use different path -->
<!-- AndroidApp/ReleaseConfig.xml -->
<Target name="production" path="releases/android" type="folder" />
<!-- Still inherits ENV variable! -->
```

**process Attribute:**
- `process="true"` - Include artifacts from nested build in parent package
- `process="false"` - Execute nested build but don't collect artifacts (useful for migrations, tests, etc.)

**Example 5: WebAPI + Android App (Different Output Paths)**

Common scenario: Building both a web API and mobile app that need different output locations.

```xml
<!-- Main: ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>MobileAppSolution</Name>

  <!-- Default target for WebAPI -->
  <Target name="production" path="releases/api" type="zip" archive-version="~SemVer~">
    <Set name="ENV" value="production" />
    <Set name="API_URL" value="https://api.example.com" />
    <Set name="DB_SERVER" value="prod-db.example.com" />
    <Set name="ENABLE_ANALYTICS" value="true" />
  </Target>

  <Target name="staging" path="releases/staging" type="folder">
    <Set name="ENV" value="staging" />
    <Set name="API_URL" value="https://api-staging.example.com" />
    <Set name="DB_SERVER" value="staging-db.example.com" />
    <Set name="ENABLE_ANALYTICS" value="false" />
  </Target>

  <!-- Build both components -->
  <ReleaseBuilder folder="WebAPI" process="true" />
  <ReleaseBuilder folder="AndroidApp" process="true" />
</ReleaseConfig>

<!-- WebAPI/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>WebAPI</Name>

  <!-- No Target defined - uses parent's Target -->
  <!-- For production: outputs to releases/api/MobileAppSolution-1.2.3.zip -->

  <!-- Inherits all parent variables:
       ENV, API_URL, DB_SERVER, ENABLE_ANALYTICS, SemVer, etc. -->

  <Artefacts>
    <build>
      <!-- Use inherited variables in config -->
      <create file="appsettings.Production.json">
{
  "Environment": "~ENV~",
  "ApiUrl": "~API_URL~",
  "ConnectionStrings": {
    "DefaultConnection": "Server=~DB_SERVER~;Database=MyApp"
  },
  "Analytics": {
    "Enabled": ~ENABLE_ANALYTICS~
  },
  "Version": "~SemVer~"
}
      </create>

      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>

<!-- AndroidApp/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>AndroidApp</Name>

  <!-- Override Target - APK goes to different location -->
  <Target name="production" path="releases/android" type="folder" />
  <Target name="staging" path="releases/android-staging" type="folder" />

  <!-- STILL inherits all parent variables! -->
  <!-- ENV, API_URL, DB_SERVER, ENABLE_ANALYTICS are available -->

  <Artefacts>
    <build>
      <!-- Generate Android config using parent variables -->
      <create file="app/src/main/assets/config.properties">
environment=~ENV~
apiUrl=~API_URL~
analyticsEnabled=~ENABLE_ANALYTICS~
version=~SemVer~
versionCode=~Major~~Minor~~Patch~
      </create>

      <!-- Build would happen here via gradle or other tool -->

      <!-- Copy the built APK -->
      <copy from="../mobile/Android/app/release/app-release.apk"
            to="."
            name="MyApp-~ENV~-~SemVer~.apk" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

**Build Commands:**
```bash
# Build for production
ReleaseBuilder --target production

# Results:
# - WebAPI → releases/api/MobileAppSolution-1.2.3.zip
# - AndroidApp → releases/android/MyApp-production-1.2.3.apk
# Both use same ENV, API_URL, etc. from parent

# Build for staging
ReleaseBuilder --target staging

# Results:
# - WebAPI → releases/staging/ (folder)
# - AndroidApp → releases/android-staging/MyApp-staging-1.2.3.apk
```

**Key Benefits:**
1. **Single source of truth** - Define `ENV`, `API_URL`, etc. once in parent
2. **Consistent configuration** - All components use same environment values
3. **Flexible output** - Each component can go to appropriate location
4. **Environment switching** - Change `--target` to switch entire solution

**See Also:**
- [User Guide: Multi-Component Tutorial](User-Guide.md#tutorial-3-multi-component-application-with-chaining)
- [User Reference: --module Option](User-Reference.md#-m---module-name)
- [User Reference: ReleaseBuilder Element](User-Reference.md#element-releasebuilder)

---

## Build Actions

Build actions are executed within `<build>` elements inside `<Artefacts>`.

### clean

Removes files from a directory.

**Syntax:**
```xml
<clean folder="path" match="pattern" include-folders="true|false" />
```

**Attributes:**

| Attribute | Required | Default | Description |
|-----------|----------|---------|-------------|
| `folder` | Yes | - | Folder to clean |
| `match` | No | `*.*` | File pattern to delete |
| `include-folders` | No | `false` | Also delete subdirectories |

**Example:**
```xml
<clean folder="bin" />
<clean folder="temp" match="*.tmp" />
<clean folder="output" include-folders="true" />
```

---

### copy

Copies files from one location to another with optional transformations.

**Syntax:**
```xml
<copy from="source"
      to="destination"
      match="pattern"
      name="new_filename"
      transform="transformation"
      recursive="true|false">
  <transform-content transform="transformation" />
</copy>
```

**Attributes:**

| Attribute | Required | Default | Description |
|-----------|----------|---------|-------------|
| `from` | Yes | - | Source file or directory |
| `to` | No | Current dir | Destination directory |
| `match` | No | `*.*` | File pattern to copy |
| `name` | No | - | Rename single file |
| `transform` | No | - | Transform filename (see [Transform Functions](#transform-functions)) |
| `recursive` | No | `false` | Include subdirectories |

**Child Elements:**
- `<transform-content transform="...">` - Transform file contents during copy

**Examples:**
```xml
<!-- Copy all DLLs -->
<copy from="bin\Release" to="output" match="*.dll" />

<!-- Copy and rename single file -->
<copy from="app.exe" to="bin" name="MyApp-~VERSION~.exe" />

<!-- Copy with filename transformation -->
<copy from="bin" to="output" match="*.txt" transform="replace,.txt,-~VERSION~.txt" />

<!-- Copy recursively -->
<copy from="assets" to="output\assets" match="*.*" recursive="true" />

<!-- Copy with content transformation -->
<copy from="config.json" to="output">
  <transform-content transform="replace,%%VERSION%%,~VERSION~" />
</copy>
```

**Common Use Cases:**

**1. Environment-Specific Configuration Files:**

A common pattern is to maintain separate configuration files for each target environment in a predefined folder structure, then copy the appropriate config based on the active target:

```xml
<Target name="production" path="releases" type="zip">
  <Set name="ENV" value="production" />
</Target>

<Target name="dev" path="builds" type="folder">
  <Set name="ENV" value="dev" />
</Target>

<Artefacts>
  <build>
    <!-- Build the application -->
    <exec app="dotnet" args="publish -c Release -o publish" />

    <!-- Copy environment-specific config from configs/{ENV}/ to the build -->
    <copy from="configs/~ENV~/appsettings.json" to="publish" />
    <copy from="configs/~ENV~/web.config" to="publish" />
  </build>
  <folder>publish</folder>
</Artefacts>
```

Directory structure:
```
project/
  configs/
    production/
      appsettings.json
      web.config
    dev/
      appsettings.json
      web.config
  publish/
```

**2. Building a Release Tree:**

Instead of using a build output folder directly, you can use `copy` to construct a complete release directory structure with custom organization:

```xml
<Artefacts folder="release-tree">
  <build>
    <!-- Clean the release tree -->
    <clean folder="." include-folders="true" />

    <!-- Build the release tree structure -->
    <copy from="../src/bin/Release" to="bin" match="*.exe" />
    <copy from="../src/bin/Release" to="bin" match="*.dll" />
    <copy from="../docs" to="documentation" recursive="true" />
    <copy from="../LICENSE.txt" to="." />
    <copy from="../README.md" to="." />

    <!-- Create config -->
    <create file="config/app.config">
      <!-- config content -->
    </create>
  </build>
  <folder>release-tree</folder>
</Artefacts>
```

**3. Using Publish Folder:**

For .NET applications, you can either use the `dotnet publish` output folder directly, or copy from it to build custom structure:

```xml
<!-- Option A: Use publish folder directly -->
<Artefacts>
  <build>
    <exec app="dotnet" args="publish -c Release -o publish" />
  </build>
  <folder>publish</folder>
</Artefacts>

<!-- Option B: Copy from publish folder to build custom structure -->
<Artefacts folder="custom-output">
  <build>
    <exec app="dotnet" args="publish -c Release -o ../temp-publish" />

    <!-- Copy selectively from publish folder -->
    <copy from="../temp-publish" to="bin" match="*.dll" />
    <copy from="../temp-publish/MyApp.exe" to="bin" />

    <!-- Add additional files not in publish folder -->
    <copy from="../scripts" to="scripts" recursive="true" />
    <copy from="configs/~ENV~" to="config" recursive="true" />

    <!-- Clean up temp folder -->
    <clean folder="../temp-publish" include-folders="true" />
  </build>
  <folder>custom-output</folder>
</Artefacts>
```

---

### exec

Executes an external process.

**Syntax:**
```xml
<exec app="executable"
      args="arguments"
      folder="working_directory"
      log-stdout="true|false" />
```

**Attributes:**

| Attribute | Required | Default | Description |
|-----------|----------|---------|-------------|
| `app` | Yes | - | Executable name or path |
| `args` | No | - | Command-line arguments |
| `folder` | No | Current | Working directory |
| `log-stdout` | No | `false` | Log stdout output at Info level |

The tool searches for executables in:
1. Directories specified with `--toolsdir`
2. Application base directory
3. System PATH

**Examples:**
```xml
<!-- Build with MSBuild -->
<exec app="msbuild.exe" args="MyApp.csproj /p:Configuration=Release" />

<!-- Run dotnet command -->
<exec app="dotnet.exe"
      args="publish -c Release -r win-x64"
      folder="~PUBLISHROOT~/src" />

<!-- Execute with environment variable -->
<exec app="dotnet.exe"
      args="nuget push package.~SemVer~.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json"
      folder="bin/release"
      log-stdout="true" />

<!-- Angular build -->
<exec app="node.exe"
      args="node_modules\@angular\cli\bin\ng build --configuration production"
      folder="~PUBLISHROOT~"
      log-stdout="true" />
```

---

### create

Creates a text file with specified content.

**Syntax:**
```xml
<create file="filename">
  File content here
  Variables like ~VERSION~ are expanded
</create>
```

**Attributes:**

| Attribute | Required | Description |
|-----------|----------|-------------|
| `file` | Yes | Path to file to create |

**Examples:**
```xml
<!-- Create version file -->
<create file="version.txt">~SemVer~</create>

<!-- Create TypeScript version file -->
<create file="src\app\appVersionDetails.ts">
  export const appVersionDetails = ~GITVERSION.JSON~;
</create>

<!-- Create config file -->
<create file="config.ini">
[app]
version=~VERSION~
environment=~TYPE~
build_date=~BuildDate~
</create>
```

---

### xml-edit

Edits XML files using XPath selectors.

**Syntax:**
```xml
<xml-edit file="filename" omit-declaration="true|false">
  <node path="xpath_expression" action="transformation" />
</xml-edit>
```

**Attributes:**

| Attribute | Required | Default | Description |
|-----------|----------|---------|-------------|
| `file` | Yes | - | XML file to edit |
| `omit-declaration` | No | `false` | Omit XML declaration when saving |

**Child Elements:**
- `<node path="..." action="...">` - Modify nodes matching XPath

**node Attributes:**

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | Yes | XPath expression to select nodes |
| `action` | Yes | Transformation to apply to node value |

**Examples:**
```xml
<!-- Update version in XML -->
<xml-edit file="package.config">
  <node path="//package/@version" action="set,~VERSION~" />
</xml-edit>

<!-- Replace text in multiple nodes -->
<xml-edit file="web.config">
  <node path="//appSettings/add[@key='Environment']/@value" action="set,~TYPE~" />
  <node path="//connectionStrings/add/@connectionString"
        action="replace,localhost,prodserver.com" />
</xml-edit>
```

---

## Transform Functions

Transformations are used in various contexts (filename transforms, XML edits, content transforms). They use comma-separated syntax: `function,arg1,arg2,...`

### set

Sets a value, replacing the original.

**Syntax:** `set,new_value`

**Example:**
```xml
<node path="//version" action="set,~VERSION~" />
```

---

### replace

String replacement.

**Syntax:** `replace,search,replacement`

**Example:**
```xml
<!-- Replace text in filename -->
<copy from="app.exe" transform="replace,.exe,-~VERSION~.exe" />

<!-- Replace in XML node -->
<node path="//server" action="replace,localhost,production.example.com" />
```

---

### regex-replace

Regular expression replacement.

**Syntax:** `regex-replace,pattern,replacement`

**Example:**
```xml
<!-- Replace version pattern -->
<node path="//version" action="regex-replace,\d+\.\d+\.\d+,~VERSION~" />

<!-- Update all localhost URLs -->
<transform-content transform="regex-replace,http://localhost:\d+,https://api.example.com" />
```

---

### getversion

Extracts version number from a path or string.

**Syntax:** `getversion,path`

Extracts digits and dots pattern (e.g., `1.2.3.4`) from the last path component.

**Example:**
```xml
<Target name="live" archive-version="getversion,~APPPATH~" />
<!-- If APPPATH = "C:\builds\MyApp_1.2.3.4" then archive-version becomes "1.2.3.4" -->
```

---

### when

Conditional transformation (returns empty string if condition fails).

**Syntax:** `when,value1,operator,value2`

**Operators:**
- `==` - Equals
- `!=` - Not equals
- `>` - Greater than
- `<` - Less than
- `>=` - Greater than or equal
- `<=` - Less than or equal

**Example:**
```xml
<!-- Only process if TYPE is "live" -->
<Artefacts active="when,~TYPE~,==,live">
  <folder>production-only-files</folder>
</Artefacts>
```

---

## Examples

### Example 1: Simple .NET Application Build

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MyDotNetApp</Name>

  <Target name="live" path="releases" type="zip" />
  <Target name="dev" path="dev-builds" type="folder" />

  <Artefacts>
    <build>
      <exec app="dotnet.exe" args="build -c Release" />
      <exec app="dotnet.exe" args="publish -c Release -o publish" />
    </build>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>
```

### Example 2: NuGet Package Publishing

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>OdsReaderWriter</Name>

  <Artefacts>
    <build>
      <exec app="dotnet.exe" args="build -c Release" folder="~PUBLISHROOT~" />
      <exec app="dotnet.exe"
            args="pack OdsReaderWriter.csproj -c Release"
            folder="~PUBLISHROOT~\OdsReaderWriter" />
      <exec app="dotnet.exe"
            args="nuget push OdsReaderWriter.~SemVer~.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json"
            folder="~PUBLISHROOT~\OdsReaderWriter\bin\release\" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

### Example 3: Angular Application Build

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>WebUI</Name>

  <Target name="test" path="$SYNC_TEST_PATH" />
  <Target name="live" path="$SYNC_PROD_PATH" />

  <Artefacts>
    <build>
      <!-- Create version file -->
      <create file="src\app\appVersionDetails.ts">
        export const appVersionDetails = ~GITVERSION.JSON~;
      </create>

      <!-- Copy environment-specific config -->
      <copy from="env-~TYPE~" match="*.*" to="src\environments" />

      <!-- Build Angular app -->
      <exec app="node.exe"
            args="node_modules\@angular\cli\bin\ng build --configuration production"
            folder="~PUBLISHROOT~"
            log-stdout="true" />
    </build>
    <folder>dist</folder>
  </Artefacts>
</ReleaseConfig>
```

### Example 4: Multi-Module Build

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>CompanyAPI</Name>

  <Target name="live" path="$DEPLOY_PATH_LIVE" />
  <Target name="dev" path="$DEPLOY_PATH_DEV" />

  <!-- Build three separate modules -->
  <ReleaseBuilder folder="Dashboard" process="true" />
  <ReleaseBuilder folder="Background" process="true" />
  <ReleaseBuilder folder="ETool" process="true" />
</ReleaseConfig>
```

Module config (`Dashboard/ReleaseConfig.xml`):
```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>Dashboard</Name>

  <Target name="live" path="$DEPLOY_PATH_LIVE" />
  <Target name="dev" path="$DEPLOY_PATH_DEV" />

  <Artefacts>
    <build>
      <exec app="msbuild.exe"
            args="dashboard.csproj /p:Configuration=Release /p:DeployOnBuild=true" />
    </build>
  </Artefacts>

  <Artefacts>
    <folder>../deployments/dashboard-~TYPE~</folder>
  </Artefacts>
</ReleaseConfig>
```

### Example 5: Complex Build with Transformations

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>EnterpriseApp</Name>

  <Folder name="APPPATH" path="Build_*" version="last-name" name-version="BUILD_VER" />

  <Target name="live" path="releases" type="zip" archive-version="~SemVer~">
    <Set name="ENV" value="production" />
    <Set name="API_URL" value="https://api.prod.example.com" />
  </Target>

  <Target name="staging" path="staging" type="folder">
    <Set name="ENV" value="staging" />
    <Set name="API_URL" value="https://api.staging.example.com" />
  </Target>

  <Artefacts>
    <build>
      <!-- Clean output -->
      <clean folder="dist" include-folders="true" />

      <!-- Build application -->
      <exec app="msbuild.exe"
            args="App.csproj /p:Configuration=Release /p:Version=~SemVer~" />

      <!-- Copy and transform config -->
      <copy from="config.template.json" to="dist" name="config.json">
        <transform-content transform="replace,%%API_URL%%,~API_URL~" />
        <transform-content transform="replace,%%VERSION%%,~SemVer~" />
        <transform-content transform="replace,%%ENV%%,~ENV~" />
      </copy>

      <!-- Copy binaries -->
      <copy from="bin\Release" to="dist" match="*.dll" />
      <copy from="bin\Release" to="dist" match="*.exe" />

      <!-- Edit app.config -->
      <xml-edit file="dist\app.config">
        <node path="//appSettings/add[@key='Version']/@value" action="set,~SemVer~" />
        <node path="//appSettings/add[@key='Environment']/@value" action="set,~ENV~" />
      </xml-edit>

      <!-- Create version file -->
      <create file="dist\VERSION.txt">
Version: ~SemVer~
Build: ~BUILD_VER~
Target: ~TYPE~
Date: ~BuildDate~
      </create>
    </build>

    <folder>dist</folder>
  </Artefacts>
</ReleaseConfig>
```

### Example 6: Android APK Packaging

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig>
  <Name>MobileApp</Name>

  <Target name="live" path="$RELEASE_PATH" />

  <Artefacts>
    <build>
      <!-- Copy and rename APK with version -->
      <copy from="..\mobile\Android\app\release\app-release.apk"
            to="android-~TYPE~"
            name="MobileApp-~VERSION~.apk" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

---

## Command-Line Integration

### Using Targets

```bash
# Build for live target
ReleaseBuilder --config ReleaseConfig.xml --target live

# Build for dev target
ReleaseBuilder --config ReleaseConfig.xml --target dev
```

### Using Modules

```bash
# Build only specific modules
ReleaseBuilder --module Dashboard --module API

# Build all modules
ReleaseBuilder
```

### Setting Root Directory

```bash
# Specify project root
ReleaseBuilder --root /path/to/project --config ReleaseConfig.xml
```

### Using Tool Directories

```bash
# Add custom tool search paths
ReleaseBuilder --toolsdir /custom/tools --toolsdir /another/path
```

---

## Best Practices

1. **Use Variables**: Leverage `~TYPE~`, `~VERSION~`, and custom variables to make configs reusable across targets

2. **Modular Builds**: Break complex builds into modules using `<ReleaseBuilder>` elements

3. **Environment Variables**: Use `$VAR` syntax for sensitive data (API keys, passwords) and deployment paths

4. **Version Management**: Let GitVersion handle versioning automatically via `~SemVer~`

5. **Clean Before Build**: Use `<clean>` actions to ensure reproducible builds

6. **Transformations**: Use content transformations instead of maintaining multiple config file copies

7. **Error Handling**: Test configs with `-v` (verbose) flag to debug issues

8. **Documentation**: Add XML comments to complex configs for maintainability

## Schema Validation

Validate your ReleaseConfig.xml against the schema:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig xmlns:p1="http://www.w3.org/2001/XMLSchema-instance"
               p1:noNamespaceSchemaLocation="ReleaseConfig.xsd">
  <!-- Your configuration -->
</ReleaseConfig>
```

The XSD schema file (`ReleaseConfig.xsd`) is included in the ReleaseBuilder repository.
