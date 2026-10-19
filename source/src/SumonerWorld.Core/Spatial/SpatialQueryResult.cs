namespace SumonerWorld.Core.Spatial;

/// <summary>Outcome of a spatial query that writes into a caller-owned buffer.</summary>
/// <param name="Count">Number of entities written to the buffer.</param>
/// <param name="Truncated">True if more entities matched than fit in the buffer; the extra ones were skipped.</param>
public readonly record struct SpatialQueryResult(int Count, bool Truncated);
