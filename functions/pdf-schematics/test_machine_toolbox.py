"""Tests unitaires de la Toolbox File Search machine, sans appel Azure réel."""

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


class FakeBlobClient:
    def __init__(self, container, blob_name):
        self.container = container
        self.blob_name = blob_name

    def download_blob(self):
        if self.blob_name not in self.container.blobs:
            raise function_app.ResourceNotFoundError("blob absent")
        data = self.container.blobs[self.blob_name]
        return SimpleNamespace(readall=lambda: data)

    def upload_blob(self, data, **_kwargs):
        self.container.blobs[self.blob_name] = data

    def delete_blob(self):
        self.container.blobs.pop(self.blob_name, None)


class FakeContainer:
    def __init__(self):
        self.blobs = {}

    def get_blob_client(self, blob_name):
        return FakeBlobClient(self, blob_name)

    def close(self):
        pass


class FakeBlobService:
    def __init__(self, container):
        self.container = container

    def get_container_client(self, _name):
        return self.container


class ForbiddenAgentOperations:
    def __init__(self):
        self.access_count = 0

    def __getattr__(self, name):
        self.access_count += 1
        raise AssertionError(f"La logique Toolbox ne doit pas accéder à agents.{name}")


class FakeToolboxes:
    def __init__(self):
        self.versions = {}
        self.create_calls = []

    def add_version(
        self,
        name,
        version,
        vector_store_id,
        max_num_results=function_app.FILE_SEARCH_MAX_RESULTS,
    ):
        tool_kwargs = {
            "vector_store_ids": [vector_store_id],
        }

        if max_num_results is not None:
            tool_kwargs["max_num_results"] = max_num_results

        toolbox_version = SimpleNamespace(
            name=name,
            version=str(version),
            tools=[function_app.FileSearchToolboxTool(**tool_kwargs)],
        )

        self.versions[(name, str(version))] = toolbox_version
        return toolbox_version

    def get_version(self, name, version):
        try:
            return self.versions[(name, str(version))]
        except KeyError as exc:
            raise function_app.ResourceNotFoundError("version absente") from exc

    def list_versions(self, name):
        return [
            version
            for (candidate_name, _), version in self.versions.items()
            if candidate_name == name
        ]

    def create_version(self, *, name, description, tools):
        version = str(
            1 + sum(1 for candidate_name, _ in self.versions if candidate_name == name)
        )
        self.create_calls.append(
            {"name": name, "description": description, "tools": tools}
        )
        created = SimpleNamespace(name=name, version=version, tools=tools)
        self.versions[(name, version)] = created
        return created


class FakeProjectClient:
    def __init__(self):
        self.toolboxes = FakeToolboxes()
        self.agents = ForbiddenAgentOperations()

    def close(self):
        pass


