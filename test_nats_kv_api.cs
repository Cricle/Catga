// 临时文件：测试 NATS KV Store API
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

// 测试编译以确定正确的 API
public class NatsKvApiTest
{
    public async Task TestApi()
    {
        var connection = new NatsConnection();
        var jsContext = new NatsJSContext(connection);
        
        // 尝试创建 KV Store
        var kvConfig = new NatsKVConfig("test-bucket");
        
        // 方法 1: 通过 JSContext
        var kv = await jsContext.CreateKeyValueAsync(kvConfig);
        
        // 方法 2: 获取已存在的 KV
        // var kv = await jsContext.GetKeyValueAsync("test-bucket");
        
        // Put 操作
        await kv.PutAsync("key1", "value1");
        
        // Get 操作
        var entry = await kv.GetEntryAsync<string>("key1");
        
        // Watch 操作
        await foreach (var watchEntry in kv.WatchAsync<string>())
        {
            // 处理变更
        }
        
        // Keys 操作
        await foreach (var key in kv.GetKeysAsync())
        {
            // 遍历键
        }
    }
}

