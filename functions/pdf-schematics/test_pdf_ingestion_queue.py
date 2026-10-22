"""Tests ciblés du découpage V1 de l'ingestion PDF par Azure Queue."""

import copy
import json
import os
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

os.environ.setdefault("AzureWebJobsStorage", "UseDevelopmentStorage=true")
os.environ.setdefault("AZURE_SUBSCRIPTION_ID", "00000000-0000-0000-0000-000000000000")
os.environ.setdefault("FOUNDRY_RESOURCE_GROUP", "test-rg")
os.environ.setdefault("FOUNDRY_RESOURCE_NAME", "test-foundry")
os.environ.setdefault("FOUNDRY_LOCATION", "francecentral")

import function_app  # noqa: E402


class FakeQueueMessage:
    def __init__(self, payload):
        self._body = json.dumps(payload).encode("utf-8")

    def get_body(self):
        return self._body


class FakeQueueOutput:
    def __init__(self):
        self.value = None

    def set(self, value):
        self.value = value


class FakeBlobClient:
    def __init__(self, blob_name, etag='"etag-1"', data=b"pdf"):
        self.blob_name = blob_name
        self._etag = etag
        self._data = data

    def get_blob_properties(self):
        return SimpleNamespace(etag=self._etag)

    def download_blob(self, **_kwargs):
        return SimpleNamespace(readall=lambda: self._data)


class FakeContainer:
    def __init__(self, etag='"etag-1"'):
        self.etag = etag

    def get_blob_client(self, blob_name):
        return FakeBlobClient(blob_name, etag=self.etag)


class FakeBlobService:
    def __init__(self, container):
        self.container = container

    def get_container_client(self, _name):
        return self.container

    def close(self):
        pass


class FakeDoc:
    def __init__(self, page_count):
        self.page_count = page_count

    def close(self):
        pass


class InMemoryBlobClient:
    def __init__(self, container, blob_name):
        self.container = container
        self.blob_name = blob_name

    def get_blob_properties(self):
        return SimpleNamespace(etag=self.container.etag)

    def download_blob(self, **_kwargs):
        if self.blob_name not in self.container.blobs:
            raise function_app.ResourceNotFoundError("blob absent")
        self.container.downloads.append(self.blob_name)
        data = self.container.blobs[self.blob_name]
        return SimpleNamespace(readall=lambda: data)

    def upload_blob(self, data, **_kwargs):
        self.container.blobs[self.blob_name] = data

    def delete_blob(self):
        self.container.blobs.pop(self.blob_name, None)


class InMemoryContainer:
    def __init__(self, blobs=None, etag='"etag-1"'):
        self.blobs = dict(blobs or {})
        self.etag = etag
        self.downloads = []

    def get_blob_client(self, blob_name):
        return InMemoryBlobClient(self, blob_name)


