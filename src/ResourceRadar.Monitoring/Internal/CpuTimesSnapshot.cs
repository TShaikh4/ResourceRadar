namespace ResourceRadar.Monitoring.Internal;

internal readonly record struct CpuTimesSnapshot(ulong TotalTicks, ulong IdleTicks)
{
    public static double CalculateUsagePercent(in CpuTimesSnapshot previous, in CpuTimesSnapshot current)
    {
        var deltaTotal = current.TotalTicks >= previous.TotalTicks
            ? current.TotalTicks - previous.TotalTicks
            : 0;

        var deltaIdle = current.IdleTicks >= previous.IdleTicks
            ? current.IdleTicks - previous.IdleTicks
            : 0;

        if (deltaTotal == 0)
        {
            return 0;
        }

        var busy = deltaTotal > deltaIdle ? deltaTotal - deltaIdle : 0;
        return Math.Clamp((double)busy / deltaTotal * 100.0, 0, 100);
    }
}
