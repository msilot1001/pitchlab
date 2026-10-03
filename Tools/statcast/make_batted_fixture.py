#!/usr/bin/env python3
"""Rebuild the committed batted-ball validation fixture (stdlib only).

Downloads pitch-level Statcast CSVs for the listed Tropicana Field games (fixed dome: no wind, controlled
climate) and keeps balls in play with launch speed, launch angle and projected distance. Game weather and venue
elevation come from the MLB Stats API game feed. Writes
Assets/Game/Tests/Fixtures/Statcast/batted_validation.csv and batted_games.csv.

Usage: python3 Tools/statcast/make_batted_fixture.py
"""
import csv
import io
import json
import os
import sys
import urllib.request

# role, game_pk (2024 Tampa Bay home games, Tropicana Field)
GAMES = [
    ("development", 745101), ("development", 745100), ("development", 745103), ("development", 745098), ("development", 745095),
    ("holdout", 745097), ("holdout", 745094), ("holdout", 745096), ("holdout", 745092),
]
COLUMNS = ["game_pk", "stand", "bb_type", "events", "launch_speed", "launch_angle", "hit_distance_sc", "hc_x", "hc_y"]
SAVANT = "https://baseballsavant.mlb.com/statcast_search/csv?all=true&type=details&game_pk={}"
FEED = "https://statsapi.mlb.com/api/v1.1/game/{}/feed/live"
FIXTURES = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Game", "Tests", "Fixtures", "Statcast")


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "pitchlab-fixture/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read().decode("utf-8-sig")


def main():
    balls, games = [], []
    for role, game_pk in GAMES:
        data = json.loads(fetch(FEED.format(game_pk)))["gameData"]
        weather = data.get("weather", {})
        venue = data["venue"]
        games.append([role, game_pk, data["datetime"]["officialDate"], venue["name"], venue.get("location", {}).get("elevation", ""),
                      weather.get("temp", ""), weather.get("condition", ""), weather.get("wind", "")])
        for row in csv.DictReader(io.StringIO(fetch(SAVANT.format(game_pk)))):
            if row.get("type") == "X" and all(row.get(c, "") != "" for c in ["launch_speed", "launch_angle", "hit_distance_sc"]):
                balls.append([role] + [row.get(c, "") for c in COLUMNS])
    with open(os.path.join(FIXTURES, "batted_validation.csv"), "w", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(["role"] + COLUMNS)
        writer.writerows(balls)
    with open(os.path.join(FIXTURES, "batted_games.csv"), "w", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(["role", "game_pk", "date", "venue", "elevation_ft", "temp_f", "condition", "wind"])
        writer.writerows(games)
    print(f"wrote {len(balls)} batted balls from {len(games)} games")


if __name__ == "__main__":
    sys.exit(main())
