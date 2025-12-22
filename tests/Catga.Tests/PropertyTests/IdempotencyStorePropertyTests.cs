using Catga.Abstractions;
using Catga.Idempotency;
using Catga.Tests.PropertyTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using NSubstitute;
using System.Text;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// InMemoryIdempotencyStore 属性测试
/// 使用 FsCheck 进行属性测试验证
/// 
/// 注意: FsCheck.Xunit 的 [Property] 特性要求测试类有无参构造函数
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "InMemory")]
public class InMemoryIdempotencyStorePropertyTests
{
    /// <summary>
    /// Property 7: IdempotencyStore Exactly-Once Semantics
    /// 
    /// *For any* message ID, marking as processed then checking SHALL return true,
    /// and concurrent mark operations SHALL result in exactly one successful mark.
    /// 
    /// **Validates: Requirements 3.12, 3.13**
    /// 
    /// Feature: tdd-validation, Property 7: IdempotencyStore Exactly-Once Semantics
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property IdempotencyStore_MarkThenCheck_ReturnsTrue()
    {
        return Prop.ForAll(
            MessageGenerators.MessageIdArbitrary(),
            (messageId) =>
            {
                // Arrange
                var serializer = CreateMockSerializer();
                var store = new MemoryIdempotencyStore(serializer);

                // Act
                store.MarkAsProcessedAsync<string>(messageId, "test-result").GetAwaiter().GetResult();
                var isProcessed = store.HasBeenProcessedAsync(messageId).GetAwaiter().GetResult();

                // Assert - After marking, the message should be marked as processed
                return isProcessed;
            });
    }

    /// <summary>
    /// Property 7 (Alternative): IdempotencyStore Concurrent Marks - Exactly Once
    /// 
    /// *For any* message ID and concurrent mark operations, all operations SHALL complete
    /// successfully and the message SHALL be marked as processed exactly once (idempotent behavior).
    /// 
    /// **Validates: Requirements 3.12, 3.13**
    /// 
    /// Feature: tdd-validation, Property 7: IdempotencyStore Exactly-Once Semantics (Concurrent)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property IdempotencyStore_ConcurrentMarks_AllSucceedIdempotently()
    {
        return Prop.ForAll(
            MessageGenerators.MessageIdArbitrary(),
            Gen.Choose(2, 10).ToArbitrary(),
            (messageId, concurrency) =>
            {
                // Arrange
                var serializer = CreateMockSerializer();
                var store = new MemoryIdempotencyStore(serializer);

                // Act - Concurrent mark operations
                var tasks = Enumerable.Range(0, concurrency)
                    .Select(i => store.MarkAsProcessedAsync(messageId, $"result-{i}"))
                    .ToArray();

                Task.WhenAll(tasks).GetAwaiter().GetResult();

                // Assert - After all concurrent marks, the message should be processed
                var isProcessed = store.HasBeenProcessedAsync(messageId).GetAwaiter().GetResult();

                // The message should be marked as processed (idempotent - all marks succeed)
                return isProcessed;
            });
    }

    /// <summary>
    /// Property 7 (Check Before Mark): IdempotencyStore - Unprocessed Messages Return False
    /// 
    /// *For any* message ID that has not been marked as processed, 
    /// checking SHALL return false.
    /// 
    /// **Validates: Requirements 3.12**
    /// 
    /// Feature: tdd-validation, Property 7: IdempotencyStore Exactly-Once Semantics (Unprocessed)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property IdempotencyStore_UnprocessedMessage_ReturnsFalse()
    {
        return Prop.ForAll(
            MessageGenerators.MessageIdArbitrary(),
            (messageId) =>
            {
                // Arrange
                var serializer = CreateMockSerializer();
                var store = new MemoryIdempotencyStore(serializer);

                // Act - Check without marking
                var isProcessed = store.HasBeenProcessedAsync(messageId).GetAwaiter().GetResult();

                // Assert - Unprocessed message should return false
                return !isProcessed;
            });
    }

    /// <summary>
    /// Property 7 (Multiple Messages): IdempotencyStore - Multiple Messages Are Independent
    /// 
    /// *For any* set of distinct message IDs, marking one message as processed 
    /// SHALL NOT affect the processed status of other messages.
    /// 
    /// **Validates: Requirements 3.12, 3.13**
    /// 
    /// Feature: tdd-validation, Property 7: IdempotencyStore Exactly-Once Semantics (Independence)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property IdempotencyStore_MultipleMessages_AreIndependent()
    {
        return Prop.ForAll(
            Gen.ListOf(5, MessageGenerators.MessageIdArbitrary().Generator)
                .Where(list => list.Distinct().Count() == list.Count()) // Ensure unique IDs
                .ToArbitrary(),
            (messageIds) =>
            {
                // Arrange
                var serializer = CreateMockSerializer();
                var store = new MemoryIdempotencyStore(serializer);
                var idList = messageIds.ToList();

                if (idList.Count < 2) return true; // Skip if not enough unique IDs

                // Act - Mark only the first message
                store.MarkAsProcessedAsync(idList[0], "result").GetAwaiter().GetResult();

                // Assert - First message should be processed, others should not
                var firstProcessed = store.HasBeenProcessedAsync(idList[0]).GetAwaiter().GetResult();
                var othersNotProcessed = idList.Skip(1)
                    .All(id => !store.HasBeenProcessedAsync(id).GetAwaiter().GetResult());

                return firstProcessed && othersNotProcessed;
            });
    }

    /// <summary>
    /// Property 7 (Idempotent Marking): IdempotencyStore - Multiple Marks Are Idempotent
    /// 
    /// *For any* message ID, marking it as processed multiple times sequentially
    /// SHALL always result in the message being marked as processed (idempotent operation).
    /// 
    /// **Validates: Requirements 3.12, 3.13**
    /// 
    /// Feature: tdd-validation, Property 7: IdempotencyStore Exactly-Once Semantics (Idempotent)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property IdempotencyStore_MultipleMarks_AreIdempotent()
    {
        return Prop.ForAll(
            MessageGenerators.MessageIdArbitrary(),
            Gen.Choose(2, 5).ToArbitrary(),
            (messageId, markCount) =>
            {
                // Arrange
                var serializer = CreateMockSerializer();
                var store = new MemoryIdempotencyStore(serializer);

                // Act - Mark multiple times sequentially
                for (int i = 0; i < markCount; i++)
                {
                    store.MarkAsProcessedAsync(messageId, $"result-{i}").GetAwaiter().GetResult();
                }

                // Assert - Message should be processed after all marks
                var isProcessed = store.HasBeenProcessedAsync(messageId).GetAwaiter().GetResult();

                return isProcessed;
            });
    }

    /// <summary>
    /// Creates a mock serializer for testing
    /// </summary>
    private static IMessageSerializer CreateMockSerializer()
    {
        var serializer = Substitute.For<IMessageSerializer>();

        serializer.Serialize(Arg.Any<object>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var obj = callInfo.ArgAt<object>(0);
                return Encoding.UTF8.GetBytes(obj?.ToString() ?? "null");
            });

        serializer.Deserialize(Arg.Any<byte[]>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var bytes = callInfo.ArgAt<byte[]>(0);
                return Encoding.UTF8.GetString(bytes);
            });

        return serializer;
    }
}
