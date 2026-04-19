---
id: 9
type: task
title: 'G4: Source fetch — memctl fetch <url> helper'
status: Todo
priority: low
created: 2026-04-19
updated: 2026-04-19
---

## Description

Bot cần fetch raw source (URL, article) để synthesize vào vault. Claude Code có WebFetch tool nhưng có limitations (redirect handling, JS-rendered pages, auth). `memctl fetch` là CLI helper thuần túy — fetch + convert HTML → markdown → stdout. Bot đọc, synthesize, gọi `create`/`append`. memctl là fetch helper; bot là brain.

## Dependencies

- Standalone command — không depends on G1-G5
- NuGet deps mới: HTML parser + HTML→markdown converter (e.g. `HtmlAgilityPack` + `ReverseMarkdown`, hoặc `AngleSharp`)

## Implementation

New command `memctl fetch <url>`:

**Files to create/modify:**
- NEW: `src/memctl/Operators/FetchOperator.cs`
- MODIFY: `src/memctl/Bootstrap/Program.cs` — register `fetch` subcommand

**Algorithm:**
1. Detect input type: HTTP/HTTPS URL vs local file path
2. For URL: `HttpClient.GetAsync` với reasonable User-Agent, follow redirects
3. For file: `File.ReadAllText`
4. HTML → markdown: strip boilerplate selectors (nav, footer, header, aside, .ad, #cookie-banner, script, style), convert main content
5. Output markdown to stdout
6. Non-markdown files (PDF, plain text): output as-is

## Acceptance Criteria

- `memctl fetch https://example.com/article` → markdown content to stdout, exit 0
- `memctl fetch ./docs/paper.html` → local file support, exit 0
- Strip boilerplate: nav, footer, header, aside, script, style, cookie banners, ads
- Preserve: headings, paragraphs, code blocks, tables, lists, inline code
- Redirect follow: tự follow HTTP 301/302 (tối đa 5 hops)
- User-Agent header hợp lý (không bị block bởi cloudflare basic checks)
- Timeout 10s, exit 1 với error JSON `{"success":false,"action":"fetch","message":"timeout"}` nếu fail
- HTTP 4xx/5xx: exit 1 với error JSON kèm status code
- Không cần vault — command hoàn toàn độc lập
- `--raw` flag: output raw HTML thay vì converted markdown
