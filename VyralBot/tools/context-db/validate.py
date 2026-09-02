"""Final validation: summary table of all features."""
import sqlite3
from pathlib import Path

DB = Path(__file__).parent / "vyralbot_context.db"
conn = sqlite3.connect(str(DB))

rows = conn.execute("""
    SELECT f.name,
           (SELECT COUNT(*) FROM files WHERE feature_id = f.id) as total_files,
           (SELECT COUNT(*) FROM files WHERE feature_id = f.id AND type = 'cs') as cs_files,
           (SELECT COUNT(*) FROM code_entities WHERE feature_id = f.id) as entities,
           (SELECT COUNT(*) FROM data_operations WHERE feature_id = f.id) as ops,
           (SELECT COUNT(*) FROM di_registrations WHERE feature_id = f.id) as di,
           (SELECT COUNT(*) FROM events WHERE feature_id = f.id) as events
    FROM features f
    ORDER BY f.name
""").fetchall()

header = f"{'Feature':30s} {'Files':>5s} {'CS':>4s} {'Ents':>5s} {'Ops':>4s} {'DI':>4s} {'Evts':>4s}"
print(header)
print('-' * len(header))
for r in rows:
    flag = " !" if r[3] == 0 else ""
    print(f"{r[0]:30s} {r[1]:5d} {r[2]:4d} {r[3]:5d} {r[4]:4d} {r[5]:4d} {r[6]:4d}{flag}")

total_ents = conn.execute('SELECT COUNT(*) FROM code_entities').fetchone()[0]
total_ops = conn.execute('SELECT COUNT(*) FROM data_operations').fetchone()[0]
total_cols = conn.execute('SELECT COUNT(*) FROM db_columns').fetchone()[0]
fts = conn.execute('SELECT COUNT(*) FROM search_index').fetchone()[0]
print(f"\nTotals: {total_ents} entities, {total_ops} data_ops, {total_cols} db_columns, {fts} FTS entries")
conn.close()
