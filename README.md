# Opensimulator MQTT Module

Standalone OpenSimulator region-module project for cross-simulator MQTT messaging, external integrations, and region telemetry.

**Status:** M0 diagnostic proof of concept; no MQTT functionality yet.

- [Project specification](PROJECT_SPEC.md)
- [Development plan](DEVELOPMENT_PLAN.md)
- [First Windows test](docs/M0-TESTING.md)

The initial module is deliberately limited to startup/shutdown logging. It requires no changes to YEngine or OpenSimulator core. TLS support is planned but optional; local testing will use plaintext MQTT.
