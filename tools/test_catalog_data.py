import json
import pathlib
import re
import subprocess
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
UPDATES_ROOT = REPO_ROOT.parent / "botologyupdates"
FROZEN_AETHERFEED_SHA256 = "28e98ec13f5c2feabd9166c8a4cd3749ee42b81b6a9638175106da97ec27f7f5"

LIVE_AI_TRUE_IDS = {
    "xivshiniessyncplugin", "betterdeaths", "damageterror", "artisan", "autofategrind",
    "bocchi", "fatewalker", "mogtome", "sealbreaker", "ai_duty_solver", "lootgoblin",
    "vermaxion", "footballer", "master_of_puppets", "windupkey", "choke_abo",
    "DomanMahjongAI", "automarketpro", "auxmarketboard", "charon", "coppelia", "dad",
    "frenrider", "hrothgarscent", "autofrontline", "autopvpseriesgrind", "aetherphone",
    "begone", "dhog_potato_system", "echokraut", "pfpresets", "popotovox",
    "autodailytribes", "questionable_wiggly", "aetherfit", "alphachannel",
    "gobchatexplugin", "houtfits", "caduceus", "olympus", "hmoniker", "krangler",
    "botology", "ChocoboColourized", "dheacon", "dhoggpt", "dtroverlay",
    "thick_thighs_save_lives", "xa_hud_navigator", "xa_slave", "active_venue_finder",
}

BUNDLED_AI_TRUE_IDS = {
    "artisan", "bocchi", "lootgoblin", "mogtome", "ai_duty_solver",
    "questionable_wiggly", "botology", "choke_abo", "dheacon", "dhog_potato_system",
    "dhoggpt", "footballer", "frenrider", "krangler", "thick_thighs_save_lives",
    "vermaxion", "xa_hud_navigator", "xa_slave",
}

FALLBACK_AI_TRUE_IDS = {
    "lootgoblin", "mogtome", "artisan", "ai_duty_solver", "frenrider", "vermaxion",
    "xa_slave", "questionable_wiggly",
}

ADDITION_IDS = {
    "aetherphone", "venuemapper", "gambawhere", "betterdeaths", "dalamudrecipehelper",
    "housinghistory", "memoria", "ktisis", "fctracker", "dad", "aetherfit", "aetherlove",
    "fcch", "kinklinkclient", "carbunclepartner", "hammeter", "waymarkstudio",
}

HOUSING_IDS = {
    "buildingway", "burning_down_the_house", "displaceplugin", "househunter",
    "housingcullfix", "housingway", "makeplaceassistant", "target_furniture", "visitorspass",
}

ROLEPLAY_IDS = {
    "cards_against_hyurmanity", "characterselectplugin", "customize", "dynamic_bridge",
    "glamourer", "hellofellowhumans", "hyperborea", "lightlesssync", "loci", "mannerisms",
    "moodles", "penumbra", "playersync", "proximityvoicechat",
}

VENUE_IDS = {
    "active_venue_finder", "shoutrunner", "venuescope", "venuestatusandgreet", "wahjumps",
}


def load_relaxed_json_text(raw_text: str) -> dict:
    return json.loads(re.sub(r",\s*([}\]])", r"\1", raw_text))


def load_relaxed_json(path: pathlib.Path) -> dict:
    return load_relaxed_json_text(path.read_text(encoding="utf-8"))


