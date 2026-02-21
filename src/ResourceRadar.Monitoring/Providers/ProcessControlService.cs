using System.Diagnostics;
using ResourceRadar.Monitoring.Abstractions;

namespace ResourceRadar.Monitoring.Providers;

public sealed class ProcessControlService : IProcessControlService
{
    public bool TryTerminateProcess(int processId, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: false);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
