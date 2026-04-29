#!/usr/bin/env python3
"""Check SQLite database for albums"""

import sqlite3
import os

db_path = r"D:\repos\PhotoGallery\PhotoGallery\PhotoGallery\app.db"

if not os.path.exists(db_path):
    print(f"Database not found at: {db_path}")
    exit(1)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

print("\n" + "="*70)
print("SQLite Database Inspection")
print("="*70)

# Get all tables
print("\n[1] Tables in database:")
cursor.execute("SELECT name FROM sqlite_master WHERE type='table';")
tables = cursor.fetchall()
for table in tables:
    print(f"  - {table[0]}")

# Check Albums table
print("\n[2] Albums table schema:")
cursor.execute("PRAGMA table_info(Albums);")
columns = cursor.fetchall()
for col in columns:
    print(f"  - {col[1]} ({col[2]})")

# Check how many albums
print("\n[3] Album records:")
cursor.execute("SELECT COUNT(*) as cnt FROM Albums;")
count = cursor.fetchone()[0]
print(f"  Total albums: {count}")

if count > 0:
    print("\n  Albums:")
    cursor.execute("SELECT Id, Title, Description, CreatedDate, OwnerId FROM Albums;")
    for row in cursor.fetchall():
        print(f"    - {row[1]} (ID: {row[0]}, Owner: {row[4]})")

# Check Users table
print("\n[4] Users table:")
cursor.execute("SELECT COUNT(*) as cnt FROM AspNetUsers;")
user_count = cursor.fetchone()[0]
print(f"  Total users: {user_count}")

if user_count > 0:
    print("\n  Users:")
    cursor.execute("SELECT Id, Email, UserName FROM AspNetUsers LIMIT 5;")
    for row in cursor.fetchall():
        print(f"    - {row[1]} ({row[2]})")

conn.close()

print("\n" + "="*70)
print("Inspection Complete")
print("="*70 + "\n")
