using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Markdig;

namespace VyralBot.UI.Services.Docs;

/// <summary>
/// One navigable page loaded from AppDocs/appdocs.json.
/// </summary>
public record DocPageInfo(string Path, string Title);

/// <summary>
/// One navigation section loaded from AppDocs/appdocs.json.
/// </summary>
public record DocSectionInfo(string Title, IReadOnlyList<DocPageInfo> Pages);

/// <summary>
/// Loads and renders the in-app documentation (AppDocs folder).
///
/// The same markdown files are the source of truth on GitHub and in the app:
/// - Doc pages are markdown files under AppDocs rendered to HTML (Markdig).
/// - Source-code links are ordinary relative links out of AppDocs into the repo
///   (e.g. ../../src/...); in-app they open in the built-in read-only code viewer,
///   on GitHub they are normal file links.
/// </summary>
public sealed class DocumentationService
{
    private const string DocsFolderName = "AppDocs";
    private const string ManifestFileName = "appdocs.json";
    private const int MaxCodeRenderBytes = 1_500_000; // NavigateToString has a ~2 MB limit

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
    };

    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions() // GFM tables, task lists, auto heading identifiers
        .Build();

    public string DocsRoot { get; }
    public string AppRoot { get; }

    private DocumentationService(string docsRoot)
    {
        DocsRoot = docsRoot;
        AppRoot = Path.GetFullPath(Path.Combine(docsRoot, ".."));
    }

    /// <summary>
    /// Resolves the AppDocs folder: walk up from the exe (dev layout puts AppDocs at
    /// the repo root next to src/), then fall back to an AppDocs folder next to the exe
    /// (published layout). Returns null when no docs folder exists.
    /// </summary>
    public static DocumentationService? Create()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = dir; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, DocsFolderName);
            if (Directory.Exists(candidate))
                return new DocumentationService(candidate);
        }

        var exeRelative = Path.Combine(AppContext.BaseDirectory, DocsFolderName);
        return Directory.Exists(exeRelative) ? new DocumentationService(exeRelative) : null;
    }

    public IReadOnlyList<DocSectionInfo> LoadManifest()
    {
        var manifestPath = Path.Combine(DocsRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
            return new[] { new DocSectionInfo("Documentation", new[] { new DocPageInfo("index.md", "Overview") }) };

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var sections = new List<DocSectionInfo>();

        foreach (var section in document.RootElement.GetProperty("sections").EnumerateArray())
        {
            var title = section.GetProperty("title").GetString() ?? "Section";
            var pages = new List<DocPageInfo>();
            foreach (var page in section.GetProperty("pages").EnumerateArray())
            {
                var path = page.GetProperty("path").GetString();
                var pageTitle = page.GetProperty("title").GetString();
                if (!string.IsNullOrWhiteSpace(path) && pageTitle is not null)
                    pages.Add(new DocPageInfo(path.Replace('\\', '/'), pageTitle));
            }
            if (pages.Count > 0)
                sections.Add(new DocSectionInfo(title, pages));
        }

        return sections;
    }

    public bool DocExists(string virtualPath)
    {
        var fullPath = ResolveWithinRoot(DocsRoot, virtualPath);
        return fullPath is not null && File.Exists(fullPath)
            && Path.GetExtension(fullPath).Equals(".md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders a markdown doc page (virtual path relative to AppDocs) into a complete
    /// HTML document for the in-app browser.
    /// </summary>
    public string RenderPage(string virtualPath)
    {
        var fullPath = ResolveWithinRoot(DocsRoot, virtualPath)
            ?? throw new FileNotFoundException($"Doc page not found: {virtualPath}");

        var markdown = File.ReadAllText(fullPath);

        // The template renders its own styled H1 (plus file path), so drop the markdown's
        // first H1 line to avoid showing the title twice.
        var title = RemoveFirstH1(markdown, out var titleText);
        var content = Markdown.ToHtml(title, _markdownPipeline);
        var pageTitle = WebUtility.HtmlEncode(titleText ?? Path.GetFileName(virtualPath));
        var pagePath = WebUtility.HtmlEncode("AppDocs/" + virtualPath.Replace('\\', '/'));

        return SharedHead
            .Replace("@TITLE@", pageTitle)
            .Replace("@PATH@", pagePath)
            .Replace("@CONTENT@", content);
    }

    /// <summary>Returns the markdown without its first "# " heading line; out param gets the heading text.</summary>
    private static string RemoveFirstH1(string markdown, out string? titleText)
    {
        var lines = markdown.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').TrimStart();
            if (trimmed.StartsWith("# "))
            {
                titleText = trimmed[2..].Trim();
                lines[i] = string.Empty; // keep line count so any anchor links stay reasonable
                return string.Join("\n", lines).TrimStart('\n', '\r');
            }
        }
        titleText = null;
        return markdown;
    }

    /// <summary>
    /// Renders a repository file into a read-only code view with line numbers.
    /// </summary>
    public string RenderCode(string repoRelativePath)
    {
        var fullPath = ResolveWithinRoot(AppRoot, repoRelativePath)
            ?? throw new FileNotFoundException($"File not found: {repoRelativePath}");

        var bytes = File.ReadAllBytes(fullPath);
        var truncated = bytes.Length > MaxCodeRenderBytes;
        var text = truncated
            ? Encoding.UTF8.GetString(bytes, 0, MaxCodeRenderBytes)
            : Encoding.UTF8.GetString(bytes);

        // Drop a possibly broken partial last line from the truncation cut.
        if (truncated)
        {
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline >= 0)
                text = text[..lastNewline];
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var rows = new StringBuilder(Math.Min(lines.Length, 20_000) * 48);
        for (var i = 0; i < lines.Length; i++)
            rows.Append("<tr><td class=\"ln\">").Append(i + 1)
                .Append("</td><td class=\"lc\">").Append(WebUtility.HtmlEncode(lines[i]))
                .AppendLine("</td></tr>");

        var notice = truncated
            ? "<div class=\"notice\">File truncated in this view — the source file is larger than the viewer limit.</div>"
            : "";

        return SharedHead
            .Replace("@TITLE@", WebUtility.HtmlEncode(Path.GetFileName(fullPath)))
            .Replace("@PATH@", WebUtility.HtmlEncode(repoRelativePath.Replace('\\', '/')))
            .Replace("@CONTENT@",
                "<div class=\"codeheader\"><div class=\"path\">Read-only source view &middot; use the back button to return to the docs</div>" +
                notice + "</div><table class=\"code\">" + rows + "</table>");
    }

    /// <summary>
    /// Where a clicked href should go. Doc and Code targets are returned as repo-relative
    /// virtual paths (forward slashes) so the caller can route them. Doc targets keep a
    /// heading fragment (e.g. "commands.md#usage" → Value="commands.md", Fragment="usage").
    /// </summary>
    public LinkTarget ResolveHref(string currentVirtualPath, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return new LinkTarget(LinkKind.Invalid, href);

        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return new LinkTarget(LinkKind.External, href);

        var fragmentIndex = href.IndexOf('#');
        var pathHref = fragmentIndex >= 0 ? href[..fragmentIndex] : href;
        var fragment = fragmentIndex >= 0 ? href[(fragmentIndex + 1)..] : null;

        if (pathHref.Length == 0)
            return new LinkTarget(LinkKind.Anchor, fragment ?? href);

        // Resolve the href relative to the folder of the current page (virtual, forward slashes).
        var currentDir = Path.GetDirectoryName(currentVirtualPath.Replace('/', '\\')) ?? string.Empty;
        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(DocsRoot, currentDir, Uri.UnescapeDataString(pathHref)));
        }
        catch (Exception)
        {
            return new LinkTarget(LinkKind.Invalid, href);
        }

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

        return new LinkTarget(LinkKind.Invalid, href);
    }

    private static string? ResolveWithinRoot(string root, string virtualPath)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, virtualPath.Replace('/', '\\')));
            return IsUnderRoot(full, root) ? full : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalized = Path.GetFullPath(path);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalized.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    public enum LinkKind
    {
        Doc,
        Code,
        External,
        Anchor,
        Invalid
    }

    public sealed record LinkTarget(LinkKind Kind, string Value, string? Fragment = null);

    // NOTE: templates use @TOKEN@ placeholders (not string.Format) because the CSS
    // is full of literal braces.
    private const string SharedHead = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
            :root {
                --bg: #17181c; --panel: #1d1f24; --border: #2c2f36;
                --text: #e2e4e9; --muted: #9aa0ab; --accent: #e05252; --accent-dim: #b9484c;
                --code-bg: #131418; --quote: #24262c;
            }
            html { background: var(--bg); }
            body {
                margin: 0; padding: 28px 36px 60px; background: var(--bg); color: var(--text);
                font-family: 'Segoe UI', -apple-system, sans-serif; font-size: 14.5px; line-height: 1.65;
            }
            article { max-width: 880px; margin: 0 auto; }
            h1, h2, h3, h4 { color: #f3f4f7; line-height: 1.3; }
            h1 { font-size: 28px; border-bottom: 1px solid var(--border); padding-bottom: 10px; margin-top: 8px; }
            h2 { font-size: 21px; margin-top: 36px; border-bottom: 1px solid var(--border); padding-bottom: 6px; }
            h3 { font-size: 17px; margin-top: 26px; }
            a { color: var(--accent); text-decoration: none; }
            a:hover { text-decoration: underline; color: #ff6b6b; }
            code {
                font-family: 'Cascadia Code', 'Consolas', monospace; font-size: 12.8px;
                background: var(--code-bg); border: 1px solid var(--border); border-radius: 4px;
                padding: 1px 5px; color: #e8a0a0; word-break: break-word;
            }
            pre {
                background: var(--code-bg); border: 1px solid var(--border); border-radius: 8px;
                padding: 14px 16px; overflow-x: auto; line-height: 1.5;
            }
            pre code { background: none; border: none; padding: 0; color: #d6d9df; font-size: 12.5px; }
            blockquote {
                margin: 14px 0; padding: 8px 16px; background: var(--quote);
                border-left: 3px solid var(--accent-dim); border-radius: 0 6px 6px 0; color: var(--muted);
            }
            blockquote p { margin: 6px 0; }
            table { border-collapse: collapse; width: 100%; margin: 16px 0; font-size: 13.5px; display: block; overflow-x: auto; }
            th, td { border: 1px solid var(--border); padding: 7px 12px; text-align: left; vertical-align: top; }
            th { background: var(--panel); color: #f3f4f7; font-weight: 600; }
            tr:nth-child(even) td { background: #1a1c21; }
            hr { border: none; border-top: 1px solid var(--border); margin: 28px 0; }
            ul, ol { padding-left: 26px; }
            li { margin: 4px 0; }
            input[type="checkbox"] { accent-color: var(--accent); }
            .path {
                font-family: Consolas, monospace; font-size: 11.5px; color: var(--muted);
                margin-bottom: 4px; user-select: text;
            }
            footer {
                margin-top: 44px; padding-top: 14px; border-top: 1px solid var(--border);
                color: var(--muted); font-size: 11.5px;
            }
            .notice {
                background: #3a2f1b; border: 1px solid #6b5a2e; color: #e8d9a0;
                border-radius: 6px; padding: 8px 14px; margin: 0 0 14px; font-size: 12.5px;
            }
            table.code { border: 1px solid var(--border); border-radius: 8px; }
            table.code td { border: none; padding: 0; }
            table.code tr:nth-child(even) td { background: transparent; }
            td.ln {
                text-align: right; padding: 0 12px 0 16px; color: #565b66; user-select: none;
                font-family: Consolas, monospace; font-size: 12px; line-height: 1.5;
                border-right: 1px solid var(--border); background: #191b1f; white-space: pre;
            }
            td.lc {
                padding: 0 16px; font-family: 'Cascadia Code', Consolas, monospace;
                font-size: 12.5px; white-space: pre; line-height: 1.5; color: #d6d9df;
            }
            .codeheader { margin-bottom: 10px; }
            .codeheader .path { margin-bottom: 2px; }
        </style>
        </head>
        <body>
        <article>
        <div class="path">@PATH@</div>
        <h1>@TITLE@</h1>
        @CONTENT@
        </article>
        <footer>Rendered from the live markdown file &mdash; the same file you see on GitHub. Links to source files open the built-in code viewer.</footer>
        <script>
        document.addEventListener('click', function (e) {
            var a = e.target.closest && e.target.closest('a');
            if (!a) return;
            var href = a.getAttribute('href') || '';
            if (href === '' || href.charAt(0) === '#') return; // empty / same-page anchor
            e.preventDefault();
            e.stopPropagation();
            window.chrome.webview.postMessage(href);
        });
        </script>
        </body>
        </html>
        """;
}