def head_file(repo: pathlib.Path, relative_path: str) -> dict:
    result = subprocess.run(
        ["git", "show", f"HEAD:{relative_path}"],
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return load_relaxed_json_text(result.stdout)


def rows_without_ai(rows: list[dict]) -> list[dict]:
    cleaned = []
    for row in rows:
        copied = dict(row)
        copied.pop("IsAiAttributed", None)
        copied.pop("isAiAttributed", None)
        cleaned.append(copied)
    return cleaned


class CatalogDataTests(unittest.TestCase):
    def test_ui_and_configuration_contracts_are_wired(self) -> None:
        configuration = (REPO_ROOT / "botology" / "Configuration.cs").read_text(encoding="utf-8")
        main_window = (REPO_ROOT / "botology" / "Windows" / "MainWindow.cs").read_text(encoding="utf-8")
        config_window = (REPO_ROOT / "botology" / "Windows" / "ConfigWindow.cs").read_text(encoding="utf-8")
        editor = (REPO_ROOT / "botology" / "Windows" / "CatalogEditorWindow.cs").read_text(encoding="utf-8")
        plugin = (REPO_ROOT / "botology" / "Plugin.cs").read_text(encoding="utf-8")

        self.assertIn("public bool ShowAiColumn { get; set; } = true;", configuration)
        self.assertIn("public string LastProcessedCatalogReleaseId", configuration)
        self.assertLess(main_window.index("GridColumn.Source"), main_window.index("columns.Add(GridColumn.AiAttribution)"))
        self.assertIn("DrawColumnToggle(\"AI attribution\"", main_window)
        self.assertIn("Likely AI-written code based on Aetherfeed contributor and coding-pattern attribution; snapshot 2026-07-22.", main_window)
        self.assertIn("ImGui.SmallButton(\"Patch Notes\")", main_window)
        self.assertIn("No patch notes available", main_window)
        self.assertIn("Likely AI-written (Aetherfeed attribution)", editor)
        self.assertIn("Toast for catalog notes affecting installed plugins.", config_window)
        self.assertNotIn("Botology master catalog changed and was refreshed.", plugin)

    def test_frozen_hash_and_live_true_set_are_pinned(self) -> None:
        self.assertEqual(64, len(FROZEN_AETHERFEED_SHA256))
        self.assertEqual(51, len(LIVE_AI_TRUE_IDS))

        catalog = load_relaxed_json(UPDATES_ROOT / "plugin-repository-links.json")
        rows = catalog["Plugins"]
        ids = [row["Id"] for row in rows]
        true_ids = {row["Id"] for row in rows if row.get("IsAiAttributed") is True}

        self.assertEqual(233, len(rows))
        self.assertEqual(len(ids), len({entry_id.casefold() for entry_id in ids}))
        self.assertEqual(LIVE_AI_TRUE_IDS, true_ids)
        self.assertTrue(all(row.get("IsAiAttributed") is True for row in rows if "IsAiAttributed" in row))

    def test_bundled_and_compiled_fallback_true_sets_match_the_frozen_mapping(self) -> None:
        bundled = load_relaxed_json(REPO_ROOT / "botology" / "plugin-repository-links.json")
        bundled_true = {
            row["id"] for row in bundled["plugins"] if row.get("isAiAttributed") is True
        }
        self.assertEqual(BUNDLED_AI_TRUE_IDS, bundled_true)

        fallback_source = (REPO_ROOT / "botology" / "Services" / "BotologyCatalog.cs").read_text(encoding="utf-8")
        fallback_true = set(re.findall(
            r'new PluginCatalogEntry\("([^"]+)"[^\n]+IsAiAttributed: true\)',
            fallback_source,
        ))
        self.assertEqual(FALLBACK_AI_TRUE_IDS, fallback_true)

    def test_ai_edits_do_not_change_unrelated_catalog_row_fields(self) -> None:
        live = load_relaxed_json(UPDATES_ROOT / "plugin-repository-links.json")
        live_head = head_file(UPDATES_ROOT, "plugin-repository-links.json")
        self.assertEqual(
            rows_without_ai(live_head["Plugins"]),
            rows_without_ai(live["Plugins"]),
        )

        bundled = load_relaxed_json(REPO_ROOT / "botology" / "plugin-repository-links.json")
        bundled_head = head_file(REPO_ROOT, "botology/plugin-repository-links.json")
        self.assertEqual(
            rows_without_ai(bundled_head["plugins"]),
            rows_without_ai(bundled["plugins"]),
        )

    def test_changelog_release_ids_and_affected_ids_are_valid(self) -> None:
        master = load_relaxed_json(UPDATES_ROOT / "plugin-repository-links.json")
        master_ids = {row["Id"].casefold() for row in master["Plugins"]}
        changelog = json.loads((UPDATES_ROOT / "changelog.json").read_text(encoding="utf-8"))
        releases = changelog["Releases"]
        release_ids = [release["Id"] for release in releases]

        self.assertEqual(1, changelog["SchemaVersion"])
        self.assertEqual(len(release_ids), len({release_id.casefold() for release_id in release_ids}))
        for release in releases:
            affected = release["AffectedPluginIds"]
            self.assertEqual(len(affected), len({entry_id.casefold() for entry_id in affected}))
            self.assertTrue({entry_id.casefold() for entry_id in affected} <= master_ids)

    def test_seeded_release_contains_exact_additions_and_recategorizations(self) -> None:
        changelog = json.loads((UPDATES_ROOT / "changelog.json").read_text(encoding="utf-8"))
        release = changelog["Releases"][0]
        sections = {section["Heading"]: section["Items"] for section in release["Sections"]}
        expected_affected = ADDITION_IDS | HOUSING_IDS | ROLEPLAY_IDS | VENUE_IDS

        self.assertEqual("2026-06-30-catalog", release["Id"])
        self.assertEqual("2026-06-30T22:15:06Z", release["PublishedUtc"])
        self.assertEqual(expected_affected, set(release["AffectedPluginIds"]))
        self.assertEqual(17, len(sections["Catalog additions"]))
        self.assertEqual(9, len(sections["Housing (9)"]))
        self.assertEqual(14, len(sections["Roleplay (14)"]))
        self.assertEqual(5, len(sections["Venues (5)"]))
        self.assertIn("dad (Multi-Purpose Plugins)", sections["Catalog additions"])
        self.assertNotIn("chatcasino", release["AffectedPluginIds"])
        self.assertNotIn("maskofkefka", release["AffectedPluginIds"])

        master = load_relaxed_json(UPDATES_ROOT / "plugin-repository-links.json")
        category_by_id = {row["Id"]: row["Category"] for row in master["Plugins"]}
        self.assertTrue(all(category_by_id[entry_id] == "Housing" for entry_id in HOUSING_IDS))
        self.assertTrue(all(category_by_id[entry_id] == "Roleplay" for entry_id in ROLEPLAY_IDS))
        self.assertTrue(all(category_by_id[entry_id] == "Venues" for entry_id in VENUE_IDS))


if __name__ == "__main__":
    unittest.main()
