namespace DoodleNook.Engine.Tests;

/// <summary>
/// The performance measurements run on their own, after the other engine tests, so other tests running in parallel
/// don't inflate the numbers they report.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MeasurementCollection
{
    public const string Name = "Measurements";
}
