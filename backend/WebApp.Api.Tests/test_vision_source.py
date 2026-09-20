"""Offline regression for the external Azure Function, without importing its Azure dependencies.

Run: python test_vision_source.py PATH_TO_FUNCTION_APP
Executes the actual analyze_page tail from client creation through JSON serialization.
"""
import ast
import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

SOURCE = Path(sys.argv.pop(1))


class VisionSourceTests(unittest.TestCase):
    def run_source(self, model):
        tree = ast.parse(SOURCE.read_text(encoding="utf-8-sig"))
        function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "analyze_page")
        start = next(i for i, n in enumerate(function.body) if isinstance(n, ast.Assign)
                     and any(isinstance(t, ast.Name) and t.id == "client" for t in n.targets))
        function.body = function.body[start:]
        function.decorator_list = []
        function.args = ast.arguments(posonlyargs=[], args=[], kwonlyargs=[], kw_defaults=[], defaults=[])
        function.returns = None
        module = ast.fix_missing_locations(ast.Module(body=[function], type_ignores=[]))
        response = SimpleNamespace(content=[SimpleNamespace(type="text", text="answer")],
                                   usage=SimpleNamespace(input_tokens=9708, output_tokens=653),
                                   stop_reason="end_turn")
        if model is not None:
            response.model = model
        create = Mock(return_value=response)
        client = Mock(return_value=SimpleNamespace(messages=SimpleNamespace(create=create)))
        env = {"FOUNDRY_ANTHROPIC_API_KEY": "fake", "FOUNDRY_ANTHROPIC_ENDPOINT": "https://example.invalid",
               "FOUNDRY_CLAUDE_DEPLOYMENT": "explicit-deployment"}
        scope = dict(AnthropicFoundry=client, os=SimpleNamespace(environ=env), json=json,
                     func=SimpleNamespace(HttpResponse=lambda body, **kwargs: json.loads(body)),
                     content=[], page_number=1, images_sent=1)
        exec(compile(module, str(SOURCE), "exec"), scope)
        result = scope["analyze_page"]()
        self.assertEqual("explicit-deployment", create.call_args.kwargs["model"])
        self.assertEqual("Anthropic", result["provider"])
        self.assertEqual("explicit-deployment", result["deployment"])
        self.assertEqual({"input_tokens": 9708, "output_tokens": 653}, result["usage"])
        return result

    def test_uses_response_model_independently_of_deployment(self):
        self.assertEqual("actual-response-model", self.run_source("actual-response-model")["model"])

    def test_missing_response_model_is_null_without_fallback(self):
        self.assertIsNone(self.run_source(None)["model"])


if __name__ == "__main__":
    unittest.main()
