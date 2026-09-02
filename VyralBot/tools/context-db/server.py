"""
VyralBot Context Database MCP Server
=====================================
A comprehensive MCP server that provides AI agents with deep project knowledge.
Query features, files, code entities, event flows, DB schema, DI registrations,
navigation structure, and architecture rules — all from a single SQLite database.
"""

import sqlite3
import json
import os
from pathlib import Path
from mcp.server.fastmcp import FastMCP

# Database path (next to this script)
DB_DIR = Path(__file__).parent
DB_PATH = DB_DIR / "vyralbot_context.db"

mcp = FastMCP(
    "vyralbot-context",
    instructions="""You have access to the VyralBot project context database.
This database contains comprehensive information about every feature, file, code entity,
database table, event flow, DI registration, navigation item, and architecture rule in the project.

START HERE:
- Use list_features() to see all features
- Use get_feature_context(name) to get EVERYTHING about a feature
- Use search(query) for full-text search across the entire project
- Use get_navigation_map() to understand the UI sidebar structure

When modifying or creating features, ALWAYS query the context DB first to understand
the existing patterns, file locations, and architecture rules.""",
)


def get_db() -> sqlite3.Connection:
    """Get a database connection with row factory."""
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


# ============================================================
# FEATURE TOOLS
# ============================================================


