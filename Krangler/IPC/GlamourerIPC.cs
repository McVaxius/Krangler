using System;

namespace Krangler.IPC;

/// <summary>
/// Stub — Glamourer IPC replaced by direct memory modification.
/// Kept only for compilation compatibility; will be removed in a future cleanup.
/// </summary>
public class GlamourerIPC : IDisposable
{
    public void RevertAll() { }
    public void Dispose() { }
}
