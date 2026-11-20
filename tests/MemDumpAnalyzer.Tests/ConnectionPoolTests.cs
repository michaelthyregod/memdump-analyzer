using MemDumpAnalyzer.Core.Analysis;
using MemDumpAnalyzer.Core.Heuristics;
using MemDumpAnalyzer.Core.Models;

namespace MemDumpAnalyzer.Tests;

/// <summary>
/// Threads waiting for a pooled SQL connection. Seen in production: 7,308 of 7,539 threads in
/// DbConnectionPool.TryGetConnection while 97 ran queries — classified as "running" before this rule.
/// </summary>
public class ConnectionPoolTests
{
    // .NET Framework System.Data stack of a thread in SqlConnection.Open waiting for a pooled connection
    private static readonly string[] PoolWaitTop =
    [
        "System.Data.Common.SafeNativeMethods.WaitForMultipleObjectsEx(UInt32, IntPtr, Boolean, UInt32, Boolean)  [System.Data]",
        "DomainNeutralILStubClass.IL_STUB_PInvoke(UInt32, IntPtr, Boolean, UInt32, Boolean)  [System.Data]",
        "System.Data.ProviderBase.DbConnectionPool.TryGetConnection(System.Data.Common.DbConnection, UInt32, Boolean, Boolean, System.Data.Common.DbConnectionOptions, System.Data.ProviderBase.DbConnectionInternal ByRef)  [System.Data]",
        "System.Data.ProviderBase.DbConnectionFactory.TryGetConnection(System.Data.Common.DbConnection, System.Threading.Tasks.TaskCompletionSource`1<System.Data.ProviderBase.DbConnectionInternal>, System.Data.Common.DbConnectionOptions, System.Data.ProviderBase.DbConnectionInternal, System.Data.ProviderBase.DbConnectionInternal ByRef)  [System.Data]",
        "System.Data.SqlClient.SqlConnection.TryOpen(System.Threading.Tasks.TaskCompletionSource`1<System.Data.ProviderBase.DbConnectionInternal>)  [System.Data]",
        "System.Data.SqlClient.SqlConnection.Open()  [System.Data]",
    ];

    // A data layer every query goes through, then the code path that matters
    private static string[] PoolWait(string codePath) =>
    [
        .. PoolWaitTop,
        "Acme.Data.Sql.Command..ctor(System.Data.IDbCommand)  [Acme.Data]",
        "Acme.Data.Sql.Api.CreateReader(System.String, System.Object[])  [Acme.Data]",
        codePath,
        "Acme.Web.Pages.Index.Render()  [Acme.Web]",
    ];

    private static readonly string[] SqlRead =
    [
        "<Module>.SNIReadSyncOverAsync(SNI_ConnWrapper*, SNI_Packet**, Int32)  [System.Data]",
        "SNINativeMethodWrapper.SNIReadSyncOverAsync(System.Runtime.InteropServices.SafeHandle, IntPtr ByRef, Int32)  [System.Data]",
        "System.Data.SqlClient.TdsParserStateObject.ReadSniSyncOverAsync()  [System.Data]",
        "System.Data.SqlClient.SqlDataReader.Read()  [System.Data]",
        "Acme.Data.Sql.Command.ExecuteReader()  [Acme.Data]",
        "Acme.Catalog.ChildrenQuery.Run()  [Acme.Catalog]",
    ];

    private static ThreadInfo Thread(uint id, string[] frames)
    {
        var (kind, description) = ThreadAnalyzer.ClassifyWait(frames);
        return new ThreadInfo(id, (int)id, "Alive", null, frames, false, false,
            kind is WaitKind.None or WaitKind.IdleWorker ? null : $"Waiting on {description}", IsAlive: true, WaitKind: kind);
    }

