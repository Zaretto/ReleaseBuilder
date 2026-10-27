# ReleaseBuilder User Reference Manual

## Table of Contents

1. [Command-Line Reference](#command-line-reference)
2. [Configuration File Reference](#configuration-file-reference)
3. [Variable System](#variable-system)
4. [Build Actions Reference](#build-actions-reference)
5. [Transform Functions Reference](#transform-functions-reference)
6. [Path Resolution](#path-resolution)
7. [Exit Codes](#exit-codes)
8. [File Formats](#file-formats)

---

## Command-Line Reference

### Synopsis

```
ReleaseBuilder [OPTIONS]
```

### Options

#### -r, --root <directory>

**Type:** Directory path
**Required:** No
**Default:** Current working directory

Specifies the root directory for the build process. All relative paths in the configuration file are resolved relative to this directory.

**Examples:**
```bash
ReleaseBuilder --root /home/user/project
ReleaseBuilder -r C:\Projects\MyApp
```

**Notes:**
- Directory must exist
- Path can be absolute or relative
- Becomes available as `~PUBLISHROOT~` variable

---

#### -c, --config <file>

**Type:** File path
**Required:** No
**Default:** See [Configuration File Location](#configuration-file-location)

Path to the ReleaseConfig.xml configuration file.

**Examples:**
```bash
ReleaseBuilder --config custom-config.xml
ReleaseBuilder -c configs/ReleaseConfig.xml
```

**Search Order:**
1. File specified with `--config`
2. `ReleaseConfig.xml` in root directory (specified with `--root`)
3. `ReleaseConfig.xml` in current directory

---

#### -t, --target <name>

**Type:** String
**Required:** No
**Default:** `live`

Selects which `<Target>` element to use from the configuration file.

**Examples:**
```bash
ReleaseBuilder --target production
ReleaseBuilder -t dev
```

**Notes:**
- Target name must match a `<Target name="...">` in config
- Case-insensitive matching
- Affects which `<Set>` variables are active
- Becomes available as `~TYPE~` variable

---

#### -p, --toolsdir <directory>

**Type:** Directory path
**Required:** No
**Default:** Application base directory
**Multiple:** Yes (can be specified multiple times)

Adds directories to search path for executable tools used in `<exec>` actions.

**Examples:**
```bash
ReleaseBuilder --toolsdir /opt/custom-tools
ReleaseBuilder -p C:\BuildTools -p C:\Utils
```

**Search Order for Executables:**
1. Directories specified with `--toolsdir` (in order specified)
2. Application base directory
3. System PATH

---

#### -m, --module <name>

**Type:** String
**Required:** No
**Multiple:** Yes (can be specified multiple times)

Filters which modules/components to build in multi-component projects. Used with `<ReleaseBuilder>` element chaining to selectively build components.

**How Module Filtering Works:**

When you specify `--module`, ReleaseBuilder performs case-insensitive substring matching against:
1. The `<Name>` element in each configuration file
2. The `name` attribute in `<ReleaseBuilder name="...">` elements

**Matching Algorithm:**

For each configuration encountered:
```
if (--module flags specified):
    if (<Name> content contains any module name):
        INCLUDE this configuration
    else if (<ReleaseBuilder name="..."> contains any module name):
        INCLUDE this configuration
    else:
        SKIP this configuration
else:
    INCLUDE all configurations (no filtering)
```

**Important:** The main/root configuration is ALWAYS processed, even if its name doesn't match. Module filtering only applies to nested `<ReleaseBuilder>` elements.

**Examples:**

**Basic Usage:**
```bash
# Build only API module
ReleaseBuilder --module API

# Build multiple modules
ReleaseBuilder -m Frontend -m Backend

# Substring matching (case-insensitive)
ReleaseBuilder --module back
# Matches: Backend, Background, FeedbackService, etc.
```

**Configuration Example:**
```xml
<!-- Main ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>EnterpriseSolution</Name>

  <ReleaseBuilder folder="API" process="true" />
  <ReleaseBuilder folder="Frontend" process="true" />
  <ReleaseBuilder folder="Worker" process="true" />
</ReleaseConfig>

<!-- API/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>API</Name>
  <Artefacts>...</Artefacts>
</ReleaseConfig>

<!-- Frontend/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>Frontend</Name>
  <Artefacts>...</Artefacts>
</ReleaseConfig>

<!-- Worker/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>Worker</Name>
  <Artefacts>...</Artefacts>
</ReleaseConfig>
```

**Execution with Module Filter:**
```bash
ReleaseBuilder --module API

# Execution flow:
# 1. Process Main (EnterpriseSolution) - always runs
# 2. Check API/ReleaseConfig.xml
#    - Name is "API"
#    - CONTAINS "API" → INCLUDE
# 3. Check Frontend/ReleaseConfig.xml
#    - Name is "Frontend"
#    - Does NOT contain "API" → SKIP
# 4. Check Worker/ReleaseConfig.xml
#    - Name is "Worker"
#    - Does NOT contain "API" → SKIP
# Result: Only API is built
```

**Using Named Modules:**

Instead of matching on `<Name>`, you can use explicit module names:

```xml
<ReleaseConfig>
  <Name>EnterpriseSolution</Name>

  <!-- Use 'name' attribute for explicit module identification -->
  <ReleaseBuilder name="BackendAPI" folder="API" process="true" />
  <ReleaseBuilder name="WebUI" folder="Frontend" process="true" />
  <ReleaseBuilder name="BackgroundProcessor" folder="Worker" process="true" />
</ReleaseConfig>
```

```bash
# Match by ReleaseBuilder name attribute
ReleaseBuilder --module BackendAPI
# Matches: Only BackendAPI

# Substring matching works on name attribute too
ReleaseBuilder --module Backend
# Matches: BackendAPI, BackgroundProcessor (both contain "Backend")

# Multiple filters (OR logic)
ReleaseBuilder --module BackendAPI --module WebUI
# Matches: BackendAPI OR WebUI
```

**Matching Priority:**

ReleaseBuilder checks in this order:
1. If `<ReleaseBuilder name="X">` specified → match against `name` attribute
2. Otherwise → match against `<Name>` element in the nested config

**Multiple Module Behavior:**

Multiple `--module` flags use OR logic:

```bash
ReleaseBuilder --module API --module Frontend
# Includes: API OR Frontend (both are built)
```

**Use Cases:**

1. **Development workflows** - Build only components you're working on:
   ```bash
   ReleaseBuilder --module API -v
   ```

2. **Continuous Integration** - Build specific services:
   ```bash
   ReleaseBuilder --module BackendAPI --module Database --target staging
   ```

3. **Debugging** - Test individual component builds:
   ```bash
   ReleaseBuilder --module Worker --nobuild -v
   ```

4. **Partial deployments** - Deploy subset of components:
   ```bash
   ReleaseBuilder --module Frontend --target production
   ```

**Case Sensitivity:**

Matching is case-insensitive:
```bash
ReleaseBuilder --module api        # Matches "API"
ReleaseBuilder --module FRONTEND   # Matches "Frontend"
ReleaseBuilder --module worker     # Matches "Worker"
```

**Substring Matching:**

Partial names match:
```bash
ReleaseBuilder --module Front      # Matches "Frontend"
ReleaseBuilder --module end        # Matches "Frontend", "Backend"
ReleaseBuilder --module API        # Matches "API", "BackendAPI"
```

**Notes:**
- Main configuration always processes (no filtering at root level)
- If no modules specified, all modules are built
- Module filtering applies recursively to all levels of nesting
- Useful for large multi-component projects
- Can significantly speed up development builds

**Troubleshooting:**

If module not building:
```bash
# Use verbose mode to see filtering decisions
ReleaseBuilder --module MyModule -vv

# Check Name elements match
# In nested configs, look for:
# <Name>MyModule</Name> or substring containing module name
```

**See Also:**
- [ReleaseBuilder Element Reference](#element-releasebuilder)
- [User Guide: Multi-Component Tutorial](User-Guide.md#tutorial-3-multi-component-application-with-chaining)

---

#### -n, --nobuild

**Type:** Boolean flag
**Required:** No
**Default:** false

Skips all `<build>` actions in `<Artefacts>` elements. Useful for testing artifact collection without rebuilding.

**Examples:**
```bash
ReleaseBuilder --nobuild
ReleaseBuilder -n
```

**Use Cases:**
- Testing which files are included
- Re-packaging existing builds
- Debugging artifact selection

---

#### -s, --shell-exec

**Type:** Boolean flag
**Required:** No
**Default:** false

Uses ShellExecute for process execution instead of direct process creation. May be required for certain shell commands or scripts.

**Examples:**
```bash
ReleaseBuilder --shell-exec
ReleaseBuilder -s
```

**When to Use:**
- Executing shell scripts directly
- Commands that require shell interpretation
- Platform-specific shell features

---

#### -v, --verbose

**Type:** Boolean flag
**Required:** No
**Multiple:** Yes (use twice for extra verbosity)

Increases logging verbosity.

**Levels:**
- **Default**: Info and Error messages
- **-v**: + Trace messages (shows variable expansion, file operations)
- **-vv**: + Debug messages (detailed execution flow, command output)

**Examples:**
```bash
ReleaseBuilder -v                    # Trace level
ReleaseBuilder --verbose --verbose   # Debug level
ReleaseBuilder -vv                   # Debug level (shorthand)
```

**What You'll See:**
- `-v`: Variable values, files added, operations performed
- `-vv`: All of above plus command stdout/stderr, detailed path resolution

---

#### -d, --dry-run

**Type:** Boolean flag
**Required:** No
**Default:** false

Parses and validates the configuration, expands variables, and reports what would happen — without executing any build actions or creating artefacts.

**Examples:**
```bash
ReleaseBuilder --dry-run -c ReleaseConfig.xml
ReleaseBuilder -d --target staging
```

**What dry-run does:**
- Parses the XML configuration
- Resolves all variables
- Validates path existence
- Reports each action it would take (prefixed with `[dry-run]`)
- Collects and reports the artefact list
- Validates certificates for real (`check-certificate`) — the check mutates nothing, and confirming the signing certificate is present and in date is one of the most useful things a dry run can report
- Generates certificates in memory (`create-certificate`) and publishes their `~CERT_*~` variables, without writing the file, so a later reference to one still resolves

**What dry-run skips:**
- File deletion (`clean`)
- File creation (`create`)
- XML editing (`xml-edit`)
- File copying (`copy`, `modify`)
- External process execution (`exec`)
- Writing the certificate file (`create-certificate`)
- Archive creation and file copying in `Process()`

**Use Cases:**
- Validate configuration changes before a real build
- CI/CD pull request checks that verify config is valid
- Debugging build configurations safely

```bash
# Validate config without any side effects
release-builder --dry-run --config ReleaseConfig.xml --target production
```

---

#### -o, --output-manifest <file>

**Type:** File path string
**Required:** No
**Default:** None (no manifest written)

Writes a JSON manifest of build outputs after `Process()` completes. Use `-` to write to stdout.

**Examples:**
```bash
release-builder -c ReleaseConfig.xml -o manifest.json
release-builder -c ReleaseConfig.xml -o -       # stdout
```

**Manifest JSON structure:**
```json
{
  "TargetName": "MyApp",
  "TargetType": "ZipFile",
  "Version": "1.2.3",
  "OutputPath": "/releases",
  "ErrorCount": 0,
  "Timestamp": "2026-04-26T12:00:00+00:00",
  "Artefacts": [
    {
      "Path": "/releases/MyApp-live-1.2.3.zip",
      "ArchiveName": "MyApp-live-1.2.3.zip",
      "SizeBytes": 98765
    }
  ]
}
```

**Fields:**
- `TargetName` — value of `<Name>` in the config
- `TargetType` — `ZipFile` or `LocalFolder`
- `Version` — resolved `~SemVer~` at time of build
- `OutputPath` — resolved output directory
- `ErrorCount` — number of errors logged during the build
- `Timestamp` — UTC time when `Process()` ran
- `Artefacts` — list of output files produced

**Use Cases:**
- Feed artefact paths to subsequent pipeline steps
- Audit what was produced by a build
- Upload specific output files from a CI job

---

#### -h, --help

**Type:** Boolean flag
**Required:** No

Displays help information and exits.

**Example:**
```bash
ReleaseBuilder --help
```

---

### Configuration File Location

If `--config` is not specified, ReleaseBuilder searches for `ReleaseConfig.xml`:

1. In the root directory (specified with `--root` or current directory)
2. In the current working directory

**Search Algorithm:**
```
if --config specified:
    use specified file
else if --root specified:
    search for ReleaseConfig.xml in root directory
    if not found, search in current directory
else:
    search for ReleaseConfig.xml in current directory
```

---

### Exit Codes

| Code | Meaning | Description |
|------|---------|-------------|
| 0 | Success | Build completed successfully |
| 1 | ConfigError | Configuration file not found or not valid |
| 2 | BuildError | Build actions failed (errors were logged) |
| 3 | ArtefactError | Artefact processing failed |
| 4 | InvalidArguments | Unrecognised command-line arguments |

**Checking Exit Code:**

**Bash/Linux/macOS:**
```bash
ReleaseBuilder --target production
if [ $? -eq 0 ]; then
    echo "Build succeeded"
else
    echo "Build failed"
fi
```

**Windows CMD:**
```cmd
ReleaseBuilder --target production
if %ERRORLEVEL% EQU 0 (
    echo Build succeeded
) else (
    echo Build failed
)
```

**PowerShell:**
```powershell
ReleaseBuilder --target production
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded"
} else {
    Write-Host "Build failed"
}
```

---

## Configuration File Reference

### XML Structure

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig xmlns:p1="http://www.w3.org/2001/XMLSchema-instance"
               p1:noNamespaceSchemaLocation="ReleaseConfig.xsd">
  <Name>...</Name>
  <Folder>...</Folder>
  <Target>...</Target>
  <Artefacts>...</Artefacts>
  <Artefact>...</Artefact>
  <ReleaseBuilder>...</ReleaseBuilder>
</ReleaseConfig>
```

### Element: Name

**Parent:** `<ReleaseConfig>`
**Occurrence:** 0 or 1
**Content:** Text

Specifies the name of the release configuration.

**Syntax:**
```xml
<Name>ProjectName</Name>
```

**Purpose:**
- Identifies the configuration in log output
- Used for module filtering with `--module`
- Used in default archive naming

**Examples:**
```xml
<Name>MyApplication</Name>
<Name>CompanyName.ProductName.ComponentName</Name>
```

---

### Element: Folder

**Parent:** `<ReleaseConfig>`
**Occurrence:** 0 or more
**Content:** Empty

Defines a variable pointing to a folder selected from a search pattern.

**Syntax:**
```xml
<Folder name="VARIABLE_NAME"
        path="search_pattern"
        version="selection_method"
        name-version="VERSION_VAR" />
```

**Attributes:**

##### name (required)

**Type:** String
**Purpose:** Name of variable to create

Variable will be available as `~NAME~` in subsequent configuration.

##### path (required)

**Type:** String with wildcards
**Purpose:** Search pattern for folders

Supports wildcards:
- `*` - Matches any characters
- `?` - Matches single character

**Examples:**
- `Build_*` - Matches `Build_1.0`, `Build_2.0`, etc.
- `MyApp_*_Release` - Matches `MyApp_1.0_Release`, etc.

##### version (optional)

**Type:** Enumeration
**Default:** `latest`
**Values:**
- `latest` - Most recently created folder (by creation time)
- `oldest` - Oldest created folder
- `name` - First alphabetically
- `last-name` - Last alphabetically

##### name-version (optional)

**Type:** String
**Purpose:** Variable name to store extracted version number

Extracts version pattern (digits and dots) from the folder name.

**Pattern Matched:** `\d+.+\d` (e.g., `1.2.3`, `2.0.1.4`)

**Complete Example:**
```xml
<!-- Find most recent build and extract version -->
<Folder name="BUILD_PATH"
        path="Builds/MyApp_*"
        version="latest"
        name-version="BUILD_VERSION" />

<!-- Now you can use: -->
<!-- ~BUILD_PATH~ = "Builds/MyApp_1.2.3" -->
<!-- ~BUILD_VERSION~ = "1.2.3" -->
```

**Use Cases:**
- Locating UWP package folders with version numbers
- Finding most recent automated build output
- Extracting version from folder structure

---

### Element: Target

**Parent:** `<ReleaseConfig>`
**Occurrence:** 0 or more
**Content:** `<Set>` elements

Defines a deployment target configuration.

**Syntax:**
```xml
<Target name="target_name"
        path="output_path"
        type="output_type"
        archive-version="version">
  <Set name="VAR" value="value" />
  ...
</Target>
```

**Attributes:**

##### name (required)

**Type:** String
**Purpose:** Target identifier

Used with `--target` command-line option. The target with matching name becomes active.

##### path (required)

**Type:** String (directory or file path)
**Purpose:** Output location

- For `type="folder"`: Directory where files are copied
- For `type="zip"`: Directory where ZIP file is created

Supports variable substitution.

##### type (optional)

**Type:** Enumeration
**Default:** `zip`
**Values:**
- `zip` - Create a ZIP archive
- `folder` - Copy files to a folder

##### archive-version (optional)

**Type:** String
**Purpose:** Version string for archive filename

Only used when `type="zip"`. Supports variables and transforms.

**Archive Naming:**
- If `archive-version` specified: `{path}/{name}-{archive-version}.zip`
- If not specified: `{path}/{name}.zip`

Where `{name}` comes from `<Name>` element.

**Child Elements:**

##### Set

Defines target-specific variables that are only active when this target is selected.

**Syntax:**
```xml
<Set name="VARIABLE_NAME" value="variable_value" />
<Set name="VARIABLE_NAME" transform="transform_expression" />
```

**Attributes:**

| Attribute | Required | Description |
|-----------|----------|-------------|
| `name` | Yes | Variable name, referenced elsewhere as `~name~` |
| `value` | No | Value; `~VARIABLE~` and `$ENVIRONMENT` references are expanded when the target becomes active |
| `transform` | No | [Transform function](#transform-functions-reference) applied to `value` (which may be omitted) to produce the variable |
| `secret` | No | `true` replaces the value with `***` everywhere ReleaseBuilder logs — see [Secret Redaction](#secret-redaction) |

Variables available to a `<Set>` value are the built-ins, the GitVersion variables, `~TARGETPATH~`, and anything defined earlier in the file — including preceding `<Set>` elements of the same target.

**Complete Example:**
```xml
<Target name="development"
        path="builds/dev"
        type="folder">
  <Set name="ENVIRONMENT" value="Development" />
  <Set name="API_URL" value="http://localhost:5000" />
  <Set name="LOG_LEVEL" value="Debug" />
</Target>

<Target name="production"
        path="releases"
        type="zip"
        archive-version="~SemVer~">
  <Set name="ENVIRONMENT" value="Production" />
  <Set name="API_URL" value="https://api.example.com" />
  <Set name="LOG_LEVEL" value="Warning" />
  <!-- Build-time secret; generated once and used wherever ~API_SECRET~ appears -->
  <Set name="API_SECRET" transform="random-key,32" />
</Target>
```

**Usage:**
```bash
# Development target: creates builds/dev/ folder
ReleaseBuilder --target development

# Production target: creates releases/MyApp-1.2.3.zip
ReleaseBuilder --target production
```

---

### Element: Artefacts

**Parent:** `<ReleaseConfig>`
**Occurrence:** 0 or more
**Content:** `<build>`, `<file>`, `<folder>` elements

Defines a collection of artifacts to include in the release.

**Syntax:**
```xml
<Artefacts folder="working_directory" active="condition">
  <build>...</build>
  <file>...</file>
  <folder>...</folder>
</Artefacts>
```

**Attributes:**

##### folder (optional)

**Type:** String (directory path)
**Purpose:** Working directory for build actions

All paths in child elements are resolved relative to this folder.

**Resolution Order:**
1. Try as relative to root directory
2. Try as relative to current directory

##### active (optional)

**Type:** String (transform expression)
**Purpose:** Conditional inclusion

If the transform expression evaluates to empty string, this `<Artefacts>` block is skipped.

**Example:**
```xml
<!-- Only process if TYPE is "production" -->
<Artefacts active="when,~TYPE~,==,production">
  <folder>production-docs</folder>
</Artefacts>
```

**Child Elements:**

##### build

Contains build actions to execute. See [Build Actions Reference](#build-actions-reference).

##### file

Specifies a single file to include.

**Syntax:**
```xml
<file folder="subfolder"
      newname="new_filename"
      skip-directories-front="N"
      name="filename">path/to/file</file>
```

**Attributes:**
- `folder` - Subdirectory containing the file
- `newname` - Rename file in output
- `skip-directories-front` - Number of leading directories to remove from output path
- `name` - Original filename

**Content:** File path (supports variables)

##### folder

Specifies a folder to include recursively.

**Syntax:**
```xml
<folder skip-directories-front="N">path/to/folder</folder>
```

**Attributes:**
- `skip-directories-front` - Number of leading directories to remove from output path

**Content:** Folder path (supports variables)

**Complete Example:**
```xml
<Artefacts folder="release-build">
  <build>
    <clean folder="." />
    <exec app="dotnet" args="build -c Release" />
    <copy from="bin/Release" match="*.dll" />
  </build>
  <folder>release-build</folder>
</Artefacts>

<!-- Include additional files -->
<Artefacts>
  <file>README.md</file>
  <file newname="license.txt">LICENSE</file>
  <folder>documentation</folder>
</Artefacts>
```

---

### Element: Artefact

**Parent:** `<ReleaseConfig>`
**Occurrence:** 0 or more
**Content:** Empty

Alternative syntax for defining individual artifacts.

**Syntax:**
```xml
<Artefact folder="source_folder"
          directory="directory_path"
          file="file_path"
          skip-directories-front="N" />
```

**Attributes:**

##### folder (optional)

**Type:** String
**Purpose:** Source folder for relative path resolution

##### directory (optional)

**Type:** String
**Purpose:** Directory to include

##### file (optional)

**Type:** String
**Purpose:** File to include

##### skip-directories-front (optional)

**Type:** Integer
**Purpose:** Number of leading directories to skip

**Examples:**
```xml
<Artefact file="readme.txt" />
<Artefact directory="bin/Release" folder="output" />
<Artefact file="docs/manual.pdf" skip-directories-front="1" />
<!-- manual.pdf will be in root of output, not in docs/ -->
```

---

### Element: ReleaseBuilder

**Parent:** `<ReleaseConfig>` or `<build>`
**Occurrence:** 0 or more
**Content:** Zero or more `<Set>` child elements

Recursively invokes ReleaseBuilder on a nested configuration, enabling modular builds.

> **Note:** Inside `<build>` sections, the lowercase form `<release-builder>` is also accepted.

**Syntax:**
```xml
<ReleaseBuilder name="module_name"
                folder="subfolder"
                file="config_filename"
                process="true|false"
                nobuild="true|false">
  <Set name="VAR_NAME" value="value" />
</ReleaseBuilder>
```

**Attributes:**

##### name (optional)

**Type:** String
**Purpose:** Module identifier for filtering

Used with `--module` command-line option.

##### folder (required)

**Type:** String
**Purpose:** Directory containing nested configuration

The nested ReleaseBuilder will use this as its root directory.

##### file (optional)

**Type:** String
**Default:** `ReleaseConfig.xml` or `ReleaseConfig{name}.xml`
**Purpose:** Configuration filename

If `name` is specified and `file` is not, tries `ReleaseConfig{name}.xml` first.

##### process (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Whether to process artifacts from nested build

- `true` - Execute nested build and include its artifacts in parent
- `false` - Execute nested build but don't include artifacts

##### nobuild (optional)

**Type:** Boolean
**Default:** Inherits from parent or command-line
**Purpose:** Skip build actions in nested configuration

##### Set (child element, optional, repeatable)

**Syntax:** `<Set name="VAR_NAME" value="value" />`
**Purpose:** Inject a variable into the child config's variable store

The `value` attribute is expanded using the parent's variables before being passed to the child, so parent variable references like `~PARENT_VAR~` in a `value` string are resolved at the parent level. Multiple `<Set>` elements are allowed and all are injected.

An optional `transform` attribute applies a [transform function](#transform-functions-reference) to the expanded value, for example `<Set name="API_SECRET" transform="random-key,32" />` to generate a secret in the parent and hand the same key to the child build. An optional `secret="true"` attribute registers the value for [redaction](#secret-redaction) in the log, in the child build as well as the parent.

Variables injected this way are available throughout the child config — in paths, `active` conditions, `<create>` content, `archive-version`, and anywhere else variables are expanded.

**Examples:**

**Example 1: Multi-module build**
```xml
<!-- Main ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>EnterpriseSolution</Name>

  <Target name="production" path="releases" type="zip" />

  <ReleaseBuilder folder="API" process="true" />
  <ReleaseBuilder folder="Frontend" process="true" />
  <ReleaseBuilder folder="Worker" process="true" />
</ReleaseConfig>
```

**Example 2: Named modules with filtering**
```xml
<ReleaseConfig>
  <Name>MyProduct</Name>

  <ReleaseBuilder name="Backend" folder="src/backend" process="true" />
  <ReleaseBuilder name="Frontend" folder="src/frontend" process="true" />
  <ReleaseBuilder name="Docs" folder="documentation" process="true" />
</ReleaseConfig>
```

Build only backend:
```bash
ReleaseBuilder --module Backend
```

**Directory Structure:**
```
project/
  ReleaseConfig.xml
  API/
    ReleaseConfig.xml
  Frontend/
    ReleaseConfig.xml
```

**How Chaining Works:**

The `<ReleaseBuilder>` element implements configuration chaining by recursively invoking ReleaseBuilder:

1. **Load main config** - Parse root `ReleaseConfig.xml`
2. **For each `<ReleaseBuilder>` element:**
   - **Check module filter** - If `--module` specified, check if this module matches
   - **Change directory** - `cd` to specified `folder`
   - **Load nested config** - Read `ReleaseConfig.xml` (or specified `file`) in that folder
   - **Set root** - Nested config uses its folder as `PUBLISHROOT`
   - **Inherit target** - Nested build uses same `--target` as parent
   - **Execute build** - Run nested configuration completely
   - **Collect artifacts** - If `process="true"`, gather artifacts into parent
   - **Return directory** - `cd` back to parent folder
   - **Error propagation** - If nested build fails, entire build fails
3. **Create final package** - Combine all collected artifacts

**Important Behaviors:**

| Aspect | Behavior |
|--------|----------|
| **Target inheritance** | Child uses the same `--target` name as the parent |
| **Variable inheritance** | Child starts with all parent variables as a base |
| **Set override** | `<Set>` children on `<ReleaseBuilder>` add new vars or override inherited ones |
| **Built-ins re-derived** | `~TYPE~`, `~PUBLISHROOT~`, `~OS~`, `~ARCH~`, `~RUNTIME~`, and GitVersion vars are re-derived in the child, overriding any inherited values for those keys |
| **Target override** | Child can define own `<Target>` to change output path |
| **Module filtering** | Applied at each nesting level based on `<Name>` element |
| **Working directory** | Each config runs in its own folder context |
| **Error handling** | Any failure stops entire build chain |
| **Artifact collection** | Only if `process="true"` on parent element |

**Execution Flow Diagram:**

```
Main ReleaseConfig.xml
│
├─ Process local <Artefacts>
│
├─ <ReleaseBuilder folder="API" process="true" />
│  │
│  ├─ cd API/
│  ├─ Load API/ReleaseConfig.xml
│  ├─ Execute API build
│  ├─ Collect API artifacts
│  └─ cd ../
│
├─ <ReleaseBuilder folder="Frontend" process="true" />
│  │
│  ├─ cd Frontend/
│  ├─ Load Frontend/ReleaseConfig.xml
│  ├─ Execute Frontend build
│  ├─ Collect Frontend artifacts
│  └─ cd ../
│
└─ Create final package (Main + API + Frontend artifacts)
```

**Variable Inheritance and Override Example:**

```xml
<!-- Main config — Target <Set> vars are inherited by all children automatically.
     Use <Set> on <ReleaseBuilder> only to add new vars or override specific ones. -->
<ReleaseConfig>
  <Name>Main</Name>

  <Target name="production" path="releases" type="zip">
    <Set name="ENV"       value="production" />
    <Set name="API_URL"   value="https://api.example.com" />
    <Set name="DB_SERVER" value="prod-db.example.com" />
  </Target>

  <!-- Children inherit ENV, API_URL, DB_SERVER automatically -->
  <ReleaseBuilder folder="API" process="true" />

  <!-- Worker overrides DB_SERVER only; still inherits ENV and API_URL -->
  <ReleaseBuilder folder="Worker" process="true">
    <Set name="DB_SERVER" value="worker-db.example.com" />
  </ReleaseBuilder>
</ReleaseConfig>

<!-- Child: API/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>API</Name>

  <!-- ENV, API_URL, DB_SERVER inherited from parent.
       Built-ins (PUBLISHROOT, TYPE, OS, etc.) and GitVersion vars
       are re-derived from the child's own context. -->

  <Artefacts>
    <build>
      <create file="appsettings.json">
{
  "Environment": "~ENV~",
  "ApiUrl": "~API_URL~",
  "DatabaseServer": "~DB_SERVER~",
  "Version": "~SemVer~"
}
      </create>
      <exec app="dotnet" args="publish -c Release -o publish" />
    </build>
    <folder>publish</folder>
  </Artefacts>
</ReleaseConfig>
```

**Target Override Example:**

Child configs can override the parent's Target to use different output paths. Parent variables are inherited automatically.

```xml
<!-- Parent -->
<ReleaseConfig>
  <Name>MobileAppSuite</Name>

  <Target name="production" path="releases/api" type="zip">
    <Set name="ENV"     value="production" />
    <Set name="API_URL" value="https://api.example.com" />
  </Target>

  <!-- Both children inherit ENV and API_URL automatically -->
  <ReleaseBuilder folder="WebAPI"      process="true" />
  <ReleaseBuilder folder="AndroidApp"  process="true" />
</ReleaseConfig>

<!-- Child: WebAPI/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>WebAPI</Name>

  <!-- No Target defined — uses parent's target path (releases/api) -->
  <!-- ENV, API_URL available via inheritance -->

  <Artefacts>
    <build>
      <exec app="dotnet" args="publish -c Release" />
    </build>
  </Artefacts>
</ReleaseConfig>

<!-- Child: AndroidApp/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>AndroidApp</Name>

  <!-- Override Target — APK goes to a different location -->
  <Target name="production" path="releases/android" type="folder" />

  <!-- ENV, API_URL still available via inheritance -->

  <Artefacts>
    <build>
      <create file="config.properties">
env=~ENV~
apiUrl=~API_URL~
version=~SemVer~
      </create>
      <copy from="../mobile/Android/app/release/app-release.apk"
            name="MyApp-~SemVer~.apk" />
    </build>
  </Artefacts>
</ReleaseConfig>
```

**Result:**
- WebAPI uses parent Target → ZIP at `releases/api/MobileAppSuite-1.2.3.zip`
- AndroidApp overrides Target → Folder at `releases/android/`
- Both inherit `~ENV~` and `~API_URL~` from the parent automatically

**Nested Chaining:**

You can chain multiple levels deep:

```xml
<!-- Level 1: Main -->
<ReleaseConfig>
  <Name>Suite</Name>
  <ReleaseBuilder folder="ProductA" process="true" />
</ReleaseConfig>

<!-- Level 2: ProductA/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>ProductA</Name>
  <ReleaseBuilder folder="API" process="true" />
  <ReleaseBuilder folder="UI" process="true" />
</ReleaseConfig>

<!-- Level 3: ProductA/API/ReleaseConfig.xml -->
<ReleaseConfig>
  <Name>API</Name>
  <Artefacts>...</Artefacts>
</ReleaseConfig>
```

Execution: `Suite → ProductA → (API, UI)`

**Module Filtering with Chaining:**

```bash
ReleaseBuilder --module API
```

Applies filtering at each level:
```
Main (always processed)
  ├─ Check ProductA: Does "ProductA" contain "API"? No → Still process as parent
      ├─ Check API: Does "API" contain "API"? Yes → INCLUDE
      └─ Check UI: Does "UI" contain "API"? No → SKIP
```

**process Attribute:**

Controls whether artifacts from nested build are included in parent:

```xml
<!-- Include artifacts in parent package -->
<ReleaseBuilder folder="API" process="true" />

<!-- Just build, don't include artifacts -->
<ReleaseBuilder folder="Docs" process="false" />
```

**Use Cases:**

1. **Multi-service applications** - Each service has own config
2. **Monorepo builds** - Build multiple projects from one root
3. **Conditional builds** - Use module filtering for selective builds
4. **Hierarchical projects** - Nest configs to match project structure

**See Also:**
- [--module Command-Line Option](#-m---module-name)
- [User Guide: Multi-Component Tutorial](User-Guide.md#tutorial-3-multi-component-application-with-chaining)

---

## Variable System

### Variable Types

ReleaseBuilder supports three types of variables:

1. **Built-in variables** - Provided automatically by ReleaseBuilder
2. **User-defined variables** - Defined in configuration
3. **Environment variables** - From system environment

### Variable Reference Syntax

**Tilde syntax** (for built-in and user-defined):
```
~VARIABLE_NAME~
```

**Dollar syntax** (for environment variables):
```
$VARIABLE_NAME
$VARIABLE_~VAR~_NAME
```

The name of an environment variable may itself contain `~VARIABLE~` references, so the variable actually read can depend on the active target — see [Environment Variables](#environment-variables).

### Built-in Variables

These variables are automatically available:

| Variable | Description | Example Value |
|----------|-------------|---------------|
| `TYPE` | Current target name | `production`, `dev` |
| `PUBLISHROOT` | Root directory | `/home/user/project` |
| `OS` | Operating system: `windows`, `osx`, or `linux` | `windows` |
| `ARCH` | CPU architecture: `x64`, `arm64`, `x86`, `arm` | `x64` |
| `RUNTIME` | .NET RID-style combined platform: `win-x64`, `osx-arm64`, `linux-x64` | `win-x64` |
| `SemVer` | Semantic version from GitVersion | `1.2.3` |
| `VERSION` | Alias for SemVer | `1.2.3` |
| `TARGETPATH` | Active target's output path | `/releases` |
| `TargetVersion` | Version string for the active target (from `archive-version`) | `1.2.3` |
| `GITVERSION.JSON` | Full GitVersion JSON | `{"SemVer":"1.2.3",...}` |

Additionally, `<check-certificate>` and `<create-certificate>` publish `~CERT_*~` variables describing the certificate they handled — see [Certificate Variables](#certificate-variables).

**GitVersion Variables:**

ReleaseBuilder requires GitVersion.Tool to function.

**Installation:**
```bash
dotnet tool install --global GitVersion.Tool
```

**How ReleaseBuilder Uses GitVersion:**

1. At startup, ReleaseBuilder executes `dotnet-gitversion` in the root directory
2. GitVersion analyzes the Git repository and returns JSON with version data
3. ReleaseBuilder parses the JSON and creates variables from all string properties

**Version Variable Creation:**

The `VERSION` variable is created using this logic:
```csharp
if (!string.IsNullOrEmpty(NuGetVersionV2))
    VERSION = NuGetVersionV2
else
    VERSION = MajorMinorPatch
```

The `SemVer` variable is set to the `SemVer` property from GitVersion JSON.

**Complete List of GitVersion Variables:**

All string properties from GitVersion JSON become variables:

| Variable | Type | Description | Example |
|----------|------|-------------|---------|
| `Major` | int | Major version number | `1` |
| `Minor` | int | Minor version number | `2` |
| `Patch` | int | Patch version number | `3` |
| `PreReleaseTag` | string | Pre-release identifier | `alpha.1`, `beta.5` |
| `PreReleaseTagWithDash` | string | Pre-release with dash prefix | `-alpha.1` |
| `PreReleaseLabel` | string | Pre-release label only | `alpha`, `beta` |
| `PreReleaseNumber` | long? | Pre-release number | `1`, `5` |
| `BuildMetaData` | string | Build metadata | `Branch.main.Sha.abc1234` |
| `BuildMetaDataPadded` | string | Padded build metadata | `0005` |
| `FullBuildMetaData` | string | Complete build metadata | Full metadata string |
| `MajorMinorPatch` | string | Standard version | `1.2.3` |
| `SemVer` | string | Semantic version | `1.2.3`, `1.2.3-alpha.1` |
| `LegacySemVer` | string | Legacy semantic version | `1.2.3-alpha1` |
| `LegacySemVerPadded` | string | Padded legacy version | `1.2.3-alpha0001` |
| `AssemblySemVer` | string | Assembly version | `1.2.0.0` |
| `AssemblySemFileVer` | string | Assembly file version | `1.2.3.0` |
| `FullSemVer` | string | Full semantic version | `1.2.3-alpha.1+5` |
| `InformationalVersion` | string | Full version with metadata | `1.2.3-alpha.1+Branch.main.Sha.abc1234` |
| `BranchName` | string | Git branch name | `main`, `develop`, `feature/xyz` |
| `Sha` | string | Git commit SHA (short) | `abc1234` |
| `NuGetVersionV2` | string | NuGet v2 version | `1.2.3-alpha0001` |
| `NuGetVersion` | string | NuGet v3 version | `1.2.3-alpha.1` |
| `NuGetPreReleaseTagV2` | string | NuGet v2 pre-release tag | `alpha0001` |
| `NuGetPreReleaseTag` | string | NuGet v3 pre-release tag | `alpha.1` |
| `CommitsSinceVersionSource` | long? | Commits since last tag | `5`, `42` |
| `CommitsSinceVersionSourcePadded` | string | Padded commit count | `0005` |
| `CommitDate` | string | Date of commit | `2024-01-15` |

**Special Variables:**

| Variable | Description |
|----------|-------------|
| `GITVERSION.JSON` | Complete JSON output from GitVersion (not parsed) |
| `IntSemVer` | Packed integer version: `major * 10000 + minor * 100 + patch`. Supports CalVer (major up to 9999, e.g. year-based versioning). |

**Example GitVersion JSON:**
```json
{
  "Major": 1,
  "Minor": 2,
  "Patch": 3,
  "PreReleaseTag": "alpha.1",
  "BuildMetaData": "",
  "MajorMinorPatch": "1.2.3",
  "SemVer": "1.2.3-alpha.1",
  "BranchName": "develop",
  "Sha": "abc1234",
  "CommitsSinceVersionSource": 5,
  "CommitDate": "2024-01-15"
}
```

**Using GitVersion Variables:**
```xml
<!-- Archive with semantic version -->
<Target name="release" path="releases" type="zip" archive-version="~SemVer~" />
<!-- Creates: releases/MyApp-1.2.3.zip -->

<!-- Version info file -->
<create file="version.txt">
Version: ~SemVer~
Full Version: ~FullSemVer~
NuGet Version: ~NuGetVersion~
Branch: ~BranchName~
Commit: ~Sha~ (~CommitDate~)
Commits Since Tag: ~CommitsSinceVersionSource~
</create>

<!-- Use in assembly info -->
<create file="AssemblyInfo.cs">
[assembly: AssemblyVersion("~AssemblySemVer~")]
[assembly: AssemblyFileVersion("~AssemblySemFileVer~")]
[assembly: AssemblyInformationalVersion("~InformationalVersion~")]
</create>

<!-- Conditional based on branch -->
<Artefacts active="when,~BranchName~,==,main">
  <folder>production-only</folder>
</Artefacts>
```

**Behavior Without GitVersion:**

If `dotnet-gitversion` is not found or execution fails:
- **ReleaseBuilder will throw an error and exit**
- Error message: "Failed to get git version: [details]"
- **Build will NOT proceed**
- Exit code: -1

**Troubleshooting:**

Check if GitVersion is working:
```bash
# Verify installation
dotnet-gitversion --version

# Test in your repository
cd /path/to/your/repo
dotnet-gitversion

# Use verbose mode in ReleaseBuilder
ReleaseBuilder -vv
# Look for: "Version {version}" output
```

**See Also:**
- GitVersion Documentation: https://gitversion.net/
- Semantic Versioning Specification: https://semver.org/

### User-Defined Variables

#### Via Folder Element

```xml
<Folder name="MY_VAR" path="pattern" version="latest" />
<!-- Creates ~MY_VAR~ -->
```

#### Via Set Element (in Target)

```xml
<Target name="production">
  <Set name="MY_VAR" value="my_value" />
  <!-- Creates ~MY_VAR~ (only when this target is active) -->
</Target>
```

#### Generated Values (transform on Set)

```xml
<Target name="production">
  <Set name="API_SECRET" transform="random-key,32" />
  <!-- Creates ~API_SECRET~ holding a random 32-character key,
       generated once and unchanged for the rest of the build -->
</Target>
```

See [Function: random-key](#function-random-key).

### Environment Variables

Access system environment variables with `$` prefix:

```xml
<exec args="--api-key $NUGET_API_KEY" />
<Target name="prod" path="$DEPLOY_PATH" />
```

**Setting Environment Variables:**

**Linux/macOS:**
```bash
export DEPLOY_PATH=/var/www/app
ReleaseBuilder --target prod
```

**Windows CMD:**
```cmd
set DEPLOY_PATH=C:\inetpub\wwwroot\app
ReleaseBuilder --target prod
```

**Windows PowerShell:**
```powershell
$env:DEPLOY_PATH = "C:\inetpub\wwwroot\app"
ReleaseBuilder --target prod
```

#### Target-Specific Environment Variables

An environment variable name may contain `~VARIABLE~` references, which are resolved before the environment is read. With `~TYPE~` holding the active target name this selects a different environment variable per target, so each target can have its own secret:

```xml
<create file="portal.env">API_KEY=$PROJ_PORTAL_~TYPE~</create>
<!-- Target "live"  reads PROJ_PORTAL_live  -->
<!-- Target "test"  reads PROJ_PORTAL_test  -->
```

```bash
export PROJ_PORTAL_live=live-key-value
export PROJ_PORTAL_test=test-key-value
```

**Rules:**
- The name must begin with a literal character: `$~TYPE~` is a literal `$` followed by the expanded variable, unchanged from previous behaviour.
- A `~VARIABLE~` reference may appear anywhere after the first character: `$RB_~TYPE~_KEY` is equally valid.
- If the composed name is not defined in the environment, ReleaseBuilder falls back to the literal part of the name (`$BASE~VER~` reads `BASE` and then expands `~VER~` separately). The error message names the composed variable when neither exists.
- Environment variable names are case-sensitive on Linux and macOS but not on Windows. Where a variable such as `~TYPE~` supplies part of the name, define the environment variable using exactly the case of the target name.
- Expansion is a single left-to-right pass: `$` and `~` characters inside a substituted value are not expanded again. A Windows short path such as `C:\PROGRA~1` is therefore safe as an environment variable value.

### Secret Redaction

A `<Set>` marked `secret="true"` registers its value with the logger, which replaces every occurrence of that value with `***` in everything it writes:

```xml
<Target name="live" path="releases" type="zip">
  <Set name="API_KEY"     value="$PROJ_PORTAL_~TYPE~" secret="true" />
  <Set name="MACHINE_KEY" transform="random-key,32" />
</Target>

<build>
  <exec app="dotnet" args="nuget push --api-key ~API_KEY~" />
</build>
```

```
[DEBUG] API_KEY=***
[DEBUG] MACHINE_KEY=***
[ INFO] [dry-run] Would exec: dotnet nuget push --api-key ***
```

**What it covers:**

Redaction happens on the way to the console, so it applies to every message: variable values at `-v -v`, `[dry-run] Would exec` lines, the command line included in an `<exec>` failure message, and captured process output that echoes its own arguments.

**Rules and limits:**
- Redaction matches the **value**, not the variable name. A secret used in three places is hidden in all three.
- Values shorter than 8 characters are not registered — redacting them would blank out unrelated text throughout the log.
- `random-key` output registers itself; `secret="true"` is not needed for it.
- The `password` and `issuer-password` attributes of `<check-certificate>` and `<create-certificate>` register themselves too — a password is a secret by definition, so there is no `secret` attribute to set on them.
- Registration is process-wide, so a secret marked in a parent config stays redacted inside nested `<ReleaseBuilder>` builds.
- Reference secrets through a marked `<Set>` rather than using `$ENV_VAR` directly in an `<exec>` argument: a value the tool never registers cannot be redacted.
- **Artefact contents are not redacted.** Writing a secret into a packaged file is the intended use of these variables; the redaction applies to log output only.

**Continuous integration:**

When `GITHUB_ACTIONS=true` is set in the environment, each registered secret is also emitted as a `::add-mask::` workflow command, so the runner masks the value in anything else the job prints — including output from steps that ReleaseBuilder does not control.

### Variable Expansion

Variables are expanded (recursively) before use:

```xml
<Folder name="BUILD" path="output/Build_*" />
<!-- BUILD = "output/Build_1.2.3" -->

<copy from="~BUILD~/bin" to="release" />
<!-- Expands to: from="output/Build_1.2.3/bin" -->
```

**Nested Expansion:**
```xml
<Target name="live" path="releases" type="zip">
  <Set name="BASE"    value="C:/Projects" />
  <Set name="PROJECT" value="~BASE~/MyApp" />
  <!-- PROJECT = "C:/Projects/MyApp" -->
</Target>
```

Expansion is a single left-to-right pass over each string: a value substituted into the result is not scanned again, so `~` and `$` characters within a variable's value are literal text.

### Variable Scope

| Type | Scope | Lifetime |
|------|-------|----------|
| Built-in | Global | Entire execution |
| Folder | Global | After definition |
| Set (in Target) | Target-specific | Only when target is active |
| Environment | Global | Inherited from shell |

**Example:**
```xml
<Target name="dev">
  <Set name="URL" value="http://localhost" />
</Target>

<Target name="prod">
  <Set name="URL" value="https://example.com" />
</Target>

<Artefacts>
  <build>
    <!-- ~URL~ has different value depending on --target -->
    <create file="config.txt">API URL: ~URL~</create>
  </build>
</Artefacts>
```

---

## Build Actions Reference

Build actions are executed within `<build>` elements.

Every build action accepts an optional `active` attribute, using the same `when,~VAR~,==,value` syntax as `<Artefacts active="...">`. When the expression evaluates to empty the action is skipped — the way to gate a single action on a platform or on the result of a preceding check.

### Action: clean

Deletes files from a directory.

**Syntax:**
```xml
<clean folder="path"
       match="pattern"
       include-folders="true|false" />
```

**Attributes:**

##### folder (required)

**Type:** String
**Purpose:** Directory to clean

**Resolution:** Searches in:
1. Relative to `<Artefacts folder="...">`
2. Relative to root directory
3. Relative to current directory

##### match (optional)

**Type:** String (wildcard pattern)
**Default:** `*.*`
**Purpose:** File pattern to delete

##### include-folders (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Also delete subdirectories

**Examples:**
```xml
<!-- Delete all files in bin -->
<clean folder="bin" />

<!-- Delete only .tmp files -->
<clean folder="temp" match="*.tmp" />

<!-- Delete files and subdirectories -->
<clean folder="output" include-folders="true" />

<!-- Delete specific pattern -->
<clean folder="logs" match="*.log" />
```

**Behavior:**
- Does not delete the folder itself, only its contents
- If folder doesn't exist, no error is raised
- Logs each deleted file when verbose mode enabled

---

### Action: copy

Copies files from source to destination with optional transformations.

**Syntax:**
```xml
<copy from="source"
      to="destination"
      match="pattern"
      name="new_filename"
      transform="transformation"
      recursive="true|false"
      if-different="true|false">
  <transform-content transform="transformation" />
  ...
</copy>
```

**Attributes:**

##### from (required)

**Type:** String
**Purpose:** Source file or directory

Can be:
- Specific file: `config.json`
- Directory: `bin/Release`

**Resolution:** Searches in:
1. Relative to `<Artefacts folder="...">`
2. Relative to root directory
3. Relative to current directory

##### to (optional)

**Type:** String
**Default:** Current build directory
**Purpose:** Destination directory

##### match (optional)

**Type:** String (wildcard pattern)
**Default:** `*.*`
**Purpose:** File pattern to copy (when `from` is directory)

##### name (optional)

**Type:** String
**Purpose:** Rename single file

Only valid when copying a single file.

##### transform (optional)

**Type:** String (transform expression)
**Purpose:** Transform filename

See [Transform Functions Reference](#transform-functions-reference).

##### recursive (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Include subdirectories

When `true`, `from` must be a directory.

##### if-different (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Leave a destination file alone when it already holds the same bytes

When `true`, a file that already exists at the destination is replaced only if the result of the
copy (after any `transform-content`) differs from it. An identical destination keeps its
timestamp, so a build that decides what to recompile from timestamps does not treat the copy as
a change. Use it when the copy refreshes files that a later step builds, e.g. regenerated sources.
A destination that does not exist is always written. With `--dry-run` the message reads
"Would copy if different".

**Child Elements:**

##### transform-content

Transforms file content during copy.

**Syntax:**
```xml
<transform-content transform="transformation" />
```

Can be specified multiple times; applied in order.

**Examples:**

**Example 1: Copy all DLLs**
```xml
<copy from="bin/Release" to="output" match="*.dll" />
```

**Example 2: Copy and rename**
```xml
<copy from="app.exe" to="bin" name="MyApp-~VERSION~.exe" />
```

**Example 3: Transform filename**
```xml
<copy from="logs" to="output" match="*.log"
      transform="replace,.log,-~VERSION~.log" />
<!-- app.log becomes app-1.2.3.log -->
```

**Example 4: Recursive copy**
```xml
<copy from="assets" to="output/assets" recursive="true" />
```

**Example 5: Transform content**
```xml
<copy from="config.template.json" to="output" name="config.json">
  <transform-content transform="replace,{{VERSION}},~VERSION~" />
  <transform-content transform="replace,{{API_URL}},~API_URL~" />
</copy>
```

**Example 6: Multiple files with content transform**
```xml
<copy from="templates" to="output" match="*.html">
  <transform-content transform="replace,%%YEAR%%,2024" />
</copy>
```

**Behavior:**
- Creates destination directory if it doesn't exist
- Overwrites existing files, unless `if-different="true"` and the contents are already identical
- Preserves directory structure when `recursive="true"`
- Content transformations only apply to text files

**Common Use Cases:**

**1. Environment-Specific Configuration Files:**

A common pattern is to maintain separate configuration files for each target environment (dev, staging, production) in a predefined folder structure, then copy the appropriate config based on the active target:

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

Instead of copying to a build output folder, you can use `copy` to construct a complete release directory structure:

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

This builds a complete release folder structure that gets packaged.

**3. Using Publish Folder:**

For .NET applications, you can either use the `dotnet publish` output folder directly, or copy from it:

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

Choose the approach based on your needs:
- Use publish folder directly for simple deployments
- Copy from publish folder when you need to combine with other files or create custom structure
- Build release tree when you need complete control over the final structure

---

### Action: modify

Modifies a file in-place using transform functions. This is the counterpart to `copy` with `transform-content` — use `modify` when you want to edit an existing file rather than creating a copy.

**Syntax:**
```xml
<modify file="path/to/file">
  <transform-content transform="transformation_expression" />
</modify>
```

##### file (required)

**Type:** String
**Purpose:** Path to the file to modify (supports variable expansion)

Path resolution follows the standard search order: artefacts folder, root directory, current directory.

##### transform-content (child elements)

Each `<transform-content>` element applies a transform function to the entire file contents. Multiple transforms are applied sequentially. The file is only rewritten if the content actually changed.

**Examples:**

**Example 1: Replace version placeholder**
```xml
<modify file="src/AssemblyInfo.cs">
  <transform-content transform="replace,0.0.0.0,~SemVer~.0" />
</modify>
```

**Example 2: Multiple transforms**
```xml
<modify file="config/app.conf">
  <transform-content transform="replace,__VERSION__,~SemVer~" />
  <transform-content transform="replace,__ENV__,~TYPE~" />
</modify>
```

**Example 3: Regex replacement**
```xml
<modify file="package.json">
  <transform-content transform="regex-replace,&quot;version&quot;:\s*&quot;[^&quot;]+&quot;,&quot;version&quot;: &quot;~SemVer~&quot;" />
</modify>
```

**When to use modify vs copy with transform-content:**
- Use `modify` when the file already exists at its final location and you want to edit it in-place
- Use `copy` with `transform-content` when you need to copy the file to a different location and transform during the copy

---

### Action: exec

Executes an external process.

**Syntax:**
```xml
<exec app="executable"
      args="arguments"
      folder="working_directory"
      required-exit-codes="0,1" />
```

**Attributes:**

##### app (required)

**Type:** String
**Purpose:** Executable name or path

**Resolution:** Searches in:
1. Directories specified with `--toolsdir` (in order)
2. Application base directory
3. System PATH
4. Absolute path (if specified)

##### args (optional)

**Type:** String
**Purpose:** Command-line arguments

Supports variable expansion.

##### folder (optional)

**Type:** String
**Default:** Current directory
**Purpose:** Working directory for execution

##### required-exit-codes (optional)

**Type:** Comma-separated integers
**Default:** `0`
**Purpose:** Exit codes that indicate success

By default only exit code 0 is considered success. Some tools use non-zero exit codes
for non-error conditions (e.g. robocopy uses 1 for "files copied"). Use this attribute
to specify which exit codes should be treated as success.

**Examples:**

**Example 1: Build with dotnet**
```xml
<exec app="dotnet" args="build -c Release" />
```

**Example 2: MSBuild with specific folder**
```xml
<exec app="msbuild.exe"
      args="MyApp.csproj /p:Configuration=Release"
      folder="src" />
```

**Example 3: With environment variable**
```xml
<exec app="dotnet"
      args="nuget push package.~VERSION~.nupkg --api-key $NUGET_API_KEY" />
```

**Example 4: npm build**
```xml
<exec app="npm" args="run build" folder="frontend" />
```

**Example 5: Custom script**
```xml
<exec app="bash" args="build-assets.sh" folder="scripts" />
```

**Example 6: Accept multiple exit codes**
```xml
<exec app="robocopy" args="src dest /MIR" required-exit-codes="0,1,2,3" />
```

**Behavior:**
- Waits for process to complete
- Exit code is checked against `required-exit-codes` (default: 0); non-matching codes cause build failure
- stdout and stderr are captured and logged at Debug level (visible with `-vv`)
- Use the `--shell-exec` command-line option to run processes in a visible terminal window instead
- Environment variables inherited from parent process

---

### Action: check-certificate

Validates a PKCS#12 (`.pfx` / `.p12`) certificate before the rest of the build depends on it.

**Syntax:**
```xml
<check-certificate file="signing/acme.pfx"
                   password="$SIGNING_PFX_PASSWORD"
                   min-days="30"
                   purpose="code-signing"
                   prefix="SIGNCERT">
  <trust-root file="ca/acme-internal-root.cer" />
</check-certificate>
```

**Attributes:**

##### file (required)

**Type:** String
**Purpose:** Path to the certificate archive

**Resolution:** Searches the action's folder, then the root directory, then the current directory.

##### password (optional)

**Type:** String
**Default:** None
**Purpose:** Passphrase for the archive

Supports variable and `$ENV` expansion. Registered as a secret automatically, so it is redacted from all log output; `secret="true"` is not needed. Note that values shorter than 8 characters are not registered — see [Secret Redaction](#secret-redaction).

##### min-days (optional)

**Type:** Integer
**Default:** `0`
**Purpose:** Minimum days that must remain before expiry

`0` checks only that the certificate has not already expired.

##### private-key (optional)

**Type:** Boolean
**Default:** `true`
**Purpose:** Require the archive to contain the private key

##### purpose (optional)

**Type:** `token-signing` | `code-signing` | `server-auth` | `client-auth` | `ca` | `none`
**Default:** None
**Purpose:** Shorthand for the `eku` requirement implied by that purpose

##### eku (optional)

**Type:** Comma-separated strings
**Default:** None
**Purpose:** Extended key usages the certificate must declare

Accepts friendly names (`code-signing`, `server-auth`, `client-auth`, `email-protection`, `time-stamping`, `ocsp-signing`, `any`) or raw OIDs. Overrides `purpose`.

##### key-algorithm (optional)

**Type:** `rsa` | `ecdsa`
**Default:** None
**Purpose:** Require a particular public key algorithm

##### min-key-size (optional)

**Type:** Integer
**Default:** `0`
**Purpose:** Minimum key size in bits

##### trusted (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Require the certificate to chain to a trusted root

Uses the operating system's trust store unless `<trust-root>` children are supplied, in which case only those roots are trusted.

##### revocation (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Check revocation online. Only meaningful with `trusted="true"`

##### thumbprint (optional)

**Type:** String
**Default:** None
**Purpose:** Expected SHA-1 thumbprint

Separators (`:`, `-`, spaces) are ignored and matching is case-insensitive, so a thumbprint copied from any tool works unmodified.

##### subject (optional)

**Type:** String
**Default:** None
**Purpose:** Case-insensitive substring the subject distinguished name must contain

##### prefix (optional)

**Type:** String
**Default:** `CERT`
**Purpose:** Prefix for the exported variables

##### on-fail (optional)

**Type:** `stop` | `error` | `warn`
**Default:** `stop`
**Purpose:** What a failed check does to the build

| Value | Effect |
|-------|--------|
| `stop` | Abort the build immediately; exit code `2`. Nothing after the check runs |
| `error` | Report the failures and continue; exit code `2` at the end of the run |
| `warn` | Report at Warn and continue; the exit code is unaffected |

`stop` is the default because a check that lets the build proceed is not a check — a signing
step would run against a certificate already known to be bad, and a long build would complete
before reporting a problem detectable at the start. Use `error` when you would rather one run
surfaced every problem in the config, and `warn` when the build should decide for itself using
`~PREFIX_VALID~`.

##### active (optional)

**Type:** String
**Default:** None
**Purpose:** Condition expression using the same `when,~VAR~,==,value` syntax as `<Artefacts active="...">`

**Child element: `<trust-root>`**

Zero or more roots to build the chain against instead of the operating system's trust store — the way to validate against an internal CA without installing anything on the build machine.

| Attribute | Required | Description |
|-----------|----------|-------------|
| `file` | Yes | DER or PEM `.cer`/`.crt`, or a `.pfx` |
| `password` | No | Passphrase, if the file is a password-protected `.pfx` |

**Examples:**

**Example 1: Fail if the signing certificate expires within a month**
```xml
<check-certificate file="signing/acme.pfx" password="$SIGNING_PFX_PASSWORD" min-days="30" />
```

**Example 2: Check without failing, then sign only if it passed**
```xml
<check-certificate file="signing.pfx" password="$PW" on-fail="warn" prefix="SIGN" />
<exec app="signtool" args="sign /f signing.pfx /p $PW /fd sha256 app.exe"
      active="when,~SIGN_VALID~,==,1" />
```

**Example 3: Pin to a known certificate**
```xml
<check-certificate file="signing.pfx" password="$PW"
                   thumbprint="A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90:A1:B2:C3:D4" />
```

**Example 4: Validate against an internal CA**
```xml
<check-certificate file="leaf.pfx" password="$PW" trusted="true">
  <trust-root file="ca/internal-root.cer" />
</check-certificate>
```

**Behavior:**
- Every failed criterion is reported, not just the first
- A certificate with **no** EKU extension satisfies any `eku`/`purpose` requirement, with a warning — per RFC 5280 an absent EKU means unconstrained, and Authenticode accepts it
- All date comparisons are performed in UTC
- Runs for real under `--dry-run`; it mutates nothing, and confirming the certificate is present and in date is the most useful check a dry run can make
- Every failed criterion is reported with its config line and column; `on-fail` then decides whether the build stops there, continues and fails at the end, or merely warns
- Anything that prevents the check running — a missing file, an unreadable trust root, an unknown `purpose` or `eku` — is treated as a failed check, since it leaves the build just as unverified

**Exported variables:** see [Certificate Variables](#certificate-variables).

---

### Action: create-certificate

Generates a PKCS#12 certificate entirely in managed code — no `openssl`, no `New-SelfSignedCertificate`, no external tool of any kind, and identical behaviour on Windows, Linux and macOS.

The common case is an OpenID token-signing certificate: ABP.IO and OpenIddict need an `authserver.pfx` that has no extended key usage, and for which self-signed is the correct answer.

**Security note:** a self-signed certificate is trusted only by machines explicitly told to trust it. For `code-signing` and `server-auth` that makes it suitable for test and internal use only — a certificate for public distribution must come from a commercial CA, and ReleaseBuilder logs a warning saying so. For `token-signing` the opposite holds: the certificate is only ever validated by the issuer itself, so self-signed is correct and no warning is emitted.

**Syntax:**
```xml
<create-certificate file="authserver.pfx"
                    subject="CN=AuthServer"
                    purpose="token-signing"
                    password="$AUTH_PFX_PASSWORD"
                    days="3650" />
```

**Attributes:**

##### file (required)

**Type:** String
**Purpose:** Output path for the `.pfx`

The file need not exist; the directory must.

##### subject (required)

**Type:** String
**Purpose:** X.500 distinguished name, e.g. `CN=AuthServer, O=Acme Ltd, C=GB`

##### password (optional)

**Type:** String
**Default:** None
**Purpose:** Passphrase to protect the archive

Registered as a secret automatically.

##### days (optional)

**Type:** Integer
**Default:** `730`
**Purpose:** Validity period in days from `not-before`

##### not-before (optional)

**Type:** ISO-8601 date/time
**Default:** Five minutes ago
**Purpose:** Start of the validity window

The small backdate absorbs clock skew between the build machine and whatever validates the certificate.

##### purpose (optional)

**Type:** `token-signing` | `code-signing` | `server-auth` | `client-auth` | `ca` | `none`
**Default:** `token-signing`
**Purpose:** Preset for extended key usage, key usage and basic constraints

| `purpose` | Extended key usage | Key usage | CA | Self-signed warning |
|-----------|--------------------|-----------|----|---------------------|
| `token-signing` | *(none)* | digitalSignature, keyEncipherment | No | No |
| `code-signing` | `1.3.6.1.5.5.7.3.3` | digitalSignature | No | Yes |
| `server-auth` | `1.3.6.1.5.5.7.3.1` | digitalSignature, keyEncipherment | No | Yes |
| `client-auth` | `1.3.6.1.5.5.7.3.2` | digitalSignature, keyEncipherment | No | No |
| `ca` | *(none)* | keyCertSign, cRLSign | Yes | No |
| `none` | *(none)* | digitalSignature | No | No |

##### eku (optional)

**Type:** Comma-separated strings
**Default:** From `purpose`
**Purpose:** Extended key usages to write

Friendly names or raw OIDs; anything containing a dot is treated as an OID. `none` writes no EKU extension at all, which per RFC 5280 leaves the certificate unconstrained.

##### key-usage (optional)

**Type:** Comma-separated strings
**Default:** From `purpose`
**Purpose:** Key usage flags

Accepts `digital-signature`, `non-repudiation`, `key-encipherment`, `data-encipherment`, `key-agreement`, `key-cert-sign`, `crl-sign`, `encipher-only`, `decipher-only`, `none`.

##### key (optional)

**Type:** `rsa` | `ecdsa`
**Default:** `rsa`
**Purpose:** Key algorithm

`signtool` and Authenticode support for ECDSA signing certificates is patchy; RSA is the tested path for code signing.

##### key-size (optional)

**Type:** Integer
**Default:** `3072` for RSA, `256` for ECDSA
**Purpose:** Key size in bits

RSA accepts 2048, 3072 or 4096. ECDSA accepts 256 (nistP256), 384 (nistP384) or 521 (nistP521).

##### hash (optional)

**Type:** `sha256` | `sha384` | `sha512`
**Default:** `sha256`
**Purpose:** Signature hash algorithm

##### dns-names (optional)

**Type:** Comma-separated strings
**Default:** None
**Purpose:** Subject alternative names (dNSName entries)

##### issuer-file (optional)

**Type:** String
**Default:** None
**Purpose:** A CA `.pfx` to sign the new certificate with

Omit for a self-signed certificate. The issuer must contain a private key.

##### issuer-password (optional)

**Type:** String
**Default:** None
**Purpose:** Passphrase for `issuer-file`. Registered as a secret

##### overwrite (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Whether an existing file may be replaced

Defaults to refusing, so a config cannot silently regenerate a signing identity on every build.

##### prefix (optional)

**Type:** String
**Default:** `CERT`
**Purpose:** Prefix for the exported variables

##### on-fail (optional)

**Type:** `stop` | `error` | `warn`
**Default:** `stop`
**Purpose:** What a failure to generate the certificate does to the build

Takes the same three values as [check-certificate's on-fail](#on-fail-optional), and defaults to
`stop` for the same reason: a certificate that could not be written leaves everything downstream
operating on a file that is missing or stale. Under `error` and `warn` the build continues, but
the `~PREFIX_*~` variables are not published, so a later reference to one fails with
"Variable not found".

##### active (optional)

**Type:** String
**Default:** None
**Purpose:** Condition expression using the same `when,~VAR~,==,value` syntax as `<Artefacts active="...">`

**Examples:**

**Example 1: ABP.IO / OpenIddict authserver.pfx**
```xml
<create-certificate file="src/Acme.AuthServer/authserver.pfx"
                    subject="CN=AuthServer"
                    password="$AUTH_PFX_PASSWORD"
                    days="3650" />
```

**Example 2: Throwaway code-signing certificate for a test build**
```xml
<create-certificate file="build/test-signing.pfx"
                    subject="CN=Acme Test Signing, O=Acme Ltd, C=GB"
                    purpose="code-signing"
                    password="$TEST_PFX_PASSWORD"
                    days="90"
                    overwrite="true" />
```

**Example 3: Internal CA and a leaf signed by it**
```xml
<create-certificate file="ca/internal-root.pfx" subject="CN=Acme Internal Root"
                    purpose="ca" days="3650" password="$CA_PASSWORD" />
<create-certificate file="build/service.pfx" subject="CN=service.internal"
                    purpose="server-auth" dns-names="service.internal,service"
                    issuer-file="ca/internal-root.pfx" issuer-password="$CA_PASSWORD" />
```

**Example 4: A purpose the presets do not cover**
```xml
<create-certificate file="ts.pfx" subject="CN=Timestamp"
                    eku="time-stamping,1.3.6.1.5.5.7.3.9"
                    key-usage="digital-signature" key="ecdsa" key-size="384" />
```

**Behavior:**
- Under `--dry-run` the certificate is generated in memory and the variables are published, but nothing is written — so a later `~CERT_THUMBPRINT~` reference still resolves during a dry run
- A leaf signed with `issuer-file` receives an authority key identifier and a random 128-bit serial number
- Generated PKCS#12 archives use the legacy password-based encryption that every version of `signtool` and `certutil` accepts
- Anything that prevents the certificate being written — a bad attribute, a refused overwrite, an unusable `issuer-file` — stops the build by default

**What this action does not do:** it creates certificates, it does not use them. Authenticode signing of an executable or MSI, RFC-3161 timestamping, enrolment against an external CA, and installing a root into the operating system's trust store all still require `signtool`, `osslsigncode` or `certutil` via `<exec>`.

**Exported variables:** see [Certificate Variables](#certificate-variables).

<a id="certificate-variables"></a>
### Certificate Variables

Both certificate actions publish the certificate's properties as variables, named with the action's `prefix` (default `CERT`):

| Variable | Description |
|----------|-------------|
| `~PREFIX_SUBJECT~` | Subject distinguished name |
| `~PREFIX_ISSUER~` | Issuer distinguished name |
| `~PREFIX_THUMBPRINT~` | SHA-1 thumbprint, uppercase hex, no separators |
| `~PREFIX_SERIAL~` | Serial number |
| `~PREFIX_NOT_BEFORE~` | Start of validity, `yyyy-MM-ddTHH:mm:ssZ` |
| `~PREFIX_NOT_AFTER~` | End of validity, `yyyy-MM-ddTHH:mm:ssZ` |
| `~PREFIX_DAYS_REMAINING~` | Days until expiry; negative if already expired |
| `~PREFIX_HAS_PRIVATE_KEY~` | `1` or `0` |
| `~PREFIX_KEY_ALGORITHM~` | `rsa` or `ecdsa` |
| `~PREFIX_KEY_SIZE~` | Key size in bits |
| `~PREFIX_EKU~` | Comma-separated extended key usages, or `(none)` |
| `~PREFIX_VALID~` | `1` if every check passed, `0` otherwise |
| `~PREFIX_FILE~` | Output path (`create-certificate` only) |

Values are never empty — booleans are `1`/`0` and absent strings become `(none)` — because an empty variable value is itself reported as an error.

If a `<check-certificate>` cannot read the file at all, only `~PREFIX_VALID~` is set (to `0`); the remaining variables are left undefined, and referencing one is an error.

---

### Action: create

Creates a text file with specified content.

**Syntax:**
```xml
<create file="filename">
  Text content here
  Variables like ~VERSION~ are expanded
</create>
```

**Attributes:**

##### file (required)

**Type:** String
**Purpose:** File path to create

**Resolution:** Relative to:
1. `<Artefacts folder="...">` if specified
2. Current directory

**Content:**

Text content of the element. Line endings normalized to `\n`.

**Variable Expansion:**
All variables in content are expanded.

**Examples:**

**Example 1: Version file**
```xml
<create file="VERSION.txt">~SemVer~</create>
```

**Example 2: Multi-line config**
```xml
<create file="app.config">
[Application]
Version=~VERSION~
Environment=~TYPE~
ApiUrl=~API_URL~

[Logging]
Level=~LOG_LEVEL~
</create>
```

**Example 3: JSON config**
```xml
<create file="config.json">
{
  "version": "~SemVer~",
  "environment": "~TYPE~",
  "apiUrl": "~API_URL~",
  "branch": "~BranchName~"
}
</create>
```

**Example 4: TypeScript/JavaScript**
```xml
<create file="src/version.ts">
export const version = {
  number: '~SemVer~',
  branch: '~BranchName~',
  gitSha: '~Sha~'
};
</create>
```

**Example 5: With GitVersion JSON**
```xml
<create file="version.json">~GITVERSION.JSON~</create>
```

**Behavior:**
- Overwrites existing file
- Creates parent directories if needed
- UTF-8 encoding without BOM
- Line endings normalized

---

### Action: xml-edit

Edits XML files using XPath selectors.

**Syntax:**
```xml
<xml-edit file="filename" omit-declaration="true|false">
  <node path="xpath_expression" action="transformation" />
  ...
</xml-edit>
```

**Attributes:**

##### file (required)

**Type:** String
**Purpose:** XML file to edit

File must exist and be valid XML.

##### omit-declaration (optional)

**Type:** Boolean
**Default:** `false`
**Purpose:** Omit `<?xml ...?>` declaration when saving

**Child Elements:**

##### node / attribute

Both directives are equivalent — use whichever makes the intent clearer. `<node>` is conventional for element content; `<attribute>` is a readable alias that signals you are targeting an attribute.

**Syntax:**
```xml
<node      path="xpath_expression" action="transformation" />
<attribute path="xpath_expression" action="transformation" />
```

**Attributes:**
- `path` (required) - XPath expression to select elements or attributes
- `action` (required) - Transformation to apply to the matched value

**Examples:**

**Example 1: Update version element**
```xml
<xml-edit file="Package.appxmanifest">
  <node path="//Identity/@Version" action="set,~VERSION~" />
</xml-edit>
```

**Example 2: Update app settings**
```xml
<xml-edit file="web.config">
  <node path="//appSettings/add[@key='Environment']/@value"
        action="set,~TYPE~" />
  <node path="//appSettings/add[@key='Version']/@value"
        action="set,~VERSION~" />
</xml-edit>
```

**Example 3: Replace text in nodes**
```xml
<xml-edit file="config.xml">
  <node path="//connectionStrings/add/@connectionString"
        action="replace,localhost,~DB_SERVER~" />
</xml-edit>
```

**Example 4: Multiple transformations**
```xml
<xml-edit file="project.xml">
  <node path="//PropertyGroup/Version" action="set,~SemVer~" />
  <node path="//PropertyGroup/Authors" action="set,~AUTHOR~" />
  <node path="//PropertyGroup/Description"
        action="replace,TODO,~DESCRIPTION~" />
</xml-edit>
```

**Example 5: Android manifest — namespace-prefixed attributes**

Android manifests use `android:versionName` and `android:versionCode`, but the `android:` namespace prefix cannot be resolved without a namespace manager. Use a `local-name()` predicate instead:

```xml
<xml-edit file="app/src/main/AndroidManifest.xml">
  <attribute path="/manifest/@*[local-name()='versionName']" action="set,~SemVer~" />
  <attribute path="/manifest/@*[local-name()='versionCode']" action="set,~IntSemVer~" />
</xml-edit>
```

**XPath Support:**
- Full XPath 1.0 syntax
- Attribute selection with `@`
- Predicates for filtering
- Axes for navigation

**Common XPath Patterns:**
```xpath
//elementName                           Select all <elementName> elements
//parent/child                          Select <child> under <parent>
//@attributeName                        Select all named attributes
//element[@attr='value']                Filter by attribute value
//element[@attr='value']/@attr2         Select attribute of filtered element
//element[1]                            First element
//element[last()]                       Last element
/root/@*[local-name()='attrName']       Attribute by local name (namespace-safe)
```

**Behavior:**
- File must be well-formed XML
- Changes saved back to same file
- Only nodes matching XPath are modified
- If XPath matches no nodes, no error (silent skip)
- Multiple `<node>` / `<attribute>` elements processed in order

---

## Transform Functions Reference

Transformations manipulate strings in various contexts:
- Filename transformations in `<copy>`
- Content transformations in `<copy>`
- Value transformations in `<xml-edit>`
- Conditional expressions in `active` attributes

### Syntax

```
function_name,arg1,arg2,...
```

Arguments are comma-separated. Variable expansion occurs before function execution.

### Function: set

Replaces value entirely.

**Syntax:**
```
set,new_value
```

**Parameters:**
- `new_value` - Value to set

**Examples:**
```xml
<!-- Set node value -->
<node path="//version" action="set,~VERSION~" />

<!-- Set to literal -->
<node path="//status" action="set,active" />
```

---

### Function: replace

String replacement.

**Syntax:**
```
replace,search,replacement
```

**Parameters:**
- `search` - Text to find (literal string)
- `replacement` - Text to replace with

**Examples:**
```xml
<!-- Replace in filename -->
<copy from="app.exe" transform="replace,.exe,-~VERSION~.exe" />
<!-- app.exe → app-1.2.3.exe -->

<!-- Replace in content -->
<transform-content transform="replace,{{VERSION}},~VERSION~" />

<!-- Replace in XML -->
<node path="//server" action="replace,localhost,prod-server" />
```

**Behavior:**
- Case-sensitive
- Replaces all occurrences
- If `search` not found, returns original value

---

### Function: regex-replace

Regular expression replacement.

**Syntax:**
```
regex-replace,pattern,replacement
```

**Parameters:**
- `pattern` - Regular expression pattern (.NET regex syntax)
- `replacement` - Replacement string (supports capture groups)

**Examples:**
```xml
<!-- Replace version pattern -->
<node path="//version" action="regex-replace,\d+\.\d+\.\d+,~VERSION~" />

<!-- Remove non-alphanumeric -->
<transform-content transform="regex-replace,[^a-zA-Z0-9],_" />

<!-- Extract and reformat -->
<node path="//id" action="regex-replace,^ID(\d+)$,$1" />
<!-- ID123 → 123 -->
```

**.NET Regex Syntax:**
- `.` - Any character
- `\d` - Digit
- `\w` - Word character
- `\s` - Whitespace
- `*` - Zero or more
- `+` - One or more
- `?` - Zero or one
- `[abc]` - Character class
- `(...)` - Capture group
- `^` - Start of string
- `$` - End of string

**Replacement Tokens:**
- `$1`, `$2`, ... - Captured groups
- `$&` - Entire match

---

### Function: getversion

Extracts version number from string.

**Syntax:**
```
getversion,path
```

**Parameters:**
- `path` - String containing version

**Extraction Pattern:**
`\d+.+\d` - Digits and dots (e.g., `1.2.3`, `2.0.1.4`)

Takes last path component (after last `/` or `\`) and extracts version pattern.

**Examples:**
```xml
<Folder name="BUILD" path="Builds/MyApp_*" version="latest" />
<!-- BUILD = "Builds/MyApp_1.2.3.4" -->

<Target name="prod" archive-version="getversion,~BUILD~" />
<!-- archive-version = "1.2.3.4" -->
```

**Example Extractions:**
- `C:\Builds\MyApp_1.2.3` → `1.2.3`
- `/home/app/Release_2.0.1.4` → `2.0.1.4`
- `Package_1.0` → `1.0`

---

### Function: random-key

Generates a cryptographically random key for use as a build-time secret.

**Syntax:**
```
random-key[,length[,charset]]
```

**Parameters:**
- `length` - Number of characters (default `32`); may be a variable reference
- `charset` - Named character set, or the literal characters to choose from (default `alnum`)

**Character Sets:**

| Name | Characters |
|------|------------|
| `alnum` (default) | `A-Z`, `a-z`, `0-9` |
| `alpha` | `A-Z`, `a-z` |
| `numeric` | `0-9` |
| `hex` | `0-9`, `a-f` |
| `base64` | `A-Z`, `a-z`, `0-9`, `+`, `/` |
| `base64url` | `A-Z`, `a-z`, `0-9`, `-`, `_` |
| `ascii` | All printable ASCII except space (`!` to `~`) |
| Any other value | Used literally as the set of characters (duplicates ignored; escape a comma as `\,`) |

**Examples:**
```xml
<!-- 32-character alphanumeric key, held in a variable for the whole build -->
<Target name="production" path="releases" type="zip">
  <Set name="API_SECRET" transform="random-key" />
  <Set name="DB_KEY"     transform="random-key,64,hex" />
  <Set name="PIN"        transform="random-key,6,numeric" />
</Target>

<!-- Substitute the generated key into a config file as it is copied -->
<copy from="appsettings.template.json" to="appsettings.json">
  <transform-content transform="replace,__API_SECRET__,~API_SECRET~" />
</copy>

<!-- Write a secrets file for deployment -->
<create file="secrets.env">API_SECRET=~API_SECRET~</create>

<!-- Direct use in an XML edit: a fresh key each time it is evaluated -->
<xml-edit file="web.config">
  <node path="//appSettings/add[@key='MachineKey']/@value" action="random-key,48" />
</xml-edit>
```

**Behaviour:**
- Each evaluation returns a new key. To use one key in several places, assign it once with `<Set name="..." transform="random-key,..."/>` and reference the variable.
- Characters are drawn from the platform cryptographic random number generator (`RandomNumberGenerator`) with equal probability.
- Errors (raised as build errors): length that is not a positive integer, more than two arguments, or a character set with fewer than two distinct characters.

**Notes:**
- The default `alnum` set avoids characters that need escaping in JSON, XML, shell scripts, and `.env` files. `ascii` includes `$`, `&`, `<`, `"` and similar; check the destination format before using it.
- Avoid `random-key` as the replacement in `regex-replace` — `$` characters in a generated key are interpreted as regex substitutions.
- Generated keys register themselves for [secret redaction](#secret-redaction) without needing `secret="true"`, so they reach the log as `***`. Keys shorter than 8 characters are not registered.

---

### Function: when

Conditional transformation.

**Syntax:**
```
when,value1,operator,value2
```

**Parameters:**
- `value1` - Left operand
- `operator` - Comparison operator
- `value2` - Right operand

**Operators:**
- `==` - Equals
- `!=` - Not equals
- `>` - Greater than
- `<` - Less than
- `>=` - Greater than or equal
- `<=` - Less than or equal

**Returns:**
- Original value if condition is true
- Empty string if condition is false

**Examples:**
```xml
<!-- Conditional artifact inclusion -->
<Artefacts active="when,~TYPE~,==,production">
  <folder>production-only</folder>
</Artefacts>

<!-- Multiple conditions -->
<Artefacts active="when,~INCLUDE_DOCS~,==,true">
  <folder>docs</folder>
</Artefacts>
```

**Use Cases:**
- Conditional `<Artefacts>` processing
- Feature flags
- Environment-specific logic

**Comparison Logic:**
- String comparison (case-sensitive)
- Numeric comparison if both values are numbers
- Empty string is falsy

---

## Path Resolution

### Working Directory

The working directory affects how relative paths are resolved.

**Initial Working Directory:**
- Specified with `--root` option, or
- Current directory when ReleaseBuilder is invoked

**Available as:** `~PUBLISHROOT~`

### Relative Path Resolution

When a relative path is encountered, ReleaseBuilder searches in multiple locations:

**General Pattern:**
1. Relative to `<Artefacts folder="...">` (if specified)
2. Relative to root directory (`--root` or current)
3. Relative to current working directory

**Example:**
```xml
<Artefacts folder="output">
  <build>
    <copy from="bin/Release" to="dist" />
  </build>
</Artefacts>
```

Search for `bin/Release`:
1. `output/bin/Release`
2. `{root}/bin/Release`
3. `{cwd}/bin/Release`

### Absolute Paths

Absolute paths are used as-is:

**Windows:**
```xml
<copy from="C:\builds\output" to="dist" />
```

**Unix:**
```xml
<copy from="/home/user/builds/output" to="dist" />
```

### Variable Expansion in Paths

Variables are expanded before path resolution:

```xml
<Folder name="BUILDDIR" path="builds/latest" />
<copy from="~BUILDDIR~/bin" to="output" />
<!-- Searches: builds/latest/bin -->
```

### Path Separators

Both `/` and `\` are accepted. Internally normalized to platform separator.

```xml
<!-- These are equivalent: -->
<copy from="bin/Release" />
<copy from="bin\Release" />
```

### skip-directories-front

Removes leading path components from output.

**Example:**
```xml
<file skip-directories-front="2">path/to/deep/file.txt</file>
<!-- Output: deep/file.txt (skipped "path/to") -->

<folder skip-directories-front="1">source/files</folder>
<!-- Contents of source/files/ placed in output root -->
```

**Use Case:** Flattening directory structure in output.

---

## Exit Codes

| Code | Constant | Meaning |
|------|----------|---------|
| 0 | `Success` | Build completed with no errors |
| 1 | `ConfigError` | Configuration file not found |
| 2 | `BuildError` | Build actions failed — one or more errors were logged |
| 3 | `ArtefactError` | Artefact processing failed |
| 4 | `InvalidArguments` | Unrecognised command-line arguments |

ReleaseBuilder uses continue-and-report mode: it logs all errors and returns a non-zero exit code at the end, rather than stopping at the first error. This lets you see all problems in one run.

A failed `<check-certificate>` or `<create-certificate>` returns `2`, and by default these are the actions that stop the run rather than continuing — see [on-fail](#on-fail-optional). There is no distinct exit code for it. Use `on-fail="error"` for continue-and-report, or `on-fail="warn"` with `~PREFIX_VALID~` if the build should carry on and decide for itself.

**Viewing Errors:**
Use `-v` or `-vv` for detailed error information:

```bash
release-builder --target production -vv
```

---

## CI/CD Integration

### Installation

Install ReleaseBuilder as a global .NET tool:

```bash
dotnet tool install --global ReleaseBuilder
release-builder --help
```

Or install a specific version:

```bash
dotnet tool install --global ReleaseBuilder --version 1.2.3
```

### Validate Config on Pull Requests

Use `--dry-run` in PR pipelines to validate configuration without building:

```yaml
- name: Validate ReleaseBuilder config
  run: release-builder --dry-run --config ReleaseConfig.xml --target production
```

Exit code is non-zero if errors are found (missing files, bad XML, etc.), so the pipeline fails fast.

### Capture Build Outputs

Use `--output-manifest` to feed artefact paths to subsequent pipeline steps:

```yaml
- name: Build release
  run: release-builder --config ReleaseConfig.xml --target production --output-manifest manifest.json

- name: Upload artefacts
  run: |
    path=$(jq -r '.Artefacts[0].Path' manifest.json)
    # upload $path to object storage, etc.
```

### GitHub Actions Example

```yaml
- name: Install ReleaseBuilder
  run: dotnet tool install --global ReleaseBuilder

- name: Build
  run: release-builder --config ReleaseConfig.xml --target ${{ github.ref == 'refs/heads/main' && 'production' || 'staging' }} --output-manifest manifest.json

- name: Check exit code
  run: |
    if [ $? -ne 0 ]; then
      echo "Build failed"
      exit 1
    fi
```

---

## File Formats

### Configuration File Format

**Format:** XML
**Encoding:** UTF-8
**Schema:** `ReleaseConfig.xsd`
**Root Element:** `<ReleaseConfig>`

**Validation:**

Most XML editors support XSD validation. Configure your editor to use `ReleaseConfig.xsd`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ReleaseConfig xmlns:p1="http://www.w3.org/2001/XMLSchema-instance"
               p1:noNamespaceSchemaLocation="ReleaseConfig.xsd">
  ...
</ReleaseConfig>
```

### Output Formats

#### ZIP Archive

**Format:** Standard ZIP format
**Compression:** Deflate
**Encoding:** UTF-8 for filenames

**Naming:**
- With `archive-version`: `{name}-{version}.zip`
- Without: `{name}.zip`

**Structure:**
```
MyApp-1.2.3.zip
├── file1.exe
├── file2.dll
├── config/
│   └── settings.json
└── README.md
```

#### Folder Output

**Structure:** Direct file copy

Files and folders are copied to the target directory maintaining their structure (respecting `skip-directories-front`).

---

## Appendix

### Common Patterns

#### Multi-Environment Configuration

```xml
<Target name="dev" path="builds/dev" type="folder">
  <Set name="ENV" value="Development" />
</Target>
<Target name="staging" path="builds/staging" type="folder">
  <Set name="ENV" value="Staging" />
</Target>
<Target name="prod" path="releases" type="zip" archive-version="~SemVer~">
  <Set name="ENV" value="Production" />
</Target>
```

#### Versioned Releases

```xml
<Target name="release"
        path="releases"
        type="zip"
        archive-version="~SemVer~" />

<Artefacts>
  <build>
    <create file="VERSION.txt">~SemVer~</create>
  </build>
</Artefacts>
```

#### Clean Build

```xml
<Artefacts>
  <build>
    <clean folder="bin" include-folders="true" />
    <clean folder="obj" include-folders="true" />
    <exec app="dotnet" args="build -c Release" />
  </build>
</Artefacts>
```

### Performance Considerations

- **Clean operations** on large directories can be slow
- **Recursive copy** may take time for large directory trees
- **XML editing** loads entire file into memory
- Use `--nobuild` to test artifact collection without rebuilding

### Limitations

- **Maximum path length**: Subject to OS limits (260 chars on Windows without long path support)
- **XML size**: Large XML files (>100MB) may cause performance issues in xml-edit
- **Regex complexity**: Very complex regex patterns may be slow
- **Concurrent execution**: Not designed for parallel builds

---

## See Also

- [User Guide](User-Guide.md) - Task-oriented tutorials and examples
- [ReleaseConfig Format Reference](ReleaseConfig-Format.md) - Detailed XML format documentation with examples
- [README.md](../README.md) - Project overview and quick start
