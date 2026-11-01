namespace SseAssertions.TUnit;

/// <summary>The order the ids checked by <c>HasSseEventIds</c> must follow.</summary>
internal enum SseIdOrder
{
    /// <summary>Any id is accepted; only its presence is checked.</summary>
    Any,

    /// <summary>The ids are integers, each greater than the one before.</summary>
    StrictlyIncreasing,

    /// <summary>The ids are integers, each one more than the one before.</summary>
    Sequential,
}
