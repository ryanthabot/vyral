"""
VyralBot Context Database — Population Helpers
================================================
Functions to insert data into the context database.
Used by the agent to populate the DB row by row.
Run sections of this script to insert data in batches.
"""

import sqlite3
import json
import hashlib
from pathlib import Path
from datetime import datetime

DB_PATH = Path(__file__).parent / "vyralbot_context.db"


def get_conn():
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


# ============================================================
# INSERT HELPERS
# ============================================================


def insert_feature(name, display_name, category, description, sidebar_location=None, status="complete", dependencies=None, notes=None):
    conn = get_conn()
    conn.execute(
        """INSERT OR REPLACE INTO features (name, display_name, category, description, sidebar_location, status, dependencies, notes)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
        (name, display_name, category, description, sidebar_location, status,
         json.dumps(dependencies) if dependencies else None, notes),
    )
    conn.commit()
    fid = conn.execute("SELECT id FROM features WHERE name = ?", (name,)).fetchone()[0]
    conn.close()
    return fid


def insert_file(path, file_type, layer, summary, feature_id=None, content=None):
    size = len(content.encode("utf-8")) if content else 0
    content_hash = hashlib.sha256(content.encode("utf-8")).hexdigest() if content else None
    conn = get_conn()
    conn.execute(
        """INSERT OR REPLACE INTO files (path, type, feature_id, layer, content, content_hash, size_bytes, last_modified, summary)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (path, file_type, feature_id, layer, content, content_hash, size, datetime.now().isoformat(), summary),
    )
    conn.commit()
    fid = conn.execute("SELECT id FROM files WHERE path = ?", (path,)).fetchone()[0]
    conn.close()
    return fid


def insert_code_entity(file_id, feature_id, kind, name, namespace=None, parent_entity_id=None, signature=None, start_line=None, end_line=None, access_modifier=None, body=None, summary=None, is_entry_point=False):
    conn = get_conn()
    conn.execute(
        """INSERT INTO code_entities (file_id, feature_id, kind, name, namespace, parent_entity_id, signature, start_line, end_line, access_modifier, body, summary, is_entry_point)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (file_id, feature_id, kind, name, namespace, parent_entity_id, signature, start_line, end_line, access_modifier, body, summary, 1 if is_entry_point else 0),
    )
    conn.commit()
    eid = conn.execute("SELECT last_insert_rowid()").fetchone()[0]
    conn.close()
    return eid


def insert_db_table(name, feature_id, create_sql, description, row_example=None):
    conn = get_conn()
    conn.execute(
        """INSERT OR REPLACE INTO db_tables (name, feature_id, create_sql, description, row_example)
           VALUES (?, ?, ?, ?, ?)""",
        (name, feature_id, create_sql, description, row_example),
    )
    conn.commit()
    tid = conn.execute("SELECT id FROM db_tables WHERE name = ?", (name,)).fetchone()[0]
    conn.close()
    return tid


def insert_db_column(table_id, name, col_type, nullable=False, default_value=None, is_primary_key=False, is_foreign_key=False, fk_target_table=None, description=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO db_columns (table_id, name, type, nullable, default_value, is_primary_key, is_foreign_key, fk_target_table, description)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (table_id, name, col_type, 1 if nullable else 0, default_value, 1 if is_primary_key else 0, 1 if is_foreign_key else 0, fk_target_table, description),
    )
    conn.commit()
    conn.close()


def insert_data_operation(table_id, feature_id, file_id, operation, method_name, sql_template, line_number=None, description=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO data_operations (table_id, feature_id, file_id, operation, method_name, sql_template, line_number, description)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
        (table_id, feature_id, file_id, operation, method_name, sql_template, line_number, description),
    )
    conn.commit()
    conn.close()


def insert_event(name, source_platform, feature_id=None, event_class_file_id=None, properties=None, description=None):
    conn = get_conn()
    conn.execute(
        """INSERT OR REPLACE INTO events (name, source_platform, feature_id, event_class_file_id, properties, description)
           VALUES (?, ?, ?, ?, ?, ?)""",
        (name, source_platform, feature_id, event_class_file_id, json.dumps(properties) if properties else None, description),
    )
    conn.commit()
    eid = conn.execute("SELECT id FROM events WHERE name = ?", (name,)).fetchone()[0]
    conn.close()
    return eid


def insert_event_flow(event_id, publisher_entity_id=None, subscriber_entity_id=None, publisher_file_id=None, subscriber_file_id=None, description=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO event_flows (event_id, publisher_entity_id, subscriber_entity_id, publisher_file_id, subscriber_file_id, description)
           VALUES (?, ?, ?, ?, ?, ?)""",
        (event_id, publisher_entity_id, subscriber_entity_id, publisher_file_id, subscriber_file_id, description),
    )
    conn.commit()
    conn.close()


