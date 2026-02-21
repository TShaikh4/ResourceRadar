namespace ResourceRadar.Monitoring.Abstractions;

public interface IProcessControlService
{
    bool TryTerminateProcess(int processId, out string? errorMessage);
}
