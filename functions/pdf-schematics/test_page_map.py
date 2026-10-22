import copy
import json
import os
import unittest
from types import SimpleNamespace
from unittest.mock import patch

os.environ.setdefault("AzureWebJobsStorage", "UseDevelopmentStorage=true")
os.environ.setdefault("AZURE_SUBSCRIPTION_ID", "00000000-0000-0000-0000-000000000000")
os.environ.setdefault("FOUNDRY_RESOURCE_GROUP", "test-rg")
os.environ.setdefault("FOUNDRY_RESOURCE_NAME", "test-foundry")
os.environ.setdefault("FOUNDRY_LOCATION", "francecentral")

import function_app  # noqa: E402


def word(value, zone="footer"):
    if zone == "header":
        y0, y1 = 50, 60
    elif zone == "body":
        y0, y1 = 450, 460
    else:
        y0, y1 = 900, 910
    return (10, y0, 30, y1, str(value), 0, 0, 0)


class FakePage:
    def __init__(self, words=None, label=""):
        self.words = list(words or [])
        self.label = label
        self.rect = SimpleNamespace(height=1000)

    def get_text(self, kind, sort=False):
        if kind != "words" or not sort:
            raise AssertionError("Extraction inattendue")
        return self.words

    def get_label(self):
        return self.label


class FakeDoc:
    def __init__(self, pages, label_rules=None):
        self.pages = pages
        self.page_count = len(pages)
        self.label_rules = label_rules if label_rules is not None else [
            {"startpage": 0, "prefix": "", "firstpagenum": 1, "style": "D"}
        ]
        self.closed = False

    def load_page(self, page_index):
        return self.pages[page_index]

    def get_page_labels(self):
        return self.label_rules

    def close(self):
        self.closed = True


def build_map(doc, source_etag='"etag-1"'):
    return function_app.build_page_map(
        doc,
        "manuel",
        "entreprise/machine/manuel.pdf",
        source_etag,
    )


class PageMapDetectionTests(unittest.TestCase):
    def test_unnumbered_leading_pages(self):
        doc = FakeDoc(
            [FakePage(), FakePage(), FakePage([word(1)]), FakePage([word(2)]), FakePage([word(3)])]
        )
        pages = build_map(doc)["pages"]
        self.assertEqual([page["displayPage"] for page in pages], [None, None, "1", "2", "3"])

    def test_regular_numeric_pagination_with_offset(self):
        doc = FakeDoc([FakePage([word(value)]) for value in (20, 21, 22, 23)])
        pages = build_map(doc)["pages"]
        self.assertEqual([page["displayPage"] for page in pages], ["20", "21", "22", "23"])
        self.assertTrue(all(page["source"] == "ExtractedText" for page in pages))

    def test_informative_page_labels(self):
        rules = [{"startpage": 0, "prefix": "", "firstpagenum": 1, "style": "r"}]
        doc = FakeDoc(
            [FakePage(label="i"), FakePage(label="ii"), FakePage(label="iii")],
            label_rules=rules,
        )
        pages = build_map(doc)["pages"]
        self.assertEqual([page["displayPage"] for page in pages], ["i", "ii", "iii"])
        self.assertTrue(all(page["source"] == "PdfPageLabel" for page in pages))

    def test_identity_labels_yield_to_different_printed_pagination(self):
        doc = FakeDoc(
            [
                FakePage([word(70)], label="1"),
                FakePage([word(71)], label="2"),
                FakePage([word(72)], label="3"),
            ]
        )
        pages = build_map(doc)["pages"]
        self.assertEqual([page["displayPage"] for page in pages], ["70", "71", "72"])
        self.assertTrue(all(page["source"] == "ExtractedText" for page in pages))

    def test_no_native_text_is_unknown(self):
        pages = build_map(FakeDoc([FakePage(), FakePage(), FakePage()]))["pages"]
        self.assertTrue(all(page["displayPage"] is None for page in pages))
        self.assertTrue(all(page["source"] == "Unknown" for page in pages))

    def test_body_number_is_not_pagination(self):
        doc = FakeDoc([FakePage([word(value, "body")]) for value in (1, 2, 3)])
        self.assertTrue(
            all(page["displayPage"] is None for page in build_map(doc)["pages"])
        )

    def test_sequence_break_only_keeps_recurrent_segment(self):
        doc = FakeDoc([FakePage([word(value)]) for value in (10, 11, 99, 13, 14, 15)])
        values = [page["displayPage"] for page in build_map(doc)["pages"]]
        self.assertEqual(values, [None, None, None, "13", "14", "15"])

    def test_ambiguous_recurrent_sequences_are_unknown(self):
        doc = FakeDoc(
            [
                FakePage([word(1), word(11)]),
                FakePage([word(2), word(12)]),
                FakePage([word(3), word(13)]),
            ]
        )
        self.assertTrue(
            all(page["displayPage"] is None for page in build_map(doc)["pages"])
        )


