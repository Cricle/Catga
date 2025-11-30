using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace OrderSystem.Api.Infrastructure.Telemetry
{
    public static class Telemetry
    {
        public static readonly ActivitySource ActivitySource = new("OrderSystem.Api");
        public static readonly Meter Meter = new("OrderSystem.Api");

        public static class Tags
        {
            public const string OrderId = "order.id";
            public const string ItemCount = "item.count";
            public const string Amount = "order.amount";
            public const string PaymentMethod = "payment.method";
        }

        public static class Metrics
        {
            public static readonly Counter<long> OrderCreated = Meter.CreateCounter<long>(
                "orders.created",
                "number",
                "Number of orders created");

            public static readonly Counter<long> OrderCancelled = Meter.CreateCounter<long>(
                "orders.cancelled",
                "number",
                "Number of orders cancelled");

            public static readonly Histogram<double> OrderAmount = Meter.CreateHistogram<double>(
                "order.amount",
                "USD",
                "Distribution of order amounts");
        }
    }

    public static class ActivityExtensions
    {
        public static Activity? StartActivityWithTags(
            this ActivitySource source,
            string name,
            string? orderId = null,
            int? itemCount = null,
            decimal? amount = null,
            string? paymentMethod = null,
            ActivityKind kind = ActivityKind.Internal)
        {
            var activity = source.StartActivity(name, kind);
            if (activity == null) return null;

            if (orderId != null)
                activity.SetTag(Telemetry.Tags.OrderId, orderId);
            if (itemCount.HasValue)
                activity.SetTag(Telemetry.Tags.ItemCount, itemCount.Value);
            if (amount.HasValue)
                activity.SetTag(Telemetry.Tags.Amount, amount.Value);
            if (paymentMethod != null)
                activity.SetTag(Telemetry.Tags.PaymentMethod, paymentMethod);

            return activity;
        }
    }

    public static class HistogramExtensions
    {
        public static IDisposable Measure(this Histogram<double> histogram, params KeyValuePair<string, object?>[] tags)
        {
            var startTime = Stopwatch.GetTimestamp();
            return new DisposableMeasurement(histogram, startTime, tags);
        }

        private class DisposableMeasurement : IDisposable
        {
            private readonly Histogram<double> _histogram;
            private readonly long _startTimestamp;
            private readonly KeyValuePair<string, object?>[] _tags;

            public DisposableMeasurement(Histogram<double> histogram, long startTimestamp, KeyValuePair<string, object?>[] tags)
            {
                _histogram = histogram;
                _startTimestamp = startTimestamp;
                _tags = tags;
            }

            public void Dispose()
            {
                var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
                _histogram.Record(elapsed.TotalMilliseconds, _tags);
            }
        }
    }
}
