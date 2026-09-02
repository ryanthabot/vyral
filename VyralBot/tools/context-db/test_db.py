"""Quick test of populated database."""
import sqlite3

conn = sqlite3.connect("tools/context-db/vyralbot_context.db")
conn.row_factory = sqlite3.Row

print("=== TEST 1: Features ===")
rows = conn.execute("SELECT id, name, category FROM features ORDER BY category, name").fetchall()
for r in rows:
    print(f"  [{r['category']}] {r['name']} ({r['id']})")

print(f"\n=== TEST 2: Feature context (currency) ===")
feat = conn.execute("SELECT * FROM features WHERE name = ?", ("currency",)).fetchone()
fid = feat['id']
print(f"  Name: {feat['name']} (id={fid})")
print(f"  Desc: {feat['description'][:100]}...")
files = conn.execute("SELECT path, summary FROM files WHERE feature_id = ?", (fid,)).fetchall()
print(f"  Files ({len(files)}):")
for f in files:
    print(f"    {f['path']}")
di = conn.execute("SELECT interface_name, implementation_name FROM di_registrations WHERE feature_id = ?", (fid,)).fetchall()
print(f"  DI registrations ({len(di)}):")
for d in di:
    print(f"    {d['interface_name']} -> {d['implementation_name']}")

print(f"\n=== TEST 3: FTS Search 'webhook' ===")
results = conn.execute("SELECT source_type, source_id, searchable_text FROM search_index WHERE search_index MATCH ?", ("webhook",)).fetchall()
print(f"  {len(results)} results:")
for r in results[:5]:
    text = r["searchable_text"][:80] if r["searchable_text"] else ""
    print(f"    [{r['source_type']}:{r['source_id']}] {text}")

print(f"\n=== TEST 4: Navigation Map ===")
navs = conn.execute("SELECT sidebar_section, menu_item_label, route_command FROM navigation_map ORDER BY sidebar_section, menu_item_label").fetchall()
for n in navs:
    print(f"  [{n['sidebar_section']}] {n['menu_item_label']} -> {n['route_command']}")

print(f"\n=== TEST 5: Architecture Rules ===")
rules = conn.execute("SELECT category, rule_text FROM architecture_rules WHERE severity = 'critical'").fetchall()
print(f"  {len(rules)} critical rules")

print(f"\n=== TEST 6: DB Tables (app schema) ===")
tables = conn.execute("SELECT id, name, description FROM db_tables ORDER BY name").fetchall()
for t in tables:
    cols = conn.execute("SELECT COUNT(*) FROM db_columns WHERE table_id = ?", (t['id'],)).fetchone()[0]
    print(f"  {t['name']} ({cols} columns)")

print(f"\n=== TEST 7: Events ===")
events = conn.execute("SELECT name, source_platform FROM events ORDER BY source_platform, name").fetchall()
for e in events:
    print(f"  [{e['source_platform']}] {e['name']}")

print(f"\n=== TEST 8: Event Flows ===")
flows = conn.execute("SELECT description FROM event_flows").fetchall()
print(f"  {len(flows)} flows")
for fl in flows[:5]:
    print(f"    {fl['description']}")

conn.close()
print("\nALL TESTS PASSED")
