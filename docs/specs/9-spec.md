# Task #9 — memctl fetch <url>: Spec

## Functional Requirements

| # | FR | Test |
|---|-----|------|
| FR-01 | `memctl fetch <url>` accepts http/https URL, fetches HTML, outputs markdown to stdout | [e2e] |
| FR-02 | `memctl fetch <path>` accepts local file path (relative or absolute) | [e2e] |
| FR-03 | HTML is converted to markdown: h1-h6→headers, p→paragraph, ul/ol/li→lists, pre/code→fenced/inline, strong/b→**bold**, em/i→*italic*, a→links, img→image, table→markdown table, br→newline | [unit] |
| FR-04 | Boilerplate stripped before conversion: `nav, footer, header, aside, script, style, noscript, template` tags; nodes with class containing `cookie, banner, ad, sidebar, popup, modal, newsletter`; nodes with id containing `cookie, banner, sidebar, nav` | [unit] |
| FR-05 | HTTP redirects followed (up to 5 hops) | [unit] |
| FR-06 | User-Agent header set to reasonable browser UA on all HTTP requests | [unit] |
| FR-07 | Timeout 10s — exit 1 with `{"success":false,"action":"fetch","message":"Request timed out after 10 seconds"}` | [unit] |
| FR-08 | HTTP 4xx/5xx — exit 1 with JSON including status code in message | [unit] |
| FR-09 | File not found — exit 1 with JSON `{"success":false,"action":"fetch","message":"File not found: <path>"}` | [unit] |
| FR-10 | `--raw` flag outputs raw HTML instead of markdown | [e2e] |
| FR-11 | No vault dependency — command works without a vault present | [e2e] |
| FR-12 | Non-HTML files (`.md`, `.txt`) output content as-is | [unit] |

## Non-Functional Requirements

| # | NFR |
|---|-----|
| NFR-01 | No new ports or index methods — standalone operator |
| NFR-02 | HttpClient created with 5 max redirects, 10s timeout, browser User-Agent |
| NFR-03 | All threshold values as named constants (RULE #9) |
| NFR-04 | All catch blocks with comments (RULE #10) |

## Edge Cases

- Empty HTML body → empty markdown output
- URL with query string/fragments → handled by Uri.TryCreate
- Non-UTF8 encoding → HttpClient decodes via Content-Type charset
- Very large page → streamed via ReadAsStringAsync, no size limit
- Connection error (non-timeout) → generic error message, exit 1
