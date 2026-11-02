using System.Collections.Generic;
using System.Globalization;
using SseAssertions;

namespace SseAssertions.TUnit;

/// <summary>
/// Matching engine shared by every <c>HasSseEventIds</c> chain: every event carries an <c>id:</c>, and, when an
/// order is configured, the ids are integers that increase strictly or by one. Evaluates events one at a time,
/// so a streaming read stops at the first violation.
/// </summary>
internal sealed class SseEventIdsMatcher : ISseObserver
{
    private readonly List<string?> _ids = [];
    private long? _previous;

    /// <summary>Gets or sets the order the ids must follow.</summary>
    internal SseIdOrder Order { get; set; }

    /// <summary>Gets or sets the id the first event must carry under <see cref="SseIdOrder.Sequential"/>, or
    /// <see langword="null"/> when any first id is accepted.</summary>
    internal long? StartingAt { get; set; }

    /// <inheritdoc/>
    public string? Until { get; set; }

    /// <summary>Gets the expectation, for the expectation line and the failure message.</summary>
    internal string Expectation => Order switch
    {
        SseIdOrder.StrictlyIncreasing => "every event to carry an integer id, increasing strictly",
        SseIdOrder.Sequential when StartingAt is { } start
            => string.Create(CultureInfo.InvariantCulture, $"every event to carry an integer id, sequential from {start}"),
        SseIdOrder.Sequential => "every event to carry an integer id, sequential",
        _ => "every event to carry an id",
    };

    /// <summary>Evaluates a whole parsed body, stopping where a streaming read would.</summary>
    /// <param name="events">The parsed events.</param>
    /// <returns><see langword="null"/> when the expectation is met; otherwise the failure message.</returns>
    internal string? Evaluate(IReadOnlyList<SseEvent> events) => SseStreamEvaluation.EvaluateParsed(this, events);

    /// <inheritdoc/>
    public SseOutcome? Observe(SseEvent evt)
    {
        _ids.Add(evt.Id);
        var number = _ids.Count;
        if (evt.Id is not { } id)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"event {number} ({evt.EventName}) carries no id"));
        }

        if (Order is SseIdOrder.Any)
        {
            return null;
        }

        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"event {number} has the id \"{id}\", which is not an integer"));
        }

        var violation = Order is SseIdOrder.StrictlyIncreasing
            ? CheckIncreasing(number, value)
            : CheckSequential(number, value);
        _previous = value;
        return violation is null ? null : Fail(violation);
    }

    /// <inheritdoc/>
    public string? Finish()
        => _ids.Count is 0 ? SseFailureMessage.EventIdsNotAsExpected(Expectation, "no event arrived", _ids) : null;

    private string? CheckIncreasing(int number, long value)
        => _previous is { } previous && value <= previous
            ? string.Create(CultureInfo.InvariantCulture, $"event {number} has the id {value}, not greater than the previous id {previous}")
            : null;

    private string? CheckSequential(int number, long value)
    {
        var expected = _previous + 1 ?? StartingAt;
        return expected is { } next && value != next
            ? string.Create(CultureInfo.InvariantCulture, $"event {number} has the id {value} where {next} was expected")
            : null;
    }

    private SseOutcome Fail(string violation)
        => new(SseFailureMessage.EventIdsNotAsExpected(Expectation, violation, _ids));
}
