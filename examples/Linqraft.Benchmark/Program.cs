using BenchmarkDotNet.Running;

namespace Linqraft.Benchmark;

class Program
{
    static async Task Main(string[] args)
    {
        BenchmarkRunner.Run<SelectBenchmark>();
    }
}
