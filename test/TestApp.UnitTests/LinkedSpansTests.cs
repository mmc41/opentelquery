using System.Diagnostics;
using TestApp.Endpoints;

namespace TestApp.UnitTests;

public class LinkedSpansTests
{
    [Fact]
    public void EmitLinkedSpans_UnderAmbientServerSpan_SecondIsNewRootTrace()
    {
        const string sourceName = "TestApp.LinkedSpansTests";
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var ambientServerSource = new ActivitySource("TestApp.LinkedSpansTests.Ambient");
        using var ambientListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ambientServerSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(ambientListener);
        using var serverSpan = ambientServerSource.StartActivity("server.request");
        Assert.NotNull(serverSpan);

        var result = TraceEndpoints.EmitLinkedSpans(source);

        Assert.NotEqual(default, result.First.TraceId);
        Assert.NotEqual(default, result.Second.TraceId);

        Assert.Equal(serverSpan!.TraceId, result.First.TraceId);

        Assert.NotEqual(serverSpan.TraceId, result.Second.TraceId);
        Assert.NotEqual(result.First.TraceId, result.Second.TraceId);

        var link = Assert.Single(result.SecondLinks);
        Assert.Equal(result.First.TraceId, link.Context.TraceId);
        Assert.Equal(result.First.SpanId, link.Context.SpanId);
    }

    [Fact]
    public void EmitLinkedSpans_NoAmbientActivity_StillProducesDistinctTraces()
    {
        const string sourceName = "TestApp.LinkedSpansTests.NoAmbient";
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        Activity.Current = null;

        var result = TraceEndpoints.EmitLinkedSpans(source);

        Assert.NotEqual(default, result.First.TraceId);
        Assert.NotEqual(default, result.Second.TraceId);
        Assert.NotEqual(result.First.TraceId, result.Second.TraceId);
    }
}