class BatchConstructionTests(unittest.TestCase):
    def test_pdf_5_pages_creates_one_batch(self):
        self.assertEqual(
            function_app.build_ingestion_batches(5),
            [
                {
                    "start_page": 1,
                    "end_page": 5,
                    "status": "pending",
                    "owner_work_id": None,
                }
            ],
        )

    def test_pdf_15_pages_creates_two_batches(self):
        self.assertEqual(
            function_app.build_ingestion_batches(15),
            [
                {
                    "start_page": 1,
                    "end_page": 10,
                    "status": "pending",
                    "owner_work_id": None,
                },
                {
                    "start_page": 11,
                    "end_page": 15,
                    "status": "pending",
                    "owner_work_id": None,
                },
            ],
        )

    def test_pdf_126_pages_creates_thirteen_batches(self):
        batches = function_app.build_ingestion_batches(126)
        self.assertEqual(len(batches), 13)
        self.assertEqual(batches[0]["start_page"], 1)
        self.assertEqual(
            batches[11],
            {
                "start_page": 111,
                "end_page": 120,
                "status": "pending",
                "owner_work_id": None,
            },
        )
        self.assertEqual(
            batches[12],
            {
                "start_page": 121,
                "end_page": 126,
                "status": "pending",
                "owner_work_id": None,
            },
        )

    def test_generate_page_images_keeps_global_page_numbers(self):
        loaded_indexes = []

        class Pix:
            def tobytes(self, _format):
                return b"png"

        class Page:
            def get_pixmap(self, **_kwargs):
                return Pix()

        class Doc:
            def load_page(self, page_index):
                loaded_indexes.append(page_index)
                return Page()

        class ExistingBlob:
            def exists(self):
                return True

        class Container:
            def get_blob_client(self, _path):
                return ExistingBlob()

        with (
            patch.object(function_app, "detect_content_rect", return_value=object()),
            patch.object(function_app, "make_tiles", return_value=[]),
        ):
            function_app.generate_page_images(
                Doc(), Container(), "manuel", "societe/machine/manuel", 126, 41, 50
            )

        self.assertEqual(loaded_indexes, list(range(40, 50)))

    def test_describe_pages_keeps_global_page_numbers(self):
        requested_paths = []

        class Container:
            def get_blob_client(self, path):
                requested_paths.append(path)
                return object()

        with patch.dict(os.environ, {"ENABLE_IMAGE_DESCRIPTION": "false"}):
            descriptions = function_app.describe_pages(
                Container(), "manuel", "societe/machine/manuel", 126, 41, 50
            )

        self.assertEqual(descriptions, [])
        self.assertEqual(len(requested_paths), 10)
        self.assertIn("page-00041", requested_paths[0])
        self.assertIn("page-00050", requested_paths[-1])

    def test_each_new_queue_message_gets_a_new_work_id(self):
        state = function_app.build_processing_state(
            "manuel", "entreprise/machine/manuel.pdf", '"etag-1"', 5
        )
        first = json.loads(
            function_app.build_ingestion_queue_message(state, state["batches"][0])
        )
        second = json.loads(
            function_app.build_ingestion_queue_message(state, state["batches"][0])
        )
        self.assertTrue(first["work_id"])
        self.assertNotEqual(first["work_id"], second["work_id"])


