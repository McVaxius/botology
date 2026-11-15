import json
import pathlib
import tempfile
import unittest

import review_catalog


def entry(entry_id: str, display_name: str = "Example") -> dict:
    return {
        "id": entry_id,
        "displayName": display_name,
        "matchTokens": [entry_id],
    }


class ManifestLoadingTests(unittest.TestCase):
    def test_populated_plugins_win_when_entries_is_empty(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            path = pathlib.Path(temp_directory) / "catalog.json"
            path.write_text(json.dumps({
                "Plugins": [entry("master")],
                "Entries": [],
            }), encoding="utf-8")

            manifest = review_catalog.load_manifest_data(path)

        self.assertEqual(["master"], [row["id"] for row in manifest.entries])

    def test_discord_url_aliases_load_as_the_canonical_field(self) -> None:
        for alias in ("discordUrl", "DiscordUrl", "DISCORDURL", " discordURL "):
            with self.subTest(alias=alias), tempfile.TemporaryDirectory() as temp_directory:
                path = pathlib.Path(temp_directory) / "catalog.json"
                path.write_text(json.dumps({
                    "Plugins": [entry("alias") | {alias: "https://discord.gg/ExampleInvite"}],
                }), encoding="utf-8")

                manifest = review_catalog.load_manifest_data(path)
                reviews = review_catalog.review_submission([], manifest.entries)

                self.assertEqual("https://discord.gg/ExampleInvite", manifest.entries[0]["discordUrl"])
                self.assertEqual(1, len(reviews))
                self.assertEqual("APPROVE", reviews[0].recommendation)


class DiscordUrlReviewTests(unittest.TestCase):
    def test_missing_null_and_blank_discord_urls_are_equivalent(self) -> None:
        for fields in ({}, {"discordUrl": None}, {"discordUrl": ""}, {"discordUrl": "  "}):
            with self.subTest(fields=fields):
                reviews = review_catalog.review_submission(
                    [entry("optional")],
                    [entry("optional") | fields],
                )

                self.assertEqual([], reviews)

    def test_supplied_discord_url_is_trimmed_and_approved_for_an_additive_row(self) -> None:
        submission = entry("new") | {"discordUrl": "  https://discord.gg/ExampleInvite  "}

        reviews = review_catalog.review_submission([], [submission])

        self.assertEqual("https://discord.gg/ExampleInvite", review_catalog.normalize_entry(submission)["discordUrl"])
        self.assertEqual(1, len(reviews))
        self.assertEqual("additive row", reviews[0].row_kind)
        self.assertEqual("APPROVE", reviews[0].recommendation)

    def test_identical_discord_url_with_surrounding_whitespace_is_skipped(self) -> None:
        reviews = review_catalog.review_submission(
            [entry("same") | {"discordUrl": "https://discord.gg/ExampleInvite"}],
            [entry("same") | {"discordUrl": "  https://discord.gg/ExampleInvite  "}],
        )

        self.assertEqual([], reviews)

    def test_discord_url_changes_are_reported_as_overrides(self) -> None:
        cases = (
            ({}, {"discordUrl": "https://discord.gg/ExampleInvite"}),
            ({"discordUrl": "https://discord.gg/OldInvite"}, {"discordUrl": "https://discord.gg/ExampleInvite"}),
            ({"discordUrl": "https://discord.gg/ExampleInvite"}, {"discordUrl": "https://discord.gg/exampleinvite"}),
            ({"discordUrl": "https://discord.gg/ExampleInvite"}, {}),
            ({"discordUrl": "https://discord.gg/ExampleInvite"}, {"discordUrl": None}),
            ({"discordUrl": "https://discord.gg/ExampleInvite"}, {"discordUrl": "  "}),
        )
        for master_fields, submission_fields in cases:
            with self.subTest(master=master_fields, submission=submission_fields):
                reviews = review_catalog.review_submission(
                    [entry("changed") | master_fields],
                    [entry("changed") | submission_fields],
                )

                self.assertEqual(1, len(reviews))
                self.assertEqual("override row", reviews[0].row_kind)
                self.assertEqual("APPROVE", reviews[0].recommendation)
                self.assertEqual(["discordUrl"], reviews[0].changed_fields)

    def test_non_string_discord_urls_are_denied(self) -> None:
        for value in (123, True, ["https://discord.gg/ExampleInvite"], {"url": "https://discord.gg/ExampleInvite"}):
            with self.subTest(value=value):
                reviews = review_catalog.review_submission(
                    [],
                    [entry("invalid") | {"discordUrl": value}],
                )

                self.assertEqual(1, len(reviews))
                self.assertEqual("DENY", reviews[0].recommendation)
                self.assertEqual(["Field 'discordUrl' must be a string"], reviews[0].reasons)

    def test_discord_url_preserves_legacy_compatibility_and_ai_attribution(self) -> None:
        submission = entry("legacy") | {
            "discordUrl": "https://discord.gg/ExampleInvite",
            "ruleType": "paired_conflict_red",
            "relatedIds": [" OTHER "],
            "isAiAttributed": True,
        }

        normalized = review_catalog.normalize_entry(submission)
        reviews = review_catalog.review_submission([], [submission])

        self.assertEqual(["other"], normalized["red"])
        self.assertIs(True, normalized["isAiAttributed"])
        self.assertEqual(1, len(reviews))
        self.assertEqual("APPROVE", reviews[0].recommendation)


class DeletedIdReviewTests(unittest.TestCase):
    def test_existing_master_id_is_approved_for_removal(self) -> None:
        reviews = review_catalog.review_submission(
            [entry("old")],
            [],
            ["old"],
        )

        self.assertEqual(1, len(reviews))
        self.assertEqual("master-row removal", reviews[0].row_kind)
        self.assertEqual("APPROVE", reviews[0].recommendation)

    def test_unknown_id_is_denied_for_removal(self) -> None:
        reviews = review_catalog.review_submission(
            [entry("known")],
            [],
            ["unknown"],
        )

        self.assertEqual("DENY", reviews[0].recommendation)
        self.assertIn("not present in master", " ".join(reviews[0].reasons))

    def test_same_id_cannot_be_saved_and_removed(self) -> None:
        reviews = review_catalog.review_submission(
            [entry("same")],
            [entry("same", "Changed")],
            ["same"],
        )

        self.assertEqual(2, len(reviews))
        self.assertTrue(all(review.recommendation == "DENY" for review in reviews))


class AiAttributionTests(unittest.TestCase):
    def test_missing_ai_attribution_normalizes_to_false(self) -> None:
        normalized = review_catalog.normalize_entry(entry("missing"))

        self.assertIs(False, normalized["isAiAttributed"])

    def test_true_and_false_ai_attribution_values_are_preserved(self) -> None:
        true_entry = entry("true") | {"isAiAttributed": True}
        false_entry = entry("false") | {"isAiAttributed": False}

        self.assertIs(True, review_catalog.normalize_entry(true_entry)["isAiAttributed"])
        self.assertIs(False, review_catalog.normalize_entry(false_entry)["isAiAttributed"])

    def test_ai_attribution_alias_is_case_insensitive(self) -> None:
        normalized = review_catalog.normalize_entry_keys(
            entry("alias") | {"ISAIATTRIBUTED": True},
        )

        self.assertEqual(True, normalized["isAiAttributed"])

    def test_ai_attribution_change_is_reported(self) -> None:
        master = review_catalog.normalize_entry(entry("changed") | {"isAiAttributed": True})
        submission = review_catalog.normalize_entry(entry("changed") | {"isAiAttributed": False})

        self.assertIn("isAiAttributed", review_catalog.compare_entries(master, submission))

    def test_non_boolean_ai_attribution_is_denied(self) -> None:
        reviews = review_catalog.review_submission(
            [entry("invalid")],
            [entry("invalid") | {"isAiAttributed": "true"}],
        )

        self.assertEqual(1, len(reviews))
        self.assertEqual("DENY", reviews[0].recommendation)
        self.assertIn("must be a boolean", " ".join(reviews[0].reasons))


if __name__ == "__main__":
    unittest.main()