@mcp.tool()
def list_features(category: str | None = None) -> str:
    """List all features in the project, optionally filtered by category.
    Categories: platform, chat, stream-tools, audio-alerts, monitoring, settings, utility, core
    """
    conn = get_db()
    try:
        if category:
            rows = conn.execute(
                "SELECT id, name, display_name, category, status, description FROM features WHERE category = ? ORDER BY display_name",
                (category,),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT id, name, display_name, category, status, description FROM features ORDER BY category, display_name"
            ).fetchall()
        if not rows:
            return "No features found."
        result = []
        for r in rows:
            result.append(
                f"[{r['category']}] {r['display_name']} ({r['name']}) — {r['status']}\n  {r['description']}"
            )
        return "\n\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def get_feature_context(feature_name: str) -> str:
    """Get COMPLETE context for a feature — all files, code entities, DB tables,
    events, DI registrations, nav items, and applicable architecture rules.
    This is the primary tool for understanding a feature before making changes.
    """
    conn = get_db()
    try:
        # Get feature
        feat = conn.execute(
            "SELECT * FROM features WHERE name = ? OR display_name LIKE ?",
            (feature_name, f"%{feature_name}%"),
        ).fetchone()
        if not feat:
            return f"Feature '{feature_name}' not found. Use list_features() to see available features."

        fid = feat["id"]
        sections = []

        # Feature header
        sections.append(
            f"# Feature: {feat['display_name']}\n"
            f"Category: {feat['category']} | Status: {feat['status']}\n"
            f"Sidebar: {feat['sidebar_location'] or 'N/A'}\n"
            f"Description: {feat['description']}\n"
            f"Notes: {feat['notes'] or 'None'}"
        )

        # Dependencies
        deps = feat["dependencies"]
        if deps:
            sections.append(f"## Dependencies\n{deps}")

        # Files by layer
        files = conn.execute(
            "SELECT path, layer, summary FROM files WHERE feature_id = ? ORDER BY layer, path",
            (fid,),
        ).fetchall()
        if files:
            layers = {}
            for f in files:
                layers.setdefault(f["layer"], []).append(f)
            file_lines = ["## Files"]
            for layer, layer_files in layers.items():
                file_lines.append(f"\n### {layer.upper()}")
                for f in layer_files:
                    file_lines.append(f"  - {f['path']}\n    {f['summary']}")
            sections.append("\n".join(file_lines))

        # Code entities
        entities = conn.execute(
            """SELECT ce.kind, ce.name, ce.namespace, ce.signature, ce.summary, f.path
               FROM code_entities ce
               LEFT JOIN files f ON ce.file_id = f.id
               WHERE ce.feature_id = ?
               ORDER BY ce.kind, ce.name""",
            (fid,),
        ).fetchall()
        if entities:
            ent_lines = ["## Code Entities"]
            for e in entities:
                ent_lines.append(
                    f"  [{e['kind']}] {e['name']} — {e['path'] or 'unknown'}\n"
                    f"    {e['signature']}\n"
                    f"    {e['summary']}"
                )
            sections.append("\n".join(ent_lines))

        # DB tables
        tables = conn.execute(
            "SELECT name, description, create_sql FROM db_tables WHERE feature_id = ?",
            (fid,),
        ).fetchall()
        if tables:
            tbl_lines = ["## Database Tables"]
            for t in tables:
                cols = conn.execute(
                    "SELECT name, type, nullable, is_primary_key, is_foreign_key, fk_target_table, description FROM db_columns WHERE table_id = (SELECT id FROM db_tables WHERE name = ?)",
                    (t["name"],),
                ).fetchall()
                tbl_lines.append(f"\n### {t['name']}\n{t['description']}")
                for c in cols:
                    pk = " [PK]" if c["is_primary_key"] else ""
                    fk = f" [FK → {c['fk_target_table']}]" if c["is_foreign_key"] else ""
                    null = " NULL" if c["nullable"] else " NOT NULL"
                    tbl_lines.append(
                        f"  - {c['name']} {c['type']}{null}{pk}{fk} — {c['description'] or ''}"
                    )
            sections.append("\n".join(tbl_lines))

        # Data operations
        ops = conn.execute(
            """SELECT do2.operation, do2.method_name, do2.sql_template, do2.description, f.path
               FROM data_operations do2
               LEFT JOIN files f ON do2.file_id = f.id
               WHERE do2.feature_id = ?
               ORDER BY do2.operation""",
            (fid,),
        ).fetchall()
        if ops:
            op_lines = ["## Data Operations (SQL)"]
            for o in ops:
                op_lines.append(
                    f"  [{o['operation']}] {o['method_name']}() — {o['path'] or ''}\n"
                    f"    SQL: {o['sql_template']}\n"
                    f"    {o['description'] or ''}"
                )
            sections.append("\n".join(op_lines))

        # Events
        events = conn.execute(
            "SELECT name, source_platform, description, properties FROM events WHERE feature_id = ?",
            (fid,),
        ).fetchall()
        if events:
            ev_lines = ["## Events"]
            for ev in events:
                ev_lines.append(
                    f"  {ev['name']} (from {ev['source_platform']})\n"
                    f"    {ev['description']}\n"
                    f"    Properties: {ev['properties'] or 'N/A'}"
                )
                # Event flows for this event
                flows = conn.execute(
                    """SELECT ef.description, pub.name as pub_name, sub.name as sub_name,
                              pf.path as pub_path, sf.path as sub_path
                       FROM event_flows ef
                       LEFT JOIN code_entities pub ON ef.publisher_entity_id = pub.id
                       LEFT JOIN code_entities sub ON ef.subscriber_entity_id = sub.id
                       LEFT JOIN files pf ON ef.publisher_file_id = pf.id
                       LEFT JOIN files sf ON ef.subscriber_file_id = sf.id
                       WHERE ef.event_id = (SELECT id FROM events WHERE name = ?)""",
                    (ev["name"],),
                ).fetchall()
                for fl in flows:
                    ev_lines.append(
                        f"    Flow: {fl['pub_name']} → {fl['sub_name']}\n"
                        f"      {fl['pub_path']} → {fl['sub_path']}\n"
                        f"      {fl['description'] or ''}"
                    )
            sections.append("\n".join(ev_lines))

        # DI registrations
        di = conn.execute(
            "SELECT interface_name, implementation_name, lifetime, notes FROM di_registrations WHERE feature_id = ?",
            (fid,),
        ).fetchall()
        if di:
            di_lines = ["## DI Registrations"]
            for d in di:
                di_lines.append(
                    f"  {d['interface_name']} → {d['implementation_name']} [{d['lifetime']}]"
                    + (f"\n    {d['notes']}" if d["notes"] else "")
                )
            sections.append("\n".join(di_lines))

        # Navigation
        nav = conn.execute(
            """SELECT nm.sidebar_section, nm.menu_item_label, nm.route_command,
                      vf.path as view_path, vmf.path as viewmodel_path
               FROM navigation_map nm
               LEFT JOIN files vf ON nm.view_file_id = vf.id
               LEFT JOIN files vmf ON nm.viewmodel_file_id = vmf.id
               WHERE nm.feature_id = ?""",
            (fid,),
        ).fetchall()
        if nav:
            nav_lines = ["## Navigation"]
            for n in nav:
                nav_lines.append(
                    f"  [{n['sidebar_section']}] {n['menu_item_label']}\n"
                    f"    Command: {n['route_command']}\n"
                    f"    View: {n['view_path']}\n"
                    f"    ViewModel: {n['viewmodel_path']}"
                )
            sections.append("\n".join(nav_lines))

        # Architecture rules
        rules = conn.execute(
            """SELECT rule_text, category, severity, example_good, example_bad
               FROM architecture_rules
               WHERE applies_to_features LIKE ? OR applies_to_features LIKE '%"all"%'""",
            (f'%"{feature_name}"%',),
        ).fetchall()
        if rules:
            rule_lines = ["## Applicable Architecture Rules"]
            for r in rules:
                rule_lines.append(
                    f"  [{r['severity']}] ({r['category']}) {r['rule_text']}"
                )
                if r["example_good"]:
                    rule_lines.append(f"    ✓ Good: {r['example_good']}")
                if r["example_bad"]:
                    rule_lines.append(f"    ✗ Bad: {r['example_bad']}")
            sections.append("\n".join(rule_lines))

        return "\n\n".join(sections)
    finally:
        conn.close()


