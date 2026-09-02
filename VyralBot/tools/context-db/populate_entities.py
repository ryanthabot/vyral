"""
VyralBot Context Database — Universal Code Entity Populator
=============================================================
Data-driven approach:
  1. Query ALL .cs files from the 'files' table (with feature_id)
  2. Read each real .cs source file from disk
  3. Parse with regex to extract classes, interfaces, enums, structs, records
  4. Insert code_entities with correct file_id + feature_id
  5. Rebuild the FTS index

This replaces the broken hardcoded approach in fill_gaps.py.
"""

import sys, os, re, sqlite3
from pathlib import Path

sys.path.insert(0, os.path.dirname(__file__))
from populate import get_conn, insert_code_entity, rebuild_fts_index

# Project root (where src/ lives)
PROJECT_ROOT = Path(__file__).parent.parent.parent  # tools/context-db -> tools -> VyralBot root

# Regex patterns for C# type declarations
# Matches: [access] [modifiers] (class|interface|enum|struct|record) Name [<T>] [: Base, IFoo]
TYPE_PATTERN = re.compile(
    r'^\s*'
    r'(?P<access>public|internal|private|protected)?'
    r'\s*'
    r'(?P<modifiers>(?:(?:static|abstract|sealed|partial|readonly|ref)\s+)*)'
    r'(?P<kind>class|interface|enum|struct|record)\s+'
    r'(?P<name>\w+)'
    r'(?:<[^>]+>)?'       # optional generic params
    r'(?:\s*:\s*(?P<bases>[^\{]+?))?'  # optional base list
    r'\s*(?:\{|$)',       # opening brace or end of line
    re.MULTILINE
)

# Regex for method declarations (inside classes/interfaces)
METHOD_PATTERN = re.compile(
    r'^\s*'
    r'(?P<access>public|internal|private|protected)?'
    r'\s*'
    r'(?P<modifiers>(?:(?:static|virtual|override|abstract|async|new|sealed)\s+)*)'
    r'(?P<return_type>[\w<>\[\]?,\s]+?)\s+'
    r'(?P<name>\w+)\s*'
    r'(?P<generic><[^>]+>)?\s*'
    r'\('
    r'(?P<params>[^)]*)'
    r'\)',
    re.MULTILINE
)

# Skip code-behind files (.xaml.cs) for method extraction — they're boilerplate
SKIP_METHODS_FOR = {'.xaml.cs'}


def derive_namespace(path: str) -> str:
    """Derive likely namespace from file path."""
    # src/VyralBot.Core/Services/Chat/IChatService.cs -> VyralBot.Core.Services.Chat
    parts = path.replace('/', '.').replace('\\', '.')
    # Remove src. prefix and filename
    parts = parts.removeprefix('src.')
    # Remove the filename part
    last_dot = parts.rfind('.')  # .cs
    parts = parts[:last_dot]
    last_dot = parts.rfind('.')  # filename
    parts = parts[:last_dot]
    return parts


def derive_summary(kind: str, name: str, path: str, bases: str = None) -> str:
    """Generate a short summary from entity metadata."""
    layer = ""
    if "VyralBot.Core" in path:
        layer = "Core layer"
    elif "VyralBot.Data" in path:
        layer = "Data layer"
    elif "VyralBot.UI" in path:
        layer = "UI layer"
    elif "VyralBot.Api" in path:
        layer = "API layer"
    elif "VyralBot.Integrations" in path:
        layer = "Integration layer"

    base_info = ""
    if bases:
        bases = bases.strip().rstrip('{').strip()
        if bases:
            base_info = f" Extends/implements: {bases}."

    if kind == "interface":
        return f"{layer} interface.{base_info}"
    elif kind == "enum":
        return f"{layer} enumeration."
    elif kind == "struct" or kind == "record":
        return f"{layer} {kind}.{base_info}"
    else:
        # class
        if "ViewModel" in name:
            return f"{layer} ViewModel (MVVM).{base_info}"
        elif "Service" in name:
            return f"{layer} service.{base_info}"
        elif "Controller" in name:
            return f"{layer} API controller.{base_info}"
        elif "Entity" in name or "Entities" in path:
            return f"{layer} data entity.{base_info}"
        elif "Event" in name or "Events" in path:
            return f"{layer} event class.{base_info}"
        else:
            return f"{layer} class.{base_info}"


def extract_entities_from_source(source: str, path: str):
    """Parse C# source and return list of (kind, name, namespace, access, bases, start_line, summary)."""
    entities = []

    for m in TYPE_PATTERN.finditer(source):
        kind = m.group('kind')
        name = m.group('name')
        access = (m.group('access') or 'internal').strip()
        bases = m.group('bases')
        line_num = source[:m.start()].count('\n') + 1
        ns = derive_namespace(path)
        summary = derive_summary(kind, name, path, bases)

        entities.append({
            'kind': kind,
            'name': name,
            'namespace': ns,
            'access': access,
            'bases': bases.strip().rstrip('{').strip() if bases else None,
            'start_line': line_num,
            'summary': summary,
        })

    return entities


