"""Quick query to dump all .cs files with their feature assignments."""
import sqlite3
from pathlib import Path

DB = Path(__file__).parent / "vyralbot_context.db"
conn = sqlite3.connect(str(DB))

rows = conn.execute("""
    SELECT f.id, f.path, f.feature_id, f.layer, f.summary,
           COALESCE(feat.name, 'NO-FEAT') as feature_name
    FROM files f
    LEFT JOIN features feat ON f.feature_id = feat.id
    WHERE f.type = 'cs'
    ORDER BY feature_name, f.path
""").fetchall()

print(f"Total .cs files: {len(rows)}\n")
for r in rows:
    feat = r[5]
    fid = r[0]
    path = r[1]
    print(f"  [{feat:25s}] id={fid:3d}  {path}")

# Also show features with no entities
print("\n\n--- Features vs entity counts ---")
rows2 = conn.execute("""
    SELECT f.name, f.id,
           (SELECT COUNT(*) FROM files WHERE feature_id = f.id AND type = 'cs') as cs_files,
           (SELECT COUNT(*) FROM code_entities WHERE feature_id = f.id) as entities
    FROM features f
    ORDER BY f.name
""").fetchall()
for r in rows2:
    print(f"  {r[0]:30s}  cs_files={r[2]:3d}  entities={r[3]:3d}")

conn.close()
