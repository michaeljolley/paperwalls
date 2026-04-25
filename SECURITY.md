# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest release | ✅ |
| Older releases | ❌ |

Only the latest release receives security updates.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use [GitHub's private vulnerability reporting](https://github.com/michaeljolley/paperwalls/security/advisories/new) to submit a report.

Please include:

- A description of the vulnerability
- Steps to reproduce the issue
- Any potential impact
- Suggested fix (if you have one)

## Response Timeline

- **Acknowledgment** — within 48 hours of your report
- **Assessment** — within 1 week, we'll confirm whether it's a valid
  vulnerability and share our plan
- **Fix** — we aim to release a patch as quickly as possible, depending on
  severity and complexity

## Scope

This project is a Windows desktop application that automatically rotates
desktop wallpaper using images from a public GitHub repository. It makes
unauthenticated requests to the GitHub API and uses Win32 P/Invoke for
wallpaper management. The application does not collect or store user
credentials or personal data. However, we take all security reports seriously,
including:

- Data exfiltration risks
- Code injection vulnerabilities
- Dependency vulnerabilities
- Win32 P/Invoke safety issues
- Privacy concerns beyond what's covered in our
  [Privacy Policy](PRIVACY_POLICY.md)

Thank you for helping keep this project safe.