# ============================================================
# FILE TOOLS
# ============================================================


@mcp.tool()
def get_file_info(file_path: str) -> str:
    """Get metadata and summary for a specific file. Use partial path matching."""
    conn = get_db()
    try:
        row = conn.execute(
            "SELECT f.*, feat.name as feature_name, feat.display_name as feature_display FROM files f LEFT JOIN features feat ON f.feature_id = feat.id WHERE f.path LIKE ?",
            (f"%{file_path}%",),
        ).fetchone()
        if not row:
            return f"File not found matching '{file_path}'."
        return (
            f"Path: {row['path']}\n"
            f"Type: {row['type']} | Layer: {row['layer']}\n"
            f"Feature: {row['feature_display'] or 'shared/global'}\n"
            f"Summary: {row['summary']}\n"
            f"Size: {row['size_bytes']} bytes"
        )
    finally:
        conn.close()


@mcp.tool()
def get_file_content(file_path: str) -> str:
    """Get the full stored content of a file from the context DB."""
    conn = get_db()
    try:
        row = conn.execute(
            "SELECT path, content FROM files WHERE path LIKE ?",
            (f"%{file_path}%",),
        ).fetchone()
        if not row:
            return f"File not found matching '{file_path}'."
        if not row["content"]:
            return f"File {row['path']} has no stored content (may be binary or too large)."
        return f"=== {row['path']} ===\n{row['content']}"
    finally:
        conn.close()


@mcp.tool()
def get_files_for_feature(feature_name: str, layer: str | None = None) -> str:
    """Get all files belonging to a feature, optionally filtered by architectural layer.
    Layers: view, viewmodel, service, model, entity, data, controller, config, doc, test, web-asset
    """
    conn = get_db()
    try:
        feat = conn.execute(
            "SELECT id FROM features WHERE name = ? OR display_name LIKE ?",
            (feature_name, f"%{feature_name}%"),
        ).fetchone()
        if not feat:
            return f"Feature '{feature_name}' not found."
        if layer:
            rows = conn.execute(
                "SELECT path, layer, summary FROM files WHERE feature_id = ? AND layer = ? ORDER BY path",
                (feat["id"], layer),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT path, layer, summary FROM files WHERE feature_id = ? ORDER BY layer, path",
                (feat["id"],),
            ).fetchall()
        if not rows:
            return f"No files found for feature '{feature_name}'" + (
                f" with layer '{layer}'" if layer else ""
            )
        result = []
        current_layer = None
        for r in rows:
            if r["layer"] != current_layer:
                current_layer = r["layer"]
                result.append(f"\n--- {current_layer.upper()} ---")
            result.append(f"  {r['path']}\n    {r['summary']}")
        return "\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def list_files(layer: str | None = None, file_type: str | None = None) -> str:
    """List all files in the project, optionally filtered by layer or type.
    Layers: view, viewmodel, service, model, entity, data, controller, config, doc, test, web-asset
    Types: cs, xaml, md, json, csproj, html, props, ps1, bat, xml
    """
    conn = get_db()
    try:
        query = "SELECT path, layer, type, summary FROM files WHERE 1=1"
        params = []
        if layer:
            query += " AND layer = ?"
            params.append(layer)
        if file_type:
            query += " AND type = ?"
            params.append(file_type)
        query += " ORDER BY layer, path"
        rows = conn.execute(query, params).fetchall()
        if not rows:
            return "No files found."
        result = []
        for r in rows:
            result.append(f"[{r['layer']}] {r['path']} — {r['summary']}")
        return "\n".join(result)
    finally:
        conn.close()