    private static IReadOnlyList<Finding> Evaluate(IReadOnlyList<ThreadInfo> threads, ThreadPoolStats? pool = null) =>
        FindingEngine.Evaluate(threads, [], [], new GcStats(0, 0, 0, 0, 0, 0, 0, []), [], [], pool, heapWalkComplete: true,
            TestContext.Current.CancellationToken).Findings;

    [Fact]
    public void ClassifyWait_PoolWait_IsConnectionPool_NotRunningOrQuery()
    {
        var (kind, description) = ThreadAnalyzer.ClassifyWait(PoolWait("Acme.Catalog.ChildrenQuery.Run()  [Acme.Catalog]"));

        Assert.Equal(WaitKind.ConnectionPool, kind);
        Assert.Contains("connection pool", description);
        Assert.Equal(WaitKind.Dependency, ThreadAnalyzer.ClassifyWait(SqlRead).Kind);   // a running query stays a SQL wait
    }

    [Fact]
    public void CallerOf_SkipsTheDataLayerAndCleansObfuscatedNames()
    {
        // "​‌‍" are invisible format characters, as obfuscators use for method names
        var frames = PoolWait("Acme.Nexus.Commands.GetChildrenCommand.​‌‍()  [Acme.Nexus]");

        Assert.Equal("Acme.Nexus.Commands.GetChildrenCommand (via Acme.Data.Sql.Command..ctor)", FindingEngine.CallerOf(frames));
    }

    [Theory]
    [InlineData(9, null)]
    [InlineData(10, Severity.Warning)]
    [InlineData(50, Severity.Critical)]
    public void Finding_SeverityByWaitingThreads(int waiting, Severity? expected)
    {
        var threads = Enumerable.Range(1, waiting).Select(i => Thread((uint)i, PoolWait("Acme.Catalog.ChildrenQuery.Run()  [Acme.Catalog]"))).ToList();

        var finding = Evaluate(threads).SingleOrDefault(f => f.Id == "SqlConnectionPoolExhausted");

        Assert.Equal(expected, finding?.Severity);
    }

    [Fact]
    public void Finding_CountsQueriesAndGroupsByCodePath_AndNotesAHighThreadPoolMinimum()
    {
        var threads = Enumerable.Range(1, 40).Select(i => Thread((uint)i, PoolWait("Acme.Catalog.ChildrenQuery.Run()  [Acme.Catalog]")))
            .Concat(Enumerable.Range(100, 12).Select(i => Thread((uint)i, PoolWait("Acme.Search.Index.Rebuild()  [Acme.Search]"))))
            .Concat(Enumerable.Range(200, 7).Select(i => Thread((uint)i, SqlRead)))
            .ToList();
        var pool = new ThreadPoolStats(11, MinThreads: 7450, MaxThreads: 32767, ActiveWorkerThreads: 59, IdleWorkerThreads: 3, RetiredWorkerThreads: 0);

        var f = Assert.Single(Evaluate(threads, pool), f => f.Id == "SqlConnectionPoolExhausted");

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.StartsWith("52 threads wait for a SQL connection from the pool — 7 are running SQL", f.Summary);
        Assert.Contains("thread pool minimum is 7", f.Evidence);
        Assert.Contains(f.TechnicalDetails!, l => l.Contains("40") && l.Contains("Acme.Catalog.ChildrenQuery.Run"));
        Assert.Contains(f.TechnicalDetails!, l => l.Contains("12") && l.Contains("Acme.Search.Index.Rebuild"));
        Assert.Contains(f.TechnicalDetails!, l => l.Contains("RUNNING SQL  (7 threads"));
    }

    [Fact]
    public void HighThreadCount_BreakdownNamesThePoolWait()
    {
        var threads = Enumerable.Range(1, 150).Select(i => Thread((uint)i, PoolWait("Acme.Catalog.ChildrenQuery.Run()  [Acme.Catalog]"))).ToList();

        var f = Assert.Single(Evaluate(threads), f => f.Id == "HighThreadCount");

        Assert.Contains("150 waiting for a SQL connection from the pool", f.Evidence);
    }
}
