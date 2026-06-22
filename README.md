# Osprey Relay for M365

**Osprey Relay for M365** is a lightweight Windows SMTP relay that accepts email from any device on your network — printers, copiers, line-of-business apps, monitoring systems — and delivers it through **Microsoft 365 via the Graph API**, without requiring legacy SMTP AUTH or per-device licences.

> Part of the **Osprey Relay** product family.

---

## Why Osprey Relay for M365?

Microsoft 365 deprecated basic SMTP AUTH for many tenants. Devices that relied on it (MFPs, legacy apps, monitoring tools) stopped being able to send email. The typical workarounds — direct send, shared mailboxes, third-party relays — all have drawbacks.

Osprey Relay for M365 runs locally as a Windows Service or desktop app, presents a plain SMTP listener to your devices, and uses a registered Azure AD application to deliver mail through Graph — no per-seat licences, no open relay, no cloud subscription.

---

## Features

- **SMTP listener** on a configurable port (default 2525); supports optional SMTP AUTH
- **Microsoft 365 delivery** via Microsoft Graph `sendMail` API
- **Routing rules** — route by sender address, recipient address, or wildcard suffix domain
- **OneDrive / SharePoint file storage** — save attachments directly to a drive path with rich `%variable%` filename templates
- **Suffix domain routing** — catch all mail for `*.yourdomain.com` subdomains and route accordingly
- **Smarthost failover** — if Graph is temporarily unreachable (503/504), failover to a configured SMTP smarthost (e.g. Barracuda, Mimecast) so nothing is lost
- **Windows Service mode** — runs unattended via the Windows Service Control Manager
- **Setup Wizard** — guided Azure AD app registration with admin consent flow, or manual credential entry
- **Relay Settings** — port, max message size, bind address, fallback sender, SMTP auth, and smarthost all in one place
- **Test Send** — built-in tool to fire a test message and verify routing without needing a mail client

---

## Requirements

| Requirement | Detail |
|---|---|
| OS | Windows 10 / 11 or Windows Server 2019+ |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) |
| Microsoft 365 | Any plan that includes Exchange Online |
| Azure AD | Permission to register an app and grant `Mail.Send` (application permission) |

---

## Getting Started

### 1. Build / install

Clone the repo and publish a self-contained build:

```powershell
.\publish.ps1
```

The output lands in `publish\win-x64\`. Run `Relay365.exe` directly, or install as a Windows Service.

### 2. Configure — App Registration

On first run, click **Configure App**. The wizard will either:

- **Walk you through** signing in as a Global Admin and automatically creating an Azure AD app registration with the correct `Mail.Send` permission, or
- Accept **manual credentials** (Tenant ID, Client ID, Client Secret) if you've already registered the app yourself.

The required Graph permission is:

```
Mail.Send   (Application — not Delegated)
```

### 3. Configure — Relay Settings

Click **Settings** to set:

- SMTP listener port and optional bind address
- Maximum message size
- Fallback sender address (used when the original envelope-from has no M365 mailbox)
- Optional SMTP AUTH (username / password) for devices that must authenticate
- Smarthost failover host and credentials

### 4. Add routing rules

Use **Sender Routes** and **File Rules** to define what happens to each message. If no rule matches, the message is delivered using the configured fallback sender.

---

## Architecture

```
Device / App
    │  SMTP (port 2525)
    ▼
Osprey Relay for M365
    ├── SmtpRelayServer   — accepts SMTP connections
    ├── RoutingEngine     — evaluates sender / suffix / recipient rules
    ├── GraphMailSender   — delivers via Microsoft Graph sendMail
    ├── GraphFileStorer   — saves to OneDrive / SharePoint
    └── SmtpSmarthostSender — failover delivery via external SMTP
```

---

## Osprey Relay Product Family

| Product | Target | Status |
|---|---|---|
| **Osprey Relay for M365** | Microsoft 365 / Exchange Online | This repo |
| **Osprey Relay for Workspace** | Google Workspace / Gmail | Planned |
| **Osprey Relay Edge** | SMB / FTP to Cloud storage | Planned |

---

## Licence

Proprietary — all rights reserved. This software is not open-source.
