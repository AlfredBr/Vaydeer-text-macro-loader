# Repository Guidelines

## Project Structure & Module Organization

This workspace currently contains no application code, tests, assets, or build configuration. Establish the structure when adding the first implementation, and update this guide to match it. Keep application code, automated tests, and static assets in clearly named directories such as `src/`, `tests/`, and `assets/` when appropriate. Group related functionality together and avoid introducing empty directories solely to follow a template.

## Build, Test, and Development Commands

No build, test, or local development commands are configured yet. When introducing a language or framework, add its dependency manifest and document the exact commands required to install dependencies, run locally, build, and test. Prefer repository scripts over commands that depend on undocumented machine settings. Verify commands from the repository root, `E:\vaydeer`.

## Coding Style & Naming Conventions

Follow the conventions of the selected language and keep formatting consistent within each file. Use descriptive names for files, functions, and variables. Once tooling is chosen, commit the formatter and linter configuration and document how to run them. Keep changes focused; avoid unrelated formatting edits.

## Testing Guidelines

No testing framework or coverage threshold is currently defined. Add automated tests alongside new behavior, including relevant edge cases and failure paths. Use descriptive test names that identify the behavior being checked. Document test locations and execution commands when the first test suite is added.

## Commit & Pull Request Guidelines

No Git history is available to establish existing commit conventions. Use concise, imperative commit subjects, such as `Add initial application scaffold`. Keep each commit focused on one logical change. Pull requests should describe the purpose, summarize changes, and state validation performed. Link relevant issues and include screenshots for visible interface changes. Explicitly identify any checks that could not be run.

## Security & Configuration

Do not commit credentials, tokens, or machine-specific secrets. Provide placeholder configuration examples and document required environment variables when configuration is introduced.
