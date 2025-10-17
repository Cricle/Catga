using Catga.Debugger.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Catga.Debugger.AspNetCore.Endpoints;

/// <summary>
/// API endpoints for cluster monitoring
/// </summary>
public static class ClusterEndpoints
{
    public static RouteGroupBuilder MapClusterEndpoints(this RouteGroupBuilder group)
    {
        // Get all nodes
        group.MapGet("/cluster/nodes", GetAllNodesAsync)
            .WithName("GetAllNodes")
            .WithSummary("Gets all registered nodes in the cluster")
            .Produces<List<NodeInfo>>();

        // Get cluster statistics
        group.MapGet("/cluster/stats", GetClusterStatsAsync)
            .WithName("GetClusterStats")
            .WithSummary("Gets cluster-wide statistics")
            .Produces<ClusterStats>();

        // Get specific node
        group.MapGet("/cluster/nodes/{nodeId}", GetNodeAsync)
            .WithName("GetNode")
            .WithSummary("Gets a specific node by ID")
            .Produces<NodeInfo>()
            .Produces(404);

        // Register/update a node (for distributed deployments)
        group.MapPost("/cluster/nodes", RegisterNodeAsync)
            .WithName("RegisterNode")
            .WithSummary("Registers or updates a node")
            .Produces<NodeInfo>(201);

        return group;
    }

    private static Ok<List<NodeInfo>> GetAllNodesAsync(NodeRegistry registry)
    {
        var nodes = registry.GetAllNodes();
        return TypedResults.Ok(nodes.ToList());
    }

    private static Ok<ClusterStats> GetClusterStatsAsync(NodeRegistry registry)
    {
        var stats = registry.GetClusterStats();
        return TypedResults.Ok(stats);
    }

    private static Results<Ok<NodeInfo>, NotFound> GetNodeAsync(
        string nodeId,
        NodeRegistry registry)
    {
        var node = registry.GetNode(nodeId);
        if (node == null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(node);
    }

    private static Created<NodeInfo> RegisterNodeAsync(
        [FromBody] NodeInfo nodeInfo,
        NodeRegistry registry)
    {
        registry.RegisterOrUpdateNode(nodeInfo);
        return TypedResults.Created($"/debug-api/cluster/nodes/{nodeInfo.NodeId}", nodeInfo);
    }
}

