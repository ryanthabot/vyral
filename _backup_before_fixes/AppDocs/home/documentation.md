# Documentation

> The built-in documentation browser: every markdown page in the AppDocs folder rendered GitBook-style inside the app — nav tree, search, history, and a read-only source-code viewer — plus the conventions for writing and extending these pages yourself.

## Overview

VyralBot ships its documentation as plain markdown files in the AppDocs folder at the repo root. The Documentation page renders those same files inside the app: a left navigation tree built from a manifest, a search box that filters that tree, back/forward history, and a read-only code viewer for links that point at repository source files. There is no separate doc build, no generated site, and no second copy of the content — the files you read on GitHub are the files the app renders, read straight from disk on every page view.

Two ways in:

- The **Documentation** card ("Explore the official documentation for VyralBot") on the Home page ([Dashboard](dashboard.md)).
- The **Help** button at the bottom of the sidebar ([App Shell & Navigation](app-shell.md)).

Because the browser renders live files, this page doubles as the contributor guide: the conventions every page in this folder follows are codified in [Contributing to These Docs](#contributing-to-these-docs).

## User Guide

### The toolbar

Top to bottom, left to right:

| Control | What it does |
|---|---|
| **← BACK TO HOME** | Returns to the Home page. |
| **Back / Forward arrows** | Step through your viewing history (code views included). Disabled when there is nothing in that direction. |
| **Page title + file path** | The current page's title from the navigation manifest, with its `AppDocs/...` path (or the repo-relative path when a source file is open) underneath in small text. |
| **Search box** | Filters the left navigation tree by page title or path, case-insensitive substring match. Sections with no matching pages hide. |
| **Refresh** | Re-reads `appdocs.json` and the current page's markdown from disk — edit a doc, click Refresh, see the change immediately. |
| **Folder** | Opens the AppDocs folder in Windows Explorer. |

Keyboard shortcuts work anywhere on the page: **Alt+Left**, **Backspace**, and the mouse Back button go back; **Alt+Right** and the mouse Forward button go forward. The buttons' tooltips only mention the Alt combinations.

A status strip along the bottom shows confirmations ("Documentation reloaded.") and errors ("Link target not found: ...").

### The left navigation

The nav panel lists every section and page from the manifest, always fully expanded — there is no collapsing. The page you are on is highlighted in red; opening a source file clears the highlight because a code view is not a doc page. Typing in the search box hides non-matching pages live.

### Following links

Links inside a rendered page behave by kind:

- **Links to other doc pages** (including links into other sections) navigate inside the browser and push onto the history stack.
- **Links with a heading fragment** (`file.md#some-heading`) open the target page and smooth-scroll to that heading.
- **Same-page anchors** (`#some-heading`) scroll the current page natively.
- **Links to source files** (`../../src/...` in the markdown) open the built-in read-only code viewer: a line-numbered rendering of the file with a "use the back button to return to the docs" header. Files larger than about 1.5 MB are truncated in this view with a notice.
- **External links** (`http(s)://`, `mailto:`) open in your system browser, not inside the app.
- **Broken relative links** do not navigate; the status bar shows "Link target not found".

### Refreshing picks up edits

Nothing is cached across renders. If you edit a markdown file (or the manifest) in any editor, clicking **Refresh** reloads the manifest and re-renders the current page from disk — the practical loop while writing or fixing docs.

## How It Works (Developer)

### Docs-root resolution

`DocumentationService.Create()` (`DocumentationService.cs:57-69`) locates the AppDocs folder at startup: it walks up the directory tree from `AppContext.BaseDirectory` looking for a folder named `AppDocs` (the dev layout puts the exe in `src/VyralBot.UI/bin/...`, so the walk-up finds AppDocs at the repo root, next to `src/`). If the walk-up finds nothing it falls back to an AppDocs folder directly next to the exe (the published layout). When neither exists it returns null and the page shows an error — nothing else in the app degrades. `AppRoot` (the parent of DocsRoot) is the boundary for what counts as a "source file" link.

### The manifest

The nav tree comes entirely from [appdocs.json](../appdocs.json), parsed by `LoadManifest()` (`DocumentationService.cs:71-96`):

```json
{
  "title": "VyralBot Documentation",
  "version": 1,
  "sections": [
    {
      "title": "Stream Tools",
      "pages": [
        { "path": "stream-tools/giveaways.md", "title": "Giveaways" }
      ]
    }
  ]
}
```

Paths are relative to AppDocs with forward slashes (backslashes are normalized). Section order and page order in the file are the nav order. A missing manifest degrades to a single "Documentation" section containing `index.md`; a manifest entry pointing at a file that does not exist produces a "Could not open" error when clicked.

### Rendering

`RenderPage()` (`DocumentationService.cs:109-127`) reads the markdown, strips the first `# ` heading (the HTML template renders its own styled H1 plus the file path; the stripped text becomes the page title), converts the rest with Markdig using `UseAdvancedExtensions()` (GFM tables, task lists, auto heading identifiers), and injects title, path, and content into the shared HTML template by `@TOKEN@` replacement — deliberately not `string.Format`, because the template's CSS is full of literal braces. The toolbar title comes from the manifest (`TitleFor` in the ViewModel), not the markdown H1, which is why the two can differ if the manifest is stale.

`RenderCode()` (`DocumentationService.cs:150-186`) renders any repo file as a line-numbered read-only table. Files over `MaxCodeRenderBytes` (1.5 MB — `NavigateToString` has a ~2 MB limit) are truncated at the limit, the partial last line is dropped, and a yellow notice is shown.

### Click interception and link routing

Every rendered page embeds a small script (shown in [Files & Code](#files--code)) that cancels non-anchor link clicks in-page and posts the raw href to the host via `window.chrome.webview.postMessage`. The view's `WebMessageReceived` handler (`DocumentationView.xaml.cs:102-107`) forwards it to `DocumentationViewModel.HandleLink`, which calls `ResolveHref()` (`DocumentationService.cs:193-241`) to classify it:

| Kind | Match | Destination |
|---|---|---|
| `Doc` | Existing `.md` under AppDocs, resolved relative to the current page's folder | In-app page (fragment kept for scroll) |
| `Code` | Any other existing file under the app root | Read-only code viewer |
| `External` | `http://`, `https://`, `mailto:`, and image files | System browser / default handler via `Process.Start` |
| `Anchor` | Fragment-only href (`#heading`) | Handled natively by the browser; never posted |
| `Invalid` | Anything that resolves nowhere | Status-bar error, no navigation |

Cross-file heading links work via `NavigateToPage(path, fragment)`: the fragment is stashed, and after `NavigationCompleted` the view runs `getElementById(fragment).scrollIntoView({behavior:'smooth'})`. A `NavigationStarting` handler (`DocumentationView.xaml.cs:89-100`) is the backstop — any real navigation that escapes the interceptor (e.g. a script redirect) is cancelled and routed through the same pipeline.

### History and the singleton ViewModel

`DocumentationViewModel` is registered as a DI singleton (`App.xaml.cs:221`) and keeps a plain `List<HistoryEntry>` with an index; `PushHistory` truncates forward entries like any browser. `NavigateToDocumentationAsync` (`MainViewModel.cs:1464-1487`) follows the standard navigation dance (wizard guard, null the content, dispose non-essential VMs, scope-resolve), but like every ViewModel the "scope" resolves the same singleton — so the history stack survives navigating away and back. `InitializeAsync` is guarded by `_initialized` (the constructor kicks it off; the view calls it again on load, harmlessly), so re-entering does not reset your position; only **Refresh** reloads.

### WebView2 lifecycle

The view creates its WebView2 on `Loaded` with a shared temp profile (`%TEMP%\VyralBot_WebView2_Temp`, consistent with the app's other WebView2 hosts), renders via `NavigateToString`, and re-applies HTML whenever the VM raises `CurrentHtml` (which always notifies, so back/forward re-navigates). On `Unloaded` the event handlers are detached and the control disposed — teardown exceptions are swallowed because WebView2 can throw if the core is already gone.

## Contributing to These Docs

These rules exist so pages written by different people (or AI agents) at different times read as one document set. Follow them exactly.

### 1. Where pages live

New pages go in `AppDocs/<section>/<kebab-case-name>.md` — lowercase, hyphens, matching the section folders that already exist (for example `stream-tools/browser-sources.md`). Only `index.md` sits loose in the AppDocs root.

### 2. The required page template

Every feature page uses this skeleton, in this order:

```markdown
# Page Title

> One-sentence summary of the feature, as a blockquote.

## Overview

What the feature is and why it exists, for a reader who has never seen it.

## User Guide

Task-oriented walkthrough for a non-coder: buttons, menus, expected behavior.

## How It Works (Developer)

Internals: the classes involved, the pipeline, the quirks. Cite files and lines.

## Files & Code

A | File | Role | table of linked source files, then 1-3 curated snippets.

## Data & Settings

What the feature persists and where (omit when it stores nothing).

## Status & Known Issues

What works, what is dead or broken, linked to the ledger.
```

### 3. Link rules

| You are linking to | Write it as | In-app behavior |
|---|---|---|
| Doc page, same section | `[Title](file.md)` | Opens the page |
| Doc page, other section | `[Title](../section/file.md)` | Opens the page |
| Heading inside another page | `[Title](file.md#heading-slug)` | Opens the page, scrolls to the heading |
| Heading on the same page | `[Title](#heading-slug)` | Native anchor scroll |
| Repository source file | `[File.cs](../../src/.../File.cs)` | Read-only code viewer |
| A line in prose | `` `File.cs:123` `` — code font, **not** a link | Reader follows the file link |
| External site | `[text](https://example.com)` | System browser |

Rules behind the table:

- Source links from a section folder are `../../src/...` (two levels up reaches the repo root). From `index.md` at the AppDocs root they are `../src/...`.
- Relative doc links must resolve to real files — anything else shows "Link target not found" in the status bar.
- Headings get their link ids from Markdig's auto identifiers (lowercase, hyphenated), which match GitHub's slugs: `## Status & Known Issues` → `#status--known-issues`.
- Use plain markdown links only. No HTML `<a>` tags — the pipeline classifies markdown hrefs, and HTML links render inconsistently on GitHub.
- External links are always `https` where the target supports it.

### 4. Register the page in the manifest

An unregistered page is orphaned — reachable only by hand-typed links. Every new page **must** be added to `AppDocs/appdocs.json` under the right section:

```json
{ "path": "stream-tools/new-page.md", "title": "New Page" }
```

Then cross-link it from the pages it belongs with (and from `index.md` when it is a top-level destination). New sections in the manifest appear in the nav automatically. To verify: open the docs browser and click **Refresh** — the manifest and the new page render straight from disk.

### 5. Writing style

- GitHub-flavored markdown only: headings, bullets, tables, fenced code blocks, blockquotes.
- Honest current-state tone. A stubbed, dead, or broken feature is stated plainly where it would be used, then linked to the [known issues ledger](../reference/known-issues.md) — never described as if it worked.
- One page per feature, serving both audiences: the User Guide assumes no coding knowledge, How It Works (Developer) assumes the source is open next to it.
- No emojis in headings.
- Target roughly 150-400 lines per page; a small feature may be shorter.
- Curated, annotated snippets for key logic (with a caption saying what to notice); whole files are read via source links, never pasted.

### 6. New defects go in the ledger

When you document a defect (something dead, broken, or misleading), add an entry to [reference/known-issues.md](../reference/known-issues.md) and link to it from the feature page's Status & Known Issues section. The feature page states the symptom; the ledger holds the detail.

## Files & Code

| File | Role |
|---|---|
| [DocumentationService.cs](../../src/VyralBot.UI/Services/Docs/DocumentationService.cs) | Docs-root resolution, manifest parsing, Markdig rendering, code viewer, link classification, HTML template (371 lines) |
| [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | Nav tree, search filter, history stack, link routing, refresh/open-folder commands (386 lines) |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | Toolbar, status strip, nav tree, WebView2 host (138 lines) |
| [DocumentationView.xaml.cs](../../src/VyralBot.UI/Views/DocumentationView.xaml.cs) | WebView2 creation/teardown, web-message routing, fragment scrolling, keyboard shortcuts (201 lines) |
| [appdocs.json](../appdocs.json) | The manifest that drives the nav tree (opens in the code viewer) |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Help sidebar button, Home-page Documentation card, ViewModel-to-View DataTemplate |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToDocumentationAsync` navigation command |

The click interceptor injected into every rendered page (`DocumentationService.cs:358-366`) — each non-anchor link click is cancelled in-page and posted to the host:

```html
document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a');
    if (!a) return;
    var href = a.getAttribute('href') || '';
    if (href === '' || href.charAt(0) === '#') return; // empty / same-page anchor
    e.preventDefault();
    e.stopPropagation();
    window.chrome.webview.postMessage(href);
});
```

And the classification that decides where a posted href goes (`DocumentationService.cs:222-240`) — markdown inside AppDocs is a page, any other repo file is code:

```csharp
// Doc pages: markdown inside AppDocs.
if (IsUnderRoot(combined, DocsRoot))
{
    var virtualPath = Path.GetRelativePath(DocsRoot, combined).Replace('\\', '/');
    if (Path.GetExtension(combined).Equals(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(combined))
        return new LinkTarget(LinkKind.Doc, virtualPath, fragment);
}

// Any other existing file under the app root opens in the code viewer
// (this is how ../../src/... links work).
if (IsUnderRoot(combined, AppRoot) && File.Exists(combined))
{
    var repoPath = Path.GetRelativePath(AppRoot, combined).Replace('\\', '/');
    if (ImageExtensions.Contains(Path.GetExtension(combined)))
        return new LinkTarget(LinkKind.External, combined);
    return new LinkTarget(LinkKind.Code, repoPath);
}
```

## Status & Known Issues

**Works**: manifest-driven navigation with live search filtering, back/forward history that survives leaving and re-entering the page, doc-to-doc links including cross-file heading fragments, the read-only code viewer with line numbers, external links in the system browser, refresh-after-edit, and the open-folder shortcut.

Notes on the edges:

- Same-page `#anchor` links rely on Markdig's auto heading identifiers producing the slug you typed; GitHub and Markdig agree on ordinary headings but an exotic one can be slugified differently between the two views.
- Code views truncate files at ~1.5 MB (below `NavigateToString`'s ~2 MB ceiling) and drop the partial last line, with a notice.
- A heading fragment whose id does not exist in the target page silently does nothing — no error, no scroll.
- Image links inside AppDocs open in the system image viewer (classified `External`), not in-app.
- If the AppDocs folder is missing entirely, the page shows an error banner and the rest of the app is unaffected.

See the [known issues ledger](../reference/known-issues.md) for cross-app defects.