# ============================================================
# CODE ENTITY TOOLS
# ============================================================


@mcp.tool()
def get_code_entity(entity_name: str) -> str:
    """Get details for a specific class, interface, method, or other code entity.
    Returns signature, summary, location, and full body if available.
    """
    conn = get_db()
    try:
        rows = conn.execute(
            """SELECT ce.*, f.path as file_path, feat.display_name as feature_name
               FROM code_entities ce
               LEFT JOIN files f ON ce.file_id = f.id
               LEFT JOIN features feat ON ce.feature_id = feat.id
               WHERE ce.name LIKE ?
               ORDER BY ce.kind""",
            (f"%{entity_name}%",),
        ).fetchall()
        if not rows:
            return f"No code entity found matching '{entity_name}'."
        result = []
        for r in rows:
            entry = (
                f"[{r['kind']}] {r['name']}\n"
                f"  Namespace: {r['namespace'] or 'N/A'}\n"
                f"  File: {r['file_path']} (lines {r['start_line']}-{r['end_line']})\n"
                f"  Feature: {r['feature_name'] or 'shared'}\n"
                f"  Access: {r['access_modifier'] or 'N/A'}\n"
                f"  Signature: {r['signature']}\n"
                f"  Summary: {r['summary']}"
            )
            if r["body"]:
                entry += f"\n\n  --- Body ---\n{r['body']}"
            result.append(entry)
        return "\n\n===\n\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def get_code_entities_for_feature(
    feature_name: str, kind: str | None = None
) -> str:
    """Get all code entities for a feature, optionally filtered by kind.
    Kinds: class, interface, record, enum, method, property, struct
    """
    conn = get_db()
    try:
        feat = conn.execute(
            "SELECT id FROM features WHERE name = ? OR display_name LIKE ?",
            (feature_name, f"%{feature_name}%"),
        ).fetchone()
        if not feat:
            return f"Feature '{feature_name}' not found."
        query = """SELECT ce.kind, ce.name, ce.signature, ce.summary, f.path
                   FROM code_entities ce
                   LEFT JOIN files f ON ce.file_id = f.id
                   WHERE ce.feature_id = ?"""
        params = [feat["id"]]
        if kind:
            query += " AND ce.kind = ?"
            params.append(kind)
        query += " ORDER BY ce.kind, ce.name"
        rows = conn.execute(query, params).fetchall()
        if not rows:
            return f"No code entities found."
        result = []
        for r in rows:
            result.append(
                f"[{r['kind']}] {r['name']} — {r['path']}\n"
                f"  {r['signature']}\n"
                f"  {r['summary']}"
            )
        return "\n\n".join(result)
    finally:
        conn.close()


# ============================================================
# DATABASE SCHEMA TOOLS
# ============================================================