class MachineToolboxTests(unittest.TestCase):
    endpoint = "https://foundry.invalid/api/projects/entreprise-a"
    entreprise = "entreprise-a"
    machine = "Machine 01"

    def setUp(self):
        self.container = FakeContainer()
        self.blob_service = FakeBlobService(self.container)
        self.project_client = FakeProjectClient()

        def run_action(_lock, marker_check, action, **_kwargs):
            if marker_check():
                return True
            action()
            return True

        self.patches = (
            patch.object(
                function_app.BlobServiceClient,
                "from_connection_string",
                return_value=self.blob_service,
            ),
            patch.object(
                function_app,
                "AIProjectClient",
                return_value=self.project_client,
            ),
            patch.object(
                function_app,
                "DefaultAzureCredential",
                return_value=object(),
            ),
            patch.object(function_app, "run_with_blob_lock", side_effect=run_action),
        )
        for current_patch in self.patches:
            current_patch.start()

    def tearDown(self):
        for current_patch in reversed(self.patches):
            current_patch.stop()

    def marker_path(self, machine=None):
        machine = machine or self.machine
        return f"{self.entreprise}/{machine}/.foundry/toolbox.json"

    def read_marker(self, machine=None):
        return json.loads(
            self.container.blobs[self.marker_path(machine)].decode("utf-8")
        )

    def create_toolbox(self, vector_store_id="vs_machine", machine=None):
        return function_app.get_or_create_machine_toolbox(
            project_endpoint=self.endpoint,
            entreprise=self.entreprise,
            machine=machine or self.machine,
            vector_store_id=vector_store_id,
        )

    def test_creates_toolbox_when_marker_is_absent(self):
        result = self.create_toolbox()

        self.assertEqual(len(self.project_client.toolboxes.create_calls), 1)
        tool = self.project_client.toolboxes.create_calls[0]["tools"][0]
        self.assertEqual(tool.vector_store_ids, ["vs_machine"])
        self.assertEqual(tool.max_num_results, function_app.FILE_SEARCH_MAX_RESULTS)
        self.assertEqual(result["toolbox_name"], "TB-Machine-01")

    def test_legacy_toolbox_without_max_results_is_not_reused(self):
        name = function_app.normalize_toolbox_name(self.machine)

        self.project_client.toolboxes.add_version(
            name,
            "7",
            "vs_machine",
            max_num_results=None,
        )

        marker = {
            "toolbox_name": name,
            "toolbox_version": "7",
            "vector_store_id": "vs_machine",
            "mcp_endpoint": f"{self.endpoint}/toolboxes/{name}/versions/7/mcp?api-version=v1",
        }
        self.container.blobs[self.marker_path()] = json.dumps(marker).encode("utf-8")

        result = self.create_toolbox()

        self.assertNotEqual(result["toolbox_version"], "7")
        self.assertEqual(len(self.project_client.toolboxes.create_calls), 1)

        tool = self.project_client.toolboxes.create_calls[0]["tools"][0]
        self.assertEqual(
            tool.max_num_results,
            function_app.FILE_SEARCH_MAX_RESULTS,
        )

    def test_reuses_remote_toolbox_when_marker_is_valid(self):
        name = function_app.normalize_toolbox_name(self.machine)
        self.project_client.toolboxes.add_version(name, "7", "vs_machine")
        marker = {
            "toolbox_name": name,
            "toolbox_version": "7",
            "vector_store_id": "vs_machine",
            "mcp_endpoint": f"{self.endpoint}/toolboxes/{name}/versions/7/mcp?api-version=v1",
        }
        self.container.blobs[self.marker_path()] = json.dumps(marker).encode("utf-8")

        result = self.create_toolbox()

        self.assertEqual(result, marker)
        self.assertEqual(self.project_client.toolboxes.create_calls, [])

    def test_second_pass_does_not_create_another_version(self):
        first = self.create_toolbox()
        second = self.create_toolbox()

        self.assertEqual(first, second)
        self.assertEqual(len(self.project_client.toolboxes.create_calls), 1)

    def test_marker_contains_name_version_vector_store_and_mcp_endpoint(self):
        result = self.create_toolbox()
        marker = self.read_marker()

        self.assertEqual(marker, result)
        self.assertEqual(
            set(marker),
            {"toolbox_name", "toolbox_version", "vector_store_id", "mcp_endpoint"},
        )
        self.assertNotIn("token", json.dumps(marker).lower())
        self.assertNotIn("key", json.dumps(marker).lower())

    def test_different_vector_store_creates_new_version_and_updates_marker(self):
        first = self.create_toolbox("vs_first")
        second = self.create_toolbox("vs_second")

        self.assertNotEqual(first["toolbox_version"], second["toolbox_version"])
        self.assertEqual(len(self.project_client.toolboxes.create_calls), 2)
        self.assertEqual(self.read_marker()["vector_store_id"], "vs_second")

    def test_two_machines_use_isolated_names_and_markers(self):
        first = self.create_toolbox(machine="Machine 01")
        second = self.create_toolbox(machine="Machine 02")

        self.assertNotEqual(first["toolbox_name"], second["toolbox_name"])
        self.assertIn(self.marker_path("Machine 01"), self.container.blobs)
        self.assertIn(self.marker_path("Machine 02"), self.container.blobs)

    def test_toolbox_logic_never_accesses_hosted_agents(self):
        self.create_toolbox()

        self.assertEqual(self.project_client.agents.access_count, 0)


class ClaudeDirectProvisioningTests(unittest.TestCase):
    def test_prepare_uses_project_vector_store_and_toolbox_only(self):
        call_order = []

        with (
            patch.object(
                function_app,
                "get_or_create_foundry_project",
                side_effect=lambda entreprise: (
                    call_order.append("project") or "project-endpoint"
                ),
            ) as project,
            patch.object(
                function_app,
                "get_or_create_machine_vector_store",
                side_effect=lambda **_kwargs: (
                    call_order.append("vector-store") or "vs_machine"
                ),
            ) as vector_store,
            patch.object(
                function_app,
                "get_or_create_machine_toolbox",
                side_effect=lambda **_kwargs: (
                    call_order.append("toolbox")
                    or {
                        "toolbox_name": "TB-machine-a",
                        "toolbox_version": "1",
                        "vector_store_id": "vs_machine",
                    }
                ),
            ) as toolbox,
        ):
            result = function_app.prepare_foundry_for_machine(
                "entreprise-a", "machine-a"
            )

        self.assertEqual(result, ("project-endpoint", "vs_machine"))
        self.assertEqual(call_order, ["project", "vector-store", "toolbox"])
        project.assert_called_once_with("entreprise-a")
        vector_store.assert_called_once_with(
            project_endpoint="project-endpoint",
            entreprise="entreprise-a",
            machine="machine-a",
        )
        toolbox.assert_called_once_with(
            project_endpoint="project-endpoint",
            entreprise="entreprise-a",
            machine="machine-a",
            vector_store_id="vs_machine",
        )
    def test_prepare_remains_idempotent_on_following_batches(self):
        with (
            patch.object(
                function_app,
                "get_or_create_foundry_project",
                return_value="project-endpoint",
            ) as project,
            patch.object(
                function_app,
                "get_or_create_machine_vector_store",
                return_value="vs_machine",
            ) as vector_store,
            patch.object(
                function_app,
                "get_or_create_machine_toolbox",
                return_value={"toolbox_version": "1"},
            ) as toolbox,
        ):
            first = function_app.prepare_foundry_for_machine(
                "entreprise-a", "machine-a"
            )
            second = function_app.prepare_foundry_for_machine(
                "entreprise-a", "machine-a"
            )

        self.assertEqual(first, second)
        self.assertEqual(project.call_count, 2)
        self.assertEqual(vector_store.call_count, 2)
        self.assertEqual(toolbox.call_count, 2)


