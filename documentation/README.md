# ReleaseBuilder Documentation

Welcome to the ReleaseBuilder documentation! This guide will help you find the information you need.

## Documentation Structure

ReleaseBuilder documentation is organized into three main documents:

### 1. [User Guide](User-Guide.md) 📘
**Best for: Learning and tutorials**

A task-oriented guide with step-by-step tutorials, common tasks, and best practices.

**Contents:**
- Getting started with installation and setup
- Basic concepts (variables, targets, artifacts)
- Common tasks (creating configs, adding targets, versioning)
- Complete tutorials (web apps, Angular, multi-component builds, NuGet)
- Advanced techniques (config transformations, dynamic file selection, conditionals)
- Troubleshooting common problems
- Best practices and tips

**Start here if you:**
- Are new to ReleaseBuilder
- Want to learn by example
- Need to accomplish a specific task
- Want to see real-world usage patterns

### 2. [User Reference Manual](User-Reference.md) 📖
**Best for: Complete specifications and details**

A comprehensive reference manual covering every command-line option, configuration element, and feature in detail.

**Contents:**
- Complete command-line reference with all options
- Configuration file structure and elements
- Variable system (built-in, user-defined, environment)
- Build actions reference (clean, copy, exec, create, xml-edit)
- Transform functions (set, replace, regex-replace, getversion, when)
- Path resolution rules
- Exit codes and error handling
- File format specifications

**Use this when you:**
- Need exact syntax and parameters
- Want to understand all available options
- Are looking for detailed specifications
- Need to know how features work internally

### 3. [ReleaseConfig Format Reference](ReleaseConfig-Format.md) 📋
**Best for: XML configuration details and examples**

Detailed documentation of the ReleaseConfig.xml format with extensive examples.

**Contents:**
- Complete XML element reference
- All attributes and their meanings
- Build action syntax and usage
- Transform function reference
- Six complete real-world examples
- Variable substitution guide
- Best practices for config files

**Use this when you:**
- Are writing or editing ReleaseConfig.xml
- Need XML syntax examples
- Want to see complete working configurations
- Need quick reference for elements and attributes

## Quick Reference

### Common Tasks

| I want to... | See... |
|--------------|--------|
| Install ReleaseBuilder | [User Guide: Installation](User-Guide.md#installation) |
| Create my first config | [User Guide: Task 1](User-Guide.md#task-1-create-your-first-config) |
| Build for multiple environments | [User Guide: Task 2](User-Guide.md#task-2-add-multiple-targets) |
| Add version numbers | [User Guide: Task 3](User-Guide.md#task-3-add-versioning) |
| Copy environment-specific configs | [User Guide: Task 7](User-Guide.md#task-7-copy-environment-specific-configuration-files) |
| Build a .NET web app | [User Guide: Tutorial 1](User-Guide.md#tutorial-1-building-a-net-web-application) |
| Build an Angular app | [User Guide: Tutorial 2](User-Guide.md#tutorial-2-building-an-angular-application) |
| Build multiple components | [User Guide: Tutorial 3](User-Guide.md#tutorial-3-multi-component-application) |
| Publish to NuGet | [User Guide: Tutorial 4](User-Guide.md#tutorial-4-publishing-a-nuget-package) |
| Understand all command-line options | [User Reference: Command-Line](User-Reference.md#command-line-reference) |
| See all XML elements | [Format Reference: Elements](ReleaseConfig-Format.md#elements-reference) |
| Use variables | [User Reference: Variable System](User-Reference.md#variable-system) |
| Copy files | [User Reference: copy action](User-Reference.md#action-copy) |
| Transform file contents | [Format Reference: copy with transform](ReleaseConfig-Format.md#copy) |
| Edit XML files | [User Reference: xml-edit action](User-Reference.md#action-xml-edit) |
| Troubleshoot errors | [User Guide: Troubleshooting](User-Guide.md#troubleshooting) |

### By Role

#### Developers (First-time users)
1. Read [User Guide: Getting Started](User-Guide.md#getting-started)
2. Try [User Guide: Task 1-3](User-Guide.md#common-tasks)
3. Review [Best Practices](User-Guide.md#best-practices)

#### Release Engineers
1. Study [User Guide: Tutorials](User-Guide.md#tutorials)
2. Explore [Advanced Techniques](User-Guide.md#advanced-techniques)
3. Reference [User Reference Manual](User-Reference.md)

#### Power Users
1. Master [Transform Functions](User-Reference.md#transform-functions-reference)
2. Learn [Variable System](User-Reference.md#variable-system)
3. Review [Complete Examples](ReleaseConfig-Format.md#examples)

## Additional Resources

### Schema Validation
- [ReleaseConfig.xsd](../ReleaseConfig.xsd) - XML Schema Definition file
- Configure your XML editor to use this schema for validation and autocomplete

### Quick Start
See the [main README](../README.md) for project overview and quick installation.

## Getting Help

### Verbose Output
Use `-v` or `-vv` flags to see detailed execution information:
```bash
ReleaseBuilder -v          # Trace level
ReleaseBuilder -vv         # Debug level (most detailed)
```

### Help Command
```bash
ReleaseBuilder --help
```

### Common Issues
Check the [Troubleshooting section](User-Guide.md#troubleshooting) in the User Guide.

## Documentation Contributions

Found an issue or want to improve the documentation? The documentation is written in Markdown and located in the `documentation/` folder.

## License

ReleaseBuilder is licensed under the GNU General Public License v2.0 (GPL-2.0).

---

**Happy building!** 🚀