@mcp.tool()
def get_db_schema(table_name: str | None = None) -> str:
    """Get the VyralBot application database schema — tables, columns, types, relationships.
    Optionally filter to a specific table name.
    """
    conn = get_db()
    try:
        if table_name:
            tables = conn.execute(
                "SELECT * FROM db_tables WHERE name LIKE ?",
                (f"%{table_name}%",),
            ).fetchall()
        else:
            tables = conn.execute(
                "SELECT * FROM db_tables ORDER BY name"
            ).fetchall()
        if not tables:
            return "No tables found."
        result = []
        for t in tables:
            cols = conn.execute(
                "SELECT * FROM db_columns WHERE table_id = ? ORDER BY id",
                (t["id"],),
            ).fetchall()
            col_lines = []
            for c in cols:
                pk = " [PK]" if c["is_primary_key"] else ""
                fk = f" [FK → {c['fk_target_table']}]" if c["is_foreign_key"] else ""
                null = " NULL" if c["nullable"] else " NOT NULL"
                dflt = f" DEFAULT {c['default_value']}" if c["default_value"] else ""
                col_lines.append(
                    f"    {c['name']} {c['type']}{null}{dflt}{pk}{fk}"
                    + (f" — {c['description']}" if c["description"] else "")
                )
            ops = conn.execute(
                "SELECT operation, method_name, sql_template FROM data_operations WHERE table_id = ?",
                (t["id"],),
            ).fetchall()
            op_lines = []
            for o in ops:
                op_lines.append(f"    [{o['operation']}] {o['method_name']}()")
            entry = f"TABLE: {t['name']}\n  {t['description']}\n\n  Columns:\n" + "\n".join(col_lines)
            if t["create_sql"]:
                entry += f"\n\n  CREATE SQL:\n    {t['create_sql']}"
            if op_lines:
                entry += "\n\n  Operations:\n" + "\n".join(op_lines)
            result.append(entry)
        return "\n\n" + "=" * 60 + "\n\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def get_data_operations(feature_name: str | None = None) -> str:
    """Get all SQL data operations (queries), optionally filtered by feature."""
    conn = get_db()
    try:
        if feature_name:
            feat = conn.execute(
                "SELECT id FROM features WHERE name = ? OR display_name LIKE ?",
                (feature_name, f"%{feature_name}%"),
            ).fetchone()
            if not feat:
                return f"Feature '{feature_name}' not found."
            rows = conn.execute(
                """SELECT do2.*, f.path, t.name as table_name
                   FROM data_operations do2
                   LEFT JOIN files f ON do2.file_id = f.id
                   LEFT JOIN db_tables t ON do2.table_id = t.id
                   WHERE do2.feature_id = ?
                   ORDER BY do2.operation""",
                (feat["id"],),
            ).fetchall()
        else:
            rows = conn.execute(
                """SELECT do2.*, f.path, t.name as table_name
                   FROM data_operations do2
                   LEFT JOIN files f ON do2.file_id = f.id
                   LEFT JOIN db_tables t ON do2.table_id = t.id
                   ORDER BY t.name, do2.operation"""
            ).fetchall()
        if not rows:
            return "No data operations found."
        result = []
        for r in rows:
            result.append(
                f"[{r['operation']}] {r['method_name']}() — Table: {r['table_name'] or 'N/A'}\n"
                f"  File: {r['path'] or 'N/A'}\n"
                f"  SQL: {r['sql_template']}\n"
                f"  {r['description'] or ''}"
            )
        return "\n\n".join(result)
    finally:
        conn.close()


# ============================================================
# EVENT TOOLS
# ============================================================