class AdaptiveClaudeDescriptionTests(unittest.TestCase):
    def _page_inputs(self):
        return (
            {
                "name": "manual-page-00001-full.png",
                "blob_path": "doc/page-00001/manual-page-00001-full.png",
                "bytes": b"full-image",
                "kind": "full",
            },
            [
                {
                    "name": f"manual-page-00001-tile-r{row:02d}-c{column:02d}.png",
                    "blob_path": (
                        "doc/page-00001/"
                        f"manual-page-00001-tile-r{row:02d}-c{column:02d}.png"
                    ),
                    "bytes": f"tile-{row}-{column}".encode(),
                    "kind": "tile",
                    "row": row,
                    "column": column,
                }
                for row, column in ((1, 1), (1, 2), (2, 2))
            ],
        )

    def _generate(self, responses, has_extractable_text=True):
        full_image, tiles = self._page_inputs()
        uploads = []
        with (
            patch.object(
                function_app,
                "describe_image_with_claude",
                side_effect=responses,
            ) as describe,
            patch.object(
                function_app,
                "upload_blob",
                side_effect=lambda _container, name, data, _content_type: uploads.append(
                    (name, data)
                ),
            ),
        ):
            path = function_app.generate_page_description(
                container_client=object(),
                document_id="manual",
                page_number=1,
                page_folder="doc/page-00001",
                full_image=full_image,
                uploaded_tiles=tiles,
                has_extractable_text=has_extractable_text,
            )
        return path, uploads, describe

    def test_prompt_mode_with_extractable_text_avoids_retranscription(self):
        prompt = function_app.build_description_prompt(
            "initial", True, ["r01-c01", "r03-c03"]
        )
        self.assertIn("texte est extractible", prompt)
        self.assertIn("Ne retranscris donc pas inutilement", prompt)
        self.assertIn("textes intégrés aux images", prompt)
        self.assertIn("r01-c01, r03-c03", prompt)

    def test_prompt_mode_without_extractable_text_requires_faithful_extraction(self):
        prompt = function_app.build_description_prompt("initial", False, [])
        self.assertIn("Aucun texte exploitable n'est extractible", prompt)
        self.assertIn("Extrais fidèlement l'ensemble des textes visibles", prompt)
        self.assertIn("valeurs et unités", prompt)

    def test_no_tile_request_uses_one_call_and_writes_one_final_txt(self):
        response = SimpleNamespace(
            content=[SimpleNamespace(type="text", text="Description finale unique")],
            stop_reason="end_turn",
        )

        path, uploads, describe = self._generate([response])

        self.assertEqual(describe.call_count, 1)
        self.assertEqual(path, "doc/page-00001/manual_page-00001-description.txt")
        self.assertEqual(len(uploads), 1)
        text = uploads[0][1].decode("utf-8")
        self.assertIn("Document ID: manual", text)
        self.assertIn("Page number: 1", text)
        self.assertIn("Native PDF text available: yes", text)
        self.assertEqual(text.count("Description finale unique"), 1)
        self.assertNotIn("PNG source:", text)

    def _assert_tile_request(self, requested_tile_ids):
        tool_use = SimpleNamespace(
            type="tool_use",
            id="tool-use-1",
            name="request_page_tiles",
            input={"tiles": requested_tile_ids},
        )
        first_response = SimpleNamespace(
            content=[
                SimpleNamespace(type="text", text="Analyse initiale"),
                tool_use,
            ],
            stop_reason="tool_use",
        )
        second_response = SimpleNamespace(
            content=[SimpleNamespace(type="text", text="Description consolidée")],
            stop_reason="end_turn",
        )

        _path, uploads, describe = self._generate(
            [first_response, second_response], has_extractable_text=False
        )

        self.assertEqual(describe.call_count, 2)
        second_messages = describe.call_args_list[1].args[0]
        self.assertEqual(second_messages[0], describe.call_args_list[0].args[0][0])
        self.assertIs(second_messages[1]["content"], first_response.content)
        tool_result = second_messages[2]["content"][0]
        self.assertEqual(tool_result["type"], "tool_result")
        self.assertEqual(tool_result["tool_use_id"], "tool-use-1")
        sent_images = [
            block for block in tool_result["content"] if block["type"] == "image"
        ]
        self.assertEqual(len(sent_images), len(requested_tile_ids))
        self.assertEqual(
            describe.call_args_list[1].kwargs["tool_choice"], {"type": "none"}
        )
        final_text = uploads[0][1].decode("utf-8")
        self.assertIn("Description consolidée", final_text)
        self.assertNotIn("Analyse initiale", final_text)

    def test_one_tile_request_preserves_context(self):
        self._assert_tile_request(["r01-c02"])

    def test_multiple_tile_request_preserves_context(self):
        self._assert_tile_request(["r01-c01", "r02-c02"])

    def test_truncated_response_is_not_written(self):
        response = SimpleNamespace(
            content=[SimpleNamespace(type="text", text="Réponse incomplète")],
            stop_reason="max_tokens",
        )
        full_image, tiles = self._page_inputs()
        with (
            patch.object(
                function_app, "describe_image_with_claude", return_value=response
            ),
            patch.object(function_app, "upload_blob") as upload,
        ):
            with self.assertRaisesRegex(RuntimeError, "tronquée"):
                function_app.generate_page_description(
                    object(),
                    "manual",
                    1,
                    "doc/page-00001",
                    full_image,
                    tiles,
                    True,
                )
        upload.assert_not_called()

    def test_describe_pages_detects_extractable_text_with_pymupdf(self):
        class Blob:
            def exists(self):
                return False

            def download_blob(self):
                return SimpleNamespace(readall=lambda: b"full-image")

        class Container:
            def get_blob_client(self, _path):
                return Blob()

            def list_blobs(self, **_kwargs):
                return []

        class Page:
            def __init__(self, text):
                self.text = text

            def get_text(self, mode, **kwargs):
                self.mode = mode
                self.kwargs = kwargs
                return self.text

        for extracted_text, expected in (("Texte PDF", True), ("   ", False)):
            with self.subTest(extracted_text=extracted_text):
                page = Page(extracted_text)
                doc = SimpleNamespace(load_page=lambda _index: page)
                with (
                    patch.dict(os.environ, {"ENABLE_IMAGE_DESCRIPTION": "true"}),
                    patch.object(
                        function_app,
                        "generate_page_description",
                        return_value="description.txt",
                    ) as generate,
                ):
                    result = function_app.describe_pages(
                        Container(),
                        "manual",
                        "doc",
                        1,
                        doc=doc,
                    )
                self.assertEqual(result, [(1, "description.txt")])
                self.assertEqual(page.mode, "text")
                self.assertEqual(page.kwargs, {"sort": True})
                self.assertEqual(
                    generate.call_args.kwargs["has_extractable_text"], expected
                )


