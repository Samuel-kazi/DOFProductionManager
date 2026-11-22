# API Specification

Implemented endpoints. The full target API is in
[TECHNICAL_SPECIFICATION.md](TECHNICAL_SPECIFICATION.md#part-5--api-specification). The live
OpenAPI document is served at `/swagger` in Development.

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| GET | `/health` | Anonymous | Readiness, including database connectivity |
| GET | `/health/live` | Anonymous | Liveness (process is running) |
| GET | `/api/v1/system/info` | Anonymous | API name, version, environment, server UTC time, default timezone |

Authentication arrives in Phase 1. Health and system info will stay anonymous; all other endpoints
will require authentication.
