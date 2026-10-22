using System;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class RateMeterTests
    {
        [Fact]
        public void The_rate_is_bytes_over_the_last_three_seconds()
        {
            var clock = new TestClock();
            var meter = new RateMeter(clock);

            meter.Sample(0);
            Assert.Equal(0, meter.BytesPerSecond);

            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Sample(7000000);
            Assert.Equal(7000000, meter.BytesPerSecond, 0);

            clock.Advance(TimeSpan.FromSeconds(5));      // alte Proben fallen aus dem Fenster
            meter.Sample(8000000);
            clock.Advance(TimeSpan.FromSeconds(1));
            meter.Sample(9000000);
            Assert.Equal(1000000, meter.BytesPerSecond, 0);
        }
    }
}
