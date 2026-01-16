#!/bin/bash
# Run a 3-node Catga cluster locally for testing
# Usage: ./run-cluster.sh [nodes] [base-port] [transport] [persistence]

NODES=${1:-3}
BASE_PORT=${2:-5000}
TRANSPORT=${3:-redis}
PERSISTENCE=${4:-redis}
REDIS_CONN=${5:-localhost:6379}

echo -e "\033[32mStarting $NODES-node Catga cluster...\033[0m"
echo -e "\033[36mTransport: $TRANSPORT, Persistence: $PERSISTENCE\033[0m"
echo ""

PIDS=()

cleanup() {
    echo ""
    echo -e "\033[33mStopping all nodes...\033[0m"
    for pid in "${PIDS[@]}"; do
        kill $pid 2>/dev/null
    done
    wait
    echo -e "\033[32mAll nodes stopped.\033[0m"
    exit 0
}

trap cleanup SIGINT SIGTERM

for ((i=0; i<NODES; i++)); do
    PORT=$((BASE_PORT + i))
    NODE_ID="node$i"
    
    # Build member list (all other nodes)
    MEMBERS=""
    for ((j=0; j<NODES; j++)); do
        if [ $j -ne $i ]; then
            if [ -n "$MEMBERS" ]; then
                MEMBERS="$MEMBERS,"
            fi
            MEMBERS="${MEMBERS}http://localhost:$((BASE_PORT + j))"
        fi
    done
    
    LOCAL_ENDPOINT="http://localhost:$PORT"
    
    echo -e "\033[33mStarting $NODE_ID on port $PORT...\033[0m"
    echo -e "\033[90m  Local: $LOCAL_ENDPOINT\033[0m"
    echo -e "\033[90m  Members: $MEMBERS\033[0m"
    
    # Set environment variables and start node
    export ASPNETCORE_URLS="http://0.0.0.0:$PORT"
    export Cluster__LocalNodeEndpoint="$LOCAL_ENDPOINT"
    
    # Set member endpoints
    idx=0
    IFS=',' read -ra MEMBER_ARRAY <<< "$MEMBERS"
    for member in "${MEMBER_ARRAY[@]}"; do
        export "Cluster__Members__$idx=$member"
        ((idx++))
    done
    
    dotnet run --project examples/OrderSystem \
        -- \
        --cluster \
        --transport "$TRANSPORT" \
        --persistence "$PERSISTENCE" \
        --redis "$REDIS_CONN" \
        --node-id "$NODE_ID" \
        --port "$PORT" \
        > "logs-$NODE_ID.txt" 2>&1 &
    
    PIDS+=($!)
    sleep 0.5
done

echo ""
echo -e "\033[32mAll nodes started! Press Ctrl+C to stop all nodes.\033[0m"
echo ""
echo -e "\033[36mEndpoints:\033[0m"
for ((i=0; i<NODES; i++)); do
    PORT=$((BASE_PORT + i))
    echo -e "\033[37m  Node $i: http://localhost:$PORT\033[0m"
    echo -e "\033[90m    Health: http://localhost:$PORT/health\033[0m"
    echo -e "\033[90m    Orders: http://localhost:$PORT/orders\033[0m"
done

echo ""
echo -e "\033[33mLogs are being written to logs-node*.txt files\033[0m"
echo -e "\033[33mPress Ctrl+C to stop all nodes...\033[0m"
echo ""

# Wait for all background processes
wait
