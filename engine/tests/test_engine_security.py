import os
import unittest
from fastapi.testclient import TestClient
from breach_engine.api import app, resolve_workspace

class EngineSecurityTests(unittest.TestCase):
    def setUp(self):
        self.secret = "TestSecret_AlphaOmega_2026"
        os.environ["ROGUE_ENGINE_SECRET"] = self.secret
        self.client = TestClient(app)

    def tearDown(self):
        os.environ.pop("ROGUE_ENGINE_SECRET", None)

    def test_health_endpoint_publicly_accessible(self):
        resp = self.client.get("/health")
        self.assertEqual(resp.status_code, 200)
        self.assertTrue(resp.json().get("ok"))

    def test_openapi_docs_disabled(self):
        # Without secret: blocked with 403 Forbidden
        resp = self.client.get("/openapi.json")
        self.assertEqual(resp.status_code, 403)

        # Even with secret: returns 404 Not Found because OpenAPI docs are disabled
        resp_with_secret = self.client.get("/openapi.json", headers={"X-Engine-Secret": self.secret})
        self.assertEqual(resp_with_secret.status_code, 404)

        resp_docs = self.client.get("/docs", headers={"X-Engine-Secret": self.secret})
        self.assertEqual(resp_docs.status_code, 404)

    def test_attacks_endpoint_rejects_missing_secret(self):
        resp = self.client.get("/attacks")
        self.assertEqual(resp.status_code, 403)
        self.assertIn("Forbidden", resp.json().get("detail", ""))

    def test_attacks_endpoint_rejects_invalid_secret(self):
        resp = self.client.get("/attacks", headers={"X-Engine-Secret": "WrongSecret"})
        self.assertEqual(resp.status_code, 403)

    def test_attacks_endpoint_accepts_valid_secret(self):
        resp = self.client.get("/attacks", headers={"X-Engine-Secret": self.secret})
        self.assertEqual(resp.status_code, 200)
        self.assertIsInstance(resp.json(), list)

    def test_evaluate_rejects_unauthorized_direct_probe(self):
        payload = {
            "participant_id": "test_kiosk",
            "submission_id": "sub_test",
            "attack_ids": [0]
        }
        resp = self.client.post("/evaluate/fast", json=payload)
        self.assertEqual(resp.status_code, 403)

if __name__ == "__main__":
    unittest.main()