class FileSearchMarkerTests(unittest.TestCase):
    marker_path = "document/page-00001-description.txt.file-search-indexed"
    expected_vector_store_id = "vs_test"

    def _check_marker(self, payload):
        container = InMemoryContainer(
            {self.marker_path: json.dumps(payload).encode("utf-8")}
        )
        with (
            patch.object(function_app, "run_with_blob_lock", return_value=True),
            patch.object(function_app, "upload_file_to_file_search") as upload,
        ):
            result = function_app.index_file_in_file_search_once(
                container,
                self.marker_path,
                lambda: b"description",
                "page-00001-description.txt",
                "text/plain",
                "endpoint",
                self.expected_vector_store_id,
            )
        upload.assert_not_called()
        return result

    def test_marker_accepts_file_dash_id(self):
        self.assertEqual(
            self._check_marker(
                {
                    "file_search_id": "file-abc123",
                    "vector_store_id": self.expected_vector_store_id,
                }
            ),
            "file-abc123",
        )

    def test_marker_accepts_assistant_dash_id(self):
        self.assertEqual(
            self._check_marker(
                {
                    "file_search_id": "assistant-abc123",
                    "vector_store_id": self.expected_vector_store_id,
                }
            ),
            "assistant-abc123",
        )

    def test_marker_accepts_id_without_known_prefix(self):
        self.assertEqual(
            self._check_marker(
                {
                    "file_search_id": "abc123",
                    "vector_store_id": self.expected_vector_store_id,
                }
            ),
            "abc123",
        )

    def test_marker_rejects_empty_file_search_id(self):
        self.assertIsNone(
            self._check_marker(
                {
                    "file_search_id": "",
                    "vector_store_id": self.expected_vector_store_id,
                }
            )
        )

    def test_marker_rejects_missing_file_search_id(self):
        self.assertIsNone(
            self._check_marker({"vector_store_id": self.expected_vector_store_id})
        )

    def test_marker_rejects_wrong_vector_store_id(self):
        self.assertIsNone(
            self._check_marker(
                {
                    "file_search_id": "assistant-abc123",
                    "vector_store_id": "vs_other",
                }
            )
        )


