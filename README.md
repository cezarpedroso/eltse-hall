# Eltse Hall residence-life tools

Eltse Hall is a responsive residence-life application for night-duty scheduling, resident room management, dorm checks, photographs, and PDF reporting. It uses an ASP.NET Core API, SQL Server, and a React/TypeScript interface designed for phones first.

## What is implemented

- A private, application-owned login system restricted to provisioned `@wmpenn.edu` accounts.
- Password hashing through ASP.NET Core Identity's versioned password hasher; plaintext passwords are never stored.
- Optional 30-day persistent HttpOnly sessions, CSRF protection, per-IP login throttling, and a 15-minute account lockout after five failed passwords.
- No public registration. A one-time production bootstrap creates the first administrator; Hall Directors and administrators provision everyone else.
- Server-generated temporary passwords that must be replaced at first sign-in.
- `ResidentAssistant`, `HallDirector`, and `Admin` authorization enforced by the API.
- A phone-friendly monthly night-duty calendar showing every assignee while allowing RAs to change only their own assignments.
- Dorm checks for 25 suites and four rooms per suite, resident names, photos, reset confirmation, and PDF export.
- Resident creation, editing, transfer, removal, and Excel roster analysis/import for Eltse Hall.
- Administrative scheduling, account management, password reset, activity history, and PDF reports.
- SQL persistence, optimistic concurrency, audit logs, validation, and automated backend/frontend tests.

## Security model

An email ending in `@wmpenn.edu` is necessary but is not sufficient by itself. The database must already contain a provisioned login account and an active Eltse Hall membership. There is no public sign-up endpoint, so an unknown person cannot gain access merely by entering a university-looking address.

User-chosen passwords must contain 15–128 characters. The app accepts long passphrases and does not impose arbitrary character-composition rules. Obvious Eltse/William Penn passwords are rejected. Login failures use a generic response to reduce account discovery.

The browser receives an encrypted, tamper-resistant authentication cookie with `HttpOnly`, `Secure`, and host-only protections in production. **Keep me signed in** makes it persistent for 30 days; otherwise it expires after eight hours or when the browser session ends. Every state-changing browser request also needs a server-issued antiforgery token. Role changes, deactivation, password resets, and password changes invalidate prior sessions.

For production, the React build and API are served by the same ASP.NET host. Keeping them on one HTTPS origin avoids third-party-cookie failures on mobile browsers.

## Solution layout

```text
RaDuty.slnx
├─ src/
│  ├─ RaDuty.Domain/          entities and scheduling rules
│  ├─ RaDuty.Application/     DTOs, contracts, and application errors
│  ├─ RaDuty.Infrastructure/  EF Core, Identity accounts, services, PDFs, and seed data
│  ├─ RaDuty.Api/             controllers, cookies, CSRF, policies, and middleware
│  └─ raduty-web/             React, TypeScript, Vite, and TanStack Query
├─ tests/RaDuty.Tests/        domain, persistence, authentication, and PDF tests
├─ deployment/                production database bootstrap
└─ docker-compose.yml         local SQL Server 2022
```

## Prerequisites

- .NET SDK 10.0
- Node.js 22 or newer
- Docker Desktop, or another reachable SQL Server

