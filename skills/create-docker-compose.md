# Docker Image Build and Update

Commands to build, run, and update the ArbitrageScanner Docker setup.

## Prerequisites

1. Copy `.env.example` to `.env` and set `MONGO_DB_CONNECTION_STRING`.
2. Docker Desktop or Docker Engine must be running.

---

## Build the image

```bash
docker compose build
```

## Build without cache (force full rebuild)

```bash
docker compose build --no-cache
```

---

## Start all services (detached)

```bash
docker compose up -d
```

## Build and start in one step

```bash
docker compose up -d --build
```

---

## Update the app image and restart only that service

```bash
docker compose up -d --build --force-recreate arbitragebusiness
```

## View live logs

```bash
docker compose logs -f arbitragebusiness
```

---

## Stop all services

```bash
docker compose down
```

## Stop and remove volumes (full clean)

```bash
docker compose down -v
```

---

## Kafka

The broker has no published port. Inspect topics from inside the container, e.g.
`docker compose exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list`.

---

## Notes

- `Kafka__BootstrapServers=kafka:9092` is set automatically in `docker-compose.yml`; do not change it unless the service name changes. The `kafka-init` job creates the `spreads` and `spreads.dlq` topics.
- `MongoDb_ConnectionString` is read from `.env`; it must point to your CosmosDB or MongoDB instance accessible from the container.
- The Dockerfile build context is the solution root (`docker-compose.yml` and `ArbitrageBusiness/Dockerfile`). Always run compose commands from the solution root.