@mcp.tool()
def get_event_chain(event_name: str) -> str:
    """Get the complete event flow — who publishes an event, who subscribes, and what happens.
    Shows the full chain from platform adapter through EventBus to handlers.
    """
    conn = get_db()
    try:
        ev = conn.execute(
            "SELECT * FROM events WHERE name LIKE ?",
            (f"%{event_name}%",),
        ).fetchone()
        if not ev:
            return f"No event found matching '{event_name}'. Use list_events() to see all events."
        result = [
            f"Event: {ev['name']}\n"
            f"Platform: {ev['source_platform']}\n"
            f"Description: {ev['description']}\n"
            f"Properties: {ev['properties'] or 'N/A'}"
        ]
        flows = conn.execute(
            """SELECT ef.*, pub.name as pub_name, pub.kind as pub_kind,
                      sub.name as sub_name, sub.kind as sub_kind,
                      pf.path as pub_path, sf.path as sub_path
               FROM event_flows ef
               LEFT JOIN code_entities pub ON ef.publisher_entity_id = pub.id
               LEFT JOIN code_entities sub ON ef.subscriber_entity_id = sub.id
               LEFT JOIN files pf ON ef.publisher_file_id = pf.id
               LEFT JOIN files sf ON ef.subscriber_file_id = sf.id
               WHERE ef.event_id = ?""",
            (ev["id"],),
        ).fetchall()
        if flows:
            result.append("\nEvent Flow:")
            for f in flows:
                result.append(
                    f"  {f['pub_name']} ({f['pub_kind']}) → {f['sub_name']} ({f['sub_kind']})\n"
                    f"    Publisher: {f['pub_path']}\n"
                    f"    Subscriber: {f['sub_path']}\n"
                    f"    {f['description'] or ''}"
                )
        else:
            result.append("\nNo event flows recorded for this event.")
        return "\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def list_events(platform: str | None = None) -> str:
    """List all events in the system, optionally filtered by platform.
    Platforms: Twitch, Kick, YouTube, Core, Manual
    """
    conn = get_db()
    try:
        if platform:
            rows = conn.execute(
                "SELECT name, source_platform, description FROM events WHERE source_platform LIKE ? ORDER BY name",
                (f"%{platform}%",),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT name, source_platform, description FROM events ORDER BY source_platform, name"
            ).fetchall()
        if not rows:
            return "No events found."
        result = []
        for r in rows:
            result.append(
                f"[{r['source_platform']}] {r['name']}\n  {r['description']}"
            )
        return "\n\n".join(result)
    finally:
        conn.close()


# ============================================================
# DI & ARCHITECTURE TOOLS
# ============================================================


@mcp.tool()
def get_di_registrations(feature_name: str | None = None) -> str:
    """Get dependency injection registrations, optionally filtered by feature.
    Shows interface → implementation mappings and lifetimes.
    """
    conn = get_db()
    try:
        if feature_name:
            feat = conn.execute(
                "SELECT id FROM features WHERE name = ? OR display_name LIKE ?",
                (feature_name, f"%{feature_name}%"),
            ).fetchone()
            if not feat:
                return f"Feature '{feature_name}' not found."
            rows = conn.execute(
                "SELECT * FROM di_registrations WHERE feature_id = ? ORDER BY interface_name",
                (feat["id"],),
            ).fetchall()
        else:
            rows = conn.execute(
                """SELECT dr.*, feat.display_name as feature_name
                   FROM di_registrations dr
                   LEFT JOIN features feat ON dr.feature_id = feat.id
                   ORDER BY dr.lifetime, dr.interface_name"""
            ).fetchall()
        if not rows:
            return "No DI registrations found."
        result = []
        for r in rows:
            feat_label = r.get("feature_name", "") or ""
            result.append(
                f"[{r['lifetime']}] {r['interface_name']} → {r['implementation_name']}"
                + (f" ({feat_label})" if feat_label else "")
                + (f"\n  {r['notes']}" if r["notes"] else "")
            )
        return "\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def get_navigation_map(section: str | None = None) -> str:
    """Get the UI sidebar navigation structure — shows every menu item,
    which view/viewmodel it routes to, and what command triggers it.
    Sections: Home, Platforms, Chat Features, Stream Tools, Audio & Alerts, Monitoring, Settings, Utilities
    """
    conn = get_db()
    try:
        if section:
            rows = conn.execute(
                """SELECT nm.*, vf.path as view_path, vmf.path as vm_path, feat.display_name as feature_name
                   FROM navigation_map nm
                   LEFT JOIN files vf ON nm.view_file_id = vf.id
                   LEFT JOIN files vmf ON nm.viewmodel_file_id = vmf.id
                   LEFT JOIN features feat ON nm.feature_id = feat.id
                   WHERE nm.sidebar_section LIKE ?
                   ORDER BY nm.sidebar_section, nm.menu_item_label""",
                (f"%{section}%",),
            ).fetchall()
        else:
            rows = conn.execute(
                """SELECT nm.*, vf.path as view_path, vmf.path as vm_path, feat.display_name as feature_name
                   FROM navigation_map nm
                   LEFT JOIN files vf ON nm.view_file_id = vf.id
                   LEFT JOIN files vmf ON nm.viewmodel_file_id = vmf.id
                   LEFT JOIN features feat ON nm.feature_id = feat.id
                   ORDER BY nm.sidebar_section, nm.menu_item_label"""
            ).fetchall()
        if not rows:
            return "No navigation items found."
        result = []
        current_section = None
        for r in rows:
            if r["sidebar_section"] != current_section:
                current_section = r["sidebar_section"]
                result.append(f"\n=== {current_section} ===")
            result.append(
                f"  {r['menu_item_label']}\n"
                f"    Command: {r['route_command']}\n"
                f"    View: {r['view_path']}\n"
                f"    ViewModel: {r['vm_path']}\n"
                f"    Feature: {r['feature_name'] or 'N/A'}"
            )
        return "\n".join(result)
    finally:
        conn.close()