class DiagLinkFinalizationCallbackTests(unittest.TestCase):
    def setUp(self):
        self.environment = {
            "DIAGLINK_BACKEND_BASE_URL": "https://app.diaglink.test",
            "DIAGLINK_BACKEND_AUDIENCE": "api://00000000-0000-0000-0000-000000000001",
        }
        credential = Mock()
        credential.get_token.return_value = SimpleNamespace(token="test-token")
        self.credential = credential

    def test_callback_uses_managed_identity_and_expected_payload(self):
        response = Mock(status_code=200)
        response.json.return_value = {"status": "updated"}
        with (
            patch.dict(os.environ, self.environment),
            patch.object(
                function_app, "DefaultAzureCredential", return_value=self.credential
            ),
            patch.object(function_app.requests, "post", return_value=response) as post,
        ):
            function_app.finalize_machine_claude_direct_config(
                entreprise="entreprise-a",
                machine="machine-a",
                project_endpoint=(
                    "https://foundry.services.ai.azure.com/api/projects/entreprise-a"
                ),
                vector_store_id="vs_machine",
            )

        self.credential.get_token.assert_called_once_with(
            "api://00000000-0000-0000-0000-000000000001/.default"
        )
        self.credential.close.assert_called_once_with()
        post.assert_called_once()
        self.assertEqual(
            post.call_args.args[0],
            "https://app.diaglink.test/api/internal/document-ingestion/finalize",
        )
        self.assertEqual(
            post.call_args.kwargs["json"],
            {
                "blobPrefix": "entreprise-a/machine-a",
                "projectEndpoint": (
                    "https://foundry.services.ai.azure.com/api/projects/entreprise-a"
                ),
                "vectorStoreId": "vs_machine",
            },
        )
        self.assertEqual(post.call_args.kwargs["timeout"], (5, 30))
        self.assertEqual(
            post.call_args.kwargs["headers"]["Authorization"], "Bearer test-token"
        )

    def test_callback_accepts_idempotent_response(self):
        response = Mock(status_code=200)
        response.json.return_value = {"status": "already_finalized"}
        with (
            patch.dict(os.environ, self.environment),
            patch.object(
                function_app, "DefaultAzureCredential", return_value=self.credential
            ),
            patch.object(function_app.requests, "post", return_value=response),
        ):
            function_app.finalize_machine_claude_direct_config(
                "entreprise-a",
                "machine-a",
                "https://foundry.services.ai.azure.com/api/projects/entreprise-a",
                "vs_machine",
            )

    def test_callback_retries_transient_responses_only(self):
        unavailable = Mock(status_code=503)
        success = Mock(status_code=200)
        success.json.return_value = {"status": "updated"}
        with (
            patch.dict(os.environ, self.environment),
            patch.object(
                function_app, "DefaultAzureCredential", return_value=self.credential
            ),
            patch.object(
                function_app.requests,
                "post",
                side_effect=[unavailable, unavailable, success],
            ) as post,
            patch.object(function_app.time, "sleep") as sleep,
        ):
            function_app.finalize_machine_claude_direct_config(
                "entreprise-a",
                "machine-a",
                "https://foundry.services.ai.azure.com/api/projects/entreprise-a",
                "vs_machine",
            )

        self.assertEqual(post.call_count, 3)
        self.assertEqual(sleep.call_count, 2)

    def test_callback_does_not_retry_configuration_conflict(self):
        conflict = Mock(status_code=409)
        with (
            patch.dict(os.environ, self.environment),
            patch.object(
                function_app, "DefaultAzureCredential", return_value=self.credential
            ),
            patch.object(function_app.requests, "post", return_value=conflict) as post,
        ):
            with self.assertRaisesRegex(RuntimeError, "HTTP 409"):
                function_app.finalize_machine_claude_direct_config(
                    "entreprise-a",
                    "machine-a",
                    "https://foundry.services.ai.azure.com/api/projects/entreprise-a",
                    "vs_machine",
                )

        post.assert_called_once()


if __name__ == "__main__":
    unittest.main()
