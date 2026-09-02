"""
VyralBot Context Database — Schema Initialization
===================================================
Creates all tables, indexes, and FTS5 virtual table.
Run this before populating the database.
"""

import sqlite3
from pathlib import Path

DB_PATH = Path(__file__).parent / "vyralbot_context.db"

SCHEMA = """
-- ============================================================
-- FEATURES — the anchor table. Every query starts here.
-- ============================================================
CREATE TABLE IF NOT EXISTS features (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,          -- kebab-case identifier
    display_name TEXT NOT NULL,         -- human-readable label
    category TEXT NOT NULL,             -- platform|chat|stream-tools|audio-alerts|monitoring|settings|utility|core
    description TEXT NOT NULL,          -- what this feature does (user-facing behavior)
    sidebar_location TEXT,              -- where it appears in sidebar nav
    status TEXT NOT NULL DEFAULT 'complete',  -- complete|partial|planned
    dependencies TEXT,                  -- JSON array of other feature names it depends on
    notes TEXT                          -- gotchas, known issues, planned changes
);

-- ============================================================
-- FILES — every file tagged by feature + architectural layer
-- ============================================================
CREATE TABLE IF NOT EXISTS files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,          -- relative path from project root
    type TEXT NOT NULL,                 -- cs|xaml|md|json|csproj|html|props|ps1|bat|xml|sln
    feature_id INTEGER,                -- FK to features (NULL for shared/global files)
    layer TEXT,                         -- view|viewmodel|service|model|entity|data|controller|config|doc|test|web-asset|build|script
    content TEXT,                       -- full file content
    content_hash TEXT,                  -- SHA256 of content for change detection
    size_bytes INTEGER,
    last_modified TEXT,
    summary TEXT,                       -- 1-2 sentence description
    FOREIGN KEY (feature_id) REFERENCES features(id)
);
CREATE INDEX IF NOT EXISTS idx_files_feature ON files(feature_id);
CREATE INDEX IF NOT EXISTS idx_files_layer ON files(layer);
CREATE INDEX IF NOT EXISTS idx_files_type ON files(type);

-- ============================================================
-- CODE ENTITIES — classes, interfaces, methods, properties
-- ============================================================
CREATE TABLE IF NOT EXISTS code_entities (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id INTEGER,                    -- FK to files
    feature_id INTEGER,                 -- FK to features
    kind TEXT NOT NULL,                 -- class|interface|record|enum|method|property|field|event|struct|delegate
    name TEXT NOT NULL,
    namespace TEXT,
    parent_entity_id INTEGER,           -- FK to self (for methods inside classes)
    signature TEXT,                      -- full declaration line
    start_line INTEGER,
    end_line INTEGER,
    access_modifier TEXT,               -- public|private|internal|protected
    body TEXT,                          -- full source code of this entity
    summary TEXT,                       -- description of what this does
    is_entry_point INTEGER DEFAULT 0,   -- bool: DI registration, startup, handler
    FOREIGN KEY (file_id) REFERENCES files(id),
    FOREIGN KEY (feature_id) REFERENCES features(id),
    FOREIGN KEY (parent_entity_id) REFERENCES code_entities(id)
);
CREATE INDEX IF NOT EXISTS idx_ce_feature ON code_entities(feature_id);
CREATE INDEX IF NOT EXISTS idx_ce_kind ON code_entities(kind);
CREATE INDEX IF NOT EXISTS idx_ce_name ON code_entities(name);
CREATE INDEX IF NOT EXISTS idx_ce_file ON code_entities(file_id);

-- ============================================================
-- DB TABLES — the app's SQLite schema (not this context DB)
-- ============================================================
CREATE TABLE IF NOT EXISTS db_tables (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    feature_id INTEGER,
    create_sql TEXT,                    -- full CREATE TABLE statement
    description TEXT,
    row_example TEXT,                   -- example JSON of a populated row
    FOREIGN KEY (feature_id) REFERENCES features(id)
);

CREATE TABLE IF NOT EXISTS db_columns (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    table_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    type TEXT NOT NULL,
    nullable INTEGER DEFAULT 0,
    default_value TEXT,
    is_primary_key INTEGER DEFAULT 0,
    is_foreign_key INTEGER DEFAULT 0,
    fk_target_table TEXT,
    description TEXT,
    FOREIGN KEY (table_id) REFERENCES db_tables(id)
);
CREATE INDEX IF NOT EXISTS idx_dbc_table ON db_columns(table_id);

CREATE TABLE IF NOT EXISTS data_operations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    table_id INTEGER,                   -- FK to db_tables
    feature_id INTEGER,                 -- FK to features
    file_id INTEGER,                    -- FK to files (which .cs file has this query)
    operation TEXT NOT NULL,             -- SELECT|INSERT|UPDATE|DELETE|CREATE
    method_name TEXT,                   -- C# method that runs this query
    sql_template TEXT,                  -- the actual SQL
    line_number INTEGER,
    description TEXT,
    FOREIGN KEY (table_id) REFERENCES db_tables(id),
    FOREIGN KEY (feature_id) REFERENCES features(id),
    FOREIGN KEY (file_id) REFERENCES files(id)
);
CREATE INDEX IF NOT EXISTS idx_do_feature ON data_operations(feature_id);
CREATE INDEX IF NOT EXISTS idx_do_table ON data_operations(table_id);

-- ============================================================
-- EVENTS — EventBus events and their flow through the system
-- ============================================================
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    source_platform TEXT,               -- Twitch|Kick|YouTube|Core|Manual|OBS
    feature_id INTEGER,
    event_class_file_id INTEGER,        -- FK to files (the .cs file defining the event class)
    properties TEXT,                    -- JSON array of event properties
    description TEXT,
    FOREIGN KEY (feature_id) REFERENCES features(id),
    FOREIGN KEY (event_class_file_id) REFERENCES files(id)
);

CREATE TABLE IF NOT EXISTS event_flows (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id INTEGER NOT NULL,
    publisher_entity_id INTEGER,        -- FK to code_entities (who publishes)
    subscriber_entity_id INTEGER,       -- FK to code_entities (who handles)
    publisher_file_id INTEGER,          -- FK to files
    subscriber_file_id INTEGER,         -- FK to files
    description TEXT,                   -- what happens in this flow
    FOREIGN KEY (event_id) REFERENCES events(id),
    FOREIGN KEY (publisher_entity_id) REFERENCES code_entities(id),
    FOREIGN KEY (subscriber_entity_id) REFERENCES code_entities(id),
    FOREIGN KEY (publisher_file_id) REFERENCES files(id),
    FOREIGN KEY (subscriber_file_id) REFERENCES files(id)
);
CREATE INDEX IF NOT EXISTS idx_ef_event ON event_flows(event_id);

-- ============================================================
-- DI REGISTRATIONS — how services are wired
-- ============================================================
CREATE TABLE IF NOT EXISTS di_registrations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    interface_name TEXT,
    implementation_name TEXT NOT NULL,
    lifetime TEXT NOT NULL,             -- singleton|scoped|transient
    feature_id INTEGER,
    registration_file_id INTEGER,       -- FK to files
    registration_line INTEGER,
    notes TEXT,
    FOREIGN KEY (feature_id) REFERENCES features(id),
    FOREIGN KEY (registration_file_id) REFERENCES files(id)
);
CREATE INDEX IF NOT EXISTS idx_di_feature ON di_registrations(feature_id);

-- ============================================================
-- ARCHITECTURE RULES — constraints from CONTEXT_MASTER etc.
-- ============================================================
CREATE TABLE IF NOT EXISTS architecture_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    category TEXT NOT NULL,             -- performance|security|style|forbidden|required|testing
    rule_text TEXT NOT NULL,
    severity TEXT NOT NULL DEFAULT 'warning',  -- critical|warning|info
    applies_to_features TEXT,           -- JSON array of feature names, or '["all"]'
    example_good TEXT,
    example_bad TEXT,
    source_doc TEXT                     -- which markdown doc defines this rule
);
CREATE INDEX IF NOT EXISTS idx_ar_category ON architecture_rules(category);

-- ============================================================
-- NAVIGATION MAP — sidebar UI structure
-- ============================================================
CREATE TABLE IF NOT EXISTS navigation_map (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sidebar_section TEXT NOT NULL,       -- Home|Platforms|Chat Features|Stream Tools|etc.
    menu_item_label TEXT NOT NULL,
    feature_id INTEGER,
    view_file_id INTEGER,               -- FK to files
    viewmodel_file_id INTEGER,          -- FK to files
    route_command TEXT,                  -- ICommand in MainViewModel
    parent_menu_item_id INTEGER,        -- FK to self for nesting
    FOREIGN KEY (feature_id) REFERENCES features(id),
    FOREIGN KEY (view_file_id) REFERENCES files(id),
    FOREIGN KEY (viewmodel_file_id) REFERENCES files(id),
    FOREIGN KEY (parent_menu_item_id) REFERENCES navigation_map(id)
);
CREATE INDEX IF NOT EXISTS idx_nav_section ON navigation_map(sidebar_section);

-- ============================================================
-- FTS5 FULL-TEXT SEARCH INDEX
-- ============================================================
CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
    source_type,        -- feature|file|entity|event|rule|doc
    source_id UNINDEXED,
    searchable_text,
    content='',         -- contentless (we manage content ourselves)
    tokenize='porter unicode61'
);

-- ============================================================
-- PROJECT METADATA (key-value store for project-level info)
-- ============================================================
CREATE TABLE IF NOT EXISTS project_metadata (
    key TEXT PRIMARY KEY,
    value TEXT
);
""".strip()


def init_db():
    """Create the database and all tables."""
    conn = sqlite3.connect(str(DB_PATH))
    conn.executescript(SCHEMA)
    conn.commit()
    print(f"Database created at: {DB_PATH}")
    print("Tables created:")
    tables = conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
    ).fetchall()
    for t in tables:
        print(f"  - {t[0]}")
    conn.close()


if __name__ == "__main__":
    init_db()
