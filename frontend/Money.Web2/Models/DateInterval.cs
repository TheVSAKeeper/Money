namespace Money.Web2.Models;

public record DateInterval(
    string DisplayName,
    string ChangeName,
    Func<DateTime, DateTime> Start,
    Func<DateTime, DateTime> End,
    Func<DateTime, int, DateTime> Change)
{
    public DateRange Increment(DateRange range)
    {
        return ChangeRange(range, 1);
    }

    public DateRange Decrement(DateRange range)
    {
        return ChangeRange(range, -1);
    }

    private DateRange ChangeRange(DateRange range, int changeAmount)
    {
        if (range.Start == null)
        {
            return range;
        }

        DateTime start = Change.Invoke(range.Start.Value, changeAmount);
        DateTime end = End.Invoke(start);
        return new DateRange(start, end);
    }
}

public class Range<T>
{
    /// <summary>Creates a new instance.</summary>
    public Range()
    {
    }

    /// <summary>Creates a new instance.</summary>
    /// <param name="start">The minimum value.</param>
    /// <param name="end">The maximum value.</param>
    public Range(T start, T end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The minimum value.</summary>
    public T Start { get; set; }

    /// <summary>The maximum value.</summary>
    public T End { get; set; }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return obj is Range<T> range && range.Start != null && range.Start.Equals(Start) && range.End != null && range.End.Equals(End);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}

public class DateRange : Range<DateTime?>, IEquatable<DateRange>
{
    /// <summary>Creates a new instance.</summary>
    public DateRange()
        : base(new DateTime?(), new DateTime?())
    {
    }

    /// <summary>Creates a new instance.</summary>
    /// <param name="start">The earliest date.</param>
    /// <param name="end">The most recent date.</param>
    public DateRange(DateTime? start, DateTime? end)
        : base(start, end)
    {
    }

    public static bool operator ==(DateRange dateRange1, DateRange dateRange2)
    {
        if (dateRange1 == (object)dateRange2)
        {
            return true;
        }

        return (object)dateRange1 != null && (object)dateRange2 != null && dateRange1.Equals(dateRange2);
    }

    public static bool operator !=(DateRange dateRange1, DateRange dateRange2)
    {
        return !(dateRange1 == dateRange2);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return Equals(obj as DateRange);
    }

    public bool Equals(DateRange other)
    {
        if (other != null)
        {
            DateTime? nullable = Start;
            DateTime? start = other.Start;

            if ((nullable.HasValue == start.HasValue ? nullable.HasValue ? nullable.GetValueOrDefault() == start.GetValueOrDefault() ? 1 : 0 : 1 : 0) != 0)
            {
                DateTime? end = End;
                nullable = other.End;

                if (end.HasValue != nullable.HasValue)
                {
                    return false;
                }

                return !end.HasValue || end.GetValueOrDefault() == nullable.GetValueOrDefault();
            }
        }

        return false;
    }
}
