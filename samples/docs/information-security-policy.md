# Information Security Policy

Document ID: SEC-POL-001 · Classification: Internal · Owner: CISO Office (security@acme.example)

## Passwords and Authentication

- Passwords must be at least **14 characters** long and must not be reused across systems.
- Multi-factor authentication (MFA) is **mandatory** for email, VPN, GitHub, AWS and the HR portal.
  Hardware security keys (YubiKey) are required for administrators and production access.
- Passwords must be stored in the company password manager (1Password). Sharing passwords over chat or
  email is forbidden.
- Accounts are locked for 15 minutes after 5 failed sign-in attempts.

## Devices

- All laptops must use full-disk encryption (FileVault or BitLocker) and be enrolled in device management (Intune).
- Screens must lock automatically after 5 minutes of inactivity.
- Personal devices may access email and chat only through the managed mobile apps; they may never store
  customer data.

## Data Classification

| Level | Examples | Handling |
|---|---|---|
| Public | Marketing site, published docs | No restrictions |
| Internal | Handbook, roadmaps, wiki | Employees and contractors only |
| Confidential | Customer data, contracts, financials | Need-to-know, encrypted at rest and in transit |
| Restricted | Production secrets, payment data, health data | Named individuals only, audited access |

Confidential and Restricted data must never be pasted into public AI tools. Use the approved internal
assistant (DocuMind) which keeps data inside Acme's infrastructure.

## Incident Reporting

Report suspected security incidents (phishing, lost devices, leaked credentials) **within 1 hour** to
security@acme.example or the #security-incidents Slack channel. Lost or stolen laptops must also be reported
to IT so the device can be remotely wiped. Do not attempt to investigate an incident yourself.

## Access Reviews

Managers review their team's system access every quarter. Access for leavers is revoked on their last
working day by IT, triggered automatically from the HR system.
