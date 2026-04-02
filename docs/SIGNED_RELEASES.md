# Signed Release Setup

This guide configures signed installer releases for PrintEase.

## 1) Prerequisites

- A code signing certificate in PFX format
- Password for the PFX
- Access to your GitHub repository settings

## 2) Prepare base64 certificate content

Run in PowerShell (replace path):

```powershell
$certPath = "C:\path\to\codesign.pfx"
[Convert]::ToBase64String([IO.File]::ReadAllBytes($certPath)) | Set-Content .\cert-base64.txt
```

Open `cert-base64.txt` and copy the full value.

## 3) Add GitHub repository secrets

In your repository:

- Settings -> Secrets and variables -> Actions -> New repository secret

Create:

- `SIGN_CERT_BASE64`: paste the full base64 certificate value
- `SIGN_CERT_PASSWORD`: your PFX password

## 4) Trigger signed release

- Commit and push your current branch.
- Create and push a version tag, for example:

```powershell
git tag v1.0.3
git push origin v1.0.3
```

The workflow file `.github/workflows/release.yml` will:

1. Build app and installer
2. Decode certificate secret to temporary PFX
3. Sign installer with signtool
4. Publish release assets to GitHub Releases

## 5) Validate signature on downloaded installer

After release, download the installer and run:

```powershell
Get-AuthenticodeSignature .\PrintEase-Setup.exe | Format-List *
```

Status should report `Valid`.

## 6) If signing is skipped

Signing is skipped when secrets are missing or empty. Ensure both secrets are present and re-run via a new tag.