def populate_all_code_entities():
    """Main function: query files table, read source, extract & insert entities."""
    conn = get_conn()

    # Clear existing entities
    old_count = conn.execute("SELECT COUNT(*) FROM code_entities").fetchone()[0]
    conn.execute("DELETE FROM code_entities")
    conn.commit()
    print(f"Cleared {old_count} existing code_entities.\n")

    # Get all .cs files with feature assignment
    cs_files = conn.execute("""
        SELECT id, path, feature_id, layer, summary
        FROM files
        WHERE type = 'cs' AND feature_id IS NOT NULL
        ORDER BY feature_id, path
    """).fetchall()
    conn.close()

    print(f"Processing {len(cs_files)} .cs files...\n")

    total_entities = 0
    files_processed = 0
    files_not_found = 0
    feature_counts = {}

    for row in cs_files:
        file_id = row[0]
        rel_path = row[1]
        feature_id = row[2]
        layer = row[3]

        # Build actual filesystem path
        fs_path = PROJECT_ROOT / rel_path
        if not fs_path.exists():
            files_not_found += 1
            continue

        try:
            source = fs_path.read_text(encoding='utf-8-sig')
        except Exception as e:
            print(f"  ERROR reading {rel_path}: {e}")
            continue

        files_processed += 1

        # Extract type declarations
        entities = extract_entities_from_source(source, rel_path)

        if not entities:
            # For files with no type declarations found (maybe partial classes or unusual formatting),
            # infer a single entity from the filename
            filename = Path(rel_path).stem
            if filename.endswith('.xaml'):
                # Skip pure XAML code-behind with no entities found
                continue

            kind = "class"
            if filename.startswith('I') and filename[1:2].isupper():
                kind = "interface"

            ns = derive_namespace(rel_path)
            summary = derive_summary(kind, filename, rel_path)
            entities = [{
                'kind': kind,
                'name': filename,
                'namespace': ns,
                'access': 'public',
                'bases': None,
                'start_line': 1,
                'summary': summary + " (inferred from filename)",
            }]

        for ent in entities:
            is_entry = ent['name'] in ('AppHost', 'App', 'Program', 'MainWindow')
            insert_code_entity(
                file_id=file_id,
                feature_id=feature_id,
                kind=ent['kind'],
                name=ent['name'],
                namespace=ent['namespace'],
                signature=f"{ent['access']} {ent['kind']} {ent['name']}" + (f" : {ent['bases']}" if ent['bases'] else ""),
                start_line=ent['start_line'],
                access_modifier=ent['access'],
                summary=ent['summary'],
                is_entry_point=is_entry,
            )
            total_entities += 1
            feature_counts[feature_id] = feature_counts.get(feature_id, 0) + 1

    print(f"\nResults:")
    print(f"  Files processed:   {files_processed}")
    print(f"  Files not found:   {files_not_found}")
    print(f"  Entities inserted: {total_entities}")

    # Show per-feature breakdown
    conn = get_conn()
    print(f"\n  Per-feature entity counts:")
    for feat_id in sorted(feature_counts.keys()):
        feat_name = conn.execute("SELECT name FROM features WHERE id = ?", (feat_id,)).fetchone()[0]
        print(f"    {feat_name:30s}  {feature_counts[feat_id]:3d} entities")

    # Check for features still at 0
    zero_feats = conn.execute("""
        SELECT f.name FROM features f
        WHERE (SELECT COUNT(*) FROM code_entities ce WHERE ce.feature_id = f.id) = 0
    """).fetchall()
    if zero_feats:
        print(f"\n  WARNING: {len(zero_feats)} features still have 0 entities:")
        for r in zero_feats:
            print(f"    - {r[0]}")

    conn.close()
    return total_entities


def main():
    print("=" * 60)
    print("VyralBot Context DB — Universal Code Entity Populator")
    print("=" * 60)

    # Step 1: Populate code entities from actual source files
    total = populate_all_code_entities()

    # Step 2: Rebuild FTS index
    print("\n" + "-" * 40)
    print("Rebuilding FTS index...")
    rebuild_fts_index()

    # Step 3: Final validation
    conn = get_conn()
    entity_count = conn.execute("SELECT COUNT(*) FROM code_entities").fetchone()[0]
    fts_count = conn.execute("SELECT COUNT(*) FROM search_index").fetchone()[0]
    feat_with_entities = conn.execute("""
        SELECT COUNT(DISTINCT feature_id) FROM code_entities WHERE feature_id IS NOT NULL
    """).fetchone()[0]
    total_features = conn.execute("SELECT COUNT(*) FROM features").fetchone()[0]
    conn.close()

    print(f"\nFinal validation:")
    print(f"  Total code_entities: {entity_count}")
    print(f"  FTS index entries:   {fts_count}")
    print(f"  Features with entities: {feat_with_entities}/{total_features}")
    print("\nDone!")


if __name__ == "__main__":
    main()