@mcp.tool()
def get_architecture_rules(category: str | None = None) -> str:
    """Get architecture rules and constraints that must be followed.
    Categories: performance, security, style, forbidden, required, testing
    """
    conn = get_db()
    try:
        if category:
            rows = conn.execute(
                "SELECT * FROM architecture_rules WHERE category = ? ORDER BY severity DESC",
                (category,),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT * FROM architecture_rules ORDER BY severity DESC, category"
            ).fetchall()
        if not rows:
            return "No architecture rules found."
        result = []
        for r in rows:
            entry = f"[{r['severity']}] ({r['category']}) {r['rule_text']}"
            if r["example_good"]:
                entry += f"\n  ✓ Good: {r['example_good']}"
            if r["example_bad"]:
                entry += f"\n  ✗ Bad: {r['example_bad']}"
            if r["source_doc"]:
                entry += f"\n  Source: {r['source_doc']}"
            if r["applies_to_features"]:
                entry += f"\n  Applies to: {r['applies_to_features']}"
            result.append(entry)
        return "\n\n".join(result)
    finally:
        conn.close()


# ============================================================
# SEARCH TOOLS
# ============================================================


@mcp.tool()
def search(query: str) -> str:
    """Full-text search across the entire project — files, code entities, features,
    docs, events, rules. Returns the most relevant matches.
    """
    conn = get_db()
    try:
        results = []

        # Search FTS index
        try:
            fts_rows = conn.execute(
                """SELECT source_type, source_id, snippet(search_index, 2, '>>>', '<<<', '...', 64) as snippet
                   FROM search_index
                   WHERE searchable_text MATCH ?
                   ORDER BY rank
                   LIMIT 30""",
                (query,),
            ).fetchall()
            for r in fts_rows:
                results.append(
                    f"[{r['source_type']}] (id:{r['source_id']}) ...{r['snippet']}..."
                )
        except Exception:
            # FTS might not be populated yet, fall back to LIKE search
            pass

        # Also do LIKE search on key tables if FTS returned few results
        if len(results) < 10:
            like = f"%{query}%"
            # Features
            feat_rows = conn.execute(
                "SELECT name, display_name, description FROM features WHERE name LIKE ? OR display_name LIKE ? OR description LIKE ?",
                (like, like, like),
            ).fetchall()
            for r in feat_rows:
                results.append(
                    f"[feature] {r['display_name']} — {r['description']}"
                )

            # Files
            file_rows = conn.execute(
                "SELECT path, summary FROM files WHERE path LIKE ? OR summary LIKE ? LIMIT 15",
                (like, like),
            ).fetchall()
            for r in file_rows:
                results.append(f"[file] {r['path']} — {r['summary']}")

            # Code entities
            ent_rows = conn.execute(
                "SELECT kind, name, summary FROM code_entities WHERE name LIKE ? OR summary LIKE ? LIMIT 15",
                (like, like),
            ).fetchall()
            for r in ent_rows:
                results.append(f"[{r['kind']}] {r['name']} — {r['summary']}")

            # Events
            ev_rows = conn.execute(
                "SELECT name, source_platform, description FROM events WHERE name LIKE ? OR description LIKE ? LIMIT 10",
                (like, like),
            ).fetchall()
            for r in ev_rows:
                results.append(f"[event] {r['name']} ({r['source_platform']}) — {r['description']}")

        if not results:
            return f"No results found for '{query}'."

        # Deduplicate
        seen = set()
        unique = []
        for r in results:
            if r not in seen:
                seen.add(r)
                unique.append(r)

        return f"Found {len(unique)} results for '{query}':\n\n" + "\n\n".join(
            unique[:30]
        )
    finally:
        conn.close()


