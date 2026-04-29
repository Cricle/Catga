#!/usr/bin/env bash

set -euo pipefail

MODE="${1:-all}"
AUTO_START_BACKENDS="${CATGA_AUTO_START_BACKENDS:-1}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ -x /root/.dotnet/dotnet ]]; then
  DOTNET_CMD="/root/.dotnet/dotnet"
else
  DOTNET_CMD="dotnet"
fi

PROJECT="tests/Catga.Tests/Catga.Tests.csproj"
COMMON_ARGS=(
  test "$PROJECT"
  -f net8.0
  --no-restore
  -m:1
  -p:UseSharedCompilation=false
  -p:BuildInParallel=false
  -p:RunAnalyzers=false
)

CORE_FILTER='FullyQualifiedName~FlowDslModernE2ETests|FullyQualifiedName~RedisFlowStoreTests|FullyQualifiedName~NatsFlowStoreTests|FullyQualifiedName~CqrsE2ETests|FullyQualifiedName~MediatorPipelineE2ETests|FullyQualifiedName~CatgaMediatorCoverageTests|FullyQualifiedName~MediatorE2ETests|FullyQualifiedName~MediatorAdvancedE2ETests|FullyQualifiedName~OutboxInboxE2ETests'
BACKENDS_FILTER='FullyQualifiedName~RedisTransportIntegrationTests|FullyQualifiedName~RedisPersistenceIntegrationTests|FullyQualifiedName~RedisTransportE2ETests|FullyQualifiedName~RedisPersistenceE2ETests|FullyQualifiedName~RedisNewFeaturesE2ETests|FullyQualifiedName~RedisSpecificFunctionalityTests|FullyQualifiedName~RedisSubscriptionLockE2ETests|FullyQualifiedName~NatsPersistenceIntegrationTests|FullyQualifiedName~NatsNewFeaturesE2ETests|FullyQualifiedName~NatsTransportE2ETests|FullyQualifiedName~NatsConnectionManagementTests|FullyQualifiedName~NatsJetStreamFunctionalityTests|FullyQualifiedName~NatsKVFunctionalityTests|FullyQualifiedName~NatsMessageFunctionalityTests|FullyQualifiedName~NatsPersistenceE2ETests|FullyQualifiedName~NatsSubscriptionLockE2ETests|FullyQualifiedName~NatsCrossComponentE2ETests|FullyQualifiedName~NatsFailoverE2ETests'
ALL_FILTER="${CORE_FILTER}|${BACKENDS_FILTER}"

check_port() {
  local host="$1"
  local port="$2"
  bash -lc "exec 3<>/dev/tcp/${host}/${port}" >/dev/null 2>&1
}

docker_available() {
  command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1
}

wait_for_port() {
  local host="$1"
  local port="$2"
  local label="$3"

  for _ in {1..40}; do
    if check_port "$host" "$port"; then
      return 0
    fi
    sleep 0.5
  done

  echo "${label} did not become ready on ${host}:${port}" >&2
  return 1
}

ensure_redis() {
  if check_port 127.0.0.1 6379; then
    return 0
  fi

  if [[ "$AUTO_START_BACKENDS" != "1" ]]; then
    return 1
  fi

  if ! docker_available; then
    return 1
  fi

  if docker ps -a --format '{{.Names}}' | grep -Fxq 'catga-redis-test'; then
    docker start catga-redis-test >/dev/null
  else
    docker run -d --name catga-redis-test -p 6379:6379 redis:7-alpine >/dev/null
  fi

  wait_for_port 127.0.0.1 6379 "Redis"
}

ensure_nats() {
  if check_port 127.0.0.1 4222; then
    return 0
  fi

  if [[ "$AUTO_START_BACKENDS" != "1" ]]; then
    return 1
  fi

  if ! docker_available; then
    return 1
  fi

  if docker ps -a --format '{{.Names}}' | grep -Fxq 'catga-nats-test'; then
    docker start catga-nats-test >/dev/null
  else
    docker run -d --name catga-nats-test -p 4222:4222 -p 8222:8222 nats:2.11-alpine -js -m 8222 >/dev/null
  fi

  wait_for_port 127.0.0.1 4222 "NATS"
}

require_backends() {
  if ! ensure_redis; then
    echo "Redis is not reachable on 127.0.0.1:6379" >&2
    echo "Start local Redis first, or keep CATGA_AUTO_START_BACKENDS=1 and ensure Docker is available." >&2
    exit 1
  fi

  if ! ensure_nats; then
    echo "NATS is not reachable on 127.0.0.1:4222" >&2
    echo "Start local NATS first, or keep CATGA_AUTO_START_BACKENDS=1 and ensure Docker is available." >&2
    exit 1
  fi
}

case "$MODE" in
  core)
    require_backends
    FILTER="$CORE_FILTER"
    ;;
  backends)
    require_backends
    FILTER="$BACKENDS_FILTER"
    ;;
  all)
    require_backends
    FILTER="$ALL_FILTER"
    ;;
  *)
    echo "Usage: scripts/test-fast-regression.sh [core|backends|all]" >&2
    exit 2
    ;;
esac

exec "$DOTNET_CMD" "${COMMON_ARGS[@]}" --filter "$FILTER"
