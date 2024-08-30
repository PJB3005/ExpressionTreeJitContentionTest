using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using ExpressionTreeJitContentionTest;

var sw = Stopwatch.StartNew();

var results = new ConcurrentBag<SerializeDelegateSignature>();

ThreadPool.SetMinThreads(48, 0);

var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 48
};

int done = 0;

Parallel.For(0, 1_000_000, options, i =>
{
    var expr = new ExpressionBuilder(i).CreateExpression();

    expr.Compile();

    var completed = Interlocked.Increment(ref done);
    if (completed % 50_000 == 0)
    {
        Console.WriteLine($"Done with {completed} at {sw.Elapsed}");
    }
});

Console.WriteLine($"Took {sw.Elapsed}");

