import sqlite3, json

conn = sqlite3.connect("tools/context-db/vyralbot_context.db")
conn.row_factory = sqlite3.Row

# Search for the feature
for pattern in ["%kickwebsetup%", "%kick-web%", "%kick%web%", "%kick%", "%web%server%"]:
    rows = conn.execute("SELECT * FROM features WHERE name LIKE ? OR display_name LIKE ?", (pattern, pattern)).fetchall()
    if rows:
        print(f"\nMatch for pattern '{pattern}':")
        for r in rows:
            print(f"\n=== Feature: {r['name']} (id={r['id']}) ===")
            print(f"  Display Name: {r['display_name']}")
            print(f"  Category: {r['category']}")
            print(f"  Status: {r['status']}")
            print(f"  Description: {r['description']}")
            print(f"  Sidebar: {r['sidebar_location']}")
            print(f"  Dependencies: {r['dependencies']}")
            print(f"  Notes: {r['notes']}")
            fid = r['id']

            # Files
            files = conn.execute("SELECT path, type, layer, summary FROM files WHERE feature_id = ?", (fid,)).fetchall()
            print(f"\n  Files ({len(files)}):")
            for f in files:
                print(f"    [{f['layer']}] {f['path']} ({f['type']}) - {f['summary']}")

            # Code entities
            entities = conn.execute("SELECT kind, name, namespace, signature, summary FROM code_entities WHERE feature_id = ?", (fid,)).fetchall()
            print(f"\n  Code Entities ({len(entities)}):")
            for e in entities:
                print(f"    [{e['kind']}] {e['name']} - {e['summary'] or ''}")
                if e['signature']:
                    print(f"           sig: {e['signature']}")

            # DB tables
            tables = conn.execute("SELECT dt.name, dt.description FROM db_tables dt WHERE dt.feature_id = ?", (fid,)).fetchall()
            print(f"\n  DB Tables ({len(tables)}):")
            for t in tables:
                print(f"    {t['name']} - {t['description']}")
                cols = conn.execute("SELECT dc.name, dc.type, dc.description FROM db_columns dc JOIN db_tables dt ON dc.table_id = dt.id WHERE dt.name = ?", (t['name'],)).fetchall()
                for c in cols:
                    print(f"      {c['name']} ({c['type']}) - {c['description']}")

            # Data operations
            ops = conn.execute("""
                SELECT do2.operation, do2.method_name, do2.sql_template, do2.description
                FROM data_operations do2
                WHERE do2.feature_id = ?
            """, (fid,)).fetchall()
            print(f"\n  Data Operations ({len(ops)}):")
            for o in ops:
                print(f"    [{o['operation']}] {o['method_name']} - {o['description']}")

            # DI registrations
            di = conn.execute("SELECT interface_name, implementation_name, lifetime FROM di_registrations WHERE feature_id = ?", (fid,)).fetchall()
            print(f"\n  DI Registrations ({len(di)}):")
            for d in di:
                print(f"    {d['interface_name']} -> {d['implementation_name']} ({d['lifetime']})")

            # Events
            events = conn.execute("SELECT name, source_platform, description FROM events WHERE feature_id = ?", (fid,)).fetchall()
            print(f"\n  Events ({len(events)}):")
            for ev in events:
                print(f"    [{ev['source_platform']}] {ev['name']} - {ev['description']}")

            # Navigation map
            nav = conn.execute("SELECT sidebar_section, menu_item_label, route_command FROM navigation_map WHERE feature_id = ?", (fid,)).fetchall()
            print(f"\n  Navigation ({len(nav)}):")
            for n in nav:
                print(f"    [{n['sidebar_section']}] {n['menu_item_label']} -> {n['route_command']}")
        break
else:
    print("No matching feature found. All features:")
    for r in conn.execute("SELECT name, display_name FROM features ORDER BY name").fetchall():
        print(f"  {r['name']} - {r['display_name']}")

conn.close()
