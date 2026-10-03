# Statcast validation fixture

`statcast_validation.csv`: 649 MLB pitches (every pitch with complete tracking fields) from two 2024 regular-season games, downloaded from Baseball Savant (`https://baseballsavant.mlb.com/statcast_search/csv?all=true&type=details&game_pk=<pk>`) and reduced to the columns used by the validation. Regenerate with `python3 Tools/statcast/make_fixture.py`.

| role | game_pk | date | venue | elevation | temp | wind | roof | pitches |
|---|---|---|---|---|---|---|---|---|
| development | 747218 | 2024-04-01 (local) | Chase Field, Phoenix | 1086 ft | 72 °F | 0 mph | closed | 323 |
| holdout | 745764 | 2024-04-05 | Yankee Stadium, New York | 55 ft | 46 °F | 16 mph, out to LF | open | 326 |

Weather and elevation: MLB Stats API game feed (`https://statsapi.mlb.com/api/v1.1/game/<pk>/feed/live`, `gameData.weather`, `gameData.venue.location.elevation`). Humidity and pressure are not reported; the validation assumes 50 % RH and standard-atmosphere pressure for the elevation.

Units and frame are Statcast's (feet, ft/s, ft/s², mph, rpm, degrees; catcher's view; origin at the rear point of home plate, +y toward the pitcher, +z up). See `Docs/VALIDATION_TASK002.md` for field definitions.