@mcp.tool()
def run_query(sql: str) -> str:
    """Run a read-only SQL query against the context database for advanced queries.
    Only SELECT statements are allowed.
    """
    if not sql.strip().upper().startswith("SELECT"):
        return "Only SELECT queries are allowed. Use this for read-only access."
    conn = get_db()
    try:
        rows = conn.execute(sql).fetchall()
        if not rows:
            return "Query returned no results."
        # Convert to list of dicts
        result = []
        for r in rows:
            result.append(dict(r))
        return json.dumps(result, indent=2, default=str)
    except Exception as e:
        return f"Query error: {e}"
    finally:
        conn.close()


# ============================================================
# PROJECT OVERVIEW TOOL
# ============================================================


@mcp.tool()
def get_project_overview() -> str:
    """Get a high-level overview of the entire VyralBot project — tech stack,
    architecture, feature count, file count, and key stats.
    """
    conn = get_db()
    try:
        stats = {}
        stats["features"] = conn.execute(
            "SELECT COUNT(*) FROM features"
        ).fetchone()[0]
        stats["files"] = conn.execute(
            "SELECT COUNT(*) FROM files"
        ).fetchone()[0]
        stats["code_entities"] = conn.execute(
            "SELECT COUNT(*) FROM code_entities"
        ).fetchone()[0]
        stats["db_tables"] = conn.execute(
            "SELECT COUNT(*) FROM db_tables"
        ).fetchone()[0]
        stats["events"] = conn.execute(
            "SELECT COUNT(*) FROM events"
        ).fetchone()[0]
        stats["di_registrations"] = conn.execute(
            "SELECT COUNT(*) FROM di_registrations"
        ).fetchone()[0]
        stats["architecture_rules"] = conn.execute(
            "SELECT COUNT(*) FROM architecture_rules"
        ).fetchone()[0]
        stats["nav_items"] = conn.execute(
            "SELECT COUNT(*) FROM navigation_map"
        ).fetchone()[0]

        # Feature categories
        cats = conn.execute(
            "SELECT category, COUNT(*) as cnt FROM features GROUP BY category ORDER BY cnt DESC"
        ).fetchall()
        cat_lines = [f"  {c['category']}: {c['cnt']}" for c in cats]

        # File type breakdown
        types = conn.execute(
            "SELECT type, COUNT(*) as cnt FROM files GROUP BY type ORDER BY cnt DESC"
        ).fetchall()
        type_lines = [f"  .{t['type']}: {t['cnt']}" for t in types]

        return (
            "# VyralBot Project Overview\n\n"
            "## Tech Stack\n"
            "  Framework: .NET 10.0 (net10.0-windows)\n"
            "  UI: WPF + CommunityToolkit.Mvvm (MVVM)\n"
            "  Database: SQLite + Dapper\n"
            "  Logging: Serilog\n"
            "  DI: Microsoft.Extensions.DependencyInjection\n"
            "  Platforms: Twitch, Kick, YouTube, TikTok, Discord\n\n"
            f"## Stats\n"
            f"  Features: {stats['features']}\n"
            f"  Files: {stats['files']}\n"
            f"  Code Entities: {stats['code_entities']}\n"
            f"  DB Tables: {stats['db_tables']}\n"
            f"  Events: {stats['events']}\n"
            f"  DI Registrations: {stats['di_registrations']}\n"
            f"  Architecture Rules: {stats['architecture_rules']}\n"
            f"  Navigation Items: {stats['nav_items']}\n\n"
            f"## Feature Categories\n" + "\n".join(cat_lines) + "\n\n"
            f"## File Types\n" + "\n".join(type_lines)
        )
    finally:
        conn.close()


# ============================================================
# ENTRY POINT
# ============================================================

if __name__ == "__main__":
    mcp.run()
