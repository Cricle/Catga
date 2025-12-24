namespace OrderSystem.Configuration;

public record NodeInfo(string NodeId, bool IsCluster, string Transport, string Persistence);
