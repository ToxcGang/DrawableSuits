# Security Policy

## Supported Versions

Security fixes are provided for the latest release of DrawableSuits and the current development branch.

| Version | Supported |
|---------|-----------|
| latest  | Yes       |
| older   | No        |

## Reporting a Vulnerability

If you believe you have found a security issue in DrawableSuits, please report it privately to the maintainer instead of opening a public issue.

Private contact method:
- joeurcino@proton.me

Please include:
- a clear description of the issue
- steps to reproduce it
- your game version
- your mod loader / framework version
- the DrawableSuits version
- any relevant screenshots, logs, or sample code

Please avoid sharing exploit details publicly until the issue has been reviewed and addressed.

## Scope

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits, place decals, save and load designs, and apply edited textures to vanilla or modded suits.

The mod stores local saves, textures, and decal images under the BepInEx config directory. Multiplayer sync is intended for applied or saved designs, not constant brush-stroke streaming.

## Security Principles

DrawableSuits follows these principles:
- minimal data storage
- least-privilege behavior where practical
- no remote code execution
- no intentional data collection
- no transmission of user data to external services unless required for the mod’s local multiplayer sync behavior

## What to Report

Please report issues such as:
- unauthorized data access
- unexpected network activity
- privilege escalation
- code execution vulnerabilities
- injection issues
- malformed texture or decal handling that could expose data or memory safety issues
- persistence or storage issues that could leak local files

## Out of Scope

The following are generally not considered security vulnerabilities:
- feature requests
- cosmetic bugs
- drawing, decal, or brush behavior issues
- suit compatibility issues caused by UV differences or modded content
- normal crashes that do not expose data or enable abuse
- visual differences between local and remote clients

## Disclosure Policy

Confirmed vulnerabilities will be addressed in a future release and disclosed after a reasonable remediation period.
