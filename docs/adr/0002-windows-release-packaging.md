# ADR 0002: Windows release packaging

## Status

Accepted for Phase 5.0.

## Context

The WPF application uses .NET 10 and native Windows filesystem APIs. Distribution must work without a separately installed runtime, remain easy to inspect, support per-user install/uninstall, and leave signing credentials outside the repository.

## Decision

Publish an untrimmed, non-single-file, self-contained `win-x64` folder and package it as both a portable ZIP and an Inno Setup 6.7.3 installer. The installer uses the lowest privilege level and `%LOCALAPPDATA%`. Its stable AppId supports compatible upgrades. Signing runs only for trusted version tags and must pass Authenticode verification before checksums are created.

Inno Setup was selected over WiX because this utility needs a small file-copy installer, Start Menu integration, optional desktop shortcut, upgrade identity, silent compilation, and uninstall support. A WiX toolchain would add project and package complexity without a current MSI-specific requirement.

## Consequences

The publish directory contains multiple runtime files and is larger than a framework-dependent package, but startup and native dependency behavior stay explicit. Runtime security updates arrive through application releases. Windows 11 x64 is the only claimed target until broader validation exists. The MIT license and final application icon are included in each package. Production distribution awaits signing credentials; Phase 5.1 handles clean-machine validation.