def insert_di_registration(interface_name, implementation_name, lifetime, feature_id=None, registration_file_id=None, registration_line=None, notes=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO di_registrations (interface_name, implementation_name, lifetime, feature_id, registration_file_id, registration_line, notes)
           VALUES (?, ?, ?, ?, ?, ?, ?)""",
        (interface_name, implementation_name, lifetime, feature_id, registration_file_id, registration_line, notes),
    )
    conn.commit()
    conn.close()


def insert_architecture_rule(category, rule_text, severity="warning", applies_to_features=None, example_good=None, example_bad=None, source_doc=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO architecture_rules (category, rule_text, severity, applies_to_features, example_good, example_bad, source_doc)
           VALUES (?, ?, ?, ?, ?, ?, ?)""",
        (category, rule_text, severity, json.dumps(applies_to_features) if applies_to_features else '["all"]', example_good, example_bad, source_doc),
    )
    conn.commit()
    conn.close()


def insert_navigation_item(sidebar_section, menu_item_label, feature_id=None, view_file_id=None, viewmodel_file_id=None, route_command=None, parent_menu_item_id=None):
    conn = get_conn()
    conn.execute(
        """INSERT INTO navigation_map (sidebar_section, menu_item_label, feature_id, view_file_id, viewmodel_file_id, route_command, parent_menu_item_id)
           VALUES (?, ?, ?, ?, ?, ?, ?)""",
        (sidebar_section, menu_item_label, feature_id, view_file_id, viewmodel_file_id, route_command, parent_menu_item_id),
    )
    conn.commit()
    nid = conn.execute("SELECT last_insert_rowid()").fetchone()[0]
    conn.close()
    return nid


def insert_project_metadata(key, value):
    conn = get_conn()
    conn.execute(
        "INSERT OR REPLACE INTO project_metadata (key, value) VALUES (?, ?)",
        (key, value),
    )
    conn.commit()
    conn.close()


def rebuild_fts_index():
    """Rebuild the full-text search index from all tables."""
    conn = get_conn()
    # Drop and recreate (contentless FTS5 tables don't support DELETE)
    conn.execute("DROP TABLE IF EXISTS search_index")
    conn.execute("""
        CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
            source_type,
            source_id UNINDEXED,
            searchable_text,
            content='',
            tokenize='porter unicode61'
        )
    """)

    # Index features
    for r in conn.execute("SELECT id, name, display_name, description, notes FROM features").fetchall():
        text = f"{r['name']} {r['display_name']} {r['description']} {r['notes'] or ''}"
        conn.execute("INSERT INTO search_index (source_type, source_id, searchable_text) VALUES (?, ?, ?)",
                     ("feature", r["id"], text))

    # Index files
    for r in conn.execute("SELECT id, path, summary, content FROM files").fetchall():
        text = f"{r['path']} {r['summary'] or ''} {r['content'] or ''}"
        conn.execute("INSERT INTO search_index (source_type, source_id, searchable_text) VALUES (?, ?, ?)",
                     ("file", r["id"], text))

    # Index code entities
    for r in conn.execute("SELECT id, name, signature, summary, body FROM code_entities").fetchall():
        text = f"{r['name']} {r['signature'] or ''} {r['summary'] or ''} {r['body'] or ''}"
        conn.execute("INSERT INTO search_index (source_type, source_id, searchable_text) VALUES (?, ?, ?)",
                     ("entity", r["id"], text))

    # Index events
    for r in conn.execute("SELECT id, name, description, properties FROM events").fetchall():
        text = f"{r['name']} {r['description'] or ''} {r['properties'] or ''}"
        conn.execute("INSERT INTO search_index (source_type, source_id, searchable_text) VALUES (?, ?, ?)",
                     ("event", r["id"], text))

    # Index architecture rules
    for r in conn.execute("SELECT id, rule_text, example_good, example_bad FROM architecture_rules").fetchall():
        text = f"{r['rule_text']} {r['example_good'] or ''} {r['example_bad'] or ''}"
        conn.execute("INSERT INTO search_index (source_type, source_id, searchable_text) VALUES (?, ?, ?)",
                     ("rule", r["id"], text))

    conn.commit()
    count = conn.execute("SELECT COUNT(*) FROM search_index").fetchone()[0]
    print(f"FTS index rebuilt with {count} entries.")
    conn.close()


if __name__ == "__main__":
    print("Population helpers loaded. Import and use the insert_* functions.")