class MemoryBlobClient:
    def __init__(self, container, blob_name):
        self.container = container
        self.blob_name = blob_name

    def download_blob(self, **_kwargs):
        if self.blob_name not in self.container.blobs:
            raise function_app.ResourceNotFoundError("blob absent")
        return SimpleNamespace(
            readall=lambda: self.container.blobs[self.blob_name],
            properties=SimpleNamespace(etag=self.container.etags[self.blob_name]),
        )

    def get_blob_properties(self):
        if self.blob_name not in self.container.blobs:
            raise function_app.ResourceNotFoundError("blob absent")
        return SimpleNamespace(etag=self.container.etags[self.blob_name])

    def upload_blob(self, data, overwrite=False, etag=None, **_kwargs):
        if self.container.conflict_payload is not None and self.blob_name.endswith("page-map.json"):
            payload = self.container.conflict_payload
            self.container.conflict_payload = None
            self.container.blobs[self.blob_name] = json.dumps(payload).encode("utf-8")
            self.container.etags[self.blob_name] = '"winner-etag"'
            raise function_app.ResourceExistsError("conflit")
        if not overwrite and self.blob_name in self.container.blobs:
            raise function_app.ResourceExistsError("existe")
        if etag is not None and self.container.etags.get(self.blob_name) != etag:
            raise function_app.ResourceModifiedError("etag modifié")
        self.container.sequence += 1
        new_etag = f'"etag-{self.container.sequence}"'
        self.container.blobs[self.blob_name] = data
        self.container.etags[self.blob_name] = new_etag
        return {"etag": new_etag}


class MemoryContainer:
    def __init__(self, blobs=None, etags=None):
        self.blobs = dict(blobs or {})
        self.etags = dict(etags or {})
        self.sequence = 10
        self.conflict_payload = None
        self.closed = False

    def get_blob_client(self, blob_name):
        return MemoryBlobClient(self, blob_name)

    def close(self):
        self.closed = True


class MemoryBlobService:
    def __init__(self, container):
        self.container = container
        self.closed = False

    def get_container_client(self, _name):
        return self.container

    def close(self):
        self.closed = True


class PageMapPersistenceTests(unittest.TestCase):
    folder = "entreprise/machine/manuel"
    map_path = f"{folder}/page-map.json"
    source_blob = "entreprise/machine/manuel.pdf"

    def setUp(self):
        self.doc = FakeDoc([FakePage([word(1)]), FakePage([word(2)]), FakePage([word(3)])])

    def _ensure(self, container, source_etag='"etag-1"'):
        return function_app.ensure_page_map(
            self.doc,
            container,
            "manuel",
            self.folder,
            self.source_blob,
            source_etag,
        )

    def test_valid_page_map_is_reused(self):
        payload = build_map(self.doc)
        container = MemoryContainer(
            {self.map_path: json.dumps(payload).encode("utf-8")},
            {self.map_path: '"map-etag"'},
        )
        with patch.object(function_app, "build_page_map") as generator:
            result, etag, created = self._ensure(container)
        generator.assert_not_called()
        self.assertEqual(result, payload)
        self.assertEqual(etag, '"map-etag"')
        self.assertFalse(created)

    def test_source_etag_change_invalidates_page_map(self):
        stale_payload = build_map(self.doc, source_etag='"old-etag"')
        container = MemoryContainer(
            {self.map_path: json.dumps(stale_payload).encode("utf-8")},
            {self.map_path: '"map-etag"'},
        )
        result, _, created = self._ensure(container, source_etag='"new-etag"')
        self.assertTrue(created)
        self.assertEqual(result["sourceETag"], '"new-etag"')

    def test_concurrent_creation_reuses_winner(self):
        winner = build_map(self.doc)
        container = MemoryContainer()
        container.conflict_payload = winner
        result, etag, created = self._ensure(container)
        self.assertEqual(result, winner)
        self.assertEqual(etag, '"winner-etag"')
        self.assertFalse(created)

    def test_retry_after_crash_reuses_created_artifact(self):
        container = MemoryContainer()
        first, first_etag, first_created = self._ensure(container)
        second, second_etag, second_created = self._ensure(container)
        self.assertTrue(first_created)
        self.assertFalse(second_created)
        self.assertEqual(second, first)
        self.assertEqual(second_etag, first_etag)


class PageMapBackfillTests(unittest.TestCase):
    def test_completed_document_backfill_does_not_reingest(self):
        source_blob = "entreprise/machine/manuel.pdf"
        folder = "entreprise/machine/manuel"
        processing_path = f"{folder}/processing.json"
        source_etag = '"source-etag"'
        state = function_app.build_processing_state(
            "manuel", source_blob, source_etag, 3
        )
        state["status"] = "completed"
        state["source_pdf_indexed"] = True
        original_state = copy.deepcopy(state)
        processing_bytes = json.dumps(state).encode("utf-8")
        container = MemoryContainer(
            {processing_path: processing_bytes, source_blob: b"pdf"},
            {processing_path: '"processing-etag"', source_blob: source_etag},
        )
        service = MemoryBlobService(container)
        doc = FakeDoc([FakePage([word(1)]), FakePage([word(2)]), FakePage([word(3)])])

        with (
            patch.object(
                function_app.BlobServiceClient,
                "from_connection_string",
                return_value=service,
            ),
            patch.object(function_app.fitz, "open", return_value=doc),
            patch.object(function_app, "generate_page_images") as images,
            patch.object(function_app, "describe_pages") as descriptions,
            patch.object(function_app, "index_page_descriptions") as page_search,
            patch.object(function_app, "index_source_pdf") as pdf_search,
        ):
            result = function_app.backfill_page_map(source_blob, "connection")

        images.assert_not_called()
        descriptions.assert_not_called()
        page_search.assert_not_called()
        pdf_search.assert_not_called()
        self.assertTrue(result["created"])
        self.assertEqual(result["determinedPageCount"], 3)
        self.assertEqual(
            json.loads(container.blobs[processing_path].decode("utf-8")),
            original_state,
        )
        self.assertIn(f"{folder}/page-map.json", container.blobs)


if __name__ == "__main__":
    unittest.main()