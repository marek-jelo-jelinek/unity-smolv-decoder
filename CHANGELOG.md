# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Renamed the package/namespace from `SmolVDecoder` to `Tesearis.SmolVDecoder`.
- Adopted `netstandard2.0;net8.0` multi-targeting, NUnit test tooling, and NuGet packaging/release
  conventions from `Tesearis.HlslParser`.

## [1.0.0] - 2026-08-02

### Added

- Initial implementation: decode-only C# port of SMOL-V for reading SMOL-V-compressed SPIR-V out of
  Unity's Vulkan shader cache.
