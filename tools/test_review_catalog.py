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


if __name__ == "__main__":
    unittest.main()