class QueuePipelineTests(unittest.TestCase):
    def setUp(self):
        self.source_blob = "entreprise/machine/manuel.pdf"
        self.source_etag = '"etag-1"'
        self.container = FakeContainer(self.source_etag)
        self.service = FakeBlobService(self.container)
        self.state = None
        self.saved_states = []

    def _state(self, page_count):
        return function_app.build_processing_state(
            "manuel", self.source_blob, self.source_etag, page_count
        )

    def _message(self, start_page=None, end_page=None, work_id="work-a"):
        payload = {
            "schema_version": 1,
            "document_id": "manuel",
            "source_blob": self.source_blob,
            "source_etag": self.source_etag,
            "work_id": work_id,
        }
        if start_page is not None:
            payload["start_page"] = start_page
            payload["end_page"] = end_page
        return FakeQueueMessage(payload)

    def _load_state(self, _client):
        return self.state, '"state-etag"'

    def _save_state(self, _client, state, **_kwargs):
        self.state = state
        self.saved_states.append(copy.deepcopy(state))
        return '"state-etag"'

    def _run(self, message, output, *, generate_side_effect=None):
        page_count = self.state["page_count"]
        with (
            patch.dict(
                os.environ,
                {
                    "ENABLE_IMAGE_DESCRIPTION": "true",
                    "DESCRIPTION_TEST_PAGE": "",
                    "DIAGLINK_BACKEND_BASE_URL": "https://app.diaglink.test",
                    "DIAGLINK_BACKEND_AUDIENCE": "api://test-api",
                },
            ),
            patch.object(
                function_app.BlobServiceClient,
                "from_connection_string",
                return_value=self.service,
            ),
            patch.object(function_app, "load_processing_state", side_effect=self._load_state),
            patch.object(function_app, "save_processing_state", side_effect=self._save_state),
            patch.object(
                function_app,
                "prepare_foundry_for_machine",
                return_value=("endpoint", "vs_test"),
            ),
            patch.object(function_app.fitz, "open", return_value=FakeDoc(page_count)),
            patch.object(
                function_app,
                "ensure_page_map",
                return_value=({}, '"page-map-etag"', False),
            ),
            patch.object(
                function_app,
                "generate_page_images",
                side_effect=generate_side_effect,
            ) as generate,
            patch.object(function_app, "describe_pages", return_value=[(1, "description")]),
            patch.object(function_app, "index_page_descriptions") as index_descriptions,
            patch.object(function_app, "index_source_pdf", return_value="file_pdf") as index_pdf,
            patch.object(function_app, "finalize_machine_claude_direct_config") as finalize,
        ):
            function_app.ProcessPdfIngestion(message, output)
        self.finalize_calls = finalize.call_args_list
        return generate, index_descriptions, index_pdf

    def test_restart_with_first_batch_completed_enqueues_second_without_reprocessing(self):
        self.state = self._state(15)
        self.state["batches"][0]["status"] = "completed"
        output = FakeQueueOutput()

        generate, _, index_pdf = self._run(self._message(1, 10), output)

        generate.assert_not_called()
        index_pdf.assert_not_called()
        queued = json.loads(output.value)
        self.assertEqual((queued["start_page"], queued["end_page"]), (11, 15))

    def test_duplicate_message_does_not_reindex_completed_document(self):
        self.state = self._state(5)
        first_output = FakeQueueOutput()
        _, _, first_index_pdf = self._run(self._message(1, 5), first_output)
        first_index_pdf.assert_called_once()
        self.assertEqual(self.state["status"], "completed")

        second_output = FakeQueueOutput()
        _, _, second_index_pdf = self._run(self._message(1, 5), second_output)
        second_index_pdf.assert_not_called()
        self.assertIsNone(second_output.value)

    def test_all_batches_completed_indexes_source_pdf_once(self):
        self.state = self._state(15)
        self.state["batches"][0]["status"] = "completed"
        output = FakeQueueOutput()

        _, _, index_pdf = self._run(self._message(11, 15), output)

        index_pdf.assert_called_once()
        self.assertTrue(self.state["source_pdf_indexed"])
        self.assertEqual(self.state["status"], "completed")
        self.assertEqual(len(self.finalize_calls), 1)
        self.assertEqual(
            self.finalize_calls[0].kwargs,
            {
                "entreprise": "entreprise",
                "machine": "machine",
                "project_endpoint": "endpoint",
                "vector_store_id": "vs_test",
            },
        )
        self.assertIsNone(output.value)

    def test_exception_during_batch_leaves_batch_incomplete(self):
        self.state = self._state(5)
        output = FakeQueueOutput()

        with self.assertRaisesRegex(RuntimeError, "rendu interrompu"):
            self._run(
                self._message(1, 5),
                output,
                generate_side_effect=RuntimeError("rendu interrompu"),
            )

        self.assertEqual(self.state["batches"][0]["status"], "processing")
        self.assertEqual(self.state["batches"][0]["owner_work_id"], "work-a")
        self.assertNotEqual(self.state["status"], "completed")
        self.assertEqual(self.state["last_error"], "rendu interrompu")

    def test_other_work_id_does_not_process_or_reset_owned_batch(self):
        self.state = self._state(5)
        with patch.object(
            function_app, "save_processing_state", side_effect=self._save_state
        ):
            result, self.state, _, _ = function_app.claim_batch_for_work(
                object(), self.state, '"state-etag"', 1, 5, "work-a"
            )
        self.assertEqual(result, "owned")

        output = FakeQueueOutput()
        generate, index_descriptions, index_pdf = self._run(
            self._message(1, 5, work_id="work-b"), output
        )

        self.assertEqual(self.state["batches"][0]["status"], "processing")
        self.assertEqual(self.state["batches"][0]["owner_work_id"], "work-a")
        generate.assert_not_called()
        index_descriptions.assert_not_called()
        index_pdf.assert_not_called()
        self.assertIsNone(output.value)

    def test_etag_conflict_reloads_winner_without_resetting_batch(self):
        stale_state = self._state(5)
        winner_state = self._state(5)
        winner_state["batches"][0]["status"] = "processing"
        winner_state["batches"][0]["owner_work_id"] = "work-a"

        with (
            patch.object(
                function_app,
                "save_processing_state",
                side_effect=function_app.ResourceModifiedError("conflit"),
            ),
            patch.object(
                function_app,
                "load_processing_state",
                return_value=(winner_state, '"winner-etag"'),
            ),
        ):
            result, state, _, batch = function_app.claim_batch_for_work(
                object(), stale_state, '"stale-etag"', 1, 5, "work-b"
            )

        self.assertEqual(result, "concurrent")
        self.assertIs(state, winner_state)
        self.assertEqual(batch["status"], "processing")
        self.assertEqual(batch["owner_work_id"], "work-a")

    def test_retry_with_same_work_id_resumes_owned_batch(self):
        self.state = self._state(5)
        self.state["batches"][0]["status"] = "processing"
        self.state["batches"][0]["owner_work_id"] = "work-a"
        output = FakeQueueOutput()

        generate, index_descriptions, index_pdf = self._run(
            self._message(1, 5, work_id="work-a"), output
        )

        generate.assert_called_once()
        index_descriptions.assert_called_once()
        index_pdf.assert_called_once()
        self.assertEqual(self.state["batches"][0]["status"], "completed")
        self.assertEqual(self.state["batches"][0]["owner_work_id"], "work-a")

    def test_retry_reuses_non_prefixed_page_marker_and_continues(self):
        self.state = self._state(5)
        self.state["batches"][0]["status"] = "processing"
        self.state["batches"][0]["owner_work_id"] = "work-a"
        page_1 = "entreprise/machine/manuel/page-00001-description.txt"
        page_2 = "entreprise/machine/manuel/page-00002-description.txt"
        marker_1 = f"{page_1}.file-search-indexed"
        container = InMemoryContainer(
            {
                self.source_blob: b"pdf",
                page_1: b"description page 1",
                page_2: b"description page 2",
                marker_1: json.dumps(
                    {
                        "file_search_id": "assistant-existing-page-1",
                        "vector_store_id": "vs_test",
                    }
                ).encode("utf-8"),
            },
            etag=self.source_etag,
        )
        service = FakeBlobService(container)

        def run_action(_lock, marker_check, action, **_kwargs):
            if marker_check():
                return True
            action()
            return True

        with (
            patch.dict(
                os.environ,
                {
                    "ENABLE_IMAGE_DESCRIPTION": "true",
                    "DESCRIPTION_TEST_PAGE": "",
                    "DIAGLINK_BACKEND_BASE_URL": "https://app.diaglink.test",
                    "DIAGLINK_BACKEND_AUDIENCE": "api://test-api",
                },
            ),
            patch.object(
                function_app.BlobServiceClient,
                "from_connection_string",
                return_value=service,
            ),
            patch.object(function_app, "load_processing_state", side_effect=self._load_state),
            patch.object(function_app, "save_processing_state", side_effect=self._save_state),
            patch.object(
                function_app,
                "prepare_foundry_for_machine",
                return_value=("endpoint", "vs_test"),
            ),
            patch.object(function_app.fitz, "open", return_value=FakeDoc(5)),
            patch.object(
                function_app,
                "ensure_page_map",
                return_value=({}, '"page-map-etag"', False),
            ),
            patch.object(function_app, "generate_page_images"),
            patch.object(
                function_app,
                "describe_pages",
                return_value=[(1, page_1), (2, page_2)],
            ),
            patch.object(
                function_app,
                "upload_file_to_file_search",
                return_value="assistant-new-page-2",
            ) as upload,
            patch.object(function_app, "run_with_blob_lock", side_effect=run_action),
            patch.object(function_app, "index_source_pdf", return_value="file-pdf"),
            patch.object(function_app, "finalize_machine_claude_direct_config"),
        ):
            function_app.ProcessPdfIngestion(
                self._message(1, 5, work_id="work-a"), FakeQueueOutput()
            )

        upload.assert_called_once()
        self.assertEqual(upload.call_args.kwargs["file_name"], page_2.rsplit("/", 1)[-1])
        self.assertNotIn(page_1, container.downloads)
        self.assertIn(page_2, container.downloads)
        self.assertEqual(self.state["batches"][0]["status"], "completed")
        self.assertEqual(self.state["batches"][0]["owner_work_id"], "work-a")

    def test_other_work_id_cannot_complete_owned_batch(self):
        self.state = self._state(5)
        self.state["batches"][0]["status"] = "processing"
        self.state["batches"][0]["owner_work_id"] = "work-a"

        with (
            patch.object(
                function_app,
                "load_processing_state",
                return_value=(self.state, '"state-etag"'),
            ),
            patch.object(function_app, "save_processing_state") as save_state,
        ):
            with self.assertRaisesRegex(RuntimeError, "ne possède plus le lot"):
                function_app.complete_batch_for_work(
                    object(), 1, 5, "work-b"
                )

        save_state.assert_not_called()
        self.assertEqual(self.state["batches"][0]["status"], "processing")
        self.assertEqual(self.state["batches"][0]["owner_work_id"], "work-a")

    def test_image_descriptions_disabled_rejects_ingestion(self):
        self.state = self._state(5)
        with patch.dict(
            os.environ,
            {"ENABLE_IMAGE_DESCRIPTION": "false", "DESCRIPTION_TEST_PAGE": ""},
        ):
            with self.assertRaisesRegex(RuntimeError, "ENABLE_IMAGE_DESCRIPTION"):
                function_app.ProcessPdfIngestion(
                    self._message(1, 5), FakeQueueOutput()
                )
        self.assertNotEqual(self.state["status"], "completed")

    def test_description_test_page_rejects_normal_ingestion(self):
        self.state = self._state(5)
        with patch.dict(
            os.environ,
            {"ENABLE_IMAGE_DESCRIPTION": "true", "DESCRIPTION_TEST_PAGE": "3"},
        ):
            with self.assertRaisesRegex(RuntimeError, "DESCRIPTION_TEST_PAGE"):
                function_app.ProcessPdfIngestion(
                    self._message(1, 5), FakeQueueOutput()
                )
        self.assertNotEqual(self.state["status"], "completed")


if __name__ == "__main__":
    unittest.main()
