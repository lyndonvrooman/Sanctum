# Sanctum

A self-hosted, real-time relationship and dynamic management web application built with **.NET 11**, **Vue 3**, **Tailwind CSS**, **SQLite**, and **SignalR**. Designed to run inside a single Docker container behind **Cloudflare Zero Trust** with automatic OAuth pass-through authentication.

---

## Features

- **Safeword Status & Live Alerts:** Persistent global status bar (`GREEN` / `YELLOW` / `RED`) with optional context notes synchronized across both partners in real time via SignalR.
- **Daily Star Rating & Sticker Chart:** Dom-awarded end-of-day rating system (*Nothing*, *Silver Star*, *Gold Star*, *Super Gold Star*) with automatic point adjustments, notes, and a 7-day sticker history strip.
- **Yes / No / Maybe Consent Matrix:**
  - Mirrored checklists across both partners with inline notes/conditions, Top/Giving vs. Bottom/Receiving interest (`X, 1–5, !, ?`) and experience (`0–5, ?`) ratings.
  - 3-way view switcher (**My List**, **Partner**, and **Compare**) highlighting mutual matches, items needing discussion, and pinned Hard Limits.
  - Direct 6-column Google Sheet `.csv` checklist import with upsert support.
- **Tasks, Standing Rules & Approval Queue:**
  - Daily streak tasks and one-time tasks with optional Dom approval workflows.
  - Full CRUD editor for Standing Rules & Protocols (create, edit, pause/enable, delete).
- **Scene & Session Planner:** Plan sessions using only consent-eligible (`Yes` / `Maybe`) activities, track gear & safety checklists, and manage active session states.
- **Rewards Store, Punishment Jar & Point Ledger:**
  - Customizable Rewards Store with redemption and fulfillment tracking.
  - Random-draw Punishment Jar with pool management and active correction resolution.
  - Full chronological Point History Ledger.
- **Shared & Private Journal + Aftercare:**
  - Mood-tagged journal entries with a **Private to me only** toggle and partner response comments.
  - Customizable 1-tap Aftercare quick-request chips.
- **Cloudflare Zero Trust Auth & QR Device Pairing:**
  - Automatic login via Cloudflare Access JWT (`Cf-Access-Jwt-Assertion`) mapped to each partner's OAuth email.
  - Single-use 3-minute QR code pairing generator for future mobile companion access.

---

## Tech Stack

| Layer | Technology |
| :--- | :--- |
| **Backend** | .NET 11 (ASP.NET Core Minimal APIs, EF Core 11, SignalR) |
| **Database** | SQLite (persisted to `/app/data/sanctum.db`) |
| **Frontend** | Vue 3 (Composition API), Vite 6, Tailwind CSS v4 |
| **Auth / Edge** | Cloudflare Zero Trust (Access JWT + Service Tokens) |
| **Deployment** | Multi-stage Docker container (`node:22-alpine` -> `mcr.microsoft.com/dotnet/aspnet:11.0`) |

---

## Repository Structure

    Sanctum/
    ├── Sanctum.Api/              # .NET 11 backend, EF Core models, SignalR hub, Zero Trust auth
    ├── Sanctum.Web/              # Vue 3 + Tailwind SPA (compiled into wwwroot in Docker)
    ├── Dockerfile                # Multi-stage build (Vue SPA + .NET 11 runtime)
    ├── docker-compose.yml        # Production container & volume configuration
    ├── NuGet.config              # Explicit nuget.org package source configuration
    ├── .env.example              # Environment variable template
    └── Sanctum.slnx              # .NET 11 XML solution file

---

## Local Development

### Prerequisites
- [.NET 11 SDK](https://dotnet.microsoft.com/)
- [Node.js 22+](https://nodejs.org/)

### 1. Start the .NET 11 API

    dotnet run --project Sanctum.Api

The API starts on `http://localhost:5000`, creates `Sanctum.Api/data/sanctum.db`, and seeds starter tasks, rewards, rules, aftercare options, and Punishment Jar items. In `Development` mode, authentication automatically uses `Sanctum:DomEmail` and exposes a **Dom / Sub** role switcher in the top header.

### 2. Start the Vue 3 Dev Server
In a second terminal:

    cd Sanctum.Web
    npm install
    npm run dev

Open `http://localhost:5173`. Vite proxies `/api` and `/hubs` requests to `http://localhost:5000`.

---

## Kink Checklist CSV Import Format

Export your checklist spreadsheet as `.csv` (6 columns, `A–F`) and upload it under **Y / N / M -> Import Google Sheet CSV**:

| Col | Field | Values |
| :--- | :--- | :--- |
| **A** | Activity / Category | Activity name (or Category header if columns `B–F` are empty) |
| **B** | Experience (Top / Giving) | `0`–`5`, `?`, or blank |
| **C** | Interest (Top / Giving) | `X` (Hard Limit), `1` (No), `2`/`?` (Maybe), `3`–`5`/`!` (Yes) |
| **D** | Experience (Bottom / Receiving) | `0`–`5`, `?`, or blank |
| **E** | Interest (Bottom / Receiving) | `X` (Hard Limit), `1` (No), `2`/`?` (Maybe), `3`–`5`/`!` (Yes) |
| **F** | Comments | Conditions, soft limits, or context notes |

Imported activities are automatically mirrored to both partners so both lists stay aligned in the **Compare** tab.

---

## Docker & Cloudflare Zero Trust Deployment

1. Copy `.env.example` to `.env` and set your values:

       SANCTUM_PUBLIC_URL=https://sanctum.kinkyfairytales.ca
       SANCTUM_DOM_EMAIL=dom@example.com
       SANCTUM_SUB_EMAIL=sub@example.com
       CF_TEAM_DOMAIN=https://your-team.cloudflareaccess.com
       CF_POLICY_AUD=your-cloudflare-access-aud-tag
       CF_ACCESS_CLIENT_ID=your-service-token-client-id
       CF_ACCESS_CLIENT_SECRET=your-service-token-client-secret

2. Prepare the persistent SQLite directory (container runs as non-root `UID 1654`):

       mkdir -p data
       sudo chown -R 1654:1654 data

3. Build and start the container:

       docker compose up -d --build

4. Route `sanctum.kinkyfairytales.ca` in your Cloudflare Tunnel to `http://<docker-host-ip>:8085` (with **WebSockets** enabled) behind a Cloudflare Access policy allowing both partner emails.