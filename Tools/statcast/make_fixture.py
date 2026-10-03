#!/usr/bin/env python3
"""Rebuild the committed Statcast validation fixture (stdlib only).

Downloads full pitch-level CSVs for the listed games from Baseball Savant into a
temporary directory (never committed), keeps only the columns the validation
uses, drops rows with missing values, and writes
Assets/Game/Tests/Fixtures/Statcast/statcast_validation.csv.

Usage: python3 Tools/statcast/make_fixture.py
"""
import csv
import io
import os
import sys
import urllib.request

# role, game_pk, venue, elevation_ft, temperature_f, wind, roof — weather from MLB Stats API game feed
GAMES = [
    ("development", 747218, "Chase Field", 1086, 72, "0 mph, none", "roof closed"),
    ("holdout", 745764, "Yankee Stadium", 55, 46, "16 mph, out to LF", "open"),
]
COLUMNS = [
    "game_pk", "pitcher", "pitch_type", "p_throws", "release_speed", "release_pos_x", "release_pos_y", "release_pos_z",
    "release_extension", "vx0", "vy0", "vz0", "ax", "ay", "az", "release_spin_rate", "spin_axis",
    "pfx_x", "pfx_z", "plate_x", "plate_z",
]
URL = "https://baseballsavant.mlb.com/statcast_search/csv?all=true&type=details&game_pk={}"
# Hawk-Eye spin-based active spin (%), per pitcher and pitch type, 2024 season.
ACTIVE_SPIN_URL = "https://baseballsavant.mlb.com/leaderboard/active-spin?year=2024_spin-based&min=10&hand=&csv=true"
FIXTURES = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Game", "Tests", "Fixtures", "Statcast")
OUT = os.path.join(FIXTURES, "statcast_validation.csv")
ACTIVE_SPIN_OUT = os.path.join(FIXTURES, "active_spin_2024.csv")


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "pitchlab-fixture/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read().decode("utf-8-sig")


def main():
    rows = []
    for role, game_pk, *_ in GAMES:
        text = fetch(URL.format(game_pk))
        for row in csv.DictReader(io.StringIO(text)):
            if all(row.get(c, "") != "" for c in COLUMNS):
                rows.append([role] + [row[c] for c in COLUMNS])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(["role"] + COLUMNS)
        writer.writerows(rows)
    print(f"wrote {len(rows)} pitches to {os.path.normpath(OUT)}")

    pitchers = {row[COLUMNS.index("pitcher") + 1] for row in rows}
    spin = [r for r in csv.DictReader(io.StringIO(fetch(ACTIVE_SPIN_URL))) if r["entity_id"] in pitchers]
    with open(ACTIVE_SPIN_OUT, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(spin[0].keys()), lineterminator="\n")
        writer.writeheader()
        writer.writerows(spin)
    print(f"wrote active spin for {len(spin)} of {len(pitchers)} pitchers to {os.path.normpath(ACTIVE_SPIN_OUT)}")


if __name__ == "__main__":
    sys.exit(main())
